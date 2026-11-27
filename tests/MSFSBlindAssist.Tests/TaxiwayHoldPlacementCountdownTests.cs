// A taxiway stop placed by setback ([TWH-1]) and a named holding point's stop ([HLD-23]) are hold shorts like any other:
// since main's #370 the countdown measures to them along the route (RouteAhead.NextCountdownStop), and the stop it
// names is read from the stop's own segment (its label, "taxiway M" or "holding point L2"), whichever segment the
// cursor is on. On the navdata's short pieces before such a stop the countdown used to start on its last piece only.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiwayHoldPlacementCountdownTests
{
    private const double LookaheadM = 600.0 / 3.28084;   // HOLD_SHORT_COUNTDOWN_LOOKAHEAD_M

    private static int _nodeId = 1;

    private static TaxiRouteSegment Seg(string taxiway, double metres)
        => new()
        {
            FromNode = new TaxiNode { NodeId = _nodeId++ },
            ToNode = new TaxiNode { NodeId = _nodeId++ },
            TaxiwayName = taxiway,
            DistanceMeters = metres,
            TurnDirection = "straight",
        };

    // "Via P, L, hold short of taxiway M", mid-route: L meets M at the end of segment 4, and the navdata's pieces before
    // the setback node are 9, 4 and 2 m long.
    private static List<TaxiRouteSegment> ToTheJunctionWithM() => new()
    {
        Seg("P", 200.0),
        Seg("L", 9.0),
        Seg("L", 4.0),
        Seg("L", 2.0),
        Seg("L", 35.0),
        Seg("M", 100.0),
    };

    [Fact]
    public void A_setback_taxiway_stop_is_counted_down_along_the_route_by_its_own_label()
    {
        var route = ToTheJunctionWithM();
        double setback = TaxiwayHoldPlacement.SetbackMetres(36.0);
        var stop = TaxiwayHoldPlacement.Choose(route, junctionSegmentIndex: 4, setback, allowInsert: false, (_, _) => true);
        Assert.Equal(TaxiwayHoldPlacement.StopKind.ExistingNode, stop.Kind);
        Assert.Equal(3, stop.SegmentIndex);   // the end of the 2 m piece, 35 m before M
        route[stop.SegmentIndex].IsHoldShortPoint = true;
        route[stop.SegmentIndex].HoldShortRunway = "taxiway M";

        // Still on P, 100 m from its end: 100 + 9 + 4 + 2 to the stop.
        var next = RouteAhead.NextCountdownStop(route, currentIndex: 0, toCurrentEndMetres: 100.0, LookaheadM, legEnd: false, cleared: true);

        Assert.NotNull(next);
        Assert.Equal(3, next.Value.Index);
        Assert.Equal(115.0, next.Value.AlongMetres, 3);
        Assert.Equal(RouteAhead.CountdownStopKind.HoldShort, next.Value.Kind);
        Assert.Equal("taxiway M", route[next.Value.Index].HoldShortRunway);
        // Never M's own junction: a turn there belongs to the countdown, as at any stop.
        Assert.Null(RouteAhead.NextJunction(route, currentIndex: 1, toCurrentEndMetres: 5.0, lookaheadMetres: 300.0));
    }

    // A hold short of a named holding point stops on the segment ending at the point's node, and its label names no
    // runway: counted down along the route the same way, by that label.
    [Fact]
    public void A_holding_point_stop_is_counted_down_along_the_route_by_its_own_label()
    {
        var route = ToTheJunctionWithM();
        route[3].IsHoldShortPoint = true;
        route[3].HoldShortRunway = "holding point L2";

        var next = RouteAhead.NextCountdownStop(route, currentIndex: 1, toCurrentEndMetres: 6.0, LookaheadM, legEnd: false, cleared: true);

        Assert.NotNull(next);
        Assert.Equal(3, next.Value.Index);
        Assert.Equal(12.0, next.Value.AlongMetres, 3);
        Assert.Equal("holding point L2", route[next.Value.Index].HoldShortRunway);
    }

    // At a leg's end the same setback stop ends the route, and the leg end's words lead with its label.
    [Fact]
    public void A_setback_leg_end_is_counted_down_along_the_route_in_the_leg_end_s_words()
    {
        var route = ToTheJunctionWithM().Take(4).ToList();   // ApplyLegEndSetback cuts the route at the stop
        var term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, "M");

        var next = RouteAhead.NextCountdownStop(route, currentIndex: 0, toCurrentEndMetres: 100.0, LookaheadM, legEnd: true, cleared: true);

        Assert.NotNull(next);
        Assert.Equal(3, next.Value.Index);
        Assert.Equal(115.0, next.Value.AlongMetres, 3);
        Assert.Equal(RouteAhead.CountdownStopKind.LegEnd, next.Value.Kind);
        var last = route[next.Value.Index];
        Assert.Equal("Hold short taxiway M",
            ProgressiveLegEnd.CountdownLead(term, last.IsHoldShortPoint ? last.HoldShortRunway : null));
    }
}
