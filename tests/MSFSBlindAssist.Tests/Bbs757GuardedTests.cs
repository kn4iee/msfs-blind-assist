using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What a pick on a guarded 757 control sends: the calls that open or close the guard and write the
/// control's state var, decided from fresh readings of both. The rules (BlueBird, spec 3.3): a guarded
/// control moves only with its guard open; closing the guard writes the control's <c>CloseSets</c>
/// position (emergency lights ARMED, stab trim cutouts NORMAL), and the app writes that position
/// itself after closing so the control lands whether or not the guard's own click wrote it; a reading
/// that is unread, not a number, or a pick that is none of the control's positions sends nothing.
/// Controls are built in memory with BlueBird's own click code, never from the shipped map.
/// </summary>
public class Bbs757GuardedTests
{
    private const string LightsState = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK";
    private const string LightsGuardNode = "B757_OVHD_SWITCH_GAURD";
    private const string LightsGuardPress =
        "(L:B757_OVHD_SWITCH_GAURD, bool) ! (>L:B757_OVHD_SWITCH_GAURD, bool) (L:B757_OVHD_SWITCH_GAURD, bool) 0 == if{ 1 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK, number) }";

    private const string TrimState = "B757_Stab_trim_C_toggle_switch_CLICK";
    private const string TrimGuardNode = "B757_AISLE_STAND_STAB_1_GAURD_SWITCH";
    private const string TrimGuardPress =
        "(L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) ! (>L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) (L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) 0 == if{ 0 (>L:B757_Stab_trim_C_toggle_switch_CLICK, number) }";

    private const double Closed = 0;
    private const double Open = 1;

    private static Bbs757Control Lights() => new()
    {
        Node = LightsState, Label = "Emergency Lights", Kind = Bbs757Kinds.Switch, StateVar = LightsState,
        Positions = { new(0, "OFF"), new(1, "ARMED"), new(2, "ON") },
        Guard = LightsGuardNode, GuardClosed = 0, CloseSets = 1,
    };

    private static Bbs757Control LightsGuard(string? press = LightsGuardPress) => new()
    {
        Node = LightsGuardNode, Label = "Emergency Lights Guard", Kind = Bbs757Kinds.Guard, StateVar = LightsGuardNode,
        Positions = { new(0, "Closed"), new(1, "Open") }, Press = press,
    };

    private static Bbs757Control TrimC() => new()
    {
        Node = TrimState, Label = "Stab Trim C", Kind = Bbs757Kinds.Toggle, StateVar = TrimState,
        Positions = { new(0, "NORMAL"), new(1, "CUTOUT") },
        Guard = TrimGuardNode, GuardClosed = 0, CloseSets = 0,
    };

    private static Bbs757Control TrimGuard() => new()
    {
        Node = TrimGuardNode, Label = "Stab Trim C Guard", Kind = Bbs757Kinds.Guard, StateVar = TrimGuardNode,
        Positions = { new(0, "Closed"), new(1, "Open") }, Press = TrimGuardPress,
    };

    private static string LightsWrite(double value) => $"{value.ToString("0.######", CultureInfo.InvariantCulture)} (>L:{LightsState})";

    private static string TrimWrite(double value) => $"{value.ToString("0.######", CultureInfo.InvariantCulture)} (>L:{TrimState})";

    private static void AssertRefused(Bbs757GuardedPlan plan)
    {
        Assert.True(plan.Refused);
        Assert.Empty(plan.Calls);
    }

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

    // ---- the brief's cases -------------------------------------------------------------------

    [Fact]
    public void Off_from_armed_opens_the_guard_and_writes_zero()
    {
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 0);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { LightsGuardPress, "0 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void Armed_from_off_closes_the_guard_and_writes_one()
    {
        // Closing the guard is BlueBird's own way to ARMED: the app writes 1 as well, so the control
        // lands even if the guard's click did not write it.
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: Open, target: 1);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { LightsGuardPress, "1 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void On_with_the_guard_open_writes_two()
    {
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Open, target: 2);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { "2 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void On_with_the_guard_closed_opens_it_first()
    {
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 2);

        Assert.Equal(new[] { LightsGuardPress, LightsWrite(2) }, plan.Calls);
    }

    [Fact]
    public void Already_there_sends_nothing()
    {
        // Not a refusal: the pick landed already, so the pilot hears nothing at all.
        var open = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 2, guardState: Open, target: 2);
        var closedAtCloseSets = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 1);

        Assert.False(open.Refused);
        Assert.Empty(open.Calls);
        Assert.False(closedAtCloseSets.Refused);
        Assert.Empty(closedAtCloseSets.Calls);
    }

