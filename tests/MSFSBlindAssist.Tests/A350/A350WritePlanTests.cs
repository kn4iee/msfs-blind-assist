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
