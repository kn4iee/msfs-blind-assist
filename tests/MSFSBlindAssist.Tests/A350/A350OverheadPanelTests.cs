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
}
