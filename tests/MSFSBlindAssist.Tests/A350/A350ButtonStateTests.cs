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
    [InlineData("A350_FCU_HDG_KNOB_PUSH", "INI_ROLL_MODE", 1, "Managed")]
    [InlineData("A350_FCU_HDG_KNOB_PULL", "INI_ROLL_MODE", 2, "Selected")]
    [InlineData("A350_FCU_ALT_KNOB_PUSH", "INI_FCU_ALT_DOT", 1, "Managed")]
    [InlineData("A350_FCU_VS_KNOB_PULL", "INI_PITCH_MODE", 8, "Engaged")]
    [InlineData("A350_FCU_VS_KNOB_PUSH", "INI_PITCH_MODE", 12, "Not engaged")]
    public void A_button_with_a_state_variable_reads_it(string row, string var, double value, string expected)
    {
        _cache[_def.KeyForInput(var)] = value;
        Assert.Equal(expected, State(row));
        Assert.Contains(_def.KeyForInput(var), _def.GetVariables()[row].StateVariables!);   // the label repaints with it
    }

    [Fact]
    public void A_momentary_button_with_nothing_to_show_reads_nothing()
    {
        Assert.Null(State("A350_CALLS_FWD"));
        Assert.Null(State("A350_FCU_CHRONO_LEFT"));
    }
}
