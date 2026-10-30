using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The PFD monitor: the captain's EFIS drawn every tick (FMA call-outs), the first officer's every other tick;
// each side's latest draw kept for its PFD status display.
public class A350PfdMonitorTests
{
    private static A350DrawCapture Fixture(string name) =>
        A350Fixtures.Capture(name + ".json");

    /// <summary>These are the FMA's tests: the aligning fixture also flags ATT, which A350PfdFlagTests say.</summary>
    private static bool FlagsMuted(string key) => key == A350PfdFlagAnnouncer.MuteKey;

    /// <summary>A monitor's draw that answers each display it asks for with <paramref name="one"/>.</summary>
    internal static Func<(string Gauge, int Index)[], Task<IReadOnlyList<A350DrawCapture?>?>> Each(Func<string, int, A350DrawCapture?> one) =>
        gauges => Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(g => one(g.Gauge, g.Index)).ToList());

    [Fact]
    public async Task The_fma_speaks_when_the_flight_directors_come_on_and_each_side_is_kept()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var fo = Fixture("efis1_fd_stand");
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g != "EFIS" ? null : side == 0 ? cpt.Dequeue() : fo), FlagsMuted, said.Add);
        for (int i = 0; i < 4; i++) await m.TickAsync();
        Assert.Equal(new[] { "FMA engagement: 1 FD 2, white" }, said);
        Assert.Same(fo, m.Latest("EFIS", 1));
        Assert.NotNull(m.Latest("EFIS", 0));
    }

    // Review 2026-10-29 (engine M2): a tick already drawing when the aircraft was switched spoke its FMA change after the
    // monitor was disposed. A tick that ends after Dispose says nothing.
    [Fact]
    public async Task A_tick_ending_after_dispose_says_nothing()
    {
        // An FMA change is taken on two agreeing reads: the FDs come on in the last live read and are confirmed by the held one.
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand") });
        var pending = new TaskCompletionSource<IReadOnlyList<A350DrawCapture?>?>();
        bool hold = false;
        var said = new List<string>();
        var m = new A350PfdMonitor(gauges => hold ? pending.Task
            : Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(g => g.Gauge == "EFIS" && g.Index == 0 ? cpt.Dequeue() : null).ToList()),
            FlagsMuted, said.Add);
        await m.TickAsync();
        await m.TickAsync();   // the aligning FMA, a baseline
        await m.TickAsync();   // the FDs first seen
        Assert.Empty(said);
        hold = true;
        var tick = m.TickAsync();
        m.Dispose();
        pending.SetResult(new A350DrawCapture?[] { Fixture("efis0_fd_stand"), null, null });   // the FDs come on
        await tick;
        Assert.Empty(said);
    }

    [Fact]
    public async Task A_muted_column_says_nothing()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g == "EFIS" && side == 0 ? cpt.Dequeue() : null),
            key => FlagsMuted(key) || key == A350FmaAnnouncer.KeyFor("engagement"), said.Add);
        for (int i = 0; i < 4; i++) await m.TickAsync();
        Assert.Empty(said);
    }

    // Each draw request copies the A350's memory. Live 2026-10-28, with the memory at 495 MB, one copy took 650 ms,
    // and a tick that asked for its displays one by one made three or four copies. A tick asks for them all at once:
    // one copy, and the displays it reads are from one moment.
    [Fact]
    public async Task A_tick_asks_for_all_its_displays_at_once()
    {
        var asked = new List<(string Gauge, int Index)[]>();
        var m = new A350PfdMonitor(gauges =>
        {
            asked.Add(gauges);
            return Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(_ => (A350DrawCapture?)null).ToList());
        }, _ => false, _ => { });
        await m.TickAsync();
        await m.TickAsync();
        Assert.Equal(2, asked.Count);
        Assert.Equal(new[] { ("EFIS", 0), ("MFD", 0), ("RMP", 0), ("EFIS", 1) }, asked[0]);
        Assert.Equal(new[] { ("EFIS", 0), ("SFD", 0), ("FCU", 0) }, asked[1]);
    }

    // Review 2026-10-29 (engine I6): the E/WD monitor took a full memory copy of its own every cycle (about 650 ms at
    // 495 MB), beside the display monitor's. Fed by the display monitor, the CTR is drawn first on the tick's own copy (its
    // warnings matter most, and a request retries only one trapped draw) and handed to the E/WD.
    [Fact]
    public async Task Feeding_the_ecam_a_tick_draws_the_ctr_first_on_its_copy_and_hands_it_over()
    {
        var asked = new List<(string Gauge, int Index)[]>();
        var ctr = Fixture("ctr_powerup_fws_fault");
        var handed = new List<A350DrawCapture?>();
        var m = new A350PfdMonitor(gauges =>
        {
            asked.Add(gauges);
            return Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(g => g.Gauge == "CTR" ? ctr : null).ToList());
        }, _ => false, _ => { }) { CtrDrawn = handed.Add };
        await m.TickAsync();
        Assert.Equal(new[] { ("CTR", 0), ("EFIS", 0), ("MFD", 0), ("RMP", 0), ("EFIS", 1) }, asked[0]);
        Assert.Same(ctr, Assert.Single(handed));
    }

    // The FMA still reads the captain's EFIS wherever it sits in the tick.
    [Fact]
    public async Task Feeding_the_ecam_the_fma_still_reads_the_captains_efis()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g == "EFIS" && side == 0 ? cpt.Dequeue() : null), FlagsMuted, said.Add) { CtrDrawn = _ => { } };
        for (int i = 0; i < 4; i++) await m.TickAsync();
        Assert.Equal(new[] { "FMA engagement: 1 FD 2, white" }, said);
    }

    // Review 2026-10-29 (speech 1): after a flight load each FMA column flight B showed differently from flight A was spoken
    // ("FMA vertical clear", "FMA engagement: 1 FD 2, white"). After Rebaseline the new flight's FMA is followed silently
    // for SettleAfterLoad from its first draw.
    [Fact]
    public async Task After_a_flight_load_the_new_flights_fma_is_a_silent_baseline()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"),
            Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") }.Concat(Enumerable.Repeat(Fixture("efis0_aligning"), A350FmaAnnouncer.ClearAfterReads)));
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g == "EFIS" && side == 0 ? cpt.Dequeue() : null), FlagsMuted, said.Add, () => now);
        await m.TickAsync();
        await m.TickAsync();
        m.Rebaseline();
        Assert.Null(m.Latest("EFIS", 0));                 // flight A's PFD is not this flight's
        await m.TickAsync();
        await m.TickAsync();                      // flight B's FDs, on as it loads
        Assert.Empty(said);
        now += A350EcamMonitor.SettleAfterLoad;
        for (int read = 0; read < A350FmaAnnouncer.ClearAfterReads; read++)
            await m.TickAsync();                  // the FDs going off afterwards is news
        Assert.Contains("FMA engagement clear", said);
    }

    // Review 2026-10-29 (readers I7): the latest capture was kept forever and read as current, so after the reader stopped
    // (the A350 not found after a flight load, every draw failing) the status displays, Alt+P and the MFD window read the
    // last flight's displays as live. A capture older than MaxAge is no capture.
    [Fact]
    public async Task A_capture_older_than_its_age_is_no_capture()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var cpt = Fixture("efis0_fd_stand");
        bool draws = true;
        var m = new A350PfdMonitor(Each((g, side) => draws && g == "EFIS" && side == 0 ? cpt : null), _ => false, _ => { }, () => now);
        await m.TickAsync();
        draws = false;
        now += A350PfdMonitor.MaxAge;
        await m.TickAsync();
        Assert.Same(cpt, m.Latest("EFIS", 0));
        now += TimeSpan.FromSeconds(1);
        Assert.Null(m.Latest("EFIS", 0));
    }

    // The reader not ready (or no A350 in the sim) answers the whole tick with nothing: nothing is kept or said.
    [Fact]
    public async Task A_tick_the_reader_cannot_draw_keeps_nothing()
    {
        var said = new List<string>();
        var m = new A350PfdMonitor(_ => Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(null), _ => false, said.Add);
        await m.TickAsync();
        Assert.Null(m.Latest("EFIS", 0));
        Assert.Empty(said);
    }
    /// <summary>The gauges each of <paramref name="ticks"/> ticks half a second apart asks for, every draw good and blank;
    /// <paramref name="between"/> runs before each tick (a status display reading a display).</summary>
    private static async Task<(A350PfdMonitor Monitor, List<(string Gauge, int Index)[]> Asked)> Schedule(int ticks, Action<A350PfdMonitor, int>? between = null)
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var asked = new List<(string Gauge, int Index)[]>();
        var m = new A350PfdMonitor(gauges =>
        {
            asked.Add(gauges);
            return Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(g => (A350DrawCapture?)new A350DrawCapture(g.Gauge,
                Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1)).ToList());
        }, _ => false, _ => { }, () => now) { CtrDrawn = _ => { } };
        for (int t = 0; t < ticks; t++)
        {
            between?.Invoke(m, t);
            await m.TickAsync();
            now += TimeSpan.FromMilliseconds(500);
        }
        return (m, asked);
    }

    // Perf review 2026-10-30 and the owner's question: what the background speaks from is drawn often (the CTR and the
    // captain's EFIS each tick; the MFD's FMS messages every other tick, where every fourth made a message wait up to eight
    // ticks), RMP 1 and the SFD in turn (X and Alt+I read them at once); a display only a status display or window reads is
    // drawn now and then while none reads it, often enough that its latest draw is never too old to show.
    [Fact]
    public async Task The_background_displays_are_drawn_often_and_the_quiet_ones_kept_but_seldom()
    {
        var (m, asked) = await Schedule(40);   // 20 s
        int Count((string, int) g) => asked.Count(t => t.Contains(g));
        Assert.Equal(40, Count(("CTR", 0)));
        Assert.Equal(40, Count(("EFIS", 0)));
        Assert.Equal(20, Count(("MFD", 0)));
        Assert.True(Count(("RMP", 0)) >= 15 && Count(("SFD", 0)) >= 15, $"RMP 1 {Count(("RMP", 0))}, SFD {Count(("SFD", 0))}");
        foreach (var q in A350PfdMonitor.Quiet)
        {
            Assert.InRange(Count(q), 1, 2);
            Assert.NotNull(m.Latest(q.Gauge, q.Index));
        }
    }

    [Fact]
    public async Task A_quiet_display_read_is_drawn_every_tick_while_it_is_read()
    {
        var (_, asked) = await Schedule(40, (m, t) => { if (t >= 10 && t < 20) m.Latest("FCU", 0); });
        Assert.Equal(20, Enumerable.Range(10, 20).Count(t => asked[t].Contains(("FCU", 0))));
    }
    // Final review 2026-10-30: a quiet display was marked drawn when a tick asked for it while the reader could not draw
    // (preparing, or no A350 while a flight loads), so once it could, the quiet displays waited 15 s for a first draw.
    [Fact]
    public async Task The_quiet_displays_are_drawn_as_soon_as_the_reader_can_draw()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        bool ready = false;
        var asked = new List<(string Gauge, int Index)[]>();
        var m = new A350PfdMonitor(gauges =>
        {
            if (!ready) return Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(null);
            asked.Add(gauges);
            return Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(g => (A350DrawCapture?)new A350DrawCapture(g.Gauge,
                Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1)).ToList());
        }, _ => false, _ => { }, () => now);
        for (int t = 0; t < 8; t++) { await m.TickAsync(); now += TimeSpan.FromMilliseconds(500); }
        ready = true;
        for (int t = 0; t < A350PfdMonitor.Quiet.Count; t++) { await m.TickAsync(); now += TimeSpan.FromMilliseconds(500); }
        foreach (var q in A350PfdMonitor.Quiet) Assert.Contains(asked, g => g.Contains(q));
    }
}
