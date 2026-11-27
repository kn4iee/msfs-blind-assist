using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>Which spoken step of the end-of-leg countdown is being composed.</summary>
public enum CountdownTier { Outer, SlowDown, Stop }

/// <summary>
/// Decides when a Progressive Taxi leg with no destination has ended, and words the countdown to its end
/// and the refusal of a leg end found only beside a runway.
/// Pure: distances arrive as ready-made text, so this class never writes a unit of its own.
/// </summary>
public static class ProgressiveLegEnd
{
    public const double CaptureRadiusM = 25.0;      // same as a mid-route hold (WAYPOINT_CAPTURE_RADIUS_M)
    public const double StandstillMaxKts = 2.0;     // same as RUNWAY_HOLD_STANDSTILL_MAX_KTS
    public const double StandstillCaptureM = 30.0;  // same as RUNWAY_HOLD_STANDSTILL_CAPTURE_M

    public static bool EndsAtAHold(ProgressiveTerminatorType type) => type is
        ProgressiveTerminatorType.HoldShortRunway
        or ProgressiveTerminatorType.HoldShortTaxiway
        or ProgressiveTerminatorType.HoldAtNamedPoint;

    /// <summary>
    /// Whether the runway the aircraft is on or beside holds the leg end off this frame: a leg never ends,
    /// and BA never asks for a stop, on runway pavement (<paramref name="onRunwayPavement"/>), whatever the
    /// ending; the two endings that are not a hold (across a runway, end of the taxiway) also wait while the
    /// aircraft is within a runway's clear margin (<paramref name="nearARunway"/>). A hold ending's own line
    /// can lie inside that margin, so off the pavement it is judged as a hold short is.
    /// </summary>
    public static bool HeldOffByRunway(ProgressiveTerminatorType type, bool nearARunway, bool onRunwayPavement)
        => onRunwayPavement || (!EndsAtAHold(type) && nearARunway);

    public static bool Arrived(ProgressiveTerminatorType type, double distToEndM, double groundSpeedKts,
                               bool slowDownSpoken, bool nearARunway, bool onRunwayPavement)
    {
        if (HeldOffByRunway(type, nearARunway, onRunwayPavement)) return false;
        bool stood = groundSpeedKts <= StandstillMaxKts;
        if (EndsAtAHold(type))
            return distToEndM < CaptureRadiusM
                || (stood && (slowDownSpoken || distToEndM <= StandstillCaptureM));
        return distToEndM < CaptureRadiusM || (stood && distToEndM <= StandstillCaptureM);
    }

    /// <summary>
    /// How far along the route the end of a leg with no destination lies while it is the hold-short countdown's next
    /// stop (<see cref="RouteAhead.NextCountdownStop"/>: the route's last segment, with no hold short and no stop the
    /// pilot was cleared across before it, within <paramref name="lookaheadM"/>), measured as the countdown measures it:
    /// <paramref name="toCurrentEndM"/> to the end of the current segment, then every segment after it. Null otherwise.
    /// </summary>
    public static double? DistanceAlongTheRoute(IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double toCurrentEndM,
                                                double lookaheadM)
        => RouteAhead.NextCountdownStop(segments, currentIndex, toCurrentEndM, lookaheadM, legEnd: true, cleared: true)
               is { Kind: RouteAhead.CountdownStopKind.LegEnd } stop ? stop.AlongMetres : null;

    /// <summary>
    /// The leg end's own "Slow down": the countdown's latch, only while its latches belong to the route's last node
    /// (<paramref name="countdownNodeId"/>), so a hold short's or a cleared line's before it never ends the leg.
    /// </summary>
    public static bool SlowDownSaidForIt(IReadOnlyList<TaxiRouteSegment> segments, bool slowDownLatched, int countdownNodeId)
        => slowDownLatched && segments.Count > 0 && segments[^1].ToNode?.NodeId == countdownNodeId;

