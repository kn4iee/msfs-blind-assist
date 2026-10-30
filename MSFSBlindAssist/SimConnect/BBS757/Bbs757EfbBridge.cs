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

    /// <summary>A variable the definition registers, read fresh (the ground power's connection, read after a GPU tile's tap).</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadRegistered { get; init; }

    /// <summary>
    /// A setting read now from BlueBird's file (<c>efb_settings.json</c>, or for a registration setting the registration file
    /// written last), in the work folder <see cref="Bbs757WorkFolder.Find()"/> picks; null when it cannot be read. Called on the
    /// bridge's loop, to judge a Settings press by the file (plan 18, Task 8).
    /// </summary>
    public required Func<Bbs757Setting, CancellationToken, Task<Bbs757Value?>> ReadSetting { get; init; }

    /// <summary>Whether the pilot is signed in to Navigraph, read through the definition's tablet client: true, false, or null when
    /// it cannot be read. Called on the bridge's loop when the AIRAC page was newly read.</summary>
    public required Func<CancellationToken, Task<bool?>> SignedIn { get; init; }

    /// <summary>
    /// The Navigraph sign-in (<see cref="Bbs757NavigraphSignIn.SignInAsync"/>, MainForm-built over the tablet client): it speaks as
    /// it goes and returns the line the page keeps. Its early line ("Navigraph code: {code}", before the code is spoken) reaches the
    /// page through <see cref="Bbs757EfbBridge.ShowSignInLine"/>. Called on the bridge's loop, which awaits it.
    /// </summary>
    public required Func<CancellationToken, Task<string>> SignIn { get; init; }

    /// <summary>A press started one of the app's hooks (<see cref="Bbs757EfbExtras.HookFor"/>): the load watch or the nav data watch.
    /// Called on the UI thread (the bridge marshals), once the tap went out; for UPDATE or REMOVE with the nav data cycle installed
    /// before the tap (read on the loop right before it, as BlueBird's REMOVE may delete the file at once), else null.</summary>
    public required Action<Bbs757EfbHook, int?> Hooked { get; init; }

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
/// pressed through the EFB agent's clicks where the reader found each control, as an IMcduBridge.
///
/// While the window is shown, the tablet is mirrored onto the captain's ND (the agent's mirror) and the
/// ND pop-out is captured about every 0.7 s; a page is read (PaddleOCR, about half a second) only when
/// the picture changed, its top bar's right part changed (the departure countdown, the boarding control,
/// the notification count: a signature of their own, <see cref="TopBarSignature(Bitmap)"/>), after an
/// action, or on request, so a still tablet costs a capture and two small compares. Each read becomes the
/// window's elements (<see cref="Bbs757EfbElements"/>), kept with the frame it was read from
/// (<see cref="Bbs757EfbPushes"/>); what a read says by itself, a question that appeared or BlueBird's new
/// notifications, is said as the window's alert (<see cref="Bbs757EfbReadNews"/>). Hidden, nothing is
/// captured and the mirror is off (the ND is the ND).
///
/// Presses (plan 18, Task 8; the owner's ruling of 2026-10-29: nothing refuses a press) queue on the command
/// channel and run one at a time on the loop, through <see cref="Bbs757EfbPressRunner"/>: a press names the
/// element of the push it was made on, so it is judged against the page the pilot pressed on and the frame
/// that page was read from, and is tapped where the control is now, or not at all when it is gone. The
/// AIRAC page carries the app's extras (the Navigraph sign-in while signed out, and its code line), and a
/// press that starts one of the app's hooks (the load watch, the nav data watch) tells MainForm on the UI
/// thread. Speech follows CORE-7: the screen reader reads the control; a pressed control the tablet gave
/// other words is said once in its new words, by the press runner, whatever its key (the window says
/// nothing of them); a failure is said as an alert.
///
/// The text recognition engine is loaded at the first read while the window is shown and released when
/// it hides (<see cref="TextRecognitionEngineSlot{T}"/>), both only on the loop, which runs every read,
/// so it is never released under a running read. An engine that cannot load is said once, as the
/// reader's status, and said again when a later status took its place (the window never blames
/// something else). It is tried again after the window was hidden and shown, or when the pilot asks for
/// the reader again with the models Ready (<see cref="RetryTextRecognition"/>: a flag the loop takes
/// before its next read, so the engine stays the loop's alone).
/// </summary>
public sealed class Bbs757EfbBridge : IMcduBridge, IDisposable
{
    private const string StateTypeElements = "fbw_efb_elements";
    private const string StateTypeConnected = "fbw_efb_connected";
    private const int TickMs = 700;
    private const int MirrorRefreshMs = 10_000;
    private const int StalePageMs = 20_000;
    private const int SettleGapMs = 250;
    private const int SettleMaxMs = 2500;
    private const int AlertMs = 8000;
    private const int StatusMs = 600_000;

