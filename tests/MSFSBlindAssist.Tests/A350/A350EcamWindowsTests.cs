using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26 Task 5: the E/WD window (Alt+E) and the SD window (Alt+S), as the Fenix's: each display live, then the ECAM control
/// panel's keys that act on it (the Fenix: T.O CONFIG, CLR, RCL and EMER CANC on the E/WD; the page keys on the SD), every key of the
/// A350's three ECAM panels on one of the two.</summary>
public class A350EcamWindowsTests
{
    [Fact]
    public void The_ewd_windows_keys()
    {
        Assert.Equal(new[]
        {
            "T.O CONFIG", "CLEAR left", "CLEAR right", "RCL LAST", "RCL ALL", "EMER CANC guard", "EMER CANC", "ABN PROC", "DEFRD PROC",
            "C/L left", "C/L right", "MORE", "DISPATCH", "Left wheel up", "Left wheel down", "Right wheel up", "Right wheel down",
        }, A350EwdWindow.Keys.Select(k => k.Label));
        Assert.Equal(new[]
        {
            "A350_ECAM_TO_CONFIG", "A350_ECAM_CLEAR_LEFT", "A350_ECAM_CLEAR_RIGHT", "A350_ECAM_RCL_LAST", "A350_ECAM_RCL_ALL",
            "A350_ECAM_EMER_CANCEL_GUARD", "A350_ECAM_EMER_CANCEL", "A350_ECAM_ABN", "A350_ECAM_DEFRD", "A350_ECAM_CHECKLIST_LEFT",
            "A350_ECAM_CHECKLIST_RIGHT", "A350_ECAM_MORE", "A350_ECAM_DISPCH", "A350_ECAM_SCROLL_L#INC", "A350_ECAM_SCROLL_L#DEC",
            "A350_ECAM_SCROLL_R#INC", "A350_ECAM_SCROLL_R#DEC",
        }, A350EwdWindow.Keys.Select(k => k.Row(0)));
        Assert.Equal(new[] { "EMER CANC guard" }, A350EwdWindow.Keys.Where(k => k.Toggle).Select(k => k.Label));
    }

    [Fact]
    public void The_sd_windows_keys()
    {
        Assert.Equal(new[]
        {
            "ENG", "APU", "BLEED", "COND", "PRESS", "DOOR", "EL/AC", "EL/DC", "FUEL", "WHEEL", "HYD", "F/CTL", "ALL", "STS", "VIDEO",
            "SD knob push", "SD knob up", "SD knob down",
        }, A350SdWindow.Keys.Select(k => k.Label));
        Assert.Equal(new[]
        {
            "A350_ECAM_ENG", "A350_ECAM_APU", "A350_ECAM_BLEED", "A350_ECAM_COND", "A350_ECAM_PRESS", "A350_ECAM_DOOR", "A350_ECAM_ELAC",
            "A350_ECAM_ELDC", "A350_ECAM_FUEL", "A350_ECAM_WHEEL", "A350_ECAM_HYD", "A350_ECAM_FCTL", "A350_ECAM_ALL", "A350_ECAM_STS",
            "A350_ECAM_VIDEO", "A350_ECAM_SD_KNOB_PUSH", "A350_ECAM_SD_KNOB#INC", "A350_ECAM_SD_KNOB#DEC",
        }, A350SdWindow.Keys.Select(k => k.Row(0)));
    }

