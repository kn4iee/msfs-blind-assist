// ClearedCrossingHolds.FindNextStop: the ONE finder behind the Hold short key's "Next stop" answer and the taxi status
// readout's stop clause (spec 8.3), so both name the same stop. Each stop kind, the route's last segment left out or
// kept, the start hold held or cleared, and the composed clause under a comma-decimal culture.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class ClearedCrossingNextStopTests
{
    private static TaxiGraph.RunwayCenterline[] Runway09() => new[] { EastWest() };

    private static ClearedCrossingHolds.HoldShortKeyAircraft At(double east, double north, double kts = 10.0)
        => new(Lat(north), Lon(east), kts);

    // Four segments heading north on A, none a stop yet.
    private static TaxiRoute Plain()
    {
        var route = RouteOf(Node(1, 0, -1000), Node(2, 0, -900), Node(3, 0, -780), Node(4, 0, -700), Node(5, 0, -600));
        foreach (var s in route.Segments) s.TaxiwayName = "A";
        return route;
    }

    private static ClearedCrossingHolds.NextStopInfo? Find(TaxiRoute route, bool leaveOutLast, int cursor = 0, bool skipStart = false,
        ClearedCrossingHolds.HoldShortKeyAircraft? aircraft = null)
        => ClearedCrossingHolds.FindNextStop(route, cursor, aircraft ?? At(0, -1000), Runway09(), "Gate 12",
            leaveOutLast, includeCleared: true, skipStart);

    [Theory]
    [InlineData("runway 09")]
    [InlineData("taxiway L")]
    [InlineData("holding point B3")]
    [InlineData("end of taxiway D")]
    public void A_hold_short_of_any_kind_is_the_next_stop_by_its_label(string label)
    {
        var route = Plain();
        route.Segments[1].IsHoldShortPoint = true;
        route.Segments[1].HoldShortRunway = label;

        var stop = Find(route, leaveOutLast: true);

        Assert.NotNull(stop);
        Assert.Equal(label, stop!.Value.Label);
        Assert.Equal(1, stop.Value.SegmentIndex);
        Assert.False(stop.Value.Cleared);
        Assert.Equal(220.0, stop.Value.Metres, 1);
    }

    [Fact]
    public void A_fully_cleared_crossing_is_named_by_its_placed_label_as_cleared()
    {
        var route = Plain();
        route.Segments[1].IsHoldShortPoint = true;
        route.Segments[1].HoldShortRunway = "runway 09";
        Assert.True(ClearedCrossingHolds.ClearAt(route, 2, "09"));

        var stop = Find(route, leaveOutLast: true);

        Assert.Equal("runway 09", stop!.Value.Label);
        Assert.True(stop.Value.Cleared);
        Assert.Equal(1, stop.Value.SegmentIndex);
    }

    [Fact]
    public void A_partly_cleared_stop_keeps_its_current_label_and_is_not_cleared()
    {
        var route = Plain();
        route.Segments[1].IsHoldShortPoint = true;
        route.Segments[1].HoldShortRunway = "runway 09 and runway 01";
        Assert.True(ClearedCrossingHolds.ClearAt(route, 2, "09"));

        var stop = Find(route, leaveOutLast: true);

        Assert.Equal("runway 01", stop!.Value.Label);
        Assert.False(stop.Value.Cleared);
    }

    [Fact]
    public void The_nearest_stop_wins_and_the_cursor_skips_one_behind()
    {
        var route = Plain();
        route.Segments[0].IsHoldShortPoint = true;
        route.Segments[0].HoldShortRunway = "taxiway L";
        route.Segments[2].IsHoldShortPoint = true;
        route.Segments[2].HoldShortRunway = "runway 27";

        Assert.Equal("taxiway L", Find(route, true)!.Value.Label);
        Assert.Equal("runway 27", Find(route, true, cursor: 1)!.Value.Label);
        Assert.Null(Find(route, true, cursor: 3));
    }

    [Fact]
    public void The_routes_last_segment_is_left_out_for_the_status_and_kept_for_the_key()
    {
        var route = Plain();
        route.Segments[3].IsHoldShortPoint = true;
        route.Segments[3].HoldShortRunway = "runway 27";

        Assert.Null(Find(route, leaveOutLast: true));
        var kept = Find(route, leaveOutLast: false);
        Assert.Equal("runway 27", kept!.Value.Label);
        Assert.Equal(3, kept.Value.SegmentIndex);
    }

    [Fact]
    public void No_stop_is_none()
    {
        Assert.Null(Find(Plain(), leaveOutLast: true));
        Assert.Null(ClearedCrossingHolds.FindNextStop(null, 0, null, null, null, true, true, false));
    }

    [Fact]
    public void A_held_start_hold_is_the_next_stop_only_at_the_start()
    {
        var route = RouteOf(Node(1, 1000, 45), Node(2, 1000, 0), Node(3, 1000, -60), Node(4, 1000, -200));
        Pass(route, Runway09());
        Assert.Equal("runway 09", route.StartHoldRunway);
        var aircraft = At(1000, 45, 0.0);

        var stop = Find(route, true, 0, aircraft: aircraft);
        Assert.Equal("runway 09", stop!.Value.Label);
        Assert.Equal(-1, stop.Value.SegmentIndex);
        Assert.False(stop.Value.Cleared);

        Assert.Null(Find(route, true, cursor: 1, aircraft: aircraft));
        Assert.Equal(-1, Find(route, true, 0, skipStart: false, aircraft: aircraft)!.Value.SegmentIndex);
    }

    [Fact]
    public void A_cleared_start_hold_is_the_next_stop_until_the_aircraft_starts_across()
    {
        var route = RouteOf(Node(1, 1000, 45), Node(2, 1000, 0), Node(3, 1000, -60), Node(4, 1000, -200));
        Pass(route, Runway09());
        ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09") }, recalculation: false);
        Assert.True(ClearedCrossingHolds.IsStartHoldCleared(route));

        var standing = Find(route, true, 0, aircraft: At(1000, 45, 0.0));
        Assert.Equal("runway 09", standing!.Value.Label);
        Assert.True(standing.Value.Cleared);
        Assert.Equal(-1, standing.Value.SegmentIndex);

        // Rolling at the line: started across, so no longer a stop to name.
        Assert.Null(Find(route, true, 0, aircraft: At(1000, 40, 8.0)));
        // The key's mode (cleared crossings left to its other answers) never names a cleared start hold.
        Assert.Null(ClearedCrossingHolds.FindNextStop(route, 0, At(1000, 45, 0.0), Runway09(), "Gate 12",
            leaveOutLast: false, includeCleared: false, skipStart: false));
    }

    [Fact]
    public void The_composed_clause_follows_a_comma_decimal_culture_and_the_unit_option()
    {
        var route = Plain();
        route.Segments[1].IsHoldShortPoint = true;
        route.Segments[1].HoldShortRunway = "runway 09";
        Assert.True(ClearedCrossingHolds.ClearAt(route, 2, "09"));
        route.Segments[2].IsHoldShortPoint = true;
        route.Segments[2].HoldShortRunway = "holding point L2";

        var saved = DistanceFormatter.UnitProvider;
        try
        {
            DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
            string cleared = TestCulture.Run("de-DE", () =>
            {
                var s = Find(route, true)!.Value;
                return ClearedCrossingCallouts.StatusClause(s.Label, TaxiGuidanceManager.FormatDistance(s.Metres), s.Cleared);
            });
            Assert.Equal(", runway 09 in 220 metres, cleared to cross", cleared);

            DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
            string feet = TestCulture.Run("de-DE", () =>
            {
                var s = Find(route, true, cursor: 2)!.Value;
                return ClearedCrossingCallouts.StatusClause(s.Label, TaxiGuidanceManager.FormatDistance(s.Metres), s.Cleared);
            });
            Assert.Equal(", hold short of holding point L2 in 1000 feet", feet);
        }
        finally { DistanceFormatter.UnitProvider = saved; }
    }
}
