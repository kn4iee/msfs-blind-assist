using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>A cockpit lamp's state rule in the A350's own RPN, as the generated map carries it.</summary>
public class A350LampRuleTests
{
    private static Func<string, double?> Values(params (string Name, double Value)[] values) =>
        name => values.Where(v => v.Name == name).Select(v => (double?)v.Value).FirstOrDefault();

    [Fact]
    public void An_equality_rule_lights_at_its_value()
    {
        var rule = A350LampRule.Parse("(L:INI_BATTERY_1_FAULT) 1 ==");
        Assert.True(rule.IsLit(Values(("INI_BATTERY_1_FAULT", 1))));
        Assert.False(rule.IsLit(Values(("INI_BATTERY_1_FAULT", 0))));
    }

    [Fact]
    public void Its_inputs_are_listed_once_each_in_reading_order()
    {
        var rule = A350LampRule.Parse("(L:INI_GPU_AVAIL) 1 == (L:INI_GEN_EXT_A_ONLINE) 0 == and (L:INI_GPU_AVAIL) 0 > and");
        Assert.Equal(new[] { "INI_GPU_AVAIL", "INI_GEN_EXT_A_ONLINE" }, rule.Inputs);
    }

    [Fact]
    public void And_or_not_and_comparisons_evaluate_as_the_cockpit_does()
    {
        var adrOff = A350LampRule.Parse("(L:INI_ADR1_BUTTON) ! (L:INI_IRS1_STATE) 0 > and");
        Assert.True(adrOff.IsLit(Values(("INI_ADR1_BUTTON", 0), ("INI_IRS1_STATE", 2))));
        Assert.False(adrOff.IsLit(Values(("INI_ADR1_BUTTON", 0), ("INI_IRS1_STATE", 0))));

        var fire = A350LampRule.Parse("(L:INI_FIRE_ENG_1_FIRE) (L:INI_FIRE_TEST) 1 == or");
        Assert.True(fire.IsLit(Values(("INI_FIRE_ENG_1_FIRE", 0), ("INI_FIRE_TEST", 1))));
        Assert.False(fire.IsLit(Values(("INI_FIRE_ENG_1_FIRE", 0), ("INI_FIRE_TEST", 0))));

        var disagree = A350LampRule.Parse("(L:INI_ISOL_VALVE_AFT_BUTTON) (L:INI_ISOL_VALVE_AFT_STATE) !=");
        Assert.True(disagree.IsLit(Values(("INI_ISOL_VALVE_AFT_BUTTON", 1), ("INI_ISOL_VALVE_AFT_STATE", 0))));
    }

    [Fact]
    public void A_bare_variable_lights_at_half_or_more()
    {
        var rule = A350LampRule.Parse("(L:INI_ADR1_FAULT)");
        Assert.True(rule.IsLit(Values(("INI_ADR1_FAULT", 1))));
        Assert.False(rule.IsLit(Values(("INI_ADR1_FAULT", 0.2))));
    }

    [Fact]
    public void A_unit_on_a_variable_is_read_as_the_same_variable()
    {
        var rule = A350LampRule.Parse("(L:INI_GEAR_DOWN_ARROW_LIGHT, Bool)");
        Assert.Equal(new[] { "INI_GEAR_DOWN_ARROW_LIGHT" }, rule.Inputs);
    }

    [Fact]
    public void An_if_else_through_register_0_takes_one_branch()
    {
        var rule = A350LampRule.Parse(
            "(L:INI_FMS_SELECTOR) 0 == (L:INI_FCU_BACKUP_FMS1) 0 == or if{ 0 sp0 } els{ (L:INI_CPT_VOR1_ACTIVE, Bool) (L:INI_CPT_ADF1_ACTIVE) or sp0 } l0");
        Assert.False(rule.IsLit(Values(("INI_FMS_SELECTOR", 0), ("INI_FCU_BACKUP_FMS1", 0),
            ("INI_CPT_VOR1_ACTIVE", 1), ("INI_CPT_ADF1_ACTIVE", 1))));
        Assert.True(rule.IsLit(Values(("INI_FMS_SELECTOR", 1), ("INI_FCU_BACKUP_FMS1", 1),
            ("INI_CPT_VOR1_ACTIVE", 1), ("INI_CPT_ADF1_ACTIVE", 0))));
    }

    [Fact]
    public void An_unread_input_on_the_branch_taken_leaves_the_lamp_undecided()
    {
        var rule = A350LampRule.Parse("(L:INI_A) 1 == (L:INI_B) and");
        Assert.Null(rule.IsLit(Values(("INI_A", 1))));
    }

    [Fact]
    public void Multiplication_is_evaluated()
    {
        Assert.Equal(10, A350LampRule.Parse("(L:INI_DC_LIGHTS_FAILURE) 10 *").Evaluate(Values(("INI_DC_LIGHTS_FAILURE", 1))));
    }

    [Theory]
    [InlineData("(L:INI_A) 1 +")]          // an operator the cockpit does not use
    [InlineData("(L:INI_A) 10 * (L:INI_B) 0 ==")]   // leaves two values
    [InlineData("(L:INI_A) if{ 1 sp0 }")]  // an if with no else
    public void A_form_the_cockpit_does_not_use_is_refused(string state) =>
        Assert.Throws<FormatException>(() => A350LampRule.Parse(state));

    [Fact]
    public void Every_lamp_on_a_curated_section_parses()
    {
        var map = A350ControlMap.Load();
        foreach (var lamp in A350PanelLamps.Resolve(map, A350PanelLayout.Place(map), new[] { "Overhead" }))
            Assert.NotNull(lamp.Rule);
    }
}
