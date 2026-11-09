// The 757 upper EICAS as one reader of the 757's display monitor: its names, rows and log, its gate variables, its
// layouts and samplers from the shared module session, its gate and power, and its words; then the monitor driving the
// real reader over scripted readings, so the settle, the power-up quiet and the Ctrl+M rows act as the pilot hears them.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757UpperEicasSpeechTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757UpperEicasDisplayTests
{
    private const ulong Base = 0x20000000;

    private static readonly Bbs757UpperEicasLayout Layout = Bbs757UpperEicasLayoutTests.Sample();

    private static IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> Reader() => new Bbs757UpperEicasDisplay();

    private static Bbs757SimValues Sim(double left = 19.93, double right = 19.93, double manual = 0) =>
        new(new Dictionary<string, double>
        {
            ["TURB ENG N1:1"] = left,
            ["TURB ENG N1:2"] = right,
            ["BBS757_THRREF_PULL"] = manual,
        });

    private static ModuleBuild Build(Bbs757UpperEicasLayout? layout, string layoutFile = "BBS752.json") =>
        new(Bbs757Module.Description, "BBS752.wasm", layoutFile,
            layout is null ? Array.Empty<ReaderResult>() : new[] { new ReaderResult(Bbs757Module.UpperEicas, layout, null) });

    private sealed class CountingHandle : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public void The_reader_is_named_eicas_on_its_own_rows_and_log()
    {
        Assert.Equal("EICAS", Bbs757UpperEicasDisplay.Name);
        Assert.Equal("bbs757-upper-eicas", Bbs757UpperEicasDisplay.LogChannel);
        Assert.Equal(250, Bbs757UpperEicasDisplay.TickMs);
        Assert.Equal("MSFSBA_BBS757_EICAS_MESSAGES", Bbs757UpperEicasDisplay.MessagesKey);
        Assert.Equal("EICAS messages", Bbs757UpperEicasDisplay.MessagesLabel);
        Assert.Equal("MSFSBA_BBS757_THRUST_REFERENCE", Bbs757UpperEicasDisplay.ThrustKey);
        Assert.Equal("Thrust reference", Bbs757UpperEicasDisplay.ThrustLabel);
        Assert.Equal("MSFSBA_BBS757_ENGINE_COLORS", Bbs757UpperEicasDisplay.ColorsKey);
        Assert.Equal("Engine gauge colors", Bbs757UpperEicasDisplay.ColorsLabel);
        // The status lines go with the messages row.
        Assert.Equal(Bbs757UpperEicasDisplay.MessagesKey, Bbs757UpperEicasDisplay.MuteKey);

        var reader = Reader();
        Assert.Equal("EICAS", reader.Name);
        Assert.Equal("MSFSBA_BBS757_EICAS_MESSAGES", reader.MuteKey);
        Assert.Equal("bbs757-upper-eicas", reader.LogChannel);
        Assert.Equal(250, reader.TickMs);
    }

    [Fact]
    public void The_gate_reads_both_engines_n1_and_the_manual_thrust_flag()
    {
        var keys = new[] { "TURB ENG N1:1", "TURB ENG N1:2", "BBS757_THRREF_PULL" };
        Assert.Equal(keys, Bbs757UpperEicasDisplay.GateKeys);
        Assert.Equal(keys, Reader().GateKeys);
        Assert.False(Bbs757UpperEicasDisplay.GateKeys is string[], "the keys cannot be changed through a cast");

        // The N1s are the stock variables in percent ([VAR-2]), the manual flag BlueBird's L:var, each under its key.
        var variables = new[]
        {
            new Bbs757GateVariable("TURB ENG N1:1", "TURB ENG N1:1", SimVarType.SimVar, "percent"),
            new Bbs757GateVariable("TURB ENG N1:2", "TURB ENG N1:2", SimVarType.SimVar, "percent"),
            new Bbs757GateVariable("BBS757_THRREF_PULL", "BBS757_THRREF_PULL", SimVarType.LVar, null),
        };
        Assert.Equal(variables, Reader().GateVariables);
        Assert.Equal(keys, Reader().GateVariables.Select(v => v.Key));
        Assert.Equal(variables, new Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>(new Bbs757UpperEicasDisplay()).GateVariables);
    }

    [Fact]
    public void Passes_is_the_upper_eicas_gate()
    {
        var history = new[] { (0L, Eicas()) };
        Assert.True(Reader().Passes(history, Sim()));
        Assert.False(Reader().Passes(history, Sim(right: 33.2)));
    }

    [Fact]
    public void Layouts_are_each_builds_upper_eicas_layout_once_in_file_order()
    {
        var other = Layout with { ToTat = 332 };
        var analysis = new ModuleAnalysis(new[]
        {
            Build(Layout, "a.json"), Build(null, "b.json"), Build(other, "c.json"), Build(Layout with { SourceFile = "x" }, "d.json"),
        }, null);
        Assert.Equal(new[] { Layout, other }, Reader().LayoutsIn(analysis));
        Assert.Empty(Reader().LayoutsIn(new ModuleAnalysis(Array.Empty<ModuleBuild>(), "BlueBird 757 files not found")));
    }

    [Fact]
    public void A_sampler_reads_the_builds_own_layout_on_its_own_lease()
    {
        var handle = new CountingHandle();
        var shared = SharedHandle.Open(handle, out var sessionLease);
        var bytes = new byte[0x1000];
        var source = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        var module = new LocatedModule(Build(Layout), new ModuleAnchor(Layout.AnchorAddress, Layout.Anchor),
            new ModuleMemory(source, Base), shared);

        // Another build's layout: no sampler, and no lease taken.
        Assert.Null(Reader().SamplerFor(module, Layout with { ToTat = 332 }));

        var sampler = Reader().SamplerFor(module, Layout);
        var reader = Assert.IsType<Bbs757UpperEicasReader>(sampler);
        Assert.Equal(Layout, reader.Layout);
        // This module holds no upper EICAS: not found, the monitor's "memory not found".
        Assert.False(reader.TryRead(Sim(), out _));

        // The sampler's lease keeps the handle open after the session lets go; disposing it closes the handle.
        sessionLease.Dispose();
        Assert.Equal(0, handle.Disposed);
        reader.Dispose();
        Assert.Equal(1, handle.Disposed);

        // A closed handle: the monitor's "the simulator handle closed during the memory search".
        Assert.Throws<ObjectDisposedException>(() => Reader().SamplerFor(module, Layout));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Each_line_is_on_its_rows_key_and_keeps_its_words_after_resumed(string culture)
    {
        var heard = Eicas(new[] { ParkingBrake }, thrust: "TO");
        var now = Gauge(Eicas(new[] { ShutdownL, ParkingBrake }, thrust: "CLB"), V("EGT", "800", "360", "amber"));
        var lines = new[]
        {
            new Bbs757SpokenLine("MSFSBA_BBS757_EICAS_MESSAGES", "EICAS caution: L ENG SHUTDOWN"),
            new Bbs757SpokenLine("MSFSBA_BBS757_THRUST_REFERENCE", "Thrust reference: CLB"),
            new Bbs757SpokenLine("MSFSBA_BBS757_ENGINE_COLORS", "EICAS: L EGT amber"),
        };
        // R32: "EICAS caution" names the display, it is not a reader prefix; the words are the same after "resumed".
        Assert.Equal(lines, TestCulture.Run(culture, () => Reader().Changes(heard, now, withPrefix: true)));
        Assert.Equal(lines, TestCulture.Run(culture, () => Reader().Changes(heard, now, withPrefix: false)));
        Assert.Empty(TestCulture.Run(culture, () => Reader().Changes(now, now, withPrefix: true)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Rows_and_describe_all_are_the_upper_eicas_speech(string culture)
    {
        var reading = Eicas(new[] { ShutdownL, ParkingBrake });
        Assert.Equal(TestCulture.Run(culture, () => Bbs757UpperEicasSpeech.Rows(reading)), TestCulture.Run(culture, () => Reader().Rows(reading)));
        Assert.Equal("EPR: 1.03, 1.03. N1: 19.9, 19.9. EGT: 358, 360. Thrust reference: TO. EPR reference: 1.71, 1.71. " +
            "TAT: +11c. Caution: L ENG SHUTDOWN. Advisory: PARKING BRAKE.", TestCulture.Run(culture, () => Reader().DescribeAll(reading)));
    }

    // ---- through the monitor ----

    /// <summary>The sampler a scripted flight reads: whatever <see cref="Reading"/> holds, recording the simulator's values
    /// each read is given (S10).</summary>
    private sealed class ScriptedSampler(Bbs757UpperEicasReading reading) : IBbs757DisplaySampler<Bbs757UpperEicasReading>
    {
        public Bbs757UpperEicasReading Reading { get; set; } = reading;

        /// <summary>The simulator's values of each read the monitor made, oldest first.</summary>
        public List<Bbs757SimValues> Seen { get; } = new();

        public bool TryRead(Bbs757SimValues sim, out Bbs757UpperEicasReading read)
        {
            Seen.Add(sim);
            return TryRead(out read);
        }

        public bool TryRead(out Bbs757UpperEicasReading read)
        {
            read = Reading;
            return true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>The real reader in every member but its sampler, which is <paramref name="sampler"/> whatever the module.</summary>
    private sealed class OverScript(ScriptedSampler sampler) : IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>
    {
        private readonly IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> _real = new Bbs757UpperEicasDisplay();

        public string Name => _real.Name;
        public string MuteKey => _real.MuteKey;
        public string LogChannel => _real.LogChannel;
        public int TickMs => _real.TickMs;
        public IReadOnlyList<string> GateKeys => _real.GateKeys;
        public IReadOnlyList<Bbs757GateVariable> GateVariables => _real.GateVariables;

        public IReadOnlyList<Bbs757UpperEicasLayout> LayoutsIn(ModuleAnalysis analysis) => _real.LayoutsIn(analysis);

        public IBbs757DisplaySampler<Bbs757UpperEicasReading>? SamplerFor(LocatedModule module, Bbs757UpperEicasLayout layout) => sampler;

        public bool Passes(IReadOnlyList<(long AtMs, Bbs757UpperEicasReading Reading)> history, Bbs757SimValues sim) => _real.Passes(history, sim);

        public bool? Lit(Bbs757UpperEicasReading reading, Bbs757SimValues sim) => _real.Lit(reading, sim);

        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757UpperEicasReading heard, Bbs757UpperEicasReading now, bool withPrefix) =>
            _real.Changes(heard, now, withPrefix);

        public IReadOnlyList<string> Rows(Bbs757UpperEicasReading reading) => _real.Rows(reading);

        public string DescribeAll(Bbs757UpperEicasReading reading) => _real.DescribeAll(reading);
    }

    /// <summary>One flight: the monitor on the real reader, the session's build with the upper EICAS layout, the
    /// simulator's N1 as memory's, a tick every 250 ms (the reader's own interval).</summary>
    private sealed class Flight
    {
        public Flight(Bbs757UpperEicasReading first, params string[] muted)
        {
            Sampler = new ScriptedSampler(first);
            Monitor = new Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>(new OverScript(Sampler));
            Env = new Bbs757DisplayMonitorTests.FakeEnvironment
            {
                Simulator = Sim(),
                Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { Build(Layout) }, null)),
            };
            foreach (string key in muted) Env.Muted.Add(key);
            Monitor.Start(Env, startTimer: false);
        }

        public ScriptedSampler Sampler { get; }
        public Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> Monitor { get; }
        public Bbs757DisplayMonitorTests.FakeEnvironment Env { get; }
        public string State => Monitor.StateName;
        public string[] Spoken => Env.Spoken;

        public void Show(Bbs757UpperEicasReading reading) => Sampler.Reading = reading;

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += Bbs757UpperEicasDisplay.TickMs;
            }
        }

        public Flight RunUntilRunning(int maxTicks = 20)
        {
            for (int i = 0; i < maxTicks && State != "Running"; i++) Tick();
            Assert.Equal("Running", State);
            return this;
        }
    }

    // Review Focus: a message that shows for less than the 300 ms settle (two 250 ms ticks) and goes, or one that goes
    // and comes back as quickly, says nothing; one that holds is said once.
    [Fact]
    public void A_message_that_flickers_inside_the_settle_window_is_not_spoken()
    {
        var steady = Eicas(new[] { BleedL, ParkingBrake });
        var f = new Flight(steady).RunUntilRunning();
        f.Tick(4);
        Assert.Empty(f.Spoken);

        // On the screen for two ticks: the window shows it at once, but it is not said.
        f.Show(Eicas(new[] { ShutdownL, BleedL, ParkingBrake }));
        f.Tick();
        Assert.Contains("Caution: L ENG SHUTDOWN", f.Monitor.View());
        f.Tick();
        f.Show(steady);
        f.Tick(8);
        Assert.Empty(f.Spoken);

        // Gone for two ticks and back: no "gone".
        f.Show(Eicas(new[] { ParkingBrake }));
        f.Tick(2);
        f.Show(steady);
        f.Tick(8);
        Assert.Empty(f.Spoken);

        // Held past the settle: said once.
        f.Show(Eicas(new[] { ShutdownL, BleedL, ParkingBrake }));
        f.Tick(2);
        Assert.Empty(f.Spoken);
        f.Tick();
        Assert.Equal(new[] { "EICAS caution: L ENG SHUTDOWN" }, f.Spoken);
        f.Tick(20);
        Assert.Equal(new[] { "EICAS caution: L ENG SHUTDOWN" }, f.Spoken);
        Assert.Equal("Running", f.State);
    }

    // Review Focus: the messages BlueBird posts as the screen lights are what a pilot sees on any power-up, not changes:
    // after the 10 s quiet the first trusted reading is a silent baseline, at a cold start and after a power loss.
    [Fact]
    public void Powering_up_makes_the_first_messages_a_silent_baseline()
    {
        var f = new Flight(Eicas(powered: false));
        f.Tick(40); // ten seconds cold and dark
        Assert.Equal("Verifying", f.State);
        Assert.Equal(new[] { "EICAS reader starting" }, f.Monitor.View());

        var lit = Eicas(new[] { ShutdownL, ShutdownR, HydPress, BleedL, BleedR, ParkingBrake });
        f.Show(lit);
        f.Tick(40); // lit, still in its 10 s quiet
        Assert.Equal("Verifying", f.State);
        f.Tick(); // 10 s lit: trusted, a silent baseline
        Assert.Equal("Running", f.State);
        f.Tick(20);
        Assert.Empty(f.Spoken);
        Assert.Contains("Caution: HYD SYS PRESS", f.Monitor.View());

        // The screen goes dark in flight and lights again with other messages: silent again.
        f.Show(Eicas(new[] { ShutdownL, ShutdownR }, powered: false));
        f.Tick(8);
        Assert.Equal(new[] { "EICAS display not powered" }, f.Monitor.View());
        var relit = Eicas(new[] { ShutdownL, ShutdownR, PackL, PackR }, thrust: "CLB");
        f.Show(relit);
        f.Tick(40);
        Assert.Equal(new[] { "EICAS reader starting" }, f.Monitor.View());
        f.Tick(20);
        Assert.Empty(f.Spoken);
        Assert.Contains("Advisory: L PACK OFF", f.Monitor.View());

        // Later changes are spoken.
        f.Show(Eicas(new[] { FireL, ShutdownL, ShutdownR, PackL, PackR }, thrust: "CLB"));
        f.Tick(3);
        Assert.Equal(new[] { "EICAS warning: L ENGINE FIRE" }, f.Spoken);
    }

    [Fact]
    public void A_muted_thrust_row_says_no_thrust_line()
    {
        var f = new Flight(Eicas(new[] { ParkingBrake }, thrust: "TO"), Bbs757UpperEicasDisplay.ThrustKey).RunUntilRunning();
        f.Show(Eicas(new[] { ParkingBrake }, thrust: "CLB"));
        f.Tick(10);
        Assert.Empty(f.Spoken);

        // A message with it: the message alone.
        f.Show(Eicas(new[] { ShutdownL, ParkingBrake }, thrust: "CRZ"));
        f.Tick(10);
        Assert.Equal(new[] { "EICAS caution: L ENG SHUTDOWN" }, f.Spoken);
        // The window still shows it.
        Assert.Contains("Thrust reference: CRZ", f.Monitor.View());
    }

    [Theory]
    [InlineData("")]
    [InlineData("MSFSBA_BBS757_EICAS_MESSAGES")]
    [InlineData("MSFSBA_BBS757_THRUST_REFERENCE")]
    [InlineData("MSFSBA_BBS757_ENGINE_COLORS")]
    public void Each_muted_row_silences_its_lines_and_leaves_the_others(string muted)
    {
        var f = new Flight(Eicas(new[] { ParkingBrake }, thrust: "TO"), muted.Length == 0 ? Array.Empty<string>() : new[] { muted })
            .RunUntilRunning();
        f.Show(Gauge(Eicas(new[] { ShutdownL, ParkingBrake }, thrust: "CLB"), V("EGT", "800", "360", "amber")));
        f.Tick(10);
        var all = new[]
        {
            ("MSFSBA_BBS757_EICAS_MESSAGES", "EICAS caution: L ENG SHUTDOWN"),
            ("MSFSBA_BBS757_THRUST_REFERENCE", "Thrust reference: CLB"),
            ("MSFSBA_BBS757_ENGINE_COLORS", "EICAS: L EGT amber"),
        };
        Assert.Equal(all.Where(line => line.Item1 != muted).Select(line => line.Item2), f.Spoken);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("MSFSBA_BBS757_EICAS_MESSAGES", false)]
    [InlineData("MSFSBA_BBS757_THRUST_REFERENCE", true)]
    [InlineData("MSFSBA_BBS757_ENGINE_COLORS", true)]
    public void The_messages_row_also_mutes_the_status_lines(string muted, bool said)
    {
        var f = new Flight(Eicas(new[] { ParkingBrake }), muted.Length == 0 ? Array.Empty<string>() : new[] { muted }).RunUntilRunning();
        f.Env.Simulator = Sim(right: 40); // the simulator's right N1 disagrees with memory for longer than 2 s
        f.Tick(12);
        Assert.Equal("Mismatched", f.State);
        Assert.Equal(said ? new[] { "EICAS announcements stopped: values do not match the simulator" } : Array.Empty<string>(), f.Spoken);
    }

    // Review Focus (R7, R42): the measured engine starts. At light-off the simulator's N1 jumps for about a second (right
    // 33.2 % while memory holds 26.3 %, left 32.7 % against 26.2 %) and otherwise matches memory. No reading in the window
    // comes within 2.5 %, so the gate fails those ticks; the monitor's 2 s grace (MismatchToleranceMs) carries them: the
    // reader stays running and says no "stopped" at either start.
    [Fact]
    public void An_engine_start_light_off_says_no_stopped()
    {
        var right = new EngineStart(left: false, other: 0);
        right.Rise(26.3);
        right.Jump(26.3, 33.2, ticks: 4);
        right.Settle(26.3);
        var left = new EngineStart(left: true, other: 19.93, after: right);
        left.Rise(26.2);
        left.Jump(26.2, 32.7, ticks: 4);
        left.Settle(26.2);
        Assert.Equal(4, right.Failing); // the gate failed every jump tick of each start...
        Assert.Equal(4, left.Failing);
        Assert.Equal("Running", right.Flight.State); // ...and the reader ran throughout, silent
        Assert.Empty(right.Flight.Spoken);

        // The same jump held for 2 s stops: the grace, not the gate, carries a light-off.
        var held = new EngineStart(left: false, other: 0);
        held.Rise(26.3);
        held.Jump(26.3, 33.2, ticks: 7); // 1.75 s since the last good reading: still running
        Assert.Empty(held.Flight.Spoken);
        held.Jump(26.3, 33.2, ticks: 1, running: false); // 2 s
        Assert.Equal("Mismatched", held.Flight.State);
        Assert.Equal(new[] { "EICAS announcements stopped: values do not match the simulator" }, held.Flight.Spoken);
    }

    /// <summary>One engine starting, on the flight of the start <c>after</c> or else a new one with both engines off; the
    /// other engine at <c>other</c> % N1 in memory and the simulator alike; one 250 ms tick per value. It keeps the
    /// monitor's window beside it (shared with the start before) to count the ticks the gate fails.</summary>
    private sealed class EngineStart
    {
        private readonly bool _left;
        private readonly double _other;
        private readonly List<(long AtMs, Bbs757UpperEicasReading Reading)> _history;

        public EngineStart(bool left, double other, EngineStart? after = null)
        {
            _left = left;
            _other = other;
            _history = after?._history ?? new();
            if (after is null)
            {
                Flight = new Flight(At(0, 0));
                Flight.Env.Simulator = Sim(left: 0, right: 0);
                Flight.RunUntilRunning();
            }
            else
            {
                Flight = after.Flight;
            }
        }

        public Flight Flight { get; }

        /// <summary>The ticks the gate failed.</summary>
        public int Failing { get; private set; }

        /// <summary>N1 rising to light-off, memory and the simulator agreeing.</summary>
        public void Rise(double lightOff)
        {
            foreach (double n1 in new[] { 5.0, 10.0, 15.0, 20.0, 24.0, lightOff }) Tick(n1, n1);
        }

        /// <summary>Memory holding <paramref name="memory"/> while the simulator reads <paramref name="simulator"/>.</summary>
        public void Jump(double memory, double simulator, int ticks, bool running = true)
        {
            for (int i = 0; i < ticks; i++) Tick(memory, simulator, running);
        }

        /// <summary>N1 settling from light-off to idle, memory and the simulator agreeing.</summary>
        public void Settle(double lightOff)
        {
            foreach (double n1 in new[] { lightOff, 25.0, 23.0, 21.0, 19.93, 19.93, 19.93, 19.93 }) Tick(n1, n1);
        }

        private Bbs757UpperEicasReading At(double left, double right) => Eicas() with { GateN1Left = left, GateN1Right = right };

        private void Tick(double memory, double simulator, bool running = true)
        {
            var reading = _left ? At(memory, _other) : At(_other, memory);
            var sim = _left ? Sim(simulator, _other) : Sim(_other, simulator);
            long now = Flight.Env.NowMs;
            _history.Add((now, reading));
            _history.RemoveAll(h => now - h.AtMs > Bbs757UpperEicasGate.WindowMs);
            if (!Bbs757UpperEicasGate.Passes(_history, sim)) Failing++;
            Flight.Show(reading);
            Flight.Env.Simulator = sim;
            Flight.Tick();
            if (running) Assert.Equal("Running", Flight.State);
        }
    }

    [Fact]
    public void The_sampler_is_given_the_manual_thrust_flag_through_the_monitor()
    {
        // S10: BlueBird's draw takes L:BBS757_THRREF_PULL, so each read the monitor makes carries this tick's value (the
        // reader decodes MAN from it, Bbs757UpperEicasReaderTests), beside the gate's N1s.
        var f = new Flight(Eicas()).RunUntilRunning();
        Assert.NotEmpty(f.Sampler.Seen);
        Assert.All(f.Sampler.Seen, sim => Assert.Equal(0, sim["BBS757_THRREF_PULL"]));
        Assert.Contains("BBS757_THRREF_PULL", f.Env.KeysAsked);

        f.Env.Simulator = Sim(manual: 1);
        int reads = f.Sampler.Seen.Count;
        f.Tick();
        Assert.Equal(reads + 1, f.Sampler.Seen.Count);
        var seen = f.Sampler.Seen[^1];
        Assert.Equal(1, seen["BBS757_THRREF_PULL"]);
        Assert.Equal(19.93, seen["TURB ENG N1:1"]);
        Assert.Equal(19.93, seen["TURB ENG N1:2"]);
    }
}
