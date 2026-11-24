namespace MSFSBlindAssist.Navigation;

/// <summary>Which spoken step of the end-of-leg countdown is being composed.</summary>
public enum CountdownTier { Outer, SlowDown, Stop }

/// <summary>
/// Decides when a Progressive Taxi leg with no destination has ended, and words the countdown to its end.
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

    public static bool Arrived(ProgressiveTerminatorType type, double distToEndM, double groundSpeedKts,
                               bool slowDownSpoken, bool nearARunway)
    {
        bool stood = groundSpeedKts <= StandstillMaxKts;
        if (EndsAtAHold(type))
            return distToEndM < CaptureRadiusM
                || (stood && (slowDownSpoken || distToEndM <= StandstillCaptureM));
        return !nearARunway
            && (distToEndM < CaptureRadiusM || (stood && distToEndM <= StandstillCaptureM));
    }

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
}
