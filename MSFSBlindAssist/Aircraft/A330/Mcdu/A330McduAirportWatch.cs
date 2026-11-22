namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>
/// After the AIRPORT key, where the flight plan went (the plan's "Measured on 2026-11-20": the key moves the F-PLN to
/// the destination, then the alternate, then back to the origin, and the window says which it reached): the first
/// waypoint's name (line 1's data row, its name columns: "KMCO17L") on the next F-PLN page within
/// <see cref="WatchMs"/>, once. A page that is not an F-PLN, or an F-PLN still showing the title and first waypoint
/// row shown at the press (the frame from before the press, as <see cref="A330McduLineKeyWatch"/> takes one), says
/// nothing and the watch goes on; once the time has run, nothing. Pure: the clock is the caller's.
/// </summary>
internal sealed class A330McduAirportWatch
{
    public const int WatchMs = 1500;

    private const int FirstWaypointRow = 2;

    private readonly long _startMs;
    private readonly string _titleBefore, _firstBefore;

    /// <param name="page">The page shown when the key was sent.</param>
    /// <param name="startMs">When the key was sent.</param>
    public A330McduAirportWatch(IReadOnlyList<A330McduLine> page, long startMs)
    {
        _startMs = startMs;
        (_titleBefore, _firstBefore) = Parts(page);
    }

    /// <summary>For a page shown at <paramref name="nowMs"/>: whether the watch is over, and what to say (null: nothing).</summary>
    public (bool Over, string? Say) OnPage(IReadOnlyList<A330McduLine> page, long nowMs)
    {
        if (A330McduLines.IsFlightPlan(page) && Parts(page) != (_titleBefore, _firstBefore))
        {
            string name = A330McduLines.WaypointName(page[FirstWaypointRow]);
            return (true, name.Length > 0 ? name : null);
        }
        return Expired(nowMs) ? (true, null) : (false, null);
    }

    /// <summary>True once the watch has run its time.</summary>
    public bool Expired(long nowMs) => nowMs - _startMs >= WatchMs;

    private static (string Title, string First) Parts(IReadOnlyList<A330McduLine> page) =>
        (page[0].Text.Trim(), page[FirstWaypointRow].Text);
}
