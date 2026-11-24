// The Hold short key (Input Alt+Y, spec 8.8). ATC can take a crossing back ("hold short of 04L, traffic"); a sighted
// pilot just stops at the line. ClearedCrossingHolds.DecideHoldShortKey decides, from the route and where the aircraft
// is, which answer applies; TaxiGuidanceManager.HoldShortOfNextClearedRunway acts on it (ClearedCrossingWiringTests).
// It puts a stop back only while the aircraft is short of its line, judged from the route geometry (never from the
// "passed" note alone, which the segment jump can set early and a released shared stop sets at the Continue); past
// the line or on the runway it only reports; with no cleared runway ahead it names the next stop of any kind. It never
// clears anything, and deciding changes nothing on the route. Each test builds its route and runs the real pass.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using static MSFSBlindAssist.Tests.RunwayFixture;
using Answer = MSFSBlindAssist.Navigation.ClearedCrossingHolds.HoldShortKeyAnswer;
using KeyState = MSFSBlindAssist.Navigation.ClearedCrossingHolds.HoldShortKeyState;
using PassedLine = MSFSBlindAssist.Navigation.ClearedCrossingHolds.PassedLine;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class ClearedCrossingHoldShortKeyTests
{
    private static TaxiGraph.RunwayCenterline[] Runway09() => new[] { EastWest() };

    // Northbound on A across 09/27: the stop is Segments[0], ending 100 m south of the centreline (node 2).
    private static TaxiRoute AcrossOnA()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        foreach (var s in route.Segments) s.TaxiwayName = "A";
        return route;
    }

    private static TaxiRoute ClearedAcrossOnA()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09") }, recalculation: false);
        Assert.True(ClearedCrossingHolds.IsFullyCleared(route.Segments[0]));
        return route;
    }

    // Southbound onto 09 from a start node 15 m clear of the pavement (outside the 10 m clear margin, where the pass
    // places a start hold [HLD-10]).
    private static TaxiRoute StartsAtTheLine()
    {
        var route = RouteOf(Node(1, 1000, 45), Node(2, 1000, 0), Node(3, 1000, -60));
        Pass(route, Runway09());
        Assert.Equal("runway 09", route.StartHoldRunway);
        return route;
    }

    private static KeyState Taxiing(int segment, double east, double north, double groundSpeedKts = 10.0,
                                    PassedLine? passed = null, TaxiGraph.RunwayCenterline[]? runways = null)
        => new(Guiding: true, CurrentSegmentIndex: segment, Holding: false, HoldingAtDestination: false,
               Aircraft: new ClearedCrossingHolds.HoldShortKeyAircraft(Lat(north), Lon(east), groundSpeedKts),
               Runways: runways ?? Runway09(), Passed: passed, DestinationName: "Gate 12");

    private static KeyState Holding(int segment, double east, double north, TaxiGraph.RunwayCenterline[] runways,
                                    bool atDestination = false)
        => new(Guiding: true, CurrentSegmentIndex: segment, Holding: true, HoldingAtDestination: atDestination,
               Aircraft: new ClearedCrossingHolds.HoldShortKeyAircraft(Lat(north), Lon(east), 0.0),
               Runways: runways, Passed: null, DestinationName: "runway 27");

    private static string Said(ClearedCrossingHolds.HoldShortKeyDecision d) => d.Answer switch
    {
        Answer.NoRoute => ClearedCrossingCallouts.NoRoute,
        Answer.RestoreStartHold or Answer.RestoreSegment => ClearedCrossingCallouts.HoldRestored(d.Label, TaxiGuidanceManager.FormatDistance(d.Metres)),
        Answer.PastTheLine => ClearedCrossingCallouts.PastTheLine(d.Label),
        Answer.NextStop => ClearedCrossingCallouts.NextStop(d.Label, TaxiGuidanceManager.FormatDistance(d.Metres)),
        _ => ClearedCrossingCallouts.NoRunwayAhead,
    };

    private static T InMetres<T>(Func<T> body)
    {
        var saved = DistanceFormatter.UnitProvider;
        try
        {
            DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
            return TestCulture.Run("de-DE", body);
        }
        finally { DistanceFormatter.UnitProvider = saved; }
    }

    [Fact]
    public void With_no_route_or_no_guidance_it_says_there_is_no_route()
    {
        Assert.Equal(Answer.NoRoute, ClearedCrossingHolds.DecideHoldShortKey(null, Taxiing(0, 500, -180)).Answer);
        Assert.Equal(Answer.NoRoute, ClearedCrossingHolds.DecideHoldShortKey(ClearedAcrossOnA(),
            Taxiing(0, 500, -180) with { Guiding = false }).Answer);
        Assert.Equal(Answer.NoRoute, ClearedCrossingHolds.DecideHoldShortKey(new TaxiRoute(), Taxiing(0, 500, -180)).Answer);
        Assert.Equal("No taxi route.", Said(ClearedCrossingHolds.DecideHoldShortKey(null, Taxiing(0, 500, -180))));
    }

    // Before the line: the stop comes back, measured along the route the way the status readout measures it. A
    // second press finds it a stop again and names it: it never clears it.
    [Fact]
    public void Before_the_line_it_puts_the_stop_back_and_a_second_press_only_names_it()
    {
        var route = ClearedAcrossOnA();
        var state = Taxiing(0, 500, -180);

        var first = ClearedCrossingHolds.DecideHoldShortKey(route, state);

        Assert.Equal(Answer.RestoreSegment, first.Answer);
        Assert.Equal(0, first.SegmentIndex);
        Assert.Equal(0, first.CursorIndex);
        Assert.Equal("runway 09", first.Label);
        Assert.Equal(80.0, first.Metres, 1);
        Assert.Equal("Holding short of runway 09. 80 metres.", InMetres(() => Said(first)));
        // Deciding changed nothing: the manager restores (RestoreAt).
        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.Equal(new[] { "09" }, route.Segments[0].ClearedRunways);

        Assert.True(ClearedCrossingHolds.RestoreAt(route, first.SegmentIndex));
        var second = ClearedCrossingHolds.DecideHoldShortKey(route, state);

        Assert.Equal(Answer.NextStop, second.Answer);
        Assert.Equal(0, second.SegmentIndex);
        Assert.Equal("Next stop runway 09 in 80 metres.", InMetres(() => Said(second)));
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);
        Assert.Empty(route.Segments[0].ClearedRunways);
    }

    // Past the line by the route geometry, while the segment cursor still lags (a corner cut): it only reports, and
    // BA never asks for a stop on a runway. Within the stop tolerance (10 m) of the line it is still at the line.
    [Theory]
    [InlineData(-60.0, Answer.PastTheLine)]     // 40 m past the line, short of the pavement
    [InlineData(-10.0, Answer.PastTheLine)]     // on the runway
    [InlineData(-95.0, Answer.RestoreSegment)]  // 5 m past: still at the line
    [InlineData(-105.0, Answer.RestoreSegment)] // 5 m short
    public void Past_the_line_by_the_route_it_only_reports(double north, Answer expected)
    {
        var route = ClearedAcrossOnA();

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 500, north));

        Assert.Equal(expected, d.Answer);
        Assert.Equal("runway 09", d.Label);
        if (expected == Answer.PastTheLine)
            Assert.Equal("Past the hold line of runway 09.", InMetres(() => Said(d)));
        Assert.False(route.Segments[0].IsHoldShortPoint);
    }

    // Off the route there is no along-route answer: on the pavement of the runway the stop guards it is past the line;
    // anywhere else the cursor's view stands and the stop ahead comes back.
    [Fact]
    public void Off_the_route_on_its_own_runway_it_is_past_the_line_and_elsewhere_the_stop_comes_back()
    {
        var route = ClearedAcrossOnA();

        Assert.Equal(Answer.PastTheLine, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 560, -10)).Answer);
        Assert.Equal(Answer.RestoreSegment, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 560, -150)).Answer);
        // On ANOTHER runway's pavement, off the route: not this stop's runway, so its stop comes back.
        var other = new[] { EastWest(), NorthSouth("01", "19", eastM: 560, fromNorthM: -1000, toNorthM: 1000) };
        Assert.Equal(Answer.RestoreSegment, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 560, -150, runways: other)).Answer);
    }

    // The segment jump can say a cleared line and note it passed up to ~100 m early: with the aircraft still short of
    // the line by the route geometry the stop comes back, and the cursor goes back onto its segment, so the ordinary
    // countdown and the hold run as for any stop.
    [Fact]
    public void A_line_noted_passed_that_is_still_ahead_comes_back_with_the_cursor_on_its_segment()
    {
        var route = ClearedAcrossOnA();
        int node = route.Segments[0].ToNode.NodeId;

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(1, 500, -150, passed: new PassedLine("runway 09", node, 3.0)));

        Assert.Equal(Answer.RestoreSegment, d.Answer);
        Assert.Equal(0, d.SegmentIndex);
        Assert.Equal(0, d.CursorIndex);
        Assert.Equal("Holding short of runway 09. 50 metres.", InMetres(() => Said(d)));
    }

    // Noted passed and past it by the route: "Past the hold line", within 60 s of the line; after that, what is ahead.
    [Fact]
    public void A_line_passed_is_reported_for_a_minute_then_what_is_ahead_is_said()
    {
        var route = ClearedAcrossOnA();
        int node = route.Segments[0].ToNode.NodeId;

        var recent = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(1, 500, -50, passed: new PassedLine("runway 09", node, 3.0)));
        Assert.Equal(Answer.PastTheLine, recent.Answer);
        Assert.Equal("Past the hold line of runway 09.", InMetres(() => Said(recent)));
        Assert.Equal(Answer.PastTheLine, ClearedCrossingHolds.DecideHoldShortKey(route,
            Taxiing(1, 500, -50, passed: new PassedLine("runway 09", node, 60.0))).Answer);

        var later = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(1, 500, -50, passed: new PassedLine("runway 09", node, 61.0)));
        Assert.Equal(Answer.NoRunwayAhead, later.Answer);
        Assert.Equal("No runway ahead on this route.", InMetres(() => Said(later)));

        // Off the route, not on the runway: the note stands for its minute (the geometry has no answer there).
        Assert.Equal(Answer.PastTheLine, ClearedCrossingHolds.DecideHoldShortKey(route,
            Taxiing(1, 600, 50, passed: new PassedLine("runway 09", node, 3.0))).Answer);
        Assert.False(route.Segments[0].IsHoldShortPoint);
    }

    // A recalculation can bring a line noted passed back as an ordinary stop ahead (the aircraft was still short of it):
    // the note is never read as "past" against the route geometry, and the stop is named as the next one.
    [Fact]
    public void A_line_noted_passed_that_is_a_stop_ahead_again_is_named_as_the_next_stop()
    {
        var route = AcrossOnA();
        Pass(route, Runway09());
        int node = route.Segments[0].ToNode.NodeId;

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 500, -150, passed: new PassedLine("runway 09", node, 5.0)));

        Assert.Equal(Answer.NextStop, d.Answer);
        Assert.Equal("Next stop runway 09 in 50 metres.", InMetres(() => Said(d)));
    }

    // A stop guarding two runways, cleared for one: holding at it, the key puts the cleared one back, so the stop asks
    // for both again; released by its Continue (noted passed at the press) with the aircraft still at the line, it comes
    // back too, the cursor onto its segment; once across the line, it only reports.
    [Fact]
    public void At_a_shared_stop_it_puts_the_cleared_runway_back_so_the_stop_asks_for_both()
    {
        var (route, runways) = Intersection();
        Pass(route, runways);
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09") }, recalculation: false);
        Assert.Equal("runway 01", route.Segments[0].HoldShortRunway);
        int node = route.Segments[0].ToNode.NodeId;

        var held = ClearedCrossingHolds.DecideHoldShortKey(route, Holding(1, 1340, -60, runways));
        Assert.Equal(Answer.RestoreSegment, held.Answer);
        Assert.Equal(0, held.SegmentIndex);
        Assert.Equal(1, held.CursorIndex);
        Assert.Equal("Holding short of runway 09 and runway 01. 0 metres.", InMetres(() => Said(held)));

        var released = ClearedCrossingHolds.DecideHoldShortKey(route,
            Taxiing(1, 1340, -60, groundSpeedKts: 0.0, passed: new PassedLine("runway 09 and runway 01", node, 2.0), runways: runways));
        Assert.Equal(Answer.RestoreSegment, released.Answer);
        Assert.Equal(0, released.SegmentIndex);
        Assert.Equal(0, released.CursorIndex);

        var across = ClearedCrossingHolds.DecideHoldShortKey(route,
            Taxiing(1, 1380, -20, passed: new PassedLine("runway 09 and runway 01", node, 8.0), runways: runways));
        Assert.Equal(Answer.PastTheLine, across.Answer);
        Assert.Equal("Past the hold line of runway 09 and runway 01.", InMetres(() => Said(across)));

        Assert.True(ClearedCrossingHolds.RestoreAt(route, held.SegmentIndex));
        Assert.Equal(new[] { "09", "01" }, RunwayHoldStages.From(route.Segments[0].HoldShortRunway));
    }

    // Holding at an ordinary stop with a cleared runway further on: that one comes back, measured from the aircraft.
    [Fact]
    public void Holding_at_another_stop_it_puts_back_the_cleared_runway_further_on()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100),
            Node(5, 900, 300), Node(6, 900, 100), Node(7, 900, 0), Node(8, 900, -100));
        Pass(route, Runway09());
        Assert.True(ClearedCrossingHolds.ClearAt(route, 5, "09"));

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Holding(1, 500, -100, Runway09()));

        Assert.Equal(Answer.RestoreSegment, d.Answer);
        Assert.Equal(4, d.SegmentIndex);
        Assert.Equal(1, d.CursorIndex);
        Assert.Equal("Holding short of runway 09. 850 metres.", InMetres(() => Said(d)));
    }

    // A cleared start hold: standing at it, it comes back; once the aircraft has started across (moving at 2 kt, more
    // than 10 m along, or inside the runway's clear margin, the pass's own test for a start hold) it is passed, and only
    // reported while the line said at the start is a minute old.
    [Fact]
    public void A_cleared_start_hold_comes_back_until_the_aircraft_has_started_across()
    {
        var route = StartsAtTheLine();
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09") }, recalculation: false);
        Assert.Null(route.StartHoldRunway);
        int start = route.Segments[0].FromNode.NodeId;

        var standing = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 1000, 45, groundSpeedKts: 0.0));
        Assert.Equal(Answer.RestoreStartHold, standing.Answer);
        Assert.Equal("Holding short of runway 09. 0 metres.", InMetres(() => Said(standing)));
        Assert.Equal(Answer.RestoreStartHold, ClearedCrossingHolds.DecideHoldShortKey(route,
            Taxiing(0, 1000, 45, groundSpeedKts: 0.0) with { Guiding = true, Aircraft = null }).Answer);

        Assert.Equal(Answer.NoRunwayAhead, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 1000, 45, groundSpeedKts: 3.0)).Answer);
        Assert.Equal(Answer.NoRunwayAhead, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 1000, 30, groundSpeedKts: 0.0)).Answer);
        Assert.Equal(Answer.NoRunwayAhead, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 1000, 39, groundSpeedKts: 0.0)).Answer);
        var said = ClearedCrossingHolds.DecideHoldShortKey(route,
            Taxiing(0, 1000, 45, groundSpeedKts: 3.0, passed: new PassedLine("runway 09", start, 1.0)));
        Assert.Equal(Answer.PastTheLine, said.Answer);
        Assert.Equal("Past the hold line of runway 09.", InMetres(() => Said(said)));

        Assert.True(ClearedCrossingHolds.RestoreStartHold(route));
        var again = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 1000, 45, groundSpeedKts: 0.0));
        Assert.Equal(Answer.NextStop, again.Answer);
        Assert.Equal(-1, again.SegmentIndex);
        Assert.Equal("Next stop runway 09 in 0 metres.", InMetres(() => Said(again)));
    }

    // Holding at a start hold guarding two runways, cleared for one: it comes back whole, so the hold asks for both.
    [Fact]
    public void Holding_at_a_shared_start_hold_it_puts_the_cleared_runway_back()
    {
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1200, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 900, 35), Node(2, 1000, 0), Node(3, 1300, 0), Node(4, 1300, -60));
        Pass(route, runways);
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("01") }, recalculation: false);
        Assert.Equal("runway 09", route.StartHoldRunway);

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Holding(0, 900, 35, runways));

        Assert.Equal(Answer.RestoreStartHold, d.Answer);
        Assert.Equal("runway 09 and runway 01", d.Label);
    }

    // No cleared runway ahead: the next stop of any kind, by its own label, and nothing changes.
    [Theory]
    [InlineData("runway 27")]
    [InlineData("taxiway L")]
    [InlineData("holding point B3")]
    [InlineData("end of taxiway D")]
    [InlineData("runway 09 at taxiway W")]
    public void With_no_cleared_runway_ahead_it_names_the_next_stop_of_any_kind(string label)
    {
        var route = RouteOf(Node(1, 0, -1000), Node(2, 0, -900), Node(3, 0, -780), Node(4, 0, -700));
        route.Segments[1].IsHoldShortPoint = true;
        route.Segments[1].HoldShortRunway = label;

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 0, -1000));

        Assert.Equal(Answer.NextStop, d.Answer);
        Assert.Equal(1, d.SegmentIndex);
        Assert.Equal($"Next stop {label} in 220 metres.", InMetres(() => Said(d)));
        Assert.True(route.Segments[1].IsHoldShortPoint);
        Assert.Equal(label, route.Segments[1].HoldShortRunway);

        // Past it on the cursor: the next one is no longer ahead.
        Assert.Equal(Answer.NoRunwayAhead, ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(2, 0, -850)).Answer);
    }

    // The destination's own hold is a stop too, and holding at it the key names it, never "no runway ahead".
    [Fact]
    public void The_destinations_own_hold_is_the_next_stop()
    {
        var route = RouteOf(Node(1, 0, -1000), Node(2, 0, -900), Node(3, 0, -800));
        route.Segments[1].IsHoldShortPoint = true;
        route.Segments[1].HoldShortRunway = "runway 27";

        var ahead = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 0, -1000));
        Assert.Equal("Next stop runway 27 in 200 metres.", InMetres(() => Said(ahead)));

        foreach (int cursor in new[] { 1, 2 })
        {
            var here = ClearedCrossingHolds.DecideHoldShortKey(route, Holding(cursor, 0, -800, Runway09(), atDestination: true));
            Assert.Equal(Answer.NextStop, here.Answer);
            Assert.Equal("Next stop runway 27 in 0 metres.", InMetres(() => Said(here)));
        }
    }

    // An uncleared start hold not yet held (the route loaded, guidance not started) is the next stop.
    [Fact]
    public void An_uncleared_start_hold_is_the_next_stop_until_it_holds()
    {
        var route = StartsAtTheLine();

        var d = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 1000, 45, groundSpeedKts: 0.0));

        Assert.Equal(Answer.NextStop, d.Answer);
        Assert.Equal("Next stop runway 09 in 0 metres.", InMetres(() => Said(d)));
        // Holding at it: nothing ahead of it on this short route.
        Assert.Equal(Answer.NoRunwayAhead, ClearedCrossingHolds.DecideHoldShortKey(route, Holding(0, 1000, 45, Runway09())).Answer);
    }

    [Fact]
    public void With_no_stop_ahead_it_says_no_runway_ahead()
    {
        var d = ClearedCrossingHolds.DecideHoldShortKey(AcrossOnA(), Taxiing(0, 500, -300));

        Assert.Equal(Answer.NoRunwayAhead, d.Answer);
        Assert.Equal("No runway ahead on this route.", Said(d));
    }

    // The along-route distance past a stop node: negative short of it, by the nearest segment within a taxiway width;
    // null off the route there.
    [Fact]
    public void How_far_past_a_stop_the_aircraft_is_comes_from_the_route()
    {
        var route = AcrossOnA();

        Assert.Equal(-80.0, ClearedCrossingHolds.MetresPastStop(route, 1, Lat(-180), Lon(500))!.Value, 1);
        Assert.Equal(40.0, ClearedCrossingHolds.MetresPastStop(route, 1, Lat(-60), Lon(505))!.Value, 1);
        Assert.Equal(0.0, ClearedCrossingHolds.MetresPastStop(route, 1, Lat(-100), Lon(500))!.Value, 1);
        Assert.Null(ClearedCrossingHolds.MetresPastStop(route, 1, Lat(-60), Lon(560)));
        Assert.Null(ClearedCrossingHolds.MetresPastStop(route, 9, Lat(-60), Lon(500)));
    }
}
