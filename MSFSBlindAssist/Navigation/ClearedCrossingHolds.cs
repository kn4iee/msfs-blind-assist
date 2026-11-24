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
/// route: the stop keeps the label it was placed with (<see cref="TaxiRouteSegment.PlacedHoldLabel"/>), the runway
/// goes on <see cref="TaxiRouteSegment.ClearedRunways"/>, and its event stays <see cref="TaxiRouteRunwayEvent.Held"/>,
/// marked <see cref="TaxiRouteRunwayEvent.Cleared"/>, so it is still counted down and announced.
///
/// <para>Never by runway name alone, never carried to another crossing of the same runway: a request clears the
/// first crossing of its runway that matches its place, and a recalculation clears only the stop at the node it
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
    /// it: held, not passed, not cleared, with a stop node. On the pilot's Calculate the match is the first
    /// candidate, or with a place the first whose event location or stop segment's taxiway is <c>At</c>, or whose
    /// stop node is <c>AtNodeId</c>; with <paramref name="runStartSegmentOf"/> and a <c>SeqIndex</c>, candidates
    /// whose stop lies at or after that taxiway step's first segment are tried first. No crossing of the runway at
    /// all: <see cref="Outcome.NotOnRoute"/>; candidates but no match: <see cref="Outcome.Elsewhere"/>; a runway only
    /// crossed already, or held only by the pilot's own taxiway stop, neither. With no candidate, a crossing with no
    /// stop (#355's decision 3) is marked cleared, so it is not said as having "no hold short point".</para>
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

            if (candidates.Count > 0)
            {
                TaxiRouteRunwayEvent? match = null;
                if (runStartSegmentOf != null && request.SeqIndex >= 0)
                {
                    int runStart = runStartSegmentOf(request.SeqIndex);
                    if (runStart >= 0)
                        match = FirstMatch(route, candidates.Where(e => e.StopNodeIndex >= runStart), request);
                }
                match ??= FirstMatch(route, candidates, request);
                if (match != null) applied.Add(ClearMatch(route, match, request));
                else elsewhere.Add(request);
                continue;
            }

            var noStop = crossings.FirstOrDefault(e => !e.Held && !e.Passed && !e.Cleared
                && (string.IsNullOrWhiteSpace(request.At) || SamePlace(e.Location, request.At)));
            if (noStop != null)
            {
                noStop.Cleared = true;
                applied.Add(request with { StopNodeId = 0 });
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
            if (!CanClear(route.StartHoldRunway, runway)) return false;
            route.StartHoldRunway = RemainingLabel(route.StartHoldRunway, new[] { runway });
            MarkCleared(route, 0, runway, cleared: true);
            return true;
        }

        if (stopNodeIndex - 1 >= route.Segments.Count) return false;
        var segment = route.Segments[stopNodeIndex - 1];
        if (!segment.IsHoldShortPoint || !CanClear(segment.HoldShortRunway, runway)) return false;

        segment.PlacedHoldLabel ??= segment.HoldShortRunway;
        string normalized = RouteRunwayCrossings.NormalizeDesignator(runway);
        if (!segment.ClearedRunways.Any(c => SameRunway(c, normalized))) segment.ClearedRunways.Add(normalized);
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

    /// <summary>A stop every runway of which is cleared: no longer a hold short, but still counted down and announced.</summary>
    public static bool IsFullyCleared(TaxiRouteSegment segment)
        => segment is not null && segment.ClearedRunways.Count > 0 && !segment.IsHoldShortPoint;

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

    private static void MarkCleared(TaxiRoute route, int stopNodeIndex, string runway, bool cleared)
    {
        foreach (var e in route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>())
            if (e != null && e.StopNodeIndex == stopNodeIndex && SameRunway(e.Designator, runway)) e.Cleared = cleared;
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
