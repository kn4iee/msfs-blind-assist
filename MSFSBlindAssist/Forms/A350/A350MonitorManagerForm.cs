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
        : base("A350 Monitor Manager", BuildRows(variables)) { }

    /// <summary>The variable rows, then one row per display call-out the A350's display reader speaks
    /// (appended after the sort, as the A380's E/WD fold is): the E/WD call-outs, which Ctrl+E flips; each FMA column;
    /// the FMS messages heard with the MFD window closed; each take-off speed, said as it is entered and called on the
    /// roll (<see cref="Aircraft.A350.A350TakeoffCallouts"/>); each approach minimum (<see cref="Aircraft.A350.A350MinimumsCallout"/>); BTV's
    /// distance to the exit and runway remaining (<see cref="Aircraft.A350.A350BtvCountdown"/>); the PFD's flags, the TCAS's advisories, new printouts and low memory, one row each.</summary>
    internal static List<MonitorRow> BuildRows(IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        var rows = MonitorRowBuilder.Build(variables);
        rows.Add(new MonitorRow(Aircraft.A350.Display.A350EcamMonitor.MuteKey, Aircraft.A350.Display.A350EcamMonitor.MuteLabel));
        foreach (var column in Aircraft.A350.Display.A350FmaAnnouncer.Columns)
            rows.Add(new MonitorRow(Aircraft.A350.Display.A350FmaAnnouncer.KeyFor(column), Aircraft.A350.Display.A350FmaAnnouncer.LabelFor(column)));
        rows.Add(new MonitorRow(Aircraft.A350.Display.A350PfdFlagAnnouncer.MuteKey, Aircraft.A350.Display.A350PfdFlagAnnouncer.MuteLabel));
        rows.Add(new MonitorRow(Aircraft.A350.Display.A350FmsMessageAnnouncer.MuteKey, Aircraft.A350.Display.A350FmsMessageAnnouncer.MuteLabel));
        foreach (var (key, name) in Aircraft.A350.A350TakeoffCallouts.Speeds)
            rows.Add(new MonitorRow(key, name));
        foreach (var (key, label) in Aircraft.A350.A350MinimumsCallout.Rows)
            rows.Add(new MonitorRow(key, label));
        foreach (var (key, label) in Aircraft.A350.A350BtvCountdown.Rows)
            rows.Add(new MonitorRow(key, label));
        rows.Add(new MonitorRow(Aircraft.A350.A350TcasCallouts.MuteKey, Aircraft.A350.A350TcasCallouts.MuteLabel));
        rows.Add(new MonitorRow(Aircraft.A350.A350PrinterWatch.MuteKey, Aircraft.A350.A350PrinterWatch.MuteLabel));
        rows.Add(new MonitorRow(Aircraft.A350.Display.A350MemoryWarning.MuteKey, Aircraft.A350.Display.A350MemoryWarning.MuteLabel));
        return rows;
    }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A350DisabledMonitorVariables;
}
