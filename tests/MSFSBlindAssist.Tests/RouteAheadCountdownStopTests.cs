// The hold-short countdown's own stops on this branch, counted ALONG THE ROUTE as main's #370 counts a hold short
// (RouteAhead.NextCountdownStop): the end of a leg with no destination (Progressive Taxi) and a stop the pilot was
// cleared across. Before, each was counted down only while the CURRENT segment ended at it, and the cursor moves on
// 25 m before a segment's end, so the countdown could start no earlier than 25 m plus that segment's length out:
// a leg whose last segment is 2 m long heard its first line 27 m (89 ft) out, inside every tier at taxi speed.

using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

// The spoken-line tests set the global DistanceFormatter.UnitProvider.
[Collection("DistanceUnitGlobalState")]
public class RouteAheadCountdownStopTests
{
    private const double CaptureRadiusM = 25.0;   // TaxiGuidanceManager.WAYPOINT_CAPTURE_RADIUS_M
    private const double MetresPerSecondPerKnot = 0.5144;
    private const double LookaheadM = 600.0 / 3.28084;   // HOLD_SHORT_COUNTDOWN_LOOKAHEAD_M, the outer tier's cap

    private static int _nodeId = 1;

    private static TaxiRouteSegment Seg(string taxiway, double metres, bool hold = false, double turnDeg = 0.0,
                                        string? holdLabel = null)
        => new()
        {
            FromNode = new TaxiNode { NodeId = _nodeId++ },
            ToNode = new TaxiNode { NodeId = _nodeId++ },
            TaxiwayName = taxiway,
            DistanceMeters = metres,
            IsHoldShortPoint = hold,
            HoldShortRunway = hold ? holdLabel ?? "runway 04L at C" : null,
            TurnAngleDegrees = turnDeg,
            TurnDirection = Math.Abs(turnDeg) < 20.0 ? "straight" : turnDeg > 0 ? "right" : "left",
        };

    // A leg "via P, L, hold short of taxiway M": its stop is set back before M, and the navdata's pieces into it are
    // 28.0, 6.4 and 2.0 m long (the shape of KEWR's 04L hold on AA, 2026-11-26).
    private static List<TaxiRouteSegment> LegToItsEnd() => new()
    {
        Seg("P", 94.0),
        Seg("P", 41.0),
        Seg("L", 60.0, turnDeg: -30.0),
        Seg("L", 28.0),
        Seg("L", 6.4),
        Seg("L", 2.0),
    };

    // ---- (a) the end of a leg with no destination ----

    [Fact]
    public void A_leg_end_is_counted_down_through_the_short_pieces_before_it()
    {
        // On the 28 m piece, 20 m from its end: 20 + 6.4 + 2.0 to the end of the leg.
        var stop = RouteAhead.NextCountdownStop(LegToItsEnd(), currentIndex: 3, toCurrentEndMetres: 20.0, LookaheadM,
            legEnd: true, cleared: true);

        Assert.NotNull(stop);
        Assert.Equal(5, stop.Value.Index);
        Assert.Equal(28.4, stop.Value.AlongMetres, 3);
        Assert.Equal(RouteAhead.CountdownStopKind.LegEnd, stop.Value.Kind);
    }

    [Fact]
    public void A_leg_end_is_found_from_where_its_outer_line_is_due()
    {
        // 344 ft (105 m) out, where the outer line is due at 13.6 kt: still on the 60 m piece of L.
        var stop = RouteAhead.NextCountdownStop(LegToItsEnd(), currentIndex: 2, toCurrentEndMetres: 68.6, LookaheadM,
            legEnd: true, cleared: true);

        Assert.NotNull(stop);
        Assert.Equal(5, stop.Value.Index);
        Assert.Equal(105.0, stop.Value.AlongMetres, 3);
        Assert.Equal(RouteAhead.CountdownStopKind.LegEnd, stop.Value.Kind);
    }

    [Fact]
    public void A_route_with_a_destination_has_no_leg_end_to_count_down()
    {
        Assert.Null(RouteAhead.NextCountdownStop(LegToItsEnd(), currentIndex: 3, toCurrentEndMetres: 20.0, LookaheadM,
            legEnd: false, cleared: true));
    }

    [Fact]
    public void A_leg_end_beyond_the_lookahead_is_not_counted_down_yet()
    {
        Assert.Null(RouteAhead.NextCountdownStop(LegToItsEnd(), currentIndex: 0, toCurrentEndMetres: 90.0, LookaheadM,
            legEnd: true, cleared: true));
    }

