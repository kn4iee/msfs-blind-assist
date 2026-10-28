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
