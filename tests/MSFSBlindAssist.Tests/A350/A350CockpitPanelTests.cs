using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The Cockpit section (iniBuilds' MISC area): the cockpit door and hatches, sun shades, oxygen masks, HUD
/// units, tillers and sidesticks, with positions named by what they do.</summary>
public class A350CockpitPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());
    private const A350RowAction Set = A350RowAction.Set;
    private const A350RowAction Press = A350RowAction.Press;

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p =>
                $"{p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)}={p.Value}"))));

    // The seats, tables and footrests (A350_Interior_SST's clickspots, so not in the map), live 2026-10-30: a seat switch rests at 1;
    // BKFWD 2 moves the seat forward, 0 back; UPDOWN 0 up, 2 down; each ANIMATION is the seat's place, 0 to 100.
    [Fact]
    public void The_seats_are_nudges_and_levers()
    {
        Assert.Equal(new[]
        {
            ("Captain seat forward (nudge)", Press, ""), ("Captain seat back (nudge)", Press, ""), ("Captain seat up (nudge)", Press, ""),
            ("Captain seat down (nudge)", Press, ""), ("Captain shoulder harness", Set, "0=Free, 1=Locked"),
            ("Captain left armrest", Set, "0=Stowed, 1=Deployed"), ("Captain right armrest", Set, "0=Stowed, 1=Deployed"),
        }, Rows("Captain Seat"));
        Assert.Equal(new[] { ("Jumpseat", Set, "0=Stowed, 1=Deployed"), ("Observer left armrest", Set, "0=Stowed, 1=Deployed") }, Rows("Observer Seat"));
        var steps = A350Levers.PressStepsFor(Placement.RowsByPanel["First Officer Seat"].Single(r => r.Name == "First officer seat down (nudge)").Control!);
        Assert.Equal("2 (>L:INI_SEAT_FO_UPDOWN_SWITCH)", ((A350CalcStep)steps![0]).Rpn);
        Assert.Equal("1 (>L:INI_SEAT_FO_UPDOWN_SWITCH)", ((A350CalcStep)steps[2]).Rpn);
    }

    [Fact]
    public void The_tables_are_the_sliding_table_keyboard_cover_and_footrest()
    {
        Assert.Equal(new[]
        {
            ("Captain sliding table", Set, "0=Stowed, 1=Deployed"), ("Captain keyboard cover", Set, "0=Closed, 1=Open"), ("Captain footrest", Set, "0=Stowed, 1=Deployed"),
        }, Rows("Captain Table"));
        var table = Placement.RowsByPanel["First Officer Table"].Single(r => r.Name == "First officer sliding table").Control!;
        Assert.Equal("L:INI_FO_TRAY_COVER_TGT", table.StateVar);
        // Stowing the table closes its cover too, as its own click does.
        Assert.Equal("0 (>L:INI_FO_TRAY_COVER_TGT) 0 (>L:INI_FO_TRAY)", A350Levers.WriteFor(table, 0));
        Assert.Equal("1 (>L:INI_FO_TRAY_COVER_TGT)", A350Levers.WriteFor(table, 1));
    }

    [Fact]
    public void The_seats_place_is_a_status_line()
    {
        var line = A350StatusLines.All.Single(l => l.Key == "A350_STAT_SEAT_CPT_FORE_AFT");
        Assert.Equal("INI_SEAT_CPT_BKFWD_ANIMATION", line.Var);
        Assert.Equal("68 percent", line.Format(68.2));
        Assert.Contains(A350StatusLines.All, l => l.Key == "A350_STAT_SEAT_FO_HEIGHT" && l.Var == "INI_SEAT_FO_UPDOWN_ANIMATION");
    }

    [Fact]
    public void The_cockpit_panels()
    {
        // Each side's on a panel of its own after the shared one (owner 2026-10-27), singular where a side has one; the
        // shared ones named by what they still hold (review 2026-10-29).
        Assert.Equal(new[]
        {
            "Doors and Hatches", "Center Sun Shade", "Captain Sun Shades", "First Officer Sun Shades", "Observer Oxygen Masks",
            "Captain Oxygen Mask", "First Officer Oxygen Mask", "Captain HUD Unit", "First Officer HUD Unit",
            "Captain Tiller and Sidestick", "First Officer Tiller and Sidestick", "Captain Seat", "First Officer Seat", "Observer Seat",
            "Captain Table", "First Officer Table", "Misc",
        }, Placement.Structure["Cockpit"]);
    }

    [Fact]
    public void Doors_and_hatches()
    {
        // Locking closes the door (its set code); the outer handle moves the same door, so it is not a second row.
        Assert.Equal(new[]
        {
            ("Cockpit door lock", Set, "0=Unlocked, 1=Locked"),
            ("Cockpit door", Set, "0=Closed, 1=Open"),
            ("Avionics bay hatch", Set, "0=Closed, 1=Open"),
        }, Rows("Doors and Hatches"));
    }

    [Theory]
    [InlineData("Captain Sun Shades", "Captain")]
    [InlineData("First Officer Sun Shades", "First officer")]
    public void Sun_shades(string panel, string side)
    {
        Assert.Equal(new[]
        {
            ($"{side} sun shade", Set, ""),
            ($"{side} side sun shade", Set, "0=Stowed, 1=Deployed"),
            ($"{side} rear sun shade", Set, "0=Stowed, 1=Deployed"),
        }, Rows(panel));
        Assert.Equal(new[] { ("Center sun shade", Set, "0=Stowed, 1=Deployed") }, Rows("Center Sun Shade"));
    }

    [Theory]
    [InlineData("Captain Oxygen Mask", "Captain")]
    [InlineData("First Officer Oxygen Mask", "First officer")]
    [InlineData("Observer Oxygen Masks", "Third occupant")]
    [InlineData("Observer Oxygen Masks", "Fourth occupant")]
    public void Oxygen_masks_by_seat(string panel, string seat)
    {
        var rows = Rows(panel).ToList();
        Assert.Contains(($"{seat} oxygen mask stowage", Set, "0=Closed, 1=Open"), rows);   // its tooltip names the click
        Assert.Contains(($"{seat} oxygen N/100%", Set, "0=Normal, 1=100%"), rows);
        Assert.Contains(($"{seat} oxygen mask test", Press, ""), rows);
        Assert.Contains(($"{seat} oxygen emergency pressure", Press, ""), rows);
        Assert.Equal(panel == "Observer Oxygen Masks" ? 8 : 4, rows.Count);   // the two observers' masks share the panel
    }

    [Theory]
    [InlineData("Captain HUD Unit", "Captain")]
    [InlineData("First Officer HUD Unit", "First officer")]
    public void Hud_units_are_the_hud_options(string panel, string side)
    {
        // The handle deploys the combiner: named so, not "Captain HUD", the glareshield's HUD key panel (review 2026-10-29).
        Assert.Equal(new[]
        {
            ($"{side} HUD cover", Set, "0=Off, 1=On"),
            ($"{side} HUD combiner", Set, "0=Stowed, 1=Deployed"),
        }, Rows(panel));
        Assert.Equal("INI_IS_HUD", A350Options.For("AIRLINER_HUD_HANDLE_LEFT")!.Var);
    }

    [Theory]
    [InlineData("Captain Tiller and Sidestick", "Captain")]
    [InlineData("First Officer Tiller and Sidestick", "First officer")]
    public void Tillers_and_sidesticks(string panel, string side)
    {
        // Both tillers' disconnects flip one variable (INI_TILLER_DISC).
        Assert.Equal(new[]
        {
            ($"{side} sidestick takeover", Press, ""),
            ($"{side} tiller", Set, "0=Connected, 1=Disconnected"),
        }, Rows(panel));
    }

    [Fact]
    public void Misc()
    {
        // The eight clickspots on the torn paper all hide it (INI_PRINTER_SIDE_PAPER_VIS 0): one row.
        Assert.Equal(new[]
        {
            ("Dome light override", Set, "0=Off, 1=On"),
            ("Torn paper put away", Press, ""),
            ("Pen", Set, "0=Off, 1=On"),
        }, Rows("Misc"));
    }
}
