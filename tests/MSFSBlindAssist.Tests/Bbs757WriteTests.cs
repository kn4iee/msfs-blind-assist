using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What a panel pick sends to the 757, decided from a fresh reading of the control. The rules the
/// definition must keep around <see cref="Bbs757Rpn"/>: an unread or unknown reading never presses
/// (a toggle's press flips it, whatever it reads), a control with no click code is written through
/// its state var (ruling R8 of plan 01), and a value that is not one of the control's positions is
/// refused.
/// </summary>
public class Bbs757WriteTests
{
    private const string Press = "(L:X,bool) ! (>L:X,bool)";
    private const string Cw = "(L:S, enum) 1 + 2 min (>L:S)";
    private const string Ccw = "(L:S, enum) 1 - 0 max (>L:S)";

    private static List<Bbs757Position> Positions(params double[] values)
        => values.Select(v => new Bbs757Position(v, "P" + v.ToString(CultureInfo.InvariantCulture))).ToList();

    private static Bbs757Control Toggle(string? press = Press) => new()
    {
        Node = "X", Label = "Thing", Kind = Bbs757Kinds.Toggle, StateVar = "X", Positions = Positions(0, 1), Press = press,
    };

    private static Bbs757Control Switch() => new()
    {
        Node = "S", Label = "Selector", Kind = Bbs757Kinds.Switch, StateVar = "S", Positions = Positions(0, 1, 2), Cw = Cw, Ccw = Ccw,
    };

    private static Bbs757Control CodelessSwitch(string? stateVar = "S") => new()
    {
        Node = "S", Label = "Heater", Kind = Bbs757Kinds.Switch, StateVar = stateVar, Positions = Positions(0, 0.5, 1),
    };

    [Fact]
    public void A_toggle_away_from_its_target_sends_its_press()
    {
        var plan = Bbs757Write.ToPosition(Toggle(), current: 0, target: 1);

        Assert.Equal(Bbs757WriteOutcome.Writes, plan.Outcome);
        Assert.Equal(new[] { Press }, plan.Calls);
    }

    [Fact]
    public void A_toggle_already_there_sends_nothing()
    {
        var plan = Bbs757Write.ToPosition(Toggle(), current: 1, target: 1);

        Assert.Equal(Bbs757WriteOutcome.AtPosition, plan.Outcome);
        Assert.Empty(plan.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unread_reading_never_presses(double? current)
    {
        var plan = Bbs757Write.ToPosition(Toggle(), current, target: 1);

        Assert.Equal(Bbs757WriteOutcome.Refused, plan.Outcome);
        Assert.Empty(plan.Calls);
    }

    [Fact]
    public void A_reading_that_is_no_position_never_presses()
    {
        // Mid-travel or an unexpected encoding: one press could land anywhere.
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Toggle(), current: 0.5, target: 1).Outcome);
    }

    [Fact]
    public void A_target_that_is_no_position_is_refused()
    {
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Toggle(), current: 0, target: 2).Outcome);
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Toggle(), current: 0, target: double.NaN).Outcome);
    }

    [Fact]
    public void A_switch_steps_by_position()
    {
        var plan = Bbs757Write.ToPosition(Switch(), current: 0, target: 2);

        Assert.Equal(Bbs757WriteOutcome.Writes, plan.Outcome);
        Assert.Equal(new[] { $"{Cw} {Cw}" }, plan.Calls);
    }

    [Fact]
    public void A_switch_reading_off_its_positions_is_refused()
    {
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Switch(), current: 7, target: 1).Outcome);
    }

    [Fact]
    public void A_switch_with_no_click_code_writes_its_state_var()
    {
        var plan = Bbs757Write.ToPosition(CodelessSwitch(), current: 0, target: 1);

        Assert.Equal(Bbs757WriteOutcome.Writes, plan.Outcome);
        Assert.Equal(new[] { "1 (>L:S)" }, plan.Calls);
    }

    [Fact]
    public void A_codeless_write_is_absolute_so_any_finite_reading_will_do()
    {
        Assert.Equal(new[] { "0 (>L:S)" }, Bbs757Write.ToPosition(CodelessSwitch(), current: 7, target: 0).Calls);
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(CodelessSwitch(), current: null, target: 0).Outcome);
    }

    [Fact]
    public void A_codeless_switch_already_there_sends_nothing()
    {
        Assert.Equal(Bbs757WriteOutcome.AtPosition, Bbs757Write.ToPosition(CodelessSwitch(), current: 0.5, target: 0.5).Outcome);
    }

    [Fact]
    public void A_codeless_write_is_invariant_under_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(new[] { "0.5 (>L:S)" }, Bbs757Write.ToPosition(CodelessSwitch(), current: 0, target: 0.5).Calls);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_codeless_control_without_a_state_var_is_refused()
    {
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(CodelessSwitch(stateVar: null), current: 0, target: 1).Outcome);
    }

    [Fact]
    public void A_toggle_without_a_press_writes_its_state_var()
    {
        // ToggleTo answers null for a missing press as well as for "already there": Press is checked first.
        var plan = Bbs757Write.ToPosition(Toggle(press: null), current: 0, target: 1);

        Assert.Equal(new[] { "1 (>L:X)" }, plan.Calls);
    }

    [Fact]
    public void The_battery_from_the_map_replays_its_own_click()
    {
        var battery = Bbs757ControlMap.Load().Controls.Single(c => c.Node == "B757_OVHD_BATTERY_BUTTON");

        var plan = Bbs757Write.ToPosition(battery, current: 0, target: 1);

        Assert.Equal(new[] { battery.Press! }, plan.Calls);
    }

    [Fact]
    public void A_button_press_is_held_briefly_then_released()
    {
        var button = new Bbs757Control { Node = "B", Kind = Bbs757Kinds.Button, Press = "1 (>L:B)", Release = "0 (>L:B)" };

        var press = Bbs757Write.PressFor(button);

        Assert.NotNull(press);
        Assert.Equal("1 (>L:B)", press!.Down);
        Assert.Equal("0 (>L:B)", press.Up);
        Assert.Equal(Bbs757Write.ButtonHoldMs, press.HoldMs);
        Assert.Equal(200, Bbs757Write.ButtonHoldMs);
    }

    [Fact]
    public void A_held_button_is_held_for_its_own_time()
    {
        var held = new Bbs757Control { Node = "H", Kind = Bbs757Kinds.Held, Press = "1 (>L:H)", Release = "0 (>L:H)", HoldMs = 3000 };
        Assert.Equal(3000, Bbs757Write.PressFor(held)!.HoldMs);

        held.HoldMs = 0;
        Assert.Equal(1500, Bbs757Write.PressFor(held)!.HoldMs);
    }

    [Fact]
    public void A_button_with_no_press_code_has_no_press()
    {
        Assert.Null(Bbs757Write.PressFor(new Bbs757Control { Node = "B", Kind = Bbs757Kinds.Button }));
    }

    [Fact]
    public void A_button_without_named_positions_has_no_readable_state()
    {
        // The MCP buttons pulse a _PRESS var: a state var, but nothing a pilot would call a position.
        Assert.False(Bbs757Write.HasReadableState(new Bbs757Control { Kind = Bbs757Kinds.Button, StateVar = "B_PRESS" }));
        Assert.False(Bbs757Write.HasReadableState(new Bbs757Control { Kind = Bbs757Kinds.Button }));
        Assert.True(Bbs757Write.HasReadableState(Toggle()));
    }
}
