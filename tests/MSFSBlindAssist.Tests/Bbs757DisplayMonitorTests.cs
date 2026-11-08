// The 757's display monitor: the one state machine every BlueBird 757 memory reader drives (the PMDG 737 FMA
// monitor's, made generic over a reader). Driven by a fake reader, environment and samplers with a settable clock
// (100 ms per Tick()): no simulator, no timer, no module memory.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public sealed class Bbs757DisplayMonitorTests : IDisposable
{
    private const string LogChannel = "bbs757-x";
    private const string StartingRow = "X reader starting";
    private const string WaitingRow = "X reader waiting for the simulator";

    private static FakeReading R(string value) => new(value);

    // Every sampler any test's reader made: each must be disposed at most once (the real ones each hold a lease on
    // the one shared simulator handle).
    private readonly List<FakeReader> _readers = new();

    public void Dispose()
    {
        foreach (var reader in _readers)
            foreach (var s in reader.Samplers)
                Assert.True(s.DisposeCount <= 1, $"a sampler was disposed {s.DisposeCount} times");
    }

    private Harness New()
    {
        var h = new Harness();
        _readers.Add(h.Reader);
        return h;
    }

    [Fact]
    public void Waits_silently_while_the_simulator_reads_null()
    {
        var h = New();
        h.Env.Simulator = null;
        h.Start();
        h.Tick(300); // 30 s

        Assert.Equal("WaitingForSimulator", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(0, h.Env.AnalyseCalls);
        Assert.Equal(0, h.Env.LocateCalls);
        Assert.Equal(new[] { WaitingRow }, h.Monitor.View());
        Assert.Contains("L:X_GATE", h.Env.KeysAsked); // it asked for the gate's variables
    }

    [Fact]
    public void The_first_passing_reading_is_a_silent_baseline()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Tick(20);

        Assert.Equal("Running", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
        Assert.Equal(new[] { "X: A" }, h.Monitor.RowsWhileRunning());
        Assert.Equal(R("A"), h.Monitor.Latest);
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.NotEmpty(h.Env.Logged);
        Assert.All(h.Env.Logged, l => Assert.Equal(LogChannel, l.Channel)); // the reader's own log
    }

    [Fact]
    public void A_change_is_spoken_once_after_300_ms_of_settling()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(5);

        long t = h.Env.NowMs;
        s.Reading = R("B");
        h.TickAt(t);
        h.TickAt(t + 100);
        h.TickAt(t + 299);
        Assert.Empty(h.Env.Spoken);

        h.TickAt(t + 300);
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken);
        h.Tick(30);
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken); // once
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());
    }

    [Fact]
    public void A_change_that_returns_before_settling_is_not_spoken()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(5);

        s.Reading = R("B");
        h.Tick(2); // B for 200 ms
        s.Reading = R("A");
        h.Tick(30);

        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
    }

    [Fact]
    public void A_failing_candidate_gives_way_to_the_next()
    {
        var h = New();
        var one = new FakeLayout("one");
        var two = new FakeLayout("two");
        h.Reader.Layouts = new[] { one, two };
        h.Reader.MakeSampler = (_, layout) => h.Reader.NewSampler(layout == one ? R("WRONG") : R("A"));
        h.Start();
        h.TickUntil(() => h.State == "Verifying");
        var first = h.Reader.Samplers[0];

        h.Tick(19); // the first candidate never passes, but has its 2 s
        Assert.Equal("Verifying", h.State);
        Assert.False(first.Disposed);
        Assert.True(first.Reads >= 19, $"{first.Reads} reads");

        var running = h.RunUntilRunning(5);
        Assert.Same(h.Reader.Samplers[1], running);
        Assert.True(first.Disposed);
        Assert.Equal(1, h.Env.LocateCalls); // the next candidate, not a new search
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
    }

    [Fact]
    public void Unlit_readings_are_not_compared_and_lighting_up_waits_10_s()
    {
        var h = New();
        h.Reader.LitNow = true;
        h.Start();
        var s = h.RunUntilRunning(); // lit at its first reading: A is the baseline at once

        h.Reader.LitNow = false; // the display goes dark
        s.Reading = R("B");
        h.Tick(30);
        s.Reading = R("C");
        h.Tick(30);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State); // not compared: never a mismatch either

        long t = h.Env.NowMs; // the next tick reads at t
        h.Reader.LitNow = true;
        s.Reading = R("D");
        h.Tick(30); // t to t+2.9 s
        s.Reading = R("E");
        h.Tick(69); // t+3 s to t+9.8 s
        s.Reading = R("F");
        h.Tick(); // t+9.9 s
        Assert.Empty(h.Env.Spoken);

        Assert.Equal(t + 10_000, h.Env.NowMs);
        s.Reading = R("G");
        h.Tick(); // t+10 s: the baseline, never spoken
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: G" }, h.Monitor.View());

        s.Reading = R("H");
        h.Tick(5);
        Assert.Equal(new[] { "X: H" }, h.Env.Spoken);
    }

    [Fact]
    public void Unlit_at_the_first_reading_holds_the_candidate_until_10_s_after_it_lights()
    {
        // A cold-and-dark load: the reading counts for nothing, so the candidate is neither trusted nor dropped (a drop
        // would search again every few seconds and end the search window with "values do not match").
        var h = New();
        h.Reader.LitNow = false;
        h.Start();
        h.Tick(600); // a minute cold and dark
        Assert.Equal("Verifying", h.State);
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.False(Assert.Single(h.Reader.Samplers).Disposed);
        Assert.Equal(new[] { StartingRow }, h.Monitor.View());

        h.Reader.LitNow = true;
        h.Reader.Samplers[0].Reading = R("B");
        h.Tick(100); // lit for 9.9 s
        Assert.Equal("Verifying", h.State);

        h.Tick(); // lit for 10 s: trusted, and a silent baseline
        Assert.Equal("Running", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());
    }

    [Fact]
    public void A_dark_candidate_gives_way_to_the_next_which_runs_once_lit()
    {
        // A wrong candidate that reads cleanly and decodes "dark" is never trusted, and never holds the others from their
        // turn: held 2 s while another waits, it goes to the back of the queue. Each keeps its own view of the power.
        var h = New();
        var one = new FakeLayout("one");
        var two = new FakeLayout("two");
        h.Reader.Layouts = new[] { one, two };
        h.Reader.MakeSampler = (_, layout) => h.Reader.NewSampler(layout == one ? R("DARK") : R("OFF"));
        h.Reader.LitOf = r => r.Value is not ("DARK" or "OFF");
        h.Start();
        h.Tick(300); // 30 s, both dark: they take turns, silently
        Assert.Equal("Verifying", h.State);
        Assert.Equal(2, h.Reader.Samplers.Count);
        Assert.All(h.Reader.Samplers, s => Assert.False(s.Disposed));
        Assert.All(h.Reader.Samplers, s => Assert.True(s.Reads > 50, $"{s.Reads} reads"));

        var first = h.Reader.Samplers[0];
        var second = h.Reader.Samplers[1];
        second.Reading = R("A"); // the second one's display lights; the first reads dark forever
        var running = h.RunUntilRunning(200); // its 10 s quiet, counted across the turns it is not read
        Assert.Same(second, running);
        Assert.True(first.Disposed);
        Assert.Equal(1, h.Env.LocateCalls); // the same two candidates throughout, no new search
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
    }

    [Fact]
    public void A_long_dark_hold_leaves_the_search_window_for_after_power_up()
    {
        // Five minutes cold and dark, then the display lights and the candidate disagrees: the search goes on every 5 s,
        // because the dark time never counted toward the 120 s window (else it would already be the 30 s slow retry).
        var h = New();
        h.Reader.LitNow = false;
        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("WRONG"));
        h.Start();
        h.Tick(3000); // 5 minutes
        Assert.Equal("Verifying", h.State);
        Assert.Equal(1, h.Env.LocateCalls);

        h.Reader.LitNow = true;
        h.Tick(600); // a minute lit: 10 s quiet, then each candidate disagrees for its 2 s
        var after = h.Env.LocateTimes.Skip(1).ToArray();
        Assert.True(after.Length >= 8, $"{after.Length} attempts after power-up");
        Assert.All(after.Zip(after.Skip(1)), p => Assert.Equal(5000, p.Second - p.First));
        Assert.NotEqual("Running", h.State);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void A_change_missed_while_searching_again_is_spoken_when_found()
    {
        // A failed read searches again silently; what changed meanwhile is told once it reads again, never swallowed.
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Fail = true;
        h.Tick();
        Assert.Equal("Locating", h.State);
        Assert.True(s.Disposed);

        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("B"));
        h.RunUntilRunning(100); // the next attempt is at most 5 s away
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken);
        Assert.Equal(2, h.Env.LocateCalls);
    }

    [Fact]
    public void View_while_starting_is_one_starting_row()
    {
        var h = New();
        Assert.Equal(new[] { StartingRow }, h.Monitor.View()); // not started yet
        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("WRONG"));
        h.Start();

        var seen = new HashSet<string>();
        for (int i = 0; i < 15; i++) // through Analyzing and Locating into Verifying
        {
            h.Tick();
            seen.Add(h.State);
            Assert.Equal(new[] { StartingRow }, h.Monitor.View());
            Assert.Null(h.Monitor.RowsWhileRunning());
            Assert.Equal(default(FakeReading), h.Monitor.Latest);
        }
        Assert.Contains("Analyzing", seen);
        Assert.Contains("Locating", seen);
        Assert.Contains("Verifying", seen);
        Assert.DoesNotContain("Running", seen);
    }

    // ---- fakes ----

    private sealed class Harness
    {
        public Harness() => Monitor = new Bbs757DisplayMonitor<FakeLayout, FakeReading>(Reader);

        public FakeEnvironment Env { get; } = new();
        public FakeReader Reader { get; } = new();
        public Bbs757DisplayMonitor<FakeLayout, FakeReading> Monitor { get; }
        public string State => Monitor.StateName;

        public Harness Start()
        {
            Monitor.Start(Env, startTimer: false);
            return this;
        }

        /// <summary>One Tick() at the current time per count, each followed by 100 ms on the clock.</summary>
        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += 100;
            }
        }

        /// <summary>One Tick() at <paramref name="ms"/>; the clock stays there.</summary>
        public void TickAt(long ms)
        {
            Env.NowMs = ms;
            Monitor.Tick();
        }

        public void TickUntil(Func<bool> done, int maxTicks = 300)
        {
            for (int i = 0; i < maxTicks && !done(); i++) Tick();
            Assert.True(done(), $"not reached within {maxTicks} ticks; state {State}");
        }

        /// <summary>Ticks until Running and returns the one sampler still alive (the one being read).</summary>
        public FakeSampler RunUntilRunning(int maxTicks = 300)
        {
            TickUntil(() => State == "Running", maxTicks);
            return Reader.Samplers.Single(s => !s.Disposed);
        }
    }

    internal sealed record FakeLayout(string Name);

    internal readonly record struct FakeReading(string Value, bool Corrupt = false) : IBbs757Reading
    {
        public override string ToString() => Value;
    }

    internal sealed class FakeSampler : IBbs757DisplaySampler<FakeReading>
    {
        private int _disposeCount;

        public FakeSampler(FakeReading reading) => Reading = reading;

        /// <summary>What a read returns once <see cref="Script"/> is used up.</summary>
        public FakeReading Reading { get; set; }

        public Queue<FakeReading> Script { get; } = new();
        public bool Fail { get; set; }
        public int Reads { get; private set; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool Disposed => DisposeCount > 0;

        public bool TryRead(out FakeReading reading)
        {
            // A read after Dispose is a monitor bug; throwing turns it into a visible internal error.
            if (Disposed) throw new ObjectDisposedException(nameof(FakeSampler));
            Reads++;
            reading = Fail ? default : Script.Count > 0 ? Script.Dequeue() : Reading;
            return !Fail;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    internal sealed class FakeReader : IBbs757DisplayReader<FakeLayout, FakeReading>
    {
        public FakeReader() => MakeSampler = (_, _) => NewSampler(R("A"));

        public string Name => "X";
        public string MuteKey => "MUTE_X";
        public string LogChannel => Bbs757DisplayMonitorTests.LogChannel;
        public int TickMs => 100;
        public IReadOnlyList<string> GateKeys { get; } = new[] { "L:X_GATE" };

        public IReadOnlyList<FakeLayout> Layouts { get; set; } = new[] { new FakeLayout("one") };

        /// <summary>The sampler for a located module and one of <see cref="Layouts"/>, or null.</summary>
        public Func<LocatedModule, FakeLayout, FakeSampler?> MakeSampler { get; set; }

        public List<FakeSampler> Samplers { get; } = new();

        /// <summary>The power state every reading reports (null: no power state), unless <see cref="LitOf"/> is set.</summary>
        public bool? LitNow { get; set; }

        /// <summary>When set, the power state as decoded from the reading itself.</summary>
        public Func<FakeReading, bool?>? LitOf { get; set; }

        public FakeSampler NewSampler(FakeReading reading)
        {
            var s = new FakeSampler(reading);
            Samplers.Add(s);
            return s;
        }

        public IReadOnlyList<FakeLayout> LayoutsIn(ModuleAnalysis analysis) => Layouts;

        public IBbs757DisplaySampler<FakeReading>? SamplerFor(LocatedModule module, FakeLayout layout) => MakeSampler(module, layout);

        /// <summary>A reading "WRONG" never agrees with the simulator.</summary>
        public bool Passes(IReadOnlyList<(long AtMs, FakeReading Reading)> history, Bbs757SimValues sim) =>
            history.Count > 0 && history[^1].Reading.Value != "WRONG";

        public bool? Lit(FakeReading reading, Bbs757SimValues sim) => LitOf is { } of ? of(reading) : LitNow;

        public IReadOnlyList<Bbs757SpokenLine> Changes(FakeReading heard, FakeReading now, bool withPrefix) =>
            new[] { new Bbs757SpokenLine("MUTE_X", "X: " + now) };

        public IReadOnlyList<string> Rows(FakeReading reading) => new[] { "X: " + reading };

        public string DescribeAll(FakeReading reading) => "X: " + reading;
    }

    internal sealed class FakeEnvironment : IBbs757DisplayEnvironment
    {
        private readonly List<string> _spoken = new();
        private readonly List<(string Channel, string Line)> _logged = new();
        private readonly object _gate = new();

        public FakeEnvironment()
        {
            Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { Build() }, null));
            Locate = () => Task.FromResult(new ModuleLocation(new[] { Located() }, null));
        }

        public long NowMs { get; set; }

        /// <summary>What every read of the simulator returns (null: disconnected, or another aircraft).</summary>
        public Bbs757SimValues? Simulator { get; set; } = new Bbs757SimValues(new Dictionary<string, double> { ["L:X_GATE"] = 1 });

        public List<string> KeysAsked { get; } = new();
        public HashSet<string> Muted { get; } = new();
        public Bbs757DisplayFilesNotice FilesNotice { get; } = new();

        public Func<Task<ModuleAnalysis>> Analyse { get; set; }
        public Func<Task<ModuleLocation>> Locate { get; set; }
        public int AnalyseCalls { get; private set; }
        public int LocateCalls => LocateTimes.Count;
        public List<long> LocateTimes { get; } = new();

        public string[] Spoken { get { lock (_gate) return _spoken.ToArray(); } }
        public (string Channel, string Line)[] Logged { get { lock (_gate) return _logged.ToArray(); } }

        public Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys)
        {
            KeysAsked.AddRange(keys);
            return Simulator;
        }

        public bool IsMuted(string muteKey) => Muted.Contains(muteKey);

        public void Speak(string text) { lock (_gate) _spoken.Add(text); }

        public void Log(string channel, string line) { lock (_gate) _logged.Add((channel, line)); }

        public Task<ModuleAnalysis> AnalyseAsync()
        {
            AnalyseCalls++;
            return Analyse();
        }

        public Task<ModuleLocation> LocateAsync()
        {
            LocateTimes.Add(NowMs);
            return Locate();
        }

        /// <summary>A 757 build; the fake reader does not look at it.</summary>
        public static ModuleBuild Build() =>
            new(Bbs757Module.Description, "BBS752.wasm", "BBS752.json", Array.Empty<ReaderResult>());

        /// <summary>A located build on its own shared handle; the fake reader's samplers do not read it.</summary>
        public static LocatedModule Located()
        {
            var source = new FakeProcessMemory();
            var handle = SharedHandle.Open(source, out _);
            return new LocatedModule(Build(), new ModuleAnchor(0, new byte[] { 1 }), new ModuleMemory(source, 0), handle);
        }
    }
}
