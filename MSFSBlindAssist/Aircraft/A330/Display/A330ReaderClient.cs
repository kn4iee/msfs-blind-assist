using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A330.Display;

/// The app's side of the display reader: starts the program through <c>start</c>, keeps it running, and holds the frames it sends.
/// Lines and exits arrive on the transport's thread, <see cref="Tick"/> on a pool thread, everything else from the UI thread:
/// one lock guards all state, and FrameChanged and StateChanged are raised after it is released (consumers marshal).
/// The client never touches SimConnect, a control or the announcer. <c>nowTicks</c> is milliseconds.
internal sealed class A330ReaderClient : IA330Displays, IDisposable
{
    private const int HelloTimeoutMs = 5_000;
    private const int RestartDelayMs = 5_000;
    private const int RestartWindowMs = 300_000;
    private const int MaxExitsInWindow = 4;      // the fourth exit within the window is Failed
    private const int TickIntervalMs = 250;
    private const int StopDisposeDelayMs = 1_000;
    private const int DefaultRefreshMs = 1_000;

    private readonly Func<IA330ReaderTransport?> _start;
    private readonly Func<long> _nowTicks;
    private readonly bool _runTimer;
    private readonly object _gate = new();

    private A330ReaderState _state = A330ReaderState.Stopped;
    private IA330ReaderTransport? _transport;
    private bool _started;
    private bool _helloReceived;
    private long _helloDeadline;
    private long _restartAt = -1;
    private readonly List<long> _exits = new();
    private System.Threading.Timer? _timer;
    private bool _droppedLogged;

    private readonly Dictionary<A330Gauge, List<(object Owner, int Refresh)>> _wanted = new();
    private readonly Dictionary<A330Gauge, int> _sent = new();
    private readonly Dictionary<A330Gauge, int> _lastRefresh = new();
    private readonly Dictionary<A330Gauge, A330Frame> _newest = new();
    private readonly Dictionary<A330Gauge, A330Frame> _latest = new();
    private string? _padCaptain;
    private string? _padFirstOfficer;

    public event Action<A330Gauge>? FrameChanged;
    public event Action<A330ReaderState>? StateChanged;

    public A330ReaderClient(Func<IA330ReaderTransport?> start, Func<long> nowTicks)
        : this(start, nowTicks, runTimer: true)
    {
    }