    [Fact]
    public void A_hold_short_before_the_leg_end_is_counted_down_first()
    {
        var route = LegToItsEnd();
        route[3] = Seg("L", 28.0, hold: true, holdLabel: "runway 24L at L");

        var stop = RouteAhead.NextCountdownStop(route, currentIndex: 2, toCurrentEndMetres: 30.0, LookaheadM,
            legEnd: true, cleared: true);

        Assert.NotNull(stop);
        Assert.Equal(3, stop.Value.Index);
        Assert.Equal(58.0, stop.Value.AlongMetres, 3);
        Assert.Equal(RouteAhead.CountdownStopKind.HoldShort, stop.Value.Kind);
    }

    // A "Hold short" step at the leg's own end makes its last segment a hold short too: one countdown, the leg end's
    // (led by that hold's label, ProgressiveLegEnd.CountdownLead), never both.
    [Fact]
    public void A_last_segment_that_is_a_hold_short_is_counted_down_as_the_leg_end()
    {
        var route = LegToItsEnd();
        route[5] = Seg("L", 2.0, hold: true, holdLabel: "taxiway M");

        var stop = RouteAhead.NextCountdownStop(route, currentIndex: 3, toCurrentEndMetres: 20.0, LookaheadM,
            legEnd: true, cleared: true);

        Assert.NotNull(stop);
        Assert.Equal(5, stop.Value.Index);
        Assert.Equal(RouteAhead.CountdownStopKind.LegEnd, stop.Value.Kind);
    }

    /// <summary>
    /// The leg walked at 13.6 kt, frame by frame, with the cursor moving on 25 m before each segment's end and never
    /// past the last segment ([STR-1]), as the manager's does: within the outer tier's 600 ft cap the countdown's
    /// distance is the distance still to go along the route on every frame, so the outer (344 ft), slow-down (184 ft)
    /// and stop (92 ft) tiers are each crossed where its lead puts it, before the leg ends inside its 25 m capture
    /// (ProgressiveLegEnd.CaptureRadiusM). On the last segment alone the first frame came 27 m (89 ft) out, inside all
    /// three: one "… Stop." line, then the leg's end.
    /// </summary>
    [Fact]
    public void Walking_a_leg_whose_last_segment_is_2_m_every_tier_comes_at_its_lead()
    {
        var (outerAtFt, slowAtFt, stopAtFt) = Walk(LegToItsEnd(), RouteAhead.CountdownStopKind.LegEnd, legEnd: true, cleared: true);

        Assert.InRange(outerAtFt!.Value, Outer(13.6) - 3.0, Outer(13.6));
        Assert.InRange(slowAtFt!.Value, Slow(13.6) - 3.0, Slow(13.6));
        Assert.InRange(stopAtFt!.Value, Stop(13.6) - 3.0, Stop(13.6));
    }

    // What the pilot hears on that leg at 13.6 kt, in feet and in metres, under a comma-decimal culture: on the last
    // segment alone, one "Stop." line 27 m out, then the leg's end; along the route, all three lines.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_leg_end_s_spoken_lines_on_a_2_m_last_segment(string culture)
    {
        var term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, "M");
        string Line(CountdownTier tier, string distance)
            => ProgressiveLegEnd.Countdown(ProgressiveLegEnd.CountdownLead(term, null), distance, tier);

        var (before, after) = SpokenBothWays(LegToItsEnd(), culture, Line, legEnd: true, cleared: true);

