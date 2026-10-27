using System.Text.Json;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The displays on demand, as the A380's: a Displays section in the Panels list, one panel per display, each
// status display reading that display (Ctrl+3, F5, the 1 s refresh) from the monitor's latest draw.
public class A350PfdDisplayTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void The_displays_section_holds_every_display()
    {
        var def = new IniBuildsA350Definition();
        Assert.Equal(new[]
        {
            "Captain PFD", "First Officer PFD", "Captain ND", "First Officer ND", "Captain MFD", "First Officer MFD",
            "FCU display", "RMP 1 display", "RMP 2 display", "RMP 3 display",
            "Standby flight display", "Standby navigation display", "BAT display",
        }, def.GetPanelStructure()["Displays"]);
        foreach (var d in IniBuildsA350Definition.DisplayPanels)
        {
            Assert.Contains(d.Key, def.GetPanelDisplayVariables()[d.Panel]);
            Assert.True(def.GetVariables()[d.Key].ExcludeFromMonitorManager);
        }
    }

    [Fact]
    public void Each_display_line_reads_its_display()
    {
        var def = new IniBuildsA350Definition();
        Assert.True(def.TryGetDisplayOverride(IniBuildsA350Definition.PfdCaptainKey, 1, out var none));
        Assert.Equal("Captain PFD\r\nThe A350 displays are not available yet.", none);

        var draws = new Dictionary<(string, int), A350DrawCapture>
        {
            [("EFIS", 0)] = Fixture("ground_EFIS_0"), [("EFIS", 1)] = Fixture("efis1_fd_stand"),
            [("FCU", 0)] = Fixture("ground_FCU"), [("RMP", 0)] = Fixture("ground_RMP_0"), [("BAT", 0)] = Fixture("ground_BAT"),
        };
        def.Pfd = new A350PfdMonitor((g, i) => Task.FromResult(draws.GetValueOrDefault((g, i))), _ => false, _ => { });
        for (int t = 0; t < 8; t++) def.Pfd.TickAsync().Wait();

        string Read(string panel)
        {
            var key = IniBuildsA350Definition.DisplayPanels.Single(d => d.Panel == panel).Key;
            Assert.True(def.TryGetDisplayOverride(key, 1, out var text));
            return text;
        }
        Assert.StartsWith("First Officer PFD\r\nFMA engagement: 1 FD 2, white\r\n", Read("First Officer PFD"));
        Assert.Contains("Ground speed: 0, green", Read("Captain ND"));
        Assert.Contains("Speed: 100 KT, white", Read("FCU display"));
        Assert.Contains("VHF1: active 122.800, white; standby 123.450, cyan", Read("RMP 1 display"));
        Assert.Contains("Battery voltage: 28.2 V, grey", Read("BAT display"));
        Assert.Equal("RMP 2 display\r\nThe A350 displays are not available yet.", Read("RMP 2 display"));
    }
}
