// The capture of a hold passed BESIDE its node (HoldShortAbeam, [HLD-24], from main's #369) against the step list's
// own stops, after merging main on 2026-11-27: a crossing the pilot was cleared across, a hold short of a taxiway or a
// holding point on the way, the end of a leg with no destination, and the Hold short key putting a cleared stop back.
// TaxiGuidanceManager has no test harness [VP-1]: the geometry is the pure HoldShortAbeam and
// TaxiGuidanceManager.TryMeasureHoldAbeam, the stops are made by the pure pass, ClearedCrossingHolds and
// HoldingPointHoldShort, and the manager's gate is pinned from its source.
//
// Decided at the merge: the capture takes every stop that is a hold short on the way, whatever it holds short of. A
// taxiway or holding-point stop passed beside is stopped level with its line as a runway hold is, and the missed-turn
// guard ([HLD-25]) has no runway to judge it by, so it never holds it off (main's own "a stop the route placed itself,
// a named holding point" case). Not taken: a cleared crossing (no longer a hold short: its line is said as the
// aircraft passes it, never "Stop"), and the route's last segment, a leg end's or the destination's.

using System.IO;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.RunwayFixture;
using Answer = MSFSBlindAssist.Navigation.ClearedCrossingHolds.HoldShortKeyAnswer;
using KeyState = MSFSBlindAssist.Navigation.ClearedCrossingHolds.HoldShortKeyState;

namespace MSFSBlindAssist.Tests;

public class HoldShortAbeamStepListTests
{
    private static TaxiGraph.RunwayCenterline[] Runway09() => new[] { EastWest() };

