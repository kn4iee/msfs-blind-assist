// The end of a Progressive Taxi leg judged BEFORE the cursor reaches the route's last segment. Since the merge of main's
// #370 the leg end is counted down along the route, so its "Slow down" and "Stop." can come while the cursor is still
// on an earlier segment (the last pieces short). The arrival was still asked only on the last segment, so a pilot who
// stopped where BA said "Slow down" got no leg end and nothing said: VirtualPilot legends with the pilot braking on the
// leg end's "Slow down" (VP_LEG_BRAKE_ON_SLOWDOWN=1, 40 airports, 3 per airport): 96 legs never ended, LTFM "via N5 hold
// short of T9" standing 47 m short of its end on the 24.6 m piece before a 7.2 m last one. The guide's promise (taxi
// guidance, Progressive Taxi: "also ends when you stop at any point after Slow down ... you do not need to creep up to
// the end") and [STR-25].
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the test to match the output.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ProgressiveLegEndAlongRouteTests
{
    private const double LookaheadM = 600.0 / 3.28084;   // HOLD_SHORT_COUNTDOWN_LOOKAHEAD_M, the outer tier's cap
    private const ProgressiveTerminatorType Twy = ProgressiveTerminatorType.HoldShortTaxiway;
    private const ProgressiveTerminatorType EndOfTwy = ProgressiveTerminatorType.EndOfTaxiway;

    // Due east along 10° N from 30° E.
    private const double Lat = 10.0, Lon0 = 30.0;
    private static double MetresPerDegreeLon => 111_320.0 * Math.Cos(Lat * Math.PI / 180.0);
    private static double LonAt(double metres) => Lon0 + metres / MetresPerDegreeLon;

    private static int _nodeId = 1;

    // A leg "via L, hold short of taxiway M" whose stop ends pieces of 94, 41, 60, 28.0, 6.4 and 2.0 m, due east.
    private static List<TaxiRouteSegment> Leg(params double[] pieces)
    {
        if (pieces.Length == 0) pieces = new[] { 94.0, 41.0, 60.0, 28.0, 6.4, 2.0 };
        var segments = new List<TaxiRouteSegment>();
        double at = 0.0;
        var from = new TaxiNode { NodeId = _nodeId++, Latitude = Lat, Longitude = LonAt(0.0) };
        foreach (double m in pieces)
        {
            at += m;
            var to = new TaxiNode { NodeId = _nodeId++, Latitude = Lat, Longitude = LonAt(at) };
            segments.Add(new TaxiRouteSegment
            {
                FromNode = from, ToNode = to, TaxiwayName = "L", DistanceMeters = m,
                BearingDegrees = 90.0, TurnDirection = "straight",
            });
            from = to;
        }
        return segments;
    }

    // The aircraft on segment `index`, `toEndM` before its end, on the route's line.
    private static (double Lat, double Lon) On(List<TaxiRouteSegment> route, int index, double toEndM)
        => (Lat, LonAt(route.Take(index + 1).Sum(s => s.DistanceMeters) - toEndM));

    private static bool EndedBefore(List<TaxiRouteSegment> route, int index, double toEndM, double groundSpeedKts,
        bool slowDownSpoken, ProgressiveTerminatorType type = Twy, IReadOnlyList<TaxiGraph.RunwayCenterline>? runways = null)
    {
        var (lat, lon) = On(route, index, toEndM);
        double? along = ProgressiveLegEnd.DistanceAlongTheRoute(route, index, toEndM, LookaheadM);
        Assert.NotNull(along);
        return ProgressiveLegEnd.ArrivedBeforeLastSegment(type, route, index, lat, lon, along.Value, groundSpeedKts,
            slowDownSpoken, nearARunway: false, onRunwayPavement: false, runways);
    }

    [Fact]
    public void The_leg_end_is_measured_along_the_route_from_a_segment_before_its_last()
    {
        // On the 28 m piece, 20 m from its end: 20 + 6.4 + 2.0 to the end of the leg.
        Assert.Equal(28.4, ProgressiveLegEnd.DistanceAlongTheRoute(Leg(), 3, 20.0, LookaheadM)!.Value, 6);
        // On the 60 m piece, 35 m from its end.
        Assert.Equal(71.4, ProgressiveLegEnd.DistanceAlongTheRoute(Leg(), 2, 35.0, LookaheadM)!.Value, 6);
    }

    [Fact]
    public void A_pilot_who_stops_after_the_leg_end_s_slow_down_before_its_last_segment_ends_the_leg()
        => Assert.True(EndedBefore(Leg(), 2, 35.0, groundSpeedKts: 0.0, slowDownSpoken: true));

    [Fact]
    public void A_stop_further_out_before_the_slow_down_does_not_end_the_leg()
        => Assert.False(EndedBefore(Leg(), 2, 35.0, groundSpeedKts: 0.0, slowDownSpoken: false));

    [Fact]
    public void A_leg_rolled_through_inside_25_m_ends_before_its_last_segment_too()
    {
        // 13.6 + 6.4 + 2.0 = 22 m along the route, at taxi speed.
        Assert.True(EndedBefore(Leg(), 3, 13.6, groundSpeedKts: 12.0, slowDownSpoken: false));
        // 40 + 6.4 + 2.0 = 48.4 m, still rolling: not yet.
        Assert.False(EndedBefore(Leg(), 3, 40.0, groundSpeedKts: 12.0, slowDownSpoken: true));
    }

    // The two endings that are not a hold end stopped within 30 m, never on "Slow down" alone (ProgressiveLegEnd.Arrived).
    [Fact]
    public void A_leg_to_the_end_of_a_taxiway_ends_stopped_within_30_m_before_its_last_segment()
    {
        Assert.True(EndedBefore(Leg(), 3, 20.0, groundSpeedKts: 0.0, slowDownSpoken: false, EndOfTwy));
        Assert.False(EndedBefore(Leg(), 2, 35.0, groundSpeedKts: 0.0, slowDownSpoken: true, EndOfTwy));
    }

    // The last segment is the arrival block's own: measured straight to its end, with the past-the-node backstop.
    [Fact]
    public void On_the_last_segment_the_early_test_never_runs()
    {
        var route = Leg();
        var (lat, lon) = On(route, 5, 1.0);
        Assert.False(ProgressiveLegEnd.ArrivedBeforeLastSegment(Twy, route, 5, lat, lon, 1.0, 0.0, true, false, false, null));
    }

    // Only while the leg end is the countdown's next stop: a hold short before it is stopped at first, and so is a line
    // the pilot was cleared across, which is said where it is passed.
    [Fact]
    public void A_hold_short_or_a_cleared_line_before_the_leg_end_comes_first()
    {
        var held = Leg();
        held[4].IsHoldShortPoint = true;
        held[4].HoldShortRunway = "runway 04L at L";
        Assert.Null(ProgressiveLegEnd.DistanceAlongTheRoute(held, 3, 20.0, LookaheadM));

        var cleared = Leg();
        cleared[4].IsHoldShortPoint = true;
        cleared[4].HoldShortRunway = "runway 04L at L";
        Assert.True(ClearedCrossingHolds.ClearAt(new TaxiRoute { Segments = cleared }, 5, "04L"));
        Assert.True(ClearedCrossingHolds.IsFullyCleared(cleared[4]));
        Assert.Null(ProgressiveLegEnd.DistanceAlongTheRoute(cleared, 3, 20.0, LookaheadM));
    }

    [Fact]
    public void A_leg_end_beyond_the_countdown_s_reach_is_not_judged()
        => Assert.Null(ProgressiveLegEnd.DistanceAlongTheRoute(Leg(), 0, 90.0, LookaheadM));

    // The leg end's own "Slow down": the countdown's latch only while it belongs to the route's last node, never a hold
    // short's or a cleared line's before it.
    [Fact]
    public void Only_the_leg_end_s_own_slow_down_counts()
    {
        var route = Leg();
        Assert.True(ProgressiveLegEnd.SlowDownSaidForIt(route, slowDownLatched: true, route[^1].ToNode.NodeId));
        Assert.False(ProgressiveLegEnd.SlowDownSaidForIt(route, slowDownLatched: true, route[3].ToNode.NodeId));
        Assert.False(ProgressiveLegEnd.SlowDownSaidForIt(route, slowDownLatched: false, route[^1].ToNode.NodeId));
    }

    // BA never ends a leg short of a runway the route has still to cross (the crossthenhold shape: past the line it was
    // cleared across, not yet on the pavement): the end sentence would hold the pilot inside that runway's line.
    [Fact]
    public void A_runway_still_to_cross_before_the_leg_end_holds_the_early_end_off()
    {
        // A runway running north-south across the 60 m piece, 45 m wide.
        var route = Leg();
        double crossAt = 94.0 + 41.0 + 30.0;
        var runway = new TaxiGraph.RunwayCenterline
        {
            Name1 = "36", Name2 = "18",
            Lat1 = Lat - 0.02, Lon1 = LonAt(crossAt), Lat2 = Lat + 0.02, Lon2 = LonAt(crossAt),
            HeadingDeg1 = 0.0, HalfWidthMeters = 22.5,
        };
        var runways = new[] { runway };

        // On the 41 m piece, 35 m from its end (131.4 m from the leg end), stopped after the leg end's "Slow down".
        Assert.False(EndedBefore(route, 1, 35.0, 0.0, slowDownSpoken: true, Twy, runways));
        Assert.True(EndedBefore(route, 1, 35.0, 0.0, slowDownSpoken: true, Twy, runways: null));
        // Across it, on the 28 m piece: nothing left to cross, the leg ends.
        Assert.True(EndedBefore(route, 3, 20.0, 0.0, slowDownSpoken: true, Twy, runways));
    }
}
