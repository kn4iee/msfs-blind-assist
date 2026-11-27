// Main's #370 turn notices (RouteAhead.NextJunction, the route's own direction into a junction further ahead) against
// this branch's Turn picks: the advance notice for a picked turn names the side the pick's own words name ("Turning
// left onto taxiway P, as picked."), however many short navdata pieces lie before it; an off-chart leg's two turns,
// onto the leg and off it onto the picked taxiway, stay two junctions, and the one off it has the side the leg's
// entering sentence names ("… then turn left onto taxiway C."); and the opening turn cue still owns the route's start.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TurnPickAlongRouteNoticeTests
{
    private const double TurnWordM = RouteAhead.DirectionMetres;   // TURN_WORD_APPROACH_M
    private const double MetresPerDegLat = 111_320.0;

    private static int _nodeId = 1;

    private static TaxiNode Node(double northM, double eastM)
        => new() { NodeId = _nodeId++, Latitude = northM / MetresPerDegLat, Longitude = eastM / MetresPerDegLat };

    // A route through the given points (metres north and east of the start), each segment named, as TaxiRouter builds
    // it: its bearing, length, and the turn onto it from the one before.
    private static List<TaxiRouteSegment> Route(params (double N, double E, string Taxiway, bool OffChart)[] points)
    {
        var segments = new List<TaxiRouteSegment>();
        var from = Node(0, 0);
        double? previousBearing = null;
        foreach (var p in points)
        {
            var to = Node(p.N, p.E);
            double bearing = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
            double turn = previousBearing is double b ? ((bearing - b + 540.0) % 360.0) - 180.0 : 0.0;
            segments.Add(new TaxiRouteSegment
            {
                FromNode = from,
                ToNode = to,
                TaxiwayName = p.Taxiway,
                DistanceMeters = TaxiGraph.CalculateDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude),
                BearingDegrees = bearing,
                TurnAngleDegrees = turn,
                TurnDirection = Math.Abs(turn) < 20.0 ? "straight" : turn > 0 ? "right" : "left",
                IsOffChartTurn = p.OffChart,
            });
            from = to;
            previousBearing = bearing;
        }
        return segments;
    }

    // The advance notice's word for a junction beyond the current segment, as CheckUpcomingAnnouncements composes it.
    private static string NoticeWord(List<TaxiRouteSegment> route, int junctionIndex)
        => TaxiGuidanceManager.ComputeTurnVerbalFromHeading(
            RouteAhead.RouteBearingOutOf(route, junctionIndex, TurnWordM),
            RouteAhead.RouteBearingInto(route, junctionIndex, TurnWordM));

    // "Via A, left onto P": north along A, the last pieces before the junction 3 and 2 m long, then west onto P.
    [Fact]
    public void A_picked_turn_s_advance_notice_names_the_pick_s_side_through_the_short_pieces()
    {
        var route = Route((150, 0, "A", false), (195, 0, "A", false), (198, 0, "A", false), (200, 0, "A", false),
                          (200, -100, "P", false), (200, -200, "P", false));

        // Still on the first piece of A, 100 m from its end: 100 + 45 + 3 + 2 m to the junction.
        var junction = RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 100.0, lookaheadMetres: 300.0, TurnWordM);

        Assert.NotNull(junction);
        Assert.Equal(3, junction.Value.Index);
        Assert.Equal(150.0, junction.Value.AlongMetres, 0);
        Assert.Equal("P", RouteAhead.TaxiwayOnto(route, junction.Value.Index));
        Assert.Equal(-90.0, RouteAhead.TurnAngleAt(route, junction.Value.Index, TurnWordM), 0);
        Assert.Equal("left", NoticeWord(route, junction.Value.Index));
        Assert.Equal("Turning left onto taxiway P, as picked.", TurnPickMessages.ChartedNote(TurnSide.Left, "P"));
    }

    // "Via A, left onto C" made off the chart: from A a 60 m leg with no taxi line runs north-west, then turns west onto
    // C. Each turn is its own junction (the leg is longer than the 10 m that merges two points into one corner), the
    // first onto the leg, the second off it onto C, on the side the leg's own sentence names.
    [Fact]
    public void An_off_chart_leg_s_turn_onto_the_picked_taxiway_is_its_own_junction_on_the_pick_s_side()
    {
        double d = 60.0 / Math.Sqrt(2.0);
        var route = Route((100, 0, "A", false), (200, 0, "A", false), (200 + d, -d, "", true),
                          (200 + d, -d - 100, "C", false), (200 + d, -d - 200, "C", false));

        var onto = RouteAhead.NextJunction(route, currentIndex: 1, toCurrentEndMetres: 50.0, lookaheadMetres: 300.0, TurnWordM);
        Assert.NotNull(onto);
        Assert.Equal(1, onto.Value.Index);
        Assert.Equal(1, RouteAhead.RunEnd(route, onto.Value.Index));
        Assert.Equal(-45.0, RouteAhead.TurnAngleAt(route, onto.Value.Index, TurnWordM), 0);

        var offIt = RouteAhead.NextJunction(route, currentIndex: 2, toCurrentEndMetres: 40.0, lookaheadMetres: 300.0, TurnWordM);
        Assert.NotNull(offIt);
        Assert.Equal(2, offIt.Value.Index);
        Assert.False(RouteAhead.ContinuesEarlierJunction(route, offIt.Value.Index));
        Assert.Equal("C", RouteAhead.TaxiwayOnto(route, offIt.Value.Index));
        Assert.Equal(-45.0, RouteAhead.TurnAngleAt(route, offIt.Value.Index, TurnWordM), 0);
        Assert.Equal("slight left", NoticeWord(route, offIt.Value.Index));
        Assert.Equal("Off-chart leg. No taxi line for the next 60 metres. Follow the tone, then turn left onto taxiway C.",
            TurnPickMessages.EnteringLeg(TurnSide.Left, "C", "60 metres"));
    }

    // The opening turn cue still owns the route's start: while it is pending the turn notices are not composed at all,
    // so #370's notice for a junction further ahead never speaks over it; the off-chart leg's sentence is said before
    // that, on entering the leg, as before.
    [Fact]
    public void The_opening_turn_cue_and_the_off_chart_leg_sentence_come_before_the_along_route_notices()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.Announcements.cs"));
        int leg = text.IndexOf("AnnounceInstruction(TurnPickMessages.EnteringLeg(", StringComparison.Ordinal);
        int pending = text.IndexOf("if (_initialTurnPending)", StringComparison.Ordinal);
        int notices = text.IndexOf("if (RouteAhead.NextJunction(_route.Segments, _currentSegmentIndex, distToTargetM,", StringComparison.Ordinal);

        Assert.True(leg >= 0 && pending > leg && notices > pending,
            "the off-chart leg's sentence, then the opening cue's return, then the along-route turn notices");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
