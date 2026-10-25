using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// Per-variable background-announcement manager for the BlueBird 757-200 (Ctrl+M).
///
/// Rows come from <see cref="MonitorRowBuilder"/> like every other aircraft's: every
/// Continuous + IsAnnounced variable minus those flagged ExcludeFromMonitorManager, so the announced
/// status rows (APU, master warning and caution) and the shared ones (ground state, glideslope,
/// trim). Unticked keys go to UserSettings.BBS757DisabledMonitorVariables, which MainForm honors at
/// the generic gate and through the Suppressed wrap (Services/DefAnnounceMuteSets). All behavior
/// lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class BBS757MonitorManagerForm : MonitorManagerFormBase
{
    public BBS757MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("757 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.BBS757DisabledMonitorVariables;
}
