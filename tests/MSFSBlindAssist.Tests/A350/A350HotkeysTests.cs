using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests.A350;

// The A350's own hotkeys, the A380's set (FBW_A380_Hotkeys.txt): each press drives the FCU row the panel offers, each
// read-out says what the cockpit shows in the words the A350's typed entries use ("FCU altitude 40000, managed").
public class A350HotkeysTests
{
    private static readonly HashSet<string> MapKeys = LoadMapKeys();

    private static HashSet<string> LoadMapKeys()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Resources", "a350_control_map.json")));
        return doc.RootElement.GetProperty("controls").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToHashSet();
    }

    [Theory]
    [InlineData(HotkeyAction.ToggleAutopilot1, "A350_FCU_AP_1")]
    [InlineData(HotkeyAction.ToggleAutopilot2, "A350_FCU_AP_2")]
    [InlineData(HotkeyAction.ToggleAutothrust, "A350_FCU_ATHR")]
    [InlineData(HotkeyAction.ToggleLocalizer, "A350_FCU_LOC")]
    [InlineData(HotkeyAction.ToggleApproachMode, "A350_FCU_APPR")]
    [InlineData(HotkeyAction.FCUHeadingPush, "A350_FCU_HDG_KNOB_PUSH")]
    [InlineData(HotkeyAction.FCUHeadingPull, "A350_FCU_HDG_KNOB_PULL")]
    [InlineData(HotkeyAction.FCUAltitudePush, "A350_FCU_ALT_KNOB_PUSH")]
    [InlineData(HotkeyAction.FCUAltitudePull, "A350_FCU_ALT_KNOB_PULL")]
    [InlineData(HotkeyAction.FCUSpeedPush, "A350_FCU_SPD_KNOB_PUSH")]
    [InlineData(HotkeyAction.FCUSpeedPull, "A350_FCU_SPD_KNOB_PULL")]
    [InlineData(HotkeyAction.FCUVSPush, "A350_FCU_VS_KNOB_PUSH")]
    [InlineData(HotkeyAction.FCUVSPull, "A350_FCU_VS_KNOB_PULL")]
    public void A_press_hotkey_writes_what_pressing_the_fcu_panels_own_row_writes(HotkeyAction action, string row)
    {
        Assert.Contains(row, MapKeys);
        var byHotkey = Sends((def, sim) => Assert.True(def.HandleHotkeyAction(action, sim, new SpeechCapture(), null!, null!)));
        var byPanel = Sends((def, sim) => def.HandleUIVariableSet(row, 1, def.GetVariables()[row], sim, new SpeechCapture()));
        Assert.NotEmpty(byHotkey);
        Assert.Equal(byPanel, byHotkey);
    }

    /// <summary>What a fresh definition writes while <paramref name="act"/> runs (tools review 2026-10-30: the test only
    /// re-typed the table, so HandleHotkeyAction's press branch was never driven).</summary>
    private static List<string> Sends(Action<MSFSBlindAssist.Aircraft.IniBuildsA350Definition, MSFSBlindAssist.SimConnect.SimConnectManager> act)
    {
        var sent = new List<string>();
        var sim = new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero);
        var def = new MSFSBlindAssist.Aircraft.IniBuildsA350Definition
        {
            CanLand = _ => true,
            Cached = (_, _) => null,
            Send = (_, rpn) => sent.Add(rpn),
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
        };
        def.Attach(sim);
        act(def, sim);
        return sent;
    }

    // Parity (2026-10-29): another aircraft's window key the A350 has a page for opens its MFD window on that page, pressed
    // with the KCCU's own page key: the NAV radios (Ctrl+N, the PMDG 737's and the iFly's window) on RAD NAV, the checklists
    // (Ctrl+Shift+C, the A380's live ECL) on C/L MENU, the datalink (Ctrl+Shift+D, the A32NX's DCDU) on ATC COM.
    [Theory]
    [InlineData(HotkeyAction.SetNavRadios, "RADNAV")]
    [InlineData(HotkeyAction.ShowChecklistECL, "CL_MENU")]
    [InlineData(HotkeyAction.ShowDCDU, "ATC")]
    public void A_window_key_opens_the_mfd_on_the_a350_s_own_page(HotkeyAction action, string key)
    {
        Assert.Equal(key, A350Hotkeys.MfdPages[action]);
        Assert.Contains(key, MSFSBlindAssist.Aircraft.A350.Display.A350Kccu.FunctionKeys);
    }

    [Theory]
    [InlineData("Speed window", "250 knots, managed", "FCU speed 250 knots, managed")]
    [InlineData("Speed window", "Mach 0.85", "FCU speed mach 0.85")]
    [InlineData("Speed window", "Dashes, managed", "FCU speed dashes, managed")]
    [InlineData("Heading window", "Heading 293", "FCU heading 293")]
    [InlineData("Heading window", "Track 120, preset", "FCU track 120, preset")]
    [InlineData("Heading window", "Dashes, managed", "FCU heading dashes, managed")]
    [InlineData("Altitude window", "40000 feet, managed", "FCU altitude 40000 feet, managed")]
    [InlineData("V/S window", "V/S +1200 feet per minute", "FCU vertical speed +1200 feet per minute")]
    [InlineData("V/S window", "FPA -3.0 degrees", "FCU flight path angle -3.0 degrees")]
    [InlineData("V/S window", "Dashes", "FCU vertical speed dashes")]
    public void An_fcu_window_reads_as_the_typed_entries_say_it(string window, string shown, string spoken) =>
        Assert.Equal(spoken, A350Hotkeys.FcuReadout(window, shown));

    [Fact]
    public void The_altimeter_reads_standard_or_both_units()
    {
        Assert.Equal("Altimeter standard", A350Hotkeys.Altimeter(3, 1013.25));
        // The cockpit floors its hectopascals (the baro knob's tooltip): 1011.8 shows 1011.
        Assert.Equal("Altimeter: 1011, 29.88", A350Hotkeys.Altimeter(0, 1011.8));
    }

    [Fact]
    public void The_altimeter_is_invariant_under_a_comma_culture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Altimeter: 1011, 29.88", A350Hotkeys.Altimeter(0, 1011.8));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(0, "Flaps Up")]
    [InlineData(1, "Flaps 1")]
    [InlineData(2, "Flaps 2")]
    [InlineData(3, "Flaps 3")]
    [InlineData(4, "Flaps Full")]
    public void Flaps_read_the_handle_as_the_a380_does(double handle, string spoken) =>
        Assert.Equal(spoken, A350Hotkeys.Flaps(handle));

    [Fact]
    public void The_gear_reads_its_lever() // the A350 gear lever row: 0 up, 1 down
    {
        Assert.Equal("Gear up", A350Hotkeys.Gear(0));
        Assert.Equal("Gear down", A350Hotkeys.Gear(1));
    }

    [Fact]
    public void A_display_read_aloud_joins_its_lines_as_sentences()
    {
        // Live 2026-10-28: the SD read "Door page\r. The SD is off: no power." (split on \n only); a line's own full
        // stop is not doubled.
        Assert.Equal("Door page. The SD is off: no power.", A350Hotkeys.Sentences("Door page\r\nThe SD is off: no power."));
        Assert.Equal("Cruise page. Cabin altitude: 6000 FT, green", A350Hotkeys.Sentences("Cruise page\nCabin altitude: 6000 FT, green\r\n"));
    }

    // W and Shift+W read the SD's own figures (the permanent data line, in the aircraft's unit): what the pilot sees.
    [Fact]
    public void Gross_weight_reads_the_sds_figures_in_either_unit_with_the_cg()
    {
        var kg = new[] { "TAT: +29 °C, green", "Gross weight: 199400 KG, green", "Gross weight CG: 30.1 %, green" };
        Assert.Equal("Gross weight 199400 kilograms, center of gravity 30.1 percent MAC", A350Hotkeys.GrossWeight(kg, pounds: false));
        Assert.Equal("Gross weight 439600 pounds, center of gravity 30.1 percent MAC", A350Hotkeys.GrossWeight(kg, pounds: true));
        var lbs = new[] { "Gross weight: 439600 LBS, green", "Gross weight CG: -- %, cyan" };
        Assert.Equal("Gross weight 199400 kilograms", A350Hotkeys.GrossWeight(lbs, pounds: false));
        Assert.Equal("Gross weight not available", A350Hotkeys.GrossWeight(new[] { "Gross weight: -- KG, cyan" }, pounds: false));
    }

    [Fact]
    public void Gross_weight_is_invariant_under_a_comma_culture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Gross weight 199400 kilograms, center of gravity 30.1 percent MAC",
                A350Hotkeys.GrossWeight(new[] { "Gross weight: 199400 KG, green", "Gross weight CG: 30.1 %, green" }, pounds: false));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    // X: the A350 keeps its squawk to itself (the stock TRANSPONDER CODE reads 0, live 2026-10-28); its RMP shows it.
    [Fact]
    public void The_squawk_reads_the_rmps_line()
    {
        var rmp = new[] { "VHF1: active 121.900, white; standby 118.700, cyan", "SQUAWK : 2000 - STBY, white" };
        Assert.Equal("Squawk 2000, standby", A350Hotkeys.Squawk(rmp));
        Assert.Equal("Squawk 4512, auto", A350Hotkeys.Squawk(new[] { "SQUAWK : 4512 - AUTO, white" }));
        Assert.Equal("Squawk not shown", A350Hotkeys.Squawk(new[] { "VHF1: active 121.900, white" }));
    }

    [Fact]
    public void A_characteristic_speed_reads_in_knots_or_says_it_is_not_shown()
    {
        Assert.Equal("V L S 166 knots", A350Hotkeys.Speed("V L S", 165.911));
        Assert.Equal("Alpha protection speed not shown", A350Hotkeys.Speed("Alpha protection speed", 0));
    }
    // CORE-17: a figure another program drew is parsed as such, so "Infinity" (or "NaN") is no figure, never a weight said.
    [Fact]
    public void A_gross_weight_that_is_no_number_is_not_available()
    {
        Assert.Equal("Gross weight not available", A350Hotkeys.GrossWeight(new[] { "Gross weight: Infinity KG, green" }, pounds: false));
    }
}
