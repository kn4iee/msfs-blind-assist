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
        def.Pfd = new A350PfdMonitor(A350PfdMonitorTests.Each((g, i) => draws.GetValueOrDefault((g, i))), _ => false, _ => { });
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
        def.Pfd = new A350PfdMonitor(A350PfdMonitorTests.Each((g, i) => draws.GetValueOrDefault((g, i))), _ => false, _ => { });
        for (int t = 0; t < 8; t++) def.Pfd.TickAsync().Wait();
        var key = IniBuildsA350Definition.DisplayPanels.Single(d => d.Panel == panel).Key;

        cache[def.KeyForInput(power)] = 0;
        Assert.True(def.TryGetDisplayOverride(key, 1, out var dark));
        Assert.Equal(panel + "\r\nDark: its screen is off or has no power.", dark);

        cache[def.KeyForInput(power)] = 1;
        Assert.True(def.TryGetDisplayOverride(key, 1, out var lit));
        Assert.DoesNotContain("Dark", lit);
    }

    // Review 2026-10-28: with the captain's PFD dark, Ctrl+0 said "Approach capability none shown", which reads as no
    // capability at all; Ctrl+W ("No TO waypoint shown") and X read a dark ND and RMP the same way. A readout from a
    // dark screen says it is dark, as Alt+P does.
    [Theory]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadApproachCapability, "INI_LEFT_EFIS_POWERED", "Captain PFD")]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedGD, "INI_LEFT_EFIS_POWERED", "Captain PFD")]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadNDWaypoint, "INI_LEFT_EFIS_POWERED", "Captain ND")]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSquawkCode, "INI_POTENTIOMETER_93", "RMP 1 display")]
    public void A_readout_from_a_dark_screen_says_it_is_dark(MSFSBlindAssist.Hotkeys.HotkeyAction action, string power, string panel)
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        var sim = new SimConnect.SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        var draws = new Dictionary<(string, int), A350DrawCapture>
        {
            [("EFIS", 0)] = Fixture("efis0_ls_downwind_omdb"), [("RMP", 0)] = Fixture("ground_RMP_0"),
        };
        def.Pfd = new A350PfdMonitor(A350PfdMonitorTests.Each((g, i) => draws.GetValueOrDefault((g, i))), _ => false, _ => { });
        for (int t = 0; t < 8; t++) def.Pfd.TickAsync().Wait();
        cache[def.KeyForInput(power)] = 0;
        var speech = new SpeechCapture();
        Assert.True(def.HandleHotkeyAction(action, sim, speech, null!, null!));
        Assert.Equal(new[] { panel + " dark: its screen is off or has no power." }, speech.All);
    }

    // Shift+1 to 3 read the A380's green dot, S and F speeds; the A350 shows them only as symbols on the captain's PFD
    // speed tape, so they are read off it: "not shown" while the tape does not draw one, above or below the speed scale
    // while it is drawn outside the tape's visible window (as a sighted pilot sees it), "not available" while the displays
    // do not run.
    [Theory]
    [InlineData("efis0_level6000", MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedGD, "Green dot speed 218 knots", "Green dot speed not available")]
    [InlineData("efis0_ils_above", MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedF, "F speed 137 knots", "F speed not available")]
    [InlineData("efis0_ils_below", MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedF, "F speed below the speed scale", "F speed not available")]
    [InlineData("efis0_ils_below", MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedS, "S speed not shown", "S speed not available")]
    [InlineData("efis0_fd_stand", MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedGD, "Green dot speed not shown", "Green dot speed not available")]
    [InlineData("efis0_climb_bi614", MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedGD, "Green dot speed below the speed scale", "Green dot speed not available")]
    public void The_characteristic_speeds_read_off_the_captains_pfd(string fixture, MSFSBlindAssist.Hotkeys.HotkeyAction action, string said,
        string displaysNotRunning)
    {
        var def = new IniBuildsA350Definition { Cached = (_, _) => null };
        var sim = new SimConnect.SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        var speech = new SpeechCapture();
        Assert.True(def.HandleHotkeyAction(action, sim, speech, null!, null!));
        Assert.Equal(new[] { displaysNotRunning }, speech.All);

        var captain = Fixture(fixture);
        def.Pfd = new A350PfdMonitor(A350PfdMonitorTests.Each((g, i) => g == "EFIS" && i == 0 ? captain : null), _ => false, _ => { });
        for (int t = 0; t < 8; t++) def.Pfd.TickAsync().Wait();
        speech = new SpeechCapture();
        Assert.True(def.HandleHotkeyAction(action, sim, speech, null!, null!));
        Assert.Equal(new[] { said }, speech.All);
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

    // Climbing through FL235 in STD with FL400 selected (live 2026-10-28): the tape's top shows the selected altitude ("FL"
    // at 587, "400" at 688, y 172). The tape's own selected altitude symbol (the box it draws in range: a black fill x
    // 587-691 in its frame, the value at x 597) is still drawn out of range, held at the screen's top ("400" at x 646,
    // y 38), inside the FMA's top row where the tape's clip hides it: it read as the FMA's fourth column ("FMA approach:
    // 400, cyan"), and before that into the engagement column, whose first text is "1" at 685.
    [Fact]
    public void Out_of_range_the_selected_altitude_reads_once_and_never_as_an_fma_column()
    {
        var lines = A350Pfd.Read(Fixture("pfd_climb_std_fl400"), 0);
        Assert.Equal("FMA engagement: AP1; 1 FD 2; A/THR, white", lines.Single(l => l.StartsWith("FMA engagement", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, l => l.StartsWith("FMA approach", StringComparison.Ordinal));
        Assert.Equal("Selected altitude: FL 400, cyan", lines.Single(l => l.Contains("400")));
    }

    [Fact]
    public void The_rmp_window_shows_a_dark_screen_as_dark() =>
        Assert.Equal(new[] { IniBuildsA350Definition.DarkScreen }, Forms.A350.A350RmpWindow.Lines(Fixture("ground_RMP_0"), dark: true));
}
