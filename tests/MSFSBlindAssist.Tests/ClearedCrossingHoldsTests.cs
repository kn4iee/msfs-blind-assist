// A runway crossing the pilot was cleared for (ClearedCrossingHolds): the automatic pass still places
// its stop [HLD-4]; clearing then takes that ONE stop off at that ONE place, keeps the label it was
// placed with, and can put it back. Never by runway name alone, never at another crossing of the same
// runway, never at a stop that is the pilot's own taxiway hold. Each test builds its route and runs the
// real pass, as RunwayHoldPlacementTests does.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;
using AircraftPosition = MSFSBlindAssist.Navigation.RouteRunwayCrossings.AircraftPosition;

namespace MSFSBlindAssist.Tests;

public class ClearedCrossingHoldsTests
{
    private static TaxiGraph.RunwayCenterline[] Runway09() => new[] { EastWest() };

    private static TaxiRoute Named(TaxiRoute route, string taxiway)
    {
        foreach (var s in route.Segments) s.TaxiwayName = taxiway;
        return route;
    }

    // Northbound on taxiway A across 09/27: the stop is the segment ending 100 m south of the centreline,
    // at route node 1 (node id firstId + 1).
    private static TaxiRoute AcrossOnA(int firstId = 1, double eastM = 500) => Named(RouteOf(
        Node(firstId, eastM, -300), Node(firstId + 1, eastM, -100), Node(firstId + 2, eastM, 0), Node(firstId + 3, eastM, 100)), "A");

