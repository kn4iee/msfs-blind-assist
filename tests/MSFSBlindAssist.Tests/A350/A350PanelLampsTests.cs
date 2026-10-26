using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The cockpit lights each curated panel shows, matched to their rows from the generated map.</summary>
public class A350PanelLampsTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();
    private static readonly IReadOnlyList<A350Lamp> Lamps =
        A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Overhead" });

    private static A350Lamp Lamp(string node) => Assert.Single(Lamps, l => l.Node == node);

    [Fact]
    public void A_fault_legend_is_shown_on_its_rows_panel_named_after_the_row()
    {
        var fault = Lamp("ELEC_BAT_1_SEQ1_LIGHT");
        Assert.Equal("Electrical", fault.Panel);
        Assert.Equal("BAT 1 fault light", fault.Name);
        Assert.Equal("A350_LT_ELEC_BAT_1_SEQ1_LIGHT", fault.Key);
    }

    [Fact]
    public void A_legend_that_only_mirrors_its_own_switch_is_left_out()
    {
        Assert.DoesNotContain(Lamps, l => l.Node == "ELEC_BAT_1_SEQ2_LIGHT");      // OFF: (BAT 1 switch) 0 ==
        Assert.DoesNotContain(Lamps, l => l.Node == "ANTI_ICE_WING_SEQ2_LIGHT");   // ON
    }

    [Fact]
    public void A_legend_that_reads_more_than_its_switch_is_kept()
    {
        Assert.Contains(Lamps, l => l.Node == "IRS_ADR_1_SEQ2_LIGHT");   // OFF only while its IRS runs
        Assert.Contains(Lamps, l => l.Node == "CARGO_AIR_COND_AFT_ISOL_VALVE_SEQ1_LIGHT");   // button and valve disagree
    }

    [Fact]
    public void A_button_with_no_position_shows_every_legend()
    {
        Assert.Equal("Start available light", Lamp("APU_START_SEQ1_LIGHT").Name);
        Assert.Contains(Lamps, l => l.Node == "APU_START_SEQ2_LIGHT");
    }

    [Fact]
    public void Lamps_of_sections_not_curated_are_not_shown()
    {
        Assert.DoesNotContain(Lamps, l => l.Node.StartsWith("EFIS_", StringComparison.Ordinal));
    }

    [Fact]
    public void No_two_lamps_on_a_panel_share_a_name()
    {
        foreach (var panel in Lamps.GroupBy(l => l.Panel))
            Assert.Equal(panel.Count(), panel.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void No_lamp_shares_a_name_with_a_row_on_its_panel()
    {
        var placement = A350PanelLayout.Place(Map);
        foreach (var lamp in Lamps)
            Assert.DoesNotContain(placement.RowsByPanel[lamp.Panel], r => string.Equals(r.Name, lamp.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_lamp_is_lit_only_while_its_rule_holds_and_its_bus_lights_it()
    {
        var fault = Lamp("ELEC_BAT_1_SEQ1_LIGHT");   // DC
        Func<string, double?> read(double state, double? power) => key =>
            key == A350PanelLamps.DcPowerKey ? power : key == fault.Key ? state : null;
        Assert.True(fault.IsLit(read(1, 1)));
        Assert.False(fault.IsLit(read(1, 0)));
        Assert.False(fault.IsLit(read(0, 1)));
        Assert.Null(fault.IsLit(read(1, null)));
    }

    [Fact]
    public void Each_input_arrives_under_one_key_the_first_under_the_lamps_own()
    {
        var off = Lamp("IRS_ADR_1_SEQ2_LIGHT");   // (L:INI_ADR1_BUTTON) ! (L:INI_IRS1_STATE) 0 > and
        Assert.Equal(off.Key, off.KeyFor("INI_ADR1_BUTTON"));
        Assert.Equal("A350_IN_INI_IRS1_STATE", off.KeyFor("INI_IRS1_STATE"));
        Assert.Equal(new[] { off.Key, "A350_IN_INI_IRS1_STATE", A350PanelLamps.AcPowerKey }, off.InputKeys);
    }
}
