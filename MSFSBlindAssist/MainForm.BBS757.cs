using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>
/// BlueBird 757-200 windows and menu entry. Kept in its own partial so the aircraft's surfaces sit
/// together: today the Ctrl+M monitor manager; the CDU and EFB windows join it with plan 01's
/// later tasks, and <see cref="DisposeBBS757Forms"/> is where each is torn down.
/// </summary>
public partial class MainForm
{
    private Forms.BBS757.BBS757MonitorManagerForm? bbs757MonitorManagerForm;

    /// <summary>
    /// The 757's monitor manager (Ctrl+M) — mute individual auto-announced variables. Rebuilt from
    /// the live definition each time it is created. Public because the definition calls it, after
    /// leaving output hotkey mode itself; the aircraft guard keeps a queued call that lands after a
    /// profile switch from building a "757 monitor manager" over another aircraft's variables.
    /// </summary>
    public void ShowBBS757MonitorManagerDialog()
    {
        if (currentAircraft is not BBS757Definition bbs757) return;
        if (bbs757MonitorManagerForm == null || bbs757MonitorManagerForm.IsDisposed)
            bbs757MonitorManagerForm = new Forms.BBS757.BBS757MonitorManagerForm(bbs757.GetVariables());
        bbs757MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// Closes every 757 window. Called by SwitchAircraft on every switch: the monitor manager's rows
    /// are snapshotted from the definition it was built for, so a surviving one would list the
    /// outgoing definition.
    /// </summary>
    private void DisposeBBS757Forms()
    {
        if (bbs757MonitorManagerForm != null && !bbs757MonitorManagerForm.IsDisposed) bbs757MonitorManagerForm.Dispose();
        bbs757MonitorManagerForm = null;
    }

    private void BBS757MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new BBS757Definition());
    }
}
