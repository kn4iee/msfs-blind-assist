// Main's #367 (merged into the Taxi UI Redesign on 2026-11-27): a last cleared taxiway that links across ANOTHER
// runway is driven and routed on to the destination ([RTE-17], TaxiRouter.OnwardLegOverAnotherRunway), with the
// automatic hold at the runway it crosses. Against this branch's step list:
// - with a destination, a Cross step for that runway clears that crossing (ClearedCrossingHolds, [CLR-1]): no stop,
//   and the summary says "cleared across"; without one the automatic hold stands;
// - a Progressive Taxi leg (no destination) ends on its last taxiway and is routed as no runway destination, so #367
//   never applies: a leg whose last taxiway is such a link ends at its last point clear of the runway
//   (ClearanceLegEnd.EndOfTaxiway).
// The field is TaxiRouterCrossingClearanceTests' two-runway layout, with one node more on K4 (a point clear of the
// south runway for a leg to end at).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearedCrossingOnwardLegTests
{
    private static TaxiPath P(double la1, double lo1, double la2, double lo2, string name) => new()
    {
        StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75.0,
    };

    private static Runway R(string id, double la1, double lo1, double la2, double lo2) => new()
    {
        AirportICAO = "TEST", RunwayID = id, StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        Length = 10938, Width = 197, Heading = lo2 > lo1 ? 90 : 270,
    };

    private const double DestLat = 0.008, DestLon = 0.029;

    // K south of both runways (centrelines at lat 0.004 and 0.008), M between them. K4 (west) and K9 (east) cross the
    // south runway; K4's far side (M7) angles back west, so K9 is the shorter way to the destination on the north
    // runway's east end and the router reads K4 as a branch until #367 drives it on.
    private static TaxiGraph Field()
    {
        var paths = new List<TaxiPath>
        {
            P(0.002, 0.000, 0.002, 0.005, "K"), P(0.002, 0.005, 0.002, 0.025, "K"), P(0.002, 0.025, 0.002, 0.030, "K"),
            P(0.006, 0.000, 0.006, 0.002, "M"), P(0.006, 0.002, 0.006, 0.010, "M"), P(0.006, 0.010, 0.006, 0.020, "M"),
            P(0.006, 0.020, 0.006, 0.025, "M"), P(0.006, 0.025, 0.006, 0.029, "M"), P(0.006, 0.029, 0.006, 0.030, "M"),
            P(0.002, 0.005, 0.003, 0.005, "K4"), P(0.003, 0.005, 0.0037, 0.005, "K4"),
            P(0.0037, 0.005, 0.0043, 0.005, ""), P(0.0043, 0.005, 0.006, 0.002, "M7"),
            P(0.002, 0.025, 0.0037, 0.025, "K9"), P(0.0037, 0.025, 0.0043, 0.025, ""), P(0.0043, 0.025, 0.006, 0.025, "M13"),
            P(0.006, 0.029, DestLat, DestLon, "M15"),
        };
        var runways = new List<Runway>
        {
            R("09R", 0.004, 0.000, 0.004, 0.030), R("27L", 0.004, 0.030, 0.004, 0.000),
            R("09L", 0.008, 0.000, 0.008, 0.030), R("27R", 0.008, 0.030, 0.008, 0.000),
        };
        var starts = runways.Select(r => new StartPosition
        {
            RunwayName = r.RunwayID, Latitude = r.StartLat, Longitude = r.StartLon, Heading = r.Heading,
        }).ToList();
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
        Assert.Equal(2, g.RunwayCenterlines.Count);
        return g;
    }

    private static int NodeAt(TaxiGraph g, double lat, double lon)
        => g.Nodes.Values.OrderBy(n => TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude)).First().NodeId;

    private static ClearanceStep Via(string taxiway) => new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, taxiway);
    private static ClearanceStep Cross(string runway) => new(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, runway);

    // The route a destination clearance gets, with the automatic pass and then the clearing, as AdoptRoute runs them
    // ([HLD-4], then ClearedCrossingHolds.Apply with each Cross step's crossing bound from its taxiway step on).
    private static (TaxiRoute Route, ClearedCrossingHolds.Outcome Outcome) Calculate(TaxiGraph g, params ClearanceStep[] steps)
    {
        var plan = ClearancePlanner.Build(steps, hasDestination: true);
        Assert.Null(plan.Refusal);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            plan.TaxiwaySequence.ToList(), destinationIsRunway: true);
        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.RunwayCenterlines, "Runway 27R");
        int RunStart(int seq) => route.Segments.FindIndex(s => s.TaxiwayName == plan.TaxiwaySequence[seq]);
        var outcome = ClearedCrossingHolds.Apply(route, plan.Crossings, recalculation: false, RunStart);
        return (route, outcome);
    }

    private static bool HoldsFor(TaxiRouteSegment s, string runway)
        => s.IsHoldShortPoint && (s.HoldShortRunway ?? "").Contains(runway, StringComparison.OrdinalIgnoreCase);

    private static string Names(TaxiRoute route) => string.Join(",", route.Segments.Select(s => s.TaxiwayName));

    [Fact]
    public void A_Cross_step_clears_the_crossing_the_onward_leg_makes()
    {
        var g = Field();

        var (route, outcome) = Calculate(g, Via("K"), Cross("09R"), Via("K4"));

        // #367: K4 is driven and the route goes on over 09R/27L by M7, not round by K9.
        Assert.Contains("M7", Names(route));
        Assert.DoesNotContain("K9", Names(route));
        Assert.Equal(NodeAt(g, DestLat, DestLon), route.Segments[^1].ToNode.NodeId);

        var applied = Assert.Single(outcome.Applied);
        Assert.Equal("09R", applied.Runway);
        Assert.DoesNotContain(route.Segments, s => HoldsFor(s, "09R") || HoldsFor(s, "27L"));
        var stop = Assert.Single(route.Segments, ClearedCrossingHolds.IsFullyCleared);
        Assert.Equal("K4", stop.TaxiwayName);
        Assert.StartsWith("cleared across runway 09R", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));
        Assert.DoesNotContain("crossing runway 09R", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));
    }

    // The Cross step after the link (ATC's "via K, K4, cross runway 9 right") clears the same crossing.
    [Fact]
    public void A_Cross_step_after_the_link_clears_it_too()
    {
        var (route, outcome) = Calculate(Field(), Via("K"), Via("K4"), Cross("09R"));

        Assert.Single(outcome.Applied);
        Assert.DoesNotContain(route.Segments, s => HoldsFor(s, "09R") || HoldsFor(s, "27L"));
    }

    [Fact]
    public void Without_a_Cross_step_the_automatic_hold_stands()
    {
        var (route, outcome) = Calculate(Field(), Via("K"), Via("K4"));

        Assert.Empty(outcome.Applied);
        var stop = Assert.Single(route.Segments, s => HoldsFor(s, "09R") || HoldsFor(s, "27L"));
        Assert.Equal("K4", stop.TaxiwayName);
        Assert.DoesNotContain("cleared across", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));
        Assert.Contains("crossing runway 09R", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));
    }

    // Progressive Taxi "via K, K4" with no destination: the window ends the leg at the end of K4, its last point clear
    // of the south runway, and loads it as no runway destination; the router stops there. #367's onward leg needs a
    // runway destination its last taxiway branches off, and a leg's end lies on its last taxiway itself.
    [Fact]
    public void A_leg_whose_last_taxiway_is_such_a_link_ends_clear_of_the_runway()
    {
        var g = Field();
        var plan = ClearancePlanner.Build(new[] { Via("K"), Via("K4") }, hasDestination: false);
        Assert.Equal(ClearanceEndKind.EndOfTaxiway, plan.End);
        int junction = NodeAt(g, 0.002, 0.005);
        var entry = g.Nodes[junction];

        var end = ClearanceLegEnd.EndOfTaxiway(g, junction, "K4", travelBearingTrue: 0.0, entry.Latitude, entry.Longitude);

        Assert.Equal(NodeAt(g, 0.003, 0.005), end.EndNodeId);
        Assert.Equal(NodeAt(g, 0.0037, 0.005), end.ArmEndNodeId);
        var endNode = g.Nodes[end.EndNodeId];
        Assert.Null(RouteRunwayCrossings.RunwayWithinClearMargin(g.RunwayCenterlines, endNode.Latitude, endNode.Longitude));

        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), end.EndNodeId,
            plan.TaxiwaySequence.ToList(), destinationIsRunway: false);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(end.EndNodeId, route.Segments[^1].ToNode.NodeId);
        Assert.Equal(new[] { "K", "K4" }, RouteTaxiwaySequence.DistinctConsecutive(route.Segments));
        Assert.All(route.Segments, s => Assert.Null(
            RouteRunwayCrossings.RunwayWithinClearMargin(g.RunwayCenterlines, s.ToNode.Latitude, s.ToNode.Longitude)));
        Assert.Empty(RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.RunwayCenterlines, ""));
    }

    // The same clearance to the north runway's destination goes on over the south runway (#367's own case), so the leg
    // above ends short of it because it has no destination, not because the field has no way on.
    [Fact]
    public void The_same_taxiways_to_a_runway_destination_go_on_over_the_runway()
    {
        var g = Field();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K4" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Contains("M7", Names(route!));
        Assert.Equal(NodeAt(g, DestLat, DestLon), route!.Segments[^1].ToNode.NodeId);
    }
}
