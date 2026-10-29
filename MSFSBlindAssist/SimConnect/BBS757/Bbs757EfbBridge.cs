using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What the 757 EFB bridge needs from the app, as delegates (MainForm builds it).</summary>
public sealed class Bbs757EfbBridgeIo
{
    /// <summary>The camera (type, index), read fresh; null when it cannot be read.</summary>
    public required Func<Task<(int type, int index)?>> ReadCamera { get; init; }

    /// <summary>Moves the camera (type, index), on the UI thread (MainForm marshals).</summary>
    public required Action<int, int> SetCamera { get; init; }

    /// <summary>SIM ON GROUND read fresh: true, false, or null when unread (the preset gate).</summary>
    public required Func<CancellationToken, Task<bool?>> OnGround { get; init; }

    /// <summary>A variable the definition registers, read fresh (external power for REMOVE ALL).</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadRegistered { get; init; }

    /// <summary>Opens the classic 757 EFB window (SimBrief load, Navigraph sign-in, nav data update).</summary>
    public required Action OpenClassicWindow { get; init; }

    /// <summary>Opens / ends the definition's echo for a service row around the window's own tap (Task 5).</summary>
    public required Action<Bbs757GroundService> ArmServiceEcho { get; init; }
    public required Action<Bbs757GroundService, double?> SettleServiceEcho { get; init; }

    /// <summary>Opens / ends plan 07's ground power echo around a GPU tap.</summary>
    public required Action ArmGroundPowerEcho { get; init; }
    public required Action<double?> SettleGroundPowerEcho { get; init; }

    /// <summary>Loads the text recognition engine (PaddleOCR) from the downloaded models; throws when it cannot.
    /// Called on the bridge's loop, off the UI thread, at the first read while the window is shown.</summary>
    public required Func<PaddleOcrEngine> LoadOcr { get; init; }

    /// <summary>Told once why the engine could not load (logged, and the models' installed marker removed so the
    /// download is offered again). Called on the bridge's loop; must never throw.</summary>
    public required Action<Exception> OcrFailed { get; init; }
}

/// <summary>
/// The 757 EFB for the shared accessible EFB window (FbwEfbForm, R73): BlueBird's tablet, READ from its
/// pixels (plan 11, ruling of 2026-10-26: Bbs757EfbPopout, Bbs757EfbOcr, Bbs757EfbScreen) and
/// operated through the EFB agent's clicks, as an IMcduBridge.
///
/// While the window is shown, the tablet is mirrored onto the captain's ND (the agent's mirror) and the
/// ND pop-out is captured about every 0.7 s; a page is read (PaddleOCR, about half a second) only when
/// the picture changed, after an action, or on request, so a still tablet costs a capture and a 32 x 24
/// compare. Each read becomes the window's elements (<see cref="Bbs757EfbElements"/>); each press taps
/// the tablet and reads it again. Hidden, nothing is captured and the mirror is off (the ND is the ND).
///
/// The text recognition engine is loaded at the first read while the window is shown and released when
/// it hides (<see cref="TextRecognitionEngineSlot{T}"/>), both only on the loop, which runs every read,
/// so it is never released under a running read. An engine that cannot load is said once, as the
/// reader's status, and said again when a later status took its place (the window never blames
/// something else). It is tried again after the window was hidden and shown, or when the pilot asks for
/// the reader again with the models Ready (<see cref="RetryTextRecognition"/>: a flag the loop takes
/// before its next read, so the engine stays the loop's alone).
///
/// The plan's rules hold: a ground service or the chocks are judged from their BlueBird variable before
/// and after a tap and tapped only when they differ (B757-2, <see cref="Bbs757GroundServices"/>); the
/// GPU goes through plan 07's flow; REMOVE ALL is refused with external power on line; presets keep the
/// ground / flight gate; custom SAVE/LOAD is never armed; the UPDATE/REMOVE spot is never tapped (the
/// AIRAC page's update is the classic window's guarded flow). Speech follows CORE-7: the screen reader
/// reads the control; a control whose label carries its own state is re-read for the pilot after the
/// press (announceChange); a refusal or a failure is said as an alert.
/// </summary>
public sealed class Bbs757EfbBridge : IMcduBridge, IDisposable
{
    private const string StateTypeElements = "fbw_efb_elements";
    private const string StateTypeConnected = "fbw_efb_connected";
    private const int TickMs = 700;
    private const int MirrorRefreshMs = 10_000;
    private const int StalePageMs = 20_000;
    private const int AfterTapMs = 700;
    private const int SettleGapMs = 250;
    private const int SettleMaxMs = 2500;
    private const int ChangeMaxMs = 3000;
    private const int NavigateMaxMs = 4000;

