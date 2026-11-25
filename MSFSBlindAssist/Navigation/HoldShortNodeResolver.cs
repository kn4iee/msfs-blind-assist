using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Resolves the graph node where a Progressive Taxi "Hold short of runway"
/// terminator should end the route: the hold point on the aircraft's own (near)
/// side of the runway.
///
/// The scenery's own hold-short markers (navdata HS/HSND/IHS/IHSND →
/// TaxiNodeType.HoldShort / ILSHoldShort) are authoritative for where the hold
/// line actually is. Motivating incident (KSFO 2026-07-01, "Q, hold short
/// 10R"): taxiway Q carries two HSND nodes on the 28L approach, ~97 m and
/// ~157 m from the centerline; the purely geometric scan picked the 157 m node
/// and the pilot held ~60 m before the real hold line.
///
/// Candidates are ranked in TIERS so a designated node can never hijack the
/// route away from the cleared taxiway (the 2026-07 review fix — a blanket
/// "designated first" let an HSND kilometres down the runway on a DIFFERENT
/// taxiway beat a plain node on the cleared one):
///
///   1. Designated node ON the cleared last taxiway (closest to the runway —
///      the same convention TruncateToHoldShort uses for runway destinations).
///   2. Designated node at the cleared taxiway's runway junction (within
///      HS_JUNCTION_ALONG_M along-track of the taxiway's own closest plain
///      candidate AND graph-connected to the cleared taxiway within
///      HS_STUB_MAX_HOPS edges) — covers the common scenery shape where the
///      final approach stub from taxiway to hold line is an unnamed connector,
///      so the HSND doesn't carry the taxiway name; the connectivity gate
///      keeps a parallel neighbour connector's hold line from hijacking the
///      route sideways (2026-07 review, confirmed at ladder junctions).
///   3. Plain node on the cleared taxiway — the legacy geometric pick, with
///      its deliberate setback: Runway.Width is in FEET, and dividing by 2 and
///      reading the result as METRES gives ~1.64 × the physical half-width
///      (a 200 ft runway → ~97 m), approximating real hold-line placement
///      (FAA hold lines sit 125–280 ft from the centerline). Do NOT "fix" the
///      units — see <see cref="LegacySetbackMetres"/>.
///   4. Designated node at the legacy pick's junction, any taxiway (no
///      cleared-taxiway candidate exists; with no plain candidate anywhere,
///      the best designated node in the window is the only option left —
///      the junction guard keeps an HSND far along the runway from
///      out-ranking the plain node beside the natural hold point).
///   5. Plain node on any taxiway (legacy last resort).
///
/// Designated candidates are additionally gated by the node's own
/// <see cref="TaxiNode.HoldShortName"/>: when it names a runway (reciprocal-
/// aware), it must be THIS runway — TaxiGraph names hold nodes after their
/// runway precisely to disambiguate closely spaced parallels, whose
/// between-the-pair hold nodes can sit inside the geometric window.
///
/// Pure static (graph + runway + aircraft state in, node out) so
/// tools/ProgressiveTaxiProbe can assert the tiers on a synthetic graph.
/// </summary>
public static class HoldShortNodeResolver
{
    // Search corridor — shared with TaxiAssistForm.FindFarSideRunwayNode so
    // the near-side and far-side finders scan the same window (internal, one
    // home; a retune here reaches both).
    internal const double MAX_LATERAL_M = 600.0;        // max lateral distance from runway centerline
    internal const double MAX_ALONG_PAST_END_M = 500.0; // buffer past each runway end

    // A designated hold-short node is trusted as belonging to THIS runway only
    // within the same tolerance TaxiGraph uses to NAME hold nodes after a
    // runway — a DIRECT reference to that constant, so a retune there can
    // never silently diverge from the resolver's trust window. Beyond it, an
    // HS node inside the geometric window could belong to a parallel runway.
    private const double HS_NODE_MATCH_M = TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M;

