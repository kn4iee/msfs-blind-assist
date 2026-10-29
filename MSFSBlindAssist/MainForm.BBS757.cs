using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

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

    /// <summary>The reworked 757 EFB window (plan 11): the shared accessible EFB window over the tablet reader.</summary>
    private Forms.FBWA380.FbwEfbForm? bbs757EfbReaderForm;
    private Bbs757EfbBridge? bbs757EfbBridge;

    /// <summary>Shift+T on the 757: the classic EFB window, whose every press is checked against a
    /// variable or BlueBird's settings file. The tablet screen reader is a button in it.</summary>
    private void ShowBBS757EfbDialog() => ShowBBS757ClassicEfbDialog();

    /// <summary>
    /// The 757 tablet screen reader (plan 11; read-only since the team review of 2026-10-27): BlueBird's
    /// tablet in the shared accessible EFB window, read from its pixels (<see cref="Bbs757EfbBridge"/>).
    /// It only reads, and moves between the tablet's pages; anything that changes the aircraft is done
    /// from the classic window. The first time it shows, the tablet reader's pop-out is made as the A300
    /// and A330 CDU windows make theirs: the camera goes to the captain's instrument view, the simulator
    /// comes to the front for a moment, and the camera is put back. While the window is shown the tablet
    /// is mirrored onto the captain's ND and read; hidden, nothing is read and the ND is the ND. The reader
    /// reads with the downloaded text recognition: while that is not Ready it does not open, and the offer
    /// window is shown instead, whatever the offer checkbox says (the pilot asked for the reader).
    /// </summary>
    private void ShowBBS757EfbReader()
    {
        if (currentAircraft is not BBS757Definition) return;
        if (textRecognitionDownloader.Installed().State != TextRecognitionState.Ready)
        {
            ShowTextRecognitionOffer();
            return;
        }
        hotkeyManager.ExitInputHotkeyMode();

        var efb = EnsureBbs757EfbClient();
        if (bbs757EfbBridge == null)
        {
            bbs757EfbBridge = new Bbs757EfbBridge(efb, CreateBbs757EfbBridgeIo());
            bbs757EfbBridge.Start();
        }
        if (bbs757EfbReaderForm == null || bbs757EfbReaderForm.IsDisposed)
        {
            var form = new Forms.FBWA380.FbwEfbForm(bbs757EfbBridge, announcer, "BlueBird 757 tablet screen", "tablet", "BlueBird tablet");
            var bridge = bbs757EfbBridge;
            form.VisibleChanged += (_, _) => bridge.SetActive(form.Visible && !form.IsDisposed);
            bbs757EfbReaderForm = form;
        }
        bbs757EfbReaderForm.ShowForm();
        bbs757EfbBridge.SetActive(true);
    }

    /// <summary>
    /// The bridge's seams. Camera reads and writes go to the UI thread (SimConnect calls stay there); the
    /// type and the index are written apart, since leaving an instrument view needs the two spaced
    /// (<c>SimConnectManager.SetCameraView</c>, measured). Variables are read fresh (safe from any thread).
    /// The text recognition engine loads from the downloaded models with <see cref="PaddleOcrEngine.ThreadsFor"/>'s
    /// threads, on the bridge's loop; a load that fails is logged and the models' installed marker removed, so the
    /// next check offers the download again. That report runs on the loop too, so it never throws.
    /// </summary>
    private Bbs757EfbBridgeIo CreateBbs757EfbBridgeIo() => new()
    {
        ReadCamera = async () =>
        {
            var sim = simConnectManager;
            if (sim == null || !sim.IsConnected) return null;
            var task = (Task<MSFSBlindAssist.Services.CameraViewReading?>)Invoke(new Func<Task<MSFSBlindAssist.Services.CameraViewReading?>>(() => sim.ReadCameraViewAsync(1500)));
            var r = await task;
            return r is { } v ? (v.ViewType, v.ViewIndex) : null;
        },
        SetCamera = (type, index) =>
        {
            var sim = simConnectManager;
            if (sim == null || !sim.IsConnected) return;
            Invoke(() => sim.SetCameraViewType(type));
            Thread.Sleep(150);
            Invoke(() => sim.SetCameraViewIndex(index));
        },
        OnGround = ct => ReadBbs757OnGroundAsync(1500, ct),
        ReadRegistered = (key, ct) => simConnectManager is { IsConnected: true } sim
            ? sim.ReadFreshAsync(key, 1500, ct)
            : Task.FromResult<double?>(null),
        OpenClassicWindow = ShowBBS757ClassicEfbDialog,
        ArmServiceEcho = s => { if (currentAircraft is BBS757Definition def) def.ArmServiceEcho(s); },
        SettleServiceEcho = (s, v) => { if (currentAircraft is BBS757Definition def) def.SettleServiceEcho(s, v); },
        ArmGroundPowerEcho = () => { if (currentAircraft is BBS757Definition def) def.ArmGroundPowerEcho(); },
        SettleGroundPowerEcho = v => { if (currentAircraft is BBS757Definition def) def.SettleGroundPowerEcho(v); },
        LoadOcr = () => PaddleOcrEngine.Load(TextRecognitionModels.DefaultFolder(), PaddleOcrEngine.ThreadsFor(Environment.ProcessorCount)),
        OcrFailed = ex =>
        {
            Log.Warn("BBS757", $"Text recognition could not start: {ex.Message}");
            try
            {
                TextRecognitionModels.RemoveMarker(TextRecognitionModels.DefaultFolder());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn("BBS757", $"Could not remove the text recognition marker: {e.Message}");
            }
        },
    };

    /// <summary>
    /// The classic 757 EFB window, which operates BlueBird's tablet by scripted clicks at measured points
    /// (the EFB client) and reads the Navigraph sign-in off the hidden helper view (the shared tablet
    /// client). Both go over the Coherent debugger; a cockpit preset needs SimConnect for its ground
    /// or flight check (<see cref="ReadBbs757OnGroundAsync"/>), and the SimBrief fuel and payload
    /// load for that check and the definition's load watch (<see cref="CreateBbs757LoadIo"/>).
    /// </summary>
    private void ShowBBS757ClassicEfbDialog()
    {
        if (currentAircraft is not BBS757Definition) return;
        hotkeyManager.ExitInputHotkeyMode();

        var tablet = EnsureBbs757TabletClient();
        if (tablet == null) return;
        var efb = EnsureBbs757EfbClient();
        if (bbs757EfbForm == null || bbs757EfbForm.IsDisposed)
            bbs757EfbForm = new Forms.BBS757.BBS757EfbForm(efb, tablet, announcer, ReadBbs757OnGroundAsync, CreateBbs757LoadIo())
            {
                OpenScreenReader = ShowBBS757EfbReader,
            };
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
        if (bbs757EfbReaderForm != null && !bbs757EfbReaderForm.IsDisposed) bbs757EfbReaderForm.Dispose();
        bbs757EfbReaderForm = null;
        // The bridge before the client it reads through (it turns the mirror off on its way out).
        bbs757EfbBridge?.Dispose();
        bbs757EfbBridge = null;
        bbs757EfbClient?.Dispose();
        bbs757EfbClient = null;
    }

    /// <summary>
    /// The text recognition offer, made like the startup data check ([UPD-9]): at startup on a restored 757
    /// profile (<c>MainForm_Shown</c>, after the PMDG SDK check and before the update check) and after a pick of
    /// the 757 in the Aircraft menu, never at connect; a dismissal is never remembered and only the pilot's
    /// checkbox silences it (<see cref="TextRecognitionOffer.ShouldOffer"/>). The window waits for the pilot;
    /// the download it starts keeps running when it closes.
    /// </summary>
    private void OfferTextRecognition()
    {
        if (currentAircraft is not BBS757Definition) return;
        bool settingOn = MSFSBlindAssist.Settings.SettingsManager.Current.OfferTextRecognitionFor757;
        if (!TextRecognitionOffer.ShouldOffer(true, settingOn, textRecognitionDownloader.Installed().State, textRecognitionDownloader.IsRunning))
            return;
        ShowTextRecognitionOffer();
    }

    /// <summary>Shows the offer window, modal over the main window, and never throws: the pilot is not left
    /// without the app because a window could not open.</summary>
    private void ShowTextRecognitionOffer()
    {
        try
        {
            using var dialog = new Forms.TextRecognitionOfferDialog(textRecognitionDownloader);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"The text recognition offer failed: {ex.Message}");
        }
    }

    private void BBS757MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new BBS757Definition());
        OfferTextRecognition();
    }
}
