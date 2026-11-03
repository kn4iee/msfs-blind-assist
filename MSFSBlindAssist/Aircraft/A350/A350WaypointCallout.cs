namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The next waypoint, said as the captain's ND's TO waypoint changes (plan 08; the owner's answer to question 7, 2026-10-31):
/// "Next waypoint {name}", the name as the ND draws it. A name is taken once two reads agree, so a torn copy's garbage for one
/// read is never said; a blank TO field (the route's end, a discontinuity) says nothing and keeps what was said, so the same
/// waypoint back after it is not news. The first agreed name, and every one while the call-outs settle after a flight load,
/// is the silent baseline; so is each on the ground, where the owner wants it quiet (plan 08's flight, 2026-11-03). One while
/// held (the flare) waits: said when the hold ends (a go-around), taken silently if the ground comes first (a landing). Pure.
/// </summary>
public sealed class A350WaypointCallout
{
    public const string Key = "A350_ND_NEXT_WAYPOINT";
    public const string Label = "Next waypoint call-out";

    private string? _candidate;
    private string? _current;

    /// <summary>One read of the TO waypoint's name (null or empty when the ND draws none): the sentence to say, else null.
    /// <paramref name="settling"/> takes a change silently, <paramref name="held"/> keeps it for a later read.</summary>
    public string? Observe(string? name, bool settling, bool held = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _candidate = null;
            return null;
        }
        if (!string.Equals(name, _candidate, StringComparison.Ordinal))
        {
            _candidate = name;
            return null;
        }
        if (string.Equals(name, _current, StringComparison.Ordinal))
            return null;
        if (held && !settling && _current != null)
            return null;
        bool baseline = settling || _current == null;
        _current = name;
        return baseline ? null : "Next waypoint " + name;
    }
}