    private readonly CoherentBbs757EfbClient _client;
    private readonly Bbs757EfbPopout _popout;
    private readonly Bbs757EfbBridgeIo _io;
    private readonly SynchronizationContext? _sync;
    private readonly System.Threading.Channels.Channel<(string Command, Dictionary<string, string>? Payload)> _commands =
        System.Threading.Channels.Channel.CreateUnbounded<(string, Dictionary<string, string>?)>();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    /// <summary>The text recognition engine. Touched only on the loop: Get before a read, ForgetFailure when it
    /// takes a retry, Release when the window hides, Dispose when the loop ends.</summary>
    private readonly TextRecognitionEngineSlot<PaddleOcrEngine> _ocr;

    /// <summary>Set by <see cref="RetryTextRecognition"/> on the UI thread, taken by the loop
    /// (<see cref="TakeTextRecognitionRetry"/>).</summary>
    private volatile bool _retryOcr;

    private volatile bool _active;
    private bool _popoutReady;
    private string? _popoutError;
    private bool _mirrorOn;
    private long _mirrorAtMs;
    private byte[]? _lastSignature;
    private Bitmap? _lastReadFrame;
    private string? _lastFingerprint;
    private long _lastReadMs;
    private bool _forceRead = true;
    private Bbs757EfbScreenModel? _model;
    private Bbs757EfbElements.Built? _built;
    private string? _alert;
    private long _alertUntilMs;
    private Task? _loop;

    public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;

    public Bbs757EfbBridge(CoherentBbs757EfbClient client, Bbs757EfbBridgeIo io, Bbs757EfbPopout? popout = null)
    {
        _client = client;
        _io = io;
        _popout = popout ?? new Bbs757EfbPopout(s => Log.Debug("BBS757", "EFB reader: " + s));
        _sync = SynchronizationContext.Current;
        // A status, not an alert: an alert is gone after 8 s, and the failure holds until the window is hidden and
        // shown again or the pilot asks for the reader again (the loop's inactive branch, or the retry it takes,
        // forgets it).
        _ocr = new TextRecognitionEngineSlot<PaddleOcrEngine>(io.LoadOcr, ex =>
        {
            io.OcrFailed(ex);
            Status(TextRecognitionText.ReaderCannotStart);
        });
    }

    public bool IsBridgeConnected => _client.IsReady && _popoutReady;

    /// <summary>
    /// True (as shipped since the team review of 2026-10-27): the window reads the tablet and moves
    /// between its pages, and refuses everything else. A misread must never become a press; the classic
    /// window does the pressing, checked against a variable or BlueBird's settings file.
    /// </summary>
    public bool ReadOnly { get; init; } = true;

