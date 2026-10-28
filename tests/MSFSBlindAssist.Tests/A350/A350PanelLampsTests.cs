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
        Assert.Equal("ELEC", fault.Panel);
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
        Assert.Equal("APU start available light", Lamp("APU_START_SEQ1_LIGHT").Name);
        Assert.Contains(Lamps, l => l.Node == "APU_START_SEQ2_LIGHT");
    }

    [Fact]
    public void Lamps_of_sections_not_curated_are_not_shown()
    {
        Assert.DoesNotContain(Lamps, l => l.Node.StartsWith("EFIS_", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_legends_of_one_button_lit_by_one_rule_are_one_light()
    {
        var glareshield = A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Glareshield" });
        Assert.Single(glareshield, l => l.Node.StartsWith("FCU_ATC_MSG_LEFT_", StringComparison.Ordinal));
        Assert.Single(glareshield, l => l.Node.StartsWith("FCU_AUTO_LAND_LEFT_", StringComparison.Ordinal));
    }

    [Fact]
    public void The_master_warning_is_read_from_its_steady_state_not_its_flashing_legends()
    {
        // Live 2026-10-28: MASTER WARN flashes (0.5 s lit, 0.5 s dark) and the 1 Hz delivery caught only its dark phase
        // for a whole fire test, twice in three; INI_MASTER_WARNING_ACTIVE holds 1 for the whole warning, until the
        // pilot's press. One light per button, as both its legends now read the same state.
        var glareshield = A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Glareshield" });
        var warnings = glareshield.Where(l => l.Node.StartsWith("FCU_MASTER_WARNING_", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT", "FCU_MASTER_WARNING_RIGHT_SEQ1_LIGHT" }, warnings.Select(l => l.Node));
        Assert.All(warnings, l => Assert.Contains("INI_MASTER_WARNING_ACTIVE", l.Rule.Inputs));
        var captain = warnings[0].Rule;
        Assert.True(captain.IsLit(v => v == "INI_MASTER_WARNING_ACTIVE" ? 1 : 0));   // a warning, in a dark phase
    }

    [Theory]
    [InlineData("INI_MASTER_WARNING_CAPT_TOP")]
    [InlineData("INI_MASTER_WARNING_CAPT_BOTTOM")]
    public void The_master_warning_still_reads_its_own_legends(string legend)
    {
        // Review 2026-10-28: the steady state replaced the legends' own rule, so anything that lights a legend without a
        // warning (the annunciator test, another path) read Off and was never said. The steady state now adds to them.
        var glareshield = A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Glareshield" });
        var captain = glareshield.Single(l => l.Node == "FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT").Rule;
        Assert.True(captain.IsLit(v => v == legend ? 1 : 0));
        Assert.False(captain.IsLit(_ => 0));
    }

    [Fact]
    public void A_selectors_position_markers_are_left_out()
    {
        // Each ND mode and range position has a marker light; the selector row already says the position.
        var glareshield = A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Glareshield" });
        Assert.DoesNotContain(glareshield, l => l.Node.StartsWith("EFIS_MODE_", StringComparison.Ordinal));
        Assert.DoesNotContain(glareshield, l => l.Node.StartsWith("EFIS_RANGE_", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_oxygen_masks_flow_blinker_is_shown_with_no_bus()
    {
        // Live 2026-10-28: holding a mask's PRESS TO TEST turns its flow blinker yellow (INI_OXYTEST_BLINKER_CPT 1, the
        // oxy_test sound), 0 once released. A mechanical indicator with no lamp node and no bus.
        var cockpit = A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Cockpit" });
        var blinkers = cockpit.Where(l => l.Panel == "Oxygen Masks").ToList();
        Assert.Equal(new[] { "Captain", "First officer", "Third occupant", "Fourth occupant" }.Select(s => s + " oxygen flow blinker"),
            blinkers.Select(l => l.Name));
        var captain = blinkers[0];
        Assert.Equal("A350_LT_OXYTEST_BLINKER_CPT", captain.Key);
        Assert.Equal(A350LightPower.None, captain.Power);
        Assert.True(captain.IsLit(key => key == captain.Key ? 1 : null));
        Assert.False(captain.IsLit(key => key == captain.Key ? 0 : null));
    }

    [Theory]
    // Lamps the map has but no placed control owns (found 2026-10-28 by scanning every emissive material's variables
    // against what MSFSBA reads): each is shown on the panel a pilot finds it on. Every one of them (review, 2026-10-28).
    [InlineData("Overhead", "CARGO_SMOKE_BOTTLE_1_DISCH_LIGHT", "Cargo Smoke", "Bottle 1 discharged light")]
    [InlineData("Overhead", "CARGO_SMOKE_BOTTLE_2_DISCH_LIGHT", "Cargo Smoke", "Bottle 2 discharged light")]
    [InlineData("Overhead", "CARGO_SMOKE_BOTTLE_3_DISCH_LIGHT", "Cargo Smoke", "Bottle 3 discharged light")]
    [InlineData("Overhead", "VENT_AVNCS_SMOKE_SEQ1_LIGHT", "Ventilation", "Avionics smoke light")]
    [InlineData("Overhead", "MLG_BAY_LIGHT_SEQ1_LIGHT", "Fire", "MLG bay fire light")]
    [InlineData("Overhead", "IRS_ON_BAT_LIGHT", "ADIRS", "IRS on battery light")]
    [InlineData("Overhead", "CABIN_LAVATORY_SEQ1_LIGHT", "Cabin", "Lavatory occupied light")]
    [InlineData("Glareshield", "FCU_SIDESTICK_PRIORITY_LEFT_SEQ1_LIGHT", "Warnings", "Captain priority light")]
    [InlineData("Glareshield", "FCU_SIDESTICK_PRIORITY_LEFT_SEQ2_LIGHT", "Warnings", "Captain priority arrow light")]
    [InlineData("Glareshield", "FCU_SIDESTICK_PRIORITY_RIGHT_SEQ1_LIGHT", "Warnings", "First officer priority light")]
    [InlineData("Glareshield", "FCU_SIDESTICK_PRIORITY_RIGHT_SEQ2_LIGHT", "Warnings", "First officer priority arrow light")]
    [InlineData("Instrument", "LDG_ARROW_SEQ1_LIGHT", "Gear", "Gear lever red arrow light")]
    // Live 2026-10-28: RMP 1's BRT/OFF knob at 0 lit RMP_1_OFF_LIGHT_SEQ1; the lower legend's meaning is not known yet.
    [InlineData("Pedestal", "RMP_1_OFF_LIGHT_SEQ1_LIGHT", "Captain RMP", "Captain RMP off light")]
    [InlineData("Pedestal", "RMP_1_OFF_LIGHT_SEQ2_LIGHT", "Captain RMP", "Captain RMP off lower legend light")]
    [InlineData("Pedestal", "RMP_2_OFF_LIGHT_SEQ1_LIGHT", "First Officer RMP", "First officer RMP off light")]
    [InlineData("Pedestal", "RMP_2_OFF_LIGHT_SEQ2_LIGHT", "First Officer RMP", "First officer RMP off lower legend light")]
    [InlineData("Pedestal", "RMP_3_OFF_LIGHT_SEQ1_LIGHT", "RMP 3", "RMP 3 off light")]
    [InlineData("Pedestal", "RMP_3_OFF_LIGHT_SEQ2_LIGHT", "RMP 3", "RMP 3 off lower legend light")]
    public void A_lamp_no_control_owns_is_shown_where_the_pilot_sees_it(string section, string node, string panel, string name)
    {
        var lamps = A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { section });
        var lamp = Assert.Single(lamps, l => l.Node == node);
        Assert.Equal(panel, lamp.Panel);
        Assert.Equal(name, lamp.Name);
    }

    [Fact]
    public void A_lamp_knows_the_control_it_lights() =>
        Assert.Equal("AIRLINER_ELEC_BAT_1", Lamp("ELEC_BAT_1_SEQ1_LIGHT").ControlId);

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
