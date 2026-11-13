using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26 Task 6: the ISIS window (Alt+I), as the Fenix's ISIS window with its buttons and baro, and the SND window
/// (Alt+Shift+N, the owner's key: the ND is Alt+N, the standby navigation display beside it), each its unit's own panel under the list.</summary>
public class A350StandbyWindowsTests
{
    [Fact]
    public void The_isis_windows_keys_and_typed_qnh()
    {
        Assert.Equal(new[] { "STD/CHANGE", "LS/DIR TO", "MENU", "Minus", "MODE", "Plus" }, A350IsisWindow.Keys.Select(k => k.Label));
        Assert.Equal(new[] { "A350_ISIS_KNOB_BUTTON_1", "A350_ISIS_LS_1", "A350_ISIS_MENU_1", "A350_ISIS_MINUS_1", "A350_ISIS_MODE_1", "A350_ISIS_PLUS_1" },
            A350IsisWindow.Keys.Select(k => k.Row(0)));
        Assert.Equal("A350_ISIS_QNH_SET", A350IsisWindow.QnhRow);
    }

    [Fact]
    public void The_snd_windows_keys()
    {
        Assert.Equal(new[] { "STD/CHANGE", "LS/DIR TO", "MENU", "Minus", "MODE", "Plus", "Knob up", "Knob down" }, A350SndWindow.Keys.Select(k => k.Label));
        Assert.Equal(new[] { "A350_ISIS_KNOB_BUTTON_2", "A350_ISIS_LS_2", "A350_ISIS_MENU_2", "A350_ISIS_MINUS_2", "A350_ISIS_MODE_2", "A350_ISIS_PLUS_2",
            "A350_ISIS_KNOB_2#INC", "A350_ISIS_KNOB_2#DEC" }, A350SndWindow.Keys.Select(k => k.Row(0)));
    }

    // Each unit's panel is on its window, every row of it.
    [Fact]
    public void Every_row_of_each_units_panel_is_on_its_window()
    {
        var placement = A350PanelLayout.Place(A350ControlMap.Load());
        Assert.Equal(placement.RowsByPanel["ISIS"].Select(r => r.Key).OrderBy(r => r, StringComparer.Ordinal),
            A350IsisWindow.Keys.Select(k => k.Row(0)).Append(A350IsisWindow.QnhRow).OrderBy(r => r, StringComparer.Ordinal));
        Assert.Equal(placement.RowsByPanel["SND"].Select(r => r.Key).OrderBy(r => r, StringComparer.Ordinal),
            A350SndWindow.Keys.Select(k => k.Row(0)).OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_row_they_press_is_a_row_of_the_aircraft()
    {
        var rows = new IniBuildsA350Definition().GetVariables();
        Assert.All(A350IsisWindow.Keys.Concat(A350SndWindow.Keys), k => Assert.True(rows.ContainsKey(k.Row(0)), k.Row(0)));
        Assert.True(rows.ContainsKey(A350IsisWindow.QnhRow));
    }

    [Fact]
    public void Alt_i_and_alt_shift_n_open_them()
    {
        Assert.Equal("ISIS", A350Hotkeys.DisplayWindows[HotkeyAction.ReadDisplayISIS]);
        Assert.Equal("SND", A350Hotkeys.DisplayWindows[HotkeyAction.ShowStandbyNavDisplay]);
    }

    // Alt+Shift+N is registered in output mode for the SND, and another aircraft says the window is the A350's.
    [Fact]
    public void Alt_shift_n_is_registered_in_output_mode()
    {
        string Source(params string[] path) => File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(path).ToArray()));
        string manager = Source("Hotkeys", "HotkeyManager.cs");
        Assert.Contains("RegisterHotKey(windowHandle, HOTKEY_SHOW_STANDBY_NAV_DISPLAY, MOD_ALT | MOD_SHIFT, 0x4E)", manager);
        Assert.Contains("UnregisterHotKey(windowHandle, HOTKEY_SHOW_STANDBY_NAV_DISPLAY)", manager);
        Assert.Contains("TriggerHotkey(HotkeyAction.ShowStandbyNavDisplay)", manager);
        Assert.Contains("The standby navigation display window is only on the A350.", Source("MainForm.Hotkeys.cs"));
    }

    private static IniBuildsA350Definition Def(Dictionary<string, double> cache)
    {
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        return def;
    }

    [Fact]
    public void The_isis_feed_reads_the_sfd()
    {
        var def = Def(new Dictionary<string, double>(StringComparer.Ordinal));
        var sfd = A350Fixtures.Capture("ground_SFD.json");
        Assert.Equal(A350GroundDisplays.ReadSfd(sfd), def.DisplayFeed("Standby flight display").Read(sfd, 0));
    }

    // The SND is an option of the OIS (STANDBY NAV DISPLAY): not fitted, its window says so before anything else.
    [Fact]
    public void The_snd_feed_says_not_fitted_while_the_option_is_off()
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var def = Def(cache);
        var option = A350Options.For("AIRLINER_ISIS_MENU_2")!;
        cache[option.Key] = 0;
        Assert.Equal("not fitted (an OIS option)", def.SndFeed().Unavailable!(0));
        cache[option.Key] = 1;
        Assert.Null(def.SndFeed().Unavailable!(0));
        var snd = A350Fixtures.Capture("ground_SND.json");
        Assert.Equal(A350GroundDisplays.ReadSnd(snd), def.SndFeed().Read(snd, 0));
    }
}
