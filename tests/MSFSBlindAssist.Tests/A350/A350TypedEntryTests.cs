using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>A typed value for an encoder: validated as the released aircraft validate it, then landed by the
/// aircraft's own click from one step short of it (live 2026-10-26: the FCU dials, the cabin targets and the
/// sim's baro setting are the state a click steps from).</summary>
public class A350TypedEntryTests
{
    private static Func<string, double?> State(params (string Name, double Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    private static readonly Func<string, double?> SelectedSpeed =
        State(("INI_AIRSPEED_DIAL", 200), ("INI_AIRSPEED_IS_MACH", 0), ("INI_MANAGED_SPEED", 0), ("INI_FCU_SPD_DOT", 0));

    private static string[] Rpns(A350TypedPlan plan) =>
        plan.Steps.OfType<A350CalcStep>().Select(s => s.Rpn).ToArray();

    [Fact]
    public void A_selected_speed_is_written_one_knot_short_and_clicked_up_to()
    {
        var plan = A350TypedEntry.Plan(A350TypedEntry.Speed, 250, SelectedSpeed);
        Assert.Null(plan.Refusal);
        Assert.Equal(new[] { "249 (>L:INI_AIRSPEED_DIAL)", "1 (>B:AIRLINER_FCU_SPD_KNOB_Set)" }, Rpns(plan));
        Assert.Contains(plan.Steps, s => s is A350DelayStep);
        Assert.Equal(("INI_AIRSPEED_DIAL", 250.0), (plan.ReadBackVar, plan.Target));
        Assert.Equal("250 (>L:INI_AIRSPEED_DIAL)", plan.Correction.Rpn);
    }

    [Fact]
    public void A_lower_speed_is_clicked_down_to()
    {
        var plan = A350TypedEntry.Plan(A350TypedEntry.Speed, 150, SelectedSpeed);
        Assert.Equal(new[] { "151 (>L:INI_AIRSPEED_DIAL)", "-1 (>B:AIRLINER_FCU_SPD_KNOB_Set)" }, Rpns(plan));
    }

    [Fact]
    public void A_mach_window_takes_a_mach_number_one_hundredth_short()
    {
        var read = State(("INI_AIRSPEED_DIAL", 0.80), ("INI_AIRSPEED_IS_MACH", 1), ("INI_MANAGED_SPEED", 0), ("INI_FCU_SPD_DOT", 0));
        var plan = A350TypedEntry.Plan(A350TypedEntry.Speed, 0.78, read);
        Assert.Equal(new[] { "0.79 (>L:INI_AIRSPEED_DIAL)", "-1 (>B:AIRLINER_FCU_SPD_KNOB_Set)" }, Rpns(plan));
        Assert.Equal("FCU speed mach 0.78, selected", plan.Confirm(0.78, read));
    }

    [Fact]
    public void Knots_typed_while_the_window_shows_mach_are_said_not_converted()
    {
        var read = State(("INI_AIRSPEED_DIAL", 0.80), ("INI_AIRSPEED_IS_MACH", 1), ("INI_MANAGED_SPEED", 0), ("INI_FCU_SPD_DOT", 0));
        Assert.Equal("The speed window shows Mach. Type 0.10 to 0.99, or press SPD/MACH first.",
            A350TypedEntry.Plan(A350TypedEntry.Speed, 250, read).Refusal);
        Assert.Equal("The speed window shows knots. Type 100 to 399, or press SPD/MACH first.",
            A350TypedEntry.Plan(A350TypedEntry.Speed, 0.78, SelectedSpeed).Refusal);
    }

    [Fact]
    public void A_managed_speed_is_preset_by_the_knob_and_corrected_on_the_preset()
    {
        // While managed the knob seeds INI_Preset_Dial from the managed speed (airspeed_dial_up_handler).
        var read = State(("INI_AIRSPEED_DIAL", 200), ("INI_AIRSPEED_IS_MACH", 0), ("INI_MANAGED_SPEED", 1), ("INI_FCU_SPD_DOT", 1));
        var plan = A350TypedEntry.Plan(A350TypedEntry.Speed, 250, read);
        Assert.Equal(new[] { "1 (>B:AIRLINER_FCU_SPD_KNOB_Set)" }, Rpns(plan));
        Assert.Equal(("INI_PRESET_DIAL", 250.0), (plan.ReadBackVar, plan.Target));
        Assert.Equal("250 (>L:INI_PRESET_DIAL)", plan.Correction.Rpn);
        Assert.Equal("FCU speed 250, preset", plan.Confirm(250, read));   // live in flight: the window shows the preset
    }

    [Theory]
    [InlineData(99)]
    [InlineData(400)]
    [InlineData(1.2)]
    public void A_speed_out_of_range_is_refused_with_the_fcu_wording(double typed)
    {
        Assert.Equal("Speed must be 100-399 knots or 0.10-0.99 Mach", A350TypedEntry.Plan(A350TypedEntry.Speed, typed, SelectedSpeed).Refusal);
    }

    [Fact]
    public void Text_that_is_not_a_number_is_refused_never_sent()
    {
        foreach (var field in A350TypedEntry.All)
            Assert.Equal("Invalid number format", A350TypedEntry.Plan(field, double.NaN, _ => 0).Refusal);
    }

    [Fact]
    public void An_unread_window_is_not_planned_from()
    {
        Assert.Equal(A350WritePlan.UnknownPositionRefusal, A350TypedEntry.Plan(A350TypedEntry.Speed, 250, _ => null).Refusal);
    }

    private static Func<string, double?> Heading(double hdg, double trk = 0, double trkFpa = 0, double dashed = 0) =>
        State(("INI_HEADING_DIAL", hdg), ("INI_TRK_DIAL", trk), ("INI_TRACK_FPA_STATE", trkFpa), ("INI_FCU_HDG_DOT", 0),
            ("INI_FCU_HDG_DASHED", dashed), ("INI_ROLL_MODE", dashed >= 0.5 ? 1 : 2 + 9 * trkFpa));

    /// <summary>Live in flight 2026-10-26: a turn of a dashed window (NAV, V/S not engaged) is a preset the pilot pulls
    /// to use, as on the FCU; the mode stays as it was.</summary>
    [Fact]
    public void A_value_typed_into_a_dashed_window_is_confirmed_as_a_preset()
    {
        var nav = Heading(192, dashed: 1);
        Assert.Equal("FCU heading 300, preset", A350TypedEntry.Plan(A350TypedEntry.Heading, 300, nav).Confirm(300, nav));
        var vs = State(("INI_VVI_DIAL", 0), ("INI_FPA_DIAL", 0), ("INI_TRACK_FPA_STATE", 0), ("INI_FCU_VS_DASHED", 1), ("INI_PITCH_MODE", 12));
        Assert.Equal("FCU vertical speed -1000, preset", A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -1000, vs).Confirm(-1000, vs));
    }

    /// <summary>Live in flight: a second value typed while the first preset still shows (window no longer dashed) is a
    /// preset too while NAV (INI_ROLL_MODE 1) or a vertical mode other than V/S 8 and FPA 11 (ALT is 12) is engaged.</summary>
    [Fact]
    public void A_value_typed_over_a_preset_is_a_preset_while_the_mode_is_not_engaged()
    {
        var nav = State(("INI_HEADING_DIAL", 310), ("INI_TRK_DIAL", 0), ("INI_TRACK_FPA_STATE", 0), ("INI_FCU_HDG_DOT", 0),
            ("INI_FCU_HDG_DASHED", 0), ("INI_ROLL_MODE", 1));
        Assert.Equal("FCU heading 305, preset", A350TypedEntry.Plan(A350TypedEntry.Heading, 305, nav).Confirm(305, nav));
        var hdg = State(("INI_HEADING_DIAL", 310), ("INI_TRK_DIAL", 0), ("INI_TRACK_FPA_STATE", 0), ("INI_FCU_HDG_DOT", 0),
            ("INI_FCU_HDG_DASHED", 0), ("INI_ROLL_MODE", 2));
        Assert.Equal("FCU heading 305, selected", A350TypedEntry.Plan(A350TypedEntry.Heading, 305, hdg).Confirm(305, hdg));
        var alt = State(("INI_VVI_DIAL", -500), ("INI_FPA_DIAL", 0), ("INI_TRACK_FPA_STATE", 0), ("INI_FCU_VS_DASHED", 0), ("INI_PITCH_MODE", 12));
        Assert.Equal("FCU vertical speed -700, preset", A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -700, alt).Confirm(-700, alt));
        var vsMode = State(("INI_VVI_DIAL", -500), ("INI_FPA_DIAL", 0), ("INI_TRACK_FPA_STATE", 0), ("INI_FCU_VS_DASHED", 0), ("INI_PITCH_MODE", 8));
        Assert.Equal("FCU vertical speed -700", A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -700, vsMode).Confirm(-700, vsMode));
    }

    [Fact]
    public void A_heading_goes_the_short_way_round()
    {
        Assert.Equal(new[] { "9 (>L:INI_HEADING_DIAL)", "1 (>B:AIRLINER_FCU_HDG_KNOB_Set)" },
            Rpns(A350TypedEntry.Plan(A350TypedEntry.Heading, 10, Heading(350))));
        Assert.Equal(new[] { "1 (>L:INI_HEADING_DIAL)", "-1 (>B:AIRLINER_FCU_HDG_KNOB_Set)" },
            Rpns(A350TypedEntry.Plan(A350TypedEntry.Heading, 360, Heading(10))));
        Assert.Equal(new[] { "359 (>L:INI_HEADING_DIAL)", "1 (>B:AIRLINER_FCU_HDG_KNOB_Set)" },
            Rpns(A350TypedEntry.Plan(A350TypedEntry.Heading, 0, Heading(300))));
        Assert.Equal(0.0, A350TypedEntry.Plan(A350TypedEntry.Heading, 360, Heading(10)).Target);
    }

    [Fact]
    public void A_track_window_takes_the_track_dial()
    {
        var read = Heading(100, trk: 40, trkFpa: 1);
        var plan = A350TypedEntry.Plan(A350TypedEntry.Heading, 45, read);
        Assert.Equal(new[] { "44 (>L:INI_TRK_DIAL)", "1 (>B:AIRLINER_FCU_HDG_KNOB_Set)" }, Rpns(plan));
        Assert.Equal("FCU track 045, selected", plan.Confirm(45, read));
        Assert.Equal("FCU heading 005, selected", A350TypedEntry.Plan(A350TypedEntry.Heading, 5, Heading(10)).Confirm(5, Heading(10)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(361)]
    public void A_heading_out_of_range_is_refused_with_the_fcu_wording(double typed)
    {
        Assert.Equal("Heading must be between 0 and 360 degrees", A350TypedEntry.Plan(A350TypedEntry.Heading, typed, Heading(10)).Refusal);
    }

    private static readonly Func<string, double?> Altitude10000 = State(("INI_ALTITUDE_DIAL", 10000), ("INI_FCU_ALT_DOT", 1));

    [Fact]
    public void An_altitude_rounds_to_100_feet_and_is_clicked_up_to()
    {
        var plan = A350TypedEntry.Plan(A350TypedEntry.Altitude, 12345, Altitude10000);
        Assert.Equal(new[] { "12200 (>L:INI_ALTITUDE_DIAL)", "1 (>B:AIRLINER_FCU_ALT_KNOB_Set)" }, Rpns(plan));
        Assert.Equal(12300.0, plan.Target);
        Assert.Equal("FCU altitude 12300, managed", plan.Confirm(12300, Altitude10000));
        Assert.Equal("Altitude must be between 100 and 49000 feet", A350TypedEntry.Plan(A350TypedEntry.Altitude, 49100, Altitude10000).Refusal);
    }

    private static Func<string, double?> Vertical(double trkFpa) =>
        State(("INI_VVI_DIAL", 0), ("INI_FPA_DIAL", 0), ("INI_TRACK_FPA_STATE", trkFpa), ("INI_FCU_VS_DASHED", 0), ("INI_PITCH_MODE", 8 + 3 * trkFpa));

    [Fact]
    public void A_vertical_speed_rounds_to_100_and_an_fpa_to_a_tenth()
    {
        var vs = A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -1520, Vertical(0));
        Assert.Equal(new[] { "-1400 (>L:INI_VVI_DIAL)", "-1 (>B:AIRLINER_FCU_VS_KNOB_Set)" }, Rpns(vs));
        Assert.Equal("FCU vertical speed -1500", vs.Confirm(-1500, Vertical(0)));
        var fpa = A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -3.04, Vertical(1));
        Assert.Equal(new[] { "-2.9 (>L:INI_FPA_DIAL)", "-1 (>B:AIRLINER_FCU_VS_KNOB_Set)" }, Rpns(fpa));
        Assert.Equal("FCU flight path angle -3.0", fpa.Confirm(-3.0, Vertical(1)));
    }

    [Fact]
    public void Zero_levels_off_in_either_mode()
    {
        Assert.Equal(new[] { "-100 (>L:INI_VVI_DIAL)", "1 (>B:AIRLINER_FCU_VS_KNOB_Set)" },
            Rpns(A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, 0, Vertical(0))));
        Assert.Equal(new[] { "-0.1 (>L:INI_FPA_DIAL)", "1 (>B:AIRLINER_FCU_VS_KNOB_Set)" },
            Rpns(A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, 0, Vertical(1))));
    }

    [Fact]
    public void A_vertical_value_for_the_other_mode_is_said_not_converted()
    {
        Assert.Equal("The vertical window shows V/S. Type plus or minus 100 to 6000, or press HDG-V/S / TRK-FPA first.",
            A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -3, Vertical(0)).Refusal);
        Assert.Equal("The vertical window shows FPA. Type plus or minus 9.9 or less, or press HDG-V/S / TRK-FPA first.",
            A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -1500, Vertical(1)).Refusal);
        Assert.Equal("Invalid value. VS: plus or minus 100 to 6000, FPA: plus or minus 9.9 or less",
            A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, 6100, Vertical(0)).Refusal);
        Assert.Equal("Invalid value. VS: plus or minus 100 to 6000, FPA: plus or minus 9.9 or less",
            A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, 50, Vertical(0)).Refusal);
    }

    private static Func<string, double?> Baro(int side, double mb, double hpa = 1, double mode = 0) =>
        State(($"KOHLSMAN SETTING MB:{side}", mb), ($"XMLVAR_Baro_Selector_HPA_{side}", hpa), ($"XMLVAR_Baro{side}_Mode", mode));

    [Fact]
    public void A_qnh_in_hectopascals_is_set_one_short_on_its_side_and_clicked_up_to()
    {
        var plan = A350TypedEntry.Plan(A350TypedEntry.CaptainQnh, 1013, Baro(1, 1009));
        Assert.Equal(new[] { "16192 1 r (>K:2:KOHLSMAN_SET)", "1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT_Set)" }, Rpns(plan));
        Assert.Equal(("KOHLSMAN SETTING MB:1", 1013.0), (plan.ReadBackVar, plan.Target));
        Assert.Equal("16208 1 r (>K:2:KOHLSMAN_SET)", plan.Correction.Rpn);
        Assert.Equal("Captain altimeter set 1013 hectopascals", plan.Confirm(1013, Baro(1, 1013)));
        var fo = A350TypedEntry.Plan(A350TypedEntry.FirstOfficerQnh, 1000, Baro(2, 1009));
        Assert.Equal(new[] { "16016 2 r (>K:2:KOHLSMAN_SET)", "-1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_Set)" }, Rpns(fo));
        Assert.Equal("First officer altimeter set 1000 hectopascals", fo.Confirm(1000, Baro(2, 1000)));
    }

    [Fact]
    public void A_qnh_in_inches_steps_a_hundredth_of_an_inch()
    {
        var read = Baro(1, 1009, hpa: 0);
        var plan = A350TypedEntry.Plan(A350TypedEntry.CaptainQnh, 29.92, read);
        double mb = 29.92 * 33.8639;
        Assert.Equal(mb, plan.Target, 3);
        // one hundredth of an inch short (0.338639 mb), in the sim's mb x16
        Assert.Equal($"{Math.Round((mb - 0.338639) * 16).ToString(CultureInfo.InvariantCulture)} 1 r (>K:2:KOHLSMAN_SET)", Rpns(plan)[0]);
        Assert.Equal("Captain altimeter set 29.92 inches", plan.Confirm(mb, read));
    }

    [Fact]
    public void A_qnh_out_of_range_says_the_sides_unit()
    {
        Assert.Equal("QNH must be between 900 and 1100 hectopascals.", A350TypedEntry.Plan(A350TypedEntry.CaptainQnh, 29.92, Baro(1, 1009)).Refusal);
        Assert.Equal("QNH must be between 26.6 and 32.5 inches.", A350TypedEntry.Plan(A350TypedEntry.CaptainQnh, 1013, Baro(1, 1009, hpa: 0)).Refusal);
    }

    [Fact]
    public void A_qnh_typed_while_std_is_a_preset_the_knob_steps_to()
    {
        // Live in flight: on STD the first click shows the preset, each further click a step, paced; the pull sets it.
        var plan = A350TypedEntry.Plan(A350TypedEntry.CaptainQnh, 1013, Baro(1, 1013.25, mode: 3));
        Assert.Null(plan.Refusal);
        Assert.Equal(new[] { "1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT_Set)" }, Rpns(plan));
        var stepper = plan.Stepper!;
        Assert.Equal(("INI_BARO_PRESET_CPT", "INI_BARO_PRESET_CPT_SHOW", 1013.0), (stepper.ValueVar, stepper.ShowVar, stepper.Target));
        Assert.Equal("1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT_Set)", stepper.Click(1).Rpn);
        Assert.Equal("-1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT_Set)", stepper.Click(-1).Rpn);
        Assert.Equal("Captain altimeter preset 1013 hectopascals", plan.Confirm(1013, _ => 0));
        Assert.Equal("Captain baro is on STD and its knob shows no preset here, as on the ground; pull it for QNH first.", stepper.NotShown);
        var fo = A350TypedEntry.Plan(A350TypedEntry.FirstOfficerQnh, 29.92, Baro(2, 1013.25, hpa: 0, mode: 3));
        Assert.Equal(("INI_BARO_PRESET_FO", 2992.0), (fo.Stepper!.ValueVar, fo.Stepper.Target));   // hundredths of an inch
        Assert.Equal("First officer altimeter preset 29.92 inches", fo.Confirm(2992, _ => 0));
    }

    [Fact]
    public void The_isis_qnh_is_the_third_altimeter_typed_in_either_unit()
    {
        var read = State(("KOHLSMAN SETTING MB:3", 1009.14));
        var plan = A350TypedEntry.Plan(A350TypedEntry.IsisQnh, 1013, read);
        Assert.Equal(new[] { "16192 3 r (>K:2:KOHLSMAN_SET)", "1 (>B:AIRLINER_ISIS_KNOB_1_Set)" }, Rpns(plan));
        Assert.Equal(("KOHLSMAN SETTING MB:3", 1013.0), (plan.ReadBackVar, plan.Target));
        Assert.Equal("ISIS altimeter set 1013 hectopascals", plan.Confirm(1013, read));
        var inches = A350TypedEntry.Plan(A350TypedEntry.IsisQnh, 29.92, read);
        Assert.Equal(29.92 * 33.8639, inches.Target, 3);
        Assert.Equal("ISIS altimeter set 29.92 inches", inches.Confirm(29.92 * 33.8639, read));
        Assert.Equal("QNH must be between 900 and 1100 hectopascals, or 26.6 and 32.5 inches.",
            A350TypedEntry.Plan(A350TypedEntry.IsisQnh, 500, read).Refusal);
    }

    [Fact]
    public void The_cabin_targets_take_the_aircrafts_own_steps_and_limits()
    {
        var alt = A350TypedEntry.Plan(A350TypedEntry.CabinAltitudeTarget, 3050, State(("INI_CABIN_PRESS_ALTITUDE_TARGET", 0)));
        Assert.Equal(new[] { "3000 (>L:INI_CABIN_PRESS_ALTITUDE_TARGET)", "1 (>B:AIRLINER_CABIN_PRESS_ALTITUDE_TARGET_Set)" }, Rpns(alt));
        Assert.Equal(3100.0, alt.Target);
        Assert.Equal("Cabin altitude target set to 3100 feet", alt.Confirm(3100, _ => 0));
        var vs = A350TypedEntry.Plan(A350TypedEntry.CabinVsTarget, -320, State(("INI_CABIN_PRESS_VS_TARGET", 0)));
        Assert.Equal(new[] { "-250 (>L:INI_CABIN_PRESS_VS_TARGET)", "-1 (>B:AIRLINER_CABIN_PRESS_VS_TARGET_Set)" }, Rpns(vs));
        Assert.Equal("Cabin V/S target set to -300 feet per minute", vs.Confirm(-300, _ => 0));
        Assert.Equal("Cabin altitude target must be between -2000 and 13500 feet",
            A350TypedEntry.Plan(A350TypedEntry.CabinAltitudeTarget, 14000, _ => 0).Refusal);
        Assert.Equal("Cabin V/S target must be between -1500 and 2500 feet per minute",
            A350TypedEntry.Plan(A350TypedEntry.CabinVsTarget, 3000, _ => 0).Refusal);
    }

    [Fact]
    public void Every_number_is_invariant_under_a_comma_decimal_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var read = State(("INI_VVI_DIAL", 0), ("INI_FPA_DIAL", 0), ("INI_TRACK_FPA_STATE", 1));
            var plan = A350TypedEntry.Plan(A350TypedEntry.VerticalSpeed, -3.0, read);
            Assert.Equal("-2.9 (>L:INI_FPA_DIAL)", Rpns(plan)[0]);
            Assert.Equal("FCU flight path angle -3.0", plan.Confirm(-3.0, read));
            var mach = State(("INI_AIRSPEED_DIAL", 0.80), ("INI_AIRSPEED_IS_MACH", 1), ("INI_MANAGED_SPEED", 0), ("INI_FCU_SPD_DOT", 0));
            Assert.Equal("FCU speed mach 0.78, selected", A350TypedEntry.Plan(A350TypedEntry.Speed, 0.78, mach).Confirm(0.78, mach));
            Assert.Equal("Captain altimeter set 29.92 inches",
                A350TypedEntry.Plan(A350TypedEntry.CaptainQnh, 29.92, Baro(1, 1009, hpa: 0)).Confirm(29.92 * 33.8639, Baro(1, 1009, hpa: 0)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Every_field_is_a_typed_box_for_its_encoder()
    {
        Assert.Equal(9, A350TypedEntry.All.Count);
        Assert.All(A350TypedEntry.All, f => Assert.Contains("_SET", f.Key));
        Assert.Equal(A350TypedEntry.All.Count, A350TypedEntry.All.Select(f => f.ControlId).Distinct().Count());
        Assert.Same(A350TypedEntry.Speed, A350TypedEntry.ForControl("AIRLINER_FCU_SPD_KNOB"));
        Assert.Null(A350TypedEntry.ForControl("AIRLINER_ISIS_KNOB_2"));   // the first officer's unit shows no baro the sim keeps
    }
}
