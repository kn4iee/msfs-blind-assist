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

    // The scenery's hold line names its holding point: the event's place is "A5", the stop segment's taxiway "A". The
    // stop is node 2 (route node 1); node 3 lies between it and the runway, which the route reaches at node 4 (route
    // node 3); node 5 is past the runway.
    private static TaxiRoute AtA5() => Named(RouteOf(Node(1, 500, -300), Node(2, 500, -105, TaxiNodeType.HoldShort, "runway 09 at A5"),
        Node(3, 500, -51), Node(4, 500, 0), Node(5, 500, 100)), "A");

    // TwoCrossings with the pilot's own taxiway stop where the pass places the stop for one crossing (0: the first, 4:
    // the second), so the pass shares it as "runway 09 at taxiway W".
    private static TaxiRoute TwoCrossingsPilotHeld(int segmentIndex)
    {
        var route = TwoCrossings();
        route.Segments[segmentIndex].IsHoldShortPoint = true;
        route.Segments[segmentIndex].HoldShortRunway = "taxiway W";
        return route;
    }

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

        // One that starts past both: nothing lies after it, so the first crossing is the one. The second crossing
        // reaches the runway at node 6, where segment 6 starts, so a taxiway starting past both starts at node 7.
        var past = TwoCrossings();
        Pass(past, Runway09());
        ClearedCrossingHolds.Apply(past, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: _ => 7);
        Assert.False(past.Segments[0].IsHoldShortPoint);
        Assert.True(past.Segments[4].IsHoldShortPoint);
    }

    [Fact]
    public void A_crossing_entered_after_a_taxiway_is_matched_where_the_route_reaches_the_runway_on_it()
    {
        // KSFO-like: D crosses 28R (stop at node 1), comes round and meets Q at a junction inside 28R's clear margin,
        // and Q crosses 28R again. Q's stop is set back onto D (node 5), before Q's first segment (6); the route
        // reaches the runway on Q itself (node 7). Cleared "after Q", it is Q's crossing that is cleared, never D's.
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100),
            Node(5, 700, 200), Node(6, 900, 100), Node(7, 900, 35), Node(8, 900, 0), Node(9, 900, -100));
        for (int i = 0; i < route.Segments.Count; i++) route.Segments[i].TaxiwayName = i < 6 ? "D" : "Q";
        var events = Pass(route, new[] { EastWest("10L", "28R") });
        Assert.Equal(new[] { 1, 5 }, events.Select(e => e.StopNodeIndex));
        Assert.Equal(new[] { 2, 7 }, events.Select(e => e.ReachNodeIndex));

        var outcome = ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("28R", SeqIndex: 2) }, recalculation: false,
            runStartSegmentOf: seq => { Assert.Equal(2, seq); return 6; });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.False(route.Segments[4].IsHoldShortPoint);
        Assert.Equal(new[] { false, true }, events.Select(e => e.Cleared));
        Assert.Equal(6, Assert.Single(outcome.Applied).StopNodeId);
    }

    [Fact]
    public void A_bare_request_clears_the_first_crossing_in_route_order_even_one_with_no_stop()
    {
        // Loaded from inside 09's hold line: the route crosses 09 at once with no stop of its own, then again later
        // at a stop. Cleared across 09, the crossing in front of the aircraft is the one cleared; the later stop stays.
        var route = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60), Node(4, 1000, -200),
            Node(5, 1400, -200), Node(6, 1400, -100), Node(7, 1400, 0), Node(8, 1400, 100));
        var events = Pass(route, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000)));
        Assert.Equal(2, events.Count);
        Assert.False(events[0].Held);
        Assert.False(events[0].Passed);
        Assert.True(events[1].Held);
        Assert.True(route.Segments[4].IsHoldShortPoint);

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.True(route.Segments[4].IsHoldShortPoint);
        Assert.Equal(new[] { true, false }, events.Select(e => e.Cleared));
        Assert.Equal(0, Assert.Single(outcome.Applied).StopNodeId);
        Assert.Equal("", RouteRunwayCrossings.DescribeUnheldRunways(route.RunwayEvents));

        // Cleared "after" a taxiway step that starts past the first crossing: the later stop.
        var later = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60), Node(4, 1000, -200),
            Node(5, 1400, -200), Node(6, 1400, -100), Node(7, 1400, 0), Node(8, 1400, 100));
        var laterEvents = Pass(later, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000)));
        ClearedCrossingHolds.Apply(later, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: _ => 3);
        Assert.False(later.Segments[4].IsHoldShortPoint);
        Assert.Equal(new[] { false, true }, laterEvents.Select(e => e.Cleared));
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

        // A node the route does not pass at that crossing: not cleared, and with no place in words to name, the same
        // sentence without one.
        var elsewhere = AtA5();
        Pass(elsewhere, Runway09());
        var missed = Clear(elsewhere, new ClearedCrossing("09", AtNodeId: 99));
        Assert.True(elsewhere.Segments[0].IsHoldShortPoint);
        Assert.Single(missed.Elsewhere);
        Assert.Equal("Runway 09 is not crossed there. It is not cleared; you will be stopped there.",
            TestCulture.Run("de-DE", () => ClearedCrossingHolds.WarningFor(missed)));
    }

    // "Cross runway 09 at holding point A5": the point the form pins the route through need not be the node the pass
    // chose for the stop. A point on the route from the stop to where the route reaches the runway (both included) is
    // that crossing's; one past the runway is not.
    [Theory]
    [InlineData(2)]   // the stop itself
    [InlineData(3)]   // between the stop and the runway
    [InlineData(4)]   // where the route reaches the runway
    public void A_holding_point_between_the_stop_and_the_runway_clears_that_crossing(int holdingPointNodeId)
    {
        var route = AtA5();
        var ev = Assert.Single(Pass(route, Runway09()));
        Assert.Equal(1, ev.StopNodeIndex);
        Assert.Equal(3, ev.ReachNodeIndex);

        var outcome = Clear(route, new ClearedCrossing("09", AtNodeId: holdingPointNodeId));

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.True(ev.Cleared);
        Assert.Equal(2, Assert.Single(outcome.Applied).StopNodeId);
        AssertNothingReported(outcome);
    }

    [Fact]
    public void A_holding_point_past_the_runway_is_not_that_crossings()
    {
        var route = AtA5();
        Pass(route, Runway09());

        var outcome = Clear(route, new ClearedCrossing("09", AtNodeId: 5));

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Empty(outcome.Applied);
        Assert.Single(outcome.Elsewhere);
    }

    // The form passes the holding point's name with its node, so a stop whose label names the point ("runway 09 at
    // A5") is matched by its name even where the point's node is not the stop's; and a point the route does not cross
    // the runway at is said by its name, "there" only when there is no name to say.
    [Fact]
    public void A_holding_point_is_matched_by_its_name_and_said_by_it()
    {
        var named = AtA5();
        Pass(named, Runway09());
        var cleared = Clear(named, new ClearedCrossing("09", At: "A5", AtNodeId: 1));
        Assert.False(named.Segments[0].IsHoldShortPoint);
        Assert.Equal(2, Assert.Single(cleared.Applied).StopNodeId);
        AssertNothingReported(cleared);

        var other = AtA5();
        Pass(other, Runway09());
        var missed = Clear(other, new ClearedCrossing("09", At: "B3", AtNodeId: 99));
        Assert.True(other.Segments[0].IsHoldShortPoint);
        Assert.Equal("B3", Assert.Single(missed.Elsewhere).At);
        const string byName = "Runway 09 is not crossed at B3. It is not cleared; you will be stopped there.";
        Assert.Equal(byName, ClearedCrossingHolds.WarningFor(missed));
        Assert.Equal(byName, TestCulture.Run("de-DE", () => ClearedCrossingHolds.WarningFor(missed)));
        Assert.Equal("Cleared crossings: applied=(none) notOnRoute=(none) elsewhere=09@B3", ClearedCrossingHolds.DescribeForLog(missed));
    }

    // A runway crossed twice. The request goes to the first crossing, in route order, that matches its place, passed
    // and pilot-held ones included; one that cannot be cleared there (the pilot's own hold shares its stop, or the
    // aircraft is already past it) keeps the request: nothing is cleared and nothing said, and it is never passed on to
    // the other crossing of that runway, in either direction.
    [Fact]
    public void A_request_a_pilot_held_first_crossing_cannot_take_never_clears_the_second()
    {
        foreach (string runway in new[] { "09", "27" })
        {
            var route = TwoCrossingsPilotHeld(0);
            var events = Pass(route, Runway09());
            Assert.Equal("runway 09 at taxiway W", route.Segments[0].HoldShortRunway);
            Assert.Equal(new[] { 1, 5 }, events.Select(e => e.StopNodeIndex));

            var outcome = Clear(route, new ClearedCrossing(runway));

            Assert.Empty(outcome.Applied);
            AssertNothingReported(outcome);
            Assert.Equal("runway 09 at taxiway W", route.Segments[0].HoldShortRunway);
            Assert.True(route.Segments[4].IsHoldShortPoint);
            Assert.Equal("runway 09", route.Segments[4].HoldShortRunway);
            Assert.Empty(route.Segments[4].ClearedRunways);
            Assert.All(events, e => Assert.False(e.Cleared));
        }
    }

    [Fact]
    public void A_request_a_passed_first_crossing_cannot_take_never_clears_the_second()
    {
        var route = TwoCrossings();
        // The aircraft 40 m past the first crossing's stop.
        var events = Pass(route, Runway09(), aircraft: new AircraftPosition(Lat(-60), Lon(500)));
        Assert.True(events[0].Passed);
        Assert.True(events[1].Held);
        Assert.Equal(5, events[1].StopNodeIndex);

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.Empty(outcome.Applied);
        AssertNothingReported(outcome);
        Assert.True(route.Segments[4].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[4].HoldShortRunway);
        Assert.Empty(route.Segments[4].ClearedRunways);
        Assert.All(events, e => Assert.False(e.Cleared));
    }

    // A crossing entered after a taxiway step that starts past the first crossing means the second, and clears it
    // whatever became of the first; one whose own crossing is the pilot's hold never falls back to the first.
    [Fact]
    public void A_request_after_a_later_taxiway_clears_the_second_crossing_and_never_falls_back_to_the_first()
    {
        var held = TwoCrossingsPilotHeld(0);
        Pass(held, Runway09());
        var heldOutcome = ClearedCrossingHolds.Apply(held, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: _ => 3);
        Assert.False(held.Segments[4].IsHoldShortPoint);
        Assert.Equal(6, Assert.Single(heldOutcome.Applied).StopNodeId);
        Assert.Equal("runway 09 at taxiway W", held.Segments[0].HoldShortRunway);

        var passed = TwoCrossings();
        Pass(passed, Runway09(), aircraft: new AircraftPosition(Lat(-60), Lon(500)));
        var passedOutcome = ClearedCrossingHolds.Apply(passed, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: _ => 3);
        Assert.False(passed.Segments[4].IsHoldShortPoint);
        Assert.Equal(6, Assert.Single(passedOutcome.Applied).StopNodeId);

        // The second crossing the pilot's own hold: the request stops there, the first keeps its stop.
        var second = TwoCrossingsPilotHeld(4);
        var secondEvents = Pass(second, Runway09());
        Assert.Equal("runway 09 at taxiway W", second.Segments[4].HoldShortRunway);
        var secondOutcome = ClearedCrossingHolds.Apply(second, new[] { new ClearedCrossing("09", SeqIndex: 1) }, recalculation: false,
            runStartSegmentOf: _ => 3);
        Assert.Empty(secondOutcome.Applied);
        AssertNothingReported(secondOutcome);
        Assert.True(second.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", second.Segments[0].HoldShortRunway);
        Assert.All(secondEvents, e => Assert.False(e.Cleared));
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

        Assert.False(ClearedCrossingHolds.IsStartHoldCleared(route));

        var outcome = Clear(route, new ClearedCrossing("09"));

        Assert.Null(route.StartHoldRunway);
        Assert.True(ev.Cleared);
        Assert.Equal(route.Segments[0].FromNode.NodeId, Assert.Single(outcome.Applied).StopNodeId);
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
        // Remembered on the route, as a segment stop is on its segment: counted down and put back like one.
        Assert.Equal("runway 09", route.PlacedStartHoldLabel);
        Assert.Equal(new[] { "09" }, route.StartHoldClearedRunways);
        Assert.True(ClearedCrossingHolds.IsStartHoldCleared(route));
        Assert.Equal(-1, ClearedCrossingHolds.NextClearedSegment(route, 0));
    }

    [Fact]
    public void Restoring_a_start_hold_puts_it_and_its_label_back()
    {
        var route = StartsHeld();
        var ev = Assert.Single(Pass(route, Runway09()));
        Assert.False(ClearedCrossingHolds.RestoreStartHold(route));   // nothing cleared yet
        Clear(route, new ClearedCrossing("27"));

        Assert.True(ClearedCrossingHolds.RestoreStartHold(route));

        Assert.Equal("runway 09", route.StartHoldRunway);
        Assert.Null(route.PlacedStartHoldLabel);
        Assert.Empty(route.StartHoldClearedRunways);
        Assert.False(ClearedCrossingHolds.IsStartHoldCleared(route));
        Assert.False(ev.Cleared);
        Assert.False(ClearedCrossingHolds.RestoreStartHold(route));
    }

    [Fact]
    public void A_start_hold_guarding_two_runways_is_cleared_one_runway_at_a_time_and_put_back_whole()
    {
        // No clear node before 09 and 01 is beyond it: both share the start hold.
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1200, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 900, 35), Node(2, 1000, 0), Node(3, 1300, 0), Node(4, 1300, -60));
        var events = Pass(route, runways);
        Assert.Equal("runway 09 and runway 01", route.StartHoldRunway);
        Assert.All(events, e => Assert.Equal(0, e.StopNodeIndex));

        Clear(route, new ClearedCrossing("09"));

        Assert.Equal("runway 01", route.StartHoldRunway);
        Assert.Empty(RunwayHoldStages.From(route.StartHoldRunway));
        Assert.Equal("runway 09 and runway 01", route.PlacedStartHoldLabel);
        Assert.Equal(new[] { "09" }, route.StartHoldClearedRunways);
        Assert.True(ClearedCrossingHolds.IsStartHoldCleared(route));
        Assert.Equal(new[] { true, false }, events.Select(e => e.Cleared));

        Clear(route, new ClearedCrossing("01"));
        Assert.Null(route.StartHoldRunway);
        Assert.Equal(new[] { "09", "01" }, route.StartHoldClearedRunways);

        Assert.True(ClearedCrossingHolds.RestoreStartHold(route));

        Assert.Equal("runway 09 and runway 01", route.StartHoldRunway);
        Assert.Equal(new[] { "09", "01" }, RunwayHoldStages.From(route.StartHoldRunway));
        Assert.Null(route.PlacedStartHoldLabel);
        Assert.Empty(route.StartHoldClearedRunways);
        Assert.All(events, e => Assert.False(e.Cleared));
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
        Assert.Equal("cleared across runway 09", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));

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

    // A runway is never crossed in silence: a fully cleared stop is counted down like one, in its own words. A leg end
    // on the same segment wins (its countdown already holds "Slow down" and "Stop" off a runway), and nothing is
    // counted down while holding (the frame that enters another stop's hold never adds a cleared crossing's line).
    [Fact]
    public void A_fully_cleared_stop_is_counted_down_as_cleared_unless_a_leg_end_owns_it()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());
        Assert.False(ClearedCrossingHolds.CountsDownAsCleared(route.Segments[0], legEndApplies: false, taxiing: true));

        Clear(route, new ClearedCrossing("09"));

        Assert.True(ClearedCrossingHolds.CountsDownAsCleared(route.Segments[0], legEndApplies: false, taxiing: true));
        Assert.False(ClearedCrossingHolds.CountsDownAsCleared(route.Segments[0], legEndApplies: true, taxiing: true));
        Assert.False(ClearedCrossingHolds.CountsDownAsCleared(route.Segments[0], legEndApplies: false, taxiing: false));
        Assert.False(ClearedCrossingHolds.CountsDownAsCleared(route.Segments[1], legEndApplies: false, taxiing: true));

        // A stop still guarding another runway is a hold short, counted down as one.
        var (shared, runways) = Intersection();
        Pass(shared, runways);
        Clear(shared, new ClearedCrossing("09"));
        Assert.False(ClearedCrossingHolds.CountsDownAsCleared(shared.Segments[0], legEndApplies: false, taxiing: true));
    }

    // What the countdown and the line say for a stop cleared on the real pass, under a comma-decimal culture.
    [Fact]
    public void A_cleared_stop_is_counted_down_and_said_at_its_line_by_its_placed_label()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());
        Clear(route, new ClearedCrossing("27"));
        var stop = route.Segments[0];
        string label = ClearedCrossingHolds.LineLabel(stop.PlacedHoldLabel, stop.ClearedRunways);

        Assert.Equal("runway 09", label);
        TestCulture.Run("de-DE", () =>
        {
            Assert.Equal("Runway 09 in 120 metres, cleared to cross.", ClearedCrossingCallouts.Approach(label, "120 metres", first: true));
            Assert.Equal("Runway 09 in 45 metres.", ClearedCrossingCallouts.Approach(label, "45 metres", first: false));
            Assert.Equal("Crossing runway 09.", ClearedCrossingCallouts.AtTheLine(label));
            return 0;
        });
    }

    [Fact]
    public void The_line_is_named_by_the_label_its_stop_was_placed_with()
    {
        Assert.Equal("runway 04L at C", ClearedCrossingHolds.LineLabel("runway 04L at C", new[] { "04L" }));
        // No placed label (never left so by ClearAt): the cleared runways, so the line is never said without a runway.
        Assert.Equal("runway 09 and runway 01", ClearedCrossingHolds.LineLabel(null, new[] { "09", "01" }));
        Assert.Equal("runway", ClearedCrossingHolds.LineLabel(" ", Array.Empty<string>()));
        Assert.Equal("Crossing runway 09 and runway 01.",
            TestCulture.Run("de-DE", () => ClearedCrossingCallouts.AtTheLine(ClearedCrossingHolds.LineLabel(null, new[] { "09", "01" }))));
    }

    // What a stop's cleared runways are called where they are crossed: each part of the label it was placed with whose
    // first runway is cleared (either end), split as RemainingLabel splits it; null when none is.
    [Theory]
    [InlineData("runway 09 and runway 01", new[] { "09" }, "runway 09")]
    [InlineData("runway 09 and runway 01", new[] { "27" }, "runway 09")]
    [InlineData("runway 09 and runway 01", new[] { "19" }, "runway 01")]
    [InlineData("runway 06L at D5 and runway 02", new[] { "06L" }, "runway 06L at D5")]
    [InlineData("runway 06L at D5 and runway 02", new[] { "24R" }, "runway 06L at D5")]
    [InlineData("runway 06L at D5 and runway 02", new[] { "20" }, "runway 02")]
    [InlineData("runway 09 and runway 01", new[] { "09", "01" }, "runway 09 and runway 01")]
    [InlineData("runway 15R at N", new[] { "15R" }, "runway 15R at N")]
    [InlineData("D5, Runway 22R", new[] { "22R" }, "D5, Runway 22R")]
    [InlineData("runway 09", new[] { "18" }, null)]
    [InlineData("end of taxiway B", new[] { "09" }, null)]
    [InlineData(null, new[] { "09" }, null)]
    [InlineData("runway 09 and runway 01", new string[0], null)]
    public void ClearedPartLabel_keeps_each_cleared_runways_part(string? placed, string[] cleared, string? expected)
        => Assert.Equal(expected, ClearedCrossingHolds.ClearedPartLabel(placed, cleared));

    // At a shared stop cleared for one runway, the label it was placed with splits into the part it still holds for and
    // the cleared part, which says that a cleared runway is crossed there. The Continue that releases it lets the
    // aircraft across EVERY runway at it, so its one crossing line names the whole placed label, first; the start hold
    // the same.
    [Fact]
    public void A_shared_stop_cleared_for_one_runway_names_every_runway_at_it_when_released()
    {
        var (route, runways) = Intersection();
        Pass(route, runways);
        Clear(route, new ClearedCrossing("27"));
        var stop = route.Segments[0];

        Assert.Equal("runway 01", stop.HoldShortRunway);
        Assert.Equal("runway 09", ClearedCrossingHolds.ClearedPartLabel(stop.PlacedHoldLabel, stop.ClearedRunways));
        Assert.Equal("Continuing. Crossing runway 09 and runway 01. Taxiway A. ", TestCulture.Run("de-DE",
            () => ClearedCrossingCallouts.Continuing(ClearedCrossingCallouts.AtTheLine(
                ClearedCrossingHolds.LineLabel(stop.PlacedHoldLabel, stop.ClearedRunways))) + "Taxiway A. "));

        // A start hold guarding both: cleared for 01 it holds for 09, and its release names both; cleared for both,
        // the whole line too.
        static TaxiRoute TwoRunwayStart(out TaxiGraph.RunwayCenterline[] both)
        {
            both = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1200, fromNorthM: -1500, toNorthM: 1500) };
            return RouteOf(Node(1, 900, 35), Node(2, 1000, 0), Node(3, 1300, 0), Node(4, 1300, -60));
        }
        var start = TwoRunwayStart(out var startRunways);
        Pass(start, startRunways);
        Clear(start, new ClearedCrossing("01"));
        Assert.Equal("runway 09", start.StartHoldRunway);
        Assert.Equal("runway 01", ClearedCrossingHolds.ClearedPartLabel(start.PlacedStartHoldLabel, start.StartHoldClearedRunways));
        Assert.Equal("Crossing runway 09 and runway 01.", TestCulture.Run("de-DE", () => ClearedCrossingCallouts.AtTheLine(
            ClearedCrossingHolds.LineLabel(start.PlacedStartHoldLabel, start.StartHoldClearedRunways))));

        var whole = TwoRunwayStart(out var wholeRunways);
        Pass(whole, wholeRunways);
        Clear(whole, new ClearedCrossing("09"), new ClearedCrossing("01"));
        Assert.Null(whole.StartHoldRunway);
        Assert.Equal("runway 09 and runway 01", ClearedCrossingHolds.ClearedPartLabel(whole.PlacedStartHoldLabel, whole.StartHoldClearedRunways));
        Assert.Equal("Crossing runway 09 and runway 01.", TestCulture.Run("de-DE", () => ClearedCrossingCallouts.AtTheLine(
            ClearedCrossingHolds.LineLabel(whole.PlacedStartHoldLabel, whole.StartHoldClearedRunways))));
        Assert.True(ClearedCrossingHolds.RestoreStartHold(whole));
        Assert.Null(ClearedCrossingHolds.ClearedPartLabel(whole.PlacedStartHoldLabel, whole.StartHoldClearedRunways));
    }

    // Standing at a cleared start hold the aircraft has not started across; it has once it moves (2 kt, the manager's
    // own "a real taxi start" line) or is more than the stop tolerance (10 m) along the route.
    [Theory]
    [InlineData(0.0, 0.0, false)]
    [InlineData(1.9, 0.0, false)]
    [InlineData(2.0, 0.0, true)]
    [InlineData(0.0, 10.0, false)]
    [InlineData(0.0, 10.5, true)]
    public void A_cleared_start_is_started_across_once_the_aircraft_moves(double groundSpeedKts, double progressM, bool expected)
        => Assert.Equal(expected, ClearedCrossingHolds.HasStartedAcross(groundSpeedKts, progressM));

    // A cleared crossing is used once: at its line every entry at that stop node goes, into a NEW list (LoadRoute's
    // Fail and rollback snapshots hold the old one), and a crossing with no stop (node 0) is never "at" a line.
    [Fact]
    public void A_crossing_used_at_its_line_is_dropped_into_a_new_list()
    {
        var list = new List<ClearedCrossing>
        {
            new("09", StopNodeId: 7), new("27", StopNodeId: 7), new("01", StopNodeId: 9), new("18"),
        };

        var after = ClearedCrossingHolds.WithoutStop(list, 7);

        Assert.NotSame(list, after);
        Assert.Equal(4, list.Count);
        Assert.Equal(new[] { "01", "18" }, after.Select(c => c.Runway));
        Assert.True(ClearedCrossingHolds.HasStop(list, 7));
        Assert.False(ClearedCrossingHolds.HasStop(after, 7));
        Assert.True(ClearedCrossingHolds.HasStop(after, 9));
        Assert.False(ClearedCrossingHolds.HasStop(list, 0));
        var same = ClearedCrossingHolds.WithoutStop(list, 0);
        Assert.NotSame(list, same);
        Assert.Equal(list, same);
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
        Assert.Equal("runway 04R", route.PlacedStartHoldLabel);
        Assert.True(ClearedCrossingHolds.IsStartHoldCleared(route));
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

    // Continue before the line (spec 8.4): only once the countdown has named the stop, so a stray key never clears a
    // runway the pilot has not been told about; only at a hold short whose label names a runway; never on the route's
    // last segment, the departure runway's own hold or the end of a leg.
    [Fact]
    public void A_Continue_before_the_line_clears_only_a_runway_the_countdown_has_named()
    {
        Assert.True(ClearedCrossingHolds.CanClearEarly(true, true, "runway 22R", false));
        Assert.False(ClearedCrossingHolds.CanClearEarly(false, true, "runway 22R", false));   // nothing counted down yet
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, true, "runway 22R", true));     // the departure hold or a leg end
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, true, "end of taxiway A", false));
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, true, "taxiway L", false));
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, false, "runway 22R", false));

        Assert.True(ClearedCrossingHolds.CanClearEarly(true, true, "runway 09 and runway 01", false));
        Assert.True(ClearedCrossingHolds.CanClearEarly(true, true, "D5, Runway 22R", false));
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, true, "holding point B3", false));
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, true, "", false));
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, true, null, false));
    }

    // What ContinuePastHoldShort does with an early press, on stops the real pass placed: the first runway the label
    // names, cleared at that ONE stop through ClearAt, and the sentence it says, under a comma-decimal culture.
    [Fact]
    public void A_Continue_before_the_line_clears_the_first_runway_at_that_stop_only()
    {
        // A stop for one runway: cleared whole, so it is counted down and said at its line as cleared, with no stop.
        var single = AcrossOnA();
        var ev = Assert.Single(Pass(single, Runway09()));
        var stop = single.Segments[0];
        Assert.True(ClearedCrossingHolds.CanClearEarly(true, stop.IsHoldShortPoint, stop.HoldShortRunway, isFinalSegment: false));
        string runway = RouteRunwayCrossings.ExtractRunwayDesignator(stop.HoldShortRunway)!;

        Assert.True(ClearedCrossingHolds.ClearAt(single, 1, runway));

        Assert.True(ClearedCrossingHolds.IsFullyCleared(stop));
        Assert.True(ClearedCrossingHolds.CountsDownAsCleared(stop, legEndApplies: false, taxiing: true));
        Assert.True(ev.Cleared);
        Assert.Equal("Cleared across runway 09. No stop.", TestCulture.Run("de-DE",
            () => ClearedCrossingCallouts.EarlyContinue(runway, stop.IsHoldShortPoint ? stop.HoldShortRunway : null)));
        Assert.Equal("Crossing runway 09.", ClearedCrossingCallouts.AtTheLine(ClearedCrossingHolds.LineLabel(stop.PlacedHoldLabel, stop.ClearedRunways)));
        // Pressed again: no hold short is left there, so nothing more is cleared.
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, stop.IsHoldShortPoint, stop.HoldShortRunway, isFinalSegment: false));

        // A stop guarding two runways: the first only [HLD-11]. It still holds for the other, as an ordinary hold short
        // that one Continue at the line releases, and that Continue names both runways where they are crossed.
        var (shared, runways) = Intersection();
        Pass(shared, runways);
        var both = shared.Segments[0];
        string first = RouteRunwayCrossings.ExtractRunwayDesignator(both.HoldShortRunway)!;
        Assert.Equal("09", first);

        Assert.True(ClearedCrossingHolds.ClearAt(shared, 1, first));

        Assert.True(both.IsHoldShortPoint);
        Assert.Equal("runway 01", both.HoldShortRunway);
        Assert.Empty(RunwayHoldStages.From(both.HoldShortRunway));
        Assert.False(ClearedCrossingHolds.CountsDownAsCleared(both, legEndApplies: false, taxiing: true));
        Assert.Equal(new[] { true, false }, shared.RunwayEvents.Select(e => e.Cleared));
        Assert.Equal("Cleared across runway 09. Still holding short of runway 01.", TestCulture.Run("de-DE",
            () => ClearedCrossingCallouts.EarlyContinue(first, both.IsHoldShortPoint ? both.HoldShortRunway : null)));
        Assert.Equal("runway 09", ClearedCrossingHolds.ClearedPartLabel(both.PlacedHoldLabel, both.ClearedRunways));
        Assert.Equal("Continuing. Crossing runway 09 and runway 01. ", TestCulture.Run("de-DE", () => ClearedCrossingCallouts.Continuing(
            ClearedCrossingCallouts.AtTheLine(ClearedCrossingHolds.LineLabel(both.PlacedHoldLabel, both.ClearedRunways)))));

        // A second press before the line clears the other runway too: no stop is left, and its line names both.
        Assert.True(ClearedCrossingHolds.CanClearEarly(true, both.IsHoldShortPoint, both.HoldShortRunway, isFinalSegment: false));
        string second = RouteRunwayCrossings.ExtractRunwayDesignator(both.HoldShortRunway)!;
        Assert.True(ClearedCrossingHolds.ClearAt(shared, 1, second));
        Assert.True(ClearedCrossingHolds.IsFullyCleared(both));
        Assert.Equal("Cleared across runway 01. No stop.", TestCulture.Run("de-DE",
            () => ClearedCrossingCallouts.EarlyContinue(second, both.IsHoldShortPoint ? both.HoldShortRunway : null)));
        Assert.Equal("Crossing runway 09 and runway 01.", ClearedCrossingCallouts.AtTheLine(ClearedCrossingHolds.LineLabel(both.PlacedHoldLabel, both.ClearedRunways)));

        // The Hold short key puts the stop back whole.
        Assert.True(ClearedCrossingHolds.RestoreAt(shared, 0));
        Assert.Equal("runway 09 and runway 01", both.HoldShortRunway);
        Assert.Equal(new[] { "09", "01" }, RunwayHoldStages.From(both.HoldShortRunway));
    }

    // A stop shared with the pilot's own taxiway or holding-point hold names the runway, so the countdown names it, but
    // ClearAt refuses it: the press clears nothing there, and the manager then does what the press did before.
    [Theory]
    [InlineData("taxiway W", "runway 09 at taxiway W")]
    [InlineData("holding point B3", "runway 09 at holding point B3")]
    public void A_Continue_before_a_stop_shared_with_the_pilots_own_hold_clears_nothing(string pilotLabel, string sharedLabel)
    {
        var route = Named(RouteOf(Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 09 at D5"),
            Node(3, 1000, 70), Node(4, 1000, 51), Node(5, 1000, 21), Node(6, 1000, -21), Node(7, 1000, -105)), "D");
        route.Segments[2].IsHoldShortPoint = true;
        route.Segments[2].HoldShortRunway = pilotLabel;
        var ev = Assert.Single(Pass(route, Runway09()));
        var stop = route.Segments[2];
        Assert.Equal(sharedLabel, stop.HoldShortRunway);
        Assert.True(ClearedCrossingHolds.CanClearEarly(true, stop.IsHoldShortPoint, stop.HoldShortRunway, isFinalSegment: false));
        // What the paired offer asks before it offers a stop: refused, so this one is never offered as a pair.
        Assert.False(ClearedCrossingHolds.CanClearLabel(stop.HoldShortRunway, "09"));

        Assert.False(ClearedCrossingHolds.ClearAt(route, 3, RouteRunwayCrossings.ExtractRunwayDesignator(stop.HoldShortRunway)!));

        Assert.True(stop.IsHoldShortPoint);
        Assert.Equal(sharedLabel, stop.HoldShortRunway);
        Assert.Null(stop.PlacedHoldLabel);
        Assert.Empty(stop.ClearedRunways);
        Assert.False(ev.Cleared);
    }

    // Whether clearing a runway at a stop with this label takes a part off it, as ClearAt judges it: the label names the
    // runway (either end) and that part is not the pilot's own taxiway, holding-point or end-of-taxiway stop shared
    // with it. The paired offer asks this first, so a "cleared across both" press is never offered for a stop it could
    // not clear.
    [Theory]
    [InlineData("runway 28L", "28L", true)]
    [InlineData("runway 28L", "10R", true)]
    [InlineData("runway 28L at F", "28L", true)]
    [InlineData("runway 06L at D5 and runway 02", "06L", true)]
    [InlineData("D5, Runway 22R", "22R", true)]
    [InlineData("runway 09 at taxiway W and runway 01", "01", true)]
    [InlineData("runway 28L at taxiway W", "28L", false)]
    [InlineData("runway 28L at holding point B3", "28L", false)]
    [InlineData("runway 09 at taxiway W and runway 01", "09", false)]
    [InlineData("end of taxiway D", "09", false)]
    [InlineData("taxiway W", "09", false)]
    [InlineData("runway 28L", "01", false)]
    [InlineData("runway 28L", "", false)]
    [InlineData("", "28L", false)]
    [InlineData(null, "28L", false)]
    public void CanClearLabel_refuses_a_stop_shared_with_the_pilots_own_hold(string? label, string runway, bool expected)
        => Assert.Equal(expected, ClearedCrossingHolds.CanClearLabel(label, runway));

    // Stage 3 maps each cleared crossing to a Cross step: an entry says who cleared it (a crossing the pilot entered, or
    // a Continue), and where the route reaches its runway, which is how one with no stop of its own is used up.
    [Fact]
    public void A_crossing_records_who_cleared_it_and_where_the_route_reaches_its_runway()
    {
        Assert.Equal(ClearedBy.Clearance, new ClearedCrossing("09").Origin);
        Assert.Equal(0, new ClearedCrossing("09").ReachNodeId);

        // A stop: its node, and the node where the route reaches the runway (route node 2, node id 3).
        var route = AcrossOnA();
        Assert.Equal(2, Assert.Single(Pass(route, Runway09())).ReachNodeIndex);
        var applied = Assert.Single(Clear(route, new ClearedCrossing("09")).Applied);
        Assert.Equal((2, 3, ClearedBy.Clearance), (applied.StopNodeId, applied.ReachNodeId, applied.Origin));

        // No stop of its own (#355's decision 3): node 0, and the node where the route reaches the runway.
        var noStop = StartsHeld();
        var ev = Assert.Single(Pass(noStop, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000))));
        var unheld = Assert.Single(Clear(noStop, new ClearedCrossing("09")).Applied);
        Assert.Equal(0, unheld.StopNodeId);
        Assert.Equal(noStop.Segments[ev.ReachNodeIndex - 1].ToNode.NodeId, unheld.ReachNodeId);

        // A recalculation keeps who cleared it: a crossing a Continue cleared is still a Continue's.
        var again = AcrossOnA();
        Pass(again, Runway09());
        var byContinue = new ClearedCrossing("09", StopNodeId: 2, Origin: ClearedBy.Continue);
        var kept = Assert.Single(ClearedCrossingHolds.Apply(again, new[] { byContinue }, recalculation: true).Applied);
        Assert.Equal((2, 3, ClearedBy.Continue), (kept.StopNodeId, kept.ReachNodeId, kept.Origin));
    }

    // A crossing with no stop (node 0) is never "at" a line, so WithoutStop never uses it: it is used once the segment
    // cursor has passed where the route reaches its runway, into a NEW list; null while nothing is to go.
    [Fact]
    public void A_crossing_with_no_stop_is_used_once_the_cursor_passes_where_the_route_reaches_its_runway()
    {
        var route = StartsHeld();
        var ev = Assert.Single(Pass(route, Runway09(), aircraft: new AircraftPosition(Lat(35), Lon(1000))));
        int reach = ev.ReachNodeIndex;
        Assert.True(reach >= 1);
        int reachNodeId = route.Segments[reach - 1].ToNode.NodeId;
        var list = new List<ClearedCrossing>
        {
            new("09", ReachNodeId: reachNodeId), new("01", StopNodeId: 9), new("18"), new("36", ReachNodeId: 999),
        };

        Assert.Null(ClearedCrossingHolds.WithoutPassedNoStop(list, route, reach - 1));

        var after = ClearedCrossingHolds.WithoutPassedNoStop(list, route, reach);

        Assert.NotNull(after);
        Assert.NotSame(list, after);
        Assert.Equal(4, list.Count);
        // A stop's crossing waits for its line; one whose reach is unknown or not on this route stays.
        Assert.Equal(new[] { "01", "18", "36" }, after!.Select(c => c.Runway));
        Assert.Null(ClearedCrossingHolds.WithoutPassedNoStop(after, route, route.Segments.Count));
        Assert.Null(ClearedCrossingHolds.WithoutPassedNoStop(new List<ClearedCrossing>(), route, reach));
    }

    // The Hold short key's restore tells the window which crossings it put back: those still recorded at that stop, and,
    // for a stop whose line was said as passed (the segment jump says one early), those used up there.
    [Fact]
    public void A_restore_names_every_crossing_it_put_back()
    {
        var list = new List<ClearedCrossing>
        {
            new("09", StopNodeId: 7), new("01", StopNodeId: 7, Origin: ClearedBy.Continue), new("04", StopNodeId: 9), new("18"),
        };

        var at7 = ClearedCrossingHolds.AtStop(list, 7);
        Assert.NotSame(list, at7);
        Assert.Equal(new[] { ("09", ClearedBy.Clearance), ("01", ClearedBy.Continue) }, at7.Select(c => (c.Runway, c.Origin)));
        Assert.Empty(ClearedCrossingHolds.AtStop(list, 0));
        Assert.Empty(ClearedCrossingHolds.AtStop(list, 8));

        var used = new[] { new ClearedCrossing("22R", StopNodeId: 5, Origin: ClearedBy.Continue) };
        Assert.Equal(new[] { "04" }, ClearedCrossingHolds.RestoredCrossings(list, 9, passedNodeId: 5, used).Select(c => c.Runway));
        Assert.Equal(new[] { "22R" }, ClearedCrossingHolds.RestoredCrossings(list, 5, passedNodeId: 5, used).Select(c => c.Runway));
        Assert.Empty(ClearedCrossingHolds.RestoredCrossings(list, 5, passedNodeId: 0, used));
        Assert.Empty(ClearedCrossingHolds.RestoredCrossings(list, 5, passedNodeId: 5, null));

        var args = new ClearedCrossingsChangedEventArgs(at7);
        Assert.Equal(at7, args.Restored);
        Assert.Empty(new ClearedCrossingsChangedEventArgs(null!).Restored);
    }

    // An early press records its crossing as an entered one would be: into a NEW list (LoadRoute's Fail and rollback
    // snapshots hold the old one), never twice for the same runway at the same stop.
    [Fact]
    public void A_crossing_cleared_by_Continue_is_recorded_into_a_new_list()
    {
        var list = new List<ClearedCrossing> { new("01", StopNodeId: 9) };

        var after = ClearedCrossingHolds.WithCrossing(list, new ClearedCrossing("22R", StopNodeId: 7));

        Assert.NotSame(list, after);
        Assert.Equal(new[] { "01" }, list.Select(c => c.Runway));
        Assert.Equal(new[] { "01", "22R" }, after.Select(c => c.Runway));
        Assert.True(ClearedCrossingHolds.HasStop(after, 7));
        // The same runway (either end) at the same stop is there already; another runway or another stop is another crossing.
        Assert.Equal(2, ClearedCrossingHolds.WithCrossing(after, new ClearedCrossing("22R", StopNodeId: 7)).Count);
        Assert.Equal(2, ClearedCrossingHolds.WithCrossing(after, new ClearedCrossing("04L", StopNodeId: 7)).Count);
        Assert.Equal(3, ClearedCrossingHolds.WithCrossing(after, new ClearedCrossing("22R", StopNodeId: 8)).Count);
        Assert.Equal(3, ClearedCrossingHolds.WithCrossing(after, new ClearedCrossing("09", StopNodeId: 7)).Count);
        Assert.Single(ClearedCrossingHolds.WithCrossing(null!, new ClearedCrossing("22R", StopNodeId: 7)));
    }
}