    /// <summary>Starts the bridge's loop (once). It reads only while <see cref="SetActive"/> says the
    /// window is shown.</summary>
    public void Start()
    {
        _loop ??= Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <summary>The window was shown (true) or hidden (false): reading and the mirror follow it.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active) _forceRead = true;
        _wake.Release();
    }

    /// <summary>
    /// The pilot asked for the reader with the models Ready (MainForm calls it only after its Ready check, so the
    /// models never load from a folder without their marker, OCR-1): an engine that could not load is tried again at
    /// the next read, now that the pilot downloaded the models again as the reader's status asked. Only a flag: the
    /// loop takes it before its next read (<see cref="TakeTextRecognitionRetry"/>), since only the loop touches the
    /// engine (OCR-3).
    /// </summary>
    public void RetryTextRecognition() => _retryOcr = true;

    /// <summary>
    /// The loop's side of <see cref="RetryTextRecognition"/>, at the top of each turn while the window is shown,
    /// before any read: a load that failed is forgotten (a loaded engine is kept and nothing is released), so the
    /// next read loads again, and the reader's status of the failure is taken back, so a load that works does not
    /// keep showing it. On the loop only; internal for the tests, which call it on a bridge whose loop never started.
    /// </summary>
    internal void TakeTextRecognitionRetry()
    {
        if (!_retryOcr) return;
        _retryOcr = false;
        _ocr.ForgetFailure();
        if (_alert == TextRecognitionText.ReaderCannotStart) _alert = null;
    }

    public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
    {
        _commands.Writer.TryWrite((command, payload));
        _wake.Release();
    }

    // ------------------------------------------------------------------ the loop

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await RunLoopAsync(ct);
        }
        finally
        {
            // The loop has ended, so no read runs: the engine goes with it (Dispose may have stopped waiting for it).
            _ocr.Dispose();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_active)
                {
                    // Hidden: the engine is freed (no read runs between two turns of the loop), and a load that
                    // failed is forgotten with the reader's word of it, so the next showing tries again.
                    _ocr.Release();
                    if (_alert == TextRecognitionText.ReaderCannotStart) _alert = null;
                    if (_mirrorOn) { await _client.SetMirrorAsync(false, ct); _mirrorOn = false; }
                    await _wake.WaitAsync(ct);
                    continue;
                }
                // Shown: a retry the pilot asked for is taken before anything is read.
                TakeTextRecognitionRetry();
                if (!_popoutReady)
                {
                    if (_popout.Find() != IntPtr.Zero) { _popout.Park(_popout.Find()); _popoutReady = true; }
                    else
                    {
                        Status("Opening the tablet reader. The simulator comes to the front for a moment.");
                        _popoutError = await _popout.EnsureAsync(_io.ReadCamera, _io.SetCamera, ct);
                        _popoutReady = _popoutError == null;
                        if (!_popoutReady)
                        {
                            Status("The tablet reader could not open: " + _popoutError + " Press F5 to try again.");
                            await WaitForCommandOrWakeAsync(ct, untilCommand: true);
                            continue;
                        }
                        Raise(StateTypeConnected, new Dictionary<string, string>());
                    }
                }
                long now = Environment.TickCount64;
                if (!_mirrorOn || now - _mirrorAtMs > MirrorRefreshMs)
                {
                    var m = await _client.SetMirrorAsync(true, ct);
                    if (m != true)
                    {
                        Status(m == null ? "The BlueBird tablet cannot be reached. Is the 757 loaded?" : "The BlueBird tablet could not be mirrored.");
                        _mirrorOn = false;
                        await WaitForCommandOrWakeAsync(ct, untilCommand: false, ms: 2000);
                        continue;
                    }
                    if (!_mirrorOn) { _mirrorOn = true; await Task.Delay(650, ct); }   // measured: visible ~0.5 s after
                    _mirrorAtMs = now;
                }

                if (_commands.Reader.TryRead(out var cmd))
                {
                    await HandleAsync(cmd.Command, cmd.Payload, ct);
                    continue;
                }

                await ReadIfChangedAsync(ct);
                await WaitForCommandOrWakeAsync(ct, untilCommand: false, ms: TickMs);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Warn("BBS757", "EFB reader loop: " + ex.Message);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
        try { if (_mirrorOn) await _client.SetMirrorAsync(false, CancellationToken.None); } catch { }
    }

    private async Task WaitForCommandOrWakeAsync(CancellationToken ct, bool untilCommand, int ms = 10_000)
    {
        if (untilCommand) { await _wake.WaitAsync(ct); return; }
        await _wake.WaitAsync(ms, ct);
    }

    /// <summary>
    /// Captures the tablet and reads it when the picture changed (status bar excluded: its clock), a read
    /// was asked for, or the last read is old AND the pixels moved since. What the pilot hears must not
    /// change unless the tablet did (plan 11 feedback: "the presentation changed upon refreshes"):
    /// <list type="bullet">
    /// <item>a picture that has not really changed (rendering noise only, <see cref="Bbs757EfbFrameCompare"/>) is
    /// never read again on its own: OCR noise was all a re-read could add;</item>
    /// <item>a read waits for the picture to hold still (a page still animating in read as garbage);</item>
    /// <item>a picture that never holds still is read from two captures, and the page is taken only when
    /// the two reads agree; else a third read decides, and when it agrees with neither the page is shown
    /// with a warning that it may be wrong;</item>
    /// <item>a read that says the same as the page already shown is not pushed again.</item>
    /// </list>
    /// On the loop only, as it gets the engine (OCR-3); internal for the tests, which call it on a bridge whose
    /// loop never started.
    /// </summary>
    internal async Task<bool> ReadIfChangedAsync(CancellationToken ct, bool force = false)
    {
        // No engine (it could not load, and the reader says so): nothing is captured or read.
        var engine = EngineForRead();
        if (engine == null) return false;
        var bmp = _popout.CaptureTablet();
        if (bmp == null) { _popoutReady = false; Status("The tablet reader's window closed. Opening it again."); return false; }
        try
        {
            var sig = Signature(bmp);
            bool changed = _lastSignature == null || Differs(sig, _lastSignature);
            long now = Environment.TickCount64;
            bool stale = now - _lastReadMs >= StalePageMs;
            if (!force && !_forceRead && !changed && !stale) return false;
            // Nothing really moved since the last read (rendering noise only): reading again could only add OCR noise.
            if (!force && !_forceRead && _lastReadFrame != null && Bbs757EfbFrameCompare.Same(bmp, _lastReadFrame))
            {
                _lastSignature = sig;
                _lastReadMs = now;
                return false;
            }
            _forceRead = false;

            // Wait for two captures in a row that show the same thing (Bbs757EfbFrameCompare).
            bool still = false;
            long settleUntil = Environment.TickCount64 + SettleMaxMs;
            while (Environment.TickCount64 < settleUntil)
            {
                await Task.Delay(SettleGapMs, ct);
                var next = _popout.CaptureTablet();
                if (next == null) break;
                bool same = Bbs757EfbFrameCompare.Same(bmp, next);
                bmp.Dispose();
                bmp = next;
                if (same) { still = true; break; }
            }

            var read = await Bbs757EfbPageReader.ReadAsync(engine, bmp, ct);
            int reads = 1;
            bool unclear = false;
            if (!still)
            {
                // Never held still (something on the page animates): agree twice before the page is taken.
                string fp1 = Bbs757EfbPress.Fingerprint(read.Model);
                var read2 = await ReadFreshCaptureAsync(ct);
                if (read2 != null)
                {
                    reads++;
                    string fp2 = Bbs757EfbPress.Fingerprint(read2.Model);
                    if (fp2 != fp1)
                    {
                        var read3 = await ReadFreshCaptureAsync(ct);
                        if (read3 != null)
                        {
                            reads++;
                            string fp3 = Bbs757EfbPress.Fingerprint(read3.Model);
                            if (fp3 == fp2) read = read2;
                            else if (fp3 != fp1) { read = read3; unclear = true; }
                        }
                        else unclear = true;
                    }
                }
            }

            _lastSignature = Signature(bmp);
            _lastReadFrame?.Dispose();
            _lastReadFrame = new Bitmap(bmp);
            _lastReadMs = Environment.TickCount64;
            string fp = Bbs757EfbPress.Fingerprint(read.Model);
            Log.Debug("BBS757", $"EFB reader: {read.Model.PageName}, {read.Model.Items.Count} items, {read.OcrMs} ms, "
                + $"still={still}, reads={reads}, unclear={unclear}, same-as-shown={fp == _lastFingerprint}");
            if (unclear)
                Alert("The tablet read differently each time, so this page may not be right. Use Read the tablet again.");
            if (fp == _lastFingerprint && !unclear) return true;
            _lastFingerprint = fp;
            _model = read.Model;
            Push();
            return true;
        }
        finally { bmp.Dispose(); }
    }

    private async Task<Bbs757EfbPageRead?> ReadFreshCaptureAsync(CancellationToken ct)
    {
        var engine = EngineForRead();
        if (engine == null) return null;
        using var b = _popout.CaptureTablet();
        return b == null ? null : await Bbs757EfbPageReader.ReadAsync(engine, b, ct);
    }

    /// <summary>
    /// The engine for a read, or null when it could not load. A load that fails is reported once and said as the
    /// reader's status (the slot's report). While the failure is remembered, a later status that took the window
    /// (the tablet could not be reached, say) gives way to it again, with no second report, so the window never
    /// blames something else.
    /// </summary>
    private PaddleOcrEngine? EngineForRead()
    {
        var engine = _ocr.Get();
        if (engine == null && _alert != TextRecognitionText.ReaderCannotStart) Status(TextRecognitionText.ReaderCannotStart);
        return engine;
    }

    /// <summary>A 32 x 24 grey thumbnail of the tablet below its status bar.</summary>
    internal static byte[] Signature(Bitmap bmp)
    {
        // Sampled, not scaled: a scaled thumbnail blends the status bar (its clock) into the cells below it.
        var px = Bbs757EfbPixels.FromBitmap(bmp);
        int top = Bbs757EfbScreen.StatusBottom + 4, h = bmp.Height - top;
        var o = new byte[32 * 24];
        for (int cy = 0; cy < 24; cy++)
            for (int cx = 0; cx < 32; cx++)
            {
                int sum = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        int x = (int)((cx + (sx + 0.5) / 3.0) * bmp.Width / 32), y = top + (int)((cy + (sy + 0.5) / 3.0) * h / 24);
                        sum += (int)px.Lum(Math.Min(x, bmp.Width - 1), Math.Min(y, bmp.Height - 1));
                    }
                o[cy * 32 + cx] = (byte)(sum / 9);
            }
        return o;
    }

    /// <summary>Two thumbnails differ when any cell moved by more than 6 (a page change, a toast, a
    /// tile changing colour); sensor noise stays below.</summary>
    internal static bool Differs(byte[] a, byte[] b)
    {
        for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 6) return true;
        return false;
    }

    // ------------------------------------------------------------------ commands

    private async Task HandleAsync(string command, Dictionary<string, string>? payload, CancellationToken ct)
    {
        switch (command)
        {
            case "get_display_elements":
                if (!_popoutReady) { _popoutError = null; return; }   // the loop opens it again
                await ReadIfChangedAsync(ct, force: true);
                return;
            case "click_display_element":
                if (payload != null && payload.TryGetValue("index", out var ci) && int.TryParse(ci, out int cidx))
                    await ClickAsync(cidx, ct);
                return;
            case "set_element_value":
                if (payload != null && payload.TryGetValue("index", out var si) && int.TryParse(si, out int sidx))
                    await SetAsync(sidx, payload.TryGetValue("value", out var v) ? v : "", ct);
                return;
        }
    }

    private async Task ClickAsync(int aidx, CancellationToken ct)
    {
        if (_built == null || !_built.Actions.TryGetValue(aidx, out var act)) return;
        switch (act.Kind)
        {
            case Bbs757EfbElements.ActionKind.Classic:
                PostUi(_io.OpenClassicWindow);
                return;
            case Bbs757EfbElements.ActionKind.Refresh:
                await ReadIfChangedAsync(ct, force: true);
                return;
            case Bbs757EfbElements.ActionKind.Refuse:
                Alert(act.Refusal ?? "That cannot be pressed from here.");
                return;
            case Bbs757EfbElements.ActionKind.ScrollDown:
            case Bbs757EfbElements.ActionKind.ScrollUp:
                using (var before = _popout.CaptureTablet())
                {
                    await _client.WheelAsync(Bbs757EfbScreen.WheelPoint.X, Bbs757EfbScreen.WheelPoint.Y,
                        act.Kind == Bbs757EfbElements.ActionKind.ScrollDown ? Bbs757EfbScreen.WheelStep : -Bbs757EfbScreen.WheelStep, ct);
                    await WaitForChangeAsync(before, ct);
                }
                await ReadIfChangedAsync(ct, force: true);
                return;
        }
        var item = act.Item;
        // Read-only: only page navigation is tapped (Home, the tablet's menus); a wrong one only changes
        // the page shown. Everything else is the classic window's.
        if (ReadOnly && !(item != null && (item.Kind == Bbs757EfbKind.Menu || item.Key == "btn:HOME")))
        {
            Alert(Bbs757EfbElements.ReadOnlyRefusal);
            return;
        }
        if (act.Kind == Bbs757EfbElements.ActionKind.Tap && item != null)
        {
            string words = item.Key.StartsWith("btn:", StringComparison.Ordinal) ? item.Key.Substring(4).Split('|').Last().Split('@')[0] : "";
            // A ground service the plan has proven: its own flow (state read, tap, read back).
            if (Bbs757GroundServices.ForTile(words) is { } svc && Bbs757GroundServices.Proven.Contains(svc.Key))
            {
                double? now = await ReadVarAsync(svc.StateVar, ct);
                var outcome = await Bbs757GroundServices.SwitchAsync(svc, Bbs757GroundServices.Flag(now) != true, ServiceIo(), ct);
                Alert(outcome.Text ?? RunProblemText(outcome.Run));
                await ReadIfChangedAsync(ct, force: true);
                return;
            }
            if (words.StartsWith("GPU", StringComparison.Ordinal))
            {
                double? connected = await _io.ReadRegistered(Aircraft.BBS757.Bbs757SharedKeys.GpuConnected, ct);
                bool connect = Bbs757GroundServices.Flag(connected) != true;
                var outcome = await Bbs757GroundPower.SwitchAsync(connect, GroundPowerIo(), ct);
                Alert(outcome.Text ?? RunProblemText(outcome.Run));
                await ReadIfChangedAsync(ct, force: true);
                return;
            }
            if (words == "REMOVE ALL")
            {
                double? ext = await _io.ReadRegistered(Bbs757GroundPower.ExtPwrOnKey, ct);
                if (Bbs757GroundServices.RemoveAllRefusal(ext) is { } r) { Alert(r); return; }
            }
            if (Bbs757EfbElements.PresetName(words) is { } preset)
            {
                bool? ground = await _io.OnGround(ct);
                if (ground is not bool g) { Alert("Whether the aircraft is on the ground cannot be read, so the preset was not loaded."); return; }
                if (Bbs757EfbLayout.PresetRefusal(preset, g) is { } pr) { Alert(pr); return; }
            }
        }
        if (item == null) return;
        using (var before = _popout.CaptureTablet())
        {
            await TapAsync(item.Hit, ct);
            await WaitForChangeAsync(before, ct);
        }
        await ReadIfChangedAsync(ct, force: true);

        // Page navigation is checked by the page it lands on. Measured live 2026-10-27: the first change
        // after an app tap is often the icon's press highlight, and the page itself follows 0.4-2.4 s later,
        // so a read on the first change can still be the old page. Read again until the page name is the
        // one tapped, and say so when it never is.
        string? expect = item.Key == "btn:HOME" ? "Home" : item.Kind == Bbs757EfbKind.Menu ? item.Text : null;
        if (expect == null) return;
        long until = Environment.TickCount64 + NavigateMaxMs;
        while (!PageIs(_model, expect) && Environment.TickCount64 < until)
        {
            await Task.Delay(500, ct);
            await ReadIfChangedAsync(ct, force: true);
        }
        if (!PageIs(_model, expect))
        {
            Log.Debug("BBS757", $"EFB reader: tapped '{expect}', page reads '{_model?.PageName}'");
            Alert($"The tablet did not show {expect}. Try again, or use Read the tablet again.");
        }
    }

    /// <summary>Whether the page read is the one a navigation tap asked for (app or section name).</summary>
    internal static bool PageIs(Bbs757EfbScreenModel? m, string expect)
    {
        if (m == null) return false;
        string want = expect.Replace(" (current page)", "").Trim();
        return m.PageName.Equals(want, StringComparison.OrdinalIgnoreCase)
            || m.PageName.StartsWith(want + ",", StringComparison.OrdinalIgnoreCase)
            || m.PageName.EndsWith(", " + want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// After a tap or a scroll, waits for the picture to change from <paramref name="before"/> (at most
    /// <see cref="ChangeMaxMs"/>); the read that follows then settles as usual. Measured live 2026-10-27:
    /// the tablet's new page reaches the ND pop-out late enough that a read 700 ms after the tap caught
    /// the OLD page on 4 of 5 app taps — and an unchanged old page looks perfectly still, so the settle
    /// alone cannot see it. That was the "reads needed repeating" of the first in-sim test.
    /// </summary>
    private async Task WaitForChangeAsync(Bitmap? before, CancellationToken ct)
    {
        if (before == null) { await Task.Delay(AfterTapMs, ct); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ChangeMaxMs)
        {
            await Task.Delay(100, ct);
            using var now = _popout.CaptureTablet();
            if (now != null && !Bbs757EfbFrameCompare.Same(before, now))
            {
                Log.Debug("BBS757", $"EFB reader: picture changed {sw.ElapsedMilliseconds} ms after the tap");
                return;
            }
        }
        Log.Debug("BBS757", $"EFB reader: no change in {ChangeMaxMs} ms after the tap");
    }

    private async Task SetAsync(int aidx, string value, CancellationToken ct)
    {
        if (_built == null || !_built.Actions.TryGetValue(aidx, out var act) || act.Item is not { } item) return;
        if (ReadOnly) { Alert(Bbs757EfbElements.ReadOnlyRefusal); Push(); return; }
        if (act.Kind == Bbs757EfbElements.ActionKind.Refuse) { Alert(act.Refusal ?? "That cannot be changed from here."); Push(); return; }
        if (Bbs757EfbScreen.UpdateGuard.Contains(item.Hit))
        {
            var moved = await OutOfGuardAsync(item, ct);
            if (moved == null) { Alert(Bbs757EfbElements.GuardRefusal); Push(); return; }
            item = moved;
        }
        if (item.Kind is Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox)
        {
            bool want = value == "true";
            if (item.On == want) return;
            await TapAsync(item.Hit, ct);
            if (!await ConfirmAsync(m => Find(m, item.Key)?.On == want, ct))
                Alert($"BlueBird did not change {item.Text}.");
            return;
        }
        if (item.Kind == Bbs757EfbKind.Slider && item.Slider is { } sl)
        {
            var options = Bbs757EfbElements.OptionsFor(item);
            int x;
            if (options != null)
            {
                int target = Array.FindIndex(options, o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));
                if (target < 0) return;
                x = Bbs757EfbElements.OptionX(sl, target, options.Length);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    await TapAsync(new Point(x, item.Hit.Y), ct);
                    if (await ConfirmAsync(m => string.Equals(Find(m, item.Key)?.Value, options[target], StringComparison.OrdinalIgnoreCase), ct)) return;
                    // Off by one: step toward it by a third of an option.
                    var now = Find(_model, item.Key)?.Value;
                    int at = now == null ? -1 : Array.FindIndex(options, o => string.Equals(o, now, StringComparison.OrdinalIgnoreCase));
                    if (at < 0) break;
                    double seg = (double)(sl.TrackRight - sl.TrackLeft) / Math.Max(1, options.Length - 1);
                    x += (int)Math.Round(Math.Sign(target - at) * Math.Max(4, seg / 3));
                }
                Alert($"{item.Text} could not be set to {value}.");
                return;
            }
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                x = sl.TrackLeft + (int)Math.Round(Math.Clamp(pct, 0, 100) / 100.0 * (sl.TrackRight - sl.TrackLeft));
                await TapAsync(new Point(x, item.Hit.Y), ct);
                await ConfirmAsync(_ => false, ct, maxMs: 1500);
            }
        }
    }

    /// <summary>Reads the tablet again until <paramref name="ok"/> holds for the new page, or the time is
    /// up; true when it held.</summary>
    private async Task<bool> ConfirmAsync(Func<Bbs757EfbScreenModel?, bool> ok, CancellationToken ct, int maxMs = 4000)
    {
        long until = Environment.TickCount64 + maxMs;
        await Task.Delay(400, ct);
        while (true)
        {
            await ReadIfChangedAsync(ct, force: true);
            if (ok(_model)) return true;
            if (Environment.TickCount64 >= until) return false;
            await Task.Delay(300, ct);
        }
    }

    private static Bbs757EfbItem? Find(Bbs757EfbScreenModel? m, string key) =>
        m?.Items.FirstOrDefault(i => i.Key == key) ?? m?.Menu.FirstOrDefault(i => i.Key == key);

    /// <summary>
    /// A control in the update guard on a page that scrolls: scroll it out of the guard (one step down,
    /// up to three), read the page again and return the control where it is now; null when it never
    /// leaves the guard (then nothing is tapped). Measured: one step moves the Callouts list ~130 px.
    /// </summary>
    private async Task<Bbs757EfbItem?> OutOfGuardAsync(Bbs757EfbItem item, CancellationToken ct)
    {
        var now = item;
        for (int i = 0; i < 3 && Bbs757EfbScreen.UpdateGuard.Contains(now.Hit); i++)
        {
            int dir = now.Hit.Y < Bbs757EfbScreen.UpdateGuard.Top + Bbs757EfbScreen.UpdateGuard.Height / 2 ? -1 : 1;
            await _client.WheelAsync(Bbs757EfbScreen.WheelPoint.X, Bbs757EfbScreen.WheelPoint.Y, dir * Bbs757EfbScreen.WheelStep, ct);
            await Task.Delay(400, ct);
            await ReadIfChangedAsync(ct, force: true);
            var found = Find(_model, item.Key);
            if (found == null) return null;
            now = found;
        }
        return Bbs757EfbScreen.UpdateGuard.Contains(now.Hit) ? null : now;
    }

    private async Task TapAsync(Point p, CancellationToken ct)
    {
        // Never into the update guard (the agent would refuse it too).
        if (Bbs757EfbScreen.UpdateGuard.Contains(p)) { Alert(Bbs757EfbElements.GuardRefusal); return; }
        var run = await _client.RunAsync(new[] { (new EfbPoint(p.X, p.Y), 250) }, ct);
        if (run != Bbs757EfbRunResult.Done) Alert(RunProblemText(run));
    }

    private async Task<double?> ReadVarAsync(string name, CancellationToken ct)
    {
        var vals = await _client.ReadVarsAsync(new[] { name }, ct);
        return vals != null && vals.TryGetValue(name, out var v) ? v : null;
    }

    private Bbs757GroundServiceIo ServiceIo() => new()
    {
        ReadState = ReadVarAsync,
        Run = async (seq, ct) => { var r = await _client.RunAsync(seq, ct); return (r, _client.LastRunMayHaveClicked); },
        Delay = (ms, ct) => Task.Delay(ms, ct),
        NowMs = () => Environment.TickCount64,
        ArmEcho = s => PostUi(() => _io.ArmServiceEcho(s)),
        SettleEcho = (s, v) => PostUi(() => _io.SettleServiceEcho(s, v)),
    };

    private Bbs757GroundPowerIo GroundPowerIo() => new()
    {
        Read = (key, ct) => _io.ReadRegistered(key, ct),
        Run = async (seq, ct) =>
        {
            // The GPU tile where the page reader found it (calibrated), else plan 07's measured point.
            var gpu = _model?.Items.FirstOrDefault(i => i.Kind == Bbs757EfbKind.Button && i.Key.Contains("GPU", StringComparison.Ordinal));
            var points = gpu != null ? new[] { (new EfbPoint(gpu.Hit.X, gpu.Hit.Y), 400) } : seq;
            var r = await _client.RunAsync(points, ct);
            return (r, _client.LastRunMayHaveClicked);
        },
        Delay = (ms, ct) => Task.Delay(ms, ct),
        NowMs = () => Environment.TickCount64,
        ArmEcho = () => PostUi(_io.ArmGroundPowerEcho),
        SettleEcho = v => PostUi(() => _io.SettleGroundPowerEcho(v)),
    };

    private static string RunProblemText(Bbs757EfbRunResult run) => run switch
    {
        Bbs757EfbRunResult.Unreachable => "The BlueBird tablet cannot be reached, so nothing was tapped.",
        Bbs757EfbRunResult.LayoutUnknown => "The 757's EFB layout is not the one this version knows, so nothing was tapped.",
        _ => "The tablet did not confirm the tap.",
    };

    // ------------------------------------------------------------------ pushing

    /// <summary>
    /// Shows <paramref name="text"/> in the page's place, until the next page or status. Internal for the tests.
    /// </summary>
    internal void Status(string text)
    {
        _model = null;
        // The page is no longer shown, so the next read reads it even when the picture has not changed (an unchanged
        // tablet is never left behind a status) and pushes it even when it reads the same as before.
        _forceRead = true;
        _lastFingerprint = null;
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + 600_000;
        Push();
    }

    private void Alert(string text)
    {
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + 8000;
        Push();
    }

    private void Push()
    {
        string? alert = _alert != null && Environment.TickCount64 < _alertUntilMs ? _alert : null;
        _built = Bbs757EfbElements.Build(_model, alert, ReadOnly);
        Raise(StateTypeElements, _built.Data);
    }

    private void Raise(string type, Dictionary<string, string> data)
    {
        var args = new EFBStateUpdateEventArgs { Type = type, Data = data };
        if (_sync != null) _sync.Post(_ => StateUpdated?.Invoke(this, args), null);
        else StateUpdated?.Invoke(this, args);
    }

    private void PostUi(Action a)
    {
        if (_sync != null) _sync.Post(_ => a(), null); else a();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1500); } catch { }
        _cts.Dispose();
    }
}
