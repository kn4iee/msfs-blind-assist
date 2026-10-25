using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist;

/// <summary>
/// BlueBird 757-200 windows and menu entry. Kept in its own partial so the aircraft's surfaces sit
/// together: the Ctrl+M monitor manager, the Shift+M CDU window and the Shift+T EFB window;
/// <see cref="DisposeBBS757Forms"/> is where each is torn down.
/// </summary>
public partial class MainForm
{
    private Forms.BBS757.BBS757MonitorManagerForm? bbs757MonitorManagerForm;
    private Forms.BBS757.BBS757CduForm? bbs757CduForm;
    private Forms.BBS757.BBS757EfbForm? bbs757EfbForm;

    /// <summary>
    /// The one client on the 757's "BBSTablet" Coherent view ([DBG-9]: one socket per view): the
    /// CDU window reads and presses through it, and the EFB window reads the Navigraph code
    /// through it. Created on first use, disposed in <see cref="DisposeBBS757Forms"/>; the windows
    /// only subscribe to it.
    /// </summary>
    private CoherentBbs757TabletClient? bbs757TabletClient;

    /// <summary>The shared tablet-view client, created and started on first use.</summary>
    internal CoherentBbs757TabletClient EnsureBbs757TabletClient()
    {
        if (bbs757TabletClient == null)
        {
            bbs757TabletClient = new CoherentBbs757TabletClient();
            bbs757TabletClient.Start();
        }
        return bbs757TabletClient;
    }

    /// <summary>
    /// The one client on the 757's "BBSNavigraphMap" Coherent view, where BlueBird's EFB tablet is
    /// drawn ([DBG-9]): the EFB window clicks the tablet through it. Created on first use, disposed
    /// in <see cref="DisposeBBS757Forms"/>; the window never disposes it.
    /// </summary>
    private CoherentBbs757EfbClient? bbs757EfbClient;

    /// <summary>The EFB-view client, created and started on first use.</summary>
    internal CoherentBbs757EfbClient EnsureBbs757EfbClient()
    {
        if (bbs757EfbClient == null)
        {
            bbs757EfbClient = new CoherentBbs757EfbClient();
            bbs757EfbClient.Start();
        }
        return bbs757EfbClient;
    }

    /// <summary>
    /// Shift+M on the 757: both CDUs as text. Needs no SimConnect connection: the screens and keys
    /// go over the Coherent debugger.
    /// </summary>
    private void ShowBBS757CduDialog()
    {
        if (currentAircraft is not BBS757Definition) return;
        hotkeyManager.ExitInputHotkeyMode();

        var client = EnsureBbs757TabletClient();
        if (bbs757CduForm == null || bbs757CduForm.IsDisposed)
            bbs757CduForm = new Forms.BBS757.BBS757CduForm(client, announcer);
        bbs757CduForm.ShowForm();
    }

    /// <summary>
    /// Shift+T on the 757: the EFB window, which operates BlueBird's tablet by scripted clicks (the
    /// EFB client) and reads the Navigraph sign-in off the hidden helper view (the shared tablet
    /// client). Both go over the Coherent debugger; only a cockpit preset needs SimConnect, for its
    /// ground or flight check (<see cref="ReadBbs757OnGroundAsync"/>).
    /// </summary>
    private void ShowBBS757EfbDialog()
    {
        if (currentAircraft is not BBS757Definition) return;
        hotkeyManager.ExitInputHotkeyMode();

        var efb = EnsureBbs757EfbClient();
        var tablet = EnsureBbs757TabletClient();
        if (bbs757EfbForm == null || bbs757EfbForm.IsDisposed)
            bbs757EfbForm = new Forms.BBS757.BBS757EfbForm(efb, tablet, announcer, ReadBbs757OnGroundAsync);
        bbs757EfbForm.ShowForm();
    }

    /// <summary>
    /// SIM ON GROUND read fresh for the EFB window's preset gate: true on the ground, false airborne,
    /// null when it cannot be read (no connection, or no delivery within the timeout).
    /// </summary>
    private async Task<bool?> ReadBbs757OnGroundAsync(int timeoutMs, CancellationToken ct)
    {
        var sim = simConnectManager;
        if (sim == null || !sim.IsConnected) return null;
        double? value = await sim.ReadFreshAsync("SIM_ON_GROUND", timeoutMs, ct);
        return value is double v && double.IsFinite(v) ? v > 0.5 : null;
    }

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
    /// Closes every 757 window, then the clients they use (windows first, so none is left
    /// subscribed to or running on a disposed client). Called by SwitchAircraft on every switch,
    /// and on app exit: the monitor manager's rows are snapshotted from the definition it was built
    /// for, so a surviving one would list the outgoing definition, and each client holds the one
    /// inspector socket Coherent allows on its view.
    /// </summary>
    private void DisposeBBS757Forms()
    {
        if (bbs757MonitorManagerForm != null && !bbs757MonitorManagerForm.IsDisposed) bbs757MonitorManagerForm.Dispose();
        bbs757MonitorManagerForm = null;
        if (bbs757CduForm != null && !bbs757CduForm.IsDisposed) bbs757CduForm.Dispose();
        bbs757CduForm = null;
        if (bbs757EfbForm != null && !bbs757EfbForm.IsDisposed) bbs757EfbForm.Dispose();
        bbs757EfbForm = null;
        bbs757TabletClient?.Dispose();
        bbs757TabletClient = null;
        bbs757EfbClient?.Dispose();
        bbs757EfbClient = null;
    }

    private void BBS757MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new BBS757Definition());
    }
}