    // Every row of the ECAM control panels, both pilots' included, is on exactly one of the two windows; but the panel's SD page
    // selector, the page keys again as one combo, which the SD window gives as its buttons: a combo would press a key at every page the
    // reader arrows past (the Fenix SD window's reason).
    [Fact]
    public void Every_ecam_control_panel_row_is_on_one_of_the_two_windows()
    {
        var placement = A350PanelLayout.Place(A350ControlMap.Load());
        var rows = new[] { "ECAM Control Panel", "Captain ECAM Control Panel", "First Officer ECAM Control Panel" }
            .SelectMany(p => placement.RowsByPanel[p]).Select(r => r.Key).Where(r => r != IniBuildsA350Definition.SdPageKey).ToList();
        var onWindows = A350EwdWindow.Keys.Concat(A350SdWindow.Keys).Select(k => k.Row(0)).ToList();
        Assert.Equal(onWindows.Count, onWindows.Distinct().Count());
        Assert.Equal(rows.OrderBy(r => r, StringComparer.Ordinal), onWindows.OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_row_they_press_is_a_row_of_the_aircraft()
    {
        var rows = new IniBuildsA350Definition().GetVariables();
        Assert.All(A350EwdWindow.Keys.Concat(A350SdWindow.Keys), k => Assert.True(rows.ContainsKey(k.Row(0)), k.Row(0)));
    }

    [Fact]
    public void Alt_e_and_alt_s_open_them()
    {
        Assert.Equal("E/WD", A350Hotkeys.DisplayWindows[HotkeyAction.ReadDisplayUpperECAM]);
        Assert.Equal("SD", A350Hotkeys.DisplayWindows[HotkeyAction.ReadDisplayLowerECAM]);
    }

    private static IReadOnlyList<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    // Each reads the CTR's draw as its reader makes it: the E/WD with the SD page the ECAM shows, the SD on that page.
    [Fact]
    public void They_read_the_ctr_as_their_readers_do()
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal) { [IniBuildsA350Definition.SdPageKey] = 3 };
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        var ctr = A350Fixtures.Capture("ctr_sd_page_3.json");
        Assert.Equal(Lines(A350EwdText.Build(ctr, 3)), def.EwdFeed().Read(ctr, 0));
        Assert.Equal(Lines(A350SdText.Build(ctr, 3)), def.SdFeed().Read(ctr, 0));
        Assert.False(def.SdFeed().Dark(0));
    }

    // Plan 26's review minor: the E/WD and SD windows parsed the CTR's draw twice a tick each (whether it is dark, then the reader). The
    // definition parses a draw once, whoever asks.
    [Fact]
    public void A_ctr_draw_is_parsed_once_whoever_reads_it()
    {
        var def = new IniBuildsA350Definition();
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        var ctr = A350Fixtures.Capture("ctr_cruise.json");
        Assert.Same(def.CtrView(ctr), def.CtrView(ctr));
        Assert.NotSame(def.CtrView(ctr), def.CtrView(A350Fixtures.Capture("ctr_cruise.json")));
    }

    private static IniBuildsA350Definition WithPage(double? page)
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        if (page is double p) cache[IniBuildsA350Definition.SdPageKey] = p;
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        return def;
    }

    // Re-review M1-M5: the SD was read by the page variable, which reaches MSFSBA a moment after the draw shows a new page. It is read
    // by the page its draw shows (A350SdPage.PageOf), the variable only where the draw names none.
    [Fact]
    public void The_sd_is_read_by_the_page_its_draw_shows()
    {
        var fuel = A350Fixtures.Capture("ctr_sd_page_8.json");
        var def = WithPage(3);   // the variable still says ELEC DC
        Assert.Equal(Lines(A350SdText.Build(fuel, 8)), def.SdFeed().Read(fuel, 0));
        Assert.Equal(Lines(A350EwdText.Build(fuel, 8)), def.EwdFeed().Read(fuel, 0));
    }

    // STATUS chosen with nothing to say draws the CRUISE page: the variable's STATUS stands, said "showing the Cruise page".
    [Fact]
    public void Status_drawing_another_page_is_still_status()
    {
        var status = A350Fixtures.Capture("p04_ctr_sd_status_air.json");
        Assert.Equal(Lines(A350SdText.Build(status, 14)), WithPage(14).SdFeed().Read(status, 0));
    }

    // Re-review M3: with the page variable unread and no draw to name the page, the SD is not read yet, never read as a guessed page 0.
    [Fact]
    public void An_sd_with_no_page_known_is_not_read_yet()
    {
        Assert.Equal("not read yet", WithPage(null).SdFeed().Unavailable!(0));
        Assert.Null(WithPage(3).SdFeed().Unavailable?.Invoke(0));
    }
}
