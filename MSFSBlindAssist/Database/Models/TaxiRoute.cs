namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a calculated taxi route with segments and hold-short points.
/// </summary>
public class TaxiRoute
{
    public List<TaxiRouteSegment> Segments { get; set; } = new();
    public double TotalDistanceMeters { get; set; }
    public List<string> TaxiwaySequence { get; set; } = new();
    public List<TaxiHoldShort> HoldShortPoints { get; set; } = new();
    public string DestinationName { get; set; } = "";
    /// <summary>If the constrained route fell back to shortest path, explains why.</summary>
    public string? ConstrainedFallbackReason { get; set; }

    /// <summary>
    /// Every runway this route enters or crosses, in route order, held or not — recorded by the
    /// automatic hold-short pass when the route is adopted. The route's own arrival at a
    /// destination runway is not one. The summary and the "Route changed" clause are built from it.
    /// </summary>
    public List<TaxiRouteRunwayEvent> RunwayEvents { get; set; } = new();

    /// <summary>
    /// The hold label ("runway 12R") when the route's first stop point is its START node — it
    /// begins at a runway hold line with nowhere earlier to stop — so guidance starts held and
    /// waits for Continue. Null otherwise, and never set on a recalculated route.
    /// </summary>
    public string? StartHoldRunway { get; set; }

    /// <summary>
    /// The start hold's label as it was placed, kept while any runway at it is cleared
    /// (<see cref="StartHoldClearedRunways"/>), so it can be put back
    /// (<see cref="MSFSBlindAssist.Navigation.ClearedCrossingHolds.RestoreStartHold"/>): the start hold's counterpart
    /// of <see cref="TaxiRouteSegment.PlacedHoldLabel"/>. Null otherwise.
    /// </summary>
    public string? PlacedStartHoldLabel { get; set; }

    /// <summary>
    /// The runways the pilot is cleared across at the start hold, normalized, in the order cleared; empty when none
    /// (<see cref="MSFSBlindAssist.Navigation.ClearedCrossingHolds.IsStartHoldCleared"/>). With every runway cleared
    /// <see cref="StartHoldRunway"/> is null, and this is what remembers that a runway lies straight ahead.
    /// </summary>
    public List<string> StartHoldClearedRunways { get; } = new();

    /// <summary>
    /// A runway route's tail that the hold-short truncation cut off: the hold node, then every
    /// node on to the runway entry. After Continue the lineup tone follows it onto the pavement
    /// before the centreline intercept takes over. Null when nothing was cut.
    /// </summary>
    public List<(double Lat, double Lon)>? RunwayEntryStub { get; set; }

    /// <summary>
    /// Other runways whose pavement that entry stub rolls onto before (or while) it reaches the
    /// destination runway — intersecting runways at a threshold (KSLC 32 / 35). Named at the
    /// destination hold ("Hold short of Runway 32 and runway 35"), because nothing after it would.
    /// </summary>
    public List<string> RunwayEntryStubAlsoOn { get; set; } = new();

    /// <summary>The Turn picks the planner applied to this route, in row order.</summary>
    public List<AppliedTurnPick> TurnPicks { get; set; } = new();
}

/// <summary>
/// A single segment in the taxi route (edge between two nodes).
/// </summary>
public class TaxiRouteSegment
{
    public TaxiNode FromNode { get; set; } = null!;
    public TaxiNode ToNode { get; set; } = null!;
    public double DistanceMeters { get; set; }
    public double CumulativeDistanceMeters { get; set; }
    public double RemainingDistanceMeters { get; set; }
    public string TaxiwayName { get; set; } = "";
    public double BearingDegrees { get; set; }
    public double TurnAngleDegrees { get; set; }
    public string TurnDirection { get; set; } = "straight";
    public double PathWidth { get; set; }
    public bool IsHoldShortPoint { get; set; }
    public string? HoldShortRunway { get; set; }
    /// <summary>
    /// The stop's label as it was placed, kept while any runway at this stop is cleared
    /// (<see cref="ClearedRunways"/>), so the stop and its label can be put back
    /// (<see cref="MSFSBlindAssist.Navigation.ClearedCrossingHolds.RestoreAt"/>). Null otherwise.
    /// </summary>
    public string? PlacedHoldLabel { get; set; }
    /// <summary>
    /// The runways the pilot is cleared across at this stop, normalized, in the order cleared; empty when none.
    /// A stop with every runway it guarded cleared is no longer a hold short
    /// (<see cref="MSFSBlindAssist.Navigation.ClearedCrossingHolds.IsFullyCleared"/>).
    /// </summary>
    public List<string> ClearedRunways { get; } = new();
    /// <summary>
    /// True for a SYNTHETIC crossing segment bridging two disconnected components of
    /// the taxi network over pavement that has no taxi paths in the scenery (the
    /// "uncharted apron" leg — see TaxiRouter.FindCrossComponentPath). Guidance
    /// announces entering/leaving it, and auto-recalc is suppressed while one is
    /// still ahead (a rebuild from mid-gap has no charted position to route from).
    /// </summary>
    public bool IsUncharted { get; set; }
    /// <summary>
    /// True for a synthetic straight leg built for a Turn pick where the network has no link that
    /// way. Unnamed; spoken as off-chart. Distinct from <see cref="IsUncharted"/>, which stays the
    /// apron crossing's.
    /// </summary>
    public bool IsOffChartTurn { get; set; }
}

