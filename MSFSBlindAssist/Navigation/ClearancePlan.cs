// A clearance (an optional destination plus the steps ATC said, in the order said) turned into what taxi
// guidance already takes: the taxiway sequence, the stops on the way, the runway crossings the pilot is
// cleared for, the Turn picks and how a leg with no destination ends. Also the checks on the route built
// from it, and the list trimmed to what is still ahead. Pure: no graph, no manager, no form.
// Design: docs/superpowers/specs/2026-11-14-taxi-clearance-steps-design.md, sections 8.1 and 8.2.

using MSFSBlindAssist.Database.Models;   // TurnSide, TaxiRouteSegment

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// How a clearance ends: at its destination, or, with none, as its steps say (the limit said first, else the
/// last step): at a hold short of a runway, of a taxiway or at a named holding point, just across a runway, or
/// at the end of the last taxiway.
/// </summary>
public enum ClearanceEndKind { Destination, HoldShortRunway, HoldShortTaxiway, HoldAtNamedPoint, AcrossRunway, EndOfTaxiway }

/// <summary>
/// A Hold short step that is a stop on the way. <paramref name="SeqIndex"/> is the taxiway (Via-kind) step it was
/// entered after, its index in <see cref="ClearancePlan.TaxiwaySequence"/>, so it is looked for from that taxiway
/// on; -1 when none comes before it, and the whole route is searched.
/// </summary>
public sealed record ClearanceHold(int SeqIndex, ClearanceStep Step);

/// <summary>
/// What <see cref="ClearancePlanner.Build"/> makes of a clearance: everything <c>LoadRoute</c> takes.
/// <list type="bullet">
/// <item><see cref="TaxiwaySequence"/>: the Via, Left onto and Right onto steps' taxiways, in order; one used twice is
/// kept twice ([RTE-10], [RTE-33]).</item>
/// <item><see cref="Holds"/>: the stops on the way (a destination runway held short of is not one: guidance already
/// holds there; nor is the step that ends a leg with no destination, <see cref="EndStep"/>).</item>
/// <item><see cref="Crossings"/>: one <see cref="ClearedCrossing"/> per Cross runway step, the only thing besides
/// Continue that clears a runway ([CLR-1]).</item>
/// <item><see cref="TurnPicks"/>: PR #355's picks, keyed by the Via step's index in <see cref="TaxiwaySequence"/>;
/// they reach guidance only as <c>LoadRoute</c>'s <c>userTurnPicks</c> ([TURN-1]).</item>
/// <item><see cref="TaxiwaysToCross"/>: every Cross taxiway step, in step order, for
/// <see cref="ClearanceRouteChecks.Warnings"/>.</item>
/// <item><see cref="End"/> and <see cref="EndStep"/>: how the leg ends, and the step that ends it (null with a
/// destination).</item>
/// <item><see cref="Refusal"/>: why Calculate must not route this, spoken as it stands; null when it may.</item>
/// </list>
/// </summary>
public sealed record ClearancePlan(
    IReadOnlyList<string> TaxiwaySequence, IReadOnlyList<ClearanceHold> Holds,
    IReadOnlyList<ClearedCrossing> Crossings, IReadOnlyDictionary<int, TurnSide> TurnPicks,
    IReadOnlyList<string> TaxiwaysToCross, ClearanceEndKind End, ClearanceStep? EndStep, string? Refusal)
{
    /// <summary>
    /// The Cross taxiway steps entered after the last Via-kind step (all of them when there is none), in list
    /// order, when the leg ends at a hold short of a taxiway (<see cref="ClearanceEndKind.HoldShortTaxiway"/>);
    /// empty for every other end. This is the Cross taxiway tie-breaker (spec 8.1: "via A, cross B, hold short of
    /// C" is the C beyond B): the form finds where the last Via taxiway meets each of them and keeps only the
    /// leg-end meetings beyond that crossing (<see cref="ClearanceRouteChecks.BeyondTheCrossing"/>). LoadRoute
    /// must be given those crossing nodes too, or the manager's leg-end meet scan (<c>ApplyLegEndSetback</c>) and
    /// a Turn pick's end re-resolution (<c>TurnPickApplier.ResolveProgressiveEnd</c>), which both take the FIRST
    /// meeting along the route, cut the leg back to a meeting before the crossed taxiway.
    /// </summary>
    public IReadOnlyList<string> LegEndCrossedTaxiways { get; init; } = Array.Empty<string>();

    /// <summary>
    /// For a leg with no destination that ends just across a runway at a named holding point ("Cross runway 24L
    /// at B3", the last step, its "at" a holding point: <see cref="ClearanceStep.AtNodeId"/> set): the point's
    /// node, which <c>LoadRoute</c> takes as <c>holdingPointHoldNodeId</c> and pins the route through, as the
    /// "After crossing runway" leg did, so the pilot crosses where ATC said. Null otherwise: anywhere else an "at"
    /// does not bend the route (spec 8.1), and its crossing is cleared by its node alone
    /// (<see cref="ClearedCrossing.AtNodeId"/>); and null for a crossing already made, whose node is its stop behind
    /// the aircraft (<see cref="ClearanceStep.AtNodeIsPassedStop"/>).
    /// </summary>
    public int? CrossAtHoldingPointNodeId { get; init; }

    /// <summary>
    /// The holding point's name for <see cref="CrossAtHoldingPointNodeId"/> (the step's <see cref="ClearanceStep.At"/>),
    /// which <c>LoadRoute</c> takes as <c>holdingPointName</c>. Null when that is null.
    /// </summary>
    public string? CrossAtHoldingPointName { get; init; }
}