    /// <paramref name="runTimer"/> false: no timer, the test drives <see cref="Tick"/>.
    internal A330ReaderClient(Func<IA330ReaderTransport?> start, Func<long> nowTicks, bool runTimer)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _nowTicks = nowTicks ?? throw new ArgumentNullException(nameof(nowTicks));
        _runTimer = runTimer;
    }

    public A330ReaderState State
    {
        get { lock (_gate) return _state; }
    }

    public string StatusText => A330ReaderStatus.Text(State);

    private static bool IsTerminal(A330ReaderState s) => s is A330ReaderState.OutOfDate or A330ReaderState.Failed or A330ReaderState.NotInstalled;

    // ---- lifecycle ----

    public void Start()
    {
        var post = new List<Action>();
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _restartAt = -1;
            _exits.Clear();          // a new run: a full restart budget and a fresh first dropped line
            _droppedLogged = false;
            StartProgram(post);
            if (_runTimer && !IsTerminal(_state) && _timer == null)
                _timer = new System.Threading.Timer(_ => TickSafe(), null, TickIntervalMs, TickIntervalMs);
        }
        Flush(post);
    }

    /// A flight reload: tells the reader and forgets every frame; the wanted displays stay wanted.
    public void Reset()
    {
        lock (_gate)
        {
            _transport?.WriteLine(A330ReaderProtocol.Reset());
            ForgetFrames();
        }
    }

    public void Stop()
    {
        var post = new List<Action>();
        IA330ReaderTransport? old;
        lock (_gate)
        {
            old = _transport;
            _transport = null;
            _started = false;
            _restartAt = -1;
            _sent.Clear();
            old?.WriteLine(A330ReaderProtocol.Quit());
            SetState(A330ReaderState.Stopped, post);
            _timer?.Dispose();
            _timer = null;
        }
        DisposeLater(old, StopDisposeDelayMs);
        Flush(post);
    }

    public void Dispose() => Stop();

    // ---- timing ----

    private void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { Log.Channel("a330-displays").Error("the display reader's timer failed: " + ex.Message); }
    }

    /// Checks the hello deadline and a due restart against the clock. A program that has not answered hello in time
    /// is let go (quit, then disposed) and counts as an exit of it: restarted, or Failed at the fourth (<see cref="ProgramEnded"/>).
    internal void Tick()
    {
        var post = new List<Action>();
        IA330ReaderTransport? drop = null;
        lock (_gate)
        {
            if (!_started) return;
            long now = _nowTicks();
            if (_transport != null && !_helloReceived && now >= _helloDeadline)
            {
                drop = _transport;
                drop.WriteLine(A330ReaderProtocol.Quit());
                ProgramEnded(now, post);
            }
            else if (_transport == null && _restartAt >= 0 && now >= _restartAt && !IsTerminal(_state))
            {
                _restartAt = -1;
                StartProgram(post);
            }
        }
        DisposeLater(drop, 0);
        Flush(post);
    }

    /// Starts the program and sends hello and every wanted display. Call under the lock.
    private void StartProgram(List<Action> post)
    {
        IA330ReaderTransport? t;
        try { t = _start(); }
        catch (Exception ex)
        {
            Log.Channel("a330-displays").Error("the display reader did not start: " + ex.Message);
            t = null;
        }
        if (t == null)
        {
            SetState(A330ReaderState.NotInstalled, post);
            return;
        }
        _transport = t;
        _helloReceived = false;
        _helloDeadline = _nowTicks() + HelloTimeoutMs;
        _sent.Clear();
        SetState(A330ReaderState.Starting, post);
        t.LineReceived += line => OnLine(t, line);
        t.Exited += () => OnExited(t);
        t.WriteLine(A330ReaderProtocol.Hello());
        foreach (var gauge in _wanted.Keys.ToList()) SyncWant(gauge);
    }

    // ---- wanting ----

    public void Want(A330Gauge gauge, object owner, int refreshMs)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            if (!_wanted.TryGetValue(gauge, out var owners)) _wanted[gauge] = owners = new();
            int i = owners.FindIndex(o => ReferenceEquals(o.Owner, owner));
            if (i >= 0) owners[i] = (owner, refreshMs);
            else owners.Add((owner, refreshMs));
            SyncWant(gauge);
        }
    }

    public void Release(A330Gauge gauge, object owner)
    {
        lock (_gate)
        {
            if (!_wanted.TryGetValue(gauge, out var owners)) return;
            if (owners.RemoveAll(o => ReferenceEquals(o.Owner, owner)) == 0) return;
            if (owners.Count == 0) _wanted.Remove(gauge);
            SyncWant(gauge);
        }
    }

    /// Sends what the reader needs to hear about this gauge: the smallest refresh, or a release. Call under the lock.
    private void SyncWant(A330Gauge gauge)
    {
        int? smallest = _wanted.TryGetValue(gauge, out var owners) && owners.Count > 0 ? owners.Min(o => o.Refresh) : null;
        if (smallest != null) _lastRefresh[gauge] = smallest.Value;
        if (_transport == null) return;
        if (smallest == null)
        {
            if (_sent.Remove(gauge)) _transport.WriteLine(A330ReaderProtocol.Release(gauge));
        }
        else if (!_sent.TryGetValue(gauge, out int sent) || sent != smallest.Value)
        {
            _sent[gauge] = smallest.Value;
            _transport.WriteLine(A330ReaderProtocol.Want(gauge, smallest.Value));
        }
    }

    // ---- frames ----

    public A330Frame? Latest(A330Gauge gauge)
    {
        lock (_gate)
        {
            if (!_latest.TryGetValue(gauge, out var frame)) return null;
            int refresh = _wanted.TryGetValue(gauge, out var owners) && owners.Count > 0 ? owners.Min(o => o.Refresh)
                : _lastRefresh.TryGetValue(gauge, out int last) ? last : DefaultRefreshMs;
            return _nowTicks() - frame.ReadTicks > 3L * refresh ? null : frame;
        }
    }

    public A330Frame? Newest(A330Gauge gauge)
    {
        lock (_gate) return _newest.TryGetValue(gauge, out var frame) ? frame : null;
    }

    public string? McduScratchpad(bool firstOfficer)
    {
        lock (_gate) return firstOfficer ? _padFirstOfficer : _padCaptain;
    }

    private void ForgetFrames()
    {
        _newest.Clear();
        _latest.Clear();
        _padCaptain = null;
        _padFirstOfficer = null;
    }

    // ---- the reader's lines ----

    private void OnLine(IA330ReaderTransport source, string line)
    {
        var post = new List<Action>();
        IA330ReaderTransport? drop = null;
        try
        {
            var parsed = A330ReaderProtocol.Parse(line, _nowTicks());
            lock (_gate)
            {
                if (!ReferenceEquals(source, _transport) || IsTerminal(_state)) return;
                if (parsed == null)
                {
                    if (!_droppedLogged)
                    {
                        _droppedLogged = true;
                        Log.Channel("a330-displays").Warn("a line from the display reader was dropped (later ones are not logged): " + DroppedLineNote(line));
                    }
                    return;
                }
                if (parsed.HelloVersion is int version)
                {
                    if (_helloReceived) return;
                    if (version != A330ReaderProtocol.Version)
                    {
                        drop = _transport;
                        _transport = null;
                        drop!.WriteLine(A330ReaderProtocol.Quit());
                        SetState(A330ReaderState.OutOfDate, post);
                    }
                    else _helloReceived = true;
                }
                // Until the hello, nothing else is taken: a valid line, only early, so neither used nor logged as dropped.
                else if (!_helloReceived) return;
                else if (parsed.State is A330ReaderState state) SetState(state, post);
                else if (parsed.Frame is A330Frame frame) StoreFrame(frame, post);
                else if (parsed.Scratchpad is { } pad)
                {
                    if (pad.Gauge == A330Gauge.McduCaptain) _padCaptain = pad.Text;
                    else if (pad.Gauge == A330Gauge.McduFirstOfficer) _padFirstOfficer = pad.Text;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Channel("a330-displays").Error("a line from the display reader failed: " + ex.Message);
            return;
        }
        DisposeLater(drop, 0);
        Flush(post);
    }

    /// What the log says of a line the protocol refused: its length and, for a JSON object, its first property's name,
    /// never its content (a scratchpad or frame line can hold what the pilot typed, which is never logged).
    internal static string DroppedLineNote(string line)
    {
        line ??= "";
        string what;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(line));
            if (!reader.Read()) what = "not JSON";
            else if (reader.TokenType != JsonTokenType.StartObject) what = "not an object";
            else if (!reader.Read()) what = "not JSON";
            else if (reader.TokenType == JsonTokenType.PropertyName) what = "first property \"" + reader.GetString() + "\"";
            else if (reader.TokenType == JsonTokenType.EndObject) what = "an empty object";
            else what = "not JSON";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            what = "not JSON";
        }
        return line.Length + " characters, " + what;
    }

    /// Call under the lock.
    private void StoreFrame(A330Frame frame, List<Action> post)
    {
        _newest[frame.Gauge] = frame;
        if (!frame.Complete) return;
        bool changed = !_latest.TryGetValue(frame.Gauge, out var before) || !SameTexts(before.Texts, frame.Texts);
        _latest[frame.Gauge] = frame;
        if (changed)
        {
            var gauge = frame.Gauge;
            post.Add(() => FrameChanged?.Invoke(gauge));
        }
    }

    /// True when two frames' texts are the same in everything they carry: what decides FrameChanged (the replay source
    /// in the tests uses it too).
    internal static bool SameTexts(IReadOnlyList<A330Text> a, IReadOnlyList<A330Text> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Order != y.Order || x.Text != y.Text || x.X != y.X || x.Baseline != y.Baseline || x.Font != y.Font
                || x.Size != y.Size || x.Colour != y.Colour || !x.CharX.SequenceEqual(y.CharX)) return false;
        }
        return true;
    }

    // ---- the program ending ----

    private void OnExited(IA330ReaderTransport source)
    {
        var post = new List<Action>();
        try
        {
            lock (_gate)
            {
                if (!ReferenceEquals(source, _transport) || !_started) return;
                ProgramEnded(_nowTicks(), post);
            }
        }
        catch (Exception ex)
        {
            Log.Channel("a330-displays").Error("the display reader's exit failed: " + ex.Message);
            return;
        }
        DisposeLater(source, 0);
        Flush(post);
    }

    /// The program is gone (it exited, or it missed its hello): every frame is forgotten, and it is started again after
    /// 5 s, unless this is the fourth such end within five minutes, which is Failed. Call under the lock.
    private void ProgramEnded(long now, List<Action> post)
    {
        _transport = null;
        ForgetFrames();
        _exits.RemoveAll(e => now - e > RestartWindowMs);
        _exits.Add(now);
        if (_exits.Count >= MaxExitsInWindow)
        {
            SetState(A330ReaderState.Failed, post);
            _restartAt = -1;
        }
        else
        {
            SetState(A330ReaderState.Starting, post);
            _restartAt = now + RestartDelayMs;
        }
    }

    // ---- helpers ----

    /// Call under the lock.
    private void SetState(A330ReaderState state, List<Action> post)
    {
        if (_state == state) return;
        _state = state;
        post.Add(() => StateChanged?.Invoke(state));
    }

    private static void Flush(List<Action> post)
    {
        foreach (var action in post)
        {
            try { action(); }
            catch (Exception ex) { Log.Channel("a330-displays").Error("a display reader event handler threw: " + ex.Message); }
        }
    }

    /// Disposes now (always from the transport's or a pool thread, never the UI thread) or, with a delay, without blocking the caller.
    private static void DisposeLater(IA330ReaderTransport? transport, int delayMs)
    {
        if (transport == null) return;
        if (delayMs <= 0)
        {
            try { transport.Dispose(); }
            catch (Exception) { }
            return;
        }
        Task.Delay(delayMs).ContinueWith(_ =>
        {
            try { transport.Dispose(); }
            catch (Exception) { }
        }, TaskScheduler.Default);
    }
}