    /// <summary>The top bar's right part, which gets a signature of its own (ruling 10): x 1000 to the tablet's right edge.</summary>
    private const int TopBarLeft = 1000, TopBarRight = Bbs757EfbScreen.Width;

    private const string UnclearText = "The tablet read differently each time, so this page may not be right. Use Read the tablet again.";

    /// <summary>The app's extras on the AIRAC page: the sign-in button and the sign-in's line.</summary>
    private const string SignInKey = "x:navigraph", SignInLineKey = "x:navigraph-code";

    private readonly CoherentBbs757EfbClient _client;
    private readonly Bbs757EfbPopout _popout;
    private readonly Bbs757EfbBridgeIo _io;
    private readonly Bbs757EfbPressRunner _press;
    private readonly SynchronizationContext? _sync;
    /// <summary>The window's commands, each with the showing it was sent in (<see cref="_showing"/>).</summary>
    private readonly System.Threading.Channels.Channel<(string Command, Dictionary<string, string>? Payload, int Showing)> _commands =
        System.Threading.Channels.Channel.CreateUnbounded<(string, Dictionary<string, string>?, int)>();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    /// <summary>The text recognition engine. Touched only on the loop: Get before a read, ForgetFailure when it
    /// takes a retry, Release when the window hides, Dispose when the loop ends.</summary>
    private readonly TextRecognitionEngineSlot<PaddleOcrEngine> _ocr;
    /// <summary>The pushes the window may still press on, with the pages they showed and their frames. On the loop only.</summary>
    private readonly Bbs757EfbPushes _pushes = new();
    private readonly Bbs757EfbReadNews _news = new();

    /// <summary>Set by <see cref="RetryTextRecognition"/> on the UI thread, taken by the loop
    /// (<see cref="TakeTextRecognitionRetry"/>).</summary>
    private volatile bool _retryOcr;

