using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>The captain's ND as the display monitor draws it each tick (plan 01's seam, <see cref="ObserveCaptainEfis"/>).</summary>
public partial class IniBuildsA350Definition
{
    private readonly A350WaypointCallout _nextWaypoint = new();

    /// <summary>Below this radio altitude (the A350's LAND mode height) the next waypoint is held: on an approach the only change
    /// there is the threshold's, to the missed approach, said in the flare on plan 08's flight (2026-11-03).</summary>
    private const int WaypointHeldBelowFeet = 400;

    /// <summary>Runs on the monitor's pool thread: reads the capture and speaks only through <paramref name="speak"/>
    /// ([SIM-5], [SIM-20]); the ground is the sim's cache, which is safe to read here. The call-out keeps its baseline while
    /// muted, so ticking the row never says a stale waypoint; on the ground it takes each change silently, and below 400 ft it
    /// holds one for a go-around (the owner: quiet on the ground and in the flare).</summary>
    partial void ObserveNd(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted, Action<string> speak)
    {
        bool held = A350Pfd.RadioAltitude(capture, 0) is < WaypointHeldBelowFeet;
        if (_nextWaypoint.Observe(A350GroundDisplays.ToWaypointName(capture, 0), settling || IsOnGroundNow(), held) is { } said
            && !muted(A350WaypointCallout.Key))
            speak(said);
    }
}