        Assert.Equal(new[] { "Hold short taxiway M in 75 feet. Stop." }, before.Feet);
        Assert.Equal(new[] { "Hold short taxiway M in 25 metres. Stop." }, before.Metres);
        Assert.Equal(new[]
        {
            "Hold short taxiway M in 350 feet.",
            "Hold short taxiway M in 175 feet. Slow down.",
            "Hold short taxiway M in 100 feet. Stop.",
        }, after.Feet);
        Assert.Equal(new[]
        {
            "Hold short taxiway M in 100 metres.",
            "Hold short taxiway M in 55 metres. Slow down.",
            "Hold short taxiway M in 30 metres. Stop.",
        }, after.Metres);
    }

    // ---- the walk ----

    // The lines heard walking the route, on the last segment alone (before) and along the route (after), in both units.
    private static ((List<string> Feet, List<string> Metres) Before, (List<string> Feet, List<string> Metres) After) SpokenBothWays(
        List<TaxiRouteSegment> route, string culture, Func<CountdownTier, string, string> line, bool legEnd, bool cleared)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUnit = DistanceFormatter.UnitProvider;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            int last = route.Count - 1;
            List<string> Say(DistanceUnit unit, bool alongTheRoute)
            {
                DistanceFormatter.UnitProvider = () => unit;
                return Spoken(route, line, (cursor, toCursorEnd) => alongTheRoute
                    ? RouteAhead.NextCountdownStop(route, cursor, toCursorEnd, LookaheadM, legEnd, cleared)?.AlongMetres
                    : cursor == last ? toCursorEnd : null);
            }
            return ((Say(DistanceUnit.Feet, false), Say(DistanceUnit.Metres, false)),
                    (Say(DistanceUnit.Feet, true), Say(DistanceUnit.Metres, true)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            DistanceFormatter.UnitProvider = savedUnit;
        }
    }

    // The countdown's lines walking the route at 13.6 kt to its last 25 m, worded as CheckHoldShortCountdown words them:
    // the first frame inside a tier speaks it, and a frame first inside a deeper tier speaks only that one.
    private static List<string> Spoken(List<TaxiRouteSegment> route, Func<CountdownTier, string, string> line,
                                       Func<int, double, double?> stopDistanceM, double speedKt = 13.6)
    {
        var said = new List<string>();
        double total = route.Sum(s => s.DistanceMeters);
        double stepM = speedKt * MetresPerSecondPerKnot * 0.1;
        int cursor = 0;
        bool outer = false, slow = false, stop = false;
        for (double travelled = 0.0; total - travelled > CaptureRadiusM; travelled += stepM)
        {
            double endOfCursor = route.Take(cursor + 1).Sum(s => s.DistanceMeters);
            while (cursor < route.Count - 1 && endOfCursor - travelled < CaptureRadiusM)
            {
                cursor++;
                endOfCursor = route.Take(cursor + 1).Sum(s => s.DistanceMeters);
            }
            if (stopDistanceM(cursor, endOfCursor - travelled) is not double m) continue;
            double ft = m * 3.28084;
            if (ft < Stop(speedKt)) { outer = true; slow = true; }
            else if (ft < Slow(speedKt)) outer = true;
            if (ft < Outer(speedKt) && !outer) { said.Add(line(CountdownTier.Outer, DistanceFormatter.FromFeet(ft))); outer = true; }
            else if (ft < Slow(speedKt) && !slow) { said.Add(line(CountdownTier.SlowDown, DistanceFormatter.FromFeet(ft))); slow = true; }
            else if (ft < Stop(speedKt) && !stop) { said.Add(line(CountdownTier.Stop, DistanceFormatter.FromFeet(ft))); stop = true; }
        }
        return said;
    }

    private static double Outer(double kt) => Math.Clamp(kt * 1.68781 * 15.0, 300.0, 600.0);
    private static double Slow(double kt) => Math.Clamp(kt * 1.68781 * 8.0, 150.0, 400.0);
    private static double Stop(double kt) => Math.Clamp(kt * 1.68781 * 4.0, 50.0, 200.0);

    // Where each tier is first crossed, in feet along the route, walking the route to its last 25 m at 13.6 kt; every
    // frame within the lookahead must find the stop of `kind` at the distance still to go.
    private static (double? Outer, double? Slow, double? Stop) Walk(List<TaxiRouteSegment> route, RouteAhead.CountdownStopKind kind,
                                                                    bool legEnd, bool cleared, double speedKt = 13.6)
    {
        double total = route.Sum(s => s.DistanceMeters);
        double stepM = speedKt * MetresPerSecondPerKnot * 0.1;
        int cursor = 0;
        double? outerAt = null, slowAt = null, stopAt = null;
        for (double travelled = 0.0; total - travelled > CaptureRadiusM; travelled += stepM)
        {
            double toGo = total - travelled;
            double endOfCursor = route.Take(cursor + 1).Sum(s => s.DistanceMeters);
            while (cursor < route.Count - 1 && endOfCursor - travelled < CaptureRadiusM)
            {
                cursor++;
                endOfCursor = route.Take(cursor + 1).Sum(s => s.DistanceMeters);
            }

            var stop = RouteAhead.NextCountdownStop(route, cursor, endOfCursor - travelled, LookaheadM, legEnd, cleared);
            if (toGo * 3.28084 > 600.0)
            {
                Assert.Null(stop);
                continue;
            }
            Assert.NotNull(stop);
            Assert.Equal(kind, stop.Value.Kind);
            Assert.Equal(route.Count - 1, stop.Value.Index);
            Assert.Equal(toGo, stop.Value.AlongMetres, 6);

            double ft = stop.Value.AlongMetres * 3.28084;
            if (ft < Outer(speedKt)) outerAt ??= ft;
            if (ft < Slow(speedKt)) slowAt ??= ft;
            if (ft < Stop(speedKt)) stopAt ??= ft;
        }
        return (outerAt, slowAt, stopAt);
    }
}
