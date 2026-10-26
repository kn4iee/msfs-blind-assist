using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Each glareshield panel's rows as live-tested on the A350-900 (package 1.2.7).</summary>
public class A350GlareshieldPanelTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();
    private static readonly A350Placement Placement = A350PanelLayout.Place(Map);

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))));

    private static IEnumerable<string> Lights(string panel) =>
        A350PanelLamps.Resolve(Map, Placement, new[] { "Glareshield" }).Where(l => l.Panel == panel).Select(l => l.Name);

    private const string OffOn = "0=Off, 1=On";
    private const A350RowAction Press = A350RowAction.Press;
    private const A350RowAction Set = A350RowAction.Set;

    /// <summary>Live 2026-10-26 through MSFSBA's path: every button presses (SPD/MACH and HDG-V/S / TRK-FPA swap their
    /// windows' units and flip back on a second press); each knob turns both ways (speed 1 kt or 0.01 Mach, heading 1°,
    /// altitude 1000 ft, V/S 100 ft/min, FPA 0.1°), pushes and pulls; AUTOLAND is a light test the cockpit returns.</summary>
    [Fact]
    public void Fcu()
    {
        Assert.Equal(new[]
        {
            ("AP 1", Press, ""), ("AP 2", Press, ""), ("A/THR", Press, ""), ("FD", Set, OffOn), ("LOC", Press, ""),
            ("APPR", Press, ""), ("ALT", Press, ""), ("SPD/MACH", Press, ""), ("HDG-V/S / TRK-FPA", Press, ""),
            ("Metric altitude", Set, "0=Off, 1=On"), ("TRUE/MAG", Set, "0=Magnetic, 1=True"),
            ("Speed knob push", Press, ""), ("Speed knob pull", Press, ""),
            ("Speed knob increase", A350RowAction.Increase, ""), ("Speed knob decrease", A350RowAction.Decrease, ""),
            ("Heading knob push", Press, ""), ("Heading knob pull", Press, ""),
            ("Heading knob increase", A350RowAction.Increase, ""), ("Heading knob decrease", A350RowAction.Decrease, ""),
            ("Altitude knob push", Press, ""), ("Altitude knob pull", Press, ""),
            ("Altitude knob increase", A350RowAction.Increase, ""), ("Altitude knob decrease", A350RowAction.Decrease, ""),
            ("V/S knob push", Press, ""), ("V/S knob pull", Press, ""),
            ("V/S knob increase", A350RowAction.Increase, ""), ("V/S knob decrease", A350RowAction.Decrease, ""),
            ("Altitude 100/1000 selector", Press, ""),
            ("Captain chrono", Press, ""), ("First officer chrono", Press, ""),
            ("Captain loudspeaker", Set, ""), ("First officer loudspeaker", Set, ""),
            ("Captain HUD brightness", Set, ""), ("First officer HUD brightness", Set, ""),
            ("Captain HUD crosswind mode", Set, OffOn), ("First officer HUD crosswind mode", Set, OffOn),
            ("Captain HUD declutter", Press, ""), ("First officer HUD declutter", Press, ""),
            ("Captain master caution", Press, ""), ("First officer master caution", Press, ""),
            ("Captain master warning", Press, ""), ("First officer master warning", Press, ""),
            ("Captain autoland", Press, ""), ("First officer autoland", Press, ""),
            ("Captain ATC message", Press, ""), ("First officer ATC message", Press, ""),
            ("Captain sliding table light", Set, ""), ("First officer sliding table light", Set, ""),
            ("FCU display brightness", Set, ""), ("FCU integral lighting", Set, ""),
        }, Rows("FCU"));
        Assert.Equal(new[]
        {
            "AP 1 light", "AP 2 light", "A/THR light", "LOC light", "APPR light", "ALT light",
            "Captain master caution light", "Captain master caution lower light",
            "First officer master caution light", "First officer master caution lower light",
            "Captain master warning light", "Captain master warning lower light",
            "First officer master warning light", "First officer master warning lower light",
            "Captain autoland light", "First officer autoland light",
            "Captain ATC message light", "First officer ATC message light",
        }, Lights("FCU"));
    }
}
