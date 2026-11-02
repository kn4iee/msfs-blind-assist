using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The Altimeter window's one entry, planned from one read (measured in flight 2026-11-02, docs/a350/evidence/02-altimeter.md):
/// LINKED INSTR 0 NONE, 1 CPT &amp; FO (a side's click copies the other side, the ISIS apart), 2 CPT/FO &amp; ISIS (a click on any of the three
/// copies all, and the ISIS's click takes both sides off STD).</summary>
public class A350BaroEntryTests
{
    private const double Std = 3, Qnh = 0, Hpa = 1, Inches = 0;

    private static Func<string, double?> State(double link = 0, double mode1 = Std, double mode2 = Std, double unit1 = Hpa, double unit2 = Hpa)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [A350BaroEntry.LinkedVar] = link,
            ["XMLVAR_Baro1_Mode"] = mode1,
            ["XMLVAR_Baro2_Mode"] = mode2,
            ["XMLVAR_Baro_Selector_HPA_1"] = unit1,
            ["XMLVAR_Baro_Selector_HPA_2"] = unit2,
            ["KOHLSMAN SETTING MB:1"] = 1013.25,
            ["KOHLSMAN SETTING MB:2"] = 1013.25,
            ["KOHLSMAN SETTING MB:3"] = 1013.25,
            ["INI_BARO_PRESET_CPT"] = 0,
            ["INI_BARO_PRESET_CPT_SHOW"] = 0,
            ["INI_BARO_PRESET_FO"] = 0,
            ["INI_BARO_PRESET_FO_SHOW"] = 0,
        };
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    private static Func<string, double?> Without(Func<string, double?> read, string name) => n => n == name ? null : read(n);

    private static A350TypedField[] Fields(A350BaroEntryPlan plan) => plan.Targets.Select(t => t.Field).ToArray();

    [Fact]
    public void Inches_typed_on_two_hectopascal_sides_refuses_it_all_and_sends_nothing()
    {
        var plan = A350BaroEntry.Plan(30.5, State());
        Assert.Equal("QNH must be between 900 and 1100 hectopascals.", plan.Refusal);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void Hectopascals_typed_on_two_inches_sides_refuses_it_all_and_sends_nothing()
    {
        var plan = A350BaroEntry.Plan(1033, State(unit1: Inches, unit2: Inches));
        Assert.Equal("QNH must be between 26.6 and 32.5 inches.", plan.Refusal);
        Assert.Empty(plan.Targets);
    }

    [Theory]
    [InlineData(1033)]
    [InlineData(30.5)]
    public void Sides_in_different_units_refuse_naming_each_unit(double typed)
    {
        var plan = A350BaroEntry.Plan(typed, State(unit1: Hpa, unit2: Inches));
        Assert.Equal("QNH not set: the captain's baro shows hectopascals, the first officer's inches. Set both sides to one unit first.", plan.Refusal);
        Assert.Empty(plan.Targets);
        Assert.Equal("QNH not set: the captain's baro shows inches, the first officer's hectopascals. Set both sides to one unit first.",
            A350BaroEntry.Plan(typed, State(unit1: Inches, unit2: Hpa)).Refusal);
    }

    [Fact]
    public void An_unread_linked_option_refuses()
    {
        var plan = A350BaroEntry.Plan(1033, Without(State(), A350BaroEntry.LinkedVar));
        Assert.Equal(A350WritePlan.UnknownPositionRefusal, plan.Refusal);
        Assert.Empty(plan.Targets);
    }

    [Theory]
    [InlineData("XMLVAR_Baro2_Mode")]
    [InlineData("XMLVAR_Baro_Selector_HPA_1")]
    [InlineData("KOHLSMAN SETTING MB:3")]
    [InlineData("INI_BARO_PRESET_FO_SHOW")]
    public void Any_unread_input_of_the_three_refuses(string name)
    {
        var plan = A350BaroEntry.Plan(1033, Without(State(), name));
        Assert.Equal(A350WritePlan.UnknownPositionRefusal, plan.Refusal);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void Not_linked_plans_three_targets_in_order()
    {
        var plan = A350BaroEntry.Plan(1033, State(mode1: Qnh, mode2: Qnh));
        Assert.Null(plan.Refusal);
        Assert.False(plan.Linked);
        Assert.Equal(new[] { A350TypedEntry.CaptainQnh, A350TypedEntry.FirstOfficerQnh, A350TypedEntry.IsisQnh }, Fields(plan));
        Assert.Empty(plan.SidesOnStd);
    }

    [Fact]
    public void Linked_cpt_and_fo_plans_the_captains_side_and_the_isis_the_link_leaves_out()
    {
        var plan = A350BaroEntry.Plan(1033, State(link: 1, mode1: Qnh, mode2: Qnh));
        Assert.True(plan.Linked);
        Assert.Equal(new[] { A350TypedEntry.CaptainQnh, A350TypedEntry.IsisQnh }, Fields(plan));
    }

    [Fact]
    public void Linked_with_the_isis_plans_the_captains_side_only()
    {
        var plan = A350BaroEntry.Plan(1033, State(link: 2, mode1: Qnh, mode2: Qnh));
        Assert.True(plan.Linked);
        Assert.Equal(new[] { A350TypedEntry.CaptainQnh }, Fields(plan));
        Assert.Equal("16512 1 r (>K:2:KOHLSMAN_SET)", plan.Targets[0].Plan.Steps.OfType<A350CalcStep>().First().Rpn);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Linked_on_std_plans_the_captains_preset(double link)
    {
        var plan = A350BaroEntry.Plan(1033, State(link: link));
        var captain = plan.Targets[0];
        Assert.Equal(A350TypedEntry.CaptainQnh, captain.Field);
        Assert.NotNull(captain.Plan.Stepper);
        Assert.Equal(new[] { 1, 2 }, plan.SidesOnStd);
    }

    [Fact]
    public void No_isis_target_on_std_where_its_click_takes_the_sides_off_it()
    {
        // LINKED INSTR 2, both sides STD: the ISIS's click set both sides to QNH at its own value.
        Assert.DoesNotContain(A350TypedEntry.IsisQnh, Fields(A350BaroEntry.Plan(1033, State(link: 2))));
        Assert.DoesNotContain(A350TypedEntry.IsisQnh, Fields(A350BaroEntry.Plan(1033, State(link: 2, mode1: Std, mode2: Qnh))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_isis_is_set_on_std_where_its_click_leaves_the_sides_alone(double link)
    {
        // Measured: short write and click moved MB:3 only, both modes stayed 3.
        var plan = A350BaroEntry.Plan(1033, State(link: link));
        Assert.Contains(A350TypedEntry.IsisQnh, Fields(plan));
        Assert.Equal(A350TypedEntry.IsisQnh, plan.Targets[^1].Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void An_isis_on_its_own_std_takes_no_target_and_is_said(double link)
    {
        var plan = A350BaroEntry.Plan(1033, State(link: link), isisOnStd: true);
        Assert.Null(plan.Refusal);
        Assert.DoesNotContain(A350TypedEntry.IsisQnh, Fields(plan));
        Assert.Equal(A350TypedEntry.CaptainQnh, plan.Targets[0].Field);
        Assert.Equal(A350BaroEntry.IsisOnStdNotice, plan.Notice);
    }

    [Fact]
    public void An_isis_not_on_std_or_unread_says_nothing_of_it()
    {
        Assert.Null(A350BaroEntry.Plan(1033, State(), isisOnStd: false).Notice);
        var unread = A350BaroEntry.Plan(1033, State(), isisOnStd: null);
        Assert.Null(unread.Notice);
        Assert.Contains(A350TypedEntry.IsisQnh, Fields(unread));
    }

    [Fact]
    public void Sides_on_std_are_named_for_the_check_afterwards()
    {
        Assert.Equal(new[] { 2 }, A350BaroEntry.Plan(1033, State(mode1: Qnh, mode2: Std)).SidesOnStd);
        Assert.Equal(new[] { 1 }, A350BaroEntry.Plan(1033, State(mode1: Std, mode2: Qnh)).SidesOnStd);
    }

    [Fact]
    public void A_not_a_number_is_refused_as_every_typed_value_is()
    {
        var plan = A350BaroEntry.Plan(double.NaN, State());
        Assert.NotNull(plan.Refusal);
        Assert.Empty(plan.Targets);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_refusal_reads_the_same_under_de_DE_and_sv_SE(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.Equal("QNH must be between 26.6 and 32.5 inches.", A350BaroEntry.Plan(1033, State(unit1: Inches, unit2: Inches)).Refusal);
            Assert.Equal("QNH not set: the captain's baro shows hectopascals, the first officer's inches. Set both sides to one unit first.",
                A350BaroEntry.Plan(1033, State(unit2: Inches)).Refusal);
            Assert.Equal("16512 1 r (>K:2:KOHLSMAN_SET)",
                A350BaroEntry.Plan(1033, State(link: 2, mode1: Qnh, mode2: Qnh)).Targets[0].Plan.Steps.OfType<A350CalcStep>().First().Rpn);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
