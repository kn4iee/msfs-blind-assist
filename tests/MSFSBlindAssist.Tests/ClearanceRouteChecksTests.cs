// A leg with no destination that ends "hold short of taxiway B" never ends beside a runway: where the last taxiway
// meets B first inside a runway's clear margin, the route crosses B there and holds short of a later meeting, and
// Calculate says so (owner, 2026-11-25: "Warn at Calculate"). The decision (was the first meeting skipped, and beside
// which runway) and the wording are pure (ClearanceRouteChecks, RouteRunwayCrossings.RunwayNameWithinClearMargin);
// the manager's wiring is pinned in ClearanceWiringTests. Every sentence is also checked under a comma-decimal culture.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceRouteChecksTests
{
    private static TaxiRouteSegment Seg(string taxiway, int toNode)
        => new() { TaxiwayName = taxiway, FromNode = new TaxiNode { NodeId = toNode - 1 }, ToNode = new TaxiNode { NodeId = toNode } };

    // Along A: node 3 meets B beside runway 27, node 5 meets B clear of every runway (the leg end).
    private static readonly TaxiRouteSegment[] AlongA = { Seg("A", 2), Seg("A", 3), Seg("A", 4), Seg("A", 5) };

    private static bool TouchesB(int node, string name) => name == "B" && (node == 3 || node == 5);

    private static string? BesideAt3(TaxiNode node) => node.NodeId == 3 ? "27" : null;

    private const string Expected =
        "Taxiway B meets taxiway A first beside runway 27; you will cross B there and hold short of it further on.";

    [Fact]
    public void A_first_meeting_beside_a_runway_names_that_runway()
        => Assert.Equal("27", ClearanceRouteChecks.RunwayBesideFirstMeeting(AlongA, 0, "B", TouchesB, BesideAt3));

    // The leg end itself (the last segment's end node) is never the skipped meeting, whatever lies beside it.
    [Fact]
    public void The_leg_end_is_not_a_skipped_meeting()
    {
        var endOnly = new[] { Seg("A", 2), Seg("A", 4), Seg("A", 5) };
        Assert.Null(ClearanceRouteChecks.RunwayBesideFirstMeeting(endOnly, 0, "B", TouchesB, _ => "27"));
    }

    // A first meeting clear of every runway (one the Cross taxiway tie-breaker passed over) says nothing, even with a
    // meeting beside a runway after it: "first" would not be true.
    [Fact]
    public void A_first_meeting_clear_of_runways_says_nothing()
    {
        bool Touches(int node, string name) => name == "B" && (node == 3 || node == 4 || node == 5);
        Assert.Null(ClearanceRouteChecks.RunwayBesideFirstMeeting(AlongA, 0, "B", Touches, n => n.NodeId == 4 ? "27" : null));
    }

    // A route that drives onto B is not crossing it: no sentence (the leg-end scan treats it as a mismatch too).
    [Fact]
    public void A_route_that_drives_onto_the_taxiway_says_nothing()
    {
        var ontoB = new[] { Seg("A", 2), Seg("A", 3), Seg("b", 4), Seg("A", 5) };
        Assert.Null(ClearanceRouteChecks.RunwayBesideFirstMeeting(ontoB, 0, "B", TouchesB, BesideAt3));
    }

    // Only from the last taxiway's run: a meeting before it is not where the last taxiway meets B.
    [Fact]
    public void Only_meetings_from_the_run_start_count()
    {
        Assert.Null(ClearanceRouteChecks.RunwayBesideFirstMeeting(AlongA, 2, "B", TouchesB, BesideAt3));
        Assert.Null(ClearanceRouteChecks.RunwayBesideFirstMeeting(AlongA, 0, "C", TouchesB, BesideAt3));
        Assert.Null(ClearanceRouteChecks.RunwayBesideFirstMeeting(Array.Empty<TaxiRouteSegment>(), 0, "B", TouchesB, BesideAt3));
    }

    [Fact]
    public void The_warning_says_where_the_route_crosses_the_taxiway_and_that_it_holds_short_further_on()
    {
        Assert.Equal(Expected, ClearanceRouteChecks.MetFirstBesideARunway("B", "A", "27"));
        Assert.Equal(Expected, TestCulture.Run("de-DE", () => ClearanceRouteChecks.MetFirstBesideARunway("B", "A", "27")));
        Assert.Equal(Expected, TestCulture.Run("sv-SE", () => ClearanceRouteChecks.MetFirstBesideARunway("B", "A", "27")));
    }

    // ---- Which runway: the one whose clear margin holds the meeting, named by its end nearer the meeting ----

    private const double OLat = 47.0, OLon = 8.0;

    private static (double Lat, double Lon) P(double x, double y)
        => (OLat + y / 111132.0, OLon + x / (111132.0 * Math.Cos(OLat * Math.PI / 180.0)));

    /// An east-west runway 2 km long on y = 0, its west end "09", its east end "27", 23 m half-width.
    private static TaxiGraph.RunwayCenterline Runway0927()
    {
        var west = P(-1000, 0);
        var east = P(1000, 0);
        return new TaxiGraph.RunwayCenterline
        {
            Name1 = "09", Name2 = "27",
            Lat1 = west.Lat, Lon1 = west.Lon, Lat2 = east.Lat, Lon2 = east.Lon,
            HalfWidthMeters = RunwayShape.DefaultHalfWidthMeters,
            PavementLat1 = west.Lat, PavementLon1 = west.Lon, PavementLat2 = east.Lat, PavementLon2 = east.Lon,
            PavementHalfWidthMeters = RunwayShape.DefaultHalfWidthMeters,
        };
    }

    [Theory]
    [InlineData(700, 28, "27")]    // beside the pavement, inside the 10 m margin, nearer the east end
    [InlineData(-700, -28, "09")]  // the other side, nearer the west end
    [InlineData(1005, 0, "27")]    // past the east end, inside the margin along the axis
    [InlineData(300, 0, "27")]     // on the pavement
    [InlineData(700, 45, null)]    // clear of the margin
    [InlineData(1020, 0, null)]    // past the end and clear of it
    public void The_runway_is_the_one_whose_clear_margin_holds_the_meeting(double x, double y, string? expected)
    {
        var runways = new List<TaxiGraph.RunwayCenterline> { Runway0927() };
        var (lat, lon) = P(x, y);
        Assert.Equal(expected, RouteRunwayCrossings.RunwayNameWithinClearMargin(runways, lat, lon));
        Assert.Equal(expected, TestCulture.Run("de-DE", () => RouteRunwayCrossings.RunwayNameWithinClearMargin(runways, lat, lon)));
    }

    [Fact]
    public void No_runways_no_name()
    {
        var (lat, lon) = P(0, 0);
        Assert.Null(RouteRunwayCrossings.RunwayNameWithinClearMargin(null, lat, lon));
        Assert.Null(RouteRunwayCrossings.RunwayNameWithinClearMargin(new List<TaxiGraph.RunwayCenterline>(), lat, lon));
    }
}
