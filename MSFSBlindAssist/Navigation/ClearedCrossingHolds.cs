using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A runway crossing the pilot was cleared for, as entered: the runway, and where (<paramref name="At"/>, a
/// taxiway or holding-point name, or <paramref name="AtNodeId"/>, a node). <paramref name="StopNodeId"/> is the
/// node of the stop it cleared, recorded when it is applied (0: not yet, or a crossing with no stop), which a
/// recalculation matches on. <paramref name="SeqIndex"/> is the taxiway step the crossing was entered after, -1 for none.
/// </summary>
public sealed record ClearedCrossing(string Runway, string? At = null, int AtNodeId = 0, int StopNodeId = 0, int SeqIndex = -1);

/// <summary>
/// Clears ONE runway crossing at ONE place, and puts it back. The automatic pass still places a stop before every
/// runway on every route [HLD-4]; this runs after it and only takes a stop the pilot was cleared across off the
/// route: the stop keeps the label it was placed with (<see cref="TaxiRouteSegment.PlacedHoldLabel"/>; a start hold,
/// <see cref="TaxiRoute.PlacedStartHoldLabel"/>), the runway goes on <see cref="TaxiRouteSegment.ClearedRunways"/>
/// (<see cref="TaxiRoute.StartHoldClearedRunways"/>), and its event stays <see cref="TaxiRouteRunwayEvent.Held"/>,
/// marked <see cref="TaxiRouteRunwayEvent.Cleared"/>, so it is still counted down and announced.
///
/// <para>Never by runway name alone, never carried to another crossing of the same runway: a request clears the
/// first crossing of its runway, in route order, that matches its place, and a recalculation clears only the stop at the node it
/// was applied at (<see cref="ClearedCrossing.StopNodeId"/>). A stop guarding two runways loses only the named one
/// ([HLD-11]: the other still takes its own Continue). A stop that is the pilot's own taxiway, holding-point or
/// "end of taxiway" hold is never cleared. Pure.</para>
/// </summary>
public static class ClearedCrossingHolds
{
    public sealed record Outcome(IReadOnlyList<ClearedCrossing> Applied,
                                 IReadOnlyList<ClearedCrossing> NotOnRoute,
                                 IReadOnlyList<ClearedCrossing> Elsewhere);

    // How a pilot's own stop is labelled ("taxiway W", "holding point B3", "end of taxiway D"); the runway pass
    // shares one as "runway 09 at taxiway W" (RouteRunwayCrossings.ComposeCrossingLabel).
    private static readonly string[] PilotStopLabels = { "taxiway ", "holding point ", "end of taxiway " };

    private const string PartSeparator = " and runway ";

    /// <summary>
    /// Clears each request's crossing on <paramref name="route"/>, after the automatic pass.
    ///
    /// <para>A request's candidates are the crossings of its runway (either end) still held by a stop that names
    /// it: held, not passed, not cleared, with a stop node. On the pilot's Calculate the match is taken in ROUTE
    /// order (where the route reaches the runway, <see cref="TaxiRouteRunwayEvent.ReachNodeIndex"/>) from the
    /// candidates and the crossings with no stop (#355's decision 3: neither held nor passed), so a bare request
    /// clears the crossing in front of the aircraft, never a later one: the first of them, or with a place the first
    /// whose event location or stop segment's taxiway is <c>At</c> (a crossing with no stop: its location), or whose
    /// stop node is <c>AtNodeId</c>. With <paramref name="runStartSegmentOf"/> and a <c>SeqIndex</c>, those the
    /// route reaches at or after that taxiway step's first segment are tried first: the reach, not the stop, which
    /// can be set back onto the taxiway before (the binding a pilot's runway pick uses). A crossing with no stop is
    /// marked cleared, so it is not said as having "no hold short point". No crossing of the runway at all:
    /// <see cref="Outcome.NotOnRoute"/>; candidates but no match: <see cref="Outcome.Elsewhere"/>; a runway only
    /// crossed already, held only by the pilot's own taxiway stop, or crossed with no stop at another place, neither.</para>
    ///
    /// <para>On a <paramref name="recalculation"/> the match is the candidate whose stop node is the request's
    /// <c>StopNodeId</c>; anything else is dropped and in neither list (<see cref="DroppedOnRecalculation"/>).</para>
    /// </summary>
    public static Outcome Apply(TaxiRoute route, IReadOnlyList<ClearedCrossing> requests, bool recalculation,
                                Func<int, int>? runStartSegmentOf = null)
    {
        var applied = new List<ClearedCrossing>();
        var notOnRoute = new List<ClearedCrossing>();
        var elsewhere = new List<ClearedCrossing>();
        if (route is null || requests is null) return new Outcome(applied, notOnRoute, elsewhere);
        var events = route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>();

        foreach (var request in requests)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Runway)) continue;
            var crossings = events
                .Where(e => e is { Kind: RunwayEventKind.Crossing } && SameRunway(e.Designator, request.Runway))
                .ToList();
            var candidates = crossings.Where(e => IsCandidate(route, e)).ToList();

            if (recalculation)
            {
                var kept = request.StopNodeId == 0
                    ? null
                    : candidates.FirstOrDefault(e => StopNode(route, e.StopNodeIndex)?.NodeId == request.StopNodeId);
                if (kept != null) applied.Add(ClearMatch(route, kept, request));
                continue;
            }

            if (crossings.Count == 0)
            {
                notOnRoute.Add(request);
                continue;
            }

            // Route order: the pass records its events in it, but a pilot pick's event is merged in at the end.
            var open = crossings
                .Where(e => IsCandidate(route, e) || HasNoStop(e))
                .OrderBy(e => e.ReachNodeIndex < 0 ? int.MaxValue : e.ReachNodeIndex)
                .ToList();
            TaxiRouteRunwayEvent? match = null;
            if (runStartSegmentOf != null && request.SeqIndex >= 0)
            {
                int runStart = runStartSegmentOf(request.SeqIndex);
                if (runStart >= 0)
                    match = FirstMatch(route, open.Where(e => e.ReachNodeIndex >= runStart), request);
            }
            match ??= FirstMatch(route, open, request);

            if (match is null)
            {
                if (candidates.Count > 0) elsewhere.Add(request);
            }
            else if (HasNoStop(match))
            {
                match.Cleared = true;
                applied.Add(request with { StopNodeId = 0 });
            }
            else
            {
                applied.Add(ClearMatch(route, match, request));
            }
        }
        return new Outcome(applied, notOnRoute, elsewhere);
    }

    /// <summary>
    /// Clears <paramref name="runway"/> at the stop on route node <paramref name="stopNodeIndex"/> (the end node of
    /// segment <c>stopNodeIndex - 1</c>; 0 the start hold). The stop keeps the label it was placed with and loses this
    /// runway's part of it; with no runway left it is no longer a hold short. The events at that stop for this runway
    /// (either end) are marked cleared. False, and nothing changed, for a node off the route, a segment that is not a
    /// hold short, a stop whose label does not name the runway, or the pilot's own taxiway stop.
    /// </summary>
    public static bool ClearAt(TaxiRoute route, int stopNodeIndex, string runway)
    {
        if (route is null || string.IsNullOrWhiteSpace(runway) || stopNodeIndex < 0) return false;

        if (stopNodeIndex == 0)
        {
            // Remembered on the route as a segment stop is on its segment: a cleared start hold leaves no label,
            // and the runway straight ahead must still be counted down, said and put back (RestoreStartHold).
            if (!CanClear(route.StartHoldRunway, runway)) return false;
            route.PlacedStartHoldLabel ??= route.StartHoldRunway;
            AddCleared(route.StartHoldClearedRunways, runway);
            route.StartHoldRunway = RemainingLabel(route.PlacedStartHoldLabel, route.StartHoldClearedRunways);
            MarkCleared(route, 0, runway, cleared: true);
            return true;
        }

        if (stopNodeIndex - 1 >= route.Segments.Count) return false;
        var segment = route.Segments[stopNodeIndex - 1];
        if (!segment.IsHoldShortPoint || !CanClear(segment.HoldShortRunway, runway)) return false;

        segment.PlacedHoldLabel ??= segment.HoldShortRunway;
        AddCleared(segment.ClearedRunways, runway);
        string? remaining = RemainingLabel(segment.PlacedHoldLabel, segment.ClearedRunways);
        if (remaining is null)
        {
            segment.IsHoldShortPoint = false;
            segment.HoldShortRunway = "";
        }
        else
        {
            segment.HoldShortRunway = remaining;
        }
        MarkCleared(route, stopNodeIndex, runway, cleared: true);
        return true;
    }

    /// <summary>
    /// Puts back every runway cleared at segment <paramref name="segmentIndex"/>'s stop: its placed label, a hold
    /// short again, and its events no longer cleared. False when nothing was cleared there.
    /// </summary>
    public static bool RestoreAt(TaxiRoute route, int segmentIndex)
    {
        if (route is null || segmentIndex < 0 || segmentIndex >= route.Segments.Count) return false;
        var segment = route.Segments[segmentIndex];
        if (segment.ClearedRunways.Count == 0) return false;

        segment.HoldShortRunway = segment.PlacedHoldLabel;
        segment.IsHoldShortPoint = true;
        segment.ClearedRunways.Clear();
        segment.PlacedHoldLabel = null;
        foreach (var e in route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>())
            if (e != null && e.StopNodeIndex == segmentIndex + 1) e.Cleared = false;
        return true;
    }

    /// <summary>
    /// <paramref name="placedLabel"/> with each cleared runway's part removed: the label is split at
    /// <c>" and runway "</c>, and a part goes when the first designator it names is cleared (either end). Null when
    /// no runway is left; the label unchanged when it names no cleared runway.
    /// </summary>
    public static string? RemainingLabel(string? placedLabel, IReadOnlyCollection<string> clearedRunways)
    {
        if (string.IsNullOrEmpty(placedLabel)) return null;
        if (clearedRunways is null || clearedRunways.Count == 0) return placedLabel;

        var parts = LabelParts(placedLabel);
        var kept = parts
            .Where(p => !(RouteRunwayCrossings.ExtractRunwayDesignator(p) is string first
                          && clearedRunways.Any(c => SameRunway(c, first))))
            .ToList();
        if (kept.Count == parts.Count) return placedLabel;
        return kept.Any(p => RouteRunwayCrossings.ExtractRunwayDesignator(p) != null) ? string.Join(" and ", kept) : null;
    }

    /// <summary>
    /// The other half of <see cref="RemainingLabel"/>: <paramref name="placedLabel"/>'s parts (split the same way, at
    /// <c>" and runway "</c>) whose first designator is cleared (either end), joined with <c>" and "</c>; the whole label
    /// when every runway is cleared. Null when no part is. What a stop's cleared runways are called where the aircraft
    /// crosses them: at a stop still holding for another runway, the Continue that releases it names this part.
    /// </summary>
    public static string? ClearedPartLabel(string? placedLabel, IReadOnlyCollection<string> clearedRunways)
    {
        if (string.IsNullOrEmpty(placedLabel) || clearedRunways is null || clearedRunways.Count == 0) return null;
        var cleared = LabelParts(placedLabel)
            .Where(p => RouteRunwayCrossings.ExtractRunwayDesignator(p) is string first
                        && clearedRunways.Any(c => SameRunway(c, first)))
            .ToList();
        return cleared.Count > 0 ? string.Join(" and ", cleared) : null;
    }

    /// <summary>
    /// Whether a runway was cleared at the route's start hold (<see cref="TaxiRoute.StartHoldClearedRunways"/>): the
    /// start hold's counterpart of a cleared segment stop, which <see cref="NextClearedSegment"/> does not report.
    /// </summary>
    public static bool IsStartHoldCleared(TaxiRoute route)
        => route is not null && route.StartHoldClearedRunways.Count > 0;

    /// <summary>
    /// Puts back every runway cleared at the start hold: its placed label, and its events (stop node 0) no longer
    /// cleared. False when nothing was cleared there.
    /// </summary>
    public static bool RestoreStartHold(TaxiRoute route)
    {
        if (!IsStartHoldCleared(route)) return false;
        route.StartHoldRunway = route.PlacedStartHoldLabel;
        route.StartHoldClearedRunways.Clear();
        route.PlacedStartHoldLabel = null;
        MarkCleared(route, 0, runway: null, cleared: false);
        return true;
    }

    /// <summary>A stop every runway of which is cleared: no longer a hold short, but still counted down and announced.</summary>
    public static bool IsFullyCleared(TaxiRouteSegment segment)
        => segment is not null && segment.ClearedRunways.Count > 0 && !segment.IsHoldShortPoint;

    /// <summary>
    /// Whether the hold-short countdown counts <paramref name="segment"/> down as a cleared crossing: a stop every
    /// runway of which is cleared (<see cref="IsFullyCleared"/>), while taxiing, that is not the end of a leg with no
    /// destination. A runway is never crossed in silence, so it is counted down at the same distances as a stop, in
    /// its own words (<see cref="ClearedCrossingCallouts.Approach"/>). A leg end on the same segment wins: its own
    /// countdown is spoken, and it already holds its slow-down and stop calls off a runway (ProgressiveLegEnd). Not
    /// while holding: the frame that enters another stop's hold never adds a cleared crossing's line to it.
    /// </summary>
    public static bool CountsDownAsCleared(TaxiRouteSegment segment, bool legEndApplies, bool taxiing)
        => taxiing && !legEndApplies && IsFullyCleared(segment);

    /// <summary>The ground speed, knots, at or above which an aircraft at a cleared start has started across it.</summary>
    public const double StartCrossingMovingKts = 2.0;

    /// <summary>
    /// Whether an aircraft standing at a cleared start line has started across it, which is when the crossing is said:
    /// moving at <see cref="StartCrossingMovingKts"/> or more (the manager's own line for a real taxi start), or more
    /// than RouteRunwayCrossings.StopPassedToleranceMetres along the route (<paramref name="routeProgress"/>, in
    /// metres, from RouteRunwayCrossings.RouteProgressMeters). Never while it stands at the line.
    /// </summary>
    public static bool HasStartedAcross(double groundSpeedKts, double routeProgress)
        => groundSpeedKts >= StartCrossingMovingKts || routeProgress > RouteRunwayCrossings.StopPassedToleranceMetres;

    /// <summary>
    /// What a cleared line is called when it is counted down and said: the label its stop was placed with, else
    /// (never left so by <see cref="ClearAt"/>) its cleared runways, so it is never said without a runway.
    /// </summary>
    public static string LineLabel(string? placedLabel, IReadOnlyCollection<string> clearedRunways)
    {
        if (!string.IsNullOrWhiteSpace(placedLabel)) return placedLabel;
        if (clearedRunways is null || clearedRunways.Count == 0) return "runway";
        return string.Join(" and ", clearedRunways.Select(r => "runway " + r));
    }

    /// <summary>
    /// Whether a crossing in <paramref name="crossings"/> is cleared at the stop on node <paramref name="stopNodeId"/>.
    /// Never for node 0, which a crossing with no stop records. No allocation: asked on position frames.
    /// </summary>
    public static bool HasStop(IReadOnlyList<ClearedCrossing> crossings, int stopNodeId)
    {
        if (crossings is null || stopNodeId <= 0) return false;
        for (int i = 0; i < crossings.Count; i++)
            if (crossings[i]?.StopNodeId == stopNodeId) return true;
        return false;
    }

    /// <summary>
    /// <paramref name="crossings"/> without every crossing cleared at the stop on node <paramref name="stopNodeId"/>:
    /// a cleared crossing is used once, at its line. Always a NEW list, never the one given changed: the manager's
    /// list is held by LoadRoute's refusal snapshot and its rollback, so it is replaced, never mutated in place.
    /// </summary>
    public static List<ClearedCrossing> WithoutStop(IReadOnlyList<ClearedCrossing> crossings, int stopNodeId)
    {
        var kept = new List<ClearedCrossing>();
        if (crossings is null) return kept;
        foreach (var c in crossings)
            if (c is not null && (stopNodeId <= 0 || c.StopNodeId != stopNodeId)) kept.Add(c);
        return kept;
    }

    /// <summary>The first segment at or after <paramref name="fromSegmentIndex"/> with a runway cleared at its stop, else -1.</summary>
    public static int NextClearedSegment(TaxiRoute route, int fromSegmentIndex)
    {
        if (route is null) return -1;
        for (int i = Math.Max(0, fromSegmentIndex); i < route.Segments.Count; i++)
            if (route.Segments[i].ClearedRunways.Count > 0) return i;
        return -1;
    }

    /// <summary>
    /// What the pilot is told about the crossings that could not be cleared, one sentence per request: a runway the
    /// route does not cross, and one it crosses only somewhere else, whose stop stays. Empty when there are none.
    /// </summary>
    public static string WarningFor(Outcome outcome)
    {
        if (outcome is null) return "";
        var sentences = new List<string>();
        foreach (var c in outcome.NotOnRoute)
            sentences.Add($"The route does not cross runway {c.Runway.Trim()}.");
        foreach (var c in outcome.Elsewhere)
            sentences.Add(string.IsNullOrWhiteSpace(c.At)
                ? $"Runway {c.Runway.Trim()} is not crossed there. It is not cleared; you will be stopped there."
                : $"Runway {c.Runway.Trim()} is not crossed at {c.At.Trim()}. It is not cleared; you will be stopped there.");
        return string.Join(" ", sentences);
    }

    /// <summary>
    /// After a recalculation: each crossing cleared on the route before that the new route did not clear again at
    /// the same node, while the new route still has a stop for that runway ahead. One sentence per runway.
    /// </summary>
    public static string DroppedOnRecalculation(TaxiRoute route, IReadOnlyList<ClearedCrossing> before, Outcome after)
    {
        if (route is null || before is null) return "";
        var applied = after?.Applied ?? Array.Empty<ClearedCrossing>();
        var events = route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>();
        var said = new List<string>();
        var sentences = new List<string>();
        foreach (var c in before)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Runway)) continue;
            if (applied.Any(a => SameRunway(a.Runway, c.Runway) && a.StopNodeId == c.StopNodeId)) continue;
            bool stopAhead = events.Any(e => e is { Kind: RunwayEventKind.Crossing, Held: true, Passed: false, Cleared: false }
                                             && SameRunway(e.Designator, c.Runway));
            if (!stopAhead || said.Any(s => SameRunway(s, c.Runway))) continue;
            said.Add(c.Runway);
            sentences.Add($"Runway {c.Runway.Trim()} is a stop again.");
        }
        return string.Join(" ", sentences);
    }

    /// <summary>The "Cleared crossings:" log line: runway@stop node applied, the runways not on the route, runway@place elsewhere.</summary>
    public static string DescribeForLog(Outcome outcome)
    {
        static string ListOrNone(IEnumerable<string> items)
        {
            string joined = string.Join(",", items);
            return joined.Length > 0 ? joined : "(none)";
        }
        var applied = outcome?.Applied ?? Array.Empty<ClearedCrossing>();
        var notOnRoute = outcome?.NotOnRoute ?? Array.Empty<ClearedCrossing>();
        var elsewhere = outcome?.Elsewhere ?? Array.Empty<ClearedCrossing>();
        return $"Cleared crossings: applied={ListOrNone(applied.Select(c => $"{c.Runway}@{c.StopNodeId}"))} " +
               $"notOnRoute={ListOrNone(notOnRoute.Select(c => c.Runway))} " +
               $"elsewhere={ListOrNone(elsewhere.Select(c => string.IsNullOrWhiteSpace(c.At) ? $"{c.Runway}@{c.AtNodeId}" : $"{c.Runway}@{c.At}"))}";
    }

    // ---- matching ----

    private static bool IsCandidate(TaxiRoute route, TaxiRouteRunwayEvent e)
        => e.Held && !e.Passed && !e.Cleared && e.StopNodeIndex >= 0
           && CanClear(StopLabel(route, e.StopNodeIndex), e.Designator);

    // #355's decision 3: a crossing the route makes with no stop of its own, still ahead and not yet cleared.
    private static bool HasNoStop(TaxiRouteRunwayEvent e) => !e.Held && !e.Passed && !e.Cleared;

    private static TaxiRouteRunwayEvent? FirstMatch(TaxiRoute route, IEnumerable<TaxiRouteRunwayEvent> candidates, ClearedCrossing request)
    {
        bool hasAt = !string.IsNullOrWhiteSpace(request.At);
        foreach (var e in candidates)
        {
            if (!hasAt && request.AtNodeId == 0) return e;
            if (hasAt && (SamePlace(e.Location, request.At!) || SamePlace(StopTaxiway(route, e.StopNodeIndex), request.At!)))
                return e;
            if (request.AtNodeId != 0 && StopNode(route, e.StopNodeIndex)?.NodeId == request.AtNodeId) return e;
        }
        return null;
    }

    private static ClearedCrossing ClearMatch(TaxiRoute route, TaxiRouteRunwayEvent match, ClearedCrossing request)
    {
        int stopNodeId = StopNode(route, match.StopNodeIndex)?.NodeId ?? 0;
        ClearAt(route, match.StopNodeIndex, match.Designator);
        return request with { StopNodeId = stopNodeId };
    }

    // The events at that stop for that runway (either end); every event at it when runway is null.
    private static void MarkCleared(TaxiRoute route, int stopNodeIndex, string? runway, bool cleared)
    {
        foreach (var e in route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>())
            if (e != null && e.StopNodeIndex == stopNodeIndex && (runway is null || SameRunway(e.Designator, runway)))
                e.Cleared = cleared;
    }

    private static void AddCleared(List<string> clearedRunways, string runway)
    {
        string normalized = RouteRunwayCrossings.NormalizeDesignator(runway);
        if (!clearedRunways.Any(c => SameRunway(c, normalized))) clearedRunways.Add(normalized);
    }

    // ---- the stop at a route node ----

    private static TaxiNode? StopNode(TaxiRoute route, int stopNodeIndex)
    {
        if (stopNodeIndex == 0) return route.Segments.Count > 0 ? route.Segments[0].FromNode : null;
        return stopNodeIndex >= 1 && stopNodeIndex - 1 < route.Segments.Count ? route.Segments[stopNodeIndex - 1].ToNode : null;
    }

    private static string? StopLabel(TaxiRoute route, int stopNodeIndex)
    {
        if (stopNodeIndex == 0) return route.StartHoldRunway;
        if (stopNodeIndex < 1 || stopNodeIndex - 1 >= route.Segments.Count) return null;
        var segment = route.Segments[stopNodeIndex - 1];
        return segment.IsHoldShortPoint ? segment.HoldShortRunway : null;
    }

    // The taxiway the stop is on: the segment ending at it, or for a start hold the one leaving it.
    private static string? StopTaxiway(TaxiRoute route, int stopNodeIndex)
    {
        if (stopNodeIndex == 0) return route.Segments.Count > 0 ? route.Segments[0].TaxiwayName : null;
        return stopNodeIndex >= 1 && stopNodeIndex - 1 < route.Segments.Count ? route.Segments[stopNodeIndex - 1].TaxiwayName : null;
    }

    // ---- labels ----

    /// <summary>
    /// Whether clearing <paramref name="runway"/> takes a part off <paramref name="label"/>: the label names the
    /// runway (either end) as the first designator of one of its parts, and that part is not the pilot's own stop
    /// shared with it ("runway 09 at taxiway W"), which stays.
    /// </summary>
    private static bool CanClear(string? label, string runway)
    {
        if (string.IsNullOrEmpty(label)) return false;
        if (!RouteRunwayCrossings.ExtractRunwayDesignators(label).Any(d => SameRunway(d, runway))) return false;
        bool removes = false;
        foreach (string part in LabelParts(label))
        {
            if (RouteRunwayCrossings.ExtractRunwayDesignator(part) is not string first || !SameRunway(first, runway)) continue;
            if (IsPilotStop(part)) return false;
            removes = true;
        }
        return removes;
    }

    private static bool IsPilotStop(string part)
    {
        int at = part.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;
        string place = part[(at + 4)..].TrimStart();
        return PilotStopLabels.Any(p => place.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // "runway 06L at D5 and runway 02" -> "runway 06L at D5", "runway 02"; joined again with " and ".
    private static List<string> LabelParts(string label)
    {
        var parts = new List<string>();
        int start = 0;
        while (true)
        {
            int next = label.IndexOf(PartSeparator, start, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
            {
                parts.Add(label[start..]);
                return parts;
            }
            parts.Add(label[start..next]);
            start = next + " and ".Length;
        }
    }

    private static bool SamePlace(string? place, string at)
        => !string.IsNullOrWhiteSpace(place) && string.Equals(place.Trim(), at.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameRunway(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        string x = RouteRunwayCrossings.NormalizeDesignator(a);
        string y = RouteRunwayCrossings.NormalizeDesignator(b);
        return x.Equals(y, StringComparison.OrdinalIgnoreCase)
            || x.Equals(RouteRunwayCrossings.Reciprocal(y), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Every sentence BA speaks about a cleared crossing, exactly as worded. Distances arrive as ready-made text from
/// <c>DistanceFormatter</c> (the pilot's metres or feet): this class never writes a unit. Pure.
/// </summary>
public static class ClearedCrossingCallouts
{
    public const string NoRunwayAhead = "No runway ahead on this route.";
    public const string NoRoute = "No taxi route.";

    /// <summary>The countdown to a cleared crossing; the first one also says it is cleared.</summary>
    public static string Approach(string placedLabel, string distanceText, bool first)
    {
        string label = string.IsNullOrEmpty(placedLabel) ? placedLabel : char.ToUpperInvariant(placedLabel[0]) + placedLabel[1..];
        return first ? $"{label} in {distanceText}, cleared to cross." : $"{label} in {distanceText}.";
    }

    /// <summary>At the hold line of a cleared crossing.</summary>
    public static string AtTheLine(string placedLabel) => $"Crossing {placedLabel}.";

    /// <summary>
    /// A Continue's confirmation, then <paramref name="crossingLine"/> (<see cref="AtTheLine"/> for the runway cleared at
    /// a stop that held for another), as one utterance; the confirmation exactly as it was when there is none.
    /// </summary>
    public static string AfterContinue(string confirmation, string? crossingLine)
        => string.IsNullOrWhiteSpace(crossingLine) ? confirmation : $"{confirmation.TrimEnd()} {crossingLine}";

    /// <summary>Continue pressed early: <paramref name="clearedRunway"/> is cleared, and what still holds (null: nothing).</summary>
    public static string EarlyContinue(string clearedRunway, string? remainingLabel)
        => string.IsNullOrWhiteSpace(remainingLabel)
            ? $"Cleared across runway {clearedRunway}. No stop."
            : $"Cleared across runway {clearedRunway}. Still holding short of {remainingLabel}.";

    /// <summary>A cleared crossing put back as a stop.</summary>
    public static string HoldRestored(string label, string distanceText) => $"Holding short of {label}. {distanceText}.";

    /// <summary>The aircraft is past a stop's line.</summary>
    public static string PastTheLine(string placedLabel) => $"Past the hold line of {placedLabel}.";

    /// <summary>The next stop of any kind.</summary>
    public static string NextStop(string label, string distanceText) => $"Next stop {label} in {distanceText}.";

    /// <summary>A clause for a status readout: a cleared crossing, or a stop to hold short of.</summary>
    public static string StatusClause(string label, string distanceText, bool cleared)
        => cleared ? $", {label} in {distanceText}, cleared to cross" : $", hold short of {label} in {distanceText}";
}
