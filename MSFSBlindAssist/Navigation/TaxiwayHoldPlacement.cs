using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Where a "hold short of a taxiway" stop goes on a route: set back from the junction so the aircraft
/// stays off the taxiway, never on or inside a runway's clear margin, nor where the aircraft would be
/// captured by the stop on one (the <see cref="ProgressiveLegEnd.CaptureRadiusM"/> before it), never
/// splitting an off-chart leg; with a Turn pick, before the turn when the taxiway lies less than the setback past
/// it, as with none (<see cref="ChooseAroundTurn"/>). Pure.
/// </summary>
public static class TaxiwayHoldPlacement
{
    public const double JunctionHalfWidthM = 15.0;    // assumed: half a wide taxiway plus a margin
    public const double NoseShareOfWingspan = 0.55;   // assumed: reference point to nose, as a share of wingspan
    public const double DefaultWingspanM = 36.0;
    public const double MinSetbackM = 25.0;
    public const double MaxSetbackM = 60.0;
    public const double ExistingNodeReachM = 40.0;
    public const double RecalcMaxBackM = 150.0;
    public const double MinFromRouteStartM = 10.0;
    public const double NodeMatchToleranceM = 1.0;    // a recalculated route measures an inserted node from the split edges

    public const double SampleStepM = 5.0;            // runway sampling along a stretch of route, at most this far apart

    public enum StopKind { ExistingNode, InsertOnSegment, Junction, NoSafeStop }
    public readonly record struct Stop(StopKind Kind, int SegmentIndex, double MetresBeforeSegmentEnd);

    public static double SetbackMetres(double wingspanM)
        => Math.Clamp(JunctionHalfWidthM + NoseShareOfWingspan * (wingspanM > 0 ? wingspanM : DefaultWingspanM),
                      MinSetbackM, MaxSetbackM);

    /// <param name="isClearOfRunways">(lat, lon) → true when the point is clear of every runway by the margin the
    /// automatic runway pass demands of a stop it invents. The manager passes
    /// <c>RouteRunwayCrossings.RunwayWithinClearMargin(_graph.RunwayCenterlines, lat, lon) == null</c>.</param>
    /// <param name="firstUsableIndex">The first segment on the taxiway the pilot was told to turn onto (a Turn pick's
    /// <c>FirstTaxiwaySegment</c>); 0 without one.</param>
    /// <remarks>A candidate (an existing node or an inserted point) is usable only on or after
    /// <paramref name="firstUsableIndex"/> and when the whole route from <see cref="ProgressiveLegEnd.CaptureRadiusM"/>
    /// before it up to the junction is clear of runways, sampled at most <see cref="SampleStepM"/> apart: a mid-route
    /// hold and a leg end are both captured that far before their stop, and a leg never ends, nor is a stop asked
    /// for, on a runway (design 8.5, 8.8). Where that stretch runs off the start of the route, only the part that
    /// exists is judged. A refused candidate moves forward to the first usable node, else the junction; a junction
    /// whose own approach is not clear gets no stop at all.</remarks>
    public static Stop Choose(IReadOnlyList<TaxiRouteSegment> segments, int junctionSegmentIndex, double setbackM, bool allowInsert,
                              Func<double, double, bool> isClearOfRunways, int firstUsableIndex = 0)
    {
        int j = junctionSegmentIndex;
        // Metres along the route to each node: node i + 1 is segments[i].ToNode, node 0 the route's first node.
        var along = new double[j + 2];
        for (int i = 0; i <= j; i++) along[i + 1] = along[i] + segments[i].DistanceMeters;
        double junctionM = along[j + 1];

        // Clear from the capture radius before the point to the junction: clear for one point, clear for every
        // point after it.
        bool ApproachClear(double atM)
            => RouteClear(segments, along, Math.Max(0, atM - ProgressiveLegEnd.CaptureRadiusM), junctionM, isClearOfRunways);

        // The junction inside a runway's clear margin, or reached over one within the capture radius: no point
        // before it is usable either.
        if (!ApproachClear(junctionM))
            return new Stop(StopKind.NoSafeStop, -1, 0);

        bool NodeUsable(int k) => k >= firstUsableIndex && ApproachClear(along[k + 1]);

        // Rule 1's candidate: the largest k whose end node lies at least the setback before the junction.
        int kFound = -1;
        double dK = 0;
        {
            double d = 0;
            for (int k = j - 1; k >= 0; k--)
            {
                d += segments[k + 1].DistanceMeters;
                if (d >= setbackM - NodeMatchToleranceM) { kFound = k; dK = d; break; }
            }
        }

        bool passedOver = false;

        // Rule 1.
        if (kFound >= 0 && dK <= setbackM + ExistingNodeReachM)
        {
            if (NodeUsable(kFound)) return new Stop(StopKind.ExistingNode, kFound, 0);
            passedOver = true;
        }

        // Rule 2.
        bool refusedOffChart = false;
        if (allowInsert)
        {
            double dm = 0;
            for (int m = j; m >= 0; m--)
            {
                if (m < j) dm += segments[m + 1].DistanceMeters;
                var seg = segments[m];
                if (dm <= setbackM && setbackM < dm + seg.DistanceMeters)
                {
                    if (seg.IsOffChartTurn || seg.IsUncharted)
                    {
                        refusedOffChart = true;
                    }
                    else
                    {
                        double metresBeforeEnd = setbackM - dm;
                        double fromStart = along[m + 1] - metresBeforeEnd;
                        if (fromStart >= MinFromRouteStartM)
                        {
                            if (m >= firstUsableIndex && ApproachClear(fromStart))
                                return new Stop(StopKind.InsertOnSegment, m, metresBeforeEnd);
                            passedOver = true;
                        }
                    }
                    break;
                }
            }
        }

        // Rule 3.
        if ((!allowInsert || refusedOffChart) && kFound >= 0 && dK <= RecalcMaxBackM)
        {
            if (NodeUsable(kFound)) return new Stop(StopKind.ExistingNode, kFound, 0);
            passedOver = true;
        }

        // A refused candidate moves forward: the first usable node before the junction, else the junction, whose
        // approach was found clear above.
        if (passedOver)
        {
            for (int k = Math.Max(0, firstUsableIndex); k < j; k++)
                if (NodeUsable(k)) return new Stop(StopKind.ExistingNode, k, 0);
            return new Stop(StopKind.Junction, j, 0);
        }

        // Rule 4.
        return new Stop(StopKind.Junction, j, 0);
    }

