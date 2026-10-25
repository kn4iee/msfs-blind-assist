using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The BlueBird 757's hidden helper view ("VCockpitNN - BBSTablet") over the MSFS Coherent GT
/// remote debugger (127.0.0.1:19999): both CDU screens, CDU key presses and the Navigraph device
/// code. Shaped like <see cref="CoherentHS787CduClient"/>: the view is resolved BY TITLE on every
/// (re)connect (its VCockpit number changes on each aircraft load), and
/// <c>Resources/coherent-bbs757-tablet-agent.js</c> is installed into it (window.__MSFSBA_BBS757).
///
/// ONE client per view ([DBG-9]): MainForm owns a single lazily created instance that the CDU
/// window and the EFB window share; the windows subscribe and unsubscribe, never dispose it. The
/// client CLAIMS its view (<see cref="CoherentViewOwnership"/>) from Start to Stop, reconnect gaps
/// included, as <see cref="CoherentA32nxMcduClient"/> does. A still-open socket whose agent went
/// missing gets the agent re-installed on it; any other socket is aborted and disposed before a
/// new one connects ([A380C-2]). An agent is only ever taken as installed while it can hear the
/// Comm Bus: an install the page answers NOT_READY (an aircraft reload, the view's scripts not run
/// yet) is retried on the same socket, and a scrape that shows no listener, or one still not ready
/// after <see cref="ListenerReadyTimeoutMs"/>, installs it again (<see cref="NeedsReinstall"/>).
/// Whenever the link is lost the queued keys are dropped, never sent later to a page nobody has
/// checked (<see cref="KeysDropped"/>). On-demand calls (<see cref="GetNavigraphAsync"/>, key presses)
/// only ever use the loop's socket and never connect themselves, so they cannot race the loop's
/// connection setup ([A380C-3]).
///
/// Screens: the agent keeps each CDU's last Comm Bus frame (they arrive only on change); the loop
/// scrapes the ACTIVE CDU only, every <see cref="PollIntervalMs"/> ms while the CDU window is
/// shown and every <see cref="IdleIntervalMs"/> ms while it is hidden, and raises
/// <see cref="ScreenUpdated"/> when that CDU's frame changed.
///
/// Keys: one queue for both CDUs, so presses never overlap. The agent holds each key 250 ms
/// (a CLR held about a second clears the whole scratchpad, so the hold is the agent's, fixed);
/// the next key goes out <see cref="KeyGapMs"/> ms after the release.
/// Restart is not supported: dispose and create a new instance.
/// </summary>
public sealed class CoherentBbs757TabletClient : IDisposable
{
    private const string DebuggerBase = "http://127.0.0.1:19999";
    public const string ViewNeedle = "BBSTablet";
    private const string AgentFile = "coherent-bbs757-tablet-agent.js";
    private const string InstallMarker = "MSFSBA_BBS757_TABLET_INSTALLED";
    private const string NoAgent = "no-agent";

    private const int PollIntervalMs = 350;
    private const int IdleIntervalMs = 1000;
    private const int ReconnectDelayMs = 2000;
    private const int EvalTimeoutMs = 5000;
    private const int ConnectTimeoutMs = 4000;
    // Consecutive evals answered by nothing before the socket is presumed dead (a half-open
    // socket never sends a Close frame).
    private const int DeadSocketEvalFailures = 3;
    // A scrape answered within this long keeps the client readable.
    private const int ReadableWindowMs = 5000;
    // Installs answered with something other than the marker (the view's scripts have not run
    // yet) before the view is resolved again from the page list.
    private const int InstallRetriesPerSocket = 5;

    /// <summary>
    /// How long an installed agent's Comm Bus listener may stay not ready before it is installed
    /// again (the agent itself re-registers after 4 s, so a re-install after this always does).
    /// </summary>
    internal const int ListenerReadyTimeoutMs = 5000;

    /// <summary>How long the agent holds a key down (its HOLD_MS).</summary>
    public const int KeyHoldMs = 250;

    /// <summary>The pause after a key's release before the next key (docs/b757/evidence/cdu.md).</summary>
    public const int KeyGapMs = 400;

    /// <summary>One key's whole cycle in the queue: hold, release, gap.</summary>
    public const int KeyCycleMs = KeyHoldMs + KeyGapMs;

