// Where a leg with no destination ends, as its steps say (spec 8.1): the lookups the Taxi Guidance window makes from
// the plan (ClearancePlan.End, EndStep) before LoadRoute. Graph in, node out; the window adds the aircraft.

using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The pieces of a leg end that do not need the window: which of several junctions or nodes a "hold short of taxiway"
/// leg ends at (the nearest by taxi distance the aircraft need not turn round for), the nodes of a taxiway a limit
/// said with no taxiway step can end at, where the last taxiway ends (along its own edges, the way the aircraft
/// travels on it), and whether the aircraft is already across a crossing it made.
/// </summary>
public static class ClearanceLegEnd
{
    /// <summary>A way whose first real leg points more than this off the heading begins by turning round (as LoadRoute's backwards-start test).</summary>
    public const double ReversalDegrees = 120.0;

    /// <summary>Legs shorter than this never decide which way a route begins.</summary>
    public const double MinLegMetres = 3.0;

    /// <summary>An arm of a taxiway within this of the direction of travel is straight on.</summary>
    public const double StraightOnDegrees = 45.0;

    /// <summary>How far from the entry an arm's direction is read, so a short stub at a junction does not decide it.</summary>
    public const double ArmDirectionMetres = 20.0;

    /// <summary>
    /// Of <paramref name="candidateNodeIds"/>, the nearest by taxi distance from the route's start whose way there does
    /// not begin by turning round (<paramref name="needsReversal"/>, asked nearest first and only until one is ahead);
    /// with every reachable one behind, the nearest of them. Unreachable candidates (no distance) are never asked
    /// about and never beat a reachable one; with only those, <see cref="TaxiwayHoldPlacement.NearestByTaxiDistance"/>
    /// decides. -1 for none.
    /// </summary>
    public static int NearestAhead(IReadOnlyList<int> candidateNodeIds, IReadOnlyDictionary<int, double> taxiMetresFromStart,
        Func<int, bool> needsReversal)
    {
        if (candidateNodeIds == null || candidateNodeIds.Count == 0) return -1;
        var reachable = candidateNodeIds
            .Where(id => taxiMetresFromStart.ContainsKey(id))
            .Distinct()
            .OrderBy(id => taxiMetresFromStart[id])
            .ThenBy(id => id)
            .ToList();
        foreach (int id in reachable)
            if (!needsReversal(id)) return id;
        return TaxiwayHoldPlacement.NearestByTaxiDistance(candidateNodeIds, taxiMetresFromStart);
    }

    /// <summary>
    /// Whether the shortest way from <paramref name="startNodeId"/> to <paramref name="targetNodeId"/> begins with a
    /// real leg (<see cref="MinLegMetres"/> or more) pointing more than <see cref="ReversalDegrees"/> off
    /// <paramref name="headingTrue"/>: the aircraft would have to turn round for it. False for the start itself and
    /// for a target it cannot reach. A fresh router, so no Turn pick's blocked nodes take part ([TURN-1]).
    /// </summary>
    public static bool NeedsReversal(TaxiGraph graph, int startNodeId, int targetNodeId, double headingTrue)
    {
        if (graph == null || startNodeId == targetNodeId) return false;
        var route = new TaxiRouter(graph).FindShortestPath(startNodeId, targetNodeId);
        if (route?.Segments == null) return false;
        foreach (var seg in route.Segments)
        {
            if (seg.DistanceMeters < MinLegMetres) continue;
            return Math.Abs(TaxiGraph.NormalizeAngle(seg.BearingDegrees - headingTrue)) > ReversalDegrees;
        }
        return false;
    }

    /// <summary>
    /// Every node on <paramref name="taxiway"/> in component <paramref name="componentId"/> outside every runway's
    /// clear margin (<see cref="RouteRunwayCrossings.RunwayWithinClearMargin"/>): where a limit "hold short of taxiway
    /// L" said with no taxiway step may end, a leg never ending on or beside a runway. The window takes the nearest.
    /// </summary>
    public static List<int> NodesOnTaxiway(TaxiGraph graph, string taxiway, int componentId)
    {
        var nodes = new List<int>();
        if (graph == null || string.IsNullOrWhiteSpace(taxiway)) return nodes;
        foreach (int id in graph.GetNodesOnTaxiway(taxiway))
        {
            if (!graph.Nodes.TryGetValue(id, out var node) || node.ComponentId != componentId) continue;
            if (!ClearOfRunways(graph, node)) continue;
            nodes.Add(id);
        }
        return nodes;
    }