    /// <summary>
    /// The stop for a taxiway hold on a row with a Turn pick, <paramref name="pick"/> (null without one): where
    /// <see cref="Choose"/> places it with no floor, as if the route had no pick (owner, 2026-11-25: "Stop before the
    /// turn"), so a taxiway less than the setback past the turn is held short of before the turn, on the taxiway before
    /// it, the whole aircraft clear of it, and one further on after the turn, as before. One stop alone never stands:
    /// the turn's own join node reached over a link from its departure node (<see cref="IsJoinOfALinkedTurn"/>), where
    /// the turn is made but the leg would end with it dropped from the route, an off-chart leg ending the route and
    /// never announced: that one is placed on the picked taxiway, floored at its first segment, as before.
    /// </summary>
    public static Stop ChooseAroundTurn(IReadOnlyList<TaxiRouteSegment> segments, int junctionSegmentIndex, double setbackM,
                                        bool allowInsert, Func<double, double, bool> isClearOfRunways, AppliedTurnPick? pick)
    {
        var stop = Choose(segments, junctionSegmentIndex, setbackM, allowInsert, isClearOfRunways);
        if (pick == null || !IsJoinOfALinkedTurn(stop, pick)) return stop;
        return Choose(segments, junctionSegmentIndex, setbackM, allowInsert, isClearOfRunways, firstUsableIndex: pick.FirstTaxiwaySegment);
    }

    /// <summary>
    /// Whether <paramref name="stop"/> is an existing node that is <paramref name="pick"/>'s join node at the end of a
    /// link from its departure node (an off-chart leg, or a charted link's connector): node k is the end node of
    /// segment k, and the join node ends the segment before <see cref="AppliedTurnPick.FirstTaxiwaySegment"/>. A turn
    /// at a junction (departure node = join node) has no link: stopping at it is stopping before the turn. A point
    /// inserted on the link, or a node on it before the join, is before the picked taxiway, and is kept.
    /// </summary>
    public static bool IsJoinOfALinkedTurn(Stop stop, AppliedTurnPick pick)
        => stop.Kind == StopKind.ExistingNode
           && pick.FirstTaxiwaySegment > pick.FirstSegmentAfterDeparture
           && stop.SegmentIndex == pick.FirstTaxiwaySegment - 1;

    /// The point metresBeforeEnd back from the segment's end node, interpolated linearly between its two nodes.
    public static (double Lat, double Lon) PointBeforeEnd(TaxiRouteSegment segment, double metresBeforeEnd)
    {
        double f = segment.DistanceMeters > 0 ? (segment.DistanceMeters - metresBeforeEnd) / segment.DistanceMeters : 0;
        return (segment.FromNode.Latitude + f * (segment.ToNode.Latitude - segment.FromNode.Latitude),
                segment.FromNode.Longitude + f * (segment.ToNode.Longitude - segment.FromNode.Longitude));
    }

    /// True when every sample of the route from fromM to toM metres along it (alongM[i] is node i's distance, node 0
    /// the route's first) is clear of runways: within each segment, steps of at most SampleStepM with both ends of
    /// its piece included, so a runway crossed inside one edge, which has no node on it, is found too.
    private static bool RouteClear(IReadOnlyList<TaxiRouteSegment> segments, double[] alongM, double fromM, double toM,
                                   Func<double, double, bool> isClearOfRunways)
    {
        for (int i = 0; i + 1 < alongM.Length; i++)
        {
            double segStart = alongM[i], segEnd = alongM[i + 1];
            if (segEnd < fromM || segStart > toM) continue;
            double a = Math.Max(segStart, fromM), b = Math.Min(segEnd, toM);
            int steps = Math.Max(1, (int)Math.Ceiling((b - a) / SampleStepM));
            for (int s = 0; s <= steps; s++)
            {
                var (lat, lon) = PointBeforeEnd(segments[i], segEnd - (a + (b - a) * s / steps));
                if (!isClearOfRunways(lat, lon)) return false;
            }
        }
        return true;
    }