    /// <summary>A CDU's screen changed (1 captain, 2 first officer); posted to the UI context.</summary>
    public event Action<int, Bbs757CduScreen>? ScreenUpdated;

    /// <summary>
    /// The agent no longer holds a screen for this CDU, though one was delivered (the aircraft was
    /// reloaded and the agent installed afresh); posted to the UI context. Its screen arrives again
    /// on its next change.
    /// </summary>
    public event Action<int>? ScreenLost;

    /// <summary>
    /// Queued keys were thrown away because the link to the tablet view was lost (the socket
    /// dropped, or the agent was gone or deaf): a key meant for the page the pilot was reading is
    /// never sent later to a page nobody has checked. Raised once per outage with the number of
    /// keys dropped; posted to the UI context.
    /// </summary>
    public event Action<int>? KeysDropped;

    private readonly SynchronizationContext? _syncContext;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly WakeableDelay _wake = new();
    // Read by the key pump and drained by DropQueuedKeys, so not single-reader.
    private readonly Channel<(int Cdu, string Key)> _keys = Channel.CreateUnbounded<(int, string)>(
        new UnboundedChannelOptions { SingleReader = false });
    private readonly Func<string> _readAgent;

    private CancellationTokenSource? _cts;
    // _cts.Token captured at Start: reading .Token off a disposed source throws, and a UI-thread
    // call (a key, the Navigraph read) can still arrive during teardown.
    private CancellationToken _loopToken = CancellationToken.None;
    private volatile ClientWebSocket? _ws;
    private string _agentJs = "";
    private int _msgId;
    private volatile bool _agentInstalled;
    private volatile bool _active;
    private volatile int _activeCdu = 1;
    private int _emptyEvalStreak;
    private int _installFailures;
    private DateTime? _notReadySinceUtc;
    private int _pendingKeys;
    // Set when KeysDropped has been raised, cleared when the agent is installed again.
    private volatile bool _keysDroppedRaised;
    private long _lastGoodScrapeTicks;
    // Per CDU (index 1 and 2): the agent tag of the frame last delivered, whether the agent
    // holds a frame, and whether one was ever delivered.
    private readonly string[] _tags = { "", "", "" };
    private readonly bool[] _hasFrame = new bool[3];
    private readonly bool[] _delivered = new bool[3];
    private IDisposable? _viewClaim;
    private bool _disposed;

    public CoherentBbs757TabletClient()
        : this(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", AgentFile)))
    {
    }

    /// <param name="readAgent">Reads the page agent's source (the Resources file by default).</param>
    internal CoherentBbs757TabletClient(Func<string> readAgent)
    {
        _readAgent = readAgent;
        _syncContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// The CDU the loop scrapes, 1 (captain) or 2 (first officer). Setting it asks for that CDU's
    /// current screen at once.
    /// </summary>
    public int ActiveCdu
    {
        get => _activeCdu;
        set
        {
            int cdu = value == 2 ? 2 : 1;
            _activeCdu = cdu;
            _tags[cdu] = "";
            _wake.Wake();
        }
    }

    /// <summary>
    /// True while the socket is open, the agent installed and a scrape answered in the last few
    /// seconds: keys and reads can reach the tablet view.
    /// </summary>
    public bool IsReadable
    {
        get
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open || !_agentInstalled) return false;
            long ticks = Interlocked.Read(ref _lastGoodScrapeTicks);
            return ticks != 0 && (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds < ReadableWindowMs;
        }
    }

    /// <summary>Whether the agent holds a screen for this CDU (as of the last scrape of it).</summary>
    public bool HasFrame(int cdu) => _hasFrame[cdu == 2 ? 2 : 1];

    /// <summary>Keys queued or still in their cycle.</summary>
    public int PendingKeyCount => Volatile.Read(ref _pendingKeys);

    public void Start()
    {
        if (_cts != null) return;   // restart is not a supported lifecycle: dispose and recreate
        _cts = new CancellationTokenSource();
        _loopToken = _cts.Token;
        try
        {
            _agentJs = _readAgent();
            if (string.IsNullOrEmpty(_agentJs)) Log.Warn("BBS757", $"{AgentFile} is empty.");
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"Could not load {AgentFile}: {ex.Message}");
        }
        // Without its agent the client can neither read nor press, so it never opens a socket
        // and claims nothing.
        if (string.IsNullOrEmpty(_agentJs)) return;

        _viewClaim = CoherentViewOwnership.Claim(ViewNeedle, onOneShotDone: _wake.Wake);
        _ = Task.Run(() => RunLoop(_cts.Token));
        _ = Task.Run(() => KeyPump(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _keys.Writer.TryComplete();
        var ws = _ws;
        _ws = null;
        CloseSocket(ws);
        _agentInstalled = false;
        _viewClaim?.Dispose();
        _viewClaim = null;
    }

    /// <summary>Scrape at the visible rate while the CDU window is shown, at the idle rate while hidden.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            _tags[_activeCdu] = "";
            _wake.Wake();
        }
    }

