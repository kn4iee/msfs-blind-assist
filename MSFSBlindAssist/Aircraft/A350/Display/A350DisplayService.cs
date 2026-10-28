using System.Collections.Concurrent;
using MSFSBlindAssist.Utils.Logging;
using Wasmtime;

namespace MSFSBlindAssist.Aircraft.A350.Display;

public enum A350DisplayState { Idle, Preparing, Ready, Unavailable }

/// <summary>
/// The A350 display reader for the rest of MSFSBA: one worker thread owns the Wasmtime engine, the
/// memory reader and the draw engine (a Wasmtime store is never shared between threads). Start prepares
/// in the background: the installed module is read and, the first time per A350 version, patched and
/// compiled (spoken: "A350 displays getting ready", then "ready"). A draw request takes one fresh memory
/// copy for all the gauges it names, so they agree with each other; a trapped draw is retried once on a
/// new copy (a torn copy, [A350-2]); the A350 is re-located when its anchors no longer read (a reload).
/// When every draw keeps failing the pilot is told, and told again when they read (<see cref="A350ReadHealth"/>).
/// </summary>
public sealed class A350DisplayService : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private readonly string _localAppData;
    private Engine? _engine;
    private Module? _module;
    private A350DrawEngine? _draw;
    private A350MemoryReader? _reader;
    private A350ModuleFacts? _facts;
    private Func<string, double?> _aVar = _ => null;
    private Func<string, string?> _calcString = _ => null;
    private readonly A350ReadHealth _health = new();
    private bool _disposed;

    /// <summary>A status sentence to speak (getting ready, ready, not available).</summary>
    public event Action<string>? Spoken;

    public A350DisplayState State { get; private set; } = A350DisplayState.Idle;
    public string? Reason { get; private set; }

    /// <summary>The A: vars the draw code asks the sim for ("KOHLSMAN SETTING HG:1|inches of mercury").</summary>
    public Func<string, double?> AVarAnswer
    {
        set { _aVar = value; _work.Add(() => { if (_draw != null) _draw.AVarAnswer = value; }); }
    }

    /// <summary>The strings the draw code asks through calculator code ("(A:NAV IDENT:2, String)").</summary>
    public Func<string, string?> CalcStringAnswer
    {
        set { _calcString = value; _work.Add(() => { if (_draw != null) _draw.CalcStringAnswer = value; }); }
    }

    public A350DisplayService(string? localAppData = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _thread = new Thread(Loop) { IsBackground = true, Name = "A350 displays" };
    }

    private void Loop()
    {
        foreach (var job in _work.GetConsumingEnumerable())
        {
            try { job(); }
            catch (Exception ex) { Log.Error("A350Display", "worker: " + ex); }
        }
    }

    /// <summary>Starts preparing in the background; idempotent.</summary>
    public void Start()
    {
        if (_thread.IsAlive || _disposed) return;
        _thread.Start();
        State = A350DisplayState.Preparing;
        _work.Add(Prepare);
    }

    private void Prepare()
    {
        try
        {
            var path = A350ModuleCache.FindInstalledModule() ?? throw new A350ModuleException("the A350 is not installed");
            var mod = A350WasmModule.Parse(File.ReadAllBytes(path));
            _facts = A350ModuleFacts.From(mod);
            var ui = A350ModulePatcher.UiDrawClassesOf(mod);
            bool cached = File.Exists(A350ModuleCache.CachePath(_localAppData, A350ModuleCache.Sha256Of(path)));
            if (!cached) Spoken?.Invoke(A350DisplayStatus.GettingReady);
            _engine = A350ModuleCache.CreateEngine();
            _module = A350ModuleCache.LoadOrBuild(_engine, path, _localAppData, out _);
            _draw = new A350DrawEngine(_engine, _module, ui, _facts) { AVarAnswer = _aVar, CalcStringAnswer = _calcString, RecordShapes = true };   // the SD's symbols
            _reader = new A350MemoryReader(_facts.Anchors);
            State = A350DisplayState.Ready;
            if (!cached) Spoken?.Invoke(A350DisplayStatus.Ready);
            Log.Info("A350Display", "ready");
        }
        catch (Exception ex) when (ex is A350ModuleException or WasmtimeException or IOException or UnauthorizedAccessException)
        {
            State = A350DisplayState.Unavailable;
            Reason = ex.Message;
            Log.Warn("A350Display", "not available: " + ex.Message);
            Spoken?.Invoke(A350DisplayStatus.NotAvailable(ex.Message));
        }
    }

    private bool EnsureLocated()
    {
        if (_reader == null) return false;
        if (_reader.Base != 0 && _reader.IsStillValid()) return true;
        bool ok = _reader.TryLocate();
        if (ok) Log.Info("A350Display", $"A350 memory at 0x{_reader.Base:x}");
        return ok;
    }

    /// <summary>Draws the named gauges on one fresh copy; null while not ready or with no A350 in the sim.</summary>
    public Task<IReadOnlyList<A350DrawCapture>?> DrawAsync(params (string Gauge, int Index)[] gauges)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<A350DrawCapture>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_disposed || State != A350DisplayState.Ready && State != A350DisplayState.Preparing) { tcs.SetResult(null); return tcs.Task; }
        _work.Add(() =>
        {
            var started = DateTime.UtcNow;
            try
            {
                if (State != A350DisplayState.Ready || _draw == null || !EnsureLocated()) { tcs.SetResult(null); return; }
                _draw.Load(_reader!.Snapshot());
                var caps = new List<A350DrawCapture>(gauges.Length);
                bool retried = false;
                foreach (var (g, i) in gauges)
                {
                    var cap = _draw.Draw(g, i);
                    if (!cap.Ok && !retried && cap.Error!.StartsWith("trap"))
                    {
                        retried = true;
                        _draw.Load(_reader.Snapshot());
                        cap = _draw.Draw(g, i);
                    }
                    caps.Add(cap);
                }
                if (caps.Count > 0 && _health.Report(caps.Any(c => c.Ok), started, DateTime.UtcNow) is { } said) Spoken?.Invoke(said);
                tcs.SetResult(caps);
            }
            catch (Exception ex)
            {
                Log.Warn("A350Display", "draw: " + ex.Message);
                tcs.SetResult(null);
            }
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _work.Add(() =>
        {
            _draw?.Dispose();
            _module?.Dispose();
            _engine?.Dispose();
            _reader?.Dispose();
        });
        _work.CompleteAdding();
    }
}