    /// <summary>
    /// Where a taxiway ends: <see cref="EndNodeId"/> the last node clear of every runway's margin along the chosen arm
    /// (past the entry), -1 when there is none; <see cref="ArmEndNodeId"/> the arm's own last node whatever lies
    /// there, -1 when the entry has no arm on the taxiway (the window tells "no end clear of a runway" from "not found" by it).
    /// </summary>
    public readonly record struct TaxiwayEnd(int EndNodeId, int ArmEndNodeId);

    /// <summary>
    /// The end of <paramref name="taxiway"/> for a leg that ends there, from where the aircraft is on it or joins it
    /// (<paramref name="entryNodeId"/>), found along the taxiway's OWN edges, never the node of that name farthest away
    /// in a straight line. Each arm leaving the entry on the taxiway is walked to its end, taking the straightest way
    /// on where the taxiway forks and never a node twice. The arm is the only one; else the one leaving within
    /// <see cref="StraightOnDegrees"/> of <paramref name="travelBearingTrue"/>, the way the aircraft travels (its
    /// heading when on the taxiway, the way it comes when joining it), when exactly one does; else the arm whose end
    /// lies farthest from the aircraft. A taxiway that runs onto a runway ends at its last node clear of it.
    /// </summary>
    public static TaxiwayEnd EndOfTaxiway(TaxiGraph graph, int entryNodeId, string taxiway, double travelBearingTrue,
        double aircraftLat, double aircraftLon)
    {
        if (graph == null || string.IsNullOrWhiteSpace(taxiway) || !graph.Nodes.ContainsKey(entryNodeId)
            || !graph.Adjacency.TryGetValue(entryNodeId, out var edges))
            return new TaxiwayEnd(-1, -1);

        var arms = new List<List<int>>();
        foreach (var edge in edges)
        {
            if (!OnTaxiway(edge, taxiway) || edge.ToNodeId == entryNodeId) continue;
            if (arms.Any(a => a.Count > 1 && a[1] == edge.ToNodeId)) continue;
            arms.Add(WalkArm(graph, entryNodeId, edge.ToNodeId, taxiway));
        }
        if (arms.Count == 0) return new TaxiwayEnd(-1, -1);

        var arm = ChooseArm(graph, arms, travelBearingTrue, aircraftLat, aircraftLon);
        for (int i = arm.Count - 1; i >= 1; i--)
        {
            if (graph.Nodes.TryGetValue(arm[i], out var node) && ClearOfRunways(graph, node))
                return new TaxiwayEnd(arm[i], arm[^1]);
        }
        return new TaxiwayEnd(-1, arm[^1]);
    }

    // The arm from entry through first, along the taxiway's edges: at each node the straightest way on, never a node twice.
    private static List<int> WalkArm(TaxiGraph graph, int entry, int first, string taxiway)
    {
        var arm = new List<int> { entry, first };
        var seen = new HashSet<int> { entry, first };
        int prev = entry, cur = first;
        while (arm.Count < 100_000 && graph.Adjacency.TryGetValue(cur, out var edges))
        {
            double inBearing = Bearing(graph, prev, cur);
            int next = -1;
            double bestTurn = double.MaxValue;
            foreach (var edge in edges)
            {
                if (!OnTaxiway(edge, taxiway) || seen.Contains(edge.ToNodeId)) continue;
                double turn = Math.Abs(TaxiGraph.NormalizeAngle(Bearing(graph, cur, edge.ToNodeId) - inBearing));
                if (turn < bestTurn || (turn == bestTurn && edge.ToNodeId < next))
                {
                    bestTurn = turn;
                    next = edge.ToNodeId;
                }
            }
            if (next < 0) break;
            arm.Add(next);
            seen.Add(next);
            prev = cur;
            cur = next;
        }
        return arm;
    }