    public static (TaxiRouteSegment First, TaxiRouteSegment Second) Split(TaxiRouteSegment segment, TaxiNode mid, double metresBeforeEnd)
    {
        var first = new TaxiRouteSegment
        {
            FromNode = segment.FromNode,
            ToNode = mid,
            DistanceMeters = segment.DistanceMeters - metresBeforeEnd,
            CumulativeDistanceMeters = segment.CumulativeDistanceMeters,
            RemainingDistanceMeters = segment.RemainingDistanceMeters,
            TaxiwayName = segment.TaxiwayName,
            BearingDegrees = segment.BearingDegrees,
            TurnAngleDegrees = segment.TurnAngleDegrees,
            TurnDirection = segment.TurnDirection,
            PathWidth = segment.PathWidth,
            IsHoldShortPoint = false,
            HoldShortRunway = "",
            IsUncharted = segment.IsUncharted,
            IsOffChartTurn = segment.IsOffChartTurn,
        };
        var second = new TaxiRouteSegment
        {
            FromNode = mid,
            ToNode = segment.ToNode,
            DistanceMeters = metresBeforeEnd,
            CumulativeDistanceMeters = segment.CumulativeDistanceMeters,
            RemainingDistanceMeters = segment.RemainingDistanceMeters,
            TaxiwayName = segment.TaxiwayName,
            BearingDegrees = segment.BearingDegrees,
            TurnAngleDegrees = 0,
            TurnDirection = "straight",
            PathWidth = segment.PathWidth,
            IsHoldShortPoint = segment.IsHoldShortPoint,
            HoldShortRunway = segment.HoldShortRunway,
            // The stop is the far half's, and so are the runways cleared at it (ClearedCrossingHolds).
            PlacedHoldLabel = segment.PlacedHoldLabel,
            IsUncharted = segment.IsUncharted,
            IsOffChartTurn = segment.IsOffChartTurn,
        };
        second.ClearedRunways.AddRange(segment.ClearedRunways);
        return (first, second);
    }

    /// The runway events with each stop and reach node index above splitIndex moved up by one, replaced in place:
    /// segment splitIndex became two, so its own end node (node splitIndex + 1, where the far half keeps the stop) and
    /// every node after it are one further along. A start hold (0) and no stop (-1) never move. Null: nothing to shift.
    public static void ShiftEventsAfterSplit(List<TaxiRouteRunwayEvent>? events, int splitIndex)
    {
        if (events == null) return;
        static int Shifted(int nodeIndex, int split) => nodeIndex > split && nodeIndex >= 1 ? nodeIndex + 1 : nodeIndex;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is not { } ev) continue;
            int stop = Shifted(ev.StopNodeIndex, splitIndex), reach = Shifted(ev.ReachNodeIndex, splitIndex);
            if (stop != ev.StopNodeIndex || reach != ev.ReachNodeIndex)
                events[i] = ev.WithNodeIndices(stop, reach);
        }
    }

    /// The picks with each segment index above splitIndex moved up by one (segment splitIndex became two).
    public static List<AppliedTurnPick> ShiftPicksAfterSplit(IReadOnlyList<AppliedTurnPick> picks, int splitIndex)
        => picks.Select(p => new AppliedTurnPick
        {
            RowIndex = p.RowIndex,
            Side = p.Side,
            TaxiwayName = p.TaxiwayName,
            DepartureNodeId = p.DepartureNodeId,
            JoinNodeId = p.JoinNodeId,
            FirstSegmentAfterDeparture = p.FirstSegmentAfterDeparture + (p.FirstSegmentAfterDeparture > splitIndex ? 1 : 0),
            FirstTaxiwaySegment = p.FirstTaxiwaySegment + (p.FirstTaxiwaySegment > splitIndex ? 1 : 0),
            ChangedRoute = p.ChangedRoute,
            IsOffChart = p.IsOffChart,
            OffChartLengthMeters = p.OffChartLengthMeters,
            BlockedNodeIds = p.BlockedNodeIds,
        }).ToList();

    /// The candidate with the smallest taxi distance; one missing from the dictionary ranks after every one
    /// present; ties go to the smaller id; an empty list gives -1.
    public static int NearestByTaxiDistance(IReadOnlyList<int> candidateNodeIds, IReadOnlyDictionary<int, double> taxiMetresFromStart)
    {
        int best = -1;
        bool bestHas = false;
        double bestD = 0;
        foreach (int id in candidateNodeIds)
        {
            bool has = taxiMetresFromStart.TryGetValue(id, out double d);
            bool better;
            if (best < 0) better = true;
            else if (has != bestHas) better = has;
            else if (has && d != bestD) better = d < bestD;
            else better = id < best;
            if (better) { best = id; bestHas = has; bestD = has ? d : 0; }
        }
        return best;
    }
}