    /// <summary>
    /// Queues one key for a CDU (<c>L1</c>, <c>LEGS</c>, <c>KEYA</c>, <c>KEYCLR</c> …, the cockpit's
    /// own names, <see cref="Bbs757CduKeys"/>). Keys go out one at a time, <see cref="KeyCycleMs"/>
    /// apart. When the link is lost every queued key is dropped (<see cref="KeysDropped"/>); the
    /// window refuses aloud before queueing when <see cref="IsReadable"/> is false.
    /// </summary>
    public void PressKey(int cdu, string key)
    {
        if ((cdu != 1 && cdu != 2) || !Bbs757CduKeys.IsKey(key))
        {
            Log.Debug("BBS757", $"CDU key {cdu}/{key}: not a key, not sent.");
            return;
        }
        Interlocked.Increment(ref _pendingKeys);
        if (!_keys.Writer.TryWrite((cdu, key))) Interlocked.Decrement(ref _pendingKeys);
    }

    /// <summary>
    /// When a Navigraph reading (<see cref="GetNavigraphAsync"/>, answered by the tablet) last found
    /// the pilot signed out, UTC; null if none has. BlueBird looks for a newer AIRAC cycle only when
    /// the flight loads, so a signed-out reading taken after the current flight loaded means that
    /// check ran signed out (<see cref="Bbs757EfbLayout.SignedOutDuringLoad"/>).
    /// </summary>
    public DateTime? LastSignedOutUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastSignedOutTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    private long _lastSignedOutTicks;

