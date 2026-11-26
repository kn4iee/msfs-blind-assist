// What taxi guidance reports back to the Taxi Guidance window about the clearance it is flying, so the window can
// trim its step list to what is still ahead (ClearanceTrim.Remaining): how many Via steps are behind, which crossings
// the pilot entered are still cleared, used or put back, which of the pilot's own stops are passed, and whether the turn
// onto the first remaining taxiway is made. Numbered as the window last calculated (ClearanceNumbering). Pure: the
// manager keeps one ClearanceProgressState and asks it for a report under its lock.

using MSFSBlindAssist.Database.Models;   // TaxiRouteSegment

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// <c>TaxiGuidanceManager.GetClearanceProgress</c>'s answer: exactly what <see cref="ClearanceTrim.Remaining"/> takes,
/// numbered as the step list was last calculated. Only crossings the pilot entered (<see cref="ClearedBy.Clearance"/>)
/// are listed: a Continue's crossing stands for no step.
/// </summary>
public sealed record ClearanceProgress(
    int ViaStepsDone,
    IReadOnlyList<ClearedCrossing> ClearedInForce,
    IReadOnlyList<ClearedCrossing> ClearedUsed,
    IReadOnlyList<ClearedCrossing> PutBack,
    IReadOnlyList<ClearanceHold> HoldsPassed,
    bool TurnOntoFirstRemainingMade)
{
    /// <summary>No route: nothing is known done, so nothing is trimmed.</summary>
    public static readonly ClearanceProgress None = new(0, Array.Empty<ClearedCrossing>(), Array.Empty<ClearedCrossing>(),
        Array.Empty<ClearedCrossing>(), Array.Empty<ClearanceHold>(), false);
}

/// <summary>
/// One of the pilot's own stops (a Hold short step on the way) as guidance placed it on a route: the row it was keyed
/// to as the clearance stood when it was placed (-1: no taxiway of its own), what it holds short of (a runway's or
/// taxiway's name as entered, or a holding point with its node, <paramref name="PointNodeId"/>), and the node the stop
/// is on (<paramref name="StopNodeId"/>), by which it is judged passed.
/// </summary>
public sealed record PlacedStop(int Row, ClearanceTargetKind Kind, string Name, int PointNodeId, int StopNodeId)
{
    /// <summary>The Hold short step it came from, as <see cref="ClearanceTrim.Remaining"/> matches a passed stop: its row
    /// as entered, a stop kept with no row when its taxiway was left out included (<see cref="ClearanceNumbering.AsEntered(PlacedStop)"/>).</summary>
    public ClearanceHold AsHold(ClearanceNumbering numbering)
        => new(numbering.AsEntered(this), new ClearanceStep(ClearanceStepKind.HoldShort, Kind, Name, NodeId: PointNodeId));

    /// <summary>
    /// The pilot's stops as <c>LoadRoute</c> takes them ((row, target) pairs: runways, taxiways, holding points), as stops
    /// not placed yet (node 0), named as placing them records them, for <see cref="ClearanceNumbering.Removing(IReadOnlyCollection{int}, IEnumerable{PlacedStop})"/>.
    /// </summary>
    public static List<PlacedStop> Entered(IReadOnlyList<KeyValuePair<int, string>>? runway,
        IReadOnlyList<KeyValuePair<int, string>>? taxiway, IReadOnlyList<KeyValuePair<int, HoldingPointHoldShort>>? points)
        => (runway ?? Array.Empty<KeyValuePair<int, string>>())
            .Select(s => new PlacedStop(s.Key, ClearanceTargetKind.Runway, s.Value, 0, 0))
            .Concat((taxiway ?? Array.Empty<KeyValuePair<int, string>>())
                .Select(s => new PlacedStop(s.Key, ClearanceTargetKind.Taxiway, s.Value, 0, 0)))
            .Concat((points ?? Array.Empty<KeyValuePair<int, HoldingPointHoldShort>>())
                .Select(s => new PlacedStop(s.Key, ClearanceTargetKind.HoldingPoint, s.Value.Name, s.Value.NodeId, 0)))
            .ToList();
}