    // A designated node closer to the centerline than the true pavement
    // half-width PLUS this buffer is treated as a misplaced scenery marker,
    // not a hold line — real hold lines never hug the pavement edge (FAA
    // minimum hold-line offset is 125 ft ≈ 38 m from the centerline; the
    // buffer keeps a heavy jet's nose out of the runway safety area when a
    // third-party scenery drops an HSND a metre off the pavement).
    private const double HS_MIN_SETBACK_BUFFER_M = 15.0;

    // Tier-2 window: how far along the runway a designated node may sit from
    // the cleared taxiway's own closest plain candidate and still be treated
    // as "this taxiway's hold line on an unnamed stub". Kept below typical
    // parallel-taxiway junction spacing (~100 m+) so an adjacent taxiway's
    // hold node doesn't hijack the route.
    private const double HS_JUNCTION_ALONG_M = 75.0;

    // Tier-2 stub budget: a designated node counts as "the cleared taxiway's
    // hold line on an unnamed stub" only when it is graph-connected to a node
    // carrying the cleared taxiway's name within this many edges. Without the
    // connectivity gate, a NEIGHBOURING connector's HSND inside the 75 m along
    // window hijacked the route sideways onto the wrong connector (2026-07
    // review, confirmed) — ladder junctions under 75 m spacing exist.
    private const int HS_STUB_MAX_HOPS = 3;

    private static bool ConnectsToTaxiway(TaxiGraph graph, int nodeId, string taxiway, int maxHops)
    {
        var visited = new HashSet<int> { nodeId };
        var frontier = new Queue<(int id, int depth)>();
        frontier.Enqueue((nodeId, 0));
        while (frontier.Count > 0)
        {
            var (id, depth) = frontier.Dequeue();
            if (graph.Nodes.TryGetValue(id, out var n) && n.TaxiwayNames.Contains(taxiway))
                return true;
            if (depth >= maxHops) continue;
            if (!graph.Adjacency.TryGetValue(id, out var edges)) continue;
            foreach (var e in edges)
                if (visited.Add(e.ToNodeId))
                    frontier.Enqueue((e.ToNodeId, depth + 1));
        }
        return false;
    }

    /// <summary>
    /// The legacy geometric hold setback. Runway.Width is in FEET; dividing by
    /// 2 and reading the result as METRES is a DELIBERATE conservative setback
    /// (~1.64 × the physical half-width — a 200 ft runway → ~97 m),
    /// approximating real hold-line placement so a geometric hold target
    /// clears the runway safety area. Do NOT "fix" the units to ft→m — that
    /// would put the target at the pavement edge with the tail still over the
    /// runway. Shared with TaxiAssistForm.FindFarSideRunwayNode so the
    /// near-side and far-side finders can never diverge on the setback.
    /// </summary>
    public static double LegacySetbackMetres(Runway runway) =>
        Math.Max(runway.Width > 0 ? runway.Width / 2.0 : 30.0, 15.0);

    /// <summary>
    /// TRUE physical half-width in metres (Runway.Width is FEET) — the
    /// pavement edge, used for on-runway detection and as the base of the
    /// designated-node sanity floor.
    /// </summary>
    public static double TrueHalfWidthMetres(Runway runway) =>
        runway.Width > 0 ? runway.Width * 0.3048 / 2.0 : 23.0;

    /// <summary>
    /// On-pavement side heuristic, shared by the near-side resolver and
    /// TaxiAssistForm.FindFarSideRunwayNode so the two can never diverge:
    /// the sign of the runway side the aircraft's heading points TOWARD
    /// (+1 = left of the runway heading, matching RunwayFrame's cross-track
    /// convention; runway-parallel headings deterministically return +1).
    /// The far-side finder targets this sign; the near-side resolver targets
    /// its negation (the side the aircraft is coming FROM). Both operands
    /// must be in the magnetic frame (runway.HeadingMag vs PLANE HEADING
    /// DEGREES MAGNETIC).
    /// </summary>
    public static int HeadingExitSideSign(Runway runway, double aircraftHeadingMag)
    {
        double perpComp = Math.Sin((runway.HeadingMag - aircraftHeadingMag) * Math.PI / 180.0);
        return perpComp >= 0 ? 1 : -1;
    }

