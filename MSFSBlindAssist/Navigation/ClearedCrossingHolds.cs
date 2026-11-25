using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>Who cleared a runway crossing: a crossing the pilot entered (a Cross step, the clearance), or the Continue key.</summary>
public enum ClearedBy
{
    /// <summary>A crossing the pilot entered: a Cross step, or the "After crossing runway" leg.</summary>
    Clearance,
    /// <summary>A Continue pressed before the line, or the paired double Continue: no Cross step stands for it.</summary>
    Continue,
}

/// <summary>
/// A runway crossing the pilot was cleared for, as entered: the runway, and where (<paramref name="At"/>, a
/// taxiway or holding-point name, or <paramref name="AtNodeId"/>, a node). <paramref name="StopNodeId"/> is the
/// node of the stop it cleared, recorded when it is applied (0: not yet, or a crossing with no stop), which a
/// recalculation matches on. <paramref name="SeqIndex"/> is the taxiway step the crossing was entered after, -1 for none.
/// <paramref name="Origin"/> is who cleared it (a crossing entered and one a Continue cleared both carry
/// <c>SeqIndex</c> -1), kept through a recalculation. <paramref name="ReachNodeId"/> is the node where the route
/// reaches the runway, recorded when it is applied (0: not recorded), by which a crossing with no stop is used up
/// once the aircraft is past it (<see cref="ClearedCrossingHolds.WithoutPassedNoStop"/>).
/// </summary>
public sealed record ClearedCrossing(string Runway, string? At = null, int AtNodeId = 0, int StopNodeId = 0, int SeqIndex = -1,
                                     ClearedBy Origin = ClearedBy.Clearance, int ReachNodeId = 0);

/// <summary>
/// What <c>TaxiGuidanceManager.ClearedCrossingsChanged</c> carries: every crossing the Hold short key put back as a stop,
/// as it was recorded (its runway, place, stop node and who cleared it): one for a stop guarding one runway, one per
/// cleared runway at a shared stop. Empty only for a stop whose record was already gone.
/// </summary>
public sealed class ClearedCrossingsChangedEventArgs : EventArgs
{
    public ClearedCrossingsChangedEventArgs(IReadOnlyList<ClearedCrossing> restored)
        => Restored = restored ?? Array.Empty<ClearedCrossing>();

    /// <summary>The crossings put back, in the order they were recorded.</summary>
    public IReadOnlyList<ClearedCrossing> Restored { get; }
}

