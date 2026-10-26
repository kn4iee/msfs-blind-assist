using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The A350 definition: its panels and variables from the placement, and its writes through the plans.</summary>
public class IniBuildsA350DefinitionTests
{
    private readonly IniBuildsA350Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();

    public IniBuildsA350DefinitionTests()
    {
        _def = new IniBuildsA350Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,   // a hold never ends inside a test
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
        };
        _def.Attach(_sim);
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    [Fact]
    public void It_is_the_iniBuilds_A350()
    {
        Assert.Equal("INI_A350", _def.AircraftCode);
        Assert.Equal("iniBuilds Airbus A350", _def.AircraftName);
    }

    [Fact]
    public void The_overhead_section_lists_the_placed_panels()
    {
        var placement = A350PanelLayout.Place(A350ControlMap.Load());
        Assert.Equal(placement.Structure["Overhead"], _def.GetPanelStructure()["Overhead"]);
    }

    [Fact]
    public void Stock_com_tuning_is_refused_as_the_aircraft_has_no_stock_radio()
    {
        // Live 2026-10-26: COM AVAILABLE:1 reads 0 and COM_STBY_RADIO_SET_HZ left COM 1 at 0.
        Assert.Contains("radio management panel", _def.StockComTuningRefusal);
    }

    [Fact]
    public void Only_the_curated_sections_are_offered()
    {
        Assert.Equal(new[] { "Overhead", "Glareshield", "Instrument" }, _def.GetPanelStructure().Keys);
        Assert.Contains("A350_EFIS_RANGE_LEFT", _def.GetVariables().Keys);
        Assert.Contains("A350_LDG_LEVER", _def.GetVariables().Keys);
        Assert.DoesNotContain("A350_PARK_BRK_PARKING_BRAKE", _def.GetVariables().Keys);   // the pedestal: not yet
    }

    private sealed class WithSections : IniBuildsA350Definition
    {
        public WithSections(params string[] sections) : base(sections) { }
    }

    [Fact]
    public void A_harness_can_offer_a_section_still_being_curated()
    {
        var def = new WithSections("Overhead", "Glareshield");
        Assert.Equal(new[] { "Overhead", "Glareshield" }, def.GetPanelStructure().Keys);
        Assert.Contains("FCU", def.GetPanelControls().Keys);
    }

    [Fact]
    public void The_fcu_windows_are_status_lines_on_the_fcu_panel()
    {
        var def = new WithSections("Glareshield");
        def.Attach(_sim);
        def.Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null;
        Assert.Contains("A350_FCU_ALTITUDE_WINDOW", def.GetPanelDisplayVariables()["FCU"]);
        Assert.Equal("INI_ALTITUDE_DIAL", def.GetVariables()["A350_FCU_ALTITUDE_WINDOW"].Name);
        _cache["A350_FCU_ALTITUDE_WINDOW"] = 12000;
        _cache["A350_IN_INI_FCU_ALT_DOT"] = 1;
        Assert.True(def.TryGetDisplayOverride("A350_FCU_ALTITUDE_WINDOW", 12000, out var text));
        Assert.Equal("12000 feet, managed", text);
    }

    [Fact]
    public void A_button_lit_by_its_own_light_reads_that_light()
    {
        var def = new WithSections("Glareshield");
        def.Attach(_sim);
        def.Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null;
        const string Cstr = "A350_EFIS_TSK_1_LEFT", Lamp = "A350_LT_EFIS_TSK_1_LEFT_SEQ1_LIGHT";
        Assert.Contains(Lamp, def.GetVariables()[Cstr].StateVariables!);
        // (L:INI_FMS_SELECTOR) 0 == (L:INI_FCU_BACKUP_FMS1) 0 == or if{ 0 sp0 } els{ (L:INI_SHOW_CONSTRAINTS) sp0 } l0
        _cache[Lamp] = 1;   // the rule's first input, INI_FMS_SELECTOR: 1 is the normal FMS
        _cache["A350_IN_INI_FCU_BACKUP_FMS1"] = 1;
        _cache["A350_IN_INI_SHOW_CONSTRAINTS"] = 1;
        _cache[A350PanelLamps.DcPowerKey] = 1;
        _cache[A350PanelLamps.AcPowerKey] = 1;
        Assert.True(def.TryDescribeControlState(Cstr, out var state));
        Assert.Equal("On", state);
    }