    /// <summary>
    /// Whether the leg has ended while the cursor is still short of the route's last segment: the leg end is counted
    /// down along the route, so its "Slow down" and "Stop." can come there, and the pilot may stop where BA says. Judged
    /// as <see cref="Arrived"/> judges it on the last segment, at <paramref name="alongM"/> (from
    /// <see cref="DistanceAlongTheRoute"/>), and never while the route from the aircraft to the leg end still runs onto a
    /// runway (<see cref="RunwayBeforeTheEnd"/>): past a line the pilot was cleared across and short of that runway's
    /// pavement, the end sentence would hold the aircraft inside the line. On the last segment the arrival is the
    /// manager's own, measured straight to the end, with its past-the-node backstop.
    /// </summary>
    public static bool ArrivedBeforeLastSegment(ProgressiveTerminatorType type, IReadOnlyList<TaxiRouteSegment> segments,
        int currentIndex, double lat, double lon, double alongM, double groundSpeedKts, bool slowDownSpoken,
        bool nearARunway, bool onRunwayPavement, IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
        => currentIndex >= 0 && currentIndex < segments.Count - 1
           && Arrived(type, alongM, groundSpeedKts, slowDownSpoken, nearARunway, onRunwayPavement)
           && !RunwayBeforeTheEnd(segments, currentIndex, lat, lon, runways);

    /// <summary>
    /// Whether the route from the aircraft (on segment <paramref name="currentIndex"/>) to the end of its last segment
    /// runs onto a runway's pavement (<see cref="RunwayShape.IsRunOntoBy"/>, the test the incursion guard's crossing
    /// wording uses): from the aircraft to the current segment's end, then segment by segment.
    /// </summary>
    public static bool RunwayBeforeTheEnd(IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double lat, double lon,
                                          IReadOnlyList<TaxiGraph.RunwayCenterline>? runways)
    {
        if (runways == null || runways.Count == 0 || currentIndex < 0) return false;
        double fromLat = lat, fromLon = lon;
        for (int i = currentIndex; i < segments.Count; i++)
        {
            if (segments[i].ToNode is not { } to) continue;
            for (int r = 0; r < runways.Count; r++)
                if (runways[r] != null && RunwayShape.For(runways[r]).IsRunOntoBy(fromLat, fromLon, to.Latitude, to.Longitude))
                    return true;
            fromLat = to.Latitude;
            fromLon = to.Longitude;
        }
        return false;
    }

    /// <summary>
    /// Whether a countdown tier may be spoken (and latched) now. BA never asks for a stop on a runway:
    /// "Slow down" and "Stop" wait while the runway holds the leg end off (<see cref="HeldOffByRunway"/>,
    /// the same test <see cref="Arrived"/> uses). The outer tier always speaks.
    /// </summary>
    public static bool MaySpeak(ProgressiveTerminatorType type, CountdownTier tier, bool nearARunway, bool onRunwayPavement)
        => tier == CountdownTier.Outer || !HeldOffByRunway(type, nearARunway, onRunwayPavement);

    public static string CountdownLead(ProgressiveTerminator terminator, string? segmentHoldLabel)
    {
        if (!string.IsNullOrEmpty(segmentHoldLabel)) return $"Hold short {segmentHoldLabel}";
        return terminator.Type switch
        {
            ProgressiveTerminatorType.HoldShortRunway => $"Hold short runway {terminator.Target}",
            ProgressiveTerminatorType.HoldShortTaxiway => $"Hold short taxiway {terminator.Target}",
            ProgressiveTerminatorType.HoldAtNamedPoint => $"Hold at {terminator.Target}",
            _ => "Route ends",
        };
    }

    public static string Countdown(string lead, string distanceText, CountdownTier tier)
    {
        string text = $"{lead} in {distanceText}.";
        return tier switch
        {
            CountdownTier.SlowDown => text + " Slow down.",
            CountdownTier.Stop => text + " Stop.",
            _ => text,
        };
    }

    /// <summary>
    /// The refusal when <paramref name="onTaxiway"/> meets <paramref name="taxiway"/> only inside a
    /// runway's clear margin: the pilot's entry is right, so it says why rather than "Check your entry".
    /// </summary>
    public static string NoHoldShortClearOfARunway(string taxiway, string onTaxiway)
        => $"There is no place to hold short of taxiway {taxiway} on {onTaxiway} clear of a runway.";

    /// <summary>The refusal when every end of <paramref name="taxiway"/> lies inside a runway's clear margin.</summary>
    public static string NoEndClearOfARunway(string taxiway)
        => $"Taxiway {taxiway} has no end clear of a runway.";
}