    private static List<int> ChooseArm(TaxiGraph graph, List<List<int>> arms, double travelBearingTrue,
        double aircraftLat, double aircraftLon)
    {
        if (arms.Count == 1) return arms[0];
        var straightOn = arms
            .Where(a => Math.Abs(TaxiGraph.NormalizeAngle(ArmDirection(graph, a) - travelBearingTrue)) <= StraightOnDegrees)
            .ToList();
        if (straightOn.Count == 1) return straightOn[0];
        return arms
            .OrderByDescending(a => graph.Nodes.TryGetValue(a[^1], out var end)
                ? TaxiGraph.FastDistanceMeters(aircraftLat, aircraftLon, end.Latitude, end.Longitude)
                : -1.0)
            .ThenBy(a => a[^1])
            .First();
    }

    // The bearing from the arm's entry to its first node at least ArmDirectionMetres away (its end when none is).
    private static double ArmDirection(TaxiGraph graph, List<int> arm)
    {
        var entry = graph.Nodes[arm[0]];
        int toward = arm[^1];
        for (int i = 1; i < arm.Count; i++)
        {
            var node = graph.Nodes[arm[i]];
            if (TaxiGraph.FastDistanceMeters(entry.Latitude, entry.Longitude, node.Latitude, node.Longitude) >= ArmDirectionMetres)
            {
                toward = arm[i];
                break;
            }
        }
        return Bearing(graph, arm[0], toward);
    }