    [Fact]
    public void The_baro_line_replaces_the_baro_knobs_value_line()
    {
        var def = new WithSections("Glareshield");
        var lines = def.GetPanelDisplayVariables()["EFIS Captain"];
        Assert.Contains("A350_FCU_CAPTAIN_BARO", lines);
        Assert.DoesNotContain("A350_VAL_EFIS_QNH_PRESSURE_SELECTOR_LEFT", lines);   // its map state is the unit flag
        var baro = def.GetVariables()["A350_FCU_CAPTAIN_BARO"];
        Assert.Equal("KOHLSMAN SETTING MB:1", baro.Name);
        Assert.Equal(SimVarType.SimVar, baro.Type);
        Assert.Equal("millibars", baro.Units);
    }

    [Fact]
    public void The_fcu_windows_are_absent_while_the_glareshield_is_not_offered() =>
        Assert.DoesNotContain("A350_FCU_ALTITUDE_WINDOW", new WithSections("Overhead").GetVariables().Keys);

    [Fact]
    public void Every_overhead_row_is_a_registered_variable_on_its_panel()
    {
        var placement = A350PanelLayout.Place(A350ControlMap.Load());
        var vars = _def.GetVariables();
        var controls = _def.GetPanelControls();
        foreach (var panel in placement.Structure["Overhead"])
            foreach (var row in placement.RowsByPanel[panel])
            {
                Assert.True(vars.ContainsKey(row.Key), row.Key);
                Assert.Contains(row.Key, controls[panel]);
            }
    }

