using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// Per-variable background-announcement manager for the iniBuilds A350 (Ctrl+M).
///
/// Rows come from <see cref="MonitorRowBuilder"/> like every other aircraft's: every Continuous + IsAnnounced
/// variable minus those flagged ExcludeFromMonitorManager, so the A350 lists each switch and selector (spoken on
/// MainForm's generic path when something other than the pilot's own pick moves it, the A380's convention) and
/// each light (spoken by the definition as the cockpit lights it). Unticked keys go to
/// UserSettings.A350DisabledMonitorVariables, honored by MainForm at the generic gate and around the definition's
/// ProcessSimVarUpdate. All behavior lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class A350MonitorManagerForm : MonitorManagerFormBase
{
    public A350MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("A350 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A350DisabledMonitorVariables;
}
