using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniBuildsA350Definition
{
    /// <summary>The First Officer's write of one panel row, through the panels' own path, so a flow moves a switch exactly as the pilot's
    /// panel does; false when the key is no row (it writes nothing: there is no L:var fallback to report a success over a control nobody
    /// moved, FOA-4's lesson).</summary>
    internal bool ApplyForFirstOfficer(string key, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer) =>
        GetVariables().TryGetValue(key, out var def) && HandleUIVariableSet(key, value, def, sim, announcer);
}