    [Fact]
    public void Cutout_opens_and_writes_one()
    {
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 0, guardState: Closed, target: 1);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { TrimGuardPress, "1 (>L:B757_Stab_trim_C_toggle_switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void Normal_closes_the_guard_and_writes_zero()
    {
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 1, guardState: Open, target: 0);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { TrimGuardPress, "0 (>L:B757_Stab_trim_C_toggle_switch_CLICK)" }, plan.Calls);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(1.0, null)]
    [InlineData(null, null)]
    public void Unread_state_refuses(double? current, double? guardState)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current, guardState, target: 0));
    }

    [Fact]
    public void Calls_are_invariant_under_de_DE()
    {
        InCulture("de-DE", () =>
        {
            Assert.Equal(new[] { LightsGuardPress, "0 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" },
                Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 0).Calls);

            // A fractional position is the case a comma culture would break: "2,5" is not RPN.
            var fractional = Lights();
            fractional.Positions.Add(new Bbs757Position(2.5, "HALF"));
            Assert.Equal(new[] { "2.5 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" },
                Bbs757Guarded.Plan(fractional, LightsGuard(), current: 1, guardState: Open, target: 2.5).Calls);
        });
    }

    // ---- the other refusals (ruling R2) --------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(1.0, double.NaN)]
    [InlineData(1.0, double.NegativeInfinity)]
    public void A_reading_that_is_not_a_number_refuses(double current, double guardState)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current, guardState, target: 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_control_with_no_state_var_refuses(string? stateVar)
    {
        var control = Lights();
        control.StateVar = stateVar;

        AssertRefused(Bbs757Guarded.Plan(control, LightsGuard(), current: 1, guardState: Open, target: 2));
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(0.5)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_target_that_is_no_position_refuses(double target)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Open, target));
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target));
    }

    [Fact]
    public void A_control_with_no_guard_closed_value_refuses()
    {
        var control = Lights();
        control.GuardClosed = null;

        AssertRefused(Bbs757Guarded.Plan(control, LightsGuard(), current: 1, guardState: Open, target: 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_guard_with_no_press_refuses(string? press)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(press), current: 1, guardState: Open, target: 2));
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(press), current: 1, guardState: Closed, target: 2));
    }

    [Fact]
    public void A_guard_with_no_open_position_refuses()
    {
        // Only a Closed position (or none): there is no value to toggle the guard to.
        var onlyClosed = LightsGuard();
        onlyClosed.Positions.RemoveAt(1);
        var noPositions = LightsGuard();
        noPositions.Positions.Clear();

        AssertRefused(Bbs757Guarded.Plan(Lights(), onlyClosed, current: 1, guardState: Open, target: 2));
        AssertRefused(Bbs757Guarded.Plan(Lights(), onlyClosed, current: 1, guardState: Closed, target: 2));
        AssertRefused(Bbs757Guarded.Plan(Lights(), noPositions, current: 1, guardState: Closed, target: 2));
    }

    [Fact]
    public void An_open_value_the_closed_reading_cannot_tell_apart_refuses()
    {
        // The guard's "open" position is within the read-back slack of the closed reading: toggling
        // "to" it would be no toggle at all, so nothing is sent rather than a state write behind a closed guard.
        var guard = LightsGuard();
        guard.Positions[1] = new Bbs757Position(0.0011, "Open");

        AssertRefused(Bbs757Guarded.Plan(Lights(), guard, current: 1, guardState: 0.0009, target: 2));
    }

    // ---- guard and target interplay ------------------------------------------------------------

    [Fact]
    public void A_guard_closed_with_a_pick_that_is_not_close_sets_opens_it_and_writes()
    {
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 1, guardState: Closed, target: 1);

        // Cutout already reads 1 behind a closed guard (an odd pair of readings): the plan still opens
        // the guard first, since a target other than CloseSets always needs it open.
        Assert.Equal(new[] { TrimGuardPress, TrimWrite(1) }, plan.Calls);
    }

    [Fact]
    public void A_pick_that_is_close_sets_with_the_guard_already_closed_writes_only_when_it_differs()
    {
        var differs = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: Closed, target: 1);
        var same = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 1);

        Assert.Equal(new[] { LightsWrite(1) }, differs.Calls);
        Assert.Empty(same.Calls);
    }

    [Fact]
    public void A_pick_that_is_close_sets_with_the_guard_open_closes_it_even_when_it_already_reads_it()
    {
        // The guard is still open, so the pilot's pick is "cover the switch": close it, then the write.
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 0, guardState: Open, target: 0);

        Assert.Equal(new[] { TrimGuardPress, TrimWrite(0) }, plan.Calls);
    }

    [Fact]
    public void A_control_with_no_close_sets_never_closes_the_guard()
    {
        var control = Lights();
        control.CloseSets = null;

        var open = Bbs757Guarded.Plan(control, LightsGuard(), current: 0, guardState: Open, target: 1);
        var closed = Bbs757Guarded.Plan(control, LightsGuard(), current: 0, guardState: Closed, target: 1);
        var there = Bbs757Guarded.Plan(control, LightsGuard(), current: 1, guardState: Open, target: 1);

        Assert.Equal(new[] { LightsWrite(1) }, open.Calls);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(1) }, closed.Calls);
        Assert.Empty(there.Calls);
    }

    [Fact]
    public void Readings_within_the_read_back_slack_count_as_the_position()
    {
        // 1.0004 is the open guard and the ON switch; 0.0004 is the closed guard.
        Assert.Empty(Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 2.0004, guardState: 1.0004, target: 2).Calls);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(2) },
            Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: 0.0004, target: 2).Calls);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(1) },
            Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: 1.0004, target: 1).Calls);
    }

    [Fact]
    public void A_state_write_goes_out_whatever_finite_value_the_control_reads()
    {
        // The write is absolute (SetLVar), so a switch reading off its positions is still written to.
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 7, guardState: Open, target: 2);

        Assert.Equal(new[] { LightsWrite(2) }, plan.Calls);
    }

    [Fact]
    public void Steps_are_a_pause_apart()
    {
        // The guard's click and animation must land before the state write follows it.
        Assert.Equal(300, Bbs757Guarded.StepGapMs);
    }
}