    [Fact]
    public void A_switch_row_reads_its_state_variable_with_its_position_words()
    {
        var bat = _def.GetVariables()["A350_ELEC_BAT_1"];
        Assert.Equal("INI_BATTERY_1_SWITCH", bat.Name);
        Assert.Equal(SimVarType.LVar, bat.Type);
        Assert.Equal(UpdateFrequency.Continuous, bat.UpdateFrequency);
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, bat.ValueDescriptions);
    }

    [Fact]
    public void A_button_row_is_a_button()
    {
        Assert.True(_def.GetVariables()["A350_APU_START"].RenderAsButton);
    }

    [Fact]
    public void A_pick_of_a_switch_elsewhere_fires_its_own_set_event()
    {
        _cache["A350_ELEC_BAT_1"] = 0;
        Assert.True(Set("A350_ELEC_BAT_1", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_ELEC_BAT_1_Set)" }, _sent);
    }

    [Fact]
    public void A_pick_of_an_unread_switch_is_refused_aloud_and_sends_nothing()
    {
        Assert.True(Set("A350_ELEC_BAT_1", 1));
        Assert.Empty(_sent);
        Assert.Equal(new[] { A350WritePlan.UnknownPositionRefusal }, _speech.All);
    }

    [Fact]
    public void A_second_pick_inside_the_cache_lag_plans_from_the_first()
    {
        _cache["A350_ELEC_BAT_1"] = 0;
        Set("A350_ELEC_BAT_1", 1);
        Set("A350_ELEC_BAT_1", 1);   // the cache still says 0: without the hold this would flip it back
        Assert.Single(_sent);
    }

    [Fact]
    public void A_press_row_presses()
    {
        Set("A350_APU_START", 1);
        Assert.Equal(new[] { "1 (>B:AIRLINER_APU_START_Set)" }, _sent);
    }

    [Fact]
    public void Disposing_lets_go_of_a_button_still_held()
    {
        Set("A350_FIRE_TEST", 1);   // its 3 s hold never ends inside a test
        Assert.Equal(new[] { "2 (>B:AIRLINER_FIRE_TEST_Set)" }, _sent);
        _def.Dispose();
        Assert.Equal(new[] { "2 (>B:AIRLINER_FIRE_TEST_Set)", "0 (>B:AIRLINER_FIRE_TEST_Set)" }, _sent);
    }

    [Fact]
    public void A_write_that_cannot_land_is_refused_aloud()
    {
        _def.CanLand = _ => false;
        _cache["A350_ELEC_BAT_1"] = 0;
        Set("A350_ELEC_BAT_1", 1);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "BAT 1 unavailable" }, _speech.All);
    }

    private const string BatFault = "A350_LT_ELEC_BAT_1_SEQ1_LIGHT";

    [Fact]
    public void A_panels_status_display_lists_its_lights()
    {
        var displays = _def.GetPanelDisplayVariables();
        Assert.Contains(BatFault, displays["ELEC"]);
        Assert.DoesNotContain("A350_LT_ELEC_BAT_1_SEQ2_LIGHT", displays["ELEC"]);   // the switch's own OFF legend
    }

    [Fact]
    public void A_light_streams_its_rules_first_variable_and_repaints_on_its_bus_power()
    {
        var vars = _def.GetVariables();
        var fault = vars[BatFault];
        Assert.Equal("INI_BATTERY_1_FAULT", fault.Name);
        Assert.Equal("BAT 1 fault light", fault.DisplayName);
        Assert.True(fault.RenderAsReadOnlyStatus);
        Assert.Contains(A350PanelLamps.DcPowerKey, fault.StateVariables!);
        Assert.Equal(A350PanelLamps.DcPowerVar, vars[A350PanelLamps.DcPowerKey].Name);
        Assert.Equal("INI_IRS1_STATE", vars["A350_IN_INI_IRS1_STATE"].Name);
    }

    [Fact]
    public void A_light_reads_on_only_while_its_bus_lights_it()
    {
        _cache[BatFault] = 1;
        _cache[A350PanelLamps.DcPowerKey] = 1;
        Assert.True(_def.TryGetDisplayOverride(BatFault, 1, out var lit));
        Assert.Equal("On", lit);
        _cache[A350PanelLamps.DcPowerKey] = 0;
        Assert.True(_def.TryGetDisplayOverride(BatFault, 1, out var dark));
        Assert.Equal("Off", dark);
    }

    [Fact]
    public void A_light_with_an_unread_input_is_not_composed()
    {
        _cache[BatFault] = 1;   // its bus's light power still unread: the iniBuilds A300's choice, never a guessed Off
        Assert.False(_def.TryGetDisplayOverride(BatFault, 1, out _));
    }

    [Fact]
    public void A_light_delivery_is_consumed_silently()
    {
        Assert.True(_def.ProcessSimVarUpdate(BatFault, 1, _speech));
        Assert.True(_def.ProcessSimVarUpdate(A350PanelLamps.DcPowerKey, 0, _speech));
        Assert.Empty(_speech.All);
    }

    private const string JettisonOption = "A350_OPT_INI_OPTION_FUEL_JETTISON";

    [Fact]
    public void An_option_the_aircraft_is_built_without_refuses_its_controls_aloud()
    {
        _cache[JettisonOption] = 0;
        _cache["A350_JETTISON_ARM"] = 0;
        Assert.True(Set("A350_JETTISON_ARM", 1));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Fuel jettison is not fitted. It is an option on the OIS options page." }, _speech.All);
    }

    [Fact]
    public void A_fitted_option_lets_its_controls_through()
    {
        _cache[JettisonOption] = 1;
        _cache["A350_JETTISON_ARM"] = 0;
        Set("A350_JETTISON_ARM", 1);
        Assert.Equal(new[] { "1 (>B:AIRLINER_JETTISON_ARM_Set)" }, _sent);
    }

    [Fact]
    public void An_options_panel_shows_whether_it_is_fitted()
    {
        Assert.Contains(JettisonOption, _def.GetPanelDisplayVariables()["Fuel"]);
        var option = _def.GetVariables()[JettisonOption];
        Assert.Equal("INI_OPTION_FUEL_JETTISON", option.Name);
        Assert.Equal("Fuel jettison option", option.DisplayName);
        Assert.Equal(new Dictionary<double, string> { [0] = "Not fitted", [1] = "Fitted" }, option.ValueDescriptions);
    }

    private const string CabinAltitudeTarget = "A350_VAL_CABIN_PRESS_ALTITUDE_TARGET";

    [Fact]
    public void An_encoders_value_is_a_status_line_on_its_panel()
    {
        Assert.Contains(CabinAltitudeTarget, _def.GetPanelDisplayVariables()["Pressurization"]);
        var value = _def.GetVariables()[CabinAltitudeTarget];
        Assert.Equal("INI_CABIN_PRESS_ALTITUDE_TARGET", value.Name);
        Assert.Equal("Cabin altitude target", value.DisplayName);
    }

    [Fact]
    public void An_encoders_value_reads_with_its_unit()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.True(_def.TryGetDisplayOverride(CabinAltitudeTarget, 2200, out var text));
            Assert.Equal("2200 feet", text);
            Assert.True(_def.TryGetDisplayOverride("A350_VAL_CABIN_PRESS_VS_TARGET", -350.4, out var vs));
            Assert.Equal("-350 feet per minute", vs);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_typed_row_is_a_text_field_that_refuses_what_is_not_a_number()
    {
        var def = _def.GetVariables()["A350_CABIN_ALT_TARGET_SET"];
        Assert.True(def.UnparseableTextAsNaN);
        Assert.Equal(UpdateFrequency.Never, def.UpdateFrequency);
        Assert.False(def.RenderAsButton);
        Assert.Empty(def.ValueDescriptions);
        Assert.Equal("Cabin altitude target (feet)", def.DisplayName);
        Assert.Contains("A350_CABIN_ALT_TARGET_SET", _def.GetPanelControls()["Pressurization"]);
    }

    [Fact]
    public void A_typed_value_that_is_refused_is_said_and_sends_nothing()
    {
        Assert.True(Set("A350_CABIN_ALT_TARGET_SET", 20000));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Cabin altitude target must be between -2000 and 13500 feet" }, _speech.All);
    }

    [Fact]
    public void A_typed_value_is_written_short_and_clicked_to()
    {
        _cache[_def.KeyForInput("INI_CABIN_PRESS_ALTITUDE_TARGET")] = 0;
        Assert.True(Set("A350_CABIN_ALT_TARGET_SET", 3000));
        Assert.Equal(new[] { "2900 (>L:INI_CABIN_PRESS_ALTITUDE_TARGET)" }, _sent);   // the click waits on the delay
    }

    [Fact]
    public void The_calculator_path_probe_is_registered()
    {
        Assert.True(_def.GetVariables().ContainsKey("MSFSBA_BRIDGE_PROBE"));
    }

    [Fact]
    public void A_base_variable_is_left_to_mainform()
    {
        Assert.False(_def.HandleUIVariableSet("SIM_ON_GROUND", 1, new SimVarDefinition(), _sim, _speech));
    }
}