    private volatile bool _active;
    /// <summary>Counts the times the window hid (UI thread; read on the loop): a command sent in an earlier showing is dropped.</summary>
    private int _showing;
    private bool _popoutReady;
    private string? _popoutError;
    private bool _mirrorOn;
    private long _mirrorAtMs;
    private byte[]? _lastSignature;
    private byte[]? _lastTopBar;
    private Bitmap? _lastReadFrame;
    private string? _lastFingerprint;
    private long _lastReadMs;
    private bool _forceRead = true;
    /// <summary>The page the window shows, with the frame it was read from; null while a status stands in its place.</summary>
    private Bbs757EfbShownPage? _shown;
    /// <summary>The page the last read gave, shown or not (the press runner's read).</summary>
    private Bbs757EfbScreenModel? _lastRead;
    /// <summary>When the pilot's last tap went out: BlueBird's notifications are not said for a while after it.</summary>
    private long _lastOwnTapMs = long.MinValue / 2;
    private bool? _signedIn;
    private bool _signInStale;
    private string? _signInLine;
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
        _press = new Bbs757EfbPressRunner(PressIo());
    }

    public bool IsBridgeConnected => _client.IsReady && _popoutReady;

    private Bbs757EfbScreenModel? Model => _shown?.Model;

    /// <summary>Starts the bridge's loop (once). It reads only while <see cref="SetActive"/> says the
    /// window is shown.</summary>
    public void Start()
    {
        _loop ??= Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <summary>The window was shown (true) or hidden (false): reading and the mirror follow it. Hidden, the presses it sent and
    /// the loop has not come to are dropped: they were made on a tablet the next showing may no longer show.</summary>
    public void SetActive(bool active)
    {
        if (!active) Interlocked.Increment(ref _showing);
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
        _commands.Writer.TryWrite((command, payload, Volatile.Read(ref _showing)));
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
            // The loop has ended, so no read runs: the engine and the frames go with it (Dispose may have stopped waiting for it).
            _ocr.Dispose();
            FreeFrames();
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
                    // Hidden: what the window sent is dropped (it never runs at the next showing), the engine is freed
                    // (no read runs between two turns of the loop), and a load that failed is forgotten with the
                    // reader's word of it, so the next showing tries again.
                    DropQueuedCommands();
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

                if (await HandleNextCommandAsync(ct))
                {
                    await RefreshSignInAsync(ct);
                    continue;
                }

                await ReadIfChangedAsync(ct);
                await RefreshSignInAsync(ct);
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
    /// Captures the tablet and reads it when the picture changed (status bar excluded: its clock), the top bar's
    /// right part changed, a read was asked for, or the last read is old AND the pixels moved since. What the pilot
    /// hears must not change unless the tablet did (plan 11 feedback: "the presentation changed upon refreshes"):
    /// <list type="bullet">
    /// <item>a picture that has not really changed (rendering noise only, <see cref="Bbs757EfbFrameCompare"/>) is
    /// never read again on its own: OCR noise was all a re-read could add;</item>
    /// <item>a read waits for the picture to hold still (a page still animating in read as garbage);</item>
    /// <item>a picture that never holds still is read from two captures, and the page is taken only when
    /// the two reads agree on the page itself; else a third read decides, and when it agrees with neither the page
    /// is shown with a warning that it may be wrong (<see cref="AgreeAsync"/>);</item>
    /// <item>a read that says the same as the page already shown is not pushed again (<see cref="TakeRead"/>).</item>
    /// </list>
    /// On the loop only, as it gets the engine (OCR-3); internal for the tests, which call it on a bridge whose
    /// loop never started.
    /// </summary>
    internal async Task<bool> ReadIfChangedAsync(CancellationToken ct, bool force = false)
    {
        // No engine (it could not load, and the reader says so): nothing is captured or read.
        var engine = EngineForRead();
        if (engine == null) return false;
        Bitmap? bmp = _popout.CaptureTablet();
        if (bmp == null) { _popoutReady = false; Status("The tablet reader's window closed. Opening it again."); return false; }
        var made = new List<TabletRead>();
        try
        {
            var px = Bbs757EfbPixels.FromBitmap(bmp);
            var sig = Signature(px);
            var bar = TopBarSignature(px);
            bool changed = _lastSignature == null || Differs(sig, _lastSignature);
            bool barChanged = _lastTopBar != null && Differs(bar, _lastTopBar);
            long now = Environment.TickCount64;
            bool stale = now - _lastReadMs >= StalePageMs;
            if (!force && !_forceRead && !changed && !barChanged && !stale) return false;
            // Nothing really moved since the last read (rendering noise only): reading again could only add OCR noise. The top
            // bar is outside that comparison, so a change there is read whatever the page did.
            if (!force && !_forceRead && !barChanged && _lastReadFrame != null && Bbs757EfbFrameCompare.Same(bmp, _lastReadFrame))
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

            var first = new TabletRead(await Bbs757EfbPageReader.ReadAsync(engine, bmp, ct), bmp);
            made.Add(first);
            bmp = null;   // the read's frame from here on
            var (taken, reads, unclear) = still
                ? (first, 1, false)
                : await AgreeAsync(first, async () =>
                {
                    // Never held still (something on the page animates): agree before the page is taken.
                    var again = await ReadFreshCaptureAsync(ct);
                    if (again != null) made.Add(again);
                    return again;
                }, r => r.Read.Model);

            var takenPx = Bbs757EfbPixels.FromBitmap(taken.Frame);
            _lastSignature = Signature(takenPx);
            _lastTopBar = TopBarSignature(takenPx);
            _lastReadFrame?.Dispose();
            _lastReadFrame = new Bitmap(taken.Frame);
            _lastReadMs = Environment.TickCount64;
            made.Remove(taken);
            bool pushed = TakeRead(taken.Read.Model, taken.Frame, unclear);
            Log.Debug("BBS757", $"EFB reader: {taken.Read.Model.PageName}, {taken.Read.Model.Items.Count} items, {taken.Read.OcrMs} ms, "
                + $"still={still}, reads={reads}, unclear={unclear}, pushed={pushed}");
            return true;
        }
        finally
        {
            bmp?.Dispose();
            foreach (var r in made) r.Frame.Dispose();
        }
    }

    /// <summary>One read of the tablet and the capture it was read from (owned).</summary>
    private sealed record TabletRead(Bbs757EfbPageRead Read, Bitmap Frame);

    private async Task<TabletRead?> ReadFreshCaptureAsync(CancellationToken ct)
    {
        var engine = EngineForRead();
        if (engine == null) return null;
        var b = _popout.CaptureTablet();
        if (b == null) return null;
        try
        {
            return new TabletRead(await Bbs757EfbPageReader.ReadAsync(engine, b, ct), b);
        }
        catch
        {
            b.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The read to take of a page that never held still (something on it animates), from <paramref name="first"/> and up to two
    /// more reads (<paramref name="again"/>, null when none could be made): the later of the first two when they agree; else the
    /// later of the pair a third agrees with; else the third, unclear. Reads agree when the page itself reads the same
    /// (<see cref="Bbs757EfbPress.PageUnchanged"/>, ruling 2): the departure countdown ticking or a notification passing between two
    /// reads never makes them disagree. The count is the reads made. Internal for the tests.
    /// </summary>
    internal static async Task<(T Taken, int Reads, bool Unclear)> AgreeAsync<T>(T first, Func<Task<T?>> again, Func<T, Bbs757EfbScreenModel> model)
        where T : class
    {
        var second = await again();
        if (second == null) return (first, 1, false);
        if (Bbs757EfbPress.PageUnchanged(model(first), model(second))) return (second, 2, false);
        var third = await again();
        if (third == null) return (first, 2, true);
        if (Bbs757EfbPress.PageUnchanged(model(second), model(third))) return (third, 3, false);
        if (Bbs757EfbPress.PageUnchanged(model(first), model(third))) return (third, 3, false);
        return (third, 3, true);
    }

    /// <summary>
    /// A read taken as what the tablet shows; <paramref name="frame"/>, the picture it was read from, is the bridge's from here on.
    /// What the read says by itself is said (<see cref="Bbs757EfbReadNews"/>: a question that was not in the previous read, BlueBird's
    /// notifications that were not, unless the pilot's own tap was under four seconds ago), with the warning when its reads disagreed,
    /// as one alert; and it is pushed when it says something the window does not show yet, kept with its frame for the presses made
    /// on it. A newly shown AIRAC page has the Navigraph sign-in read again. True when pushed. On the loop only; internal for the
    /// tests, which give it pages read from the captures.
    /// </summary>
    internal bool TakeRead(Bbs757EfbScreenModel model, Bitmap frame, bool unclear)
    {
        _lastRead = model;
        string? said = _news.After(model, Environment.TickCount64 - _lastOwnTapMs);
        if (unclear) said = said == null ? UnclearText : said + " " + UnclearText;
        string fp = Bbs757EfbPress.Fingerprint(model);
        bool fresh = fp != _lastFingerprint;
        if (!fresh)
        {
            frame.Dispose();
            if (said == null) return false;
        }
        else
        {
            _lastFingerprint = fp;
            _shown = _pushes.Show(model, frame);
            if (IsAiracPage(model.PageName)) _signInStale = true;
        }
        if (said != null) SetAlert(said);
        Push();
        return true;
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
    internal static byte[] Signature(Bitmap bmp) => Signature(Bbs757EfbPixels.FromBitmap(bmp));

    private static byte[] Signature(Bbs757EfbPixels px)
    {
        // Sampled, not scaled: a scaled thumbnail blends the status bar (its clock) into the cells below it.
        int top = Bbs757EfbScreen.StatusBottom + 4, h = px.Height - top;
        var o = new byte[32 * 24];
        for (int cy = 0; cy < 24; cy++)
            for (int cx = 0; cx < 32; cx++)
            {
                int sum = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        int x = (int)((cx + (sx + 0.5) / 3.0) * px.Width / 32), y = top + (int)((cy + (sy + 0.5) / 3.0) * h / 24);
                        sum += (int)px.Lum(Math.Min(x, px.Width - 1), Math.Min(y, px.Height - 1));
                    }
                o[cy * 32 + cx] = (byte)(sum / 9);
            }
        return o;
    }

    /// <summary>
    /// A 32 x 4 grey thumbnail of the top bar's right part, x 1000-1397 and y 0-32 (ruling 10): the departure countdown, the
    /// boarding control and the notification count, which the page's <see cref="Signature(Bitmap)"/> leaves out with the clock. A
    /// change there reads the page again (and pushes it only when it reads differently); the clock, the date and the outside air
    /// temperature at the bar's left never do. Nothing about the top bar is spoken.
    /// </summary>
    internal static byte[] TopBarSignature(Bitmap bmp) => TopBarSignature(Bbs757EfbPixels.FromBitmap(bmp));

    private static byte[] TopBarSignature(Bbs757EfbPixels px)
    {
        int left = Math.Min(TopBarLeft, px.Width - 1), width = Math.Min(TopBarRight, px.Width) - left;
        int bottom = Math.Min(Bbs757EfbScreen.StatusBottom, px.Height);
        var o = new byte[32 * 4];
        for (int cy = 0; cy < 4; cy++)
            for (int cx = 0; cx < 32; cx++)
            {
                int sum = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        int x = left + (int)((cx + (sx + 0.5) / 3.0) * width / 32), y = (int)((cy + (sy + 0.5) / 3.0) * bottom / 4);
                        sum += (int)px.Lum(Math.Min(x, px.Width - 1), Math.Min(y, px.Height - 1));
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

    /// <summary>
    /// The next command the window sent, handled; false when none waits. A command sent before the window last hid is dropped
    /// unhandled: a press made on what the window showed then never runs on what it shows now, the tablet having gone on meanwhile.
    /// On the loop only; internal for the tests.
    /// </summary>
    internal async Task<bool> HandleNextCommandAsync(CancellationToken ct)
    {
        while (_commands.Reader.TryRead(out var cmd))
        {
            if (cmd.Showing != Volatile.Read(ref _showing))
            {
                Log.Debug("BBS757", $"EFB reader: {cmd.Command} sent before the window hid, dropped.");
                continue;
            }
            await HandleAsync(cmd.Command, cmd.Payload, ct);
            return true;
        }
        return false;
    }

    /// <summary>The window hid: what it sent before is dropped, so nothing it asked for runs at the next showing.</summary>
    private void DropQueuedCommands()
    {
        int dropped = 0;
        while (_commands.Reader.TryRead(out _)) dropped++;
        if (dropped > 0) Log.Debug("BBS757", $"EFB reader: {dropped.ToString(CultureInfo.InvariantCulture)} command(s) dropped as the window hid.");
    }

    /// <summary>A command from the window, on the loop: a read asked for, or a press on an element of a push. On the loop only;
    /// internal for the tests.</summary>
    internal async Task HandleAsync(string command, Dictionary<string, string>? payload, CancellationToken ct)
    {
        switch (command)
        {
            case "get_display_elements":
                if (!_popoutReady) { _popoutError = null; return; }   // the loop opens it again
                await ReadIfChangedAsync(ct, force: true);
                return;
            case "click_display_element":
                if (Pressed(payload) is { } click) await ClickAsync(click.Action, click.Page, ct);
                return;
            case "set_element_value":
                if (Pressed(payload) is { } set) await SetAsync(set.Action, set.Page, payload!.TryGetValue("value", out var v) ? v : "", ct);
                return;
        }
    }

    /// <summary>The element a press names (its push's number), with the page that push showed; null when no kept push gave the
    /// number out, so nothing is tapped.</summary>
    private (Bbs757EfbElements.Action Action, Bbs757EfbShownPage? Page)? Pressed(Dictionary<string, string>? payload)
    {
        if (payload == null || !payload.TryGetValue("index", out var index)
            || !int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aidx)) return null;
        if (_pushes.TryFind(aidx, out var action, out var page) && action != null) return (action, page);
        Log.Debug("BBS757", $"EFB reader: a press named element {aidx.ToString(CultureInfo.InvariantCulture)}, which no kept push gave out; nothing tapped.");
        return null;
    }

    private async Task ClickAsync(Bbs757EfbElements.Action act, Bbs757EfbShownPage? page, CancellationToken ct)
    {
        BeginPress();
        switch (act.Kind)
        {
            case Bbs757EfbElements.ActionKind.Refresh:
                await ReadIfChangedAsync(ct, force: true);
                return;
            case Bbs757EfbElements.ActionKind.ScrollDown or Bbs757EfbElements.ActionKind.ScrollUp:
                await _press.ScrollAsync(act.Kind == Bbs757EfbElements.ActionKind.ScrollDown, ct);
                return;
            case Bbs757EfbElements.ActionKind.Extra:
                await PressExtraAsync(act.ExtraKey, ct);
                return;
            case Bbs757EfbElements.ActionKind.Tap when act.Item is { } item && page != null:
                await _press.ClickAsync(new Bbs757EfbPressed(item, page.Model, page.Frame), ct);
                return;
        }
    }

    private async Task SetAsync(Bbs757EfbElements.Action act, Bbs757EfbShownPage? page, string value, CancellationToken ct)
    {
        if (act.Kind != Bbs757EfbElements.ActionKind.Tap || act.Item is not { } item || page == null) return;
        BeginPress();
        var pressed = new Bbs757EfbPressed(item, page.Model, page.Frame);
        if (item.Kind is Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox)
            await _press.SwitchAsync(pressed, value == "true", ct);
        else if (item.Kind == Bbs757EfbKind.Slider)
            await _press.SliderAsync(pressed, value, ct);
    }

    /// <summary>A press of the pilot's begins: the alert shown (a press's outcome, a question, a notification) goes, so the window
    /// speaks the next one even when it has the same words (it says an alert's text once while it stays). A status stays: it is why
    /// no page is shown.</summary>
    private void BeginPress()
    {
        if (_alert == null || Model == null) return;
        _alert = null;
        Push();
    }

    /// <summary>The press runner's seams: the pop-out's captures, this bridge's settled read, the agent's taps, wheel and variable
    /// reads, BlueBird's file through the app, the window's alert, and the app's echoes and hooks marshalled to the UI thread.</summary>
    private Bbs757EfbPressIo PressIo() => new()
    {
        Capture = () => _popout.CaptureTablet(),
        Same = Bbs757EfbFrameCompare.Same,
        SameTopBar = (a, b) => !Differs(TopBarSignature(a), TopBarSignature(b)),
        ReadInstalledCycle = () => Bbs757WorkFolder.Find() is { } folder ? Bbs757WorkFolder.ReadInstalledCycle(folder) : null,
        Read = async ct => await ReadIfChangedAsync(ct, force: true) ? _lastRead : null,
        Tap = TapAsync,
        Wheel = async (delta, ct) => await _client.WheelAsync(Bbs757EfbScreen.WheelPoint.X, Bbs757EfbScreen.WheelPoint.Y, delta, ct),
        ReadVar = ReadVarAsync,
        ReadRegistered = (key, ct) => _io.ReadRegistered(key, ct),
        ReadSetting = (setting, ct) => _io.ReadSetting(setting, ct),
        Alert = Alert,
        Delay = (ms, ct) => Task.Delay(ms, ct),
        NowMs = () => Environment.TickCount64,
        ArmServiceEcho = s => PostUi(() => _io.ArmServiceEcho(s)),
        SettleServiceEcho = (s, v) => PostUi(() => _io.SettleServiceEcho(s, v)),
        ArmGroundPowerEcho = () => PostUi(_io.ArmGroundPowerEcho),
        SettleGroundPowerEcho = v => PostUi(() => _io.SettleGroundPowerEcho(v)),
        Hooked = (hook, cycle) => PostUi(() => _io.Hooked(hook, cycle)),
    };

    /// <summary>One tap through the EFB agent: true when it went out (done, or started and maybe clicked), and then it is the pilot's
    /// own press, after which BlueBird's notifications are not said for a while.</summary>
    private async Task<bool> TapAsync(Point p, CancellationToken ct)
    {
        long at = Environment.TickCount64;
        var run = await _client.RunAsync(new[] { (new EfbPoint(p.X, p.Y), 250) }, ct);
        bool tapped = run == Bbs757EfbRunResult.Done || _client.LastRunMayHaveClicked;
        if (tapped) _lastOwnTapMs = at;
        if (run != Bbs757EfbRunResult.Done) Log.Debug("BBS757", $"EFB reader: a tap ended {run}{(tapped ? ", and may have clicked" : "")}.");
        return tapped;
    }

    private async Task<double?> ReadVarAsync(string name, CancellationToken ct)
    {
        var vals = await _client.ReadVarsAsync(new[] { name }, ct);
        return vals != null && vals.TryGetValue(name, out var v) ? v : null;
    }

    // ------------------------------------------------------------------ the AIRAC page's extras

    private static bool IsAiracPage(string pageName) => Bbs757EfbSettingsMatch.SectionFor(pageName) == Bbs757SettingSection.AiracData;

    /// <summary>The app's extras on the page shown: on the AIRAC page, the Navigraph sign-in while the pilot is known to be signed
    /// out, and the sign-in's line (the code while it is spoken, then what came of it).</summary>
    private IReadOnlyList<Bbs757EfbExtraElement> Extras()
    {
        if (Model is not { } page || !IsAiracPage(page.PageName)) return Array.Empty<Bbs757EfbExtraElement>();
        var extras = new List<Bbs757EfbExtraElement>(2);
        if (Bbs757EfbExtras.OffersSignIn(page.PageName, _signedIn)) extras.Add(new(SignInKey, Bbs757EfbExtras.SignInText, IsButton: true));
        if (_signInLine != null) extras.Add(new(SignInLineKey, _signInLine, IsButton: false));
        return extras;
    }

    /// <summary>
    /// The Navigraph sign-in read again once the AIRAC page was newly shown (its picture changed: the pilot may have signed in in
    /// the browser meanwhile) or a sign-in ran, between presses and reads, never inside one. Signed in, the code line is used up.
    /// A change is pushed. On the loop only; internal for the tests.
    /// </summary>
    internal async Task RefreshSignInAsync(CancellationToken ct)
    {
        if (!_signInStale) return;
        _signInStale = false;
        bool? signedIn = await _io.SignedIn(ct);
        bool lineUsed = signedIn == true && _signInLine != null;
        if (lineUsed) _signInLine = null;
        if (signedIn == _signedIn && !lineUsed) return;
        _signedIn = signedIn;
        if (Model != null) Push();
    }

    /// <summary>The sign-in button pressed: the flow runs (it speaks as it goes), and the line it returns stays on the page.</summary>
    private async Task PressExtraAsync(string? key, CancellationToken ct)
    {
        if (key != SignInKey) return;
        string line = await _io.SignIn(ct);
        _signInLine = line;
        _signInStale = true;   // the flow may have found the pilot signed in
        if (Model != null) Push();
    }

    /// <summary>
    /// The sign-in's early line ("Navigraph code: {code}"), put on the page before the code is spoken, so the pilot can read it
    /// character by character while it is said (<see cref="Bbs757NavigraphIo.Show"/>). Called by the sign-in flow the loop awaits
    /// (MainForm wires it), so on the loop's own flow. Never logged.
    /// </summary>
    public void ShowSignInLine(string line)
    {
        _signInLine = line;
        if (Model != null) Push();
    }

    // ------------------------------------------------------------------ pushing

    /// <summary>
    /// Shows <paramref name="text"/> in the page's place, until the next page or status. Internal for the tests.
    /// </summary>
    internal void Status(string text)
    {
        _shown = null;
        // The page is no longer shown, so the next read reads it even when the picture has not changed (an unchanged
        // tablet is never left behind a status) and pushes it even when it reads the same as before.
        _forceRead = true;
        _lastFingerprint = null;
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + StatusMs;
        Push();
    }

    private void SetAlert(string text)
    {
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + AlertMs;
    }

    private void Alert(string text)
    {
        SetAlert(text);
        Push();
    }

    /// <summary>The window's elements now, numbered and kept with the page they show, for the presses made on them
    /// (<see cref="Bbs757EfbPushes"/>).</summary>
    private void Push()
    {
        string? alert = _alert != null && Environment.TickCount64 < _alertUntilMs ? _alert : null;
        var built = _pushes.Push(_shown, alert, Extras());
        Raise(StateTypeElements, built.Data);
    }

    private void Raise(string type, Dictionary<string, string> data)
    {
        var args = new EFBStateUpdateEventArgs { Type = type, Data = data };
        if (_sync == null) StateUpdated?.Invoke(this, args);
        else Post(() => StateUpdated?.Invoke(this, args));
    }

    private void PostUi(Action a)
    {
        if (_sync == null) a();
        else Post(a);
    }

    /// <summary>Posts to the UI thread's context, tolerating it going away (app exit): a WinForms context's post is a BeginInvoke on
    /// its marshaling control, which throws once that handle is destroyed (A380C-6's lesson).</summary>
    private void Post(Action a)
    {
        try { _sync!.Post(_ => a(), null); }
        catch (InvalidOperationException) { }
    }

    private void FreeFrames()
    {
        _pushes.Dispose();
        _lastReadFrame?.Dispose();
        _lastReadFrame = null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1500); } catch { }
        // A loop that never started frees nothing itself; one that did frees its frames as it ends.
        if (_loop == null) FreeFrames();
        _cts.Dispose();
    }
}
