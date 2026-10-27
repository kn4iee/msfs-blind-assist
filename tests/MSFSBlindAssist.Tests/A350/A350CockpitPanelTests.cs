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

    [Fact]
    public void The_cockpit_panels()
    {
        Assert.Equal(new[] { "Doors and Hatches", "Sun Shades", "Oxygen Masks", "HUD Units", "Tillers and Sidesticks", "Misc" },
            Placement.Structure["Cockpit"]);
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

    [Fact]
    public void Sun_shades()
    {
        Assert.Equal(new[]
        {
            ("Captain sun shade", Set, ""),
            ("Captain side sun shade", Set, "0=Stowed, 1=Deployed"),
            ("Captain rear sun shade", Set, "0=Stowed, 1=Deployed"),
            ("First officer sun shade", Set, ""),
            ("First officer side sun shade", Set, "0=Stowed, 1=Deployed"),
            ("First officer rear sun shade", Set, "0=Stowed, 1=Deployed"),
            ("Center sun shade", Set, "0=Stowed, 1=Deployed"),
        }, Rows("Sun Shades"));
    }

    [Fact]
    public void Oxygen_masks_by_seat()
    {
        var rows = Rows("Oxygen Masks").ToList();
        foreach (var seat in new[] { "Captain", "First officer", "Third occupant", "Fourth occupant" })
        {
            Assert.Contains(($"{seat} oxygen mask stowage", Set, "0=Closed, 1=Open"), rows);   // its tooltip names the click
            Assert.Contains(($"{seat} oxygen N/100%", Set, "0=Normal, 1=100%"), rows);
            Assert.Contains(($"{seat} oxygen mask test", Press, ""), rows);
            Assert.Contains(($"{seat} oxygen emergency pressure", Press, ""), rows);
        }
        Assert.Equal(16, rows.Count);
    }

    [Fact]
    public void Hud_units_are_the_hud_options()
    {
        Assert.Equal(new[]
        {
            ("Captain HUD cover", Set, "0=Off, 1=On"),
            ("First officer HUD cover", Set, "0=Off, 1=On"),
            ("Captain HUD", Set, "0=Stowed, 1=Deployed"),
            ("First officer HUD", Set, "0=Stowed, 1=Deployed"),
        }, Rows("HUD Units"));
        Assert.Equal("INI_IS_HUD", A350Options.For("AIRLINER_HUD_HANDLE_LEFT")!.Var);
    }

    [Fact]
    public void Tillers_and_sidesticks()
    {
        // Both tillers' disconnects flip one variable (INI_TILLER_DISC).
        Assert.Equal(new[]
        {
            ("Captain sidestick takeover", Press, ""),
            ("First officer sidestick takeover", Press, ""),
            ("Captain tiller", Set, "0=Connected, 1=Disconnected"),
            ("First officer tiller", Set, "0=Connected, 1=Disconnected"),
        }, Rows("Tillers and Sidesticks"));
    }

    [Fact]
    public void Misc()
    {
        // The eight printer paper clickspots all hide the paper (INI_PRINTER_SIDE_PAPER_VIS 0): one row.
        Assert.Equal(new[]
        {
            ("Dome light override", Set, "0=Off, 1=On"),
            ("Printer paper tear off", Press, ""),
            ("Pen", Set, "0=Off, 1=On"),
        }, Rows("Misc"));
    }
}
