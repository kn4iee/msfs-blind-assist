using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's announced lights and states (plan 06, <see cref="Bbs757Annunciators"/>): what each row
/// registers, which status display shows it, what a change says, and the EXT PWR button's read-back,
/// which says the light's state instead of "pressed".
/// </summary>
public class Bbs757AnnunciatorsTests
{
    private const string OnKey = Bbs757SharedKeys.ExtPwrOn;
    private const string AvailKey = Bbs757SharedKeys.ExtPwrAvail;

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; } = 100_000;
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig(Bbs757ControlMap? map = null)
            => Def = map == null
                ? new BBS757Definition { NestRunning = () => true, NowMs = () => Now }
                : new BBS757Definition(map) { NestRunning = () => true, NowMs = () => Now };

        public bool Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);
    }

    /// <summary>A map of one control on <paramref name="panel"/>, so no test depends on the generated map's names.</summary>
    private static Bbs757ControlMap MapWithPanel(string panel) => Bbs757ControlMap.Parse($$"""
        {
          "controls": [
            {
              "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "{{panel}}",
              "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON"
            }
          ]
        }
        """);

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- Task 1: EXT PWR ----------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, "EXT PWR on")]
    [InlineData(0.0, "EXT PWR off")]
    [InlineData(0.51, "EXT PWR on")]
    [InlineData(0.5, "EXT PWR off")]
    [InlineData(null, "EXT PWR unavailable")]
    [InlineData(double.NaN, "EXT PWR unavailable")]
    public void Ext_pwr_press_reads_back(double? value, string said)
    {
        // The press reads the ON light back instead of saying "EXT PWR pressed".
        Assert.Equal(OnKey, BBS757Definition.LinkedStatusKey(Bbs757Annunciators.ExtPwrButtonNode));
        Assert.Equal("B757_OVHD_EXT_PWR_BUTTON", Bbs757Annunciators.ExtPwrButtonNode);
        Assert.Equal(said, BBS757Definition.LinkedPhrase(OnKey, value));

        // The read-back rebaselines the row with this phrase, so the row's own words must be the same, or the
        // next delivery would announce the press a second time.
        if (value is double v && double.IsFinite(v))
            Assert.Equal(said, Bbs757Annunciators.ForKey(OnKey)!.Phrase(v, null));
    }

    [Fact]
    public void Ext_pwr_rows_registered_as_announced_status_rows()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();

        foreach (var (key, lvar, label) in new[]
        {
            (OnKey, "B757_OVHD_EXT_PWR_BUTTON_ON", "External Power On"),
            (AvailKey, "B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL", "External Power Available"),
        })
        {
            var row = vars[key];
            Assert.Equal(lvar, row.Name);
            Assert.Equal(label, row.DisplayName);
            Assert.Equal(SimVarType.LVar, row.Type);
            Assert.Equal("number", row.Units);
            Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
            Assert.True(row.IsAnnounced);
            Assert.True(row.RenderAsReadOnlyStatus);
            Assert.False(row.ExcludeFromMonitorManager);
            Assert.Contains(def.MonitorRows(), r => r.Key == key && r.Label == label);
            Assert.DoesNotContain(key, def.GetPanelControls().SelectMany(p => p.Value));
        }

        Assert.True(def.TryGetDisplayOverride(OnKey, 1, out string on));
        Assert.Equal("On", on);
        Assert.True(def.TryGetDisplayOverride(OnKey, 0, out string off));
        Assert.Equal("Off", off);
        Assert.True(def.TryGetDisplayOverride(AvailKey, 1, out string avail));
        Assert.Equal("Available", avail);
        Assert.True(def.TryGetDisplayOverride(AvailKey, 0, out string notAvail));
        Assert.Equal("Not available", notAvail);
        Assert.True(def.TryGetDisplayOverride(AvailKey, double.NaN, out string unread));
        Assert.Equal("unavailable", unread);
    }

    [Theory]
    [InlineData("Electrical", true)]
    [InlineData("Pedestal", false)]
    public void Ext_pwr_rows_on_electrical(string panel, bool joins)
    {
        var def = new Rig(MapWithPanel(panel)).Def;
        var displays = def.GetPanelDisplayVariables();

        if (joins)
        {
            var rows = displays[Bbs757Annunciators.ElectricalPanel];
            Assert.Equal(new[] { OnKey, AvailKey }, rows.Where(r => r is OnKey or AvailKey));
            Assert.Single(displays.SelectMany(p => p.Value), OnKey);
            Assert.Single(displays.SelectMany(p => p.Value), AvailKey);
            // Plan 07's ground power row follows: the button's lights, then the cart.
            Assert.True(rows.IndexOf(AvailKey) < rows.IndexOf(Bbs757SharedKeys.GpuConnected));
        }
        else
        {
            Assert.DoesNotContain(OnKey, displays.SelectMany(p => p.Value));
            Assert.DoesNotContain(AvailKey, displays.SelectMany(p => p.Value));
        }
    }

    [Theory]
    [InlineData(OnKey, 0.0, 1.0, "EXT PWR on")]
    [InlineData(OnKey, 1.0, 0.0, "EXT PWR off")]
    [InlineData(AvailKey, 0.0, 1.0, "External power available")]
    [InlineData(AvailKey, 1.0, 0.0, "External power not available")]
    public void Ext_pwr_change_announced_once(string key, double first, double then, string said)
    {
        var rig = new Rig();

        Assert.True(rig.Deliver(key, first));   // the cockpit as found: a silent baseline
        rig.Deliver(key, first);
        rig.Deliver(key, double.NaN);           // unreadable: neither spoken nor a baseline
        Assert.Empty(rig.Speech.All);

        Assert.True(rig.Deliver(key, then));
        rig.Deliver(key, then);
        Assert.Equal(new[] { said }, rig.Speech.All);

        rig.Def.OnSimContextReset();            // a flight load: the next reading is a baseline again
        rig.Deliver(key, first);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public async Task An_ext_pwr_press_is_heard_once_even_if_avail_changes_with_it()
    {
        // Not measured: BlueBird may put the AVAIL light out when ON lights. The press says "EXT PWR on" itself
        // (CORE-7); an AVAIL change inside its echo must not be said beside it, nor sound like power lost.
        var map = Bbs757ControlMap.Parse("""
            {
              "controls": [
                {
                  "node": "B757_OVHD_EXT_PWR_BUTTON", "section": "Overhead", "panel": "Electrical", "label": "EXT PWR",
                  "kind": "button", "state_var": null,
                  "press": "1 (>L:B757_OVHD_EXT_PWR_BUTTON, bool)", "release": "0 (>L:B757_OVHD_EXT_PWR_BUTTON, bool)"
                }
              ]
            }
            """);
        var io = new Bbs757FakeIo { Hold = _ => true };
        io.Fresh[OnKey] = 1;
        long now = 100_000;
        var def = new BBS757Definition(map) { NestRunning = () => true, NowMs = () => now, WriteIo = _ => io.Io };
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(OnKey, 0, speech);
        def.ProcessSimVarUpdate(AvailKey, 1, speech);

        def.HandleUIVariableSet(Bbs757Annunciators.ExtPwrButtonNode, 1, new SimVarDefinition(), new SimConnectManager(IntPtr.Zero), speech);
        await io.WritesReached(1);
        now += 300;
        def.ProcessSimVarUpdate(OnKey, 1, speech);     // the press's own echo
        def.ProcessSimVarUpdate(AvailKey, 0, speech);  // AVAIL out as ON lights
        for (int i = 0; i < 200 && !speech.All.Contains("EXT PWR on"); i++)
        {
            for (int d = 0; d < io.Delays.Count; d++) io.Release(d);   // each wait as it is asked
            await Task.Delay(10);
        }
        now += 1_000;
        def.ProcessSimVarUpdate(AvailKey, 0, speech);

        Assert.Equal(new[] { "EXT PWR on" }, speech.All);
    }

    [Fact]
    public void An_irs_switch_value_survives_a_flight_load()
    {
        // A switch value is a fact, not a baseline: the batch delivers only changes, so after a flight load
        // (the tracker reset) the switch at NAV is not delivered again, and the IRS must still say aligned.
        var rig = new Rig();
        rig.Deliver("BBS757_IRS_L_SWITCH_WATCH", 2);
        rig.Deliver("BBS757_IRS_L_ALIGN", 1);
        rig.Def.OnSimContextReset();
        rig.Deliver("BBS757_IRS_L_ALIGN", 1);   // the baseline after the load
        rig.Deliver("BBS757_IRS_L_ALIGN", 0);
        Assert.Equal(new[] { "IRS L aligned" }, rig.Speech.All);
    }

    [Fact]
    public void A_ground_power_window_run_is_heard_once_with_both_rows_present()
    {
        // Plan 07's handoff: the GPU tap changes the AVAIL light too (about 0.17 s after it); the window says
        // what came of its taps, so neither the Ground Power row nor the AVAIL row may say it again.
        var rig = new Rig();
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 0);
        rig.Deliver(AvailKey, 0);

        rig.Def.ArmGroundPowerEcho();
        rig.Now += 6_000;
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 1);
        rig.Deliver(AvailKey, 1);
        rig.Def.SettleGroundPowerEcho(1);
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 1);
        rig.Deliver(AvailKey, 1);
        Assert.Empty(rig.Speech.All);

        // A later change from elsewhere (the cart removed by BlueBird) speaks both rows.
        rig.Now += 60_000;
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 0);
        rig.Deliver(AvailKey, 0);
        Assert.Equal(new[] { "Ground power disconnected", "External power not available" }, rig.Speech.All);
    }

    // ---- Task 4: the essential announcements ---------------------------------------------------------------

    /// <summary>Delivers each (key, value) in order and returns what was said.</summary>
    private static List<string> Said(params (string Key, double Value)[] deliveries)
    {
        var rig = new Rig();
        foreach (var (key, value) in deliveries) Assert.True(rig.Deliver(key, value));
        return rig.Speech.All;
    }

    [Fact]
    public void Asa_phrases()
    {
        // The upper ASA: 0 LAND 3, 2 LAND 2, 1 blank (silent); the first reading is a baseline.
        Assert.Equal(new[] { "LAND 3", "LAND 2", "LAND 3" }, Said(
            ("BBS757_ASA_UPPER", 1), ("BBS757_ASA_UPPER", 0), ("BBS757_ASA_UPPER", 2),
            ("BBS757_ASA_UPPER", 1), ("BBS757_ASA_UPPER", 0)));
        Assert.Equal(new[] { "NO LAND 3", "NO AUTOLAND" }, Said(
            ("BBS757_ASA_LOWER", 1), ("BBS757_ASA_LOWER", 0), ("BBS757_ASA_LOWER", 1), ("BBS757_ASA_LOWER", 2)));
        Assert.Empty(Said(("BBS757_ASA_UPPER", 0)));   // LAND 3 as found: no change
    }

    [Theory]
    [InlineData("BBS757_ALT_ALERT", "Altitude alert")]
    [InlineData("BBS757_CONFIG_WARNING", "Configuration warning")]
    public void Alerts_speak_when_they_light(string key, string said)
    {
        Assert.Equal(new[] { said, said }, Said((key, 0), (key, 1), (key, 0), (key, 1)));
        Assert.Empty(Said((key, 1), (key, 0)));   // lit as found, then out: silent
    }

    [Theory]
    [InlineData("L")]
    [InlineData("C")]
    [InlineData("R")]
    public void Irs_aligned_only_in_nav(string irs)
    {
        string light = $"BBS757_IRS_{irs}_ALIGN", sw = $"BBS757_IRS_{irs}_SWITCH_WATCH";

        // OFF to NAV: the ALIGN light comes on, then goes out at NAV: aligned.
        Assert.Equal(new[] { $"IRS {irs} aligned" }, Said((sw, 0), (light, 0), (sw, 2), (light, 1), (light, 0)));

        // Switched off while aligning: the light goes out too, and nothing is said.
        Assert.Empty(Said((sw, 1), (light, 0), (light, 1), (sw, 0), (light, 0)));
        Assert.Empty(Said((sw, 2), (light, 1), (sw, 0), (light, 0)));

        // The switch row itself is never spoken.
        Assert.Empty(Said((sw, 0), (sw, 1), (sw, 2), (sw, 3)));
    }

    [Theory]
    [InlineData("BBS757_DOOR_L1", 100, "Door L1")]
    [InlineData("BBS757_DOOR_R3", 100, "Door R3")]
    [InlineData("BBS757_DOOR_CARGO_FWD", 200, "Forward cargo door")]
    [InlineData("BBS757_DOOR_CARGO_AFT", 200, "Aft cargo door")]
    public void Door_ends_only(string key, double full, string name)
    {
        // Animating through the middle speaks only at the ends.
        Assert.Equal(new[] { $"{name} open", $"{name} closed" }, Said(
            (key, 0), (key, full * 0.3), (key, full * 0.6), (key, full * 0.95), (key, full * 0.5),
            (key, full), (key, full * 0.2), (key, full * 0.02)));
    }

    [Fact]
    public void Start_valves_open_and_close()
    {
        Assert.Equal(new[] { "Left start valve open", "Left start valve closed" },
            Said(("BBS757_L_START_VALVE", 0), ("BBS757_L_START_VALVE", 1), ("BBS757_L_START_VALVE", 0)));
        Assert.Equal(new[] { "Right start valve open" },
            Said(("BBS757_R_START_VALVE", 0), ("BBS757_R_START_VALVE", 1)));
    }

    public static TheoryData<string, string, string, string?> Task4Rows => new()
    {
        { "BBS757_ASA_UPPER", "B757_MAIN_L_AUTOLAND_1_BUTTON_ON", "Autoland Status Upper", "Status" },
        { "BBS757_ASA_LOWER", "B757_MAIN_L_AUTOLAND_2_BUTTON_ON", "Autoland Status Lower", "Status" },
        { "BBS757_ALT_ALERT", "BBS757_CA_ALTIMETER_ALT_ALERT_LIGHT", "Altitude Alert", null },
        { "BBS757_CONFIG_WARNING", "B757_MAIN_CONFIG_1_DECAL", "Configuration Warning", null },
        { "BBS757_IRS_L_ALIGN", "B757_OVHD_IRS_L_ALIGN_DECAL", "IRS L Aligned", null },
        { "BBS757_IRS_C_ALIGN", "B757_OVHD_IRS_C_ALIGN_DECAL", "IRS C Aligned", null },
        { "BBS757_IRS_R_ALIGN", "B757_OVHD_IRS_R_ALIGN_DECAL", "IRS R Aligned", null },
        { "BBS757_L_START_VALVE", "B757_OVHD_L_ENG_START_VALVE_DECAL", "Left Start Valve", "Engines" },
        { "BBS757_R_START_VALVE", "B757_OVHD_R_ENG_START_VALVE_DECAL", "Right Start Valve", "Engines" },
        { "BBS757_DOOR_L1", "B757_DOORS_PAX_L1", "Door L1", null },
        { "BBS757_DOOR_L2", "B757_DOORS_PAX_L2", "Door L2", null },
        { "BBS757_DOOR_L3", "B757_DOORS_PAX_L3", "Door L3", null },
        { "BBS757_DOOR_R1", "B757_DOORS_PAX_R1", "Door R1", null },
        { "BBS757_DOOR_R2", "B757_DOORS_PAX_R2", "Door R2", null },
        { "BBS757_DOOR_R3", "B757_DOORS_PAX_R3", "Door R3", null },
        { "BBS757_DOOR_CARGO_FWD", "B757_DOORS_CARGO_FWD", "Forward Cargo Door", null },
        { "BBS757_DOOR_CARGO_AFT", "B757_DOORS_CARGO_AFT", "Aft Cargo Door", null },
    };

    [Theory]
    [MemberData(nameof(Task4Rows))]
    public void Rows_are_announced_and_muteable(string key, string lvar, string label, string? panel)
    {
        var def = new Rig(Bbs757ControlMap.Parse("""
            {
              "controls": [
                { "node": "B757_A", "section": "Pedestal", "panel": "Engines", "label": "A", "kind": "toggle", "state_var": "B757_A" },
                { "node": "B757_B", "section": "Overhead", "panel": "Electrical", "label": "B", "kind": "toggle", "state_var": "B757_B" }
              ]
            }
            """)).Def;
        var row = def.GetVariables()[key];

        Assert.Equal(lvar, row.Name);
        Assert.Equal(label, row.DisplayName);
        Assert.Equal(SimVarType.LVar, row.Type);
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);
        Assert.Contains(def.MonitorRows(), r => r.Key == key && r.Label == label);
        Assert.False(def.IsMuteWrapExempt(key));   // MainForm's Ctrl+M wrap mutes it ([VAR-8])

        var shownOn = def.GetPanelDisplayVariables().Where(p => p.Value.Contains(key)).Select(p => p.Key).ToList();
        Assert.Equal(panel == null ? Array.Empty<string>() : new[] { panel }, shownOn);
        Assert.Equal(panel != null, row.RenderAsReadOnlyStatus);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("C")]
    [InlineData("R")]
    public void Irs_switch_watches_are_silent_rows(string irs)
    {
        var def = new BBS757Definition { NestRunning = () => true };
        string key = $"BBS757_IRS_{irs}_SWITCH_WATCH";
        var row = def.GetVariables()[key];

        Assert.Equal($"B757_IRS_{irs}_SWITCH", row.Name);
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.ExcludeFromMonitorManager);   // it speaks nothing: no Ctrl+M row ([VAR-9])
        Assert.DoesNotContain(def.MonitorRows(), r => r.Key == key);
        Assert.DoesNotContain(key, def.GetPanelDisplayVariables().SelectMany(p => p.Value));
    }

    [Fact]
    public void Variable_ceiling_holds()
    {
        var vars = new BBS757Definition { NestRunning = () => true }.GetVariables();
        Assert.True(vars.Count < 900, $"{vars.Count} variables");
        // Every row of the plan registered, each under its own key.
        Assert.All(Bbs757Annunciators.Rows, r => Assert.Equal(r.LVar, vars[r.Key].Name));
        Assert.Equal(22, Bbs757Annunciators.Rows.Count);
    }

    [Fact]
    public void Every_row_speaks_the_same_under_a_comma_decimal_culture()
    {
        double[] values = { 0, 0.3, 0.5, 0.7, 1, 2, 5, 50, 90, 95, 100, 150, 180, 200, double.NaN };
        double?[] related = { null, 0, 1, 2 };

        List<string?> Phrases()
            => Bbs757Annunciators.Rows.SelectMany(r => values.SelectMany(v => related.Select(c => r.Phrase(v, c)))).ToList();

        List<string?> expected = new();
        InCulture("", () => expected = Phrases());
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(expected, Phrases()));
    }
}