    // Northbound on A across 09/27 (its centreline at north 0): the pass's stop is Segments[0], ending at node 2,
    // 100 m south of the centreline.
    private static TaxiRoute AcrossOnA()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        foreach (var s in route.Segments) s.TaxiwayName = "A";
        Pass(route, Runway09());
        Assert.True(route.Segments[0].IsHoldShortPoint);
        return route;
    }

    private static TaxiRoute ClearedAcrossOnA()
    {
        var route = AcrossOnA();
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09") }, recalculation: false);
        return route;
    }

    // Eastbound on A, far from any runway: node 3 (east 400) is where a stop on the way is put.
    private static TaxiRoute EastOnA()
    {
        var route = RouteOf(Node(1, 0, -2000), Node(2, 200, -2000), Node(3, 400, -2000), Node(4, 600, -2000));
        foreach (var s in route.Segments) s.TaxiwayName = "A";
        Pass(route, Runway09());
        Assert.DoesNotContain(route.Segments, s => s.IsHoldShortPoint);
        return route;
    }

    // What the capture measures: level with the hold node or up to 40 m past it, within 75 m to the side, heading
    // within 60 degrees of the hold segment ([HLD-24]).
    private static bool LevelWithTheLine(TaxiRoute route, int segment, double eastM, double northM, double headingTrue)
        => TaxiGuidanceManager.TryMeasureHoldAbeam(route.Segments, segment, Lat(northM), Lon(eastM),
               out double along, out double cross, out double axis)
           && HoldShortAbeam.IsReached(along, cross, TaxiGraph.NormalizeAngle(headingTrue - axis));

    // A crossing the pilot was cleared across is no longer a hold short, so the capture's gate never takes it: the
    // geometry alone would. Its line is said where the cursor passes it (AdvanceSegment, the segment jump:
    // ClearedCrossingWiringTests), "Crossing runway 09.", and AdvanceSegment stops only a hold short.
    [Fact]
    public void A_cleared_crossing_passed_beside_its_line_is_never_stopped()
    {
        var route = ClearedAcrossOnA();
        var stop = route.Segments[0];

        Assert.False(stop.IsHoldShortPoint);
        Assert.True(ClearedCrossingHolds.IsFullyCleared(stop));
        // Level with node 2, 35 m to its east, northbound.
        Assert.True(LevelWithTheLine(route, 0, 535, -100, 0.0));

        Assert.Matches(@"if \(_currentSegmentIndex < _route\.Segments\.Count - 1 && currentSeg\.IsHoldShortPoint\s+&& TryMeasureHoldAbeam\(",
            ManagerSource());
        string advance = Member(RoutingSource(), "private void AdvanceSegment(");
        int held = advance.IndexOf("if (completedSeg.IsHoldShortPoint)", StringComparison.Ordinal);
        int stopSaid = advance.IndexOf("HandleHoldShort(completedSeg);", StringComparison.Ordinal);
        int lineSaid = advance.IndexOf("if (ClearedCrossingHolds.IsFullyCleared(completedSeg)) PassClearedLine(completedSeg);", StringComparison.Ordinal);
        Assert.True(held >= 0 && stopSaid > held && lineSaid > stopSaid,
            "AdvanceSegment stops only a hold short, and says a cleared line as it passes it");
    }

    // A stop shared with another runway the pilot was NOT cleared across stays a hold short for that one, and is
    // taken beside its line like any runway hold: one Continue still owed ([HLD-11]).
    [Fact]
    public void A_stop_still_holding_for_another_runway_is_taken_beside_its_line()
    {
        var (route, runways) = Intersection();
        Pass(route, runways);
        int stopIndex = route.Segments.FindIndex(s => s.IsHoldShortPoint);
        Assert.True(stopIndex >= 0);
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09") }, recalculation: false);

        var stop = route.Segments[stopIndex];
        Assert.True(stop.IsHoldShortPoint);
        Assert.False(ClearedCrossingHolds.IsFullyCleared(stop));
        Assert.Contains("01", stop.HoldShortRunway);
        Assert.DoesNotContain("09", stop.HoldShortRunway);
        // 35 m beside the stop node, square to the segment, travelling along it.
        double bearing = NavigationCalculator.CalculateBearing(stop.FromNode.Latitude, stop.FromNode.Longitude,
            stop.ToNode.Latitude, stop.ToNode.Longitude);
        double rad = (bearing + 90.0) * Math.PI / 180.0;
        double east = EastOf(stop.ToNode) + 35.0 * Math.Sin(rad), north = NorthOf(stop.ToNode) + 35.0 * Math.Cos(rad);
        Assert.True(LevelWithTheLine(route, stopIndex, east, north, bearing));
    }

    private static double EastOf(TaxiNode n) => (n.Longitude - BaseLon) * M * Math.Cos(BaseLat * Math.PI / 180.0);
    private static double NorthOf(TaxiNode n) => (n.Latitude - BaseLat) * M;

    // A hold short of a taxiway on the way (ApplyUserTaxiwayHoldShorts' label) and a holding point
    // (HoldingPointHoldShort.Place) are hold shorts like a runway's: passed beside, the capture takes them. Neither
    // node guards a runway, so the missed-turn guard has nothing to hold the capture off by.
    [Theory]
    [InlineData("taxiway")]
    [InlineData("holding point")]
    public void A_taxiway_or_holding_point_stop_on_the_way_is_taken_beside_its_line(string kind)
    {
        var route = EastOnA();
        if (kind == "taxiway")
        {
            route.Segments[1].IsHoldShortPoint = true;
            route.Segments[1].HoldShortRunway = "taxiway B";
        }
        else
        {
            Assert.True(new HoldingPointHoldShort("L2", route.Segments[1].ToNode.NodeId).Place(route.Segments));
            Assert.Equal("holding point L2", route.Segments[1].HoldShortRunway);
        }

        Assert.True(route.Segments[1].IsHoldShortPoint);
        // Level with node 3, 40 m to its north, eastbound; 30 m past it, 50 m to its south.
        Assert.True(LevelWithTheLine(route, 1, 400, -1960, 90.0));
        Assert.True(LevelWithTheLine(route, 1, 430, -2050, 90.0));
        // Still short of it, or past the 40 m: not yet, or no longer, at the line.
        Assert.False(LevelWithTheLine(route, 1, 380, -1960, 90.0));
        Assert.False(LevelWithTheLine(route, 1, 445, -1960, 90.0));
        Assert.False(HoldShortAbeam.IsRollingOnBehindTheLine(metresBehindLine: null, steeringErrorDegrees: 90.0));

        // The manager's gate asks only whether the segment is a hold short, never what it holds short of, and the
        // guard's distance behind the line is null for a node the incursion guard does not know.
        string manager = ManagerSource();
        Assert.Contains("double? behindLineM = MetresBehindHoldLine(currentSeg.ToNode, lat, lon);", manager);
        Assert.Contains("if (!_incursionNodeGeometry.TryGetValue(holdNode.NodeId, out var geometry)",
            Member(ServicesSource("TaxiGuidanceManager.MathUtils.cs"), "private double? MetresBehindHoldLine("));
    }

    // The end of a leg with no destination is the route's last segment, which the capture never takes: the leg end
    // has its own arrival ([STR-25], ProgressiveLegEnd.Arrived), as the destination's hold has its own captures.
    [Fact]
    public void A_leg_end_is_never_taken_by_the_capture()
    {
        string manager = ManagerSource();

        Assert.Matches(@"if \(_currentSegmentIndex < _route\.Segments\.Count - 1 && currentSeg\.IsHoldShortPoint", manager);
        string countdown = Member(manager, "private void CheckHoldShortCountdown(");
        Assert.Matches(@"var legEnd = _currentSegmentIndex == _route\.Segments\.Count - 1", countdown);
    }

    // The Hold short key puts a cleared stop back only while the aircraft is short of its line or beside it, never past
    // it ([CLR-3]): a stop put back beside its line is a hold short again, and the capture then stops the aircraft
    // there. Before main's capture nothing would: off the 25 m circle, the cursor stayed on the stop for good.
    [Fact]
    public void A_stop_the_Hold_short_key_puts_back_beside_its_line_is_then_taken()
    {
        var route = ClearedAcrossOnA();
        // Level with node 2, 35 m to its east: off every segment's 30 m projection, so not past it by the route.
        var key = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 535, -100));

        Assert.Equal(Answer.RestoreSegment, key.Answer);
        Assert.Equal(0, key.SegmentIndex);
        Assert.True(ClearedCrossingHolds.RestoreAt(route, key.SegmentIndex));
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.True(LevelWithTheLine(route, 0, 535, -100, 0.0));
    }

    // Past the line by the route, the key only reports and puts nothing back, so the capture's 40 m past the node never
    // reaches a stop the key has refused.
    [Theory]
    [InlineData(-85.0)]   // 15 m past the node, on the centreline
    [InlineData(-65.0)]   // 35 m past
    public void Past_the_line_the_key_puts_nothing_back_for_the_capture_to_take(double northM)
    {
        var route = ClearedAcrossOnA();

        var key = ClearedCrossingHolds.DecideHoldShortKey(route, Taxiing(0, 500, northM));

        Assert.Equal(Answer.PastTheLine, key.Answer);
        Assert.False(route.Segments[0].IsHoldShortPoint);
    }

    private static KeyState Taxiing(int segment, double eastM, double northM)
        => new(Guiding: true, CurrentSegmentIndex: segment, Holding: false, HoldingAtDestination: false,
               Aircraft: new ClearedCrossingHolds.HoldShortKeyAircraft(Lat(northM), Lon(eastM), 10.0),
               Runways: Runway09(), Passed: null, DestinationName: "Gate 12");

    // A member's text: from its declaration to the next member at class level.
    private static string Member(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{declaration} not found");
        var next = Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
    }

    private static string ManagerSource() => ServicesSource("TaxiGuidanceManager.cs");
    private static string RoutingSource() => ServicesSource("TaxiGuidanceManager.Routing.cs");

    private static string ServicesSource(string file)
        => File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", file));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