/// <summary>Turns the clearance steps into a <see cref="ClearancePlan"/>. Spec 8.1.</summary>
public static class ClearancePlanner
{
    /// <summary>
    /// Builds the plan for <paramref name="steps"/>, in the order entered. <paramref name="hasDestination"/>: the
    /// clearance has a destination (anything but Progressive Taxi). <paramref name="destinationRunway"/>: that
    /// destination's runway when it is one, whose hold short adds nothing (guidance already holds there).
    ///
    /// <para>With no destination the limit is the first Hold short step entered before any Via-kind step ("taxi
    /// to holding point C5 via A"), else the last step when it is a Hold short; with no limit, the last step ends
    /// the leg: across a runway, or at the end of a taxiway. A Hold short step that is not the limit is a stop.</para>
    /// </summary>
    public static ClearancePlan Build(IReadOnlyList<ClearanceStep> steps, bool hasDestination, string? destinationRunway = null)
    {
        steps ??= Array.Empty<ClearanceStep>();

        var sequence = new List<string>();
        var picks = new Dictionary<int, TurnSide>();
        var crossings = new List<ClearedCrossing>();
        var toCross = new List<string>();
        // Per step: the index in the sequence of the last Via-kind step at or before it (-1: none).
        int[] seqIndexOf = SeqIndices(steps);

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.IsVia)
            {
                // The turn word is the pick of its OWN Via step, keyed by its place among the Via steps.
                if (step.Kind == ClearanceStepKind.LeftOnto) picks[sequence.Count] = TurnSide.Left;
                else if (step.Kind == ClearanceStepKind.RightOnto) picks[sequence.Count] = TurnSide.Right;
                sequence.Add(step.Name);
                continue;
            }

