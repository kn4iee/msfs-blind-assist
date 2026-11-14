using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A pilot's per-row pick "hold short of holding point L2" on an ordinary (runway, gate or
/// place) route: the point's name and the graph node <see cref="NamedHoldingPointResolver"/>
/// resolved it to.
///
/// <para>The stop goes on the segment that ENDS at that node, the same placement the
/// "end of taxiway" and "hold short of taxiway" picks use, and it is the pilot's own stop:
/// nothing here moves or replaces a runway hold ([AUG-4] is about sharpening THOSE from online
/// data, which this does not do). Its label, "holding point L2", names no runway, so every
/// reader of a hold label takes it for the plain hold-short point it is; a runway hold the
/// automatic pass later needs at the same stop becomes "runway 16 at holding point L2"
/// (<see cref="RouteRunwayCrossings.ComposeCrossingLabel"/>).</para>
/// </summary>
public readonly record struct HoldingPointHoldShort(string Name, int NodeId)
{
    /// <summary>The hold label: spoken as "Hold short holding point L2 in 100 metres." and
    /// "Stop. Hold short of holding point L2. Press continue when cleared."</summary>
    public string Label => $"holding point {Name}";

    /// <summary>
    /// Marks the stop on the first segment at or after <paramref name="fromSegment"/> that ends
    /// at the point. False, and nothing changed, when the route does not pass it from there on
    /// (a point the route merely STARTS on is not passed: there is nothing to stop at). A stop
    /// another pick already put there is kept as it is, label and all.
    /// </summary>
    public bool Place(IReadOnlyList<TaxiRouteSegment>? segments, int fromSegment = 0)
    {
        int index = FindSegment(segments, fromSegment);
        if (index < 0) return false;

        var segment = segments![index];
        if (!segment.IsHoldShortPoint)
        {
            segment.IsHoldShortPoint = true;
            segment.HoldShortRunway = Label;
        }
        return true;
    }

    /// <summary>
    /// True while this pick's stop is still ahead of the aircraft on the route being flown: the
    /// route holds at the point, on the current segment or a later one. A recalculation carries
    /// a pick over only then. A stop already passed must not return if the new route runs
    /// through the point again, and one the route never held was reported when it was loaded.
    /// </summary>
    public bool IsStillAhead(IReadOnlyList<TaxiRouteSegment>? segments, int currentSegmentIndex)
    {
        if (segments == null) return false;
        for (int i = Math.Max(0, currentSegmentIndex); i < segments.Count; i++)
            if (segments[i].ToNode?.NodeId == NodeId && segments[i].IsHoldShortPoint) return true;
        return false;
    }

    private int FindSegment(IReadOnlyList<TaxiRouteSegment>? segments, int fromSegment)
    {
        if (segments == null) return -1;
        for (int i = Math.Max(0, fromSegment); i < segments.Count; i++)
            if (segments[i].ToNode?.NodeId == NodeId) return i;
        return -1;
    }
}