/// <summary>
/// Clears ONE runway crossing at ONE place, and puts it back. The automatic pass still places a stop before every
/// runway on every route [HLD-4]; this runs after it and only takes a stop the pilot was cleared across off the
/// route: the stop keeps the label it was placed with (<see cref="TaxiRouteSegment.PlacedHoldLabel"/>; a start hold,
/// <see cref="TaxiRoute.PlacedStartHoldLabel"/>), the runway goes on <see cref="TaxiRouteSegment.ClearedRunways"/>
/// (<see cref="TaxiRoute.StartHoldClearedRunways"/>), and its event stays <see cref="TaxiRouteRunwayEvent.Held"/>,
/// marked <see cref="TaxiRouteRunwayEvent.Cleared"/>, so it is still counted down and announced.
///
/// <para>Never by runway name alone, never carried to another crossing of the same runway: a request is about the
/// first crossing of its runway, in route order, that matches its place, and clears it or nothing (one already passed,
/// or held by the pilot's own stop, keeps the request), and a recalculation clears only the stop at the node it
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
    /// order (where the route reaches the runway, <see cref="TaxiRouteRunwayEvent.ReachNodeIndex"/>) from EVERY
    /// crossing of the runway not yet cleared, passed ones and ones held by the pilot's own stop included, so a bare
    /// request is about the crossing in front of the aircraft, never a later one: the first of them, or with a place
    /// the first whose event location or stop segment's taxiway is <c>At</c> (a crossing with no stop: its location),
    /// or whose route from its stop node to where it reaches the runway (both included) passes <c>AtNodeId</c> (a
    /// holding point the route is pinned through can lie between the scenery line the pass stopped at and the
    /// runway). With <paramref name="runStartSegmentOf"/> and a <c>SeqIndex</c>, those the route reaches at or after
    /// that taxiway step's first segment are tried first: the reach, not the stop, which can be set back onto the
    /// taxiway before (the binding a pilot's runway pick uses). The match is cleared when it is a candidate, or marked
    /// cleared when it is a crossing with no stop (#355's decision 3: neither held nor passed), so it is not said as
    /// having "no hold short point". A match already passed, or held by the pilot's own taxiway, holding-point or
    /// end-of-taxiway stop, keeps the request: nothing is cleared and nothing said, and it is never passed on to
    /// another crossing of the runway. No crossing of the runway at all: <see cref="Outcome.NotOnRoute"/>; candidates
    /// but no match: <see cref="Outcome.Elsewhere"/>.</para>
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

            // Route order: the pass records its events in it, but a pilot pick's event is merged in at the end. Every
            // crossing not yet cleared, passed and pilot-held ones too: the first that matches the request is the one it
            // is about, and one that cannot be cleared keeps it, never passing it on to a later (or earlier) crossing.
            var ordered = crossings
                .Where(e => !e.Cleared)
                .OrderBy(e => e.ReachNodeIndex < 0 ? int.MaxValue : e.ReachNodeIndex)
                .ToList();
            TaxiRouteRunwayEvent? match = null;
            if (runStartSegmentOf != null && request.SeqIndex >= 0)
            {
                int runStart = runStartSegmentOf(request.SeqIndex);
                if (runStart >= 0)
                    match = FirstMatch(route, ordered.Where(e => e.ReachNodeIndex >= runStart), request);
            }
            match ??= FirstMatch(route, ordered, request);

            if (match is null)
            {
                if (candidates.Count > 0) elsewhere.Add(request);
            }
            else if (IsCandidate(route, match))
            {
                applied.Add(ClearMatch(route, match, request));
            }
            else if (HasNoStop(match))
            {
                match.Cleared = true;
                applied.Add(request with { StopNodeId = 0, ReachNodeId = StopNode(route, match.ReachNodeIndex)?.NodeId ?? 0 });
            }
            // Else passed, or held by the pilot's own stop: nothing cleared, in neither list, and no other crossing tried.
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
    /// Puts back every runway cleared at segment <paramref name="segmentIndex"/>'s stop: its placed label, with any
    /// runway a later pick added to the stop kept (<see cref="RestoredLabel"/>), a hold short again, and its events no
    /// longer cleared. False when nothing was cleared there.
    /// </summary>
    public static bool RestoreAt(TaxiRoute route, int segmentIndex)
    {
        if (route is null || segmentIndex < 0 || segmentIndex >= route.Segments.Count) return false;
        var segment = route.Segments[segmentIndex];
        if (segment.ClearedRunways.Count == 0) return false;

        segment.HoldShortRunway = RestoredLabel(segment.PlacedHoldLabel, segment.IsHoldShortPoint ? segment.HoldShortRunway : null);
        segment.IsHoldShortPoint = true;
        segment.ClearedRunways.Clear();
        segment.PlacedHoldLabel = null;
        foreach (var e in route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>())
            if (e != null && e.StopNodeIndex == segmentIndex + 1) e.Cleared = false;
        return true;
    }

    /// <summary>
    /// The label a stop is put back with: every part of <paramref name="placedLabel"/>, the label it was placed with,
    /// then each part <paramref name="currentLabel"/> carries that the placed label lacks, joined with <c>" and "</c>.
    /// A pilot's runway pick retried after the clearing (RetryDeferredRunwayPicks runs after the pass that clears) can
    /// share a cleared stop and add its own runway to it; the restore keeps that runway, so it is still staged, one
    /// Continue each ([HLD-11]), and said at the line. Split as <see cref="RemainingLabel"/> splits, at
    /// <c>" and runway "</c>: a part naming a runway is lacked when no placed part names that runway first (either end), a
    /// part naming none when no placed part reads the same. The placed label alone when the current one adds nothing
    /// (a stop partly cleared carries only placed parts); the current one when nothing was placed.
    /// </summary>
    public static string? RestoredLabel(string? placedLabel, string? currentLabel)
    {
        if (string.IsNullOrWhiteSpace(placedLabel)) return string.IsNullOrWhiteSpace(currentLabel) ? placedLabel : currentLabel;
        if (string.IsNullOrWhiteSpace(currentLabel)) return placedLabel;

        var placedParts = LabelParts(placedLabel);
        var added = new List<string>();
        foreach (string part in LabelParts(currentLabel))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            bool lacked = RouteRunwayCrossings.ExtractRunwayDesignator(part) is string first
                ? !placedParts.Any(p => RouteRunwayCrossings.ExtractRunwayDesignator(p) is string placedFirst && SameRunway(placedFirst, first))
                : !placedParts.Any(p => string.Equals(p.Trim(), part.Trim(), StringComparison.OrdinalIgnoreCase));
            if (lacked) added.Add(part.Trim());
        }
        return added.Count == 0 ? placedLabel : placedLabel + " and " + string.Join(" and ", added);
    }

    // Knots to feet per second.
    private const double KtsToFps = 1852.0 * 3.28084 / 3600.0;
    private const double MetresToFeet = 3.28084;

    /// <summary>
    /// The hold-short countdown's outer call (the heads-up), in feet from the stop: 15 s at
    /// <paramref name="groundSpeedKts"/>, never under 300 ft nor over 600 ft. The ONE formula: the countdown's own
    /// tier (TaxiGuidanceManager's CheckHoldShortCountdown) and the Hold short key's restore
    /// (<see cref="RestoreSaysOuterCall"/>) both use it.
    /// </summary>
    public static double OuterCountdownFt(double groundSpeedKts)
        => Math.Clamp(groundSpeedKts * KtsToFps * 15.0, 300.0, 600.0);

    /// <summary>
    /// Whether the Hold short key's sentence for a stop it puts back on the current segment while taxiing stands for that
    /// stop's outer countdown call (so the outer call is latched): only within the outer call's distance
    /// (<see cref="OuterCountdownFt"/>), where that call would only cut the sentence off with the same distance.
    /// Further out the outer call still comes, as the heads-up, and the countdown's first call is never "Slow down".
    /// <paramref name="stopDistanceM"/> is the key's own distance to the stop, unit-native (straight to the current
    /// segment's end).
    /// </summary>
    public static bool RestoreSaysOuterCall(double stopDistanceM, double groundSpeedKts)
        => stopDistanceM * MetresToFeet < OuterCountdownFt(groundSpeedKts);

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
    /// when every runway is cleared. Null when no part is. Not null says a cleared runway is crossed at that stop: the
    /// Continue that releases a stop still holding for another runway then names every runway at it
    /// (<see cref="LineLabel"/>, the whole placed label), since the aircraft crosses them all.
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
    /// Puts back every runway cleared at the start hold: its placed label, with any runway a later pick added kept
    /// (<see cref="RestoredLabel"/>), and its events (stop node 0) no longer cleared. False when nothing was cleared there.
    /// </summary>
    public static bool RestoreStartHold(TaxiRoute route)
    {
        if (!IsStartHoldCleared(route)) return false;
        route.StartHoldRunway = RestoredLabel(route.PlacedStartHoldLabel, route.StartHoldRunway);
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

    /// <summary>
    /// Whether a Continue pressed while taxiing, before the line, clears the stop ahead (ATC often clears a crossing
    /// while the aircraft is still rolling): only once the hold-short countdown has named it
    /// (<paramref name="countdownSpoken"/>, any of its tiers), so a stray key never clears a runway the pilot has not
    /// been told about; only at a hold short (<paramref name="isHoldShort"/>) whose <paramref name="label"/> names at
    /// least one runway, never a taxiway, holding-point or end-of-taxiway stop; never on the route's last segment
    /// (<paramref name="isFinalSegment"/>: the departure runway's own hold, the end of a leg). The press clears the first
    /// runway the label names, through <see cref="ClearAt"/>, which can still refuse a stop shared with the pilot's own
    /// taxiway hold; a stop guarding two runways keeps the other, which takes its own Continue ([HLD-11]).
    /// </summary>
    public static bool CanClearEarly(bool countdownSpoken, bool isHoldShort, string? label, bool isFinalSegment)
        => countdownSpoken && isHoldShort && !isFinalSegment
           && RouteRunwayCrossings.ExtractRunwayDesignators(label).Count > 0;

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

    /// <summary>
    /// The crossings in <paramref name="crossings"/> cleared at the stop on node <paramref name="stopNodeId"/>, in a NEW
    /// list, in the order recorded. Never for node 0, which a crossing with no stop records.
    /// </summary>
    public static List<ClearedCrossing> AtStop(IReadOnlyList<ClearedCrossing> crossings, int stopNodeId)
    {
        var at = new List<ClearedCrossing>();
        if (crossings is null || stopNodeId <= 0) return at;
        foreach (var c in crossings)
            if (c is not null && c.StopNodeId == stopNodeId) at.Add(c);
        return at;
    }

    /// <summary>
    /// What the Hold short key put back at the stop on node <paramref name="stopNodeId"/>, for the window
    /// (<see cref="ClearedCrossingsChangedEventArgs"/>): the crossings still recorded there (<see cref="AtStop"/>), and,
    /// when that stop's line was the one said as passed (<paramref name="passedNodeId"/>; the segment jump says one up
    /// to ~100 m early), the crossings used up there (<paramref name="usedAtPassedLine"/>). A NEW list.
    /// </summary>
    public static List<ClearedCrossing> RestoredCrossings(IReadOnlyList<ClearedCrossing> recorded, int stopNodeId,
                                                          int passedNodeId, IReadOnlyList<ClearedCrossing>? usedAtPassedLine)
    {
        var restored = AtStop(recorded, stopNodeId);
        if (stopNodeId > 0 && passedNodeId == stopNodeId && usedAtPassedLine is not null)
            foreach (var c in usedAtPassedLine)
                if (c is not null && c.StopNodeId == stopNodeId) restored.Add(c);
        return restored;
    }

    /// <summary>
    /// <paramref name="crossings"/> without each crossing with no stop of its own (stop node 0, #355's decision 3) that
    /// the aircraft is past: the segment cursor <paramref name="cursor"/> has reached the route node where the route
    /// reaches its runway (<see cref="ClearedCrossing.ReachNodeId"/>). A crossing with no stop is never at a line, so
    /// <see cref="WithoutStop"/> never uses it; this does, once. A NEW list when one goes (the manager's list is replaced,
    /// never mutated); null when none does, without allocating: asked on taxiing frames. A crossing whose reach was not
    /// recorded, or is not a node of this route, stays.
    /// </summary>
    public static List<ClearedCrossing>? WithoutPassedNoStop(IReadOnlyList<ClearedCrossing> crossings, TaxiRoute route, int cursor)
    {
        if (crossings is null || route is null || route.Segments.Count == 0) return null;
        List<ClearedCrossing>? kept = null;
        for (int i = 0; i < crossings.Count; i++)
        {
            var c = crossings[i];
            bool passed = c is { StopNodeId: 0, ReachNodeId: > 0 } && RouteNodeIndex(route, c.ReachNodeId) is int reach && reach <= cursor;
            if (passed && kept is null)
            {
                kept = new List<ClearedCrossing>(crossings.Count);
                for (int j = 0; j < i; j++) if (crossings[j] is not null) kept.Add(crossings[j]);
            }
            else if (!passed && kept is not null && c is not null)
            {
                kept.Add(c);
            }
        }
        return kept;
    }

    // The first route node index (0: the start, n: segment n - 1's end) whose node is nodeId; null when none is.
    private static int? RouteNodeIndex(TaxiRoute route, int nodeId)
    {
        if (route.Segments[0].FromNode?.NodeId == nodeId) return 0;
        for (int i = 0; i < route.Segments.Count; i++)
            if (route.Segments[i].ToNode?.NodeId == nodeId) return i + 1;
        return null;
    }

    /// <summary>
    /// <paramref name="crossings"/> with <paramref name="crossing"/> added: how a Continue that clears a stop before its
    /// line records it, with its stop node, like a crossing the pilot entered. Always a NEW list, never the one given
    /// changed (as <see cref="WithoutStop"/>); the same runway (either end) already recorded at the same stop is not added twice.
    /// </summary>
    public static List<ClearedCrossing> WithCrossing(IReadOnlyList<ClearedCrossing> crossings, ClearedCrossing crossing)
    {
        var all = new List<ClearedCrossing>();
        if (crossings is not null)
            foreach (var c in crossings)
                if (c is not null) all.Add(c);
        if (crossing is not null && !all.Any(c => c.StopNodeId == crossing.StopNodeId && SameRunway(c.Runway, crossing.Runway)))
            all.Add(crossing);
        return all;
    }

    /// <summary>The first segment at or after <paramref name="fromSegmentIndex"/> with a runway cleared at its stop, else -1.</summary>
    public static int NextClearedSegment(TaxiRoute route, int fromSegmentIndex)
    {
        if (route is null) return -1;
        for (int i = Math.Max(0, fromSegmentIndex); i < route.Segments.Count; i++)
            if (route.Segments[i].ClearedRunways.Count > 0) return i;
        return -1;
    }

    // ---- the Hold short key ----

    /// <summary>What the Hold short key (Input Alt+Y, spec 8.8) does: <see cref="DecideHoldShortKey"/>.</summary>
    public enum HoldShortKeyAnswer
    {
        /// <summary>No route, or guidance in a state with no route to hold on.</summary>
        NoRoute,
        /// <summary>The cleared start hold comes back (<see cref="RestoreStartHold"/>).</summary>
        RestoreStartHold,
        /// <summary>The cleared stop on <see cref="HoldShortKeyDecision.SegmentIndex"/> comes back (<see cref="RestoreAt"/>).</summary>
        RestoreSegment,
        /// <summary>The aircraft is past the line: said, nothing changes.</summary>
        PastTheLine,
        /// <summary>No cleared runway ahead: the next stop of any kind is named, nothing changes.</summary>
        NextStop,
        /// <summary>No stop ahead at all.</summary>
        NoRunwayAhead,
    }

    /// <summary>The aircraft as the key sees it: where it is, and how fast it moves (knots).</summary>
    public readonly record struct HoldShortKeyAircraft(double Lat, double Lon, double GroundSpeedKts);

    /// <summary>The last cleared line said as passed: its label, its stop node, and how long ago, in seconds (sim clock).</summary>
    public sealed record PassedLine(string Label, int StopNodeId, double SecondsAgo);

    /// <summary>
    /// What the key is judged from: whether guidance has a route to hold on (loaded, taxiing or holding short), the
    /// segment cursor, whether it is holding short and whether at the destination's own hold, the aircraft (null: no
    /// position yet), the runways, the last cleared line passed (null: none), and the destination's name.
    /// </summary>
    public sealed record HoldShortKeyState(bool Guiding, int CurrentSegmentIndex, bool Holding, bool HoldingAtDestination,
                                           HoldShortKeyAircraft? Aircraft, IReadOnlyList<TaxiGraph.RunwayCenterline>? Runways,
                                           PassedLine? Passed, string? DestinationName);

    /// <summary>
    /// The key's answer: what it does; the label it speaks; the segment whose stop it puts back or names (-1: the start
    /// hold, or none); how far that stop is along the route, in metres (as the status readout measures: straight to the
    /// current segment's end, then the segments after it); and, for a stop put back, the segment cursor guidance must
    /// have (the current one, or the stop's own segment when the cursor already moved past a line the aircraft is still
    /// short of). Metres are unit-native; the manager formats them (FormatDistance).
    /// </summary>
    public sealed record HoldShortKeyDecision(HoldShortKeyAnswer Answer, string Label = "", int SegmentIndex = -1,
                                              double Metres = 0.0, int CursorIndex = -1);

    /// <summary>How long after its line is said as passed a cleared crossing is still reported as passed.</summary>
    public const double PastTheLineWindowSeconds = 60.0;

    // How far either side of a stop, along the route, the key looks for the segment the aircraft is on.
    private const double StopWindowMetres = 200.0;

    /// <summary>
    /// The Hold short key (spec 8.8): which answer applies. It never changes the route; the manager acts on it. In order:
    /// <list type="number">
    /// <item>No route, or no route to hold on: <see cref="HoldShortKeyAnswer.NoRoute"/>.</item>
    /// <item>A cleared start hold, while the aircraft holds at it or has not started across it
    /// (<see cref="HasStartedAcross"/>, or inside the clear margin of a runway, the pass's own test for a start hold
    /// [HLD-10]): it comes back.</item>
    /// <item>The last cleared line said as passed, when the route geometry puts the aircraft still short of it (the
    /// segment jump says a line up to ~100 m early; a Continue releasing a shared stop says it at the press): it comes
    /// back, with the cursor back onto its segment.</item>
    /// <item>The next cleared runway ahead, or the shared stop the aircraft holds at: it comes back, unless the aircraft
    /// is past its line, which is said instead.</item>
    /// <item>A cleared start hold started across, while the aircraft is still crossing its runway (inside the clear
    /// margin of a runway it guards, or on the first segment and not yet across that runway's centreline): past its line,
    /// whether or not the line has been said (an aircraft that crept into the margin under 2 kt has not had it said),
    /// with no time limit.</item>
    /// <item>The line said as passed, for <see cref="PastTheLineWindowSeconds"/>, unless the route puts the aircraft
    /// short of it: <see cref="HoldShortKeyAnswer.PastTheLine"/>.</item>
    /// <item>The next stop of any kind: the start hold before it holds, a runway, a taxiway, a holding point, an end of
    /// taxiway, the destination's own hold (named too while holding there).</item>
    /// <item>Else <see cref="HoldShortKeyAnswer.NoRunwayAhead"/>.</item>
    /// </list>
    /// Past a line is more than RouteRunwayCrossings.StopPassedToleranceMetres beyond its stop node along the route
    /// (<c>MetresPastStop</c>), or within that tolerance of it (either side) and on the pavement of a runway the stop
    /// guards (RouteRunwayCrossings.RunwayUnder; a painted line may hug the pavement edge, which the pass accepts at
    /// margin 0), or, with the aircraft off the route there, on that pavement at all: BA never asks for a stop on a
    /// runway. Pure.
    /// </summary>
    public static HoldShortKeyDecision DecideHoldShortKey(TaxiRoute? route, HoldShortKeyState state)
    {
        if (route is null || state is null || !state.Guiding || route.Segments.Count == 0)
            return new HoldShortKeyDecision(HoldShortKeyAnswer.NoRoute);
        var segments = route.Segments;
        int cursor = Math.Clamp(state.CurrentSegmentIndex, 0, segments.Count);
        var aircraft = state.Aircraft;
        bool atDestination = state.Holding && state.HoldingAtDestination;
        bool atStartHold = state.Holding && !atDestination && cursor == 0 && route.StartHoldRunway != null;

        // The start hold: the aircraft stands at its line until it starts across.
        bool startCleared = IsStartHoldCleared(route) && !atDestination;
        string startLabel = startCleared ? LineLabel(route.PlacedStartHoldLabel, route.StartHoldClearedRunways) : "";
        if (startCleared && cursor == 0 && (atStartHold || !StartedAcross(route, aircraft, state.Runways)))
            return new HoldShortKeyDecision(HoldShortKeyAnswer.RestoreStartHold, startLabel, -1, MetresToStart(route, aircraft), cursor);

        // The line said as passed, still ahead by the route: the cursor already moved past it.
        if (!state.Holding && state.Passed is { StopNodeId: > 0 } passed && aircraft is { } a)
        {
            int k = PassedClearedSegment(route, cursor, passed.StopNodeId);
            if (k >= 0)
            {
                string label = LineLabel(segments[k].PlacedHoldLabel, segments[k].ClearedRunways);
                if (PastByRoute(route, k + 1, label, a, state.Runways) is false)
                    return new HoldShortKeyDecision(HoldShortKeyAnswer.RestoreSegment, label, k, MetresToStop(route, k, k, aircraft), k);
            }
        }

        // The next cleared runway ahead; holding at a stop cleared for one of its runways, that stop.
        int from = state.Holding && !atDestination && !atStartHold && cursor > 0 ? cursor - 1 : cursor;
        int next = NextClearedSegment(route, from);
        if (next >= 0)
        {
            string label = LineLabel(segments[next].PlacedHoldLabel, segments[next].ClearedRunways);
            bool heldHere = state.Holding && next == cursor - 1;
            if (!heldHere && IsPastStop(route, next + 1, label, aircraft, state.Runways))
                return new HoldShortKeyDecision(HoldShortKeyAnswer.PastTheLine, label, next);
            return new HoldShortKeyDecision(HoldShortKeyAnswer.RestoreSegment, label, next,
                MetresToStop(route, cursor, next, aircraft), cursor);
        }

        // Started across a cleared start hold (else it came back above) and still crossing its runway: past its line,
        // whether or not the line has been said (an aircraft that crept into the margin under 2 kt has not had it said),
        // with no time limit, never "no runway ahead" or a stop further on a few metres from that runway.
        if (startCleared && aircraft is { } crossing && StillCrossingStart(route, cursor, startLabel, crossing, state.Runways))
            return new HoldShortKeyDecision(HoldShortKeyAnswer.PastTheLine, startLabel);

        // The line said as passed, for a minute, unless the route puts the aircraft short of it.
        if (state.Passed is { } line && !string.IsNullOrWhiteSpace(line.Label)
            && line.SecondsAgo >= 0.0 && line.SecondsAgo <= PastTheLineWindowSeconds
            && !IsShortOfNode(route, line.StopNodeId, aircraft, state.Runways))
            return new HoldShortKeyDecision(HoldShortKeyAnswer.PastTheLine, line.Label.Trim());

        // The next stop of any kind.
        if (FindNextStop(route, cursor, aircraft, state.Runways, state.DestinationName, leaveOutLast: false,
                includeCleared: false, skipStart: state.Holding) is { } stop)
            return new HoldShortKeyDecision(HoldShortKeyAnswer.NextStop, stop.Label, stop.SegmentIndex, stop.Metres, cursor);
        if (atDestination)
        {
            int last = segments.Count - 1;
            return new HoldShortKeyDecision(HoldShortKeyAnswer.NextStop, StopName(route, last, state.DestinationName), last,
                MetresToStop(route, cursor, last, aircraft), cursor);
        }
        return new HoldShortKeyDecision(HoldShortKeyAnswer.NoRunwayAhead);
    }

    /// <summary>The next stop ahead as one sentence needs it: its label, its segment (-1: the start hold), the metres to it, and whether it is a cleared crossing.</summary>
    public readonly record struct NextStopInfo(string Label, int SegmentIndex, double Metres, bool Cleared);

    /// <summary>
    /// The next stop ahead of segment <paramref name="cursor"/>, the ONE finder behind the Hold short key's "Next stop"
    /// answer and the taxi status readout, so both name the same stop. In order: the start hold while the cursor is at
    /// the start (<paramref name="skipStart"/> false): a held one by its current label; with
    /// <paramref name="includeCleared"/>, one cleared for every runway and not yet started across
    /// (<see cref="StartedAcross"/>) by its placed label, cleared; then the first segment from the cursor that is a hold
    /// short of any kind (a partially cleared one by its CURRENT label, not cleared) or, with
    /// <paramref name="includeCleared"/>, a fully cleared crossing (its placed label, cleared). The route's last
    /// segment is left out with <paramref name="leaveOutLast"/> (the status sentence's end names it). Metres run as the
    /// status readout measures: straight to the end of the current segment, then the segments after it. Pure.
    /// </summary>
    public static NextStopInfo? FindNextStop(TaxiRoute? route, int cursor, HoldShortKeyAircraft? aircraft,
                                             IReadOnlyList<TaxiGraph.RunwayCenterline>? runways, string? destinationName,
                                             bool leaveOutLast, bool includeCleared, bool skipStart)
    {
        if (route is null || route.Segments.Count == 0) return null;
        var segments = route.Segments;
        cursor = Math.Clamp(cursor, 0, segments.Count);
        if (!skipStart && cursor == 0)
        {
            if (!string.IsNullOrWhiteSpace(route.StartHoldRunway))
                return new NextStopInfo(route.StartHoldRunway.Trim(), -1, MetresToStart(route, aircraft), false);
            if (includeCleared && IsStartHoldCleared(route) && !StartedAcross(route, aircraft, runways))
                return new NextStopInfo(LineLabel(route.PlacedStartHoldLabel, route.StartHoldClearedRunways), -1,
                    MetresToStart(route, aircraft), true);
        }
        int end = leaveOutLast ? segments.Count - 1 : segments.Count;
        for (int i = cursor; i < end; i++)
        {
            if (segments[i].IsHoldShortPoint)
                return new NextStopInfo(StopName(route, i, destinationName), i, MetresToStop(route, cursor, i, aircraft), false);
            if (includeCleared && IsFullyCleared(segments[i]))
                return new NextStopInfo(LineLabel(segments[i].PlacedHoldLabel, segments[i].ClearedRunways), i,
                    MetresToStop(route, cursor, i, aircraft), true);
        }
        return null;
    }

    /// <summary>
    /// How far beyond route node <paramref name="stopNodeIndex"/> (the end of segment <c>stopNodeIndex - 1</c>; 0 the
    /// start) the aircraft is, along the route: negative short of it. Its position on the route is its projection onto the
    /// nearest segment (clamped, ties to the earlier) within RouteRunwayCrossings.RouteJoinMaxCrossTrackMetres, among the
    /// segments within 200 m of the stop along the route, the projection RouteRunwayCrossings.RouteProgressMeters uses.
    /// Null when no segment there qualifies (the aircraft is off the route there) or the node is not on the route.
    /// </summary>
    public static double? MetresPastStop(TaxiRoute route, int stopNodeIndex, double lat, double lon)
    {
        if (route is null || stopNodeIndex < 0 || stopNodeIndex > route.Segments.Count) return null;
        var segments = route.Segments;
        double stopDistance = 0.0;
        for (int i = 0; i < stopNodeIndex; i++) stopDistance += SegmentLength(segments[i]);

        double? progress = null;
        double nearest = double.MaxValue, start = 0.0;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            double length = SegmentLength(seg);
            if (seg?.FromNode != null && seg.ToNode != null
                && start + length >= stopDistance - StopWindowMetres && start <= stopDistance + StopWindowMetres)
            {
                var (along, perpendicular) = RouteRunwayCrossings.ProjectOntoSegment(seg.FromNode, seg.ToNode, lat, lon);
                if (perpendicular <= RouteRunwayCrossings.RouteJoinMaxCrossTrackMetres && perpendicular < nearest)
                {
                    nearest = perpendicular;
                    progress = start + along;
                }
            }
            start += length;
        }
        return progress - stopDistance;
    }

    // Started across a cleared start hold: moving, more than the stop tolerance along the route (HasStartedAcross), or
    // inside the clear margin of a runway, where the pass never places a start hold and the per-frame start-hold entry
    // skips it. No position yet: still at the line.
    private static bool StartedAcross(TaxiRoute route, HoldShortKeyAircraft? aircraft, IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
        => aircraft is { } a
           && (HasStartedAcross(a.GroundSpeedKts, RouteRunwayCrossings.RouteProgressMeters(route.Segments, a.Lat, a.Lon))
               || RouteRunwayCrossings.RunwayWithinClearMargin(runways, a.Lat, a.Lon) != null);

    // Still crossing the runway a cleared start hold guards, once started across it: inside the clear margin of a runway
    // its label names, or, on the route's first segment, not yet across that runway's centreline (on the start node's
    // side of it). On the first segment alone is not enough: it can run on past the runway to a later stop, all of which
    // the start line would hide. With no runway in hand to judge by, the first segment is the bound.
    private static bool StillCrossingStart(TaxiRoute route, int cursor, string startLabel, HoldShortKeyAircraft aircraft,
                                           IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
    {
        var guarded = runways?.Where(r => r != null && GuardsRunway(startLabel, r)).ToList();
        if (guarded is null || guarded.Count == 0) return cursor == 0;
        if (RouteRunwayCrossings.RunwayWithinClearMargin(guarded, aircraft.Lat, aircraft.Lon) != null) return true;
        if (cursor != 0 || route.Segments[0].FromNode is not { } start) return false;
        foreach (var runway in guarded)
        {
            var shape = RunwayShape.For(runway);
            if (shape.IsDegenerate) continue;
            double startSide = shape.Project(start.Latitude, start.Longitude).Lateral;
            double aircraftSide = shape.Project(aircraft.Lat, aircraft.Lon).Lateral;
            if (startSide != 0.0 && Math.Sign(startSide) == Math.Sign(aircraftSide)) return true;
        }
        return false;
    }

    // Past the line of the stop on route node stopNodeIndex, by the route: more than the stop tolerance beyond its node,
    // or within it either side and on the pavement of a runway the stop guards (a painted line can hug the pavement edge,
    // so an aircraft a few metres past it can be on the runway). Null when the aircraft is off the route there.
    private static bool? PastByRoute(TaxiRoute route, int stopNodeIndex, string label, HoldShortKeyAircraft aircraft,
                                     IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
    {
        if (MetresPastStop(route, stopNodeIndex, aircraft.Lat, aircraft.Lon) is not double past) return null;
        return past > RouteRunwayCrossings.StopPassedToleranceMetres
               || (past > -RouteRunwayCrossings.StopPassedToleranceMetres && OnGuardedPavement(label, aircraft, runways));
    }

    // On the pavement of a runway the stop labelled label guards (RouteRunwayCrossings.RunwayUnder, from the runways in
    // hand: never building a graph).
    private static bool OnGuardedPavement(string label, HoldShortKeyAircraft aircraft, IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
        => runways is not null
           && RouteRunwayCrossings.RunwayUnder(runways.Where(r => r != null && GuardsRunway(label, r)), aircraft.Lat, aircraft.Lon) != null;

    // Past the line of the stop on route node stopNodeIndex: by the route; off the route there, on the pavement of a
    // runway the stop guards. No position yet: not past.
    private static bool IsPastStop(TaxiRoute route, int stopNodeIndex, string label, HoldShortKeyAircraft? aircraft,
                                   IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
    {
        if (aircraft is not { } a) return false;
        return PastByRoute(route, stopNodeIndex, label, a, runways) ?? OnGuardedPavement(label, a, runways);
    }

    // Whether the route puts the aircraft at or short of a stop on node stopNodeId (a segment's end), and not on its
    // runway: a line said as passed that a recalculation made a stop ahead again is never reported passed.
    private static bool IsShortOfNode(TaxiRoute route, int stopNodeId, HoldShortKeyAircraft? aircraft,
                                      IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
    {
        if (aircraft is not { } a || stopNodeId <= 0) return false;
        for (int i = 0; i < route.Segments.Count; i++)
        {
            var seg = route.Segments[i];
            if (seg.ToNode?.NodeId != stopNodeId) continue;
            string label = !string.IsNullOrWhiteSpace(seg.PlacedHoldLabel) ? seg.PlacedHoldLabel : seg.HoldShortRunway ?? "";
            if (PastByRoute(route, i + 1, label, a, runways) is false) return true;
        }
        return false;
    }

    // The segment behind the cursor whose stop, on node stopNodeId, still has a runway cleared at it; -1 for none.
    private static int PassedClearedSegment(TaxiRoute route, int cursor, int stopNodeId)
    {
        for (int i = Math.Min(cursor, route.Segments.Count) - 1; i >= 0; i--)
            if (route.Segments[i].ToNode?.NodeId == stopNodeId && route.Segments[i].ClearedRunways.Count > 0) return i;
        return -1;
    }

    private static bool GuardsRunway(string label, TaxiGraph.RunwayCenterline runway)
        => RouteRunwayCrossings.ExtractRunwayDesignators(label).Any(d => SameRunway(d, runway.Name1) || SameRunway(d, runway.Name2));

    // Along the route from the aircraft to the stop at the end of segment stopSegment, as the status readout measures:
    // straight to the end of the segment the aircraft is on, then the segments after it. No position yet: the segments.
    private static double MetresToStop(TaxiRoute route, int cursor, int stopSegment, HoldShortKeyAircraft? aircraft)
    {
        var segments = route.Segments;
        int c = Math.Clamp(Math.Min(cursor, stopSegment), 0, segments.Count - 1);
        double metres = aircraft is { } a && segments[c].ToNode is { } end
            ? TaxiGraph.FastDistanceMeters(a.Lat, a.Lon, end.Latitude, end.Longitude)
            : segments[c].DistanceMeters;
        for (int i = c + 1; i <= stopSegment && i < segments.Count; i++) metres += segments[i].DistanceMeters;
        return Math.Max(metres, 0.0);
    }

    private static double MetresToStart(TaxiRoute route, HoldShortKeyAircraft? aircraft)
        => aircraft is { } a && route.Segments.Count > 0 && route.Segments[0].FromNode is { } start
            ? TaxiGraph.FastDistanceMeters(a.Lat, a.Lon, start.Latitude, start.Longitude)
            : 0.0;

    // The stop's own label; the destination's name for an unlabelled last segment.
    private static string StopName(TaxiRoute route, int segmentIndex, string? destinationName)
    {
        string? label = route.Segments[segmentIndex].HoldShortRunway;
        if (!string.IsNullOrWhiteSpace(label)) return label.Trim();
        return segmentIndex == route.Segments.Count - 1 && !string.IsNullOrWhiteSpace(destinationName)
            ? destinationName.Trim()
            : "the hold line";
    }

    private static double SegmentLength(TaxiRouteSegment? segment)
        => segment?.FromNode == null || segment.ToNode == null
            ? 0.0
            : TaxiGraph.FastDistanceMeters(segment.FromNode.Latitude, segment.FromNode.Longitude,
                segment.ToNode.Latitude, segment.ToNode.Longitude);

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

    private static TaxiRouteRunwayEvent? FirstMatch(TaxiRoute route, IEnumerable<TaxiRouteRunwayEvent> crossings, ClearedCrossing request)
    {
        bool hasAt = !string.IsNullOrWhiteSpace(request.At);
        foreach (var e in crossings)
        {
            if (!hasAt && request.AtNodeId == 0) return e;
            if (hasAt && (SamePlace(e.Location, request.At!) || SamePlace(StopTaxiway(route, e.StopNodeIndex), request.At!)))
                return e;
            if (request.AtNodeId != 0 && OnApproach(route, e, request.AtNodeId)) return e;
        }
        return null;
    }

    // Whether node nodeId lies on the route from the event's stop to where the route reaches the runway, both included:
    // a holding point the route is pinned through can sit between the scenery line the pass stopped at and the runway.
    // A crossing with no stop (passed, or none placed) is matched by its place name only.
    private static bool OnApproach(TaxiRoute route, TaxiRouteRunwayEvent e, int nodeId)
    {
        if (e.StopNodeIndex < 0) return false;
        int last = Math.Max(e.StopNodeIndex, e.ReachNodeIndex);
        for (int k = e.StopNodeIndex; k <= last; k++)
        {
            var node = StopNode(route, k);
            if (node is null) return false;
            if (node.NodeId == nodeId) return true;
        }
        return false;
    }

    private static ClearedCrossing ClearMatch(TaxiRoute route, TaxiRouteRunwayEvent match, ClearedCrossing request)
    {
        int stopNodeId = StopNode(route, match.StopNodeIndex)?.NodeId ?? 0;
        int reachNodeId = StopNode(route, match.ReachNodeIndex)?.NodeId ?? 0;
        ClearAt(route, match.StopNodeIndex, match.Designator);
        return request with { StopNodeId = stopNodeId, ReachNodeId = reachNodeId };
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
    /// Whether <see cref="ClearAt"/> would clear <paramref name="runway"/> at a hold short labelled
    /// <paramref name="label"/>: the label names the runway (either end), and that part is not the pilot's own taxiway,
    /// holding-point or end-of-taxiway stop shared with it ("runway 28L at taxiway W"), which is never cleared. The
    /// paired-crossing offer asks it before it offers a stop, so a "cleared across both" press never meets a stop it
    /// cannot clear.
    /// </summary>
    public static bool CanClearLabel(string? label, string? runway)
        => !string.IsNullOrWhiteSpace(runway) && CanClear(label, runway);

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
    /// How a Continue that lets the aircraft go opens its sentence: "Continuing. ", then <paramref name="crossingLine"/>
    /// (<see cref="AtTheLine"/> for every runway at a released stop the pilot was cleared across for one of its
    /// runways) straight after it, in the order things happen, before the taxiway, the turn cue and any paired-Continue
    /// offer that follow. Exactly "Continuing. " when there is none.
    /// </summary>
    public static string Continuing(string? crossingLine)
        => string.IsNullOrWhiteSpace(crossingLine) ? "Continuing. " : $"Continuing. {crossingLine.Trim()} ";

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