            if (step.Kind != ClearanceStepKind.Cross) continue;
            if (step.TargetKind == ClearanceTargetKind.Taxiway)
                toCross.Add(step.Name);
            else if (step.TargetKind == ClearanceTargetKind.Runway)
                // Looked for from the taxiway it was entered after.
                crossings.Add(new ClearedCrossing(step.Name, step.At, step.AtNodeId, SeqIndex: seqIndexOf[i]));
        }

        // How the leg ends.
        int endIndex = EndIndex(steps, hasDestination);
        ClearanceStep? endStep = endIndex >= 0 ? steps[endIndex] : null;
        ClearanceEndKind end = hasDestination ? ClearanceEndKind.Destination : endStep switch
        {
            { Kind: ClearanceStepKind.HoldShort, TargetKind: ClearanceTargetKind.Runway } => ClearanceEndKind.HoldShortRunway,
            { Kind: ClearanceStepKind.HoldShort, TargetKind: ClearanceTargetKind.Taxiway } => ClearanceEndKind.HoldShortTaxiway,
            { Kind: ClearanceStepKind.HoldShort } => ClearanceEndKind.HoldAtNamedPoint,
            { Kind: ClearanceStepKind.Cross } => ClearanceEndKind.AcrossRunway,
            // A Via-kind last step; or no end at all (no steps, or a last step Cross taxiway), refused below.
            _ => ClearanceEndKind.EndOfTaxiway,
        };

        // The stops on the way.
        var holds = new List<ClearanceHold>();
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Kind != ClearanceStepKind.HoldShort || i == endIndex) continue;
            // "Hold short of" the departure runway, as the built-in ATC says it: already the end, adds nothing.
            if (IsDestinationRunwayHold(step, hasDestination, destinationRunway)) continue;
            holds.Add(new ClearanceHold(seqIndexOf[i], step));
        }

        // The Cross taxiway tie-breaker's taxiways: those crossed after the last Via step, for a taxiway leg end.
        var legEndCrossed = new List<string>();
        if (end == ClearanceEndKind.HoldShortTaxiway)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step.Kind == ClearanceStepKind.Cross && step.TargetKind == ClearanceTargetKind.Taxiway
                    && seqIndexOf[i] == sequence.Count - 1)
                    legEndCrossed.Add(step.Name);
            }
        }

        // The leg ends across a runway at a holding point: pin the route through the point, as today's leg did. Never
        // through the stop node of a crossing already made (ClearanceTrim's pin): it is behind the aircraft.
        int? pinNodeId = null;
        string? pinName = null;
        if (end == ClearanceEndKind.AcrossRunway && endStep is { AtNodeId: not 0, AtNodeIsPassedStop: false } across)
        {
            pinNodeId = across.AtNodeId;
            pinName = across.At;
        }

        return new ClearancePlan(sequence, holds, crossings, picks, toCross, end, endStep,
                                 Refusal(steps, hasDestination, destinationRunway, endIndex))
        {
            LegEndCrossedTaxiways = legEndCrossed,
            CrossAtHoldingPointNodeId = pinNodeId,
            CrossAtHoldingPointName = pinName,
        };
    }

    /// <summary>
    /// Per step, the index in the taxiway sequence of the last Via-kind step at or before it: a Via-kind step's own
    /// index, any other step's <see cref="ClearanceHold.SeqIndex"/> (-1 when no Via-kind step comes before it).
    /// </summary>
    internal static int[] SeqIndices(IReadOnlyList<ClearanceStep> steps)
    {
        var seqIndexOf = new int[steps.Count];
        int seq = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].IsVia) seq++;
            seqIndexOf[i] = seq;
        }
        return seqIndexOf;
    }

    /// <summary>
    /// The index of the step that ends a leg with no destination: the limit, the first Hold short step entered
    /// before any Via-kind step; else the last step, when it is a Hold short, a Cross runway or a Via-kind step.
    /// -1 with a destination, with no steps, or when the last step is Cross taxiway (a leg with no end).
    /// </summary>
    internal static int EndIndex(IReadOnlyList<ClearanceStep> steps, bool hasDestination)
    {
        if (hasDestination || steps.Count == 0) return -1;
        for (int i = 0; i < steps.Count && !steps[i].IsVia; i++)
        {
            if (steps[i].Kind == ClearanceStepKind.HoldShort) return i;
        }
        var last = steps[^1];
        bool ends = last.Kind == ClearanceStepKind.HoldShort || last.IsVia
                    || last is { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway };
        return ends ? steps.Count - 1 : -1;
    }

    /// <summary>A Hold short step naming the destination runway (either end): accepted, and adds nothing.</summary>
    private static bool IsDestinationRunwayHold(ClearanceStep step, bool hasDestination, string? destinationRunway)
        => hasDestination && step is { Kind: ClearanceStepKind.HoldShort, TargetKind: ClearanceTargetKind.Runway }
           && !string.IsNullOrWhiteSpace(destinationRunway) && ClearanceRunways.Same(step.Name, destinationRunway);

    /// <summary>The first reason Calculate must refuse, in the brief's order; null when none applies.</summary>
    private static string? Refusal(IReadOnlyList<ClearanceStep> steps, bool hasDestination, string? destinationRunway,
                                   int endIndex)
    {
        if (steps.Count == 0 && !hasDestination)
            return "Enter a step, or choose a destination.";

        // A leg with no destination and no end: its last step is Cross taxiway and no limit was said first.
        if (!hasDestination && endIndex < 0)
            return $"A leg cannot end on cross taxiway {steps[^1].Name}. Add where to hold short, or the next taxiway.";

        // The same crossing entered as both: the same runway (either end), and the same place when both name one, or,
        // when either names none, entered after the same Via step, counting a step before any Via step as after the
        // first (searched over the whole route, it finds what a step searched from the first Via step finds). A Hold
        // short of the destination runway is accepted and adds nothing, so it is never a second crossing.
        int[] seqIndexOf = SeqIndices(steps);
        for (int c = 0; c < steps.Count; c++)
        {
            var cross = steps[c];
            if (cross is not { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway }) continue;
            for (int h = 0; h < steps.Count; h++)
            {
                var hold = steps[h];
                if (hold is not { Kind: ClearanceStepKind.HoldShort, TargetKind: ClearanceTargetKind.Runway }
                    || IsDestinationRunwayHold(hold, hasDestination, destinationRunway)
                    || !ClearanceRunways.Same(cross.Name, hold.Name))
                    continue;
                bool sameCrossing = ClearanceRunways.HasPlace(cross) && ClearanceRunways.HasPlace(hold)
                    ? ClearanceRunways.SamePlace(cross, hold)
                    : Math.Max(seqIndexOf[c], 0) == Math.Max(seqIndexOf[h], 0);
                if (sameCrossing)
                    return $"Runway {cross.Name} is entered both as a crossing and as a hold short. Remove one.";
            }
        }

        // [HLD-18]: a holding point read from a sign may only end a leg with no destination.
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].FromSign && i != endIndex)
                return $"{steps[i].Name} is read from a sign and can only end a leg.";
        }
        return null;
    }
}

