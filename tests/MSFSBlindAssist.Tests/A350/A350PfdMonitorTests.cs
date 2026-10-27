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

    [Fact]
    public async Task The_fma_speaks_when_the_flight_directors_come_on_and_each_side_is_kept()
    {
        var cpt = new Queue<A350DrawCapture?>(new[] { Fixture("efis0_aligning"), Fixture("efis0_aligning"), Fixture("efis0_fd_stand"), Fixture("efis0_fd_stand") });
        var fo = Fixture("efis1_fd_stand");
        var said = new List<string>();
        var m = new A350PfdMonitor((g, side) => Task.FromResult(g != "EFIS" ? null : side == 0 ? cpt.Dequeue() : fo), _ => false, said.Add);
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
        var m = new A350PfdMonitor((g, side) => Task.FromResult(g == "EFIS" && side == 0 ? cpt.Dequeue() : null),
            key => key == A350FmaAnnouncer.KeyFor("engagement"), said.Add);
        for (int i = 0; i < 4; i++) await m.TickAsync();
        Assert.Empty(said);
    }
}
