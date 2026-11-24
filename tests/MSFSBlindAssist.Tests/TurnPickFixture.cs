// Shared helpers for the tests that build a route, then apply the pilot's Turn picks to it
// (TurnPickApplier.Apply) the way LoadRoute does.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.KbosHcJunctionFixture;

namespace MSFSBlindAssist.Tests;

internal static class TurnPickFixture
{
    public static TaxiRoute Route(TaxiGraph g, (double Lat, double Lon) from, (double Lat, double Lon) to,
        params string[] seq)
    {
        var r = new TaxiRouter(g).FindConstrainedPath(Node(g, from), Node(g, to), seq.ToList());
        Assert.NotNull(r);
        Assert.Null(r!.ConstrainedFallbackReason);
        return r;
    }

    public static TurnPickContext Ctx(TaxiGraph g, string[] seq, (double Lat, double Lon) to,
        double heading = 0.0, (double Lat, double Lon)? aircraft = null,
        ProgressiveTerminator? terminator = null, int pinnedNodeId = 0)
    {
        var ac = aircraft ?? to;
        return new TurnPickContext(g, PavementMap.Build(g), seq, Node(g, to), DestinationIsRunway: false,
            ac.Lat, ac.Lon, heading, terminator, pinnedNodeId,
            MaxLengthRatio: 2.0, MaxLengthPadMeters: 500.0);
    }

    public static Dictionary<int, TurnSide> Picks(params (int Row, TurnSide Side)[] picks)
        => picks.ToDictionary(p => p.Row, p => p.Side);
}