    /// <summary>
    /// The near side of a runway as both near-side lookups see it: the aircraft's own side (the side its heading comes
    /// FROM when it is on the pavement), the search window in the aircraft's component, the two setbacks, and the
    /// designated-node test.
    /// </summary>
    private sealed class NearSideWindow
    {
        public readonly RunwayFrame Frame;
        public readonly double LegacyMinLateralM;
        private readonly double _hsFloorM;
        private readonly int _nearSign;
        private readonly int? _componentId;
        private readonly string _targetId;
        private readonly string _targetRecip;

        public NearSideWindow(TaxiGraph graph, Runway runway, double aircraftLat, double aircraftLon, double aircraftHeadingMag)
        {
            Frame = RunwayFrame.For(runway, aircraftLat);
            LegacyMinLateralM = LegacySetbackMetres(runway);
            double halfWidthTrueM = TrueHalfWidthMetres(runway);
            _hsFloorM = halfWidthTrueM + HS_MIN_SETBACK_BUFFER_M;

            double acSignedCT = Frame.SignedCrossTrack(aircraftLat, aircraftLon);

            // Near side = the aircraft's own side. The threshold is the TRUE
            // pavement edge, not the legacy setback: designated hold lines sit
            // INSIDE the legacy floor (KSFO: line at ~90 m, floor 97 m), so a
            // pilot legitimately stopped AT the hold line is off the pavement and
            // physically on a side — use it. Only an aircraft actually ON the
            // pavement needs the heading heuristic (which picks the side it is
            // coming FROM, opposite the exit side the far-side finder would pick,
            // and hard-codes a side for runway-parallel headings).
            _nearSign = Math.Abs(acSignedCT) >= halfWidthTrueM
                ? Math.Sign(acSignedCT)
                : -HeadingExitSideSign(runway, aircraftHeadingMag);

            // Restrict candidates to the aircraft's connected component so the
            // resolved node is actually reachable (mirrors FindFarSideRunwayNode).
            _componentId = graph.FindNearestNode(aircraftLat, aircraftLon)?.ComponentId;

            // The target runway's two designators, for the HoldShortName gate
            // (normalized — an unpadded RunwayID "9" must match a padded "09").
            _targetId = RouteRunwayCrossings.NormalizeDesignator(runway.RunwayID ?? "");
            _targetRecip = RouteRunwayCrossings.Reciprocal(_targetId);
        }

        public bool InWindow(TaxiNode node, out double lateralAbs, out double along)
        {
            lateralAbs = 0;
            along = 0;
            if (_componentId.HasValue && node.ComponentId != _componentId.Value)
                return false;

            double nodeSignedCT = Frame.SignedCrossTrack(node.Latitude, node.Longitude);
            if (Math.Sign(nodeSignedCT) != _nearSign) return false;
            lateralAbs = Math.Abs(nodeSignedCT);
            if (lateralAbs > MAX_LATERAL_M) return false;

            along = Frame.Along(node.Latitude, node.Longitude);
            if (along < -MAX_ALONG_PAST_END_M) return false;
            if (along > Frame.LengthM + MAX_ALONG_PAST_END_M) return false;
            return true;
        }

