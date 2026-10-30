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
/// copy for all the gauges it names, and hands on the FMS's take-off speeds it holds (<see cref="TakeoffSpeedsRead"/>);
/// a trapped draw is retried once on a new copy (a torn copy, [A350-2]); the A350 is re-located when its anchors no
/// longer read (a reload). When a display's draws keep failing the pilot is
/// told, and told again when they read (<see cref="A350ReadHealth"/>); when nothing can be drawn at all (no A350 found
/// in an A350 flight, no access to the sim, an error in the reader), told once why (<see cref="A350ReaderStatus"/>).
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
    private readonly A350ReaderStatus _status = new();
    private readonly A350ReaderStats _stats = new();
    private readonly A350MemoryWarning _memoryWarning = new();
    private static readonly LogChannel Diagnostics = Log.Channel("a350_display");

    /// <summary>Whether a call-out's Ctrl+M row is unticked (the low memory warning's).</summary>
    public Func<string, bool> Muted { get; set; } = _ => false;

    /// <summary>Whether the sim runs a flight with the A350 loaded: why nothing can be read is said only then (the menu
    /// or another aircraft finds no A350, and that is no news). Asked on the worker thread.</summary>
    public Func<bool> A350Flight { get; set; } = () => true;

    /// <summary>Where the installed module is (tests point it at a module of their own).</summary>
    internal Func<string?> FindModule { get; init; } = A350ModuleCache.FindInstalledModule;

    /// <summary>The displays whose last draw ran out of head room (worker thread only).</summary>
    private readonly HashSet<(string Gauge, int Index)> _exhausted = new();
    private volatile bool _disposed;

    /// <summary>A status sentence to speak (getting ready, ready, not available).</summary>
    public event Action<string>? Spoken;

    /// <summary>The FMS's take-off speeds in each copy a draw request takes (<see cref="A350TakeoffSpeeds"/>), raised on the
    /// worker thread; never when this A350 version's module does not say where they are (logged as it is prepared).</summary>
    public event Action<A350TakeoffSpeeds>? TakeoffSpeedsRead;

    /// <summary>The FMS's approach minimums in each copy a draw request takes (<see cref="A350Minimums"/>), the same way.</summary>
    public event Action<A350Minimums>? MinimumsRead;

    /// <summary>BTV's rollout in each copy a draw request takes (<see cref="A350BtvRollout"/>), the same way.</summary>
    public event Action<A350BtvRollout>? BtvRead;

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

    /// <summary>The module prepared and its identity when it was (<see cref="A350ModuleCache.IdentityOf"/>).</summary>
    private string? _modulePath;
    private A350ModuleCache.ModuleIdentity? _moduleIdentity;

    private void Prepare()
    {
        try
        {
            var path = FindModule() ?? throw new A350ModuleException("the A350 is not installed");
            _modulePath = path;
            Log.Info("A350Display", $"installed A350 package {A350ModuleCache.PackageVersionOf(path) ?? "(version not read)"}, MSFSBA's control map measured on {A350ControlMap.Load().PackageVersion}, patch version {A350ModulePatcher.Version}: {path}");
            _moduleIdentity = A350ModuleCache.IdentityOf(path);
            var mod = A350WasmModule.Parse(File.ReadAllBytes(path));
            _facts = A350ModuleFacts.From(mod);
            if (_facts.TakeoffSpeeds == null)
                Log.Warn("A350Display", "this A350 version does not say where its FMS keeps the take-off speeds: no V1, Rotate and V2 call-outs");
            if (_facts.Minimums == null)
                Log.Warn("A350Display", "this A350 version does not say where its FMS keeps the approach minimums: no minimum call-outs");
            var ui = A350ModulePatcher.UiDrawClassesOf(mod);
            _engine = A350ModuleCache.CreateEngine();
            // Hashed once; "getting ready" is said as a compile starts, whatever the reason (no cache, a corrupt one).
            _module = A350ModuleCache.LoadOrBuild(_engine, path, A350ModuleCache.Sha256Of(path), _localAppData,
                () => Say(A350DisplayStatus.GettingReady), out bool built);
            _draw = new A350DrawEngine(_engine, _module, ui, _facts) { AVarAnswer = _aVar, CalcStringAnswer = _calcString, RecordShapes = true };   // sprites: the SD's symbols, the PFD's tape and heading scale, the MFD's
            _reader = new A350MemoryReader(_facts.Anchors);
            State = A350DisplayState.Ready;
            if (built) Say(A350DisplayStatus.Ready);
            Log.Info("A350Display", "ready");
        }
        catch (Exception ex)
        {
            // Any failure leaves the displays unavailable and says why (review 2026-10-29: a module that could not be
            // parsed, a truncated file mid-update, threw past the four types caught here and left "Preparing" for good).
            bool known = ex is A350ModuleException or WasmtimeException or IOException or UnauthorizedAccessException;
            State = A350DisplayState.Unavailable;
            Reason = known ? ex.Message : "the A350's module could not be read";
            Log.Warn("A350Display", "not available: " + (known ? ex.Message : ex.ToString()));
            Say(A350DisplayStatus.NotAvailable(Reason));
        }
    }

    /// <summary>Whether the A350 is located, looking for it again once its anchors no longer read (a reload); after a
    /// look that found nothing, the next waits (<see cref="A350ReaderStatus.MayLocate"/>).</summary>
    private bool EnsureLocated()
    {
        if (_reader == null) return false;
        if (_reader.Base != 0 && _reader.IsStillValid()) return true;
        var now = DateTime.UtcNow;
        if (!_status.MayLocate(now)) return false;
        bool ok = _reader.TryLocate();
        if (ok && ModuleChanged())
        {
            // An A350 update while MSFSBA stayed open (review 2026-10-29): the prepared patch was made from the old module,
            // and its code would draw the new memory. Prepared again, and looked for again with the new module's anchors.
            Log.Info("A350Display", "the installed A350 module changed: preparing it again");
            Reprepare();
            return false;
        }
        if (ok)
        {
            Log.Info("A350Display", $"A350 memory at 0x{_reader.Base:x}");
            _draw?.Forget();   // another A350, or this one reloaded: what the engine found in the last one is gone
        }
        else
        {
            Log.Debug("A350Display", "A350 not located: " + _reader.LastFailure);
            Say(_status.Report(_reader.LastFailure ?? A350ReaderFailure.NotFound, A350Flight(), now));
        }
        return ok;
    }

    /// <summary>Speaks a status sentence, never once the service is disposed: a compile or a draw queued before it ends
    /// after the pilot switched aircraft, and its "ready" would be said over the next aircraft (review 2026-10-29).</summary>
    private void Say(string? line)
    {
        if (line == null || _disposed) return;
        Log.Info("A350Display", "said: " + line);
        Spoken?.Invoke(line);
    }

    /// <summary>Whether the installed module is no longer the one prepared (an identity that cannot be read is no change:
    /// a module being replaced is checked again on the next look).</summary>
    private bool ModuleChanged() =>
        _modulePath != null && A350ModuleCache.IdentityOf(_modulePath) is { } now && _moduleIdentity is { } prepared && now != prepared;

    /// <summary>Drops the engine built from the old module and prepares the installed one (on the worker thread).</summary>
    private void Reprepare()
    {
        _draw?.Dispose();
        _module?.Dispose();
        _engine?.Dispose();
        _reader?.Dispose();
        _draw = null;
        _module = null;
        _engine = null;
        _reader = null;
        State = A350DisplayState.Preparing;
        Prepare();
    }

    private readonly HashSet<string> _failuresLogged = new();   // the worker thread's own

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
                var copyClock = System.Diagnostics.Stopwatch.StartNew();
                var copy = _reader!.Snapshot();
                _stats.Copied(started, copyClock.Elapsed.TotalMilliseconds, _reader.LastBytes, _reader.LastReread, _reader.LastLeftOut);
                _draw.Load(copy);
                if (_facts?.TakeoffSpeeds is { } speedsAt && TakeoffSpeedsRead is { } speedsRead
                    && A350TakeoffSpeeds.Read(copy, speedsAt) is { } speeds)
                    speedsRead(speeds);
                if (_facts?.Minimums is { } minimumsAt && MinimumsRead is { } minimumsRead
                    && A350Minimums.Read(copy, minimumsAt) is { } minimums)
                    minimumsRead(minimums);
                if (_facts?.Btv is { } btvAt && BtvRead is { } btvRead && A350BtvRollout.Read(copy, btvAt) is { } btv)
                    btvRead(btv);
                var caps = new List<A350DrawCapture>(gauges.Length);
                bool retried = false;
                foreach (var (g, i) in gauges)
                {
                    var cap = _draw.Draw(g, i);
                    NoteDraw(g, i, cap);
                    if (!cap.Ok && !retried && cap.Error!.StartsWith("trap"))
                    {
                        retried = true;
                        _stats.Retried();
                        copyClock.Restart();
                        _draw.Load(_reader.Snapshot());
                        _stats.Copied(started, copyClock.Elapsed.TotalMilliseconds, _reader.LastBytes, _reader.LastReread, _reader.LastLeftOut);
                        cap = _draw.Draw(g, i);
                        NoteDraw(g, i, cap);
                    }
                    caps.Add(cap);
                    // The draw asked for more than the head room holds and got NULL: logged when it starts and when it ends,
                    // never every draw.
                    if (_draw.LastHeapExhausted != _exhausted.Contains((g, i)))
                    {
                        if (_draw.LastHeapExhausted) _exhausted.Add((g, i)); else _exhausted.Remove((g, i));
                        Log.Warn("A350Display", $"{g}#{i}: the head room {(_draw.LastHeapExhausted ? "ran out" : "is enough again")} "
                            + $"({_draw.LastHeapBytes / 1048576} MB allocated)");
                    }
                }
                var ended = DateTime.UtcNow;
                Say(_status.Report(null, true, ended));
                for (int n = 0; n < caps.Count; n++)
                    if (_health.Report(gauges[n].Gauge, gauges[n].Index, caps[n].Ok, started, ended) is { } said) Say(said);
                _failuresLogged.Clear();   // read again: a failure that comes back later is logged again
                if (_stats.IsDue(ended))
                {
                    var machine = A350MachineMemory.Read();
                    Diagnostics.Info(_stats.Due(ended, machine)!);
                    if (_memoryWarning.Observe(machine.FreeCommit) is { } low && !Muted(A350MemoryWarning.MuteKey)) Say(low);
                }
                tcs.SetResult(caps);
            }
            catch (Exception ex)
            {
                // Each distinct failure once, with its trace: the monitor asks twice a second (cleanup review 2026-10-30).
                if (_failuresLogged.Add(ex.GetType().Name + ": " + ex.Message)) Log.Warn("A350Display", "draw: " + ex);
                try { Say(_status.Report(A350ReaderFailure.Error, A350Flight(), DateTime.UtcNow)); }
                catch (Exception) { /* the status is best-effort; the request still answers */ }
                tcs.SetResult(null);
            }
        });
        return tcs.Task;
    }

    /// <summary>One draw's numbers for the minute's line, and a failure's text, with the wasm backtrace of a trap, each
    /// display's each error at most once a minute (<see cref="A350ReaderStats.LogFailure"/>).</summary>
    private void NoteDraw(string gauge, int index, A350DrawCapture cap)
    {
        _stats.Drew(gauge, index, cap.Milliseconds, cap.Ok, cap.Texts.Count(t => t.Text.Any(char.IsControl)), _draw!.LastHeapBytes);
        if (!cap.Ok && _stats.LogFailure(gauge, index, cap.Error!, DateTime.UtcNow))
            Diagnostics.Warn($"{gauge}#{index}: {cap.Error}"
                + (cap.Error!.StartsWith("trap") && _draw.LastTrap is { } trap ? Environment.NewLine + trap : ""));
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
