using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Navigation.TaxiwayHoldPlacement;

namespace MSFSBlindAssist.Tests;

public class TaxiwayHoldPlacementTests
{
    // Nodes lie on the east axis: node i is at eastM[i].
    private static List<TaxiRouteSegment> At(params double[] eastM)
        => RunwayFixture.Route(eastM.Select((e, i) => RunwayFixture.Node(i, e, 0)).ToArray());

    private static bool Everywhere(double lat, double lon) => true;

    // False for a longitude between those of the two east offsets, true elsewhere.
    private static Func<double, double, bool> RunwayBetween(double fromEastM, double toEastM)
    {
        double lo = RunwayFixture.Node(98, fromEastM, 0).Longitude;
        double hi = RunwayFixture.Node(99, toEastM, 0).Longitude;
        return (lat, lon) => !(lon >= lo && lon <= hi);
    }

    [Theory]
    [InlineData(35.8, 34.69)] [InlineData(0.0, 34.8)] [InlineData(10.0, 25.0)] [InlineData(100.0, 60.0)] [InlineData(64.8, 50.64)]
    public void Setback_follows_the_wingspan_within_its_limits(double span, double expected)
        => Assert.Equal(expected, TaxiwayHoldPlacement.SetbackMetres(span), 2);

    [Fact] public void An_existing_node_just_beyond_the_setback_is_used()            // nodes at 0, 100, 220, 260
        => Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(At(0, 100, 220, 260), 2, 35, allowInsert: true, Everywhere));
    [Fact] public void A_long_edge_gets_a_point_exactly_the_setback_back()          // nodes at 0, 100, 260
        => Assert.Equal(new Stop(StopKind.InsertOnSegment, 1, 35), Choose(At(0, 100, 260), 1, 35, allowInsert: true, Everywhere));
    [Fact] public void The_point_can_fall_on_an_earlier_edge()                       // nodes at 0, 250, 260
    {
        var stop = Choose(At(0, 250, 260), 1, 35, allowInsert: true, Everywhere);
        Assert.Equal(StopKind.InsertOnSegment, stop.Kind);
        Assert.Equal(0, stop.SegmentIndex);
        Assert.Equal(25, stop.MetresBeforeSegmentEnd, 6);   // setback minus a computed 10 m edge: not exactly 25
    }
    [Fact] public void A_recalculation_inserts_nothing_and_uses_a_node_within_150_metres()   // nodes at 0, 130, 260
        => Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), Choose(At(0, 130, 260), 1, 35, allowInsert: false, Everywhere));
    [Fact] public void A_recalculation_with_no_node_in_reach_keeps_the_junction()   // nodes at 0, 100, 260
        => Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(At(0, 100, 260), 1, 35, allowInsert: false, Everywhere));
    [Fact] public void A_route_shorter_than_the_setback_keeps_the_junction()        // nodes at 0, 30
        => Assert.Equal(new Stop(StopKind.Junction, 0, 0), Choose(At(0, 30), 0, 35, allowInsert: true, Everywhere));
    [Fact] public void Nothing_is_inserted_behind_or_at_the_aircraft()              // nodes at 0, 40: the point is 5 m from the start
        => Assert.Equal(new Stop(StopKind.Junction, 0, 0), Choose(At(0, 40), 0, 35, allowInsert: true, Everywhere));

    // Spec 8.6 with a runway in the way: "cross 24L, hold short of L", L just past 24L.
    [Fact] public void A_stop_that_would_fall_on_a_runway_moves_forward_to_the_first_clear_node()   // nodes at 0, 100, 150, 190, 230; runway 140-185
        => Assert.Equal(new Stop(StopKind.ExistingNode, 2, 0), Choose(At(0, 100, 150, 190, 230), 3, 60, allowInsert: true, RunwayBetween(140, 185)));
    [Fact] public void A_recalculation_with_a_runway_in_the_way_also_stops_past_it()                // same layout, no insertion
        => Assert.Equal(new Stop(StopKind.ExistingNode, 2, 0), Choose(At(0, 100, 150, 190, 230), 3, 60, allowInsert: false, RunwayBetween(140, 185)));
    [Fact] public void A_runway_behind_the_setback_point_changes_nothing()                           // nodes at 0, 100, 220, 260; runway 20-60
        => Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(At(0, 100, 220, 260), 2, 35, allowInsert: true, RunwayBetween(20, 60)));
    [Fact] public void A_junction_inside_a_runway_margin_gets_no_stop()                               // nodes at 0, 100, 200; runway 180-260
        => Assert.Equal(new Stop(StopKind.NoSafeStop, -1, 0), Choose(At(0, 100, 200), 1, 35, allowInsert: true, RunwayBetween(180, 260)));

    // PR #355: an off-chart leg has no graph edge to split, and a stop never cuts off a picked turn.
    [Fact] public void An_off_chart_leg_is_never_split()                                  // nodes at 0, 100, 260; segment 1 is an off-chart leg
    {
        var far = At(0, 100, 260); far[1].IsOffChartTurn = true;
        Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(far, 1, 35, allowInsert: true, Everywhere));
        var near = At(0, 150, 260); near[1].IsOffChartTurn = true;                         // a node 110 m back is in reach
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), Choose(near, 1, 35, allowInsert: true, Everywhere));
    }
    [Fact] public void The_stop_never_moves_back_before_the_picked_turn()                 // nodes at 0, 100, 130, 200; first picked-taxiway segment 2
        => Assert.Equal(new Stop(StopKind.InsertOnSegment, 2, 35), Choose(At(0, 100, 130, 200), 2, 35, allowInsert: true, Everywhere, firstUsableIndex: 2));
    [Fact] public void A_turn_too_close_to_the_taxiway_keeps_the_junction()               // nodes at 0, 100, 120; first picked-taxiway segment 1
        => Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(At(0, 100, 120), 1, 35, allowInsert: true, Everywhere, firstUsableIndex: 1));
    [Fact] public void A_recalculation_finds_the_node_inserted_at_load()                  // nodes at 0, 100, 225.4, 260: 34.6 m back
        => Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(At(0, 100, 225.4, 260), 2, 35, allowInsert: false, Everywhere));

    // A runway crossed inside ONE edge has no node on it, so only sampling the stretch finds it (spec 8.6: move forward).
    [Fact] public void Rule_2_never_lands_behind_a_runway_crossed_inside_one_edge()    // nodes at 0, 100, 260; band 205-245; point at 225
        => Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(At(0, 100, 260), 1, 35, allowInsert: true, RunwayBetween(205, 245)));
    [Fact] public void Rule_1_never_lands_behind_a_runway_crossed_inside_one_edge()    // nodes at 0, 190, 260; band 200-250; node 70 m back
        => Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(At(0, 190, 260), 1, 35, allowInsert: true, RunwayBetween(200, 250)));
    [Fact] public void Rule_3_never_lands_behind_a_runway_crossed_inside_one_edge()    // nodes at 0, 150, 260; band 180-230; node 110 m back
        => Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(At(0, 150, 260), 1, 60, allowInsert: false, RunwayBetween(180, 230)));
    [Fact] public void An_inserted_point_whose_stretch_to_the_segment_end_crosses_a_runway_is_refused()
        // nodes at 0, 300, 340; setback 120 puts the point at 220 on segment 0 (itself clear), but the band 240-260 lies
        // between it and the segment's end node at 300: the stop moves forward to that node.
        => Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), Choose(At(0, 300, 340), 1, 120, allowInsert: true, RunwayBetween(240, 260)));

    [Fact] public void Split_keeps_the_stop_on_the_far_half()
    {
        var seg = At(0, 100)[0]; seg.TaxiwayName = "A"; seg.IsHoldShortPoint = true; seg.HoldShortRunway = "runway 09";
        seg.TurnDirection = "left"; seg.TurnAngleDegrees = -90;
        var (first, second) = TaxiwayHoldPlacement.Split(seg, RunwayFixture.Node(9, 65, 0), 35);
        Assert.Equal(65, first.DistanceMeters, 1); Assert.Equal(9, first.ToNode.NodeId); Assert.False(first.IsHoldShortPoint);
        Assert.Equal(35, second.DistanceMeters, 1); Assert.Equal(9, second.FromNode.NodeId); Assert.True(second.IsHoldShortPoint);
        Assert.Equal("runway 09", second.HoldShortRunway); Assert.Equal("A", first.TaxiwayName); Assert.Equal("A", second.TaxiwayName);
        Assert.Equal("left", first.TurnDirection); Assert.Equal(-90, first.TurnAngleDegrees);
        Assert.Equal("straight", second.TurnDirection); Assert.Equal(0, second.TurnAngleDegrees);
    }

    [Fact] public void A_split_before_a_pick_moves_its_segment_indices_up()
    {
        var pick = new AppliedTurnPick { RowIndex = 1, FirstSegmentAfterDeparture = 3, FirstTaxiwaySegment = 4, IsOffChart = true, OffChartLengthMeters = 60 };
        var moved = TaxiwayHoldPlacement.ShiftPicksAfterSplit(new[] { pick }, 1).Single();
        Assert.Equal((4, 5), (moved.FirstSegmentAfterDeparture, moved.FirstTaxiwaySegment));
        Assert.True(moved.IsOffChart); Assert.Equal(60, moved.OffChartLengthMeters);
        var onTheTaxiway = TaxiwayHoldPlacement.ShiftPicksAfterSplit(new[] { pick }, 4).Single();   // the first half keeps index 4
        Assert.Equal((3, 4), (onTheTaxiway.FirstSegmentAfterDeparture, onTheTaxiway.FirstTaxiwaySegment));
    }

    [Fact] public void The_nearest_junction_by_taxi_distance_wins()
    {
        Assert.Equal(9, TaxiwayHoldPlacement.NearestByTaxiDistance(new[] { 5, 9 }, new Dictionary<int, double> { [5] = 300, [9] = 120 }));
        Assert.Equal(5, TaxiwayHoldPlacement.NearestByTaxiDistance(new[] { 5, 9 }, new Dictionary<int, double> { [5] = 300 }));
        Assert.Equal(5, TaxiwayHoldPlacement.NearestByTaxiDistance(new[] { 9, 5 }, new Dictionary<int, double> { [5] = 120, [9] = 120 }));
        Assert.Equal(-1, TaxiwayHoldPlacement.NearestByTaxiDistance(Array.Empty<int>(), new Dictionary<int, double>()));
    }
}
