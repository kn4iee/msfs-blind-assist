using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniBuildsA350Definition
{
    /// <summary>The First Officer's write of one panel row, through the panels' own path, so a flow moves a switch exactly as the pilot's
    /// panel does; true when it was sent or is already there, false when nothing reached the aircraft (no such row, refused, unavailable,
    /// not fitted), never a success over a control nobody moved (FOA-4's lesson, FOA-15).</summary>
    internal bool ApplyForFirstOfficer(string key, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer) =>
        WriteRow(key, value, sim, announcer) is A350WriteOutcome.Sent or A350WriteOutcome.AlreadyThere;

    /// <summary>A held test's press or release for the First Officer: down or up whatever the cockpit reads, never the panel's flip (with the
    /// record late, or the pilot already holding it, a flip would hold it down with no timer to let it go).</summary>
    internal virtual bool HoldForFirstOfficer(string key, bool down, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(key, out var row) || row.Control is not { } control || !A350WritePlan.IsLatched(control))
            return false;
        _sim = sim;
        return HandleLatched(row, control, sim, announcer, down) == A350WriteOutcome.Sent;
    }
}
