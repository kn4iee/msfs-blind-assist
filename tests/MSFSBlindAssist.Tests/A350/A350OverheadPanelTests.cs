using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Each overhead subpanel's rows as live-tested on the A350-900 (package 1.2.7): names, what a row does, and the
/// positions it offers, by the aircraft's own state value. A panel's test changes only with a new live drive.
/// </summary>
public class A350OverheadPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))));

    private const string WiperPositions =
        "0=Intermittent slow, 1=Intermittent medium, 2=Intermittent fast, 3=Off, 4=Slow, 5=Fast";

    /// <summary>Live 2026-10-26: both selectors reach all six positions and the wipers sweep (WIPER_ANIMATION_CPT/_FO);
    /// each rain repellent holds 1 while pressed and 0 on release, which only the cockpit sound reads.</summary>
    [Fact]
    public void Wipers()
    {
        Assert.Equal(new[]
        {
            ("Captain wiper", A350RowAction.Set, WiperPositions),
            ("First officer wiper", A350RowAction.Set, WiperPositions),
            ("Captain rain repellent", A350RowAction.Press, ""),
            ("First officer rain repellent", A350RowAction.Press, ""),
        }, Rows("Wipers"));
    }

    private static IEnumerable<string> Lights(string panel)
    {
        var map = A350ControlMap.Load();
        return A350PanelLamps.Resolve(map, Placement, new[] { "Overhead" }).Where(l => l.Panel == panel).Select(l => l.Name);
    }

    /// <summary>Live 2026-10-26 through MSFSBA's own path (tools/A350Probe): every call button presses; EMER, EVAC
    /// COMMAND and the CAPT and PURS selector reach both positions. EVAC COMMAND lights its EVAC legend
    /// (INI_EVAC_LIGHT) within seconds and puts it out when switched off. Only FWD, PURS and the EVAC variables are
    /// read by the systems; the other calls are chimes. CAPT and PURS: 0 lets only the captain order an evacuation.</summary>
    [Fact]
    public void Calls_and_evacuation()
    {
        Assert.Equal(new[]
        {
            ("Aft", A350RowAction.Press, ""),
            ("All", A350RowAction.Press, ""),
            ("Cabin crew rest", A350RowAction.Press, ""),
            ("Exit", A350RowAction.Press, ""),
            ("Flight crew rest", A350RowAction.Press, ""),
            ("Fwd", A350RowAction.Press, ""),
            ("Mech call", A350RowAction.Press, ""),
            ("Mid", A350RowAction.Press, ""),
            ("Purser", A350RowAction.Press, ""),
            ("EMER", A350RowAction.Set, "0=Off, 1=On"),
            ("EVAC command", A350RowAction.Set, "0=Off, 1=On"),
            ("EVAC horn off", A350RowAction.Press, ""),
            ("EVAC CAPT and PURS", A350RowAction.Set, "0=Captain only, 1=Captain and purser"),
        }, Rows("Calls and Evacuation"));
        Assert.Equal(new[] { "EMER call light", "EVAC light" }, Lights("Calls and Evacuation"));
    }
}