        // A scenery hold line for this runway: a hold-short node inside the trust band whose own name, when it says
        // which runway it guards, names this one (or its reciprocal — same pavement). A bare/unparseable name stays
        // geometric-only.
        public bool IsDesignated(TaxiNode node, double lateralAbs)
        {
            if ((node.Type != TaxiNodeType.HoldShort && node.Type != TaxiNodeType.ILSHoldShort)
                || lateralAbs < _hsFloorM || lateralAbs > HS_NODE_MATCH_M)
                return false;
            string? named = RouteRunwayCrossings.ExtractRunwayDesignator(node.HoldShortName);
            return named == null
                || named.Equals(_targetId, StringComparison.OrdinalIgnoreCase)
                || named.Equals(_targetRecip, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The runway's hold lines on the aircraft's side, one per approach, for a limit said with no taxiway step ("hold
    /// short of runway 24", spec 8.1 "limit only"): the caller takes the first the aircraft reaches
    /// (<c>ClearanceLegEnd.NearestAhead</c> by taxi distance), where <see cref="ResolveNearSide"/>, with no taxiway to
    /// anchor on, takes the line nearest the runway anywhere along it. Inside the same window as
    /// <see cref="ResolveNearSide"/>:
    /// <list type="bullet">
    /// <item>each designated hold line, unless another one nearer the runway lies within two edges of it (a CAT II/III
    /// line further out on the same stub: the runway's own line is the one held at, as tiers 1, 2 and 4 take the one
    /// closest to the runway);</item>
    /// <item>on an approach with no scenery line, its last node before the runway: one at or beyond the legacy setback
    /// with a neighbour nearer than it (or on the runway), unless a designated line lies within
    /// <c>HS_STUB_MAX_HOPS</c> edges and <c>HS_JUNCTION_ALONG_M</c> along the runway of it (tier 4: a designated node at
    /// the same junction outranks the plain pick).</item>
    /// </list>
    /// Empty when there is none.
    /// </summary>
    public static List<int> NearSideHoldCandidates(TaxiGraph graph, Runway runway,
        double aircraftLat, double aircraftLon, double aircraftHeadingMag)
    {
        var result = new List<int>();
        if (graph == null || runway == null) return result;
        var window = new NearSideWindow(graph, runway, aircraftLat, aircraftLon, aircraftHeadingMag);

        var designated = new Dictionary<int, (double Lateral, double Along)>();
        var plain = new List<(int Id, double Along)>();
        foreach (var node in graph.Nodes.Values)
        {
            if (!window.InWindow(node, out double lateral, out double along)) continue;
            if (window.IsDesignated(node, lateral))
            {
                designated[node.NodeId] = (lateral, along);
                continue;
            }
            if (lateral >= window.LegacyMinLateralM && LeadsOntoTheRunway(graph, window, node))
                plain.Add((node.NodeId, along));
        }

        foreach (var (id, (lateral, _)) in designated)
        {
            bool lineNearerOnTheStub = designated.Any(d => d.Key != id && d.Value.Lateral < lateral
                                                           && WithinHops(graph, id, d.Key, 2));
            if (!lineNearerOnTheStub) result.Add(id);
        }
        foreach (var (id, along) in plain)
        {
            bool lineAtTheJunction = designated.Any(d => Math.Abs(d.Value.Along - along) <= HS_JUNCTION_ALONG_M
                                                         && WithinHops(graph, id, d.Key, HS_STUB_MAX_HOPS));
            if (!lineAtTheJunction) result.Add(id);
        }
        return result;
    }

    // A neighbour of node lies nearer the runway than the legacy setback, on the pavement, or across it: node is the last
    // node before the runway on its connector.
    private static bool LeadsOntoTheRunway(TaxiGraph graph, NearSideWindow window, TaxiNode node)
    {
        if (!graph.Adjacency.TryGetValue(node.NodeId, out var edges)) return false;
        double side = window.Frame.SignedCrossTrack(node.Latitude, node.Longitude);
        foreach (var e in edges)
        {
            if (!graph.Nodes.TryGetValue(e.ToNodeId, out var next)) continue;
            double ct = window.Frame.SignedCrossTrack(next.Latitude, next.Longitude);
            if (Math.Sign(ct) != Math.Sign(side) || Math.Abs(ct) < window.LegacyMinLateralM) return true;
        }
        return false;
    }

    // Whether node b is within maxHops edges of node a.
    private static bool WithinHops(TaxiGraph graph, int a, int b, int maxHops)
    {
        var visited = new HashSet<int> { a };
        var frontier = new Queue<(int Id, int Depth)>();
        frontier.Enqueue((a, 0));
        while (frontier.Count > 0)
        {
            var (id, depth) = frontier.Dequeue();
            if (id == b) return true;
            if (depth >= maxHops || !graph.Adjacency.TryGetValue(id, out var edges)) continue;
            foreach (var e in edges)
                if (visited.Add(e.ToNodeId))
                    frontier.Enqueue((e.ToNodeId, depth + 1));
        }
        return false;
    }

    /// <summary>
    /// Finds the hold-short node on the aircraft's side of <paramref name="runway"/>,
    /// preferring the scenery's designated hold-short nodes per the tier order
    /// in the class remarks, falling back to the legacy geometric
    /// closest-to-centerline scan.
    /// </summary>
    public static TaxiNode? ResolveNearSide(
        TaxiGraph graph, Runway runway,
        double aircraftLat, double aircraftLon, double aircraftHeadingMag,
        string? lastTaxiway)
    {
        // No cleared taxiway (a limit said alone, "hold short of runway 24"): tiers 1-3 have nothing to anchor on, and
        // the pick is the line nearest the runway anywhere along it; the window takes NearSideHoldCandidates instead.
        var window = new NearSideWindow(graph, runway, aircraftLat, aircraftLon, aircraftHeadingMag);
        double legacyMinLateralM = window.LegacyMinLateralM;

        bool OnLastTaxiway(TaxiNode node) => lastTaxiway != null && node.TaxiwayNames.Contains(lastTaxiway);

        // ---- Single scan: collect designated candidates + track plain bests ----
        var designated = new List<(TaxiNode node, double lateral, double along)>();

        TaxiNode? bestNode = null;   double bestLateral = double.MaxValue;     // plain, any taxiway
        double bestNodeAlong = 0;
        TaxiNode? bestOnTw = null;   double bestLateralTw = double.MaxValue;   // plain, on lastTaxiway
        double bestOnTwAlong = 0;

        foreach (var node in graph.Nodes.Values)
        {
            if (!window.InWindow(node, out double lateralAbs, out double along)) continue;

            // Runway gate inside: when the node's own name says which runway it guards, it must be this one.
            if (window.IsDesignated(node, lateralAbs))
                designated.Add((node, lateralAbs, along));

            // Legacy geometric tracking (a designated node past the legacy
            // floor also counts here, exactly as the old single-pass scan did).
            if (lateralAbs < legacyMinLateralM) continue;
            if (lateralAbs < bestLateral) { bestLateral = lateralAbs; bestNode = node; bestNodeAlong = along; }
            if (lateralAbs < bestLateralTw && OnLastTaxiway(node))
            {
                bestLateralTw = lateralAbs;
                bestOnTw = node;
                bestOnTwAlong = along;
            }
        }

        static TaxiNode? ClosestToRunway(IEnumerable<(TaxiNode node, double lateral, double along)> c)
        {
            TaxiNode? best = null; double bestLat = double.MaxValue;
            foreach (var (node, lateral, _) in c)
                if (lateral < bestLat) { bestLat = lateral; best = node; }
            return best;
        }

        // Tier 1: designated node on the cleared taxiway.
        var tier1 = ClosestToRunway(designated.Where(d => OnLastTaxiway(d.node)));
        if (tier1 != null) return tier1;

        if (bestOnTw != null)
        {
            // Tier 2: designated node at the cleared taxiway's runway junction
            // (unnamed-stub shape) — must also CONNECT to the cleared taxiway
            // within the stub budget, so a parallel neighbour connector's hold
            // line inside the along window can't hijack the route sideways.
            var tier2 = ClosestToRunway(designated.Where(d =>
                Math.Abs(d.along - bestOnTwAlong) <= HS_JUNCTION_ALONG_M &&
                ConnectsToTaxiway(graph, d.node.NodeId, lastTaxiway!, HS_STUB_MAX_HOPS)));
            if (tier2 != null) return tier2;

            // Tier 3: plain node on the cleared taxiway (legacy behaviour).
            return bestOnTw;
        }

        // Tiers 4/5: no cleared-taxiway candidate at all. A designated node is
        // preferred only when it sits at the SAME junction as the legacy pick
        // (the marked hold line for that junction) — a designated node far
        // along the runway must not out-rank the plain node next to the
        // aircraft's natural hold point. With no plain candidate at all, the
        // best designated node anywhere is the only option left.
        var tier4 = ClosestToRunway(designated.Where(d =>
            bestNode == null || Math.Abs(d.along - bestNodeAlong) <= HS_JUNCTION_ALONG_M));
        return tier4 ?? bestNode;
    }
}
