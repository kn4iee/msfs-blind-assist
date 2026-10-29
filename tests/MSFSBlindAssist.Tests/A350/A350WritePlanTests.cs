using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The A350 takes its own <c>value (&gt;B:AIRLINER_&lt;Name&gt;_Set)</c> from outside (measured 2026-10-26): a
/// selector goes to the value sent (NO SMOKING 2), a push button FLIPS whatever value is sent (GALLEY 0 → 1).
/// </summary>
public class A350WritePlanTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
    private static readonly A350ControlMap Map = A350ControlMap.Load();

    public A350WritePlanTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    private static A350Control C(string id) => Map.Find(id)!;

    private static IEnumerable<string> Rpn(A350Plan plan) =>
        plan.Steps.Select(s => s is A350CalcStep c ? c.Rpn : $"wait {((A350DelayStep)s).Milliseconds}");

    // Review 2026-10-29 (panels minor 9): no test covered the spring plan. It is held at the picked side, then put back to its
    // rest; the hold owes the rest (sent at once if the definition goes away mid-hold).
    [Fact]
    public void A_spring_switch_is_held_at_the_picked_side_then_put_back_to_rest()
    {
        var spring = C("AIRLINER_OIS_AVNCS_EFB_LEFT");
        double rest = spring.StateForPosition(spring.Rest ?? 1);
        double away = spring.OrderedPositions().Select(p => spring.StateForPosition(p.Value)).First(v => Math.Abs(v - rest) > 0.01);
        var plan = A350WritePlan.ForSet(spring, away, rest);
        string F(double v) => v.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(new[]
        {
            $"{F(away)} (>B:AIRLINER_OIS_AVNCS_EFB_LEFT_Set)", $"wait {A350WritePlan.SpringHoldMsFor(spring)}",
            $"{F(rest)} (>B:AIRLINER_OIS_AVNCS_EFB_LEFT_Set)",
        }, Rpn(plan));
        Assert.True(((A350DelayStep)plan.Steps[1]).OwesRest);
    }

    [Fact]
    public void A_toggle_is_flipped_once_when_it_is_elsewhere()
    {
        Assert.Equal(new[] { "1 (>B:AIRLINER_ELEC_BAT_1_Set)" }, Rpn(A350WritePlan.ForSet(C("AIRLINER_ELEC_BAT_1"), 1, 0)));
    }

    [Fact]
    public void A_toggle_already_there_is_not_flipped()
    {
        Assert.Same(A350Plan.AlreadyInPosition, A350WritePlan.ForSet(C("AIRLINER_ELEC_BAT_1"), 1, 1));
    }

    [Fact]
    public void A_toggle_whose_position_is_unread_is_refused()
    {
        var plan = A350WritePlan.ForSet(C("AIRLINER_ELEC_BAT_1"), 1, null);
        Assert.Equal(A350WritePlan.UnknownPositionRefusal, plan.Refusal);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void A_toggle_reading_none_of_its_positions_is_refused()
    {
        Assert.Equal(A350WritePlan.UnknownPositionRefusal, A350WritePlan.ForSet(C("AIRLINER_ELEC_BAT_1"), 1, 0.5).Refusal);
    }

    [Fact]
    public void A_selector_is_set_absolutely()
    {
        Assert.Equal(new[] { "2 (>B:AIRLINER_SIGNS_SEAT_BELTS_Set)" }, Rpn(A350WritePlan.ForSet(C("AIRLINER_SIGNS_SEAT_BELTS"), 2, null)));
    }

    [Fact]
    public void A_selector_value_that_is_no_position_is_refused()
    {
        Assert.Equal(A350WritePlan.NotAPositionRefusal, A350WritePlan.ForSet(C("AIRLINER_SIGNS_SEAT_BELTS"), 7, 0).Refusal);
    }

    [Fact]
    public void A_hold_button_is_pressed_held_and_released()
    {
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)", "wait 250", "0 (>B:AIRLINER_ECAM_ENG_Set)" },
            Rpn(A350WritePlan.ForPress(C("AIRLINER_ECAM_ENG"))));
    }

    // The owner's rule (2026-10-28): a button a pilot presses and holds is held for as long as the pilot chooses, never
    // timed by MSFSBA: the first press holds it, the next releases it. Measured live for the reasons each needs holding:
    // FIRE TEST raises its fire warnings and CRC only after 2.5 s held; a 250 ms T.O CONFIG left the memo's TO CONFIG
    // TEST undone (1.5 s ran it); a mask's flow blinker shows only while PRESS TO TEST is held; ACCU REINFLATE refills
    // the accumulators only while held (89 to 100 % in under 2 s). CVR TEST (its tone), CVR ERASE (2 s on the ground),
    // the RMP PA keys (push to talk) and MECH call (the nose horn sounds while held) are held by their own procedures.
    // Each one's Set writes its own held L:var (`p0 2 == if{ 1 (>L:X) } els{ 0 (>L:X) }`, A350_Interior.behavior.xml,
    // 1.2.7): the cockpit's record of the button held down, which its label reads.
    [Theory]
    [InlineData("AIRLINER_FIRE_TEST", "INI_FIRE_TEST")]
    [InlineData("AIRLINER_ECAM_TO_CONFIG", "PUSH_ECAM_TOCONFIG")]
    [InlineData("AIRLINER_PARK_BRK_ACCU_REINFLATE", "INI_ACCU_REINFLATE")]
    [InlineData("AIRLINER_OXYTEST_CPT_PANEL", "INI_OXYTEST_BLINKER_CPT")]
    [InlineData("AIRLINER_OXYTEST_FO_PANEL", "INI_OXYTEST_BLINKER_FO")]
    [InlineData("AIRLINER_OXYTEST_TO_PANEL", "INI_OXYTEST_BLINKER_TO")]
    [InlineData("AIRLINER_OXYTEST_4TH_PANEL", "INI_OXYTEST_BLINKER_4TH")]
    [InlineData("AIRLINER_OXYTEST_CPT_MASK", "INI_OXYTEST_EMER_CPT")]
    [InlineData("AIRLINER_OXYTEST_FO_MASK", "INI_OXYTEST_EMER_FO")]
    [InlineData("AIRLINER_OXYTEST_TO_MASK", "INI_OXYTEST_EMER_TO")]
    [InlineData("AIRLINER_OXYTEST_4TH_MASK", "INI_OXYTEST_EMER_4TH")]
    [InlineData("AIRLINER_CVR_TEST", "INI_CVR_TEST")]
    [InlineData("AIRLINER_CVR_ERASE", "INI_CVR_ERASE")]
    [InlineData("AIRLINER_CALLS_MECH", "INI_CALLS_MECH")]
    [InlineData("AIRLINER_RMP_PA_CALL_1", "INI_RMP1_PA_CHANNEL")]
    [InlineData("AIRLINER_RMP_PA_CALL_2", "INI_RMP2_PA_CHANNEL")]
    [InlineData("AIRLINER_RMP_PA_CALL_3", "INI_RMP3_PA_CHANNEL")]
    public void A_press_and_hold_button_is_held_until_released(string id, string heldVar)
    {
        Assert.True(A350WritePlan.IsLatched(C(id)));
        Assert.Equal(heldVar, A350WritePlan.HeldVarFor(C(id)));
        string set = C(id).Event!;
        Assert.Equal(new[] { $"2 (>B:{set})" }, Rpn(A350WritePlan.ForHoldDown(C(id))));
        Assert.Equal(new[] { $"0 (>B:{set})" }, Rpn(A350WritePlan.ForHoldUp(C(id))));
    }

    [Fact]
    public void The_theory_above_names_every_held_button() =>
        Assert.Equal(17, A350ControlMap.Load().Controls.Count(A350WritePlan.IsLatched));

    [Theory]
    [InlineData("AIRLINER_ECAM_ENG")]
    [InlineData("AIRLINER_ECAM_CLEAR_LEFT")]
    [InlineData("AIRLINER_ISIS_PLUS_1")]
    [InlineData("AIRLINER_CALLS_FWD")]
    [InlineData("AIRLINER_FCU_CHRONO_LEFT")]
    public void A_momentary_key_the_aircraft_models_as_a_hold_is_still_a_tap(string id)
    {
        Assert.False(A350WritePlan.IsLatched(C(id)));
        Assert.Null(A350WritePlan.HeldVarFor(C(id)));
    }

    [Fact]
    public void A_light_test_the_cockpit_returns_itself_is_pressed_once()
    {
        // Live 2026-10-26: AUTOLAND flips INI_AUTOLAND_LIGHT_TEST to 1 and the cockpit puts it back within a second.
        Assert.Equal(new[] { "1 (>B:AIRLINER_FCU_AUTO_LAND_LEFT_Set)" }, Rpn(A350WritePlan.ForPress(C("AIRLINER_FCU_AUTO_LAND_LEFT"))));
    }

    [Fact]
    public void A_button_is_pressed_once()
    {
        Assert.Equal(new[] { "1 (>B:AIRLINER_APU_START_Set)" }, Rpn(A350WritePlan.ForPress(C("AIRLINER_APU_START"))));
    }

    [Fact]
    public void An_encoder_steps_both_ways()
    {
        var knob = C("AIRLINER_CABIN_PRESS_ALTITUDE_TARGET");
        Assert.Equal(new[] { "1 (>B:AIRLINER_CABIN_PRESS_ALTITUDE_TARGET_Set)" }, Rpn(A350WritePlan.ForStep(knob, increase: true)));
        Assert.Equal(new[] { "-1 (>B:AIRLINER_CABIN_PRESS_ALTITUDE_TARGET_Set)" }, Rpn(A350WritePlan.ForStep(knob, increase: false)));
    }

    [Fact]
    public void Numbers_are_written_invariant_under_a_comma_culture()
    {
        Assert.Equal("0.1", A350WritePlan.Format(0.1));
        Assert.Equal("0", A350WritePlan.Format(0.00001));   // never "1E-05": the RPN parser rejects it
    }
}
