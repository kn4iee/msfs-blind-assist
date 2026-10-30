using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Where each A350 control sits and what it is called, from the generated map.</summary>
public class A350PanelLayoutTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    private static A350PlacedRow Row(string panel, string key) => Placement.RowsByPanel[panel].Single(r => r.Key == key);

    [Fact]
    public void The_overhead_panels_follow_the_A380s_order()
    {
        Assert.Equal(new[]
        {
            "ELEC", "APU", "Fuel", "Hydraulics", "Bleed Air", "Air Conditioning", "Pressurization", "Ventilation", "Cargo Air",
            "Anti Ice", "Fire", "Cargo Smoke", "Cabin Smoke", "Oxygen", "Calls", "Evacuation", "Signs", "Captain Wiper",
            "First Officer Wiper", "ADIRS", "Flight Control Computers", "Engine FADEC and Manual Start", "Recorder and Misc",
            "Surveillance", "Cabin", "OIS", "Maintenance", "Interior Lighting", "Exterior Lighting",
        }, Placement.Structure["Overhead"]);
    }

    [Fact]
    public void The_instrument_panels_follow_the_A380s_order()
    {
        Assert.Equal(new[]
        {
            "Gear", "Autobrake", "ISIS", "SND", "Source Switching", "Display Power", "Captain Display Power",
            "First Officer Display Power", "Captain Side Panel", "Captain Individual Panel", "First Officer Individual Panel",
            "First Officer Side Panel",
        }, Placement.Structure["Instrument"]);
    }

    /// <summary>Owner 2026-10-27 (backlog 19, "split them" 2026-10-29): no panel holds both pilots' controls, even where the
    /// A380 keeps one (its Wipers). Each side's rows have a panel of their own, after the panel the rest stay on.</summary>
    [Fact]
    public void No_panel_holds_both_pilots_controls()
    {
        var mixed = Placement.RowsByPanel
            .Where(p => p.Value.Any(r => r.Name.StartsWith("Captain ", StringComparison.Ordinal))
                && p.Value.Any(r => r.Name.StartsWith("First officer ", StringComparison.Ordinal)))
            .Select(p => p.Key);
        Assert.Empty(mixed);
    }

    /// <summary>A control with no side on a split panel's group (one a later map could add) stays on the shared panel, in
    /// its place before the two sides, never at the end of the section (review 2026-10-29).</summary>
    [Fact]
    public void A_control_with_no_side_on_a_split_group_keeps_the_shared_panel_in_its_place()
    {
        var test = new A350Control
        {
            Id = "AIRLINER_WIPER_TEST", Key = "A350_WIPER_TEST", Area = "OVERHEAD", Panel = "WIPERS", Title = "WIPER TEST",
            Kind = A350Kinds.Button,
        };
        Assert.Equal(("Overhead", "Wipers"), (A350PanelLayout.SectionFor(test), A350PanelLayout.PanelFor(test)));
        var overhead = A350PanelLayout.Place(new A350ControlMap { Controls = A350ControlMap.Load().Controls.Append(test).ToList() })
            .Structure["Overhead"];
        Assert.Equal(new[] { "Signs", "Wipers", "Captain Wiper", "First Officer Wiper", "ADIRS" },
            overhead.SkipWhile(p => p != "Signs").Take(5));
    }

    [Fact]
    public void A_side_panel_holds_only_its_sides_rows()
    {
        foreach (var (panel, rows) in Placement.RowsByPanel)
        {
            if (panel.StartsWith("Captain ", StringComparison.Ordinal))
                Assert.All(rows, r => Assert.StartsWith("Captain ", r.Name));
            if (panel.StartsWith("First Officer ", StringComparison.Ordinal))
                Assert.All(rows, r => Assert.StartsWith("First officer ", r.Name));
        }
    }

    [Fact]
    public void Sections_come_in_cockpit_order()
    {
        Assert.Equal(new[] { "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit", "Exterior" }, Placement.Structure.Keys);
    }

    [Fact]
    public void A_toggle_row_offers_its_positions_in_sentence_case()
    {
        var bat = Row("ELEC", "A350_ELEC_BAT_1");
        Assert.Equal("BAT 1", bat.Name);
        Assert.Equal(A350RowAction.Set, bat.Action);
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, bat.Positions);
    }

    [Fact]
    public void A_selector_keeps_the_tooltip_words_by_state_value()
    {
        Assert.Equal(new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" },
            Row("Signs", "A350_SIGNS_SEAT_BELTS").Positions);
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
        Assert.Equal("ENG 1 bleed", Row("Bleed Air", "A350_AIR_ENG_BLEED_1").Name);
        Assert.Equal("ENG 2 FADEC GND PWR", Row("Engine FADEC and Manual Start", "A350_MAINT_ENG_FADEC_GND_POWER_2").Name);
        Assert.Equal("Captain wiper", Row("Captain Wiper", "A350_WIPER_LEFT").Name);
        Assert.Equal("ENG 2 agent 1", Row("Fire", "A350_FIRE_ENG_2_AGENT_1").Name);
    }

    [Fact]
    public void Jettison_controls_sit_on_the_fuel_panel()
    {
        Assert.Contains(Placement.RowsByPanel["Fuel"], r => r.Key == "A350_JETTISON_ARM");
        Assert.DoesNotContain(Placement.Structure["Overhead"], p => p == "Fuel Jettison");
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
    public void An_encoder_whose_value_is_not_shown_is_an_increase_and_a_decrease_row()
    {
        var rows = Placement.RowsByPanel.Values.SelectMany(r => r).Where(r => r.Control?.Id == "AIRLINER_WXR_GAIN_LEFT").ToList();
        Assert.Equal(new[] { A350RowAction.Increase, A350RowAction.Decrease }, rows.Select(r => r.Action));
    }

    [Fact]
    public void An_encoder_whose_value_is_shown_is_one_typed_row_in_its_place()
    {
        var rows = Placement.RowsByPanel["Pressurization"];
        var typed = Assert.Single(rows, r => r.Control?.Id == "AIRLINER_CABIN_PRESS_ALTITUDE_TARGET");
        Assert.Equal(("A350_CABIN_ALT_TARGET_SET", "Cabin altitude target (feet)", A350RowAction.Typed), (typed.Key, typed.Name, typed.Action));
        Assert.Equal(0, rows.IndexOf(typed));   // where its increase row was
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