public class IniBuildsA350ReadBackTests
{
    private sealed class Glareshield : IniBuildsA350Definition
    {
        public Glareshield() : base(new[] { "Glareshield" }) { }
    }

    private static (IniBuildsA350Definition Def, List<string> Sent, SpeechCapture Speech, Queue<double?> ReadBacks) Typed(
        Dictionary<string, double> cache)
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var readBacks = new Queue<double?>();
        var def = new Glareshield
        {
            CanLand = _ => true,
            Send = (_, rpn) => sent.Add(rpn),
            ReRead = (_, _) => { },
            Delay = _ => Task.CompletedTask,
            ReadFresh = (_, _, _) => Task.FromResult(readBacks.Count > 0 ? readBacks.Dequeue() : null),
        };
        def.Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null;
        return (def, sent, speech, readBacks);
    }

    private static Dictionary<string, double> SelectedSpeed(IniBuildsA350Definition def) => new()
    {
        [def.KeyForInput("INI_AIRSPEED_DIAL")] = 200,
        [def.KeyForInput("INI_AIRSPEED_IS_MACH")] = 0,
        [def.KeyForInput("INI_MANAGED_SPEED")] = 0,
        [def.KeyForInput("INI_FCU_SPD_DOT")] = 0,
    };

    [Fact]
    public async Task A_typed_speed_the_click_lands_is_confirmed_as_the_fcu_shows_it()
    {
        var cache = new Dictionary<string, double>();
        var (def, sent, speech, readBacks) = Typed(cache);
        foreach (var (k, v) in SelectedSpeed(def)) cache[k] = v;
        readBacks.Enqueue(250);
        def.HandleUIVariableSet("A350_FCU_SPEED_SET", 250, def.GetVariables()["A350_FCU_SPEED_SET"], new SimConnectManager(IntPtr.Zero), speech);
        await Task.Yield();
        Assert.Equal(new[] { "249 (>L:INI_AIRSPEED_DIAL)", "1 (>B:AIRLINER_FCU_SPD_KNOB_Set)" }, sent);
        Assert.Equal(new[] { "FCU speed 250, selected" }, speech.All);
    }

    [Fact]
    public async Task A_typed_value_the_click_misses_is_corrected_then_confirmed()
    {
        var cache = new Dictionary<string, double>();
        var (def, sent, speech, readBacks) = Typed(cache);
        cache[def.KeyForInput("INI_ALTITUDE_DIAL")] = 10000;
        cache[def.KeyForInput("INI_FCU_ALT_DOT")] = 0;
        readBacks.Enqueue(13000);   // the 1000 ft increment: 11900 + a click rounds to 13000
        readBacks.Enqueue(12000);
        def.HandleUIVariableSet("A350_FCU_ALTITUDE_SET", 12000, def.GetVariables()["A350_FCU_ALTITUDE_SET"], new SimConnectManager(IntPtr.Zero), speech);
        await Task.Yield();
        Assert.Equal(new[] { "11900 (>L:INI_ALTITUDE_DIAL)", "1 (>B:AIRLINER_FCU_ALT_KNOB_Set)", "12000 (>L:INI_ALTITUDE_DIAL)" }, sent);
        Assert.Equal(new[] { "FCU altitude 12000, selected" }, speech.All);
    }

    [Fact]
    public async Task A_typed_value_the_aircraft_will_not_take_says_what_it_shows()
    {
        var cache = new Dictionary<string, double>();
        var (def, sent, speech, readBacks) = Typed(cache);
        foreach (var (k, v) in SelectedSpeed(def)) cache[k] = v;
        readBacks.Enqueue(200);
        readBacks.Enqueue(200);
        def.HandleUIVariableSet("A350_FCU_SPEED_SET", 250, def.GetVariables()["A350_FCU_SPEED_SET"], new SimConnectManager(IntPtr.Zero), speech);
        await Task.Yield();
        Assert.Equal(new[] { "Speed did not take 250. FCU speed 200, selected" }, speech.All);
    }

    [Fact]
    public async Task A_switch_the_aircraft_did_not_move_is_said_once()
    {
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var readBack = new TaskCompletionSource<double?>();
        var def = new IniBuildsA350Definition
        {
            CanLand = _ => true,
            Cached = (_, _) => 0,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Delay = _ => Task.CompletedTask,
            ReadFresh = (_, _, _) => readBack.Task,
        };
        def.HandleUIVariableSet("A350_ELEC_BAT_1", 1, def.GetVariables()["A350_ELEC_BAT_1"], sim, speech);
        readBack.SetResult(0);   // still off
        await Task.Yield();
        Assert.Equal(new[] { "BAT 1 did not move" }, speech.All);
    }
}
