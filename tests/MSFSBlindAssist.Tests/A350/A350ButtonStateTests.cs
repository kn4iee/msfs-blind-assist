using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Every button row whose state the cockpit shows reads it on its label (the owner's rule, 2026-10-27): its
/// lit legends, or the state variable it switches.</summary>
public class A350ButtonStateTests
{
    private readonly Dictionary<string, double> _cache = new();
    private readonly IniBuildsA350Definition _def;

    public A350ButtonStateTests()
    {
        _def = new IniBuildsA350Definition();
        _def.Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null;
        _def.Attach(new SimConnectManager(IntPtr.Zero));
        _cache[Aircraft.A350.A350PanelLamps.AcPowerKey] = 1;
        _cache[Aircraft.A350.A350PanelLamps.DcPowerKey] = 1;
    }

    private string? State(string row) => _def.TryDescribeControlState(row, out var text) ? text : null;

    private void Lamps(params (string Key, double Value)[] lamps)
    {
        foreach (var (key, value) in lamps)
            _cache[key] = value;
    }

    /// <summary>Sets the variables the lights' rules read, by name, under every key that streams them (a light's first
    /// variable streams under the light's own key as well).</summary>
    private void Inputs(params (string Var, double Value)[] inputs)
    {
        foreach (var (name, value) in inputs)
            foreach (var (key, def) in _def.GetVariables())
                if (string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase))
                    _cache[key] = value;
    }

    [Fact]
    public void A_button_with_two_legends_names_the_lit_ones()
    {
        // EXT 1: AVAIL while the GPU is available and not online; ON while it is.
        Inputs(("INI_GPU_AVAIL", 1), ("INI_GEN_EXT_A_ONLINE", 0), ("INI_GPU1_SWITCH", 0));
        Assert.Equal("Available", State("A350_ELEC_EXT_1"));
        Inputs(("INI_GPU_AVAIL", 0));
        Assert.Equal("Off", State("A350_ELEC_EXT_1"));
        Inputs(("INI_APU_SQUIB", 1), ("INI_FIRE_TEST", 0), ("INI_FIRE_APU_FIRE_PB", 0), ("INI_FIRE_APU_AGENT_DISCH", 1));
        Assert.Equal("Squib and discharged", State("A350_FIRE_APU_AGENT"));
    }

    [Fact]
    public void A_master_light_reads_on_while_either_half_is_lit()
    {
        Lamps(("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT", 0), ("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ2_LIGHT", 1));
        Assert.Equal("On", State("A350_FCU_MASTER_CAUTION_LEFT"));
    }

    [Fact]
    public void A_supply_buttons_lower_legend_is_its_shut_valve()
    {
        Lamps(("A350_LT_HYD_SUPPLY_ENG_1_GREEN_SEQ1_LIGHT", 0), ("A350_LT_HYD_SUPPLY_ENG_1_GREEN_SEQ2_LIGHT", 1));
        Assert.Equal("Shut", State("A350_HYD_SUPPLY_ENG_1_GREEN"));
    }

    [Theory]
    [InlineData("A350_FCU_SPD_MACH", "INI_AIRSPEED_IS_MACH", 1, "Mach")]
    [InlineData("A350_FCU_SPD_MACH", "INI_AIRSPEED_IS_MACH", 0, "Knots")]
    [InlineData("A350_FCU_TRK_FPA", "INI_TRACK_FPA_STATE", 1, "TRK-FPA")]
    [InlineData("A350_FCU_TRK_FPA", "INI_TRACK_FPA_STATE", 0, "HDG-V/S")]
    [InlineData("A350_EFIS_QNH_UNIT_SELECTOR_LEFT", "XMLVAR_Baro_Selector_HPA_1", 1, "Hectopascals")]
    [InlineData("A350_EFIS_QNH_UNIT_SELECTOR_RIGHT", "XMLVAR_Baro_Selector_HPA_2", 0, "Inches")]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH", "XMLVAR_Baro1_Mode", 3, "STD")]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", "XMLVAR_Baro1_Mode", 0, "QNH")]
    [InlineData("A350_FCU_SPD_KNOB_PULL", "INI_MANAGED_SPEED", 1, "Managed")]
    [InlineData("A350_FCU_SPD_KNOB_PUSH", "INI_MANAGED_SPEED", 0, "Selected")]
    [InlineData("A350_FCU_ALT_KNOB_PUSH", "INI_FCU_ALT_DOT", 1, "Managed")]
    [InlineData("A350_FCU_VS_KNOB_PULL", "INI_PITCH_MODE", 8, "Engaged")]
    [InlineData("A350_FCU_VS_KNOB_PUSH", "INI_PITCH_MODE", 12, "Not engaged")]
    // Live 2026-10-28: with INI_ALTITUDE_STATE 1 one knob click took 100 to 1000, with 0 it took 1000 to 1100. The
    // selector's own position is a local object variable; its press only sends INI_FCU_ALTITUDE_MODE_COMMAND.
    [InlineData("A350_FCU_ALT_SELECTOR", "INI_ALTITUDE_STATE", 1, "1000 feet")]
    [InlineData("A350_FCU_ALT_SELECTOR", "INI_ALTITUDE_STATE", 0, "100 feet")]
    public void A_button_with_a_state_variable_reads_it(string row, string var, double value, string expected)
    {
        _cache[_def.KeyForInput(var)] = value;
        Assert.Equal(expected, State(row));
        Assert.Contains(_def.KeyForInput(var), _def.GetVariables()[row].StateVariables!);   // the label repaints with it
    }

    // The heading knob reads managed while NAV is engaged (INI_ROLL_MODE 1, even with a heading preset in the window
    // during a NAV turn, dot 0) or armed (the window's managed dot, INI_FCU_HDG_DOT: on the ground before takeoff the
    // FCU shows dashes and the dot while no lateral mode is engaged yet, roll mode 0). Live 2026-10-28: on the stand
    // with NAV armed it read "Selected"; in flight a pull gave roll mode 2 and dot 0, a push roll mode 1 and dot 1.
    [Theory]
    [InlineData(1, 0, "Managed")]    // NAV engaged, a heading preset in the window
    [InlineData(1, 1, "Managed")]    // NAV engaged
    [InlineData(2, 0, "Selected")]   // HDG
    [InlineData(0, 1, "Managed")]    // on the ground, NAV armed
    [InlineData(0, 0, "Selected")]   // on the ground, a heading selected
    public void The_heading_knob_reads_nav_engaged_or_armed_as_managed(double rollMode, double dot, string expected)
    {
        _cache[_def.KeyForInput("INI_ROLL_MODE")] = rollMode;
        _cache[_def.KeyForInput("INI_FCU_HDG_DOT")] = dot;
        Assert.Equal(expected, State("A350_FCU_HDG_KNOB_PUSH"));
        Assert.Equal(expected, State("A350_FCU_HDG_KNOB_PULL"));
        var repaint = _def.GetVariables()["A350_FCU_HDG_KNOB_PUSH"].StateVariables!;
        Assert.Contains(_def.KeyForInput("INI_ROLL_MODE"), repaint);
        Assert.Contains(_def.KeyForInput("INI_FCU_HDG_DOT"), repaint);
    }

    [Fact]
    public void A_momentary_button_with_nothing_to_show_reads_nothing()
    {
        Assert.Null(State("A350_CALLS_FWD"));
        Assert.Null(State("A350_FCU_CHRONO_LEFT"));
    }

    // A button the pilot presses and holds reads the cockpit's own record of it held: the L:var its Set writes while the
    // press value is 2 (A350WritePlan.HeldVarFor). Review 2026-10-28: a press row is never delivered (UpdateFrequency
    // Never), so a label composed from MSFSBA's belief never repainted after Space; this variable streams, and its
    // delivery repaints the label, after a flight reload too.
    [Theory]
    [InlineData("A350_FIRE_TEST", "INI_FIRE_TEST")]
    [InlineData("A350_ECAM_TO_CONFIG", "PUSH_ECAM_TOCONFIG")]
    [InlineData("A350_PARK_BRK_ACCU_REINFLATE", "INI_ACCU_REINFLATE")]
    [InlineData("A350_OXYTEST_CPT_PANEL", "INI_OXYTEST_BLINKER_CPT")]
    [InlineData("A350_OXYTEST_FO_PANEL", "INI_OXYTEST_BLINKER_FO")]
    [InlineData("A350_OXYTEST_TO_PANEL", "INI_OXYTEST_BLINKER_TO")]
    [InlineData("A350_OXYTEST_4TH_PANEL", "INI_OXYTEST_BLINKER_4TH")]
    [InlineData("A350_OXYTEST_CPT_MASK", "INI_OXYTEST_EMER_CPT")]
    [InlineData("A350_OXYTEST_FO_MASK", "INI_OXYTEST_EMER_FO")]
    [InlineData("A350_OXYTEST_TO_MASK", "INI_OXYTEST_EMER_TO")]
    [InlineData("A350_OXYTEST_4TH_MASK", "INI_OXYTEST_EMER_4TH")]
    [InlineData("A350_CVR_TEST", "INI_CVR_TEST")]
    [InlineData("A350_CVR_ERASE", "INI_CVR_ERASE")]
    [InlineData("A350_CALLS_MECH", "INI_CALLS_MECH")]
    [InlineData("A350_RMP_PA_CALL_1", "INI_RMP1_PA_CHANNEL")]
    [InlineData("A350_RMP_PA_CALL_2", "INI_RMP2_PA_CHANNEL")]
    [InlineData("A350_RMP_PA_CALL_3", "INI_RMP3_PA_CHANNEL")]
    public void A_held_button_reads_the_cockpit_s_record_of_it_held(string row, string heldVar)
    {
        string key = _def.KeyForInput(heldVar);
        Assert.Equal(UpdateFrequency.Continuous, _def.GetVariables()[key].UpdateFrequency);   // delivered
        Assert.Contains(key, _def.GetVariables()[row].StateVariables!);                      // and it repaints the label
        Inputs((heldVar, 1));
        Assert.StartsWith("Pressed", State(row));
        Inputs((heldVar, 0));
        Assert.StartsWith("Not pressed", State(row));
    }

    [Fact]
    public void A_held_button_reads_nothing_while_the_cockpit_s_record_is_unread() =>
        Assert.Null(State("A350_FIRE_TEST"));

    [Fact]
    public void A_held_PA_key_reads_its_microphone_light_too()
    {
        // Review 2026-10-28: the label said only Pressed, and the light's own change is quieted after the pilot's press
        // (its label says it), so the PA microphone light was on neither.
        var light = _def.GetVariables().Single(v => v.Value.DisplayName == "Captain PA microphone light");
        var repaint = _def.GetVariables()["A350_RMP_PA_CALL_1"].StateVariables!;
        Assert.All(light.Value.StateVariables!, input => Assert.Contains(input, repaint));   // the light repaints it too
        Inputs(("INI_RMP1_PA_CHANNEL", 1));
        Assert.Equal("Pressed, light on", State("A350_RMP_PA_CALL_1"));
        Inputs(("INI_RMP1_PA_CHANNEL", 0));
        Assert.Equal("Not pressed, light off", State("A350_RMP_PA_CALL_1"));
    }
}
