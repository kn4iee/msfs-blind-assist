using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The BlueBird 757's EFB tablet, pressed by scripted clicks over the MSFS Coherent GT remote
/// debugger (127.0.0.1:19999). The tablet (the WASM gauge <c>efb_l</c>) lives in the cockpit view
/// whose title holds "BBSNavigraphMap"; <c>Resources/coherent-bbs757-efb-agent.js</c> is installed
/// there (window.__MSFSBA_BBS757EFB) and clicks through the tablet's own mouse handler. Positions
/// and sequences are <see cref="Bbs757EfbLayout"/>'s.
///
/// Shaped like <see cref="CoherentBbs757TabletClient"/>, without its screen polling: the view is
/// resolved BY TITLE on every (re)connect (its VCockpit number changes on each aircraft load); ONE
/// client per view ([DBG-9]): MainForm owns a single lazily created instance, and the client CLAIMS
/// the view (<see cref="CoherentViewOwnership"/>) from Start to Stop, reconnect gaps included. A
/// still-open socket whose agent went missing gets the agent re-installed on it; any other socket
/// is aborted and disposed before a new one connects ([A380C-2]). An install the page answers
/// NOT_READY (an aircraft reload: the view's scripts or the tablet gauge not there yet) is never
/// taken as installed: it is retried on the same socket, then the view is resolved afresh.
///
/// The loop only keeps the link: it connects, installs, and while the EFB window is shown checks
/// the agent every <see cref="KeepAliveMs"/> ms so the window's status stays true; hidden, it sends
/// nothing. <see cref="RunAsync"/> uses the loop's socket and never connects itself, so it cannot
/// race the loop's connection setup ([A380C-3]); it wakes the loop and waits a few seconds for the
/// agent when the link is down. Sequences run one at a time, in the page.
/// Restart is not supported: dispose and create a new instance.
/// </summary>
public sealed class CoherentBbs757EfbClient : IDisposable
{
    private const string DebuggerBase = "http://127.0.0.1:19999";
    public const string ViewNeedle = "BBSNavigraphMap";
    private const string AgentFile = "coherent-bbs757-efb-agent.js";
    internal const string InstallMarker = "MSFSBA_BBS757_EFB_INSTALLED";
    private const string NoAgent = "no-agent";

    private const int KeepAliveMs = 3000;
    private const int IdleIntervalMs = 5000;
    private const int ReconnectDelayMs = 2000;
    private const int EvalTimeoutMs = 5000;
    private const int ConnectTimeoutMs = 4000;
    // Consecutive evals answered by nothing before the socket is presumed dead (a half-open
    // socket never sends a Close frame).
    private const int DeadSocketEvalFailures = 3;
    // Installs answered without the marker before the view is resolved again from the page list.
    private const int InstallRetriesPerSocket = 5;
    // How long a run waits for the loop to have the agent installed.
    private const int AgentWaitMs = 6000;
    // How long past its planned length a run may still be reported running before it is given up.
    private const int RunSlackMs = 5000;
    private const int StatusPollMs = 250;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    // One sequence at a time: two would interleave their clicks on the tablet.
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly WakeableDelay _wake = new();
    private readonly Func<string> _readAgent;

    private CancellationTokenSource? _cts;
    // _cts.Token captured at Start: reading .Token off a disposed source throws, and a UI-thread
    // run can still arrive during teardown.
    private CancellationToken _loopToken = CancellationToken.None;
    private volatile ClientWebSocket? _ws;
    private string _agentJs = "";
    private int _msgId;
    private volatile bool _agentInstalled;
    private volatile bool _active;
    private int _emptyEvalStreak;
    private int _installFailures;
    private IDisposable? _viewClaim;
    private bool _disposed;

    public CoherentBbs757EfbClient()
        : this(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", AgentFile)))
    {
    }

    /// <param name="readAgent">Reads the page agent's source (the Resources file by default).</param>
    internal CoherentBbs757EfbClient(Func<string> readAgent)
    {
        _readAgent = readAgent;
    }

    /// <summary>True while the socket is open and the agent installed: a sequence can be sent.</summary>
    public bool IsReady
    {
        get
        {
            var ws = _ws;
            return ws != null && ws.State == WebSocketState.Open && _agentInstalled;
        }
    }

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
        // Without its agent the client cannot click, so it never opens a socket and claims nothing.
        if (string.IsNullOrEmpty(_agentJs)) return;

        _viewClaim = CoherentViewOwnership.Claim(ViewNeedle, onOneShotDone: _wake.Wake);
        _ = Task.Run(() => RunLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        var ws = _ws;
        _ws = null;
        CloseSocket(ws);
        _agentInstalled = false;
        _viewClaim?.Dispose();
        _viewClaim = null;
    }

    /// <summary>Check the link every few seconds while the EFB window is shown; send nothing while it is hidden.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active) _wake.Wake();
    }

