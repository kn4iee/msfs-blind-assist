using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.IniA330;

/// <summary>
/// Per-variable background-announcement manager for the iniBuilds A330 (Ctrl+M). Rows
/// come from <see cref="MonitorRowBuilder"/>: every Continuous + IsAnnounced variable not flagged
/// ExcludeFromMonitorManager, then the rows the display plans add (<see cref="A330CalloutRows.All"/>).
/// Unticked keys go to
/// UserSettings.A330DisabledMonitorVariables, which MainForm honours both through the
/// Suppressed wrap (DefAnnounceMuteSets — flap detents are spoken from inside ProcessSimVarUpdate)
/// and the generic gate; the flight-mode changes, spoken from a settle timer, check the list
/// themselves (row "Flight mode changes").
/// </summary>
public sealed class A330MonitorManagerForm : MonitorManagerFormBase
{
    public A330MonitorManagerForm(Dictionary<string, SimVarDefinition> variables, string name)
        : base($"{name} Monitor Manager",
               A330CalloutRows.Build(MonitorRowBuilder.Build(variables), A330CalloutRows.All, variables.Keys))
    {
    }

    /// <summary>The persisted mute list, re-read on every access (the base class's contract).</summary>
    protected override ICollection<string> DisabledVariables => IniA330Definition.DisabledMonitorList;
}