/// <summary>
/// What guidance keeps between reports, for one loaded clearance: its numbering as entered; the crossings the pilot
/// entered that were used at their line, a crossing with no stop of its own included (<see cref="Used"/>); those the
/// Hold short key put back (<see cref="PutBack"/>); the pilot's own stops placed on the route being flown
/// (<see cref="Placed"/>) and those already passed on routes a recalculation replaced (<see cref="Passed"/>). Started
/// afresh by every load, emptied when guidance stops. Replaced, never mutated: LoadRoute's refusal snapshot and its
/// rollback hold the reference [RTE-29].
/// </summary>
public sealed record ClearanceProgressState(
    ClearanceNumbering Numbering,
    IReadOnlyList<ClearedCrossing> Used,
    IReadOnlyList<ClearedCrossing> PutBack,
    IReadOnlyList<PlacedStop> Placed,
    IReadOnlyList<PlacedStop> Passed)
{
    /// <summary>No clearance loaded.</summary>
    public static readonly ClearanceProgressState Empty = new(ClearanceNumbering.None, Array.Empty<ClearedCrossing>(),
        Array.Empty<ClearedCrossing>(), Array.Empty<PlacedStop>(), Array.Empty<PlacedStop>());

    /// <summary>
    /// The rows (as loaded) whose Turn pick was made on a route a recalculation replaced: the recalculation restarts the
    /// segment cursor, so the turn onto a taxiway the aircraft is already on is remembered here, never read as a turn
    /// still to make [TURN-3]. Ascending, each once.
    /// </summary>
    public IReadOnlyList<int> TurnsMade { get; init; } = Array.Empty<int>();

    /// <summary>
    /// The row (as loaded) the route being flown was built from: 0 for a route loaded from the whole sequence, the
    /// first row still ahead for a recalculated one, -1 for a route built from no sequence (nothing on it to count
    /// Via steps by: <see cref="ClearanceTrim.ViaStepsDoneOnRoute"/>).
    /// </summary>
    public int RouteFirstRow { get; init; } = -1;

    /// <summary>A clearance just loaded, with the crossings as entered; nothing used, put back or placed yet.</summary>
    public static ClearanceProgressState For(IReadOnlyList<ClearedCrossing>? enteredCrossings)
        => Empty with { Numbering = ClearanceNumbering.For(enteredCrossings) };

    /// <summary>Route loading removed these rows, numbered as the clearance stands now (<see cref="ClearanceNumbering.Removing"/>).</summary>
    public ClearanceProgressState WithRowsLeftOut(IReadOnlyCollection<int> rowsNow)
        => this with { Numbering = Numbering.Removing(rowsNow) };

    /// <summary>Route loading left out these first taxiways as never driven ([RTE-39]) and kept the stops on them with no
    /// row: <paramref name="stopsNow"/> are the stops before that, numbered as the clearance stands now
    /// (<see cref="ClearanceNumbering.Removing(IReadOnlyCollection{int}, IEnumerable{PlacedStop})"/>).</summary>
    public ClearanceProgressState WithRowsLeftOut(IReadOnlyCollection<int> rowsNow, IEnumerable<PlacedStop> stopsNow)
        => this with { Numbering = Numbering.Removing(rowsNow, stopsNow) };

    /// <summary>These crossings were used: at their line, or, with no stop of their own, where the route reaches the runway.</summary>
    public ClearanceProgressState WithUsed(IEnumerable<ClearedCrossing> used)
        => this with { Used = Used.Concat(used.Where(c => c is not null)).ToList() };

    /// <summary>
    /// The Hold short key put the stop on node <paramref name="stopNodeId"/> back, with these crossings
    /// (<see cref="ClearedCrossingsChangedEventArgs.Restored"/>): put back, and no longer used even when its line was said
    /// as passed (the segment jump says a line early).
    /// </summary>
    public ClearanceProgressState WithPutBack(IReadOnlyList<ClearedCrossing> restored, int stopNodeId)
        => this with
        {
            PutBack = PutBack.Concat(restored.Where(c => c is not null)).ToList(),
            Used = Used.Where(c => stopNodeId <= 0 || c.StopNodeId != stopNodeId).ToList(),
        };

    /// <summary>The pilot's own stops placed on the route now being flown.</summary>
    public ClearanceProgressState WithPlaced(IReadOnlyList<PlacedStop> placed)
        => this with { Placed = placed.ToList() };

    /// <summary>
    /// Before a recalculated route replaces the one being flown (<paramref name="segments"/>, cursor
    /// <paramref name="cursor"/>): a stop placed on it that is passed stays passed, a crossing put back whose stop is
    /// passed has been made (used), the turns made on it (<paramref name="turnsMade"/>, rows as loaded,
    /// <c>TurnPickCarryOver.MadeRows</c>) stay made, and the stops placed on it go: the new route records its own.
    /// </summary>
    public ClearanceProgressState Settled(IReadOnlyList<TaxiRouteSegment>? segments, int cursor, bool holding,
        IEnumerable<int>? turnsMade = null)
        => this with
        {
            Passed = Passed.Concat(Placed.Where(p => StopPassed(segments, p.StopNodeId, cursor, holding))).ToList(),
            Used = Used.Concat(PutBack.Where(c => StopPassed(segments, c.StopNodeId, cursor, holding))).ToList(),
            PutBack = PutBack.Where(c => !StopPassed(segments, c.StopNodeId, cursor, holding)).ToList(),
            Placed = Array.Empty<PlacedStop>(),
            TurnsMade = TurnsMade.Concat(turnsMade ?? Array.Empty<int>()).Distinct().OrderBy(r => r).ToList(),
        };

    /// <summary>
    /// The stops of one kind (<paramref name="kind"/>, runway or taxiway: (row, name) pairs) a recalculation may carry
    /// over to its new route. A stop on a row of its own (0 to <paramref name="sequenceRows"/> - 1) is left to
    /// <see cref="ClearanceRows.After"/>. A stop with no taxiway of its own is dropped only once it is KNOWN passed: a
    /// stop of the same kind and name, no row, placed on the route being replaced (<see cref="Placed"/>) that the
    /// aircraft has passed there, or one passed on an earlier route (<see cref="Passed"/>). One passed is never searched
    /// for again, or the new route stops there again where it meets it, or every later "Route changed" reports it
    /// [HLD-17]. One still ahead, held at, or never placed is kept: a later route may meet it, and the pilot holds an
    /// ATC "hold short of X".
    /// </summary>
    public List<KeyValuePair<int, string>> CarriedOver(IReadOnlyList<KeyValuePair<int, string>> stops, int sequenceRows,
        ClearanceTargetKind kind, IReadOnlyList<TaxiRouteSegment>? segments, int cursor, bool holding)
    {
        bool Same(PlacedStop p, string name) => p.Row < 0 && p.Kind == kind && p.Name == name;
        return stops.Where(s => (s.Key >= 0 && s.Key < sequenceRows)
                                || !(Placed.Any(p => Same(p, s.Value) && StopPassed(segments, p.StopNodeId, cursor, holding))
                                     || Passed.Any(p => Same(p, s.Value))))
            .ToList();
    }

    /// <summary>
    /// The report for the route being flown (<paramref name="segments"/>, the cursor on segment <paramref name="cursor"/>,
    /// <paramref name="holding"/> while the aircraft holds short at the end of segment <c>cursor - 1</c>), numbered as
    /// entered.
    /// <list type="bullet">
    /// <item><paramref name="viaStepsDoneOnRoute"/> is <see cref="ClearanceTrim.ViaStepsDone"/> over the sequence as the
    /// route has it (rows removed); numbered as entered, every removed row before the aircraft's is done too. 0 stays 0:
    /// nothing known done trims nothing, a first taxiway left out as never driven included.</item>
    /// <item><paramref name="turnOntoItMade"/> is about the first remaining row as the route has it (or that row is in
    /// <see cref="TurnsMade"/>, made before a recalculation), and stands only while that is the first remaining Via step
    /// as entered.</item>
    /// <item><paramref name="clearedNow"/> are the crossings cleared on the route now (the manager's
    /// <c>_clearedCrossings</c>, any origin). A crossing put back is reported put back until a crossing at its stop is
    /// cleared again (then in force) or the aircraft has passed its stop (then used).</item>
    /// <item>A stop of the pilot's is passed once the aircraft has rolled past it (<see cref="StopPassed"/>).</item>
    /// </list>
    /// </summary>
    public ClearanceProgress Report(int viaStepsDoneOnRoute, bool turnOntoItMade, IReadOnlyList<ClearedCrossing> clearedNow,
        IReadOnlyList<TaxiRouteSegment>? segments, int cursor, bool holding)
    {
        int done = viaStepsDoneOnRoute > 0 ? Numbering.AsEntered(viaStepsDoneOnRoute) : 0;
        bool turnMade = (turnOntoItMade || TurnsMade.Contains(Math.Max(0, viaStepsDoneOnRoute)))
                        && Numbering.AsEntered(Math.Max(0, viaStepsDoneOnRoute)) == done;

        clearedNow ??= Array.Empty<ClearedCrossing>();
        var inForce = clearedNow.Where(c => c is not null).ToList();
        var used = Used.ToList();
        var putBack = new List<ClearedCrossing>();
        foreach (var c in PutBack)
        {
            if (StopPassed(segments, c.StopNodeId, cursor, holding)) used.Add(c);
            else if (clearedNow.Any(r => r is not null && r.StopNodeId > 0 && r.StopNodeId == c.StopNodeId)) inForce.Add(c);
            else putBack.Add(c);
        }

        var holds = Passed
            .Concat(Placed.Where(p => StopPassed(segments, p.StopNodeId, cursor, holding)))
            .Distinct()
            .Select(p => p.AsHold(Numbering))
            .ToList();

        return new ClearanceProgress(done, Entered(inForce), Entered(used), Entered(putBack), holds, turnMade);
    }

    /// <summary>
    /// Whether the aircraft has rolled past the stop on node <paramref name="stopNodeId"/> of the route
    /// <paramref name="segments"/>, the cursor on segment <paramref name="cursor"/>. A stop at the end of segment s is
    /// passed once the cursor is beyond s + 1, or on s + 1 while the aircraft is not holding there; a start hold (the
    /// route's first node) once the cursor has left segment 0. A node the route does not pass, or node 0, never is: its
    /// state is unknown.
    /// </summary>
    public static bool StopPassed(IReadOnlyList<TaxiRouteSegment>? segments, int stopNodeId, int cursor, bool holding)
    {
        if (segments is not { Count: > 0 } || stopNodeId <= 0) return false;
        if (segments[0].FromNode?.NodeId == stopNodeId) return cursor > 0;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].ToNode?.NodeId != stopNodeId) continue;
            int node = i + 1;
            return cursor > node || (cursor == node && !holding);
        }
        return false;
    }

    /// <summary>
    /// The segment cursor where the aircraft stands: while it holds short (<paramref name="holding"/>), HandleHoldShort
    /// has already moved the cursor onto the segment after the stop, so it stands one segment back (as
    /// <see cref="StopPassed"/> reads a stop held at as not passed). Never below 0.
    /// </summary>
    public static int CursorAsStanding(int cursor, bool holding) => holding ? Math.Max(0, cursor - 1) : cursor;

    /// <summary>
    /// Whether the aircraft is on <paramref name="taxiway"/> (any case) past the route's first segment, judged on the
    /// segment where it stands (<see cref="CursorAsStanding"/>): the turn onto that taxiway is then behind. A turn at the
    /// route's first node is excluded, as <c>TurnPickCarryOver.BlocksToKeep</c> excludes it, and so is a turn just beyond
    /// a stop the aircraft is holding short of.
    /// </summary>
    public static bool IsOnTaxiwayPastStart(IReadOnlyList<TaxiRouteSegment>? segments, int cursor, bool holding, string? taxiway)
    {
        int at = holding ? cursor - 1 : cursor;
        return segments != null && !string.IsNullOrEmpty(taxiway) && at > 0 && at < segments.Count
               && string.Equals(segments[at]?.TaxiwayName, taxiway, StringComparison.OrdinalIgnoreCase);
    }

    // The crossings the pilot entered, numbered as entered.
    private List<ClearedCrossing> Entered(IEnumerable<ClearedCrossing> crossings)
        => crossings.Where(c => c.Origin == ClearedBy.Clearance).Select(Numbering.AsEntered).ToList();
}