    /// <summary>
    /// Runs one click sequence on the tablet and waits until it has finished (every click made and
    /// its delay after it passed). False when the view or its agent cannot be reached within a few
    /// seconds (<see cref="IsReady"/> is then false too), or the run did not complete (the page
    /// went away, a click could not be made, another run was still going in the page). A sequence
    /// whose start was sent but not answered is never sent again: it may have run.
    /// </summary>
    public async Task<bool> RunAsync(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq, CancellationToken ct)
    {
        // Not started, or started without its agent (no loop runs then): nothing could ever click.
        if (seq.Count == 0 || _viewClaim == null || _disposed) return false;
        string json = Bbs757EfbLayout.ToJson(seq);
        int plannedMs = Bbs757EfbLayout.TotalMs(seq);

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _loopToken);
            var token = linked.Token;
            await _runLock.WaitAsync(token);
            try
            {
                // A missing agent (the page re-evaluated, or the tablet gauge went) clicked nothing:
                // it is installed again and the run tried once more.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!await WaitForAgentAsync(TimeSpan.FromMilliseconds(AgentWaitMs), token)) return false;

                    string raw = await EvalAsync(
                        $"window.__MSFSBA_BBS757EFB ? JSON.stringify(__MSFSBA_BBS757EFB.clickSeq({JsonSerializer.Serialize(json)})) : '{NoAgent}'",
                        token);
                    if (raw.Length == 0)
                    {
                        NoteUnansweredEval();
                        Log.Debug("BBS757", "EFB sequence: no answer; not sent again.");
                        return false;
                    }
                    if (raw == NoAgent)
                    {
                        AgentUnusable();
                        continue;
                    }

                    var start = Parse(raw);
                    if (start == null) return false;
                    if (!start.ok)
                    {
                        Log.Debug("BBS757", $"EFB sequence refused: {start.error}");
                        if (start.error != null && start.error.StartsWith("no-canvas", StringComparison.Ordinal))
                        {
                            AgentUnusable();
                            continue;
                        }
                        return false;
                    }
                    if (start.error != null)
                    {
                        Log.Debug("BBS757", $"EFB sequence stopped at its first click: {start.error}");
                        return false;
                    }
                    return await WaitForRunAsync(start.id, plannedMs, token);
                }
                return false;
            }
            finally
            {
                _runLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // The window or the client is going away: stop the tablet's run, best effort.
            if (!_disposed && IsReady)
                _ = EvalAsync("window.__MSFSBA_BBS757EFB ? __MSFSBA_BBS757EFB.cancel() : ''", CancellationToken.None);
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Waits out a started run, then asks the agent how far it got.</summary>
    private async Task<bool> WaitForRunAsync(int id, int plannedMs, CancellationToken ct)
    {
        await Task.Delay(plannedMs + StatusPollMs, ct);
        var until = DateTime.UtcNow.AddMilliseconds(RunSlackMs);
        while (true)
        {
            string raw = await EvalAsync(
                $"window.__MSFSBA_BBS757EFB ? JSON.stringify(__MSFSBA_BBS757EFB.status({id})) : '{NoAgent}'", ct);
            if (raw.Length == 0) { NoteUnansweredEval(); return false; }
            if (raw == NoAgent) { AgentUnusable(); return false; }   // the page went away mid-run

            var status = Parse(raw);
            if (status == null || !status.ok) return false;
            if (!status.running)
            {
                if (status.error != null) Log.Debug("BBS757", $"EFB sequence stopped after {status.done} of {status.n}: {status.error}");
                return status.error == null && status.done == status.n;
            }
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(StatusPollMs, ct);
        }
    }

    private static RunAnswer? Parse(string raw)
    {
        try { return JsonSerializer.Deserialize<RunAnswer>(raw); }
        catch (JsonException) { return null; }
    }

    /// <summary>Waits until the loop has the agent installed on an open socket, waking it meanwhile.</summary>
    private async Task<bool> WaitForAgentAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (!ct.IsCancellationRequested)
        {
            if (IsReady) return true;
            if (DateTime.UtcNow >= until) return false;
            _wake.Wake();
            await Task.Delay(100, ct);
        }
        return false;
    }

    /// <summary>The agent stopped being usable: install it again on the loop's next pass.</summary>
    private void AgentUnusable()
    {
        _agentInstalled = false;
        _wake.Wake();
    }

    /// <summary>An eval no one answered: after a few in a row the loop drops the socket.</summary>
    private void NoteUnansweredEval()
    {
        Interlocked.Increment(ref _emptyEvalStreak);
        _wake.Wake();
    }

    // ---- connection loop ------------------------------------------------

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Volatile.Read(ref _emptyEvalStreak) >= DeadSocketEvalFailures)
                {
                    Log.Debug("BBS757", "EFB client: no eval answered, reconnecting.");
                    DropSocket();
                }
                if (!await EnsureConnected(ct))
                {
                    await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct);
                    continue;
                }
                if (_active) await KeepAlive(ct);
                await _wake.WaitAsync(TimeSpan.FromMilliseconds(_active ? KeepAliveMs : IdleIntervalMs), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Debug("BBS757", $"EFB client loop: {ex.Message}");
                DropSocket();
                try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct); } catch { break; }
            }
        }
    }

    /// <summary>While the window is shown: is the agent still there, and can it still click?</summary>
    private async Task KeepAlive(CancellationToken ct)
    {
        string raw = await EvalAsync($"window.__MSFSBA_BBS757EFB ? JSON.stringify(__MSFSBA_BBS757EFB.ping()) : '{NoAgent}'", ct);
        if (raw.Length == 0) { Interlocked.Increment(ref _emptyEvalStreak); return; }
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
        if (raw == NoAgent)
        {
            Log.Debug("BBS757", "EFB agent missing, re-installing on the open socket.");
            AgentUnusable();
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.False)
            {
                // The tablet gauge went (a reload in progress): the re-install answers NOT_READY
                // until it is back.
                Log.Debug("BBS757", "EFB agent cannot click now, re-installing.");
                AgentUnusable();
            }
        }
        catch (JsonException) { }
    }

    /// <summary>Abort AND dispose: Abort alone leaks the socket's native handle on every reconnect.</summary>
    private static void CloseSocket(ClientWebSocket? ws)
    {
        if (ws == null) return;
        try { ws.Abort(); } catch { }
        try { ws.Dispose(); } catch { }
    }

    /// <summary>The one teardown path: close the socket and fail the evals waiting on it.</summary>
    private void DropSocket()
    {
        var ws = _ws;
        _ws = null;
        _agentInstalled = false;
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
        _installFailures = 0;
        CloseSocket(ws);
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
    }

    /// <summary>
    /// An install the page answered without the marker (its scripts or the tablet gauge not ready,
    /// after an aircraft reload): the socket is kept and the install tried again on it next pass,
    /// until <see cref="InstallRetriesPerSocket"/> answers, after which the view is resolved afresh.
    /// </summary>
    private bool KeepSocketAfterUnreadyInstall(string answer)
    {
        Log.Debug("BBS757", $"EFB agent install answered: {Shorten(answer)}");
        return ++_installFailures < InstallRetriesPerSocket;
    }

    private void AgentInstalled()
    {
        _agentInstalled = true;
        _installFailures = 0;
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
    }

    private async Task<bool> EnsureConnected(CancellationToken ct)
    {
        if (_disposed) return false;
        var current = _ws;
        bool open = current != null && current.State == WebSocketState.Open;
        if (open && _agentInstalled) return true;

        // Socket still open but the agent went missing (the page re-evaluated) or cannot click,
        // or its install was not ready: re-install on the SAME socket rather than reconnecting. A
        // page that answers without the marker keeps the socket for a few tries; one that does not
        // answer at all is torn down.
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
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
        _ = Task.Run(() => ReceiveLoop(ws, ct));

        string install = await EvalAsync(_agentJs, ct);
        if (install.IndexOf(InstallMarker, StringComparison.Ordinal) < 0)
        {
            // Not ready (kept: the next pass re-installs on this socket) or no answer.
            if (install.Length > 0) KeepSocketAfterUnreadyInstall(install);
            return false;
        }
        Log.Info("BBS757", $"EFB agent installed on view '{ViewNeedle}' (page {pageId.Value}).");
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
                    if (int.TryParse(idEl.GetString(), System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The debugger is not up, or the 757 is not loaded: routine.
            Log.Debug("BBS757", $"EFB ResolvePageId: {ex.Message}");
        }
        return null;
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
        var buf = new byte[65536];
        // Accumulate the bytes and decode once at EndOfMessage: decoding each read separately
        // corrupts a multibyte character split across reads.
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
        catch (Exception ex) { Log.Debug("BBS757", $"EFB client receive: {ex.Message}"); }
        finally
        {
            // This socket is gone: the loop reconnects on its next pass, which this wakes.
            if (ReferenceEquals(_ws, ws))
            {
                _agentInstalled = false;
                _wake.Wake();
            }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        // _sendLock and _runLock are deliberately not disposed: the loop and a run are not joined
        // and may be waiting on them; Stop() cancelled the token, which releases the waiters.
    }

    private sealed class RunAnswer
    {
        public bool ok { get; set; }
        public int id { get; set; }
        public int n { get; set; }
        public int done { get; set; }
        public bool running { get; set; }
        public string? error { get; set; }
    }
}
