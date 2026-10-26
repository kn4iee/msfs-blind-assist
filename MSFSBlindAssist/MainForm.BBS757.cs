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
    /// The one client on the 757's "BBSTablet" Coherent view ([DBG-9]: one socket per view), which the
    /// 757 definition owns (<see cref="BBS757Definition.EnsureTabletClient"/>): the CDU window reads and
    /// presses through it, and the EFB window reads the Navigraph code through it; the windows only
    /// subscribe to it. Null when the 757 is not the loaded aircraft. The definition disposes the
    /// client when the pilot switches away, after closing these windows
    /// (<see cref="CloseBbs757TabletWindows"/>, handed to it here).
    /// </summary>
    internal CoherentBbs757TabletClient? EnsureBbs757TabletClient()
    {
        if (currentAircraft is not BBS757Definition bbs757) return null;
        bbs757.CloseTabletWindows = CloseBbs757TabletWindows;
        return bbs757.EnsureTabletClient();
    }

    /// <summary>The windows on the tablet client: the CDU window and the EFB window.</summary>
    private void CloseBbs757TabletWindows()
    {
        if (bbs757CduForm != null && !bbs757CduForm.IsDisposed) bbs757CduForm.Dispose();
        bbs757CduForm = null;
        if (bbs757EfbForm != null && !bbs757EfbForm.IsDisposed) bbs757EfbForm.Dispose();
        bbs757EfbForm = null;
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
        if (client == null) return;
        if (bbs757CduForm == null || bbs757CduForm.IsDisposed)
            bbs757CduForm = new Forms.BBS757.BBS757CduForm(client, announcer);
        bbs757CduForm.ShowForm();
    }

    /// <summary>
    /// Shift+T on the 757: the EFB window, which operates BlueBird's tablet by scripted clicks (the
    /// EFB client) and reads the Navigraph sign-in off the hidden helper view (the shared tablet
    /// client). Both go over the Coherent debugger; a cockpit preset needs SimConnect for its ground
    /// or flight check (<see cref="ReadBbs757OnGroundAsync"/>), and the SimBrief fuel and payload
    /// load for that check and the definition's load watch (<see cref="CreateBbs757LoadIo"/>).
    /// </summary>
    private void ShowBBS757EfbDialog()
    {
        if (currentAircraft is not BBS757Definition) return;
        hotkeyManager.ExitInputHotkeyMode();

        var tablet = EnsureBbs757TabletClient();
        if (tablet == null) return;
        var efb = EnsureBbs757EfbClient();
        if (bbs757EfbForm == null || bbs757EfbForm.IsDisposed)
            bbs757EfbForm = new Forms.BBS757.BBS757EfbForm(efb, tablet, announcer, ReadBbs757OnGroundAsync, CreateBbs757LoadIo());
        bbs757EfbForm.ShowForm();
    }

    /// <summary>
    /// The EFB window's seams: fresh reads of the variables the definition registers (the chocks, the
    /// ground power), the plan fetch for BlueBird's SimBrief ID, and the current 757 definition's load
    /// watch and ground power echo (the window's own ground power run, so the Ground Power row does not
    /// say its change a second time). Each reads <c>simConnectManager</c> and <c>currentAircraft</c> when
    /// it is called, so a reconnect is followed; the window is disposed on every profile switch.
    /// </summary>
    private Forms.BBS757.Bbs757LoadIo CreateBbs757LoadIo() => new()
    {
        Read = (key, timeoutMs, ct) => simConnectManager is { IsConnected: true } sim
            ? sim.ReadFreshAsync(key, timeoutMs, ct)
            : Task.FromResult<double?>(null),
        FetchOfpXml = (id, ct) => MSFSBlindAssist.Services.SimBriefService.FetchLatestOfpXmlAsync(id, null, ct),
        WatchRunning = () => currentAircraft is BBS757Definition { LoadWatchRunning: true },
        NestRunning = () => currentAircraft is BBS757Definition def ? def.NestRunning() : Aircraft.BBS757.Bbs757Nest.IsRunning(),
        StartWatch = (loads, units) => currentAircraft is BBS757Definition def && simConnectManager is { IsConnected: true } sim
            && def.StartLoadWatch(loads, units, sim, announcer),
        ArmGroundPowerEcho = () =>
        {
            if (currentAircraft is BBS757Definition def) def.ArmGroundPowerEcho();
        },
        SettleGroundPowerEcho = after =>
        {
            if (currentAircraft is BBS757Definition def) def.SettleGroundPowerEcho(after);
        },
    };

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
            bbs757MonitorManagerForm = new Forms.BBS757.BBS757MonitorManagerForm(bbs757.MonitorRows());
        bbs757MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// Closes every 757 window, then the EFB-view client (windows first, so none is left subscribed
    /// to or running on a disposed client). Called by SwitchAircraft on every switch, and on app
    /// exit: the monitor manager's rows are snapshotted from the definition it was built for, so a
    /// surviving one would list the outgoing definition, and the client holds the one inspector
    /// socket Coherent allows on its view. The tablet client is the definition's, disposed by its
    /// OnSwitchedAway (which closes the CDU and EFB windows first).
    /// </summary>
    private void DisposeBBS757Forms()
    {
        if (bbs757MonitorManagerForm != null && !bbs757MonitorManagerForm.IsDisposed) bbs757MonitorManagerForm.Dispose();
        bbs757MonitorManagerForm = null;
        CloseBbs757TabletWindows();
        bbs757EfbClient?.Dispose();
        bbs757EfbClient = null;
    }

    private void BBS757MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new BBS757Definition());
    }
}
