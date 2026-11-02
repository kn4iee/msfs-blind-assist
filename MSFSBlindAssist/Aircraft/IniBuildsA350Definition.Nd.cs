using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>The captain's ND as the display monitor draws it each tick (plan 01's seam, <see cref="ObserveCaptainEfis"/>).</summary>
public partial class IniBuildsA350Definition
{
    private readonly A350WaypointCallout _nextWaypoint = new();

    /// <summary>Runs on the monitor's pool thread: reads the capture and speaks only through <paramref name="speak"/>
    /// ([SIM-5], [SIM-20]). The call-out keeps its baseline while muted, so ticking the row never says a stale waypoint.</summary>
    partial void ObserveNd(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted, Action<string> speak)
    {
        if (_nextWaypoint.Observe(A350GroundDisplays.ToWaypointName(capture, 0), settling) is { } said && !muted(A350WaypointCallout.Key))
            speak(said);
    }
}
