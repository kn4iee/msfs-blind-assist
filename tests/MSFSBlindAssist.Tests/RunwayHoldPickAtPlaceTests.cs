// A Hold short of runway step with a place ("Hold short of runway 27L at B") stops at that runway's line where the
// route meets it at that place, matched by the ONE at-place rule a Cross step's place is matched by
// (ClearedCrossingHolds.EventAtPlace): the taxiway the route is on at the crossing, or a holding point on the route
// within 200 m before the crossing's stop with no other runway event between, a node before any name. With no match no
// stop is placed for the pick, the automatic hold still stops the pilot where the route meets the runway, and that is
// said. A recalculation carries the place; a leg with no destination that ends at such a step ends at that place's line.

using System;
using System.IO;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;
using AircraftPosition = MSFSBlindAssist.Navigation.RouteRunwayCrossings.AircraftPosition;
using UserRunwayHoldResult = MSFSBlindAssist.Navigation.RouteRunwayCrossings.UserRunwayHoldResult;

namespace MSFSBlindAssist.Tests;

public class RunwayHoldPickAtPlaceTests
{
    // 09/27 crossed twice (ClearedCrossingHoldsTests' TwoCrossings, its taxiways named): north on the first taxiway at
    // east 500 (stop at route node 1, the runway reached at route node 2), round, and south on the second at east 900
    // (stop at route node 5, reached at route node 6).
    private static TaxiRoute TwoCrossings(string first = "P", string second = "Q")
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100),
            Node(5, 900, 300), Node(6, 900, 100), Node(7, 900, 0), Node(8, 900, -100));
        for (int i = 0; i < route.Segments.Count; i++) route.Segments[i].TaxiwayName = i < 3 ? first : second;
        return route;
    }

    private static TaxiRoute Named(TaxiRoute route, string taxiway)
    {
        foreach (var s in route.Segments) s.TaxiwayName = taxiway;
        return route;
    }

    // The pilot's pick of runways[0], placed as LoadRoute places it (ApplyUserRunwayHoldShorts), from runStart.
    private static UserRunwayHoldResult Pick(TaxiRoute route, TaxiGraph.RunwayCenterline[] runways, string runwayId,
        string? at, int atNodeId, out TaxiRouteRunwayEvent? placed, int runStart = 0, AircraftPosition? aircraft = null)
        => RouteRunwayCrossings.ApplyUserRunwayHold(route, runways[0], runways, runwayId, runStart, out placed,
            aircraft, at: at, atNodeId: atNodeId);

    // Review focus 1.
    [Fact]
    public void A_pick_at_the_second_taxiway_stops_at_that_crossing_and_the_first_keeps_its_automatic_stop()
    {
        var runways = new[] { EastWest() };

        // With no place, the first crossing after the row's taxiway, as ever.
        var bare = TwoCrossings();
        Assert.Equal(UserRunwayHoldResult.Held, Pick(bare, runways, "09", null, 0, out var bareEvent));
        Assert.Equal(1, bareEvent!.StopNodeIndex);

        // At Q: Q's crossing, at its hold node, never P's.
        var route = TwoCrossings();
        Assert.Equal(UserRunwayHoldResult.Held, Pick(route, runways, "09", "Q", 0, out var placed));
        Assert.Equal(5, placed!.StopNodeIndex);
        Assert.Equal(6, placed.ReachNodeIndex);
        Assert.True(route.Segments[4].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[4].HoldShortRunway);
        Assert.False(route.Segments[0].IsHoldShortPoint);

        // The automatic pass then stops at P as ever, and shares Q's stop with the pick: one stop per crossing.
        var events = Pass(route, runways);
        Assert.Equal(new[] { 1, 5 }, events.Select(e => e.StopNodeIndex));
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);
        Assert.Equal("runway 09", route.Segments[4].HoldShortRunway);
        Assert.Equal(2, route.Segments.Count(s => s.IsHoldShortPoint));

        // The reciprocal name and the place's letter case make no difference.
        var other = TwoCrossings();
        Assert.Equal(UserRunwayHoldResult.Held, Pick(other, runways, "27", "q", 0, out var otherEvent));
        Assert.Equal(5, otherEvent!.StopNodeIndex);
    }

    // Review focus 2.
    [Fact]
    public void A_pick_at_a_taxiway_the_route_never_meets_the_runway_on_places_no_stop_and_is_said()
    {
        var runways = new[] { EastWest("09R", "27L") };
        var route = TwoCrossings();

        Assert.Equal(UserRunwayHoldResult.NotAtPlace, Pick(route, runways, "27L", "B", 0, out var placed));
        Assert.Null(placed);
        Assert.DoesNotContain(route.Segments, s => s.IsHoldShortPoint);
        Assert.Null(route.StartHoldRunway);

        // The automatic hold still stops the pilot where the route first meets the runway.
        var events = Pass(route, runways);
        Assert.Equal(new[] { 1, 5 }, events.Select(e => e.StopNodeIndex));
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09R", route.Segments[0].HoldShortRunway);

        Assert.Equal("Runway 27L is not met at B. You will be stopped where the route meets it.",
            RouteRunwayCrossings.NotMetAt("27L", "B"));

        // A runway the route never meets at all is not met somewhere else: the route does not cross it, as before.
        var far = new[] { EastWest("09R", "27L", northM: 5000) };
        Assert.Equal(UserRunwayHoldResult.NotOnRoute, Pick(TwoCrossings(), far, "27L", "B", 0, out var none));
        Assert.Null(none);
    }

    // The manager says the note with the route's warning, and never defers such a pick: there is nothing to retry.
    [Fact]
    public void The_manager_passes_the_place_says_a_pick_not_met_there_and_never_defers_it()
    {
        string routing = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.Routing.cs"));
        Assert.Contains("IReadOnlyList<KeyValuePair<int, Navigation.RunwayHoldPick>>? userRunwayHoldShorts", routing);

        string apply = Member(routing, "private string? ApplyUserRunwayHoldShorts(");
        Assert.Contains("IReadOnlyList<KeyValuePair<int, Navigation.RunwayHoldPick>> userRunwayHoldShorts", apply);
        Assert.Contains("at: pick.At, atNodeId: pick.AtNodeId);", apply);
        Assert.Contains("row, pick.At, pick.AtNodeId));", apply);
        var notAtPlace = Regex.Match(apply,
            @"case RouteRunwayCrossings\.UserRunwayHoldResult\.NotAtPlace:(?<body>[^}]*?)break;");
        Assert.True(notAtPlace.Success, "ApplyUserRunwayHoldShorts has a NotAtPlace case");
        Assert.Contains("RouteRunwayCrossings.NotMetAt(runwayId, pick.At)", notAtPlace.Groups["body"].Value);
        Assert.DoesNotContain("deferred", notAtPlace.Groups["body"].Value);

        // A pick deferred for a stop keeps its place, and is retried at it after the automatic pass.
        string retry = Member(routing, "private string? RetryDeferredRunwayPicks(");
        Assert.Contains("at: pick.At, atNodeId: pick.AtNodeId)", retry);
    }

    // A holding point names the crossing whose stop it lies within 200 m before, along the route, with no other runway
    // event between: the rule a Cross step's holding point is matched by.
    [Fact]
    public void A_pick_at_a_holding_point_within_200_m_before_a_crossings_stop_is_placed_at_that_crossing()
    {
        var runways = new[] { EastWest() };
        // 09/27 crossed twice, both on P, so no name tells them apart: north at east 500 (stop at route node 1), round,
        // and south at east 900 past A2 (node 6, route node 5), 150 m before the second stop (node 7, route node 6).
        TaxiRoute Route() => Named(RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100),
            Node(5, 900, 400), Node(6, 900, 250), Node(7, 900, 100), Node(8, 900, 0), Node(9, 900, -100)), "P");

        var bare = Route();
        Assert.Equal(UserRunwayHoldResult.Held, Pick(bare, runways, "09", null, 0, out var bareEvent));
        Assert.Equal(1, bareEvent!.StopNodeIndex);

        var route = Route();
        Assert.Equal(UserRunwayHoldResult.Held, Pick(route, runways, "09", "A2", 6, out var placed));
        Assert.Equal(6, placed!.StopNodeIndex);
        Assert.True(route.Segments[5].IsHoldShortPoint);
        Assert.False(route.Segments[0].IsHoldShortPoint);

        // 300 m before it (node 5): somewhere else.
        var tooFar = Route();
        Assert.Equal(UserRunwayHoldResult.NotAtPlace, Pick(tooFar, runways, "09", "A2", 5, out var tooFarEvent));
        Assert.Null(tooFarEvent);
        Assert.DoesNotContain(tooFar.Segments, s => s.IsHoldShortPoint);
    }

    [Fact]
    public void A_holding_point_with_another_runway_between_it_and_the_stop_is_not_that_crossings()
    {
        // South on P at east 900: A2 (node 2, route node 1) 40 m short of runway 08/26, crossed at node 3; 09's stop is
        // node 5 (route node 4), 190 m along the route from A2.
        TaxiRoute Route() => Named(RouteOf(Node(1, 900, 450), Node(2, 900, 290), Node(3, 900, 250), Node(4, 900, 200),
            Node(5, 900, 100), Node(6, 900, 0), Node(7, 900, -100)), "P");
        var r09 = EastWest();
        var r08 = EastWest("08", "26", northM: 250, halfWidthM: 15);

        // With 08/26 not there, A2 is 09's place.
        var alone = Route();
        Assert.Equal(UserRunwayHoldResult.Held, Pick(alone, new[] { r09 }, "09", "A2", 2, out var aloneEvent));
        Assert.Equal(4, aloneEvent!.StopNodeIndex);

        // With 08/26 between, it is 08/26's, never 09's.
        var route = Route();
        Assert.Equal(UserRunwayHoldResult.NotAtPlace, Pick(route, new[] { r09, r08 }, "09", "A2", 2, out var placed));
        Assert.Null(placed);
        Assert.DoesNotContain(route.Segments, s => s.IsHoldShortPoint);
    }

    // Review focus 4.
    [Fact]
    public void A_recalculation_carries_the_place()
    {
        // What the manager keeps for a recalculation, re-keyed onto the rows still ahead: each pick with its place.
        var picks = new List<KeyValuePair<int, RunwayHoldPick>>
        {
            new(1, new RunwayHoldPick("09", "Q")),
            new(-1, new RunwayHoldPick("27", "A2", 6)),
        };
        var carried = ClearanceRows.After(ClearanceProgressState.Empty.CarriedOver(picks, 2, ClearanceTargetKind.Runway,
            segments: null, cursor: 0, holding: false), rowsBehind: 1);
        Assert.Equal(new[]
        {
            new KeyValuePair<int, RunwayHoldPick>(0, new RunwayHoldPick("09", "Q")),
            new KeyValuePair<int, RunwayHoldPick>(-1, new RunwayHoldPick("27", "A2", 6)),
        }, carried);
        // Each is remembered by its runway, as a stop was by its name.
        Assert.Equal(new[] { "09", "27" }, PlacedStop.Entered(picks, null, null).Select(s => s.Name));

        // The recalculated route, from the aircraft, never started held [HLD-10]: the pick still stops at Q.
        var runways = new[] { EastWest() };
        var route = TwoCrossings();
        var aircraft = new AircraftPosition(Lat(-300), Lon(500), MayStartHeld: false);
        var pick = carried[0].Value;
        Assert.Equal(UserRunwayHoldResult.Held,
            Pick(route, runways, pick.Runway, pick.At, pick.AtNodeId, out var placed, aircraft: aircraft));
        Assert.Equal(5, placed!.StopNodeIndex);
    }

    // ---- a leg with no destination that ends "Hold short of runway 09 at C" ----

    // HoldShortNodeResolverTests' frame: runway 09 due north from (37.0, -122.0); "lateral" is metres west of it.
    private const double REF_LAT = 37.0;
    private const double BASE_LON = -122.0;
    private const double DEG_TO_M_LAT = 111320.0;

    private static double LatN(double alongM) => REF_LAT + alongM / DEG_TO_M_LAT;

    private static double LonW(double lateralWestM)
        => BASE_LON - lateralWestM / (DEG_TO_M_LAT * Math.Cos(REF_LAT * Math.PI / 180.0));

    private static Runway Runway09() => new()
    {
        RunwayID = "09", Heading = 0.0, HeadingMag = 0.0,
        StartLat = REF_LAT, StartLon = BASE_LON, EndLat = LatN(3000), EndLon = BASE_LON,
        Length = 3000.0 / 0.3048, Width = 200.0,
    };

    private static TaxiPath Edge(double alongA, double lateralA, string typeA, double alongB, double lateralB, string typeB,
        string name) => new()
    {
        StartLat = LatN(alongA), StartLon = LonW(lateralA), StartType = typeA,
        EndLat = LatN(alongB), EndLon = LonW(lateralB), EndType = typeB, Name = name, Width = 50,
    };

    private static TaxiNode NodeNear(TaxiGraph graph, double alongM, double lateralWestM)
        => graph.Nodes.Values.OrderBy(n => TaxiGraph.FastDistanceMeters(LatN(alongM), LonW(lateralWestM), n.Latitude, n.Longitude)).First();

    [Fact]
    public void A_leg_ending_hold_short_of_a_runway_at_a_taxiway_ends_at_that_taxiways_line_not_the_last_via_taxiways()
    {
        // The parallel taxiway P 300 m west; A (at along 500) and C (at along 1200) run east from it to a scenery hold
        // line 90 m from the centreline. The aircraft is on P at A; the last Via step is A. An island X lies far away.
        var graph = TaxiGraph.Build(new List<TaxiPath>
        {
            Edge(500, 300, "", 1200, 300, "", "P"),
            Edge(500, 300, "", 500, 90, "HS", "A"),
            Edge(1200, 300, "", 1200, 90, "HS", "C"),
            Edge(2500, 2000, "", 2600, 2000, "", "X"),
        }, new List<ParkingSpot>(), new List<StartPosition>());
        var lineA = NodeNear(graph, 500, 90);
        var lineC = NodeNear(graph, 1200, 90);
        lineA.HoldShortName = "runway 09";
        lineC.HoldShortName = "runway 09";
        var island = NodeNear(graph, 2500, 2000);
        double lat = LatN(500), lon = LonW(300);
        int component = graph.FindNearestNode(lat, lon)!.ComponentId;
        var runway = Runway09();

        // As before, with no place: the last Via taxiway's line.
        Assert.Equal(lineA.NodeId, HoldShortNodeResolver.ResolveNearSide(graph, runway, lat, lon, 0, "A")!.NodeId);

        // At C: C's line.
        var atC = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "09", At: "C");
        Assert.Equal(lineC.NodeId, ClearanceLegEnd.HoldShortRunwayAt(graph, runway, atC, component, lat, lon, 0));

        // At a taxiway with no line of this runway: none, never another taxiway's line.
        var atZ = atC with { At = "Z" };
        Assert.Equal(-1, ClearanceLegEnd.HoldShortRunwayAt(graph, runway, atZ, component, lat, lon, 0));

        // At a holding point: its own node, when the aircraft can reach it.
        var atPoint = atC with { At = "C1", AtNodeId = lineC.NodeId };
        Assert.Equal(lineC.NodeId, ClearanceLegEnd.HoldShortRunwayAt(graph, runway, atPoint, component, lat, lon, 0));
        var offIsland = atC with { At = "X1", AtNodeId = island.NodeId };
        Assert.Equal(-1, ClearanceLegEnd.HoldShortRunwayAt(graph, runway, offIsland, component, lat, lon, 0));

        // No place: nothing here (the window then anchors on the last Via taxiway, as before).
        var noPlace = atC with { At = null };
        Assert.Equal(-1, ClearanceLegEnd.HoldShortRunwayAt(graph, runway, noPlace, component, lat, lon, 0));
    }

    [Fact]
    public void The_window_ends_a_leg_at_the_steps_place_before_the_last_via_taxiway()
    {
        string form = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));
        string leg = Member(form, "private void CalculateLegWithNoDestination(");
        int at = leg.IndexOf("ClearanceLegEnd.HoldShortRunwayAt(graph, ", StringComparison.Ordinal);
        int lastVia = leg.IndexOf("ResolveHoldShortRunwayNode(runwayTarget, lastVia)", StringComparison.Ordinal);
        Assert.True(at >= 0 && lastVia > at, "the step's place is looked up before the last Via taxiway");

        // The picks reach LoadRoute with their places, at both Calculate paths; the names-only adapter is gone.
        Assert.Equal(2, Regex.Matches(form, @"userRunwayHoldShorts: plan\.RunwayHoldPicks\(\),").Count);
        Assert.DoesNotContain("RunwayStopNames(", form);
    }

    private static string Member(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{declaration} not found");
        var next = Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