/// <summary>Checks on the route built from a plan, spoken with the Calculate sentence (warnings first).</summary>
public static class ClearanceRouteChecks
{
    /// <summary>
    /// A Cross taxiway step is a check: the route meets the taxiway when a segment is on it (any case) or ends at
    /// a node that touches it (<paramref name="nodeTouchesTaxiway"/>, node id and name). One the route never meets
    /// is said once: "The route does not cross taxiway B." The route is never bent for it.
    /// </summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<TaxiRouteSegment> segments, ClearancePlan plan,
                                                 Func<int, string, bool> nodeTouchesTaxiway)
    {
        var warnings = new List<string>();
        var checkedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in plan.TaxiwaysToCross)
        {
            if (!checkedNames.Add(name)) continue;
            if (!RouteMeets(segments, name, nodeTouchesTaxiway))
                warnings.Add("The route does not cross taxiway " + name + ".");
        }
        return warnings;
    }

    private static bool RouteMeets(IReadOnlyList<TaxiRouteSegment> segments, string taxiway,
                                   Func<int, string, bool> nodeTouchesTaxiway)
    {
        if (segments == null) return false;
        foreach (var seg in segments)
        {
            if (seg == null) continue;
            if (string.Equals(seg.TaxiwayName, taxiway, StringComparison.OrdinalIgnoreCase)) return true;
            if (seg.ToNode != null && nodeTouchesTaxiway(seg.ToNode.NodeId, taxiway)) return true;
        }
        return false;
    }

    /// <summary>
    /// The Cross taxiway tie-breaker: of the leg-end <paramref name="candidates"/>, those lying beyond the nearest
    /// crossing. d0 is the smallest <paramref name="taxiMetresFromStart"/> of the <paramref name="crossingNodeIds"/>
    /// present in it; the result is the candidates present whose distance is greater than d0, in their given order.
    /// When no crossing node has a distance, or no candidate lies beyond d0, <paramref name="candidates"/> is
    /// returned unchanged: the tie-breaker never leaves a leg with no end.
    /// </summary>
    public static IReadOnlyList<int> BeyondTheCrossing(IReadOnlyList<int> candidates,
        IReadOnlyDictionary<int, double> taxiMetresFromStart, IReadOnlyList<int> crossingNodeIds)
    {
        double? d0 = null;
        foreach (int node in crossingNodeIds)
        {
            if (taxiMetresFromStart.TryGetValue(node, out double d) && (d0 == null || d < d0))
                d0 = d;
        }
        if (d0 == null) return candidates;

        var beyond = new List<int>();
        foreach (int node in candidates)
        {
            if (taxiMetresFromStart.TryGetValue(node, out double d) && d > d0)
                beyond.Add(node);
        }
        return beyond.Count > 0 ? beyond : candidates;
    }
}

