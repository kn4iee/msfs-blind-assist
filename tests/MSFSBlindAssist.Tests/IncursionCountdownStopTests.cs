// The incursion guard and the countdown's own stops ([RTE-41], #370): the route's next stop the countdown names, within
// its 600 ft along the route or once its countdown has begun, is latched by the guard in silence, never spoken about
// as "Crossing …" or "Passing the … hold line.". On this branch the countdown also names a stop the pilot was cleared
// across and the end of a leg with no destination, so those are the guard's silent next stop too. The cleared line
// itself is still said, once, where the aircraft reaches it ("Crossing runway 04L at C.", PassClearedLine, which needs
// no guard): silenced here, the guard's own "Crossing runway 04L at C." on approach no longer doubles it.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class IncursionCountdownStopTests
{
    private const double LookaheadM = 600.0 / 3.28084;   // HOLD_SHORT_COUNTDOWN_LOOKAHEAD_M

    private static int _nodeId = 1;

    private static TaxiRouteSegment Seg(string taxiway, double metres, string? holdLabel = null)
        => new()
        {
            FromNode = new TaxiNode { NodeId = _nodeId++ },
            ToNode = new TaxiNode { NodeId = _nodeId++, Type = holdLabel != null ? TaxiNodeType.HoldShort : TaxiNodeType.Normal },
            TaxiwayName = taxiway,
            DistanceMeters = metres,
            IsHoldShortPoint = holdLabel != null,
            HoldShortRunway = holdLabel,
            TurnDirection = "straight",
        };

    // Across runway 04L at C, cleared, then on to runway 04R's hold at C: the pieces into C's line are 28.0, 6.4, 2.0 m.
    private static TaxiRoute ClearedAcrossAtC()
    {
        var route = new TaxiRoute
        {
            Segments = new List<TaxiRouteSegment>
            {
                Seg("P", 94.0),
                Seg("C", 60.0),
                Seg("C", 28.0),
                Seg("C", 6.4),
                Seg("C", 2.0, holdLabel: "runway 04L at C"),
                Seg("C", 70.0),
                Seg("C", 90.0, holdLabel: "runway 04R at C"),
                Seg("D", 300.0),
            },
        };
        Assert.True(ClearedCrossingHolds.ClearAt(route, 5, "04L"));
        return route;
    }

    [Fact]
    public void A_cleared_line_the_countdown_counts_down_is_the_guard_s_silent_next_stop()
    {
        var route = ClearedAcrossAtC();
        int lineNode = route.Segments[4].ToNode.NodeId;

        // On the 60 m piece, 30 m from its end: 30 + 28 + 6.4 + 2.0 = 66.4 m along the route to C's line.
        int silent = TaxiGuidanceManager.CountdownStopNodeForGuard(route.Segments, currentIndex: 1, toCurrentEndMetres: 30.0,
            legEnd: false, cleared: true, countdownNodeId: -1, anyTierLatched: false, LookaheadM);

        Assert.Equal(lineNode, silent);
    }

    // While holding nothing is counted down as cleared, and the guard's next stop is the hold beyond it, as on main.
    [Fact]
    public void While_holding_the_guard_s_next_stop_is_the_hold_short_beyond_a_cleared_line()
    {
        var route = ClearedAcrossAtC();

        int silent = TaxiGuidanceManager.CountdownStopNodeForGuard(route.Segments, currentIndex: 3, toCurrentEndMetres: 5.0,
            legEnd: false, cleared: false, countdownNodeId: -1, anyTierLatched: false, LookaheadM);

        Assert.Equal(route.Segments[6].ToNode.NodeId, silent);
    }

    // Past the cleared line, the next stop beyond it: C's line is no longer the countdown's, so the guard is free to
    // judge it (behind the aircraft, [HLD-26]: a crossing still under way, latched by the line's own call).
    [Fact]
    public void Past_the_cleared_line_the_guard_s_next_stop_is_the_one_beyond_it()
    {
        var route = ClearedAcrossAtC();

        int silent = TaxiGuidanceManager.CountdownStopNodeForGuard(route.Segments, currentIndex: 5, toCurrentEndMetres: 40.0,
            legEnd: false, cleared: true, countdownNodeId: route.Segments[4].ToNode.NodeId, anyTierLatched: true, LookaheadM);

        Assert.Equal(route.Segments[6].ToNode.NodeId, silent);
    }

    // The end of a leg with no destination at a runway's hold node: the countdown names it ("Hold short runway 22R in …"),
    // so the guard never calls it "Passing the runway 22R hold line." or "Crossing runway 22R." on the way in.
    [Fact]
    public void A_leg_end_the_countdown_counts_down_is_the_guard_s_silent_next_stop()
    {
        var route = new List<TaxiRouteSegment> { Seg("P", 94.0), Seg("L", 60.0), Seg("L", 28.0), Seg("L", 6.4), Seg("L", 2.0) };
        route[^1].ToNode.Type = TaxiNodeType.HoldShort;

        int silent = TaxiGuidanceManager.CountdownStopNodeForGuard(route, currentIndex: 1, toCurrentEndMetres: 30.0,
            legEnd: true, cleared: true, countdownNodeId: -1, anyTierLatched: false, LookaheadM);

        Assert.Equal(route[^1].ToNode.NodeId, silent);
        // A route with a destination has no leg end: nothing for the guard to keep quiet about there.
        Assert.Equal(-1, TaxiGuidanceManager.CountdownStopNodeForGuard(route, currentIndex: 1, toCurrentEndMetres: 30.0,
            legEnd: false, cleared: true, countdownNodeId: -1, anyTierLatched: false, LookaheadM));
    }

    // Main's own limits stand for every kind: a stop far along the route (a route that comes back near it) is spoken
    // about as before, unless its countdown has begun.
    [Fact]
    public void A_stop_far_along_the_route_is_left_to_the_guard_unless_its_countdown_has_begun()
    {
        var route = ClearedAcrossAtC();
        int lineNode = route.Segments[4].ToNode.NodeId;

        Assert.Equal(-1, TaxiGuidanceManager.CountdownStopNodeForGuard(route.Segments, currentIndex: 0, toCurrentEndMetres: 120.0,
            legEnd: false, cleared: true, countdownNodeId: -1, anyTierLatched: false, LookaheadM));
        Assert.Equal(lineNode, TaxiGuidanceManager.CountdownStopNodeForGuard(route.Segments, currentIndex: 0, toCurrentEndMetres: 120.0,
            legEnd: false, cleared: true, countdownNodeId: lineNode, anyTierLatched: true, LookaheadM));
    }

    // Neither the guard's silent latch (#370) nor its "done with" test (#371) reaches the branch's own crossing lines: the
    // cleared line ("Crossing runway 04L at C.") and the started-across line of a cleared start hold are both said by
    // PassClearedLine, unconditionally, and latch the guard off for that node, so the guard never says them again while
    // the crossing is under way. The guard's silent next stop is the countdown's (CountdownStopNodeForGuard).
    [Fact]
    public void The_guard_never_silences_or_doubles_a_cleared_line_or_the_started_across_line()
    {
        string manager = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        string pass = Member(manager, "private void PassClearedLine(string label, int stopNodeId)");
        Assert.Contains("SpeakNow(ClearedCrossingCallouts.AtTheLine(label));", pass);
        Assert.DoesNotContain("_lastIncursionWarnedNodeId ==", pass);
        Assert.DoesNotContain("IsPlannedLineLeftBehind", pass);
        Assert.Contains("_lastIncursionWarnedNodeId = stopNodeId;", Member(manager, "private void UseClearedCrossing("));
        Assert.Contains("PassClearedLine(ClearedCrossingHolds.LineLabel(_route.PlacedStartHoldLabel, _route.StartHoldClearedRunways), start.NodeId);",
            Member(manager, "private void AnnounceClearedStartCrossing("));

        string guard = Member(manager, "private void CheckRunwayIncursion(");
        Assert.Contains("laterStopHsNodeId = CountdownStopNodeForGuard(_route.Segments, _currentSegmentIndex,", guard);
        Assert.Contains("legEnd: _progressiveTerminator != null && taxiing, cleared: taxiing, _holdShortCountdownNodeId,", guard);
        Assert.Contains("if (pick == null || pick.Value.NodeId == _lastIncursionWarnedNodeId)", guard);
        Assert.Contains("if (pick.Value.OnRoute && pick.Value.NodeId == laterStopHsNodeId)", guard);
        Assert.DoesNotContain("RouteAhead.NextStop(", guard);
    }

    private static string Member(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{declaration} not found");
        var next = System.Text.RegularExpressions.Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
