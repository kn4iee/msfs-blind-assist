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
    /// (<see cref="ClearedCrossing.AtNodeId"/>).
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
        var seqIndexOf = new int[steps.Count];

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.IsVia)
            {
                // The turn word is the pick of its OWN Via step, keyed by its place among the Via steps.
                if (step.Kind == ClearanceStepKind.LeftOnto) picks[sequence.Count] = TurnSide.Left;
                else if (step.Kind == ClearanceStepKind.RightOnto) picks[sequence.Count] = TurnSide.Right;
                sequence.Add(step.Name);
                seqIndexOf[i] = sequence.Count - 1;
                continue;
            }

            int seqIndex = sequence.Count - 1;
            seqIndexOf[i] = seqIndex;
            if (step.Kind != ClearanceStepKind.Cross) continue;
            if (step.TargetKind == ClearanceTargetKind.Taxiway)
                toCross.Add(step.Name);
            else if (step.TargetKind == ClearanceTargetKind.Runway)
                // Looked for from the taxiway it was entered after.
                crossings.Add(new ClearedCrossing(step.Name, step.At, step.AtNodeId, SeqIndex: seqIndex));
        }

        // How the leg ends.
        ClearanceEndKind end = ClearanceEndKind.EndOfTaxiway;
        int endIndex = -1;
        if (hasDestination)
        {
            end = ClearanceEndKind.Destination;
        }
        else if (steps.Count > 0)
        {
            int limit = -1;
            for (int i = 0; i < steps.Count && !steps[i].IsVia; i++)
            {
                if (steps[i].Kind == ClearanceStepKind.HoldShort) { limit = i; break; }
            }
            if (limit < 0 && steps[^1].Kind == ClearanceStepKind.HoldShort)
                limit = steps.Count - 1;

            var last = steps[^1];
            if (limit >= 0)
            {
                endIndex = limit;
                end = steps[limit].TargetKind switch
                {
                    ClearanceTargetKind.Runway => ClearanceEndKind.HoldShortRunway,
                    ClearanceTargetKind.Taxiway => ClearanceEndKind.HoldShortTaxiway,
                    _ => ClearanceEndKind.HoldAtNamedPoint,
                };
            }
            else if (last.Kind == ClearanceStepKind.Cross && last.TargetKind == ClearanceTargetKind.Runway)
            {
                endIndex = steps.Count - 1;
                end = ClearanceEndKind.AcrossRunway;
            }
            else if (last.IsVia)
            {
                endIndex = steps.Count - 1;
                end = ClearanceEndKind.EndOfTaxiway;
            }
            // Otherwise the last step is Cross taxiway: refused below, with no end step.
        }
        ClearanceStep? endStep = endIndex >= 0 ? steps[endIndex] : null;

        // The stops on the way.
        var holds = new List<ClearanceHold>();
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Kind != ClearanceStepKind.HoldShort || i == endIndex) continue;
            if (hasDestination && step.TargetKind == ClearanceTargetKind.Runway
                && !string.IsNullOrWhiteSpace(destinationRunway) && ClearanceRunways.Same(step.Name, destinationRunway))
                continue;   // "Hold short of" the departure runway, as the built-in ATC says it: already the end.
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

        // The leg ends across a runway at a holding point: pin the route through the point, as today's leg did.
        int? pinNodeId = null;
        string? pinName = null;
        if (end == ClearanceEndKind.AcrossRunway && endStep is { AtNodeId: not 0 } across)
        {
            pinNodeId = across.AtNodeId;
            pinName = across.At;
        }

        return new ClearancePlan(sequence, holds, crossings, picks, toCross, end, endStep,
                                 Refusal(steps, hasDestination, endIndex))
        {
            LegEndCrossedTaxiways = legEndCrossed,
            CrossAtHoldingPointNodeId = pinNodeId,
            CrossAtHoldingPointName = pinName,
        };
    }

    /// <summary>The first reason Calculate must refuse, in the brief's order; null when none applies.</summary>
    private static string? Refusal(IReadOnlyList<ClearanceStep> steps, bool hasDestination, int endIndex)
    {
        if (steps.Count == 0 && !hasDestination)
            return "Enter a step, or choose a destination.";

        if (!hasDestination && steps.Count > 0
            && steps[^1] is { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Taxiway } lastCross)
            return $"A leg cannot end on cross taxiway {lastCross.Name}. Add where to hold short, or the next taxiway.";

        foreach (var cross in steps)
        {
            if (cross is not { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway }) continue;
            foreach (var hold in steps)
            {
                if (hold is { Kind: ClearanceStepKind.HoldShort, TargetKind: ClearanceTargetKind.Runway }
                    && ClearanceRunways.Same(cross.Name, hold.Name))
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
/// The step list trimmed to what is still ahead while a route is active (spec 8.2): finished Via steps go, runway
/// steps whose runway is behind go, a crossing put back becomes a hold short, and a turn already made reads as Via
/// so a later Calculate never judges it again from the aircraft's heading ([TURN-3]).
/// </summary>
public static class ClearanceTrim
{
    /// <summary>
    /// The steps still ahead. The first <paramref name="viaStepsDone"/> Via-kind steps are dropped with every step
    /// before the last of them. Then, when <paramref name="turnOntoFirstRemainingMade"/>, the first remaining Via-kind
    /// step, if a Left onto or Right onto, becomes a Via step for the same taxiway (a turn ahead, or one the aircraft
    /// is in the middle of, stays a turn). Of what remains, runways compared after
    /// <see cref="RouteRunwayCrossings.NormalizeDesignator"/>, either end: a Cross runway step whose runway is not in
    /// <paramref name="runwaysAhead"/> is dropped (crossed); one ahead but not in <paramref name="clearedAhead"/>
    /// becomes a Hold short step for that runway (its stop was put back); a Hold short runway step whose runway is
    /// not ahead is dropped. Every other step stays.
    /// </summary>
    public static List<ClearanceStep> Remaining(IReadOnlyList<ClearanceStep> steps, int viaStepsDone,
        IReadOnlySet<string> runwaysAhead, IReadOnlySet<string> clearedAhead, bool turnOntoFirstRemainingMade)
    {
        var result = new List<ClearanceStep>();
        if (steps == null) return result;

        int start = 0, dropped = 0;
        for (int i = 0; i < steps.Count && dropped < viaStepsDone; i++)
        {
            if (!steps[i].IsVia) continue;
            dropped++;
            start = i + 1;
        }

        var rest = new List<ClearanceStep>();
        for (int i = start; i < steps.Count; i++) rest.Add(steps[i]);

        if (turnOntoFirstRemainingMade)
        {
            int firstVia = rest.FindIndex(s => s.IsVia);
            if (firstVia >= 0 && rest[firstVia].Kind is ClearanceStepKind.LeftOnto or ClearanceStepKind.RightOnto)
                rest[firstVia] = rest[firstVia] with { Kind = ClearanceStepKind.Via };
        }

        var ahead = ClearanceRunways.NormalizedSet(runwaysAhead);
        var cleared = ClearanceRunways.NormalizedSet(clearedAhead);
        foreach (var step in rest)
        {
            if (step.TargetKind == ClearanceTargetKind.Runway && step.Kind == ClearanceStepKind.Cross)
            {
                if (!ClearanceRunways.In(ahead, step.Name)) continue;
                if (!ClearanceRunways.In(cleared, step.Name))
                {
                    result.Add(step with { Kind = ClearanceStepKind.HoldShort, At = null, AtNodeId = 0 });
                    continue;
                }
            }
            else if (step.TargetKind == ClearanceTargetKind.Runway && step.Kind == ClearanceStepKind.HoldShort
                     && !ClearanceRunways.In(ahead, step.Name))
            {
                continue;
            }
            result.Add(step);
        }
        return result;
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

    public static HashSet<string> NormalizedSet(IReadOnlySet<string>? names)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (names == null) return set;
        foreach (string name in names)
        {
            string n = Normalize(name);
            if (n.Length > 0) set.Add(n);
        }
        return set;
    }

    /// <summary>True when the runway <paramref name="name"/> (either end) is in a <see cref="NormalizedSet"/>.</summary>
    public static bool In(HashSet<string> normalized, string? name)
    {
        string n = Normalize(name);
        if (n.Length == 0) return false;
        return normalized.Contains(n) || normalized.Contains(Normalize(RouteRunwayCrossings.Reciprocal(n)));
    }
}
