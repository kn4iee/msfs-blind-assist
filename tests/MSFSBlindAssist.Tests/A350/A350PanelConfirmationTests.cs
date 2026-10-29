using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Review 2026-10-29: a press or a step was never confirmed (panels minor 3), while the A380, the A320 and MainForm's
/// generic button say "&lt;name&gt; pressed" for an action with no readable state ([CORE-7]); and the FCU altitude and baro
/// windows said nothing when changed elsewhere (parity task 9), as the A300 and the A380 say them.
/// </summary>
public class A350PanelConfirmationTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    public A350PanelConfirmationTests()
    {
        _def = new IniBuildsA350Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = key => _muted.Contains(key),
        };
        _def.Attach(_sim);
    }

    private static A350PlacedRow Row(string controlId, A350RowAction action) =>
        Placement.RowsByPanel.Values.SelectMany(r => r).First(r => r.Control?.Id == controlId && r.Action == action);

    private static A350FcuWindow Window(string name) => A350FcuWindows.All.Single(w => w.Name == name);

    /// <summary>The cache key each window input streams under: its first under the window's own key.</summary>
    private static string InputKey(A350FcuWindow window, string input) =>
        input == window.Inputs[0] ? window.Key : A350PanelLamps.InputKey(input);

    /// <summary>Delivers each named input of a window (cached first, as MainForm caches before it dispatches).</summary>
    private void Deliver(A350FcuWindow window, params (string Input, double Value)[] inputs)
    {
        foreach (var (input, value) in inputs)
            _cache[InputKey(window, input)] = value;
        foreach (var (input, value) in inputs)
            _def.ProcessSimVarUpdate(InputKey(window, input), value, _speech);
    }

    private void BatchAfter(int ms)
    {
        _now += ms;
        _def.OnContinuousBatchDelivered(0);
    }

    private void Press(A350PlacedRow row) =>
        _def.HandleUIVariableSet(row.Key, 1, _def.GetVariables()[row.Key], _sim, _speech);

    private static readonly (string, double)[] CaptainQnh =
    {
        ("KOHLSMAN SETTING MB:1", 1013.25), ("XMLVAR_Baro1_Mode", 0), ("XMLVAR_Baro_Selector_HPA_1", 1),
        ("INI_BARO_PRESET_CPT", 1013), ("INI_BARO_PRESET_CPT_SHOW", 0),
    };

    // ---- the windows that speak on their own ----

    [Fact]
    public void An_altitude_window_change_made_elsewhere_is_said_at_the_batch_end()
    {
        var alt = Window("Altitude window");
        Deliver(alt, ("INI_ALTITUDE_DIAL", 12000), ("INI_FCU_ALT_DOT", 0));
        BatchAfter(1000);
        Assert.Empty(_speech.All);   // the first values are a baseline
        Deliver(alt, ("INI_ALTITUDE_DIAL", 13000));
        Assert.Empty(_speech.All);   // staged: said once its batch has finished
        BatchAfter(1000);
        Assert.Equal(new[] { "Altitude 13000 feet" }, _speech.All);
    }

    [Fact]
    public void A_muted_window_says_nothing()
    {
        var alt = Window("Altitude window");
        _muted.Add(alt.Key);
        Deliver(alt, ("INI_ALTITUDE_DIAL", 12000), ("INI_FCU_ALT_DOT", 0));
        BatchAfter(1000);
        Deliver(alt, ("INI_ALTITUDE_DIAL", 13000));
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Each_speaking_window_has_its_ctrl_m_row_and_no_other_window_does()
    {
        var rows = MonitorRowBuilder.Build(_def.GetVariables()).ToList();
        foreach (var name in A350FcuCallouts.Speaking)
            Assert.Equal(name, Assert.Single(rows, r => r.Key == Window(name).Key).Label);
        foreach (var window in A350FcuWindows.All.Where(w => !A350FcuCallouts.Speaking.Contains(w.Name)))
            Assert.DoesNotContain(rows, r => r.Key == window.Key);
    }

    [Fact]
    public void A_baro_unit_changed_elsewhere_says_the_setting_in_the_new_unit()
    {
        var baro = Window("Captain baro");
        Deliver(baro, CaptainQnh);
        BatchAfter(1000);
        Deliver(baro, ("XMLVAR_Baro_Selector_HPA_1", 0));
        BatchAfter(1000);
        Assert.Equal(new[] { "Captain altimeter 29.92 inches" }, _speech.All);
    }

    // The A380 says nothing back for a unit the pilot picked: its control said it (here the unit button's label,
    // "Inches").
    [Fact]
    public void A_baro_unit_pressed_in_msfsba_is_not_said_back()
    {
        var baro = Window("Captain baro");
        Deliver(baro, CaptainQnh);
        BatchAfter(1000);
        Press(Row("AIRLINER_EFIS_QNH_UNIT_SELECTOR_LEFT", A350RowAction.Press));
        Deliver(baro, ("XMLVAR_Baro_Selector_HPA_1", 0));
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_baro_going_to_std_says_standard_and_its_preset_going_out_of_view_is_quiet()
    {
        var baro = Window("Captain baro");
        Deliver(baro, CaptainQnh);
        BatchAfter(1000);
        Deliver(baro, ("XMLVAR_Baro1_Mode", 3));
        BatchAfter(1000);
        Deliver(baro, ("INI_BARO_PRESET_CPT", 1015), ("INI_BARO_PRESET_CPT_SHOW", 1));
        BatchAfter(1000);
        Deliver(baro, ("INI_BARO_PRESET_CPT_SHOW", 0));
        BatchAfter(1000);
        Assert.Equal(new[] { "Captain altimeter standard", "Captain altimeter preset 1015 hectopascals" }, _speech.All);
    }

    // MSFSBA's own typed value is confirmed by its own sentence ("Altitude set ..."), never said again by the window.
    [Fact]
    public void A_typed_altitude_is_not_said_again_by_the_window()
    {
        var alt = Window("Altitude window");
        Deliver(alt, ("INI_ALTITUDE_DIAL", 12000), ("INI_FCU_ALT_DOT", 0));
        BatchAfter(1000);
        _def.HandleUIVariableSet("A350_FCU_ALTITUDE_SET", 13000, _def.GetVariables()["A350_FCU_ALTITUDE_SET"], _sim, _speech);
        _speech.All.Clear();   // the typed path's own words, if any
        Deliver(alt, ("INI_ALTITUDE_DIAL", 13000));
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_flight_load_takes_the_new_flights_windows_as_their_baseline()
    {
        var alt = Window("Altitude window");
        Deliver(alt, ("INI_ALTITUDE_DIAL", 12000), ("INI_FCU_ALT_DOT", 0));
        BatchAfter(1000);
        _def.OnSimContextReset();
        Deliver(alt, ("INI_ALTITUDE_DIAL", 5000));
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }

    // ---- presses and steps ----

    [Fact]
    public void A_press_with_nothing_to_show_says_pressed()
    {
        var chrono = Row("AIRLINER_FCU_CHRONO_LEFT", A350RowAction.Press);
        Press(chrono);
        Assert.Equal(new[] { chrono.Name + " pressed" }, _speech.All);
    }

    [Fact]
    public void A_press_whose_label_shows_its_state_says_nothing()
    {
        Press(Row("AIRLINER_ELEC_EXT_1", A350RowAction.Press));      // its lights
        Press(Row("AIRLINER_FCU_HDG_KNOB_PUSH", A350RowAction.Press));     // managed or selected
        Assert.Empty(_speech.All);
    }

    // An encoder with a value line says the value its steps left, once a run of steps has settled: the value a sighted pilot
    // reads off the knob's scale, never each click.
    [Fact]
    public void A_value_encoder_step_is_read_back_once_after_the_last_step()
    {
        var up = Row("AIRLINER_WXR_ELEV_LEFT", A350RowAction.Increase);
        string valueKey = A350Readouts.KeyFor(up.Control!);
        _cache[valueKey] = 1;
        Press(up);
        _now += 100;
        Press(up);
        Assert.Empty(_speech.All);
        BatchAfter(A350FcuCallouts.StepSettleMs - 200);
        Assert.Empty(_speech.All);   // too soon after the last step
        _cache[valueKey] = 3;
        BatchAfter(300);
        Assert.Equal(new[] { up.Name[..^" increase".Length] + ": 3" }, _speech.All);
        BatchAfter(1000);
        Assert.Single(_speech.All);
        Press(Row("AIRLINER_WXR_ELEV_LEFT", A350RowAction.Decrease));
        _cache[valueKey] = 2;
        BatchAfter(A350FcuCallouts.StepSettleMs + 100);
        Assert.Equal(up.Name[..^" increase".Length] + ": 2", _speech.All[^1]);
    }

    [Theory]
    [InlineData("AIRLINER_CKPT_LT_DU_MASTER_BRT")]   // its variable is kept by nothing
    [InlineData("AIRLINER_ECAM_SD_KNOB")]
    [InlineData("AIRLINER_ECAM_SCROLL_L")]
    [InlineData("AIRLINER_ISIS_KNOB_2")]
    public void A_step_with_nothing_readable_says_pressed(string controlId)
    {
        var step = Row(controlId, A350RowAction.Increase);
        Press(step);
        Assert.Equal(new[] { step.Name + " pressed" }, _speech.All);
        BatchAfter(A350FcuCallouts.StepSettleMs + 100);
        Assert.Single(_speech.All);
    }
}