/// <summary>
/// The step list trimmed to what is still ahead while a route is active (spec 8.2: only completed steps are dropped;
/// a step whose state cannot be known stays). Finished Via steps go; a Cross runway or Hold short step goes only when
/// guidance's records say it is behind; a crossing put back becomes a hold short at its place; a turn already made
/// reads as Via, so a later Calculate never judges it again from the aircraft's heading ([TURN-3]). Judged from
/// RECORDS, never runway names: one runway can be cleared at two places, or crossed twice.
/// </summary>
public static class ClearanceTrim
{
    /// <summary>
    /// The steps still ahead, from <paramref name="steps"/> as last calculated (the numbering the records carry).
    /// <list type="bullet">
    /// <item>The first <paramref name="viaStepsDone"/> Via-kind steps are dropped; then, when
    /// <paramref name="turnOntoFirstRemainingMade"/>, the first remaining Via-kind step, if a Left onto or Right onto,
    /// becomes a Via step for the same taxiway (a turn ahead, or one the aircraft is in the middle of, stays a turn).</item>
    /// <item>A Cross runway step is matched to ONE record by runway (either end), by its own Via-step index (the
    /// record's <see cref="ClearedCrossing.SeqIndex"/>) and, where both have one, its place (<c>At</c>, <c>AtNodeId</c>);
    /// every step first takes a record at exactly its place, then one matching only through a missing place, each
    /// pass in route order (<paramref name="clearedUsed"/>, <paramref name="putBack"/>, <paramref name="clearedInForce"/>).
    /// Used, dropped (its crossing is behind); put back, it becomes "Hold short of runway R" at the same place; in
    /// force, kept. A record a Continue cleared (<see cref="ClearedBy.Continue"/>) never matches.</item>
    /// <item>A Hold short step is dropped only when matched, the same way, to one of <paramref name="holdsPassed"/> (the
    /// same target, Via-step index and place).</item>
    /// <item>Every other step stays, and so does any step with no match: its state is unknown.</item>
    /// <item>The step that ends a leg with no destination (<paramref name="hasDestination"/> false), a limit said
    /// first included, is never dropped. A used crossing there stays, pinned to the crossing made
    /// (<see cref="ClearanceStep.AtNodeIsPassedStop"/>); a put-back crossing there still becomes its hold short.</item>
    /// <item>On a leg with no destination trimming never makes a step the limit: a kept stop or crossing that would
    /// land in front of the first remaining Via step is moved just after it, or, when that Via step ends the leg or
    /// none remains, the last finished Via step is kept in front of it.</item>
    /// </list>
    /// </summary>
    public static List<ClearanceStep> Remaining(IReadOnlyList<ClearanceStep> steps, int viaStepsDone, bool hasDestination,
        // Tasks 3 and 7 supply these four lists from guidance (cleared crossings in force, used at their line, put back
        // by the Hold short key, and the pilot's own stops already passed), numbered as the plan they were loaded from.
        IReadOnlyList<ClearedCrossing> clearedInForce, IReadOnlyList<ClearedCrossing> clearedUsed,
        IReadOnlyList<ClearedCrossing> putBack, IReadOnlyList<ClearanceHold> holdsPassed,
        bool turnOntoFirstRemainingMade)
    {
        if (steps == null) return new List<ClearanceStep>();

        int[] seqIndexOf = ClearancePlanner.SeqIndices(steps);
        int endIndex = ClearancePlanner.EndIndex(steps, hasDestination);
        var crossings = MatchCrossings(steps, seqIndexOf, clearedUsed, putBack, clearedInForce);
        bool[] holdPassed = MatchPassedHolds(steps, seqIndexOf, holdsPassed);

        // Each kept step with its index in steps.
        var kept = new List<(ClearanceStep Step, int Index)>();
        int viaDropped = 0, lastFinishedVia = -1;
        bool firstViaSeen = false;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            bool isEnd = i == endIndex;

            if (step.IsVia)
            {
                if (viaDropped < viaStepsDone)
                {
                    viaDropped++;
                    if (!isEnd)
                    {
                        lastFinishedVia = i;
                        continue;
                    }
                }
                if (!firstViaSeen && turnOntoFirstRemainingMade
                    && step.Kind is ClearanceStepKind.LeftOnto or ClearanceStepKind.RightOnto)
                    step = step with { Kind = ClearanceStepKind.Via };
                firstViaSeen = true;
                kept.Add((step, i));
                continue;
            }

            var (state, record) = crossings[i];
            if (state == CrossingState.Used)
            {
                // Behind; but the leg ends just across it, so it still says where the leg ends.
                if (isEnd) kept.Add((PinnedToMadeCrossing(step, record!), i));
                continue;
            }
            if (state == CrossingState.PutBack)
            {
                kept.Add((step with { Kind = ClearanceStepKind.HoldShort }, i));   // keeps its At, AtNodeId
                continue;
            }
            if (holdPassed[i] && !isEnd) continue;
            kept.Add((step, i));
        }

