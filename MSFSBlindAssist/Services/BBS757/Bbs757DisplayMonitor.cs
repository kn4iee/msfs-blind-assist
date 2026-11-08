using MSFSBlindAssist.Services.WasmMemory;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>A 757 display monitor without its reading type, so one registry holds every reader's.</summary>
public interface IBbs757DisplayMonitor : IDisposable
{
    /// <summary>Starts reading through <paramref name="env"/> on the monitor's own timer.</summary>
    void Start(IBbs757DisplayEnvironment env);

    /// <summary>Flight reload: drops the memory and searches again; the new flight's first good reading is a silent
    /// baseline.</summary>
    void Restart();

    /// <summary>Aircraft switch or exit: stops the timer and releases everything.</summary>
    void Stop();

    /// <summary>Whether the reader's display key goes to the AI read instead of the reader's window (S15).</summary>
    bool KeyFallsBackToAi();

    /// <summary>The display window's rows: the reader's while running, else one status row.</summary>
    IReadOnlyList<string> View();
}

/// <summary>
/// Reads one BlueBird 757 display from BBS752.wasm's memory and speaks its changes: the PMDG 737 FMA monitor's state
/// machine and timings (S1), made generic over an <see cref="IBbs757DisplayReader{TLayout, TReading}"/>. Ticked every
/// <see cref="IBbs757DisplayReader{TLayout, TReading}.TickMs"/> on the UI thread, it waits while the simulator gives no
/// gate values, has the shared module session analyse the installed files and locate the module's memory (through
/// <see cref="IBbs757DisplayEnvironment"/>, off the UI thread, polled with <see cref="Task.IsCompleted"/> and never
/// continued), trusts a candidate only once a reading passes the one gate (<see cref="Judge"/>), then speaks each change
/// once it has settled for 300 ms. Only a good reading is spoken, shown or becomes the baseline. The first good reading
/// after Start or Restart, and after the display was dark or powering up, is a silent baseline (S4). Memory not found
/// and values that do not match are never final: after the 120 s search window the reader keeps searching every 30 s.
/// Every change line is muted by its own Ctrl+M row. Errors never escape a tick: they are logged and end in
/// Unavailable, and Restart recovers.
/// </summary>
public sealed class Bbs757DisplayMonitor<TLayout, TReading> : IBbs757DisplayMonitor
    where TLayout : class
    where TReading : IBbs757Reading
{
    internal const long SettleMs = 300;
    internal const long LocateRetryMs = 5_000;
    internal const long LocateGiveUpMs = 120_000;
    internal const long SlowRetryMs = 30_000;
    internal const long LostStopMs = 10_000;
    internal const long MismatchToleranceMs = 2_000;
    internal const int CorruptTickLimit = 10;

    /// <summary>How far back the gate's readings reach: the simulator's value may lag the memory's.</summary>
    internal const long HistoryMs = 3_000;

    /// <summary>After the display powers up, nothing is compared for this long, so power-up is never spoken (S4).</summary>
    internal const long LitQuietMs = 10_000;

    private enum State { Stopped, WaitingForSimulator, Analyzing, Locating, Verifying, Running, Mismatched, Unavailable }

    /// <summary>What the one gate makes of a reading.</summary>
    private enum Verdict { Corrupt, Quiet, Fails, Passes }

    private readonly IBbs757DisplayReader<TLayout, TReading> _reader;
    private IBbs757DisplayEnvironment? _env;
    private System.Windows.Forms.Timer? _timer;
    private State _state = State.Stopped;
    private bool _inTick;

    // The file analysis and the memory search: started here, run off the UI thread, polled each tick.
    private IReadOnlyList<TLayout>? _layouts;
    private Task<ModuleAnalysis>? _analysis;
    private Task<ModuleLocation>? _locate;
    private long _locateStartMs;
    private long? _lastAttemptMs;
    private int _attempts;
    private bool _slowRetry; // the 120 s window ran out: search every 30 s until found

    // The candidate being read and those waiting, each waiting one with what it knew of the display's power. The monitor
    // owns them all and disposes each once.
    private IBbs757DisplaySampler<TReading>? _sampler;
    private readonly Queue<(IBbs757DisplaySampler<TReading> Sampler, bool? LastLit, long? LitSinceMs)> _candidates = new();
    private long _candidateStartMs;
    private long? _holdSinceMs; // the candidate has read quiet since then: the search window pauses meanwhile
    private readonly List<(long AtMs, TReading Reading)> _history = new();

    // The display's power (S4) as the sampler being read sees it, from its readings that are not corrupt.
    private bool? _lastLit;      // at the last such reading; null when nothing is known, or with no power state
    private long? _litSinceMs;   // it powered up then, and is still in its quiet
    private bool _rebaseline;    // a quiet reading was seen: the next good reading is a silent baseline

    // Running.
    private TReading _spoken = default!;  // what the pilot last heard (or would have, unmuted)
    private TReading _current = default!; // the latest good reading, for the window
    private long? _changeAtMs;
    private long _lastPassMs;
    private int _corruptTicks;
    private bool _everRunning;
    private string _reason = Bbs757DisplayReasons.InternalError; // why unavailable, or the search window's last failure

    public Bbs757DisplayMonitor(IBbs757DisplayReader<TLayout, TReading> reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    internal string StateName => _state.ToString();

    /// <summary>The latest good reading while running, else default.</summary>
    public TReading? Latest => _state == State.Running ? _current : default;

    /// <summary>The reader's rows of the latest good reading while running, else null.</summary>
    public IReadOnlyList<string>? RowsWhileRunning() => _state == State.Running ? _reader.Rows(_current) : null;

    /// <summary>The reader's rows while running; otherwise one row saying why not. Never a reading the gate has not
    /// passed.</summary>
    public IReadOnlyList<string> View() => RowsWhileRunning() ?? new[] { StatusRow() };

    /// <summary>True while the reader cannot run: not started, or unavailable.</summary>
    public bool KeyFallsBackToAi() => _state is State.Stopped or State.Unavailable;

    public void Start(IBbs757DisplayEnvironment env) => Start(env, startTimer: true);

    /// <summary>Starts reading through <paramref name="env"/>. On a monitor that is already started it swaps in the
    /// environment and restarts; the layouts are kept only for the same environment.</summary>
    public void Start(IBbs757DisplayEnvironment env, bool startTimer)
    {
        ArgumentNullException.ThrowIfNull(env);
        bool wasStopped = _state == State.Stopped;
        ReleaseReading();
        if (!ReferenceEquals(env, _env))
        {
            // Another environment may hold another session: its files are analysed afresh.
            AbandonAnalysis();
            _layouts = null;
        }
        _env = env;
        BeginSession(wasStopped ? "started" : "started again");
        if (startTimer && _timer is null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = _reader.TickMs };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    /// <summary>Flight reload: drops the memory and searches again, keeping the derived layouts.</summary>
    public void Restart()
    {
        if (_state == State.Stopped || _env is null) return;
        ReleaseReading();
        BeginSession("restarted");
    }

    /// <summary>Aircraft switch or exit: stops the timer and releases everything.</summary>
    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer.Dispose();
            _timer = null;
        }
        if (_env is null) return;
        ReleaseReading();
        AbandonAnalysis();
        _layouts = null;
        ResetSession();
        Go(State.Stopped, "stopped");
        _env = null;
    }

    public void Dispose() => Stop();

    private string StatusRow() => _state switch
    {
        State.Unavailable => Bbs757DisplaySpeech.Unavailable(_reader.Name, _reason),
        State.WaitingForSimulator => Bbs757DisplaySpeech.Waiting(_reader.Name),
        _ => Bbs757DisplaySpeech.Starting(_reader.Name),
    };

    private void OnTimerTick(object? sender, EventArgs e) => Tick();

    internal void Tick()
    {
        // Unavailable holds until Restart or Start; the timer keeps running so either can recover.
        if (_inTick || _env is null || _state is State.Stopped or State.Unavailable) return;
        _inTick = true;
        try
        {
            Step(_env);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            _inTick = false;
        }
    }

    private void Step(IBbs757DisplayEnvironment env)
    {
        var sim = env.ReadSimulator(_reader.GateKeys);
        if (sim is null)
        {
            // Disconnected, or another aircraft loaded: drop the memory, keep the layouts (and an analysis in flight).
            if (_state != State.WaitingForSimulator)
            {
                ReleaseReading();
                Go(State.WaitingForSimulator, "the simulator's values are not available");
            }
            return;
        }
        switch (_state)
        {
            case State.WaitingForSimulator: OnWaitingForSimulator(env); break;
            case State.Analyzing: OnAnalyzing(); break;
            case State.Locating: OnLocating(env); break;
            case State.Verifying: OnVerifying(env, sim.Value); break;
            case State.Running: OnRunning(env, sim.Value); break;
            case State.Mismatched: OnMismatched(env, sim.Value); break;
        }
    }

    private void OnWaitingForSimulator(IBbs757DisplayEnvironment env)
    {
        if (_layouts is not null)
        {
            BeginLocating(newWindow: true, "the simulator is ready");
            return;
        }
        _analysis ??= env.AnalyseAsync();
        Go(State.Analyzing, "the simulator is ready; analyzing BlueBird's files");
    }

    private void OnAnalyzing()
    {
        var task = _analysis!;
        if (!task.IsCompleted) return;
        _analysis = null;
        if (!task.IsCompletedSuccessfully)
        {
            FailTask("the file analysis", task);
            return;
        }
        var analysis = task.Result;
        var layouts = _reader.LayoutsIn(analysis);
        if (layouts.Count == 0)
        {
            // A layout file that loaded but lacks this reader's layout, or no layout file at all.
            EnterUnavailable(analysis.Builds.Count > 0
                ? Bbs757DisplayReasons.FileNotRecognised
                : Bbs757DisplayReasons.FromSession(analysis.Failure ?? Bbs757DisplayReasons.FileNotRecognised));
            return;
        }
        _layouts = layouts.ToArray();
        BeginLocating(newWindow: true, Invariant($"{_layouts.Count} layout(s) derived"));
    }

    /// <summary>
    /// <paramref name="newWindow"/> starts a new 120 s search window; false keeps the current one. Attempts are always at
    /// least 5 s apart (only Start and Restart reset that), so a reading that keeps failing cannot turn into a busy search
    /// of the simulator's memory.
    /// </summary>
    private void BeginLocating(bool newWindow, string reason)
    {
        ReleaseReading();
        if (newWindow)
        {
            _locateStartMs = _env!.NowMs;
            _attempts = 0;
            _slowRetry = false;
            _reason = Bbs757DisplayReasons.MemoryNotFound;
        }
        Go(State.Locating, reason);
        OnLocating(_env!);
    }

    private void OnLocating(IBbs757DisplayEnvironment env)
    {
        long now = env.NowMs;
        if (_locate is null)
        {
            if (!_slowRetry && now - _locateStartMs >= LocateGiveUpMs)
            {
                // Not final: a long load, or a reload whose values lag, can outlast the window. Keep searching, slowly,
                // until found, Stop or Restart.
                _slowRetry = true;
                Log(Invariant($"{_reason} after {_attempts} attempt(s); searching every {SlowRetryMs / 1000} s"));
            }
            if (_lastAttemptMs is { } last && now - last < (_slowRetry ? SlowRetryMs : LocateRetryMs)) return;
            _lastAttemptMs = now;
            _attempts++;
            _locate = env.LocateAsync();
            return;
        }
        if (!_locate.IsCompleted) return;
        var task = _locate;
        _locate = null;
        if (!task.IsCompletedSuccessfully)
        {
            FailTask("the memory search", task);
            return;
        }
        var location = task.Result;
        var found = SamplersIn(location);
        if (found.Count > 0)
        {
            // Each starts from what is known of the display's power now.
            foreach (var sampler in found) _candidates.Enqueue((sampler, _lastLit, _litSinceMs));
            StartNextCandidate(Invariant($"{found.Count} candidate(s) found"));
            return;
        }
        if (location.Failure is not null)
        {
            EnterUnavailable(Bbs757DisplayReasons.FromSession(location.Failure)); // no access is final
            return;
        }
        // Not found yet: the next attempt starts 5 s (30 s once the window ran out) after this one did.
        _reason = Bbs757DisplayReasons.MemoryNotFound;
    }

    /// <summary>One sampler per layout of this reader in each located build, each on its own lease. A handle closed
    /// meanwhile (a reload or switch racing the search) is "not found yet".</summary>
    private List<IBbs757DisplaySampler<TReading>> SamplersIn(ModuleLocation location)
    {
        var samplers = new List<IBbs757DisplaySampler<TReading>>();
        try
        {
            foreach (var layout in _layouts!)
            {
                foreach (var module in location.Found)
                {
                    IBbs757DisplaySampler<TReading>? sampler;
                    try
                    {
                        sampler = _reader.SamplerFor(module, layout);
                    }
                    catch (ObjectDisposedException)
                    {
                        Log("the simulator handle closed during the memory search");
                        continue;
                    }
                    if (sampler is not null) samplers.Add(sampler);
                }
            }
        }
        catch
        {
            foreach (var sampler in samplers) DisposeQuietly(sampler);
            throw;
        }
        return samplers;
    }

    private void StartNextCandidate(string reason)
    {
        (_sampler, _lastLit, _litSinceMs) = _candidates.Dequeue();
        _candidateStartMs = _env!.NowMs;
        _holdSinceMs = null;
        _history.Clear();
        Go(State.Verifying, reason);
    }

    private void OnVerifying(IBbs757DisplayEnvironment env, Bbs757SimValues sim)
    {
        long now = env.NowMs;
        if (!_sampler!.TryRead(out var reading))
        {
            EndHold(now);
            DropCandidate(readFailed: true);
            return;
        }
        var verdict = Judge(now, reading, sim);
        if (verdict == Verdict.Quiet)
        {
            // Not compared: neither trusted nor dropped (a cold-and-dark load would otherwise search again and again), and
            // the search window pauses. Held 2 s while another candidate waits, it goes to the back of the queue, so a
            // wrong one that decodes "dark" never starves the rest; alone, it holds. It has its 2 s once the display counts.
            _holdSinceMs ??= now;
            _candidateStartMs = now;
            if (_candidates.Count > 0 && now - _holdSinceMs >= MismatchToleranceMs) RequeueCandidate(now);
            return;
        }
        EndHold(now);
        if (verdict == Verdict.Passes)
        {
            while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue().Sampler);
            EnterRunning(reading, "the memory agrees with the simulator");
            return;
        }
        if (now - _candidateStartMs >= MismatchToleranceMs) DropCandidate(readFailed: false);
    }

    /// <summary>A quiet hold ends: its time does not count toward the 120 s search window.</summary>
    private void EndHold(long now)
    {
        if (_holdSinceMs is not { } since) return;
        _locateStartMs += now - since;
        _holdSinceMs = null;
    }

    /// <summary>The quiet candidate goes to the back of the queue with what it knows of the display's power; its 2 s
    /// start afresh when it comes up again.</summary>
    private void RequeueCandidate(long now)
    {
        EndHold(now);
        _candidates.Enqueue((_sampler!, _lastLit, _litSinceMs));
        _sampler = null;
        StartNextCandidate("quiet for 2 s while another candidate waits; trying the next");
    }

    /// <summary>
    /// The candidate failed. A wrong layout usually fails the read itself (its pointer lies outside the module), so the
    /// next candidate is tried before searching again: a new search would return the same ones. With none left, it
    /// searches again in the same window, which remembers why: a failed read is "memory not found", a disagreement
    /// "values do not match".
    /// </summary>
    private void DropCandidate(bool readFailed)
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        _reason = readFailed ? Bbs757DisplayReasons.MemoryNotFound : Bbs757DisplayReasons.Mismatch;
        string why = readFailed ? "the memory read failed" : "no good reading (matching the simulator, not corrupt) for 2 s";
        if (_candidates.Count > 0)
            StartNextCandidate($"{why}; trying the next candidate");
        else
            BeginLocating(newWindow: false, $"{why}; searching again");
    }

    private void EnterRunning(TReading reading, string reason)
    {
        // The first good reading after Start or Restart, or after the display was dark or powering up, is the baseline,
        // never spoken. Later, what changed while the reader was not running (a quick, silent search) is told, never
        // swallowed.
        var missed = _everRunning && !_rebaseline && !Same(_spoken, reading)
            ? _reader.Changes(_spoken, reading, withPrefix: true)
            : Array.Empty<Bbs757SpokenLine>();
        _spoken = _current = reading;
        _rebaseline = false;
        _changeAtMs = null;
        _lastPassMs = _env!.NowMs;
        _corruptTicks = 0;
        _everRunning = true;
        _slowRetry = false;
        Go(State.Running, reason);
        foreach (var line in missed) Say(line);
    }

    private void OnRunning(IBbs757DisplayEnvironment env, Bbs757SimValues sim)
    {
        long now = env.NowMs;
        if (!TryReadRunning(out var reading)) return;
        var verdict = Judge(now, reading, sim);
        if (verdict == Verdict.Corrupt)
        {
            // Never shown or spoken; ten in a row stop the reader.
            if (_corruptTicks++ == 0) Log("a corrupt reading");
            if (_corruptTicks >= CorruptTickLimit)
            {
                var why = Bbs757DisplayReasons.UnexpectedValues;
                EnterUnavailable(why, Invariant($"{why} for {CorruptTickLimit} readings"));
            }
            return;
        }
        _corruptTicks = 0;
        switch (verdict)
        {
            case Verdict.Quiet:
                // Not compared: neither a pass nor a mismatch, and an open settle window closes unspoken.
                _lastPassMs = now;
                _changeAtMs = null;
                return;
            case Verdict.Fails:
                if (now - _lastPassMs >= MismatchToleranceMs)
                {
                    _changeAtMs = null;
                    Go(State.Mismatched, "the memory has not matched the simulator for 2 s");
                }
                return; // an open settle window keeps its start; the next good reading speaks
        }
        _lastPassMs = now;
        _current = reading;
        if (_rebaseline)
        {
            // The first good reading after the display was dark or powering up.
            _spoken = reading;
            _rebaseline = false;
            _changeAtMs = null;
            return;
        }
        SpeakSettledChange(now);
    }

    private void OnMismatched(IBbs757DisplayEnvironment env, Bbs757SimValues sim)
    {
        long now = env.NowMs;
        if (!TryReadRunning(out var reading)) return;
        if (Judge(now, reading, sim) == Verdict.Passes) EnterRunning(reading, "the memory agrees with the simulator again");
    }

    /// <summary>A failed read means the memory moved or went away: search again, silently.</summary>
    private bool TryReadRunning(out TReading reading)
    {
        if (_sampler!.TryRead(out reading)) return true;
        BeginLocating(newWindow: true, "the memory read failed; searching again");
        return false;
    }

    /// <summary>
    /// The one gate (S3, S4). A corrupt reading counts for nothing and is never remembered. A reading while the display is
    /// unpowered, or less than <see cref="LitQuietMs"/> after it powered up, is quiet: not compared, neither a pass nor a
    /// failure, and the next good reading becomes the baseline silently. Any other is good when the reader's
    /// <see cref="IBbs757DisplayReader{TLayout, TReading}.Passes"/> agrees with the simulator over the last
    /// <see cref="HistoryMs"/>.
    /// </summary>
    private Verdict Judge(long now, TReading reading, Bbs757SimValues sim)
    {
        if (reading.Corrupt) return Verdict.Corrupt;
        Remember(now, reading);
        if (IsQuiet(now, reading, sim))
        {
            _rebaseline = true;
            return Verdict.Quiet;
        }
        return _reader.Passes(_history, sim) ? Verdict.Passes : Verdict.Fails;
    }

    /// <summary>Whether the display is unpowered or powered up less than <see cref="LitQuietMs"/> ago. A display already lit
    /// when nothing is known of its power (the session's first reading) counts at once: its power-up was not seen, and
    /// that reading is a silent baseline.</summary>
    private bool IsQuiet(long now, TReading reading, Bbs757SimValues sim)
    {
        bool? lit = _reader.Lit(reading, sim);
        if (lit is not { } on)
        {
            _lastLit = null;
            _litSinceMs = null;
            return false;
        }
        if (!on)
        {
            _lastLit = false;
            _litSinceMs = null;
            return true;
        }
        if (_lastLit == false) _litSinceMs = now; // it powered up
        _lastLit = true;
        if (_litSinceMs is not { } since) return false;
        if (now - since < LitQuietMs) return true;
        _litSinceMs = null;
        return false;
    }

    /// <summary>Speaks what changed once it has held for 300 ms. A change and back inside the window says nothing; the
    /// baseline advances even when muted, and when nothing speakable changed.</summary>
    private void SpeakSettledChange(long now)
    {
        if (Same(_spoken, _current))
        {
            _changeAtMs = null;
            return;
        }
        var changes = _reader.Changes(_spoken, _current, withPrefix: true);
        if (changes.Count == 0)
        {
            _spoken = _current;
            _changeAtMs = null;
            return;
        }
        _changeAtMs ??= now;
        if (now - _changeAtMs < SettleMs) return;
        _spoken = _current;
        _changeAtMs = null;
        foreach (var line in changes) Say(line);
    }

    private static bool Same(TReading a, TReading b) => EqualityComparer<TReading>.Default.Equals(a, b);

    private void Remember(long now, TReading reading)
    {
        _history.Add((now, reading));
        int stale = 0;
        while (stale < _history.Count && now - _history[stale].AtMs > HistoryMs) stale++;
        if (stale > 0) _history.RemoveRange(0, stale);
    }

    private void BeginSession(string reason)
    {
        ResetSession();
        _lastAttemptMs = null;
        Go(State.WaitingForSimulator, reason);
    }

    /// <summary>A new session: its first good reading is a baseline, and the display's power is not known yet.</summary>
    private void ResetSession()
    {
        _everRunning = false;
        _slowRetry = false;
        _rebaseline = false;
        _lastLit = null;
        _litSinceMs = null;
        _spoken = _current = default!;
    }

    /// <summary>Final until Restart or Start.</summary>
    private void EnterUnavailable(string reason, string? logReason = null)
    {
        ReleaseReading();
        _reason = reason;
        Go(State.Unavailable, logReason ?? reason);
    }

    private void FailTask(string what, Task task) =>
        Fail(task.Exception ?? (Exception)new OperationCanceledException($"{what} was cancelled"));

    private void Fail(Exception ex)
    {
        try
        {
            Log($"internal error: {ex}");
            EnterUnavailable(Bbs757DisplayReasons.InternalError);
        }
        catch (Exception again)
        {
            Log($"internal error while stopping: {again}");
            _reason = Bbs757DisplayReasons.InternalError;
            _state = State.Unavailable;
        }
    }

    /// <summary>Speaks one line unless its Ctrl+M row is muted; always the non-interrupting announce (S5).</summary>
    private void Say(Bbs757SpokenLine line)
    {
        if (!_env!.IsMuted(line.MuteKey)) _env.Speak(line.Text);
    }

    private void Go(State next, string reason)
    {
        var from = _state;
        _state = next;
        Log($"{from} -> {next}: {reason}");
    }

    private void Log(string line) => TryLog(_env, _reader.LogChannel, line);

    /// <summary>Disposes the sampler and every candidate, and abandons a memory search in flight (its location is the
    /// session's, which releases it; the monitor makes samplers only from a search it polled).</summary>
    private void ReleaseReading()
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue().Sampler);
        _holdSinceMs = null;
        _history.Clear();
        _changeAtMs = null;

        var locate = _locate;
        _locate = null;
        if (locate is not null) LogIfFaulted(locate, "abandoned memory search");
    }

    private void AbandonAnalysis()
    {
        var analysis = _analysis;
        _analysis = null;
        if (analysis is not null) LogIfFaulted(analysis, "abandoned file analysis");
    }

    private void LogIfFaulted(Task task, string what)
    {
        var env = _env;
        string channel = _reader.LogChannel;
        task.ContinueWith(t =>
        {
            if (t.IsFaulted) TryLog(env, channel, $"{what} failed: {t.Exception}");
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void DisposeQuietly(IBbs757DisplaySampler<TReading>? sampler)
    {
        if (sampler is null) return;
        try
        {
            sampler.Dispose();
        }
        catch (Exception ex)
        {
            Log($"disposing a sampler failed: {ex}");
        }
    }

    private static void TryLog(IBbs757DisplayEnvironment? env, string channel, string line)
    {
        try
        {
            env?.Log(channel, line);
        }
        catch
        {
            // The log itself failed: there is nowhere left to report it.
        }
    }
}
