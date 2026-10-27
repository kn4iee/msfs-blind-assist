using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The pedestal's panels in the A380's order with the A380's words (ECAM keys, levers), the cockpit's own
/// legends elsewhere, and positions named from what the switch does where iniBuilds' tooltip names the click.</summary>
public class A350PedestalPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());
    private const A350RowAction Set = A350RowAction.Set;
    private const A350RowAction Press = A350RowAction.Press;

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p =>
                $"{p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)}={p.Value}"))));

    private static void AssertRows(string panel, params (string Name, A350RowAction Action, string Positions)[] expected) =>
        Assert.Equal(expected, Rows(panel));

    [Fact]
    public void The_pedestal_panels_follow_the_a380()
    {
        Assert.Equal(new[]
        {
            "Engines", "Thrust Levers", "Emergency Descent", "Flaps and Brakes", "Speed Brake", "Trim", "ECAM Control Panel",
            "Weather Radar", "Transponder", "Cockpit",
        }, Placement.Structure["Pedestal"]);
    }

    [Fact]
    public void The_rmps_and_the_kccu_wheels_wait_for_their_own_plans()
    {
        Assert.DoesNotContain(Placement.RowsByPanel.Values.SelectMany(r => r), r => r.Control?.Panel is "RMPS" or "FMGS_1" or "FMGS_2");
    }

    [Fact]
    public void Engines()
    {
        // iniBuilds' master tooltip words are swapped (its OFF key reads ON): with both engines running the masters read 1.
        AssertRows("Engines",
            ("Engine mode selector", Set, "0=Crank, 1=Norm, 2=Ign start"),
            ("ENG 1 master", Set, "0=Off, 1=On"),
            ("ENG 2 master", Set, "0=Off, 1=On"));
    }

    [Fact]
    public void Thrust_levers()
    {
        const string detents = "-20=Reverse, -6=Reverse idle, 0=Idle, 25=Climb, 35=Flex/MCT, 45=TOGA";
        AssertRows("Thrust Levers",
            ("All thrust levers", Set, detents),
            ("Thrust lever 1", Set, detents),
            ("Left A/THR disconnect", Press, ""),
            ("Thrust lever 2", Set, detents),
            ("Right A/THR disconnect", Press, ""));
    }

    [Fact]
    public void Flaps_and_brakes()
    {
        // The cover's tooltip words are the click's action; the handle needs the cover and both guards open for DOWN.
        AssertRows("Flaps and Brakes",
            ("Flaps", Set, "0=Up, 1=1, 2=2, 3=3, 4=Full"),
            ("Parking brake", Set, "0=Off, 1=On"),
            ("Brake accumulator reinflate", Press, ""),
            ("Gravity gear extension cover", Set, "0=Closed, 1=Open"),
            ("Gravity gear extension guard 1", Set, "0=Closed, 1=Open"),
            ("Gravity gear extension guard 2", Set, "0=Closed, 1=Open"),
            ("Gravity gear extension", Set, "0=Off, 1=Reset, 2=Down"));
    }

    [Fact]
    public void Ecam_control_panel_takes_the_a380s_ecam_words()
    {
        var names = Rows("ECAM Control Panel").Select(r => r.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);   // [VAR-1]
        foreach (var name in new[]
        {
            "ECAM ENG", "ECAM APU", "ECAM BLEED", "ECAM COND", "ECAM PRESS", "ECAM DOOR", "ECAM EL/AC", "ECAM EL/DC",
            "ECAM FUEL", "ECAM WHEEL", "ECAM HYD", "ECAM F/CTL", "ECAM all", "ECAM status", "ECAM video",
            "ECAM abnormal proc", "ECAM deferred proc", "ECAM dispatch", "ECAM more", "ECAM T.O config", "ECAM emergency cancel",
            "ECAM emergency cancel guard", "Captain ECAM clear", "First officer ECAM clear", "Captain ECAM checklist",
            "First officer ECAM checklist", "ECAM recall last", "ECAM recall all",
        })
            Assert.Contains(name, names);
    }

    [Fact]
    public void Weather_radar_and_transponder_split_the_surveillance_panel_as_the_a380()
    {
        var radar = Rows("Weather Radar").Select(r => r.Name).ToList();
        Assert.Contains("Weather radar system 1", radar);
        Assert.Contains("Weather radar system 2", radar);
        Assert.Contains("Captain weather radar gain auto", radar);       // push: AUTO MODE
        Assert.Contains("Captain weather radar gain manual", radar);     // pull: MANUAL MODE
        Assert.Contains("First officer weather radar elevation increase", radar);
        Assert.Equal(radar.Distinct().Count(), radar.Count);
        var xpdr = Rows("Transponder").Select(r => r.Name).ToList();
        Assert.Equal(new[] { "G/S mode", "TCAS above", "TCAS below", "TCAS TA only", "Transponder system 1", "Transponder system 2" },
            xpdr);   // the cockpit file's order
    }

    [Fact]
    public void Cockpit_door_and_lighting()
    {
        var cockpit = Rows("Cockpit").ToList();
        Assert.Contains(("Cockpit door", Set, "0=Lock, 1=Norm, 2=Unlock"), cockpit);
        Assert.Contains(("DU master", Set, "0=Inhibit, 1=On"), cockpit);
        Assert.Contains(("Captain OIS on center", Set, "0=Off, 1=On"), cockpit);
        Assert.Contains(("First officer OIS on center", Set, "0=Off, 1=On"), cockpit);
        Assert.Contains(("Captain display cycle", Set, "0=Off, 1=On"), cockpit);
        Assert.Contains(("First officer display cycle", Set, "0=Off, 1=On"), cockpit);
    }
}

/// <summary>The pedestal's lights, named by what they show.</summary>
public class A350PedestalLightTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();
    private static readonly IReadOnlyList<A350Lamp> Lamps =
        A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Pedestal" });

    private static string Name(string node) => Lamps.Single(l => l.Key == "A350_LT_" + node).Name;

    [Fact]
    public void An_engine_masters_upper_legend_is_its_fire_light()
    {
        Assert.Equal("ENG 1 master fire light", Name("ENG_MASTER_1_SEQ1_LIGHT"));
        Assert.Equal("ENG 2 master fire light", Name("ENG_MASTER_2_SEQ1_LIGHT"));
        Assert.Equal("ENG 1 master fault light", Name("ENG_MASTER_1_SEQ2_LIGHT"));
    }
}