        if (!hasDestination)
            NeverMakeALimit(kept, steps, seqIndexOf, endIndex, lastFinishedVia);
        return kept.Select(k => k.Step).ToList();
    }

    private enum CrossingState { Unknown, Used, PutBack, InForce }

    // What each Cross runway step is matched to. Exact places first, for every step, then places matched only through
    // a missing one: first-fit on a wildcard could hand a plain step the record of a step "at B4" and swap the two.
    private static (CrossingState State, ClearedCrossing? Record)[] MatchCrossings(IReadOnlyList<ClearanceStep> steps,
        int[] seqIndexOf, IReadOnlyList<ClearedCrossing> used, IReadOnlyList<ClearedCrossing> putBack,
        IReadOnlyList<ClearedCrossing> inForce)
    {
        var result = new (CrossingState State, ClearedCrossing? Record)[steps.Count];
        var lists = new[]
        {
            (State: CrossingState.Used, Records: new Records<ClearedCrossing>(used)),
            (State: CrossingState.PutBack, Records: new Records<ClearedCrossing>(putBack)),
            (State: CrossingState.InForce, Records: new Records<ClearedCrossing>(inForce)),
        };
        foreach (bool exact in new[] { true, false })
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (result[i].State != CrossingState.Unknown
                    || step is not { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway })
                    continue;
                int seq = seqIndexOf[i];
                foreach (var (state, records) in lists)
                {
                    var record = records.Take(r => CrossingMatches(r, step, seq)
                        && (!exact || ClearanceRunways.ExactPlace(r.At, r.AtNodeId, step.At, step.AtNodeId)));
                    if (record == null) continue;
                    result[i] = (state, record);
                    break;
                }
            }
        }
        return result;
    }

    // Which Hold short steps are known passed, matched as the crossings are: exact places first.
    private static bool[] MatchPassedHolds(IReadOnlyList<ClearanceStep> steps, int[] seqIndexOf,
        IReadOnlyList<ClearanceHold> holdsPassed)
    {
        var result = new bool[steps.Count];
        var records = new Records<ClearanceHold>(holdsPassed);
        foreach (bool exact in new[] { true, false })
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (result[i] || step.Kind != ClearanceStepKind.HoldShort) continue;
                int seq = seqIndexOf[i];
                result[i] = records.Take(h => h.SeqIndex == seq && SameTarget(h.Step, step)
                    && (!exact || ClearanceRunways.ExactPlace(h.Step.At, h.Step.AtNodeId, step.At, step.AtNodeId))) != null;
            }
        }
        return result;
    }

    // The end of a leg already crossed: kept, pinned to the crossing made (its stop node, and its place name when the
    // record has one), so a fresh Calculate clears only that crossing, never a later crossing of the same runway the
    // new route makes. A crossing recorded with no stop node has nothing to pin and stays as entered.
    private static ClearanceStep PinnedToMadeCrossing(ClearanceStep step, ClearedCrossing used)
    {
        if (used.StopNodeId == 0) return step;
        return step with
        {
            AtNodeId = used.StopNodeId,
            At = string.IsNullOrWhiteSpace(used.At) ? step.At : used.At,
            AtNodeIsPassedStop = true,
        };
    }

    // Trimming never makes a step the limit (a Hold short said before any Via step ends a leg with no destination). A
    // kept stop or runway crossing entered after a Via step now behind would land in front of the first remaining Via
    // step: it moves to just after it (steps are placed by the route, spec 8.1, so it keeps its meaning). When that
    // Via step ends the leg (a stop after it would end the leg instead), or none remains, the last finished Via step is
    // kept in front of them, read as Via (the aircraft has left it); LoadRoute's route-start rules leave out a first
    // taxiway the aircraft has left ([RTE-39]). A crossing alone is left where it is then: it is never read as a limit,
    // and moving it after the last step would end the leg across it. Cross taxiway steps never move: after the last
    // Via step a crossed taxiway breaks the leg end's tie, which it was not entered to do.
    private static void NeverMakeALimit(List<(ClearanceStep Step, int Index)> kept, IReadOnlyList<ClearanceStep> steps,
        int[] seqIndexOf, int endIndex, int lastFinishedVia)
    {
        int firstVia = kept.FindIndex(k => k.Step.IsVia);
        int bound = firstVia >= 0 ? firstVia : kept.Count;
        var strays = new List<int>();
        for (int p = 0; p < bound; p++)
        {
            var (step, index) = kept[p];
            if (index != endIndex && seqIndexOf[index] >= 0
                && (step.Kind == ClearanceStepKind.HoldShort
                    || step is { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway }))
                strays.Add(p);
        }
        if (strays.Count == 0) return;

        if (firstVia >= 0 && kept[firstVia].Index != endIndex)
        {
            var moved = strays.Select(p => kept[p]).ToList();
            for (int s = strays.Count - 1; s >= 0; s--) kept.RemoveAt(strays[s]);
            kept.InsertRange(firstVia - strays.Count + 1, moved);
            return;
        }

        if (lastFinishedVia < 0 || !strays.Any(p => kept[p].Step.Kind == ClearanceStepKind.HoldShort)) return;
        var via = steps[lastFinishedVia];
        if (via.Kind != ClearanceStepKind.Via) via = via with { Kind = ClearanceStepKind.Via };
        kept.Insert(strays[0], (via, lastFinishedVia));
    }

    // A clearance record for this Cross runway step: the same runway (either end), entered after the same Via step,
    // at the same place where both name one. Continue-cleared records stand for no step.
    private static bool CrossingMatches(ClearedCrossing record, ClearanceStep step, int seqIndex)
        => record.Origin == ClearedBy.Clearance && record.SeqIndex == seqIndex
           && ClearanceRunways.Same(record.Runway, step.Name)
           && ClearanceRunways.SamePlace(record.At, record.AtNodeId, step.At, step.AtNodeId);

    // The same Hold short target: kind, name (a runway by either end), a holding point's node, and place.
    private static bool SameTarget(ClearanceStep a, ClearanceStep b)
    {
        if (a.Kind != ClearanceStepKind.HoldShort || a.TargetKind != b.TargetKind) return false;
        bool sameName = a.TargetKind == ClearanceTargetKind.Runway
            ? ClearanceRunways.Same(a.Name, b.Name)
            : string.Equals(a.Name.Trim(), b.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!sameName) return false;
        if (a.NodeId != 0 && b.NodeId != 0 && a.NodeId != b.NodeId) return false;
        return ClearanceRunways.SamePlace(a, b);
    }

    // A list of records each step may claim once, in order.
    private sealed class Records<T> where T : class
    {
        private readonly IReadOnlyList<T> _items;
        private readonly bool[] _taken;

        public Records(IReadOnlyList<T>? items)
        {
            _items = items ?? Array.Empty<T>();
            _taken = new bool[_items.Count];
        }

        /// <summary>Claims the first unclaimed record that matches; null when none does.</summary>
        public T? Take(Func<T, bool> matches)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_taken[i] || !matches(_items[i])) continue;
                _taken[i] = true;
                return _items[i];
            }
            return null;
        }
    }

    /// <summary>
    /// How many Via-kind steps are behind: <c>originalSequence.Count - remainingSequence.Count</c>. 0 when
    /// <paramref name="remainingSequence"/> is null or empty, longer than the original, or when its first taxiway
    /// also appears (any case) in the original below that count: which run of a repeated taxiway ("A, C, A") the
    /// aircraft is on cannot be told from its position ([RTE-33]), so nothing is trimmed.
    /// </summary>
    public static int ViaStepsDone(IReadOnlyList<string> originalSequence, IReadOnlyList<string>? remainingSequence)
    {
        if (originalSequence == null || remainingSequence == null || remainingSequence.Count == 0
            || remainingSequence.Count > originalSequence.Count)
            return 0;
        int done = originalSequence.Count - remainingSequence.Count;
        for (int i = 0; i < done; i++)
        {
            if (string.Equals(originalSequence[i], remainingSequence[0], StringComparison.OrdinalIgnoreCase))
                return 0;
        }
        return done;
    }
}