    /// <summary>
    /// The Navigraph sign-in state BlueBird's tablet holds: signed in or not, and the pending
    /// device sign-in's user code and address. Never a token. (false, null, null) when the view
    /// or its agent cannot be read within a few seconds; callers that must tell that apart check
    /// <see cref="IsReadable"/> first. A signed-out answer is time-stamped (<see cref="LastSignedOutUtc"/>).
    /// </summary>
    public async Task<(bool SignedIn, string? UserCode, string? Uri)> GetNavigraphAsync()
    {
        try
        {
            if (!await WaitForAgentAsync(TimeSpan.FromSeconds(6))) return (false, null, null);
            string raw = await EvalAsync("window.__MSFSBA_BBS757 ? JSON.stringify(__MSFSBA_BBS757.navigraph()) : ''", _loopToken);
            if (string.IsNullOrEmpty(raw)) return (false, null, null);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return (false, null, null);
            bool signedIn = root.TryGetProperty("signedIn", out var s) && s.ValueKind == JsonValueKind.True;
            if (!signedIn) Interlocked.Exchange(ref _lastSignedOutTicks, DateTime.UtcNow.Ticks);
            string? code = root.TryGetProperty("userCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            string? uri = root.TryGetProperty("uri", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            return (signedIn, code, uri);
        }
        catch (Exception ex) when (ex is JsonException or OperationCanceledException or ObjectDisposedException)
        {
            // The answer itself is never logged: it can hold the sign-in code.
            Log.Debug("BBS757", $"Navigraph read failed: {ex.GetType().Name}");
            return (false, null, null);
        }
    }

    /// <summary>Waits until the loop has the agent installed on an open socket, waking it meanwhile.</summary>
    private async Task<bool> WaitForAgentAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!_loopToken.IsCancellationRequested)
        {
            var ws = _ws;
            if (ws != null && ws.State == WebSocketState.Open && _agentInstalled) return true;
            if (_cts == null || DateTime.UtcNow >= until) return false;
            _wake.Wake();
            await Task.Delay(100, _loopToken);
        }
        return false;
    }

    // ---- key queue --------------------------------------------------

    private async Task KeyPump(CancellationToken ct)
    {
        try
        {
            await foreach (var (cdu, key) in _keys.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var ws = _ws;
                    if (ws == null || ws.State != WebSocketState.Open || !_agentInstalled)
                    {
                        // The link went while this key waited: it and the rest are dropped.
                        DropQueuedKeys(alreadyDropped: 1);
                        continue;
                    }
                    string expr = $"window.__MSFSBA_BBS757 ? __MSFSBA_BBS757.press({cdu},{JsonSerializer.Serialize(key)}) : '{NoAgent}'";
                    string result = await EvalAsync(expr, ct);
                    if (result != "ok")
                        Log.Debug("BBS757", $"CDU {cdu} key {key}: {(result.Length == 0 ? "no answer" : result)}");
                    if (result == NoAgent)
                    {
                        _agentInstalled = false;
                        DropQueuedKeys(alreadyDropped: 1);
                        continue;
                    }
                    // Timed from the answer: the press ran before it, so its release (250 ms
                    // later in the page) is at least KeyGapMs before the next key.
                    await Task.Delay(KeyCycleMs, ct);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingKeys);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("BBS757", $"CDU key pump: {ex.Message}"); }
    }

    /// <summary>
    /// Throws away every queued key (the link to the page they were meant for is gone) and, once
    /// per outage, raises <see cref="KeysDropped"/>. <paramref name="alreadyDropped"/> counts a key
    /// the pump had already taken off the queue; its own count is the pump's to settle.
    /// </summary>
    private void DropQueuedKeys(int alreadyDropped = 0)
    {
        int dropped = alreadyDropped;
        while (_keys.Reader.TryRead(out _))
        {
            dropped++;
            Interlocked.Decrement(ref _pendingKeys);
        }
        if (dropped == 0) return;
        Log.Debug("BBS757", $"Tablet link lost: {dropped} CDU key(s) dropped, not sent.");
        if (_keysDroppedRaised) return;
        _keysDroppedRaised = true;
        PostToUI(() => KeysDropped?.Invoke(dropped));
    }

    /// <summary>
    /// Whether a scrape's answer shows an agent that cannot hear the Comm Bus, so it must be
    /// installed again: no listener at all, or a listener still not ready
    /// <see cref="ListenerReadyTimeoutMs"/> after this client first saw it not ready
    /// (<paramref name="notReadySinceUtc"/>, null while it was ready). Pure.
    /// </summary>
    internal static bool NeedsReinstall(bool listening, bool ready, DateTime? notReadySinceUtc, DateTime nowUtc)
    {
        if (!listening) return true;
        if (ready || notReadySinceUtc == null) return false;
        return (nowUtc - notReadySinceUtc.Value).TotalMilliseconds >= ListenerReadyTimeoutMs;
    }

    /// <summary>The agent stopped being usable: install it again on the next pass, and drop the queued keys.</summary>
    private void AgentUnusable()
    {
        _agentInstalled = false;
        _notReadySinceUtc = null;
        DropQueuedKeys();
    }

    // ---- connection + poll loop -------------------------------------

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await EnsureConnected(ct))
                {
                    await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct);
                    continue;
                }
                await PollOnce(ct);
                if (_emptyEvalStreak >= DeadSocketEvalFailures)
                {
                    Log.Debug("BBS757", "Tablet client: no eval answered, reconnecting.");
                    DropSocket();
                    continue;
                }
                await _wake.WaitAsync(TimeSpan.FromMilliseconds(_active ? PollIntervalMs : IdleIntervalMs), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Debug("BBS757", $"Tablet client loop: {ex.Message}");
                DropSocket();
                try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct); } catch { break; }
            }
        }
    }

    /// <summary>Abort AND dispose: Abort alone leaks the socket's native handle on every reconnect.</summary>
    private static void CloseSocket(ClientWebSocket? ws)
    {
        if (ws == null) return;
        try { ws.Abort(); } catch { }
        try { ws.Dispose(); } catch { }
    }

    /// <summary>
    /// The one teardown path: close the socket, fail the evals waiting on it, drop the queued keys,
    /// re-push on reconnect.
    /// </summary>
    private void DropSocket()
    {
        var ws = _ws;
        _ws = null;
        _agentInstalled = false;
        _emptyEvalStreak = 0;
        _installFailures = 0;
        _notReadySinceUtc = null;
        CloseSocket(ws);
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        _tags[1] = "";
        _tags[2] = "";
        DropQueuedKeys();
    }

    /// <summary>The agent answered with its marker: the link is usable again.</summary>
    private void AgentInstalled()
    {
        _agentInstalled = true;
        _installFailures = 0;
        _notReadySinceUtc = null;
        _keysDroppedRaised = false;
        _tags[1] = "";
        _tags[2] = "";
    }

    /// <summary>
    /// An install the page answered without the marker (its scripts have not run yet, after an
    /// aircraft reload): the socket is kept and the install tried again on it next pass, until
    /// <see cref="InstallRetriesPerSocket"/> answers, after which the view is resolved afresh.
    /// </summary>
    private bool KeepSocketAfterUnreadyInstall(string answer)
    {
        Log.Debug("BBS757", $"Tablet agent install answered: {Shorten(answer)}");
        return ++_installFailures < InstallRetriesPerSocket;
    }

    private async Task<bool> EnsureConnected(CancellationToken ct)
    {
        if (_disposed) return false;
        var current = _ws;
        bool open = current != null && current.State == WebSocketState.Open;
        if (open && _agentInstalled) return true;

        // Socket still open but the agent went missing (the page re-evaluated) or deaf, or its
        // install was not ready or timed out: re-install on the SAME socket rather than
        // reconnecting. A page that answers without the marker keeps the socket for a few tries;
        // one that does not answer at all is torn down.
        if (open)
        {
            string reinstall = await EvalAsync(_agentJs, ct);
            if (reinstall.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0)
            {
                AgentInstalled();
                return true;
            }
            if (reinstall.Length > 0 && KeepSocketAfterUnreadyInstall(reinstall)) return false;
        }

        // ONE inspector socket per view: tear down before reconnecting.
        if (_ws != null) DropSocket();

        // A one-shot eval that began before the claim may still hold the view; it wakes the loop
        // as it ends.
        if (CoherentViewOwnership.OneShotInFlight(ViewNeedle)) return false;

        int? pageId = await ResolvePageId(ct);
        if (pageId == null) return false;

        var ws = new ClientWebSocket();
        var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeoutMs);
            await ws.ConnectAsync(url, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            CloseSocket(ws);   // a connect timeout, not shutdown
            return false;
        }
        catch
        {
            CloseSocket(ws);
            throw;
        }
        _ws = ws;
        // Stop() may have run while connecting; it saw nothing of this socket.
        if (ct.IsCancellationRequested)
        {
            CloseSocket(ws);
            if (ReferenceEquals(_ws, ws)) _ws = null;
            return false;
        }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        _emptyEvalStreak = 0;
        _ = Task.Run(() => ReceiveLoop(ws, ct));

        string install = await EvalAsync(_agentJs, ct);
        if (install.IndexOf(InstallMarker, StringComparison.Ordinal) < 0)
        {
            // Not ready (kept: the next pass re-installs on this socket) or no answer.
            if (install.Length > 0) KeepSocketAfterUnreadyInstall(install);
            return false;
        }
        Log.Info("BBS757", $"Tablet agent installed on view '{ViewNeedle}' (page {pageId.Value}).");
        AgentInstalled();
        return true;
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s.Substring(0, 200);

    private async Task<int?> ResolvePageId(CancellationToken ct)
    {
        try
        {
            string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var view in doc.RootElement.EnumerateArray())
            {
                if (!view.TryGetProperty("title", out var titleEl)) continue;
                string title = titleEl.GetString() ?? "";
                if (title.IndexOf(ViewNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                    && view.TryGetProperty("id", out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                    if (int.TryParse(idEl.GetString(), out var n)) return n;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The debugger is not up, or the 757 is not loaded: routine.
            Log.Debug("BBS757", $"Tablet ResolvePageId: {ex.Message}");
        }
        return null;
    }

    private async Task PollOnce(CancellationToken ct)
    {
        int cdu = _activeCdu;
        string since = _tags[cdu];
        string raw = await EvalAsync(
            $"window.__MSFSBA_BBS757 ? JSON.stringify(__MSFSBA_BBS757.scrape({cdu},{JsonSerializer.Serialize(since)})) : '{NoAgent}'", ct);
        if (string.IsNullOrEmpty(raw)) { _emptyEvalStreak++; return; }
        _emptyEvalStreak = 0;
        if (raw == NoAgent)
        {
            // The page re-evaluated: re-install on this socket on the next pass.
            Log.Debug("BBS757", "Tablet agent missing, re-installing on the open socket.");
            AgentUnusable();
            return;
        }

        ScrapeResult? result;
        try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
        catch (JsonException) { return; }
        if (result == null || !result.ok) return;

        // An agent that cannot hear the Comm Bus would freeze CDU 1 on its seed and never get
        // CDU 2: install it again (the agent registers a new listener then).
        var now = DateTime.UtcNow;
        if (result.ready) _notReadySinceUtc = null;
        else _notReadySinceUtc ??= now;
        if (NeedsReinstall(result.listening, result.ready, _notReadySinceUtc, now))
        {
            Log.Debug("BBS757", $"Tablet agent deaf (listening {result.listening}, ready {result.ready}), re-installing.");
            AgentUnusable();
            return;
        }

        Interlocked.Exchange(ref _lastGoodScrapeTicks, now.Ticks);
        _hasFrame[cdu] = result.hasFrame;

        if (!result.hasFrame)
        {
            _tags[cdu] = result.tag ?? "";
            if (_delivered[cdu])
            {
                _delivered[cdu] = false;
                PostToUI(() => ScreenLost?.Invoke(cdu));
            }
            return;
        }
        if (result.frame == null) return;   // unchanged since the tag we sent

        _tags[cdu] = result.tag ?? "";
        _delivered[cdu] = true;
        var screen = Bbs757CduFrame.Parse(result.frame);
        PostToUI(() => ScreenUpdated?.Invoke(cdu, screen));
    }

    // ---- Runtime.evaluate over the inspector socket -----------------

    private async Task<string> EvalAsync(string expression, CancellationToken ct)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return "";

        int id = Interlocked.Increment(ref _msgId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var msg = JsonSerializer.Serialize(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new { expression, returnByValue = true }
        });

        byte[] bytes = Encoding.UTF8.GetBytes(msg);
        try
        {
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _pending.TryRemove(id, out _);
            return "";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(EvalTimeoutMs);
        using (timeout.Token.Register(() => tcs.TrySetCanceled()))
        {
            try { return ExtractValue(await tcs.Task); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ""; }
            finally { _pending.TryRemove(id, out _); }
        }
    }

    private static string ExtractValue(JsonElement root)
    {
        // {"id":N,"result":{"result":{"type":"string","value":"..."},"wasThrown":false}}
        if (root.TryGetProperty("result", out var outer)
            && outer.TryGetProperty("result", out var inner)
            && inner.TryGetProperty("value", out var val))
            return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
        return "";
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[131072];
        // Accumulate the bytes and decode once at EndOfMessage: decoding each read separately
        // corrupts a multibyte character (the CDU's degree sign) split across reads.
        var ms = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (res.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, res.Count);
                } while (!res.EndOfMessage);
                DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("BBS757", $"Tablet client receive: {ex.Message}"); }
        finally
        {
            // This socket is gone; the loop's next pass sees it closed and reconnects.
            if (ReferenceEquals(_ws, ws)) _agentInstalled = false;
        }
    }

    private void DispatchMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id)
                && _pending.TryGetValue(id, out var tcs))
                tcs.TrySetResult(root.Clone());   // clone: the value must outlive the JsonDocument
        }
        catch (JsonException) { /* malformed frame: ignore */ }
    }

    private void PostToUI(Action action)
    {
        if (_syncContext != null) _syncContext.Post(_ => action(), null);
        else action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        // _sendLock is deliberately not disposed: the loop is not joined and may be waiting on
        // it; Stop() canceled the token, which releases the waiter (as the sibling clients do).
    }

    private sealed class ScrapeResult
    {
        public bool ok { get; set; }
        public int cdu { get; set; }
        public bool listening { get; set; }
        public bool ready { get; set; }
        public bool hasFrame { get; set; }
        public string? tag { get; set; }
        public string? frame { get; set; }
    }
}
