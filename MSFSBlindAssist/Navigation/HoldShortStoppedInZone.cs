namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The hold-short countdown's stopped-in-zone "Stop." (TaxiGuidanceManager.CheckHoldShortCountdown): a pilot who slowed
/// to a stop after "Slow down", above the distance where the "Stop." tier is due, is told "Hold short runway 13L at H.
/// Stop." so they know to hold there. Only for a pilot who has rolled since "Slow down" was said: a route loaded at a
/// standstill inside the slow-down tier heard "… in 40 metres. Slow down." and, 0.1 s later with the aircraft still
/// standing, "… Stop.", 38 m short of the line and outside its capture, with nothing after it (VirtualPilot legends,
/// KHOU "via H, hold short of C", 2026-11-27). Pure.
/// </summary>
public static class HoldShortStoppedInZone
{
    /// <summary>Under this the aircraft is standing still.</summary>
    public const double StandstillKts = 1.0;

    /// <summary>
    /// One countdown frame after "Slow down" (<paramref name="slowDownSaid"/>): whether "{lead}. Stop." is due now.
    /// <paramref name="rolledSinceSlowDown"/> is the caller's, started false where "Slow down" is said and set here on
    /// any frame the aircraft rolls, so only a standstill it came to after the line counts.
    /// </summary>
    public static bool SaysStop(bool slowDownSaid, bool stopSaid, ref bool rolledSinceSlowDown, double groundSpeedKts)
    {
        if (!slowDownSaid) return false;
        if (groundSpeedKts >= StandstillKts)
        {
            rolledSinceSlowDown = true;
            return false;
        }
        return !stopSaid && rolledSinceSlowDown;
    }
}
