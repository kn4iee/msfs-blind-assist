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

    [Theory]
    // Live 2026-10-28: with the captain's outer display switched off (INI_LEFT_EFIS_POWERED 0, the variable following
    // the switch and the bus) the A350 still drew its PFD and ND in memory, and MSFSBA read them as if lit; a sighted
    // pilot sees a black screen. Each screen's own power or brightness variable says whether it is lit.
    [InlineData("Captain PFD", "INI_LEFT_EFIS_POWERED")]
    [InlineData("Captain ND", "INI_LEFT_EFIS_POWERED")]
    [InlineData("First Officer PFD", "INI_RIGHT_EFIS_POWERED")]
    [InlineData("Captain MFD", "INI_FMS_POWERED")]
    [InlineData("FCU display", "INI_FCU_DISPLAY_BRT")]
    [InlineData("RMP 1 display", "INI_POTENTIOMETER_93")]
    [InlineData("Standby flight display", "INI_ISIS1_POWERED")]
    [InlineData("Standby navigation display", "INI_ISIS2_POWERED")]
    public void A_display_whose_screen_is_off_reads_dark(string panel, string power)
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        var draws = new Dictionary<(string, int), A350DrawCapture>
        {
            [("EFIS", 0)] = Fixture("ground_EFIS_0"), [("EFIS", 1)] = Fixture("efis1_fd_stand"),
            [("FCU", 0)] = Fixture("ground_FCU"), [("RMP", 0)] = Fixture("ground_RMP_0"),
        };
        def.Pfd = new A350PfdMonitor((g, i) => Task.FromResult(draws.GetValueOrDefault((g, i))), _ => false, _ => { });
        for (int t = 0; t < 8; t++) def.Pfd.TickAsync().Wait();
        var key = IniBuildsA350Definition.DisplayPanels.Single(d => d.Panel == panel).Key;

        cache[def.KeyForInput(power)] = 0;
        Assert.True(def.TryGetDisplayOverride(key, 1, out var dark));
        Assert.Equal(panel + "\r\nDark: its screen is off or has no power.", dark);

        cache[def.KeyForInput(power)] = 1;
        Assert.True(def.TryGetDisplayOverride(key, 1, out var lit));
        Assert.DoesNotContain("Dark", lit);
    }

    // Review 2026-10-28: Shift+M and Ctrl+Shift+R read the latest draw directly, so a screen the status display called
    // dark showed its whole content in its window, where the pilot could even click it.
    [Theory]
    [InlineData("Captain MFD", "INI_FMS_POWERED")]
    [InlineData("First Officer MFD", "INI_FMS_POWERED")]
    [InlineData("RMP 1 display", "INI_POTENTIOMETER_93")]
    [InlineData("RMP 2 display", "INI_POTENTIOMETER_94")]
    [InlineData("RMP 3 display", "INI_POTENTIOMETER_95")]
    public void A_window_reads_its_screen_dark_as_the_status_display_does(string panel, string power)
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        Assert.False(def.IsScreenDark(panel));   // unread: taken as lit, as the status display takes it
        cache[def.KeyForInput(power)] = 0;
        Assert.True(def.IsScreenDark(panel));
        cache[def.KeyForInput(power)] = 1;
        Assert.False(def.IsScreenDark(panel));
    }

    [Fact]
    public void The_mfd_window_shows_a_dark_screen_as_dark_with_no_line_to_click()
    {
        var (lines, shown) = Forms.A350.A350MfdWindow.PageLines(Fixture("mfd_init_empty"), 0, dark: true);
        Assert.Empty(lines);
        Assert.Equal(new[] { IniBuildsA350Definition.DarkScreen }, shown);
        var (litLines, litShown) = Forms.A350.A350MfdWindow.PageLines(Fixture("mfd_init_empty"), 0, dark: false);
        Assert.NotEmpty(litLines);
        Assert.Equal(litLines.Select(l => l.Spoken), litShown);
    }

    // Climbing through FL235 in STD with FL400 selected (live 2026-10-28): the FMA's fourth column shows the altitude the
    // armed capture aims at ("400" in cyan at x 646, as the SID's 5000 constraint showed magenta there on the ground), and
    // the tape's top box shows the selected altitude ("FL" at 587, "400" at 688, y 172). The "400" at 646 was read into
    // the engagement column ("400, cyan; AP1, white; ..."), whose first text is "1" at 685, and the box read "FL" alone.
    [Fact]
    public void The_fma_altitude_target_and_the_selected_altitude_box_read_where_they_are_drawn()
    {
        var lines = A350Pfd.Read(Fixture("pfd_climb_std_fl400"), 0);
        Assert.Equal("FMA engagement: AP1; 1 FD 2; A/THR, white", lines.Single(l => l.StartsWith("FMA engagement", StringComparison.Ordinal)));
        Assert.Contains("FMA approach: 400, cyan", lines);
        Assert.Contains("Selected altitude: FL 400, cyan", lines);
    }

    [Fact]
    public void The_rmp_window_shows_a_dark_screen_as_dark() =>
        Assert.Equal(new[] { IniBuildsA350Definition.DarkScreen }, Forms.A350.A350RmpWindow.Lines(Fixture("ground_RMP_0"), dark: true));
}
