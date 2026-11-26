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

    // Spec 8.6 with a runway in the way: "cross 24L, hold short of L", L just past 24L. A stop is captured 25 m
    // before it (CaptureRadiusM, mid-route and at a leg end alike), and a leg never ends, nor does BA ask for a
    // stop, on a runway (spec 8.5, 8.8): a stop is usable only when the route from 25 m before it to the junction
    // is clear. Here node 150 (k = 1) lies in the band and the setback point (170) too; node 190 (k = 2) is 5 m
    // past the band, but its approach (165-190) is not clear; the junction's own (205-230) is: the stop is the junction.
    [Fact] public void A_stop_that_would_fall_on_a_runway_moves_forward_to_the_first_clear_node()   // nodes at 0, 100, 150, 190, 230; runway 140-185
        => Assert.Equal(new Stop(StopKind.Junction, 3, 0), Choose(At(0, 100, 150, 190, 230), 3, 60, allowInsert: true, RunwayBetween(140, 185)));
    [Fact] public void A_recalculation_with_a_runway_in_the_way_also_stops_past_it()                // same layout, no insertion: the junction too
        => Assert.Equal(new Stop(StopKind.Junction, 3, 0), Choose(At(0, 100, 150, 190, 230), 3, 60, allowInsert: false, RunwayBetween(140, 185)));
    [Fact] public void A_node_just_past_the_margin_is_refused_when_its_approach_lies_on_the_runway()   // nodes at 0, 100, 195, 240; runway 140-185
    {
        // Node 195 (k = 1) is clear and 45 m before the junction, but its 25 m approach (170-195) crosses the band, and
        // so does the inserted point's (180-205): the junction, whose approach (215-240) is clear, is the stop.
        Assert.Equal(new Stop(StopKind.Junction, 2, 0), Choose(At(0, 100, 195, 240), 2, 35, allowInsert: false, RunwayBetween(140, 185)));
        Assert.Equal(new Stop(StopKind.Junction, 2, 0), Choose(At(0, 100, 195, 240), 2, 35, allowInsert: true, RunwayBetween(140, 185)));
    }
    [Fact] public void A_junction_whose_own_approach_lies_on_a_runway_gets_no_stop()                 // nodes at 0, 100, 200, 230; runway 140-210
        // The junction (230) is clear of the band, but the 25 m before it (205-230) is not: no point before it is usable.
        => Assert.Equal(new Stop(StopKind.NoSafeStop, -1, 0), Choose(At(0, 100, 200, 230), 2, 35, allowInsert: true, RunwayBetween(140, 210)));
    [Fact] public void Only_the_route_there_is_counts_before_a_stop_near_its_start()                 // nodes at 0, 15; runway 5-30 m behind the start
        => Assert.Equal(new Stop(StopKind.Junction, 0, 0), Choose(At(0, 15), 0, 35, allowInsert: true, RunwayBetween(-30, -5)));
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

    // Owner, 2026-11-25 ("Stop before the turn"): with or without a Turn pick, a taxiway held short of less than the
    // setback past the turn is held short of BEFORE the turn, on the taxiway before it, the whole aircraft clear of
    // it. Every taxiway stop is placed by Choose with no floor (Choose keeps its floor, above); a leg end goes through
    // ChooseForLegEnd, which moves a stop on, or at the far end of, a pick's link (an off-chart leg, a connector) back
    // to where that turn starts, so no leg ends on an off-chart leg or half-way through a turn.
    private static AppliedTurnPick Pick(int firstSegmentAfterDeparture, int firstTaxiwaySegment, bool offChart = false, int row = 1)
        => new() { RowIndex = row, Side = TurnSide.Left, TaxiwayName = "F",
                   FirstSegmentAfterDeparture = firstSegmentAfterDeparture, FirstTaxiwaySegment = firstTaxiwaySegment, IsOffChart = offChart };

    private static Stop LegEnd(List<TaxiRouteSegment> route, int junction, params AppliedTurnPick[] picks)
        => ChooseForLegEnd(route, junction, 35, allowInsert: true, Everywhere, picks);

    [Fact] public void With_a_pick_a_taxiway_too_close_past_the_turn_is_held_short_of_before_the_turn()
    {
        // Nodes at 0, 50, 100, 120: Z to the turn at 100, F from there, B meets F 20 m on. The floor put the stop at the
        // junction itself (the nose on B); now it is the node 70 m back on Z, as with no pick.
        var route = At(0, 50, 100, 120);
        Assert.Equal(new Stop(StopKind.Junction, 2, 0), Choose(route, 2, 35, allowInsert: true, Everywhere, firstUsableIndex: 2));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), LegEnd(route, 2, Pick(2, 2)));
        Assert.Equal(Choose(route, 2, 35, allowInsert: true, Everywhere), LegEnd(route, 2, Pick(2, 2)));
        // A point inserted on Z when no node is in reach: nodes at 0, 100, 120, the point 15 m before the turn.
        var inserted = LegEnd(At(0, 100, 120), 1, Pick(1, 1));
        Assert.Equal((StopKind.InsertOnSegment, 0), (inserted.Kind, inserted.SegmentIndex));
        Assert.Equal(15, inserted.MetresBeforeSegmentEnd, 6);   // setback minus a computed 20 m edge
        Assert.Equal(new Stop(StopKind.Junction, 1, 0), Choose(At(0, 100, 120), 1, 35, allowInsert: true, Everywhere, firstUsableIndex: 1));
    }

    [Fact] public void With_a_pick_a_taxiway_far_enough_past_the_turn_is_held_short_of_after_the_turn()   // nodes at 0, 100, 200
        => Assert.Equal(new Stop(StopKind.InsertOnSegment, 1, 35), LegEnd(At(0, 100, 200), 1, Pick(1, 1)));

    [Fact] public void With_no_pick_a_leg_end_is_placed_as_Choose_places_it()
    {
        Assert.Equal(Choose(At(0, 50, 100, 120), 2, 35, allowInsert: true, Everywhere), LegEnd(At(0, 50, 100, 120), 2));
        Assert.Equal(Choose(At(0, 130, 260), 1, 35, allowInsert: false, Everywhere),
                     ChooseForLegEnd(At(0, 130, 260), 1, 35, allowInsert: false, Everywhere, Array.Empty<AppliedTurnPick>()));
    }

    // An off-chart turn too close to the taxiway: the stop goes back across the off-chart leg (never split) to its
    // departure node, so the leg never ends on it. Nodes at 0, 100, 160, 180: the off-chart leg 100-160, F 160-180.
    [Fact] public void An_off_chart_turn_too_close_to_the_taxiway_is_held_short_of_before_it()
    {
        var route = At(0, 100, 160, 180); route[1].IsOffChartTurn = true;
        Assert.Equal(new Stop(StopKind.Junction, 2, 0), Choose(route, 2, 35, allowInsert: true, Everywhere, firstUsableIndex: 2));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), LegEnd(route, 2, Pick(1, 2, offChart: true)));
    }

    // ZSPD "C2, P2, left onto P2, hold short of C": C2 to 100, a 59.2 m off-chart leg, then P2 16.4, 17.1 and 16.5 m to C.
    // Choose takes the join, 50 m before C, the end of the off-chart leg: the leg would end on it. It ends where the turn
    // starts instead, the departure node at 100 (the floor's re-run inserted 1.1 m before a P2 node, failed in the sim's
    // graph and fell back to the junction: the aircraft stood 19 m from C).
    [Fact] public void A_leg_end_at_the_far_end_of_an_off_chart_leg_stops_where_the_turn_starts()
    {
        var route = At(0, 100, 159.2, 175.6, 192.7, 209.2); route[1].IsOffChartTurn = true;
        Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(route, 4, 34.8, allowInsert: true, Everywhere));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0),
                     ChooseForLegEnd(route, 4, 34.8, allowInsert: true, Everywhere, new[] { Pick(1, 2, offChart: true) }));
    }

    // B between the setback less its 1 m node tolerance and the setback past the join (34.5 m, setback 35): Choose's rule 1
    // takes the join; the floor's re-run used to land below the floor and fall to the junction. The departure node now.
    [Fact] public void A_join_within_the_node_tolerance_of_the_setback_also_stops_where_the_turn_starts()
    {
        var route = At(0, 100, 160, 194.5); route[1].IsOffChartTurn = true;
        Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(route, 2, 35, allowInsert: true, Everywhere));
        Assert.Equal(new Stop(StopKind.Junction, 2, 0), Choose(route, 2, 35, allowInsert: true, Everywhere, firstUsableIndex: 2));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), LegEnd(route, 2, Pick(1, 2, offChart: true)));
    }

    // Any row's turn, not only the last row's: here an earlier row's off-chart leg 100-160 onto Z, then a junction turn
    // onto F at 180, B 15 m on. Choose takes the earlier join at 160; the leg ends where that turn starts.
    [Fact] public void A_leg_end_at_an_earlier_rows_off_chart_join_stops_where_that_turn_starts()
    {
        var route = At(0, 100, 160, 180, 195); route[1].IsOffChartTurn = true;
        var earlier = Pick(1, 2, offChart: true, row: 1);
        var last = Pick(3, 3, row: 2);
        Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(route, 3, 35, allowInsert: true, Everywhere));
        Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), LegEnd(route, 3, last));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), LegEnd(route, 3, last, earlier));
    }

    // The departure node is taken only as Choose would take a node: its 25 m approach up to the junction clear of every
    // runway. A runway just before it (90-105) leaves no usable node before the turn: the ordinary stop, the join, which
    // Choose found usable, stands, and the pick with it (fix round 2: never "no stop", which kept the B junction itself).
    [Fact] public void A_runway_before_the_turn_start_keeps_the_ordinary_stop()
    {
        var route = At(0, 100, 160, 200); route[1].IsOffChartTurn = true;
        var pick = Pick(1, 2, offChart: true);
        Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(route, 2, 35, allowInsert: true, RunwayBetween(90, 105)));
        var placed = PlaceLegEnd(route, 2, 35, allowInsert: true, RunwayBetween(90, 105), new[] { pick });
        Assert.Equal(new LegEndStop(new Stop(StopKind.ExistingNode, 1, 0), pick, AtTurnStart: false), placed);
        Assert.Equal(placed.Stop, ChooseForLegEnd(route, 2, 35, allowInsert: true, RunwayBetween(90, 105), new[] { pick }));
    }

    // A first-row turn that departs from the route's own start (an already-made pick whose walk back reaches a stand's
    // unnamed lead-in, or a pick with nothing arriving at the turn): FirstSegmentAfterDeparture 0, no node before its link.
    // The ordinary stop, at the join 40 m before B, stands, and the pick with it.
    [Fact] public void A_first_row_turn_from_the_route_start_keeps_the_ordinary_stop()
    {
        var route = At(0, 60, 100); route[0].IsOffChartTurn = true;   // the off-chart leg from the start, F 60-100
        var pick = Pick(0, 1, offChart: true, row: 0);
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), Choose(route, 1, 35, allowInsert: true, Everywhere));
        Assert.Equal(new LegEndStop(new Stop(StopKind.ExistingNode, 0, 0), pick, AtTurnStart: false),
                     PlaceLegEnd(route, 1, 35, allowInsert: true, Everywhere, new[] { pick }));
        // A connector from the start (0-30-60), already made: the plain stop at its join stands too.
        var connector = At(0, 30, 60, 100);
        var made = Pick(0, 2, row: 0);
        var plain = Choose(connector, 2, 35, allowInsert: true, Everywhere);
        Assert.Equal(plain, ChooseForLegEnd(connector, 2, 35, allowInsert: true, Everywhere, new[] { made }));
        Assert.True(IsOnALink(plain, made));
    }

    // Only a stop Choose itself cannot place is no stop: the junction's own approach on a runway (175-210).
    [Fact] public void A_leg_end_has_no_stop_only_where_Choose_has_none()
    {
        var route = At(0, 100, 160, 200); route[1].IsOffChartTurn = true;
        Assert.Equal(new Stop(StopKind.NoSafeStop, -1, 0), Choose(route, 2, 35, allowInsert: true, RunwayBetween(175, 210)));
        Assert.Equal(new LegEndStop(new Stop(StopKind.NoSafeStop, -1, 0), null, AtTurnStart: false),
                     PlaceLegEnd(route, 2, 35, allowInsert: true, RunwayBetween(175, 210), new[] { Pick(1, 2, offChart: true) }));
    }

    // A move to the turn's start is reported, with the turn, so the manager keeps that end on a recalculation.
    [Fact] public void A_move_to_the_turn_start_is_reported_with_its_turn()
    {
        var route = At(0, 100, 160, 200); route[1].IsOffChartTurn = true;
        var pick = Pick(1, 2, offChart: true);
        Assert.Equal(new LegEndStop(new Stop(StopKind.ExistingNode, 0, 0), pick, AtTurnStart: true),
                     PlaceLegEnd(route, 2, 35, allowInsert: true, Everywhere, new[] { pick }));
        Assert.Equal(new LegEndStop(Choose(At(0, 100, 200), 1, 35, allowInsert: true, Everywhere), null, AtTurnStart: false),
                     PlaceLegEnd(At(0, 100, 200), 1, 35, allowInsert: true, Everywhere, new[] { Pick(1, 1) }));
    }

    // A recalculation keeps a leg end placed at a turn's start: it routes over the rows still ahead that lead to it, the
    // rows before the turn's own (rowsBefore = the pick's row), never past it.
    [Fact] public void A_recalculation_to_a_turn_start_follows_only_the_rows_before_the_turn()
    {
        Assert.Equal(new[] { "Y", "Z" }, RowsLeadingToTurnStart(new[] { "Y", "Z", "F" }, 3, 2));
        Assert.Equal(new[] { "Z" }, RowsLeadingToTurnStart(new[] { "Z", "F" }, 3, 2));
        Assert.Null(RowsLeadingToTurnStart(new[] { "F" }, 3, 2));      // the aircraft is past every row before the turn
        Assert.Null(RowsLeadingToTurnStart(null, 3, 2));
        Assert.Null(RowsLeadingToTurnStart(new[] { "Y", "Z", "F" }, 3, 0));
    }

    // A connector link: a stop on it short of the join (here a point Choose inserts 5 m before the connector's middle
    // node) also goes back to the departure node.
    [Fact] public void A_leg_end_on_a_connector_link_stops_where_the_turn_starts()
    {
        var route = At(0, 100, 150, 160, 180);   // Z to 100, an unnamed connector 100-150-160, F 160-180
        var plain = Choose(route, 3, 35, allowInsert: true, Everywhere);
        Assert.Equal((StopKind.InsertOnSegment, 1), (plain.Kind, plain.SegmentIndex));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), LegEnd(route, 3, Pick(1, 3)));
    }

    // A mid-route stop is placed by plain Choose, no floor and no exception: the route carries on after Continue, the
    // turn is made and its off-chart leg announced, so the join of an off-chart leg is a stop like any other.
    [Fact] public void A_mid_route_stop_at_an_off_chart_join_stays_where_Choose_puts_it()
    {
        var route = At(0, 100, 160, 200); route[1].IsOffChartTurn = true;
        Assert.Equal(new Stop(StopKind.ExistingNode, 1, 0), Choose(route, 2, 35, allowInsert: true, Everywhere));
        Assert.Equal(new Stop(StopKind.ExistingNode, 0, 0), LegEnd(route, 2, Pick(1, 2, offChart: true)));   // the leg end differs
    }

    [Fact] public void A_stop_on_a_link_is_one_on_or_at_the_far_end_of_it()
    {
        var linked = Pick(1, 3);   // departure node 1 (end of segment 0), a two-segment connector, join node 3 (end of segment 2)
        Assert.True(IsOnALink(new Stop(StopKind.ExistingNode, 2, 0), linked));       // the join, its far end
        Assert.True(IsOnALink(new Stop(StopKind.ExistingNode, 1, 0), linked));       // a connector node
        Assert.True(IsOnALink(new Stop(StopKind.InsertOnSegment, 2, 10), linked));   // a point on the connector
        Assert.True(IsOnALink(new Stop(StopKind.InsertOnSegment, 1, 10), linked));
        Assert.True(IsOnALink(new Stop(StopKind.Junction, 2, 0), linked));           // B met at the join itself
        Assert.False(IsOnALink(new Stop(StopKind.ExistingNode, 0, 0), linked));      // the departure node: before the turn
        Assert.False(IsOnALink(new Stop(StopKind.InsertOnSegment, 0, 10), linked));  // on Z
        Assert.False(IsOnALink(new Stop(StopKind.ExistingNode, 3, 0), linked));      // on the picked taxiway
        Assert.False(IsOnALink(new Stop(StopKind.NoSafeStop, -1, 0), linked));
        // A turn at a junction (departure node = join node) has no link: stopping at it is stopping before the turn.
        Assert.False(IsOnALink(new Stop(StopKind.ExistingNode, 1, 0), Pick(2, 2)));
    }

    // A runway crossed inside ONE edge has no node on it, so only sampling the stretch finds it (spec 8.6: move forward).
    // In the first two the band also reaches into the junction's own 25 m approach (235-260), so the junction is no
    // stop either (spec 8.5, 8.8): nothing is placed.
    [Fact] public void Rule_2_never_lands_behind_a_runway_crossed_inside_one_edge()    // nodes at 0, 100, 260; band 205-245; point at 225
        => Assert.Equal(new Stop(StopKind.NoSafeStop, -1, 0), Choose(At(0, 100, 260), 1, 35, allowInsert: true, RunwayBetween(205, 245)));
    [Fact] public void Rule_1_never_lands_behind_a_runway_crossed_inside_one_edge()    // nodes at 0, 190, 260; band 200-250; node 70 m back
        => Assert.Equal(new Stop(StopKind.NoSafeStop, -1, 0), Choose(At(0, 190, 260), 1, 35, allowInsert: true, RunwayBetween(200, 250)));
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
        seg.PlacedHoldLabel = "runway 09 and runway 01"; seg.ClearedRunways.Add("01");   // a stop with one runway cleared
        var (first, second) = TaxiwayHoldPlacement.Split(seg, RunwayFixture.Node(9, 65, 0), 35);
        Assert.Equal(65, first.DistanceMeters, 1); Assert.Equal(9, first.ToNode.NodeId); Assert.False(first.IsHoldShortPoint);
        Assert.Equal(35, second.DistanceMeters, 1); Assert.Equal(9, second.FromNode.NodeId); Assert.True(second.IsHoldShortPoint);
        Assert.Equal("runway 09", second.HoldShortRunway); Assert.Equal("A", first.TaxiwayName); Assert.Equal("A", second.TaxiwayName);
        Assert.Equal("left", first.TurnDirection); Assert.Equal(-90, first.TurnAngleDegrees);
        Assert.Equal("straight", second.TurnDirection); Assert.Equal(0, second.TurnAngleDegrees);
        // The cleared runways and the placed label go with the stop, onto the far half, as copies.
        Assert.Null(first.PlacedHoldLabel); Assert.Empty(first.ClearedRunways);
        Assert.Equal("runway 09 and runway 01", second.PlacedHoldLabel); Assert.Equal(new[] { "01" }, second.ClearedRunways);
        Assert.NotSame(seg.ClearedRunways, second.ClearedRunways);
    }

    // Node n of a route is the end node of segment n - 1 (node 0 its first): splitting segment m makes every node
    // after m one further along, its own end node (n = m + 1, where the far half keeps the stop) included.
    [Fact] public void A_split_keeps_an_events_stop_on_the_same_node()
    {
        static int NodeIdAt(TaxiRoute route, int n) => n == 0 ? route.Segments[0].FromNode.NodeId : route.Segments[n - 1].ToNode.NodeId;

        // Northbound across 09/27: the stop is the segment ending 100 m short of the centreline, route node 2 (id 3).
        static (TaxiRoute Route, List<TaxiRouteRunwayEvent> Events) Crossing()
        {
            var route = RunwayFixture.RouteOf(RunwayFixture.Node(1, 500, -400), RunwayFixture.Node(2, 500, -300),
                RunwayFixture.Node(3, 500, -100), RunwayFixture.Node(4, 500, 0), RunwayFixture.Node(5, 500, 100));
            var events = RunwayFixture.Pass(route, new[] { RunwayFixture.EastWest() }).ToList();
            return (route, events);
        }

        static void SplitAt(TaxiRoute route, List<TaxiRouteRunwayEvent> events, int m)
        {
            var seg = route.Segments[m];
            var mid = new TaxiNode { NodeId = 99, Latitude = (seg.FromNode.Latitude + seg.ToNode.Latitude) / 2,
                                     Longitude = (seg.FromNode.Longitude + seg.ToNode.Longitude) / 2 };
            var (first, second) = TaxiwayHoldPlacement.Split(seg, mid, seg.DistanceMeters / 2);
            route.Segments[m] = first;
            route.Segments.Insert(m + 1, second);
            TaxiwayHoldPlacement.ShiftEventsAfterSplit(events, m);
        }

        // The route reaches the runway at node 3 (id 4), the end of segment 2.
        foreach (var (m, expected, expectedReach) in new[] { (0, 3, 4), (1, 3, 4), (2, 2, 4), (3, 2, 3) })
        {
            var (route, events) = Crossing();
            var ev = Assert.Single(events);
            Assert.Equal(2, ev.StopNodeIndex);
            Assert.Equal(3, ev.ReachNodeIndex);
            ev.Cleared = true;

            SplitAt(route, events, m);

            var moved = Assert.Single(events);
            Assert.Equal(expected, moved.StopNodeIndex);
            Assert.Equal(3, NodeIdAt(route, moved.StopNodeIndex));
            Assert.True(route.Segments[moved.StopNodeIndex - 1].IsHoldShortPoint);
            Assert.Equal(expectedReach, moved.ReachNodeIndex);
            Assert.Equal(4, NodeIdAt(route, moved.ReachNodeIndex));
            // Every other member rides along.
            Assert.Equal((ev.Kind, ev.Designator, ev.Held, ev.Passed, ev.Location, ev.Cleared),
                         (moved.Kind, moved.Designator, moved.Held, moved.Passed, moved.Location, moved.Cleared));
        }

        // A start hold (0) and no stop at all (-1) never move; nor does a reach at the route's first node.
        var unmoved = new List<TaxiRouteRunwayEvent>
            { new() { StopNodeIndex = 0, ReachNodeIndex = 0 }, new() { StopNodeIndex = -1, ReachNodeIndex = 2 }, new() };
        TaxiwayHoldPlacement.ShiftEventsAfterSplit(unmoved, 0);
        Assert.Equal(new[] { 0, -1, -1 }, unmoved.Select(e => e.StopNodeIndex));
        Assert.Equal(new[] { 0, 3, -1 }, unmoved.Select(e => e.ReachNodeIndex));
        TaxiwayHoldPlacement.ShiftEventsAfterSplit(null, 0);
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
