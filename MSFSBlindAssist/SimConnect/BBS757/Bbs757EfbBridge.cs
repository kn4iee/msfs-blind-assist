using System.Drawing;
using System.Globalization;
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
}

/// <summary>
/// The 757 EFB for the shared accessible EFB window (FbwEfbForm, R73): BlueBird's tablet, READ from its
/// pixels (plan 11, ruling of 2026-10-26: Bbs757EfbPopout, Bbs757EfbOcr, Bbs757EfbScreen) and
/// operated through the EFB agent's clicks, as an IMcduBridge.
///
/// While the window is shown, the tablet is mirrored onto the captain's ND (the agent's mirror) and the
/// ND pop-out is captured about every 0.7 s; a page is read (OCR, about 1-3 s) only when the picture
/// changed, after an action, or on request, so a still tablet costs a capture and a 32 x 24 compare.
/// Each read becomes the window's elements (<see cref="Bbs757EfbElements"/>); each press taps the
/// tablet and reads it again. Hidden, nothing is captured and the mirror is off (the ND is the ND).
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

    private readonly CoherentBbs757EfbClient _client;
    private readonly Bbs757EfbPopout _popout;
    private readonly Bbs757EfbBridgeIo _io;
    private readonly SynchronizationContext? _sync;
    private readonly System.Threading.Channels.Channel<(string Command, Dictionary<string, string>? Payload)> _commands =
        System.Threading.Channels.Channel.CreateUnbounded<(string, Dictionary<string, string>?)>();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);

    private volatile bool _active;
    private bool _popoutReady;
    private string? _popoutError;
    private bool _mirrorOn;
    private long _mirrorAtMs;
    private byte[]? _lastSignature;
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
    }

    public bool IsBridgeConnected => _client.IsReady && _popoutReady;

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

    public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
    {
        _commands.Writer.TryWrite((command, payload));
        _wake.Release();
    }

    // ------------------------------------------------------------------ the loop

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_active)
                {
                    if (_mirrorOn) { await _client.SetMirrorAsync(false, ct); _mirrorOn = false; }
                    await _wake.WaitAsync(ct);
                    continue;
                }
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

    /// <summary>Captures the tablet; reads it when the picture changed (status bar excluded: its clock),
    /// a read was asked for, or the last read is old.</summary>
    private async Task<bool> ReadIfChangedAsync(CancellationToken ct, bool force = false)
    {
        using var bmp = _popout.CaptureTablet();
        if (bmp == null) { _popoutReady = false; Status("The tablet reader's window closed. Opening it again."); return false; }
        var sig = Signature(bmp);
        bool changed = _lastSignature == null || Differs(sig, _lastSignature);
        long now = Environment.TickCount64;
        if (!force && !_forceRead && !changed && now - _lastReadMs < StalePageMs) return false;
        _forceRead = false;
        var read = await Bbs757EfbPageReader.ReadAsync(bmp, ct);
        _lastSignature = sig;
        _lastReadMs = Environment.TickCount64;
        _model = read.Model;
        Log.Debug("BBS757", $"EFB reader: {read.Model.PageName}, {read.Model.Items.Count} items, {read.OcrMs} ms, {read.Rereads} re-reads");
        Push();
        return true;
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
                await _client.WheelAsync(Bbs757EfbScreen.WheelPoint.X, Bbs757EfbScreen.WheelPoint.Y,
                    act.Kind == Bbs757EfbElements.ActionKind.ScrollDown ? Bbs757EfbScreen.WheelStep : -Bbs757EfbScreen.WheelStep, ct);
                await Task.Delay(400, ct);
                await ReadIfChangedAsync(ct, force: true);
                return;
        }
        var item = act.Item;
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
        await TapAsync(item.Hit, ct);
        await Task.Delay(AfterTapMs, ct);
        await ReadIfChangedAsync(ct, force: true);
    }

    private async Task SetAsync(int aidx, string value, CancellationToken ct)
    {
        if (_built == null || !_built.Actions.TryGetValue(aidx, out var act) || act.Item is not { } item) return;
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
            if (item.Key == "tgl:NOSEWHEEL STEERING")
            {
                // [B757-2]: the steering switch TOGGLES; it is judged from BlueBird's own setting
                // (tiller_realistic: true = REALISTIC = the switch on), never tapped unread or already there.
                string? folder = Bbs757WorkFolder.Find();
                bool? realistic = folder == null ? null : Bbs757WorkFolder.ReadTillerRealistic(folder);
                if (realistic is not bool r) { Alert("BlueBird's steering setting cannot be read, so nothing was tapped."); Push(); return; }
                if (r == want) { Push(); return; }
                await TapAsync(item.Hit, ct);
                await Task.Delay(800, ct);
                bool? after = folder == null ? null : Bbs757WorkFolder.ReadTillerRealistic(folder);
                if (after != want) Alert("BlueBird did not change the nosewheel steering setting.");
                await ReadIfChangedAsync(ct, force: true);
                return;
            }
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

    private void Status(string text)
    {
        _model = null;
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
        _built = Bbs757EfbElements.Build(_model, alert);
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