    // KSFO's shape: 09/27 crossed twice, stops on Segments[0] (route node 1) and Segments[4] (route node 5).
    private static TaxiRoute TwoCrossings() => RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100),
        Node(5, 900, 300), Node(6, 900, 100), Node(7, 900, 0), Node(8, 900, -100));

    // No clear node before the runway: the route starts held, "runway 09".
    private static TaxiRoute StartsHeld() => RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));

    private static ClearedCrossingHolds.Outcome Clear(TaxiRoute route, params ClearedCrossing[] requests)
        => ClearedCrossingHolds.Apply(route, requests, recalculation: false);

    private static void AssertNothingReported(ClearedCrossingHolds.Outcome outcome)
    {
        Assert.Empty(outcome.NotOnRoute);
        Assert.Empty(outcome.Elsewhere);
        Assert.Equal("", ClearedCrossingHolds.WarningFor(outcome));
    }

    [Fact]
    public void An_event_records_the_node_its_stop_sits_at()
    {
        var single = AcrossOnA();
        Assert.Equal(1, Assert.Single(Pass(single, Runway09())).StopNodeIndex);

        var twice = TwoCrossings();
        Assert.Equal(new[] { 1, 5 }, Pass(twice, Runway09()).Select(e => e.StopNodeIndex));

        var started = StartsHeld();
        Assert.Equal(0, Assert.Single(Pass(started, Runway09())).StopNodeIndex);

        // Already passed (the aircraft 40 m past the stop), and no safe stop: nothing placed, -1.
        var passed = AcrossOnA();
        var passedEvent = Assert.Single(Pass(passed, Runway09(), aircraft: new AircraftPosition(Lat(-60), Lon(500))));
        Assert.True(passedEvent.Passed);
        Assert.Equal(-1, passedEvent.StopNodeIndex);
        var unheld = StartsHeld();
        Assert.Equal(-1, Assert.Single(Pass(unheld, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000)))).StopNodeIndex);

        // The pilot's own pick records it too.
        var runway = EastWest();
        var picked = AcrossOnA();
        Assert.Equal(RouteRunwayCrossings.UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            picked, runway, new[] { runway }, "27", runStartSegmentIndex: 0, placed: out var placed));
        Assert.Equal(1, placed!.StopNodeIndex);
    }

    [Fact]
    public void A_cleared_crossing_loses_its_stop_and_keeps_its_label()
    {
        var route = AcrossOnA();
        var ev = Assert.Single(Pass(route, Runway09()));

        var outcome = Clear(route, new ClearedCrossing("09"));

        var stop = route.Segments[0];
        Assert.False(stop.IsHoldShortPoint);
        Assert.Equal("", stop.HoldShortRunway);
        Assert.Equal("runway 09", stop.PlacedHoldLabel);
        Assert.Equal(new[] { "09" }, stop.ClearedRunways);
        Assert.True(ev.Cleared);
        Assert.True(ev.Held);
        Assert.Equal(stop.ToNode.NodeId, Assert.Single(outcome.Applied).StopNodeId);
        AssertNothingReported(outcome);

        Assert.True(ClearedCrossingHolds.IsFullyCleared(stop));
        Assert.Equal(0, ClearedCrossingHolds.NextClearedSegment(route, 0));
        Assert.Equal(-1, ClearedCrossingHolds.NextClearedSegment(route, 1));
        // Cleared, so no longer a runway with "no hold short point".
        Assert.Equal("", RouteRunwayCrossings.DescribeUnheldRunways(route.RunwayEvents));
    }

    [Fact]
    public void A_second_crossing_of_the_same_runway_keeps_its_stop()
    {
        var route = TwoCrossings();
        Pass(route, Runway09());

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.True(route.Segments[4].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[4].HoldShortRunway);
        Assert.Empty(route.Segments[4].ClearedRunways);
        Assert.Equal(new[] { true, false }, route.RunwayEvents.Select(e => e.Cleared));
        Assert.Equal(route.Segments[0].ToNode.NodeId, Assert.Single(outcome.Applied).StopNodeId);
        Assert.Equal(0, ClearedCrossingHolds.NextClearedSegment(route, 0));
        Assert.Equal(-1, ClearedCrossingHolds.NextClearedSegment(route, 1));
    }

    [Fact]
    public void The_reciprocal_name_clears_the_same_crossing()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);

        var outcome = Clear(route, new ClearedCrossing("27"));

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].PlacedHoldLabel);
        Assert.Equal(new[] { "09" }, route.Segments[0].ClearedRunways);
        Assert.Equal("27", Assert.Single(outcome.Applied).Runway);
        Assert.True(Assert.Single(route.RunwayEvents).Cleared);
    }

    [Fact]
    public void A_crossing_entered_after_a_later_taxiway_clears_the_crossing_on_that_taxiway()
    {
        // The taxiway step the crossing was entered after starts on segment 3, past the first crossing.
        var later = TwoCrossings();
        Pass(later, Runway09());
        ClearedCrossingHolds.Apply(later, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: seq => { Assert.Equal(1, seq); return 3; });
        Assert.True(later.Segments[0].IsHoldShortPoint);
        Assert.False(later.Segments[4].IsHoldShortPoint);

        // One that starts past both: nothing lies after it, so the first crossing is the one.
        var past = TwoCrossings();
        Pass(past, Runway09());
        ClearedCrossingHolds.Apply(past, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: _ => 6);
        Assert.False(past.Segments[0].IsHoldShortPoint);
        Assert.True(past.Segments[4].IsHoldShortPoint);
    }

    [Fact]
    public void At_a_stop_guarding_two_runways_only_the_named_one_is_cleared()
    {
        var (route, runways) = Intersection();
        Pass(route, runways);
        Assert.Equal("runway 09 and runway 01", route.Segments[0].HoldShortRunway);

        Clear(route, new ClearedCrossing("09"));

        var stop = route.Segments[0];
        Assert.True(stop.IsHoldShortPoint);
        Assert.Equal("runway 01", stop.HoldShortRunway);
        Assert.Empty(RunwayHoldStages.From(stop.HoldShortRunway));
        Assert.Equal("runway 09 and runway 01", stop.PlacedHoldLabel);
        Assert.Equal(new[] { "09" }, stop.ClearedRunways);
        Assert.False(ClearedCrossingHolds.IsFullyCleared(stop));
        Assert.True(route.RunwayEvents.Single(e => e.Designator == "09").Cleared);
        Assert.False(route.RunwayEvents.Single(e => e.Designator == "01").Cleared);
    }

    [Fact]
    public void A_crossing_named_at_another_place_is_not_cleared()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());

        var outcome = Clear(route, new ClearedCrossing("09", At: "Z"));

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);
        Assert.False(Assert.Single(route.RunwayEvents).Cleared);
        Assert.Empty(outcome.Applied);
        Assert.Empty(outcome.NotOnRoute);
        Assert.Equal("Z", Assert.Single(outcome.Elsewhere).At);
        const string expected = "Runway 09 is not crossed at Z. It is not cleared; you will be stopped there.";
        Assert.Equal(expected, ClearedCrossingHolds.WarningFor(outcome));
        Assert.Equal(expected, TestCulture.Run("de-DE", () => ClearedCrossingHolds.WarningFor(outcome)));
    }

    [Fact]
    public void A_crossing_named_at_its_own_taxiway_is_cleared()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());

        var outcome = Clear(route, new ClearedCrossing("09", At: "a"));

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.Single(outcome.Applied);
        AssertNothingReported(outcome);
    }

    [Fact]
    public void A_crossing_is_matched_by_the_stops_taxiway_its_locative_or_its_node()
    {
        // The scenery's hold line names its holding point: the event's place is "A5", the stop segment's taxiway "A".
        static TaxiRoute AtA5() => Named(RouteOf(Node(1, 500, -300), Node(2, 500, -105, TaxiNodeType.HoldShort, "runway 09 at A5"),
            Node(3, 500, -51), Node(4, 500, 0), Node(5, 500, 100)), "A");

        foreach (var request in new[] { new ClearedCrossing("09", At: "a"), new ClearedCrossing("09", At: "a5"), new ClearedCrossing("09", AtNodeId: 2) })
        {
            var route = AtA5();
            var ev = Assert.Single(Pass(route, Runway09()));
            Assert.Equal("A5", ev.Location);

            var outcome = Clear(route, request);

            Assert.False(route.Segments[0].IsHoldShortPoint);
            Assert.Equal("runway 09 at A5", route.Segments[0].PlacedHoldLabel);
            Assert.Equal(2, Assert.Single(outcome.Applied).StopNodeId);
        }

        var elsewhere = AtA5();
        Pass(elsewhere, Runway09());
        var missed = Clear(elsewhere, new ClearedCrossing("09", AtNodeId: 3));
        Assert.True(elsewhere.Segments[0].IsHoldShortPoint);
        Assert.Single(missed.Elsewhere);
        // No place in words to name: the same sentence without one.
        Assert.Equal("Runway 09 is not crossed there. It is not cleared; you will be stopped there.",
            TestCulture.Run("de-DE", () => ClearedCrossingHolds.WarningFor(missed)));
    }

    [Fact]
    public void A_runway_the_route_does_not_cross_is_reported()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());

        var outcome = Clear(route, new ClearedCrossing("18"));

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("18", Assert.Single(outcome.NotOnRoute).Runway);
        Assert.Empty(outcome.Applied);
        Assert.Empty(outcome.Elsewhere);
        const string expected = "The route does not cross runway 18.";
        Assert.Equal(expected, ClearedCrossingHolds.WarningFor(outcome));
        Assert.Equal(expected, TestCulture.Run("de-DE", () => ClearedCrossingHolds.WarningFor(outcome)));
    }

    [Fact]
    public void Both_warnings_are_said_one_sentence_per_request()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());

        var outcome = Clear(route, new ClearedCrossing("18"), new ClearedCrossing("09", At: "Z"));

        Assert.Equal("The route does not cross runway 18. Runway 09 is not crossed at Z. It is not cleared; you will be stopped there.",
            TestCulture.Run("de-DE", () => ClearedCrossingHolds.WarningFor(outcome)));
    }

    [Fact]
    public void A_crossing_already_passed_is_neither_cleared_nor_reported()
    {
        var route = Named(RouteOf(Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 09 at D5"),
            Node(3, 1000, 51), Node(4, 1000, 21), Node(5, 1000, -21), Node(6, 1000, -105)), "D");
        var ev = Assert.Single(Pass(route, Runway09(), aircraft: new AircraftPosition(Lat(51), Lon(1000))));
        Assert.True(ev.Passed);

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.Empty(outcome.Applied);
        AssertNothingReported(outcome);
        Assert.False(ev.Cleared);
    }

    [Theory]
    [InlineData("end of taxiway D", "end of taxiway D")]
    [InlineData("taxiway W", "runway 09 at taxiway W")]
    [InlineData("holding point B3", "runway 09 at holding point B3")]
    public void A_crossing_held_by_the_pilots_own_taxiway_stop_is_never_cleared(string pilotLabel, string sharedLabel)
    {
        // The pilot's own stop 51 m out lies between the scenery's hold line and the runway: the pass shares it.
        var route = Named(RouteOf(Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 09 at D5"),
            Node(3, 1000, 70), Node(4, 1000, 51), Node(5, 1000, 21), Node(6, 1000, -21), Node(7, 1000, -105)), "D");
        route.Segments[2].IsHoldShortPoint = true;
        route.Segments[2].HoldShortRunway = pilotLabel;
        var ev = Assert.Single(Pass(route, Runway09()));
        Assert.True(ev.Held);
        Assert.Equal(3, ev.StopNodeIndex);
        Assert.Equal(sharedLabel, route.Segments[2].HoldShortRunway);

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.True(route.Segments[2].IsHoldShortPoint);
        Assert.Equal(sharedLabel, route.Segments[2].HoldShortRunway);
        Assert.Null(route.Segments[2].PlacedHoldLabel);
        Assert.Empty(route.Segments[2].ClearedRunways);
        Assert.False(ev.Cleared);
        Assert.Empty(outcome.Applied);
        AssertNothingReported(outcome);
        Assert.False(ClearedCrossingHolds.ClearAt(route, 3, "09"));
    }

    [Fact]
    public void A_recalculation_clears_only_at_the_recorded_node()
    {
        var loaded = AcrossOnA();
        Pass(loaded, Runway09());
        var first = Clear(loaded, new ClearedCrossing("09", At: "A"));
        Assert.Equal(2, Assert.Single(first.Applied).StopNodeId);

        // The same nodes again: the stop comes back on the fresh route and is cleared again, at that node.
        var same = AcrossOnA();
        Pass(same, Runway09());
        Assert.True(same.Segments[0].IsHoldShortPoint);
        var again = ClearedCrossingHolds.Apply(same, first.Applied, recalculation: true);
        Assert.False(same.Segments[0].IsHoldShortPoint);
        Assert.Equal(2, Assert.Single(again.Applied).StopNodeId);
        Assert.Equal("", ClearedCrossingHolds.DroppedOnRecalculation(same, first.Applied, again));

        // A route that crosses 09 at other nodes: never carried there, and the pilot is told.
        var moved = AcrossOnA(firstId: 11, eastM: 700);
        Pass(moved, Runway09());
        var dropped = ClearedCrossingHolds.Apply(moved, first.Applied, recalculation: true);
        Assert.True(moved.Segments[0].IsHoldShortPoint);
        Assert.False(Assert.Single(moved.RunwayEvents).Cleared);
        Assert.Empty(dropped.Applied);
        Assert.Empty(dropped.NotOnRoute);
        Assert.Empty(dropped.Elsewhere);
        const string expected = "Runway 09 is a stop again.";
        Assert.Equal(expected, ClearedCrossingHolds.DroppedOnRecalculation(moved, first.Applied, dropped));
        Assert.Equal(expected, TestCulture.Run("de-DE", () => ClearedCrossingHolds.DroppedOnRecalculation(moved, first.Applied, dropped)));

        // A route that no longer crosses it at all: nothing to say.
        var away = RouteOf(Node(21, 500, -300), Node(22, 500, -100));
        Pass(away, Runway09());
        var none = ClearedCrossingHolds.Apply(away, first.Applied, recalculation: true);
        Assert.Empty(none.Applied);
        Assert.Empty(none.NotOnRoute);
        Assert.Equal("", ClearedCrossingHolds.DroppedOnRecalculation(away, first.Applied, none));
    }

    [Fact]
    public void A_start_hold_for_the_cleared_runway_is_removed()
    {
        var route = StartsHeld();
        var ev = Assert.Single(Pass(route, Runway09()));
        Assert.Equal("runway 09", route.StartHoldRunway);

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.Null(route.StartHoldRunway);
        Assert.True(ev.Cleared);
        Assert.Equal(route.Segments[0].FromNode.NodeId, Assert.Single(outcome.Applied).StopNodeId);
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void Restoring_puts_the_stop_and_its_label_back()
    {
        var route = AcrossOnA();
        var ev = Assert.Single(Pass(route, Runway09()));
        Clear(route, new ClearedCrossing("09"));

        Assert.True(ClearedCrossingHolds.RestoreAt(route, 0));

        var stop = route.Segments[0];
        Assert.True(stop.IsHoldShortPoint);
        Assert.Equal("runway 09", stop.HoldShortRunway);
        Assert.Empty(stop.ClearedRunways);
        Assert.Null(stop.PlacedHoldLabel);
        Assert.False(ev.Cleared);
        Assert.False(ClearedCrossingHolds.IsFullyCleared(stop));
        Assert.Equal(-1, ClearedCrossingHolds.NextClearedSegment(route, 0));

        // Nothing cleared there, or no such segment: nothing to restore.
        Assert.False(ClearedCrossingHolds.RestoreAt(route, 0));
        Assert.False(ClearedCrossingHolds.RestoreAt(route, 1));
        Assert.False(ClearedCrossingHolds.RestoreAt(route, 99));
        Assert.False(ClearedCrossingHolds.RestoreAt(route, -1));
    }

    [Fact]
    public void Restoring_a_shared_stop_asks_for_both_runways_again()
    {
        var (route, runways) = Intersection();
        Pass(route, runways);
        Clear(route, new ClearedCrossing("09"), new ClearedCrossing("01"));
        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.Equal(new[] { "09", "01" }, route.Segments[0].ClearedRunways);

        Assert.True(ClearedCrossingHolds.RestoreAt(route, 0));

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09 and runway 01", route.Segments[0].HoldShortRunway);
        Assert.Equal(new[] { "09", "01" }, RunwayHoldStages.From(route.Segments[0].HoldShortRunway));
        Assert.All(route.RunwayEvents, e => Assert.False(e.Cleared));
    }

    [Fact]
    public void ClearAt_refuses_a_stop_off_the_route_or_one_already_cleared()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());

        Assert.False(ClearedCrossingHolds.ClearAt(route, -1, "09"));
        Assert.False(ClearedCrossingHolds.ClearAt(route, 99, "09"));
        Assert.False(ClearedCrossingHolds.ClearAt(route, 4, "09"));      // one past the last node
        Assert.False(ClearedCrossingHolds.ClearAt(route, 2, "09"));      // not a hold short
        Assert.False(ClearedCrossingHolds.ClearAt(route, 0, "09"));      // no start hold
        Assert.False(ClearedCrossingHolds.ClearAt(route, 1, "18"));      // another runway's
        Assert.True(route.Segments[0].IsHoldShortPoint);

        Assert.True(ClearedCrossingHolds.ClearAt(route, 1, "09"));
        Assert.False(ClearedCrossingHolds.ClearAt(route, 1, "27"));
        Assert.Equal(new[] { "09" }, route.Segments[0].ClearedRunways);
    }

    [Theory]
    [InlineData("runway 09 and runway 01", new[] { "09" }, "runway 01")]
    [InlineData("runway 06L at D5 and runway 02", new[] { "06L" }, "runway 02")]
    [InlineData("runway 15R at N", new[] { "15R" }, null)]
    [InlineData("runway 09 and runway 01", new[] { "27" }, "runway 01")]
    [InlineData("D5, Runway 22R", new[] { "22R" }, null)]
    [InlineData("runway 09 and runway 01", new[] { "09", "01" }, null)]
    [InlineData("runway 09 and runway 01", new[] { "01" }, "runway 09")]
    [InlineData("runway 09", new[] { "18" }, "runway 09")]
    [InlineData("end of taxiway B", new[] { "09" }, "end of taxiway B")]
    public void RemainingLabel_removes_each_cleared_runways_part(string placed, string[] cleared, string? expected)
        => Assert.Equal(expected, ClearedCrossingHolds.RemainingLabel(placed, cleared));

    [Fact]
    public void The_log_line_names_what_was_cleared_and_what_was_not()
    {
        var none = Array.Empty<ClearedCrossing>();
        Assert.Equal("Cleared crossings: applied=09@7 notOnRoute=(none) elsewhere=(none)",
            ClearedCrossingHolds.DescribeForLog(new ClearedCrossingHolds.Outcome(new[] { new ClearedCrossing("09", StopNodeId: 7) }, none, none)));
        Assert.Equal("Cleared crossings: applied=(none) notOnRoute=18 elsewhere=09@Z",
            ClearedCrossingHolds.DescribeForLog(new ClearedCrossingHolds.Outcome(none, new[] { new ClearedCrossing("18") },
                new[] { new ClearedCrossing("09", At: "Z") })));
    }

    [Fact]
    public void A_crossing_with_no_stop_is_cleared_without_a_warning()
    {
        // #355's decision 3: a route loaded from inside 09's hold line (35 m out, inside the clear margin) crosses 09
        // with no stop of its own, and that is said as "no hold short point".
        var route = StartsHeld();
        var ev = Assert.Single(Pass(route, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000))));
        Assert.False(ev.Held);
        Assert.False(ev.Passed);
        Assert.Equal("no hold short point for runway 09", RouteRunwayCrossings.DescribeUnheldRunways(route.RunwayEvents));

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.Equal(0, Assert.Single(outcome.Applied).StopNodeId);
        Assert.True(ev.Cleared);
        AssertNothingReported(outcome);
        Assert.Equal("", RouteRunwayCrossings.DescribeUnheldRunways(route.RunwayEvents));
        Assert.Equal("crossing runway 09", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));

        // Named at another place: not cleared, and its "no hold short point" still stands.
        var other = StartsHeld();
        var otherEvent = Assert.Single(Pass(other, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000))));
        var missed = Clear(other, new ClearedCrossing("09", At: "Z"));
        Assert.Empty(missed.Applied);
        // Not "you will be stopped there": there is no stop. The "no hold short point" warning is what is said.
        AssertNothingReported(missed);
        Assert.False(otherEvent.Cleared);
        Assert.Equal("no hold short point for runway 09", RouteRunwayCrossings.DescribeUnheldRunways(other.RunwayEvents));
    }

    [Theory]
    [InlineData("C")]
    [InlineData("")]
    public void A_cleared_crossing_is_matched_on_the_route_a_Turn_pick_leaves(string connectorName)
    {
        // PR #355: left onto C from H builds an off-chart leg straight ahead across the corner [TURN-1]. The route
        // starts at H's north end, the only node before 04R clear of it, so its 04R stop is a start hold on H.
        var g = KbosHcJunctionFixture.Build(connectorName);
        var seq = new[] { "H", "C" };
        var r0 = TurnPickFixture.Route(g, KbosHcJunctionFixture.HNorth, KbosHcJunctionFixture.CEast, seq);
        var route = TurnPickApplier.Apply(r0, TurnPickFixture.Picks((1, TurnSide.Left)),
            TurnPickFixture.Ctx(g, seq, KbosHcJunctionFixture.CEast)).Route;
        int offChart = route.Segments.FindIndex(s => s.IsOffChartTurn);
        var pick = Assert.Single(route.TurnPicks);
        var pickIndices = (pick.FirstSegmentAfterDeparture, pick.FirstTaxiwaySegment);
        var segments = route.Segments.ToList();

        var ev = Assert.Single(Pass(route, g.RunwayCenterlines.ToArray()));
        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.Equal("04R", ev.Designator);
        Assert.Equal("H", ev.Location);
        Assert.Equal("runway 04R", route.StartHoldRunway);

        var outcome = Clear(route, new ClearedCrossing("04R"));

        Assert.Null(route.StartHoldRunway);
        Assert.True(ev.Cleared);
        Assert.Equal(KbosHcJunctionFixture.Node(g, KbosHcJunctionFixture.HNorth), Assert.Single(outcome.Applied).StopNodeId);
        Assert.Equal(segments, route.Segments);
        Assert.Equal(offChart, route.Segments.FindIndex(s => s.IsOffChartTurn));
        Assert.Equal(1, route.Segments.Count(s => s.IsOffChartTurn));
        var after = Assert.Single(route.TurnPicks);
        Assert.Equal(pickIndices, (after.FirstSegmentAfterDeparture, after.FirstTaxiwaySegment));
        Assert.True(route.Segments[after.FirstSegmentAfterDeparture].IsOffChartTurn);
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
    }
}
