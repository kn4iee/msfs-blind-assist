using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One page the A350's MFD menus reach: its name as the menus say it, its KCCU key where one opens it, and the clicks that
/// open it from any page (each in the side's own MFD coordinates).</summary>
internal sealed record A350MfdPageNav(string Label, string? KccuKey, IReadOnlyList<(int X, int Y)> MenuClicks);

/// <summary>
/// Every page of the MFD's menus, walked on 1.2.8 at VCBI (plan 09 Task 1, 2026-11-03): the system selector (116, 26) opens FMS 1,
/// ATC COM, SURV, C/L MENU and FCU BKUP down x 105 from y 66, 32 px apart; each system's header keys stand on the row at y 77; a key
/// with a list opens it under itself from y 125, 42 px apart. The KCCU keys as pressed there: INIT, FPLAN, PERF, FUEL open ACTIVE's
/// pages, RADNAV POSITION/NAVAIDS, SEC the SEC INDEX, DIR the DIRECT TO page, ATC REQUEST, SURV CONTROLS, CL_MENU the checklists, REF
/// DATA/STATUS. Nothing was grey on the stand.
/// </summary>
internal static class A350MfdPages
{
    private static readonly (int X, int Y) Selector = (116, 26);
    private static (int, int) SystemAt(int i) => (105, 66 + 32 * i);
    private static (int, int) ItemAt(int x, int i) => (x, 125 + 42 * i);

    private static IEnumerable<A350MfdPageNav> Listed(int system, string key, int keyX, string?[] keys, params string[] items) =>
        items.Select((item, i) => new A350MfdPageNav($"{key}: {item}", keys[i], new[] { Selector, SystemAt(system), (keyX, 77), ItemAt(keyX, i) }));

    private static A350MfdPageNav Keyed(string label, int system, int keyX, string? kccu = null) =>
        new(label, kccu, new[] { Selector, SystemAt(system), (keyX, 77) });

    /// <summary>Menu order.</summary>
    public static readonly IReadOnlyList<A350MfdPageNav> All = new List<A350MfdPageNav>()
        .Concat(Listed(0, "ACTIVE", 115, new[] { "FPLAN", "PERF", "FUEL", null, "INIT" }, "F-PLN", "PERF", "FUEL&LOAD", "WIND", "INIT"))
        .Append(new A350MfdPageNav("ACTIVE: F-PLN: DIRECT TO", "DIR", Array.Empty<(int, int)>()))
        .Concat(Listed(0, "POSITION", 308, new[] { null, null, "RADNAV", null, null, null }, "MONITOR", "REPORT", "NAVAIDS", "IRS", "GNSS", "TIME"))
        .Concat(Listed(0, "SEC INDEX", 501, new[] { "SEC", null, null }, "SEC 1", "SEC 2", "SEC 3"))
        .Concat(Listed(0, "DATA", 694, new[] { "REF", null, null, null, null, null }, "STATUS", "WAYPOINT", "NAVAID", "ROUTE", "AIRPORT", "PRINTER"))
        .Append(Keyed("ATC COM: CONNECT", 1, 87))
        .Append(Keyed("ATC COM: REQUEST", 1, 216, "ATC"))
        .Append(Keyed("ATC COM: REPORT & MODIFY", 1, 363))
        .Append(Keyed("ATC COM: MSG RECORD", 1, 508))
        .Append(Keyed("ATC COM: D-ATIS", 1, 627))
        .Append(Keyed("ATC COM: EMER", 1, 735))
        .Append(Keyed("SURV: CONTROLS", 2, 93, "SURV"))
        .Append(Keyed("SURV: STATUS & SWITCHING", 2, 316))
        .Append(Keyed("SURV: TRAFFIC", 2, 536))
        .Append(new A350MfdPageNav("C/L MENU", "CL_MENU", new[] { Selector, SystemAt(3) }))
        .Append(Keyed("FCU BKUP: EFIS CP", 4, 96))
        .Append(Keyed("FCU BKUP: AFS CP", 4, 301))
        .ToList();

    /// <summary>Whether a menu item stands at a click point and is offered: an item drawn grey is not (a page greyed in a phase).</summary>
    public static bool Offered(IReadOnlyList<MfdLine> lines, int x, int y) =>
        lines.FirstOrDefault(l => l.Kind is "menuitem" or "button" && Math.Abs(l.ClickX - x) <= 3 && Math.Abs(l.ClickY - y) <= 3) is { } item
        && !item.Spoken.EndsWith(", grey", StringComparison.Ordinal);
}

/// <summary>A choice taken once it has settled: a closed drop-down list fires a change on every arrow, and arrowing over pages must
/// navigate once, to where the arrowing stopped (the A380 window's page list, 350 ms).</summary>
internal sealed class A350SettledChoice
{
    private readonly TimeSpan _settle;
    private int? _pending;
    private DateTime _at;

    public A350SettledChoice(TimeSpan settle) => _settle = settle;

    public void Changed(int index, DateTime now) => (_pending, _at) = (index, now);

    /// <summary>The choice to act on now, once, or null while it is still moving or already taken.</summary>
    public int? Due(DateTime now)
    {
        if (_pending is not int index || now - _at < _settle) return null;
        _pending = null;
        return index;
    }
}