/// <summary>Which way the pilot picked to turn onto a taxiway.</summary>
public enum TurnSide
{
    Left,
    Right,
}

/// <summary>A Turn pick as the planner applied it to a route, with what it took to honor it.</summary>
public sealed class AppliedTurnPick
{
    public int RowIndex { get; init; }
    public TurnSide Side { get; init; }
    public string TaxiwayName { get; init; } = "";
    /// <summary>The departure node (A) the turn is made from.</summary>
    public int DepartureNodeId { get; init; }
    /// <summary>The node (u) where the route joins the picked taxiway.</summary>
    public int JoinNodeId { get; init; }
    /// <summary>Index of the segment whose FromNode is the departure node.</summary>
    public int FirstSegmentAfterDeparture { get; init; }
    /// <summary>Index of the segment from the join node u to v, the first on the taxiway.</summary>
    public int FirstTaxiwaySegment { get; init; }
    /// <summary>False when the built route already made the turn.</summary>
    public bool ChangedRoute { get; init; }
    public bool IsOffChart { get; init; }
    public double OffChartLengthMeters { get; init; }
    public IReadOnlyList<int> BlockedNodeIds { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Represents a hold-short instruction at a specific segment.
/// </summary>
public class TaxiHoldShort
{
    public int SegmentIndex { get; set; }
    public string RunwayOrTaxiway { get; set; } = "";
    public bool IsRunway { get; set; }
}

/// <summary>
/// How a route meets a runway it does not start on: it goes onto the pavement and back off the
/// SAME side (an entry — also a route that ends on the runway), or off the OTHER side (a crossing).
/// </summary>
public enum RunwayEventKind
{
    Entry,
    Crossing,
}

/// <summary>One runway a route enters or crosses, as the pilot is told about it.</summary>
public sealed class TaxiRouteRunwayEvent
{
    public RunwayEventKind Kind { get; init; }
    /// <summary>The designator announced for it (the pilot's own on the destination strip).</summary>
    public string Designator { get; init; } = "";
    /// <summary>
    /// Whether a stop was placed for it (false: passed already, or no stop point). A crossing the pilot is cleared
    /// across keeps this, and is marked <see cref="Cleared"/>.
    /// </summary>
    public bool Held { get; set; }
    /// <summary>
    /// Not held because the aircraft is already past the stop, or already on this runway — as opposed to
    /// "there is no safe place to stop". Only the latter is announced: telling a pilot who is already
    /// crossing a runway that it has "no hold short point" invites a stop on the active runway.
    /// </summary>
    public bool Passed { get; init; }
    /// <summary>
    /// WHERE the runway is met, for the summary's "crossing runway 27 at A" — the hold label's own
    /// "at X" when the stop carries one, else the taxiway the route approaches the runway on. Null
    /// when neither is known. Spoken only for a runway met once (VATSIM gap analysis 2026-08-31, P1).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// The pilot is cleared across this runway at its stop
    /// (<see cref="MSFSBlindAssist.Navigation.ClearedCrossingHolds"/>): still <see cref="Held"/> and still
    /// announced, but the stop no longer holds the aircraft for it. Also set on a crossing with no stop the pilot
    /// was cleared for, which then is not said as having "no hold short point".
    /// </summary>
    public bool Cleared { get; set; }

    private int _stopNodeIndex = -1;

    /// <summary>
    /// The route node the stop sits at: <c>n &gt;= 1</c> is the end node of segment <c>n - 1</c>, 0 a start hold,
    /// -1 no stop placed. A split of segment <c>m</c> moves every <c>n &gt; m</c> up by one
    /// (<see cref="MSFSBlindAssist.Navigation.TaxiwayHoldPlacement.ShiftEventsAfterSplit"/>).
    /// </summary>
    public int StopNodeIndex { get => _stopNodeIndex; init => _stopNodeIndex = value; }

    private int _reachNodeIndex = -1;

    /// <summary>
    /// The first route node at or past the runway (<see cref="MSFSBlindAssist.Navigation.RunwayPassage.ReachIndex"/>):
    /// where the route reaches it, which orders the events along the route and is what binds a crossing to the
    /// taxiway it is crossed on, as a pilot's runway pick binds. The stop can lie further back, on the taxiway
    /// before. -1 when not recorded. Kept valid across a split as <see cref="StopNodeIndex"/> is.
    /// </summary>
    public int ReachNodeIndex { get => _reachNodeIndex; init => _reachNodeIndex = value; }

    /// <summary>A copy of this event with these stop and reach nodes, every other member kept.</summary>
    public TaxiRouteRunwayEvent WithNodeIndices(int stopNodeIndex, int reachNodeIndex)
    {
        var copy = (TaxiRouteRunwayEvent)MemberwiseClone();
        copy._stopNodeIndex = stopNodeIndex;
        copy._reachNodeIndex = reachNodeIndex;
        return copy;
    }
}
