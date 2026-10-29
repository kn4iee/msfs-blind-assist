using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The PFD monitor: the captain's EFIS drawn every tick (FMA call-outs), the first officer's every other tick;
// each side's latest draw kept for its PFD status display.
public class A350PfdMonitorTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    /// <summary>A monitor's draw that answers each display it asks for with <paramref name="one"/>.</summary>
    internal static Func<(string Gauge, int Index)[], Task<IReadOnlyList<A350DrawCapture?>?>> Each(Func<string, int, A350DrawCapture?> one) =>
        gauges => Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(gauges.Select(g => one(g.Gauge, g.Index)).ToList());

    [Fact]
    public async Task The_fma_speaks_when_the_flight_directors_come_on_and_each_side_is_kept()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var fo = Fixture("efis1_fd_stand");
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g != "EFIS" ? null : side == 0 ? cpt.Dequeue() : fo), _ => false, said.Add);
        for (int i = 0; i < 4; i++) await m.TickAsync();
        Assert.Equal(new[] { "FMA engagement: 1 FD 2, white" }, said);
        Assert.Same(fo, m.Latest(1));
        Assert.NotNull(m.Latest(0));
    }

    [Fact]
    public async Task A_muted_column_says_nothing()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g == "EFIS" && side == 0 ? cpt.Dequeue() : null),
            key => key == A350FmaAnnouncer.KeyFor("engagement"), said.Add);
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
        Assert.Equal(new[] { ("EFIS", 0), ("MFD", 0), ("FCU", 0) }, asked[0]);
        Assert.Equal(new[] { ("EFIS", 0), ("EFIS", 1), ("RMP", 0), ("RMP", 1) }, asked[1]);
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
        Assert.Equal(new[] { ("CTR", 0), ("EFIS", 0), ("MFD", 0), ("FCU", 0) }, asked[0]);
        Assert.Same(ctr, Assert.Single(handed));
    }

    // The FMA still reads the captain's EFIS wherever it sits in the tick.
    [Fact]
    public async Task Feeding_the_ecam_the_fma_still_reads_the_captains_efis()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g == "EFIS" && side == 0 ? cpt.Dequeue() : null), _ => false, said.Add) { CtrDrawn = _ => { } };
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
            Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand"), Fixture("efis0_aligning"), Fixture("efis0_aligning") });
        var said = new List<string>();
        var m = new A350PfdMonitor(Each((g, side) => g == "EFIS" && side == 0 ? cpt.Dequeue() : null), _ => false, said.Add, () => now);
        await m.TickAsync();
        await m.TickAsync();
        m.Rebaseline();
        Assert.Null(m.Latest(0));                 // flight A's PFD is not this flight's
        await m.TickAsync();
        await m.TickAsync();                      // flight B's FDs, on as it loads
        Assert.Empty(said);
        now += A350EcamMonitor.SettleAfterLoad;
        await m.TickAsync();
        await m.TickAsync();                      // the FDs going off afterwards is news
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
        Assert.Same(cpt, m.Latest(0));
        now += TimeSpan.FromSeconds(1);
        Assert.Null(m.Latest(0));
    }

    // The reader not ready (or no A350 in the sim) answers the whole tick with nothing: nothing is kept or said.
    [Fact]
    public async Task A_tick_the_reader_cannot_draw_keeps_nothing()
    {
        var said = new List<string>();
        var m = new A350PfdMonitor(_ => Task.FromResult<IReadOnlyList<A350DrawCapture?>?>(null), _ => false, said.Add);
        await m.TickAsync();
        Assert.Null(m.Latest(0));
        Assert.Empty(said);
    }
}