/// <summary>Runway designator comparisons for the step list: normalized, either end of the runway.</summary>
internal static class ClearanceRunways
{
    private static string Normalize(string? name)
        => RouteRunwayCrossings.NormalizeDesignator(RouteRunwayCrossings.StripRunwayPrefix(name));

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> name the same runway, by either end.</summary>
    public static bool Same(string? a, string? b)
    {
        string na = Normalize(a), nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        return na == nb || Normalize(RouteRunwayCrossings.Reciprocal(na)) == nb;
    }

    /// <summary>
    /// True unless the two places are known to differ: compared only where both have one, the node
    /// (<c>AtNodeId</c>, a holding point) and the name (<c>At</c>, any case). No place matches any place.
    /// </summary>
    public static bool SamePlace(string? atA, int atNodeA, string? atB, int atNodeB)
    {
        if (atNodeA != 0 && atNodeB != 0 && atNodeA != atNodeB) return false;
        if (!string.IsNullOrWhiteSpace(atA) && !string.IsNullOrWhiteSpace(atB)
            && !string.Equals(atA.Trim(), atB.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary><see cref="SamePlace(string?, int, string?, int)"/> for two steps' "at".</summary>
    public static bool SamePlace(ClearanceStep a, ClearanceStep b) => SamePlace(a.At, a.AtNodeId, b.At, b.AtNodeId);

    /// <summary>True when the step names a place: an "at" name or node.</summary>
    public static bool HasPlace(ClearanceStep step) => !string.IsNullOrWhiteSpace(step.At) || step.AtNodeId != 0;

    /// <summary>
    /// True when both places are the same exactly: the same name (any case; none and none alike) and the same node
    /// (0 and 0 alike). A record that matches only through a missing place is a wildcard match, taken after these.
    /// </summary>
    public static bool ExactPlace(string? atA, int atNodeA, string? atB, int atNodeB)
        => atNodeA == atNodeB
           && string.Equals((atA ?? "").Trim(), (atB ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

}
