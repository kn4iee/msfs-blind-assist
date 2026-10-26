using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Where each A350 control sits and what it is called, from the generated map.</summary>
public class A350PanelLayoutTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    private static A350PlacedRow Row(string panel, string key) => Placement.RowsByPanel[panel].Single(r => r.Key == key);

    [Fact]
    public void The_overhead_panels_follow_the_manuals_cockpit_layout()
    {
        Assert.Equal(new[]
        {
            "Wipers", "Calls and Evacuation", "Oxygen", "Recorder", "ELT", "Emergency Electrical Power", "Fuel Jettison",
            "Flight Controls", "ADIRS", "Exterior Lighting", "APU", "Interior Lighting and Signs", "Anti-Ice",
            "Air Conditioning", "Cabin Pressure", "Electrical", "Fuel", "Hydraulics", "Fire", "Engine Manual Start",
            "Ventilation", "Cargo Air Conditioning", "Cargo Smoke", "Cabin Smoke", "Ground Hydraulics", "Cabin", "OIS",
            "Surveillance", "Maintenance",
        }, Placement.Structure["Overhead"]);
    }

    [Fact]
    public void The_instrument_panels_read_left_to_right_across_the_cockpit()
    {
        Assert.Equal(new[]
        {
            "Captain Side Panel", "Captain Individual Panel", "Display Power", "FMS and Air Data", "Landing Gear and Brakes",
            "First Officer Individual Panel", "First Officer Side Panel",
        }, Placement.Structure["Instrument"]);
    }

    [Fact]
    public void Sections_come_in_cockpit_order()
    {
        Assert.Equal(new[] { "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit" }, Placement.Structure.Keys);
    }

    [Fact]
    public void A_toggle_row_offers_its_positions_in_sentence_case()
    {
        var bat = Row("Electrical", "A350_ELEC_BAT_1");
        Assert.Equal("BAT 1", bat.Name);
        Assert.Equal(A350RowAction.Set, bat.Action);
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, bat.Positions);
    }

    [Fact]
    public void A_selector_keeps_the_tooltip_words_by_state_value()
    {
        Assert.Equal(new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" },
            Row("Interior Lighting and Signs", "A350_SIGNS_SEAT_BELTS").Positions);
    }

    [Fact]
    public void Sentence_long_position_words_read_as_the_switch_is_marked()
    {
        Assert.Equal(new Dictionary<double, string> { [0] = "1", [1] = "2", [2] = "Off" }, Row("Exterior Lighting", "A350_LIGHTS_EXT_NAV").Positions);
        Assert.Equal(new Dictionary<double, string> { [0] = "T.O", [1] = "Taxi", [2] = "Off" }, Row("Exterior Lighting", "A350_LIGHTS_EXT_NOSE").Positions);
    }

    [Fact]
    public void Misnamed_and_shared_titles_are_named_apart()
    {
        Assert.Equal("ENG 1 bleed", Row("Air Conditioning", "A350_AIR_ENG_BLEED_1").Name);
        Assert.Equal("ENG 2 FADEC GND PWR", Row("Maintenance", "A350_MAINT_ENG_FADEC_GND_POWER_2").Name);
        Assert.Equal("Captain wiper", Row("Wipers", "A350_WIPER_LEFT").Name);
        Assert.Equal("ENG 2 agent 1", Row("Fire", "A350_FIRE_ENG_2_AGENT_1").Name);
    }

    [Fact]
    public void Jettison_controls_sit_on_their_own_panel()
    {
        Assert.Contains(Placement.RowsByPanel["Fuel Jettison"], r => r.Key == "A350_JETTISON_ARM");
        Assert.DoesNotContain(Placement.RowsByPanel["Fuel"], r => r.Key == "A350_JETTISON_ARM");
    }

    [Fact]
    public void Overhead_guard_covers_are_not_rows()
    {
        var overhead = Placement.Structure["Overhead"].SelectMany(p => Placement.RowsByPanel[p]);
        Assert.DoesNotContain(overhead, r => r.Key.EndsWith("_COVER", StringComparison.Ordinal));
    }

    [Fact]
    public void Buttons_and_hold_buttons_are_presses()
    {
        Assert.Equal(A350RowAction.Press, Row("APU", "A350_APU_START").Action);
        Assert.Equal(A350RowAction.Press, Row("Fire", "A350_FIRE_TEST").Action);
    }

    [Fact]
    public void An_encoder_is_an_increase_and_a_decrease_row()
    {
        var rows = Placement.RowsByPanel["Cabin Pressure"].Select(r => r.Name).ToList();
        Assert.Contains("Cabin altitude target increase", rows);
        Assert.Contains("Cabin altitude target decrease", rows);
    }

    [Fact]
    public void No_overhead_panel_has_two_rows_with_one_name()
    {
        foreach (var panel in Placement.Structure["Overhead"])
        {
            var rows = Placement.RowsByPanel[panel];
            var dupes = rows.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, $"{panel}: {string.Join(", ", dupes)}");
        }
    }
}