    private static double Bearing(TaxiGraph graph, int from, int to)
    {
        var a = graph.Nodes[from];
        var b = graph.Nodes[to];
        return NavigationCalculator.CalculateBearing(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
    }

    private static bool OnTaxiway(TaxiEdge edge, string taxiway)
        => string.Equals(edge.TaxiwayName, taxiway, StringComparison.OrdinalIgnoreCase);

    private static bool ClearOfRunways(TaxiGraph graph, TaxiNode node)
        => RouteRunwayCrossings.RunwayWithinClearMargin(graph.RunwayCenterlines, node.Latitude, node.Longitude) == null;

    /// <summary>Where the aircraft joins the last taxiway of a leg that ends at its end, and the way it travels on it.</summary>
    public readonly record struct TaxiwayEntry(int NodeId, double TravelBearingTrue);

    /// <summary>
    /// The entry and the direction <see cref="EndOfTaxiway"/> takes for <paramref name="taxiway"/>, the last Via
    /// taxiway. Joining it from <paramref name="previousTaxiway"/> (the Via taxiway before it), the aircraft not yet on
    /// it: their junction nearest by taxi distance (<see cref="NearestAhead"/>), with the bearing from the aircraft to it
    /// as the way it comes (its heading within <see cref="ArmDirectionMetres"/> of it), but only when the aircraft need
    /// not turn round for it. Otherwise, and whenever the aircraft is on the taxiway already
    /// (<see cref="IsOnTaxiway"/>: "A, P" still listed with A behind, "A, C, A", a finished Via kept in front of a stop),
    /// its node nearest ahead in <paramref name="componentId"/>, with the aircraft's own heading as the way: a junction
    /// behind taken as the entry made the bearing back to it the way on, and the leg ran back past it. -1 when the
    /// taxiway has no node in the component.
    /// </summary>
    public static TaxiwayEntry EntryOntoTaxiway(TaxiGraph graph, string taxiway, string? previousTaxiway, int componentId,
        IReadOnlyDictionary<int, double> taxiMetresFromStart, Func<int, bool> needsReversal,
        double aircraftLat, double aircraftLon, double headingTrue)
    {
        if (graph == null || string.IsNullOrWhiteSpace(taxiway)) return new TaxiwayEntry(-1, headingTrue);
        if (!string.IsNullOrWhiteSpace(previousTaxiway) && !IsOnTaxiway(graph, taxiway, aircraftLat, aircraftLon))
        {
            int junction = NearestAhead(graph.FindTaxiwayIntersectionNodes(previousTaxiway, taxiway, componentId),
                taxiMetresFromStart, needsReversal);
            if (junction >= 0 && !needsReversal(junction) && graph.Nodes.TryGetValue(junction, out var node))
            {
                double toJunction = TaxiGraph.FastDistanceMeters(aircraftLat, aircraftLon, node.Latitude, node.Longitude);
                double comes = toJunction < ArmDirectionMetres
                    ? headingTrue
                    : NavigationCalculator.CalculateBearing(aircraftLat, aircraftLon, node.Latitude, node.Longitude);
                return new TaxiwayEntry(junction, comes);
            }
        }
        var onTaxiway = graph.GetNodesOnTaxiway(taxiway)
            .Where(id => graph.Nodes.TryGetValue(id, out var n) && n.ComponentId == componentId)
            .ToList();
        return new TaxiwayEntry(NearestAhead(onTaxiway, taxiMetresFromStart, needsReversal), headingTrue);
    }

    /// <summary>
    /// Whether the position is on <paramref name="taxiway"/>: within <see cref="PavementTolerance.ForWidthFeet"/> (the
    /// off-route test's own tolerance) of one of its edges.
    /// </summary>
    public static bool IsOnTaxiway(TaxiGraph graph, string taxiway, double lat, double lon)
    {
        if (graph == null || string.IsNullOrWhiteSpace(taxiway)) return false;
        foreach (int id in graph.GetNodesOnTaxiway(taxiway))
        {
            if (!graph.Nodes.TryGetValue(id, out var a) || !graph.Adjacency.TryGetValue(id, out var edges)) continue;
            foreach (var edge in edges)
            {
                if (!OnTaxiway(edge, taxiway) || !graph.Nodes.TryGetValue(edge.ToNodeId, out var b)) continue;
                double off = GuidanceGeometry.PerpendicularDistanceToSegmentMeters(lat, lon,
                    a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (off <= PavementTolerance.ForWidthFeet(edge.WidthFeet)) return true;
            }
        }
        return false;
    }

    /// <summary>How a leg that ends just across a runway it has already crossed ends.</summary>
    public enum MadeCrossingEnd
    {
        /// <summary>The aircraft is across and clear of every runway: the leg's end is reached; refused, said.</summary>
        AlreadyAcross,
        /// <summary>Route on to the far side, the side away from the crossing's pin (<see cref="MadeCrossingJudgement.FarSideSign"/>).</summary>
        FarSide,
        /// <summary>The pin has no side to go by (it lies on the runway's pavement): refused, never guessed.</summary>
        Unknown,
    }

    /// <summary>What <see cref="JudgeMadeCrossing"/> decides, with the far side's cross-track sign for <see cref="MadeCrossingEnd.FarSide"/> (0 otherwise).</summary>
    public readonly record struct MadeCrossingJudgement(MadeCrossingEnd End, int FarSideSign);

    /// <summary>
    /// How a leg ends whose last step is a crossing already made (pinned by <c>ClearanceTrim</c>,
    /// <see cref="ClearanceStep.AtNodeIsPassedStop"/>): to its stop, or, for a crossing made with no stop, to where the
    /// route reached the runway. Signed cross-tracks in metres. The pin's side is the near side only when it lies off the
    /// pavement (<paramref name="runwayHalfWidthMetres"/> or more from the centreline); a pin on the pavement has none.
    /// The aircraft clear of every runway (<paramref name="aircraftClearOfRunways"/>) and on the other side, or with no
    /// side to compare, is already across. Otherwise a pin with a side sends the leg to the far side of THAT crossing,
    /// never the far side judged from the aircraft, which, once it is off the far edge, lies back over the runway; a pin
    /// with none is refused.
    /// </summary>
    public static MadeCrossingJudgement JudgeMadeCrossing(double pinCrossTrack, double runwayHalfWidthMetres,
        double aircraftCrossTrack, bool aircraftClearOfRunways)
    {
        int nearSide = Math.Abs(pinCrossTrack) >= runwayHalfWidthMetres ? Math.Sign(pinCrossTrack) : 0;
        if (aircraftClearOfRunways && (nearSide == 0 || Math.Sign(aircraftCrossTrack) == -nearSide))
            return new MadeCrossingJudgement(MadeCrossingEnd.AlreadyAcross, 0);
        return nearSide == 0
            ? new MadeCrossingJudgement(MadeCrossingEnd.Unknown, 0)
            : new MadeCrossingJudgement(MadeCrossingEnd.FarSide, -nearSide);
    }

    /// <summary>The Calculate refusal for <see cref="MadeCrossingEnd.AlreadyAcross"/>.</summary>
    public static string AlreadyAcross(string runway) => $"You are already across runway {runway.Trim()}. Add the next step.";
}
