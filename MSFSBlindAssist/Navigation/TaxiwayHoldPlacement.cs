using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Where a "hold short of a taxiway" stop goes on a route: set back from the junction so the aircraft
/// stays off the taxiway, never on or inside a runway's clear margin, never splitting an off-chart leg,
/// never before a picked turn. Pure.
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
    public static Stop Choose(IReadOnlyList<TaxiRouteSegment> segments, int junctionSegmentIndex, double setbackM, bool allowInsert,
                              Func<double, double, bool> isClearOfRunways, int firstUsableIndex = 0)
    {
        int j = junctionSegmentIndex;
        var junctionNode = segments[j].ToNode;
        if (!isClearOfRunways(junctionNode.Latitude, junctionNode.Longitude))
            return new Stop(StopKind.NoSafeStop, -1, 0);

        // lo: the smallest index whose end node, and every one after it up to the junction, is clear of runways.
        int lo = j;
        while (lo > 0 && isClearOfRunways(segments[lo - 1].ToNode.Latitude, segments[lo - 1].ToNode.Longitude)) lo--;
        lo = Math.Max(lo, firstUsableIndex);

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
            if (kFound >= lo) return new Stop(StopKind.ExistingNode, kFound, 0);
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
                        double fromStart = 0;
                        for (int i = 0; i <= m; i++) fromStart += segments[i].DistanceMeters;
                        fromStart -= metresBeforeEnd;
                        if (fromStart >= MinFromRouteStartM)
                        {
                            double f = seg.DistanceMeters > 0 ? (seg.DistanceMeters - metresBeforeEnd) / seg.DistanceMeters : 0;
                            double lat = seg.FromNode.Latitude + f * (seg.ToNode.Latitude - seg.FromNode.Latitude);
                            double lon = seg.FromNode.Longitude + f * (seg.ToNode.Longitude - seg.FromNode.Longitude);
                            if (m >= lo && isClearOfRunways(lat, lon))
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
            if (kFound >= lo) return new Stop(StopKind.ExistingNode, kFound, 0);
            passedOver = true;
        }

        if (passedOver)
            return lo >= j ? new Stop(StopKind.Junction, j, 0) : new Stop(StopKind.ExistingNode, lo, 0);

        // Rule 4.
        return new Stop(StopKind.Junction, j, 0);
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
            IsUncharted = segment.IsUncharted,
            IsOffChartTurn = segment.IsOffChartTurn,
        };
        return (first, second);
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
