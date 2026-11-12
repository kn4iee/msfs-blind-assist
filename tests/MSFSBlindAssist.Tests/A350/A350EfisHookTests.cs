using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The display monitor hands the captain's EFIS draw of each tick on to the aircraft definition (CaptainEfisDrawn), so the
// ND can be read in the background from it in a file of its own: once a tick, the good draw only, behind its own guard
// like every other reader, and told whether the call-outs are still settling after a flight load.
public class A350EfisHookTests
{
    private static readonly DateTime T0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    private static A350DrawCapture Fixture(string name) => A350Fixtures.Capture(name + ".json");

    /// <summary>These are not the PFD flags' tests: the aligning fixture also flags ATT.</summary>
    private static bool FlagsMuted(string key) => key == A350PfdFlagAnnouncer.MuteKey;

    // The captain's EFIS is not the first draw of a tick when the E/WD is fed (the CTR is), and the first officer's EFIS is
    // drawn beside it: the hook is handed the captain's, once, with the time of the tick.
    [Fact]
    public async Task Each_good_captain_efis_draw_is_handed_on_once()
    {
        var now = T0;
        var ctr = Fixture("ctr_powerup_fws_fault");
        var captain = Fixture("efis0_fd_stand");
        var firstOfficer = Fixture("efis1_fd_stand");
        var handed = new List<(A350DrawCapture Capture, DateTime At, bool Settling)>();
        var m = new A350PfdMonitor(A350PfdMonitorTests.Each((gauge, index) => gauge switch
        {
            "CTR" => ctr,
            "EFIS" => index == 0 ? captain : firstOfficer,
            _ => null,
        }), FlagsMuted, _ => { }, () => now)
        {
            CtrDrawn = _ => { },
            CaptainEfisDrawn = (capture, at, settling) => handed.Add((capture, at, settling)),
        };
        for (int tick = 1; tick <= 3; tick++)
        {
            await m.TickAsync();
            Assert.Equal(tick, handed.Count);   // once a tick, the first one too, which drew the first officer's EFIS as well
            now += Tick;
        }
        Assert.All(handed, h => Assert.Same(captain, h.Capture));
        Assert.Equal(new[] { T0, T0 + Tick, T0 + Tick + Tick }, handed.Select(h => h.At));
        Assert.All(handed, h => Assert.False(h.Settling));
    }

    // A draw that trapped (a copy read across a frame), one the reader returned none for, and a tick it could not draw at
    // all are no draw: the hook is handed nothing, and a good one after them is handed on, so the hook is wired.
    [Fact]
    public async Task A_trapped_efis_draw_is_not_handed_on()
    {
        var good = Fixture("efis0_fd_stand");
        var trapped = good with { Error = "trap: out of bounds memory access" };
        A350DrawCapture? captain = trapped;
        bool canDraw = true;
        var handed = new List<A350DrawCapture>();
        var m = new A350PfdMonitor(gauges => Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(canDraw
                ? gauges.Select(g => g == ("EFIS", 0) ? captain : null).ToList() : null),
            FlagsMuted, _ => { }) { CaptainEfisDrawn = (capture, _, _) => handed.Add(capture) };
        await m.TickAsync();
        captain = null;
        await m.TickAsync();
        canDraw = false;
        captain = good;
        await m.TickAsync();
        Assert.Empty(handed);
        canDraw = true;
        await m.TickAsync();
        Assert.Same(good, Assert.Single(handed));
    }

    // An aircraft switch disposes the monitor, perhaps in the middle of a tick's draw: the draw that ends after it is not handed on, as nothing
    // is said from it by the readers the monitor speaks for itself (final review, Minor 4).
    [Fact]
    public async Task A_draw_that_ends_after_dispose_is_not_handed_on()
    {
        var captain = Fixture("efis0_fd_stand");
        var drawn = new TaskCompletionSource();
        drawn.SetResult();   // the first tick draws at once
        var handed = new List<A350DrawCapture>();
        var m = new A350PfdMonitor(async gauges =>
            {
                await drawn.Task;
                return gauges.Select(g => g == ("EFIS", 0) ? captain : null).ToList();
            },
            FlagsMuted, _ => { }) { CaptainEfisDrawn = (capture, _, _) => handed.Add(capture) };
        await m.TickAsync();
        Assert.Same(captain, Assert.Single(handed));   // the hook is wired
        drawn = new TaskCompletionSource();
        var tick = m.TickAsync();   // the next tick, still drawing
        m.Dispose();                // the aircraft switch
        drawn.SetResult();
        await tick;
        Assert.Single(handed);
    }

    // Like every reader of a tick, the hook costs that read only when it throws: it is handed the next tick's draw as well,
    // and the E/WD's hand-over and the FMA read before it and the message line's read after it are untouched.
    [Fact]
    public async Task A_hook_that_throws_costs_only_its_own_read()
    {
        var aligning = Fixture("efis0_aligning");
        var fdStand = Fixture("efis0_fd_stand");
        var posted = Fixture("mfd_cl_approach");
        var quiet = posted with { Texts = posted.Texts.Select(t => t.Text == "NAV ACCURACY UPGRADED" ? t with { Text = " " } : t).ToList() };
        var mfd = new Queue<A350DrawCapture>(new[] { quiet, quiet, posted, posted });
        int efisDraws = 0, hooked = 0, ctrs = 0;
        var said = new List<string>();
        var m = new A350PfdMonitor(A350PfdMonitorTests.Each((gauge, index) => gauge switch
        {
            "EFIS" when index == 0 => efisDraws++ < 2 ? aligning : fdStand,   // the FDs come on after two aligning reads
            "MFD" => mfd.Count > 0 ? mfd.Dequeue() : null,
            _ => null,
        }), FlagsMuted, said.Add)
        {
            CtrDrawn = _ => ctrs++,
            CaptainEfisDrawn = (_, _, _) => { hooked++; throw new InvalidOperationException("a torn ND"); },
        };
        int ticks = 0;
        for (; mfd.Count > 0 && ticks < 100; ticks++)
            await m.TickAsync();
        Assert.Empty(mfd);
        Assert.Equal(ticks, hooked);
        Assert.Equal(ticks, ctrs);
        Assert.Equal(new[] { "FMA engagement: 1 FD 2, white", "NAV ACCURACY UPGRADED" }, said);
    }

    // The FMA read starts a loaded flight's settle window, so a draw handed on after it already says so: the flight's first
    // draw is settling, the window is open for SettleAfterLoad from it, and no load seen means nothing settles.
    [Fact]
    public async Task The_first_draw_after_a_flight_load_is_handed_on_as_settling()
    {
        var now = T0;
        var captain = Fixture("efis0_fd_stand");
        var settling = new List<bool>();
        var m = new A350PfdMonitor(A350PfdMonitorTests.Each((gauge, index) => gauge == "EFIS" && index == 0 ? captain : null),
            FlagsMuted, _ => { }, () => now) { CaptainEfisDrawn = (_, _, s) => settling.Add(s) };
        await m.TickAsync();   // no load seen yet
        m.Rebaseline();
        await m.TickAsync();   // the new flight's first draw
        now += A350EcamMonitor.SettleAfterLoad - Tick;
        await m.TickAsync();   // the last tick inside the window
        now += Tick;
        await m.TickAsync();   // the window is over
        Assert.Equal(new[] { false, true, true, false }, settling);
    }
}
