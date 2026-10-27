using System.Text.Json;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The PFDs on demand, as the A380's: a Displays section in the Panels list with a Captain PFD and a First Officer
// PFD panel, each status display reading that side's PFD (Ctrl+3, F5, the 1 s refresh).
public class A350PfdDisplayTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void The_displays_section_holds_both_pfds()
    {
        var def = new IniBuildsA350Definition();
        Assert.Equal(new[] { "Captain PFD", "First Officer PFD" }, def.GetPanelStructure()["Displays"]);
        Assert.Contains(IniBuildsA350Definition.PfdCaptainKey, def.GetPanelDisplayVariables()["Captain PFD"]);
        Assert.Contains(IniBuildsA350Definition.PfdFirstOfficerKey, def.GetPanelDisplayVariables()["First Officer PFD"]);
        Assert.True(def.GetVariables()[IniBuildsA350Definition.PfdCaptainKey].ExcludeFromMonitorManager);
    }

    [Fact]
    public void Each_pfd_line_reads_its_side()
    {
        var def = new IniBuildsA350Definition();
        Assert.True(def.TryGetDisplayOverride(IniBuildsA350Definition.PfdCaptainKey, 1, out var none));
        Assert.Equal("Captain PFD\r\nThe A350 displays are not available yet.", none);

        var fo = Fixture("efis1_fd_stand");
        def.Pfd = new A350PfdMonitor(side => Task.FromResult<A350DrawCapture?>(side == 1 ? fo : null), _ => false, _ => { });
        def.Pfd.TickAsync().Wait(); def.Pfd.TickAsync().Wait();
        Assert.True(def.TryGetDisplayOverride(IniBuildsA350Definition.PfdFirstOfficerKey, 1, out var text));
        Assert.StartsWith("First Officer PFD\r\nFMA engagement: 1 FD 2, white\r\n", text);
        Assert.Contains("QNH: 1010, cyan", text);
    }
}
