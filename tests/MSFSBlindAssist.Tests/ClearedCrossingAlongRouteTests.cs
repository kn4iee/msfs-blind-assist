// The Continue before the line and the Hold short key ([CLR-3]) against the countdown as it now runs, along the route
// (RouteAhead.NextCountdownStop): the countdown can name a stop several short navdata pieces ahead of the segment the
// cursor is on, so both judge the stop the countdown counts down, never the cursor's own segment.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearedCrossingAlongRouteTests
{
    private static int _nodeId = 1;

    private static TaxiRouteSegment Seg(string taxiway, double metres, string? holdLabel = null)
        => new()
        {
            FromNode = new TaxiNode { NodeId = _nodeId++ },
            ToNode = new TaxiNode { NodeId = _nodeId++ },
            TaxiwayName = taxiway,
            DistanceMeters = metres,
            IsHoldShortPoint = holdLabel != null,
            HoldShortRunway = holdLabel,
            TurnDirection = "straight",
        };

    // Along A to runway 22R's hold at A, the navdata's pieces into it 28.0, 6.4 and 2.0 m long, then across 22R and on.
    private static TaxiRoute ToTheHoldOn22R() => new()
    {
        Segments = new List<TaxiRouteSegment>
        {
            Seg("A", 94.0),
            Seg("A", 41.0),
            Seg("A", 60.0),
            Seg("A", 28.0),
            Seg("A", 6.4),
            Seg("A", 2.0, holdLabel: "runway 22R at A"),
            Seg("A", 70.0),
            Seg("B", 300.0),
        },
    };

    // ---- Continue before the line ----

    // The countdown said "Hold short runway 22R at A in 350 feet." with the cursor still on the 60 m piece, three pieces
    // short of the hold: a Continue then clears that stop, as one pressed on the hold's own segment always did.
    [Fact]
    public void A_Continue_before_the_line_clears_the_stop_the_countdown_named_along_the_route()
    {
        var route = ToTheHoldOn22R();
        int holdNode = route.Segments[5].ToNode.NodeId;

        int stop = ClearedCrossingHolds.EarlyContinueStop(route.Segments, cursor: 2, legEnd: false,
            countdownNodeId: holdNode, countdownSpoken: true);

        Assert.Equal(5, stop);
        var seg = route.Segments[stop];
        Assert.True(ClearedCrossingHolds.CanClearEarly(true, seg.IsHoldShortPoint, seg.HoldShortRunway,
            isFinalSegment: stop >= route.Segments.Count - 1));
        Assert.True(ClearedCrossingHolds.ClearAt(route, stop + 1, "22R"));
        Assert.True(ClearedCrossingHolds.IsFullyCleared(route.Segments[5]));
    }

    // Not before the countdown has named it, and never for a stop the countdown's latches are not about (another node):
    // a stray key never clears a runway the pilot has not been told about.
    [Fact]
    public void A_Continue_before_the_countdown_names_the_stop_clears_nothing()
    {
        var route = ToTheHoldOn22R();
        int holdNode = route.Segments[5].ToNode.NodeId;

        Assert.Equal(-1, ClearedCrossingHolds.EarlyContinueStop(route.Segments, cursor: 2, legEnd: false,
            countdownNodeId: holdNode, countdownSpoken: false));
        Assert.Equal(-1, ClearedCrossingHolds.EarlyContinueStop(route.Segments, cursor: 2, legEnd: false,
            countdownNodeId: route.Segments[2].ToNode.NodeId, countdownSpoken: true));
    }

    // The route's last segment (the departure runway's own hold, the end of a leg) is never cleared early, however far
    // along the route the countdown named it from.
    [Fact]
    public void The_last_segment_s_stop_is_never_cleared_early()
    {
        var route = ToTheHoldOn22R();
        route.Segments.RemoveRange(6, 2);
        int holdNode = route.Segments[5].ToNode.NodeId;

        int stop = ClearedCrossingHolds.EarlyContinueStop(route.Segments, cursor: 2, legEnd: false,
            countdownNodeId: holdNode, countdownSpoken: true);

        Assert.Equal(5, stop);
        var seg = route.Segments[stop];
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, seg.IsHoldShortPoint, seg.HoldShortRunway,
            isFinalSegment: stop >= route.Segments.Count - 1));
    }

    // Once cleared it is the countdown's cleared crossing: a second press finds no hold short there to clear.
    [Fact]
    public void A_stop_already_cleared_is_not_cleared_again()
    {
        var route = ToTheHoldOn22R();
        int holdNode = route.Segments[5].ToNode.NodeId;
        Assert.True(ClearedCrossingHolds.ClearAt(route, 6, "22R"));

        int stop = ClearedCrossingHolds.EarlyContinueStop(route.Segments, cursor: 2, legEnd: false,
            countdownNodeId: holdNode, countdownSpoken: true);

        Assert.Equal(5, stop);
        var seg = route.Segments[stop];
        Assert.False(ClearedCrossingHolds.CanClearEarly(true, seg.IsHoldShortPoint, seg.HoldShortRunway,
            isFinalSegment: stop >= route.Segments.Count - 1));
    }

    // ---- the Hold short key ----

    // The key puts the cleared stop back while the cursor is still three pieces short of it: it is the countdown's next
    // stop, so its countdown starts fresh (a Slow down and a Stop to come), as it did for a stop on the current segment.
    [Fact]
    public void A_stop_put_back_ahead_of_the_cursor_restarts_its_countdown()
    {
        var route = ToTheHoldOn22R();
        Assert.True(ClearedCrossingHolds.ClearAt(route, 6, "22R"));
        Assert.True(ClearedCrossingHolds.RestoreAt(route, 5));

        Assert.True(ClearedCrossingHolds.RestoreRestartsCountdown(route.Segments, cursor: 2, restoredSegmentIndex: 5, legEnd: false));
        Assert.True(ClearedCrossingHolds.RestoreRestartsCountdown(route.Segments, cursor: 5, restoredSegmentIndex: 5, legEnd: false));
    }

    // Another stop before it is the one being counted down: its latches stay as they are, or its last call would come
    // again and cut the key's sentence off.
    [Fact]
    public void A_stop_put_back_beyond_another_stop_leaves_that_stop_s_countdown_alone()
    {
        var route = ToTheHoldOn22R();
        route.Segments[3] = Seg("A", 28.0, holdLabel: "taxiway K");
        Assert.True(ClearedCrossingHolds.ClearAt(route, 6, "22R"));
        Assert.True(ClearedCrossingHolds.RestoreAt(route, 5));

        Assert.False(ClearedCrossingHolds.RestoreRestartsCountdown(route.Segments, cursor: 2, restoredSegmentIndex: 5, legEnd: false));
    }

    // The key judges distance as the countdown does: along the route, the current segment's end and the pieces after it
    // (MetresToStop), against the countdown's own outer call (OuterCountdownFt). 28.4 m out at 13.6 kt the key's sentence
    // is the outer call; 300 m out the outer call still comes.
    [Fact]
    public void The_key_s_outer_call_is_judged_on_the_distance_along_the_route()
    {
        Assert.True(ClearedCrossingHolds.RestoreSaysOuterCall(28.4, 13.6));
        Assert.False(ClearedCrossingHolds.RestoreSaysOuterCall(300.0, 13.6));
    }
}
