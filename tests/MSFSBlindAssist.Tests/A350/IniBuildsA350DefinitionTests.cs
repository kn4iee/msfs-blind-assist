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
        Assert.Equal(new[] { "Overhead" }, _def.GetPanelStructure().Keys);
        Assert.DoesNotContain("A350_EFIS_RANGE_LEFT", _def.GetVariables().Keys);
    }

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
    public void A_write_that_cannot_land_is_refused_aloud()
    {
        _def.CanLand = _ => false;
        _cache["A350_ELEC_BAT_1"] = 0;
        Set("A350_ELEC_BAT_1", 1);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "BAT 1 unavailable" }, _speech.All);
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
