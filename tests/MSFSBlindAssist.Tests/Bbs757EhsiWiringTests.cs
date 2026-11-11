// Plan 26 Task 7: the EHSI reader on the 757 definition: its monitor, its "EHSI messages" Ctrl+M row, Alt+N, which opens
// the "Captain EHSI" window while the reader runs and falls back to the AI read when it cannot, and Ctrl+W, which says the
// active waypoint at once, interrupting, with the window open or not. The window is a real WinForms form, as in the
// upper EICAS wiring tests; the lit-display cases drive the real monitor over a scripted sampler.

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using static MSFSBlindAssist.Tests.Bbs757EhsiTextTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiWiringTests
{
    [Fact]
    public void Alt_N_opens_the_ehsi_window_while_the_reader_runs()
    {
        using var rig = new Rig(started: true);

        Assert.True(rig.Press(HotkeyAction.ReadDisplayND));

        Assert.Empty(rig.AiReads);
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        Assert.Equal("EHSI", form.Text);
        Assert.Equal("EHSI", form.AccessibleName);
        Assert.True(form.Visible);
        Assert.Empty(rig.Speech.All);   // the window's title and list are what the screen reader says (CORE-7)
    }

    [Fact]
    public void Alt_N_falls_back_to_the_ai_read_when_unavailable()
    {
        using var rig = new Rig(started: false);

        Assert.True(rig.Def.Ehsi.KeyFallsBackToAi());
        Assert.True(rig.Press(HotkeyAction.ReadDisplayND));

        Assert.Equal(new[] { HotkeyAction.ReadDisplayND }, rig.AiReads);
        Assert.Empty(rig.Def.Displays.Windows);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_second_Alt_N_shows_the_same_window_again()
    {
        using var rig = new Rig(started: true);
        rig.Press(HotkeyAction.ReadDisplayND);
        var first = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        first.Hide();

        rig.Press(HotkeyAction.ReadDisplayND);

        Assert.Same(first, Assert.Single(rig.Def.Displays.Windows));
        Assert.True(first.Visible);
    }

    [Fact]
    public void FullWanted_follows_the_windows_visibility()
    {
        using var rig = new Rig(started: true);
        Assert.False(rig.Def.EhsiDisplay.FullWanted());   // no window yet: the route is not read

        rig.Press(HotkeyAction.ReadDisplayND);
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        Assert.True(rig.Def.EhsiDisplay.FullWanted());

        form.Hide();
        Assert.False(rig.Def.EhsiDisplay.FullWanted());

        rig.Press(HotkeyAction.ReadDisplayND);
        Assert.True(rig.Def.EhsiDisplay.FullWanted());

        form.CloseForGood();
        Assert.False(rig.Def.EhsiDisplay.FullWanted());
    }

    [Fact]
    public void Ctrl_W_says_the_top_line_interrupting()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());

        Assert.True(rig.Press(HotkeyAction.ReadNDWaypoint));

        Assert.Equal(new[] { "KOLM, 37.5 NM, no ETA" }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Empty(rig.AiReads);
        GC.KeepAlive(f);
    }

    [Fact]
    public void Ctrl_W_answers_with_the_window_closed()
    {
        using var rig = new Rig(started: false);
        rig.Lit(Light());
        Assert.False(rig.Def.EhsiDisplay.FullWanted());
        Assert.Empty(rig.Def.Displays.Windows);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "KOLM, 37.5 NM, no ETA" }, rig.Speech.Interrupts);
        Assert.Empty(rig.Def.Displays.Windows);   // the key never opens the window
    }

    [Fact]
    public void Ctrl_W_still_answers_with_the_ctrl_m_row_muted()
    {
        using var rig = new Rig(started: false);
        rig.Lit(Light(), muted: true);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "KOLM, 37.5 NM, no ETA" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void Ctrl_W_while_the_display_is_dark_says_so()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());
        f.Show(Dark());
        f.Tick();

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "EHSI waypoint unavailable: display dark" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void Ctrl_W_names_the_monitors_state_while_the_reader_waits()
    {
        using var rig = new Rig(started: true);
        Assert.Equal("WaitingForSimulator", rig.Def.Ehsi.StateName);

        rig.Press(HotkeyAction.ReadNDWaypoint);
        Assert.Equal(new[] { "EHSI waypoint unavailable: waiting for the simulator" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void Ctrl_W_names_the_monitors_own_reason_once_it_is_unavailable()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());
        f.Env.Analyse = () => Task.FromResult(new ModuleAnalysis(Array.Empty<ModuleBuild>(), "no layout file"));
        f.Monitor.Stop();
        f.Monitor.Start(f.Env, startTimer: false);
        for (int i = 0; i < 40 && f.Monitor.StateName != "Unavailable"; i++) f.Tick();
        Assert.Equal("Unavailable", f.Monitor.StateName);
        string reason = Assert.IsType<string>(f.Monitor.NoReadingReason);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "EHSI waypoint unavailable: " + reason }, rig.Speech.Interrupts);
        Assert.NotEqual("display dark", reason);
    }

    [Fact]
    public void Ctrl_W_before_the_reader_started_says_no_reading_yet()
    {
        using var rig = new Rig(started: false);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "EHSI waypoint unavailable: no reading yet" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void The_ctrl_m_row_is_listed()
    {
        var def = new BBS757Definition();
        var rows = def.MonitorRows();

        var row = new MonitorRow(Bbs757EhsiDisplay.MuteKey, "EHSI messages");
        Assert.Contains(row, rows);
        Assert.Contains(row, def.Displays.Rows);
        Assert.Same(def.Ehsi, def.Displays.Monitors.Single(m => ReferenceEquals(m, def.Ehsi)));
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_two_position_inputs_ride_the_batch_once_and_stay_under_the_ceiling()
    {
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        foreach (var input in Bbs757EhsiDisplay.SimInputs)
        {
            var carrier = vars[keys[input.Key]];
            Assert.Equal((input.Name, SimVarType.SimVar, "degrees"), (carrier.Name, carrier.Type, carrier.Units));
            Assert.Equal(UpdateFrequency.Continuous, carrier.UpdateFrequency);
            Assert.True(carrier.IsAnnounced);
            Assert.Single(vars, kv => kv.Value.Name == input.Name && ContinuousBatchLayout.RidesBatch(kv.Value));
            if (keys[input.Key].StartsWith(BBS757Definition.DisplayInputCopyPrefix, StringComparison.Ordinal))
                Assert.True(carrier.ExcludeFromMonitorManager);   // a hidden copy is never listed in Ctrl+M ([VAR-9])
        }
        Assert.True(vars.Count < 900, $"{vars.Count} variables");   // the data-definition ceiling's test
    }

    // ---- the rig ----

    private sealed class Rig : IDisposable
    {
        public BBS757Definition Def { get; private set; }
        public SpeechCapture Speech { get; } = new();
        public List<HotkeyAction> AiReads { get; } = new();
        public HotkeyManager Hotkeys { get; } = new();
        private readonly SimConnectManager _sim = new(IntPtr.Zero);
        private readonly Form _parent = new();
        private readonly bool _started;

        public Rig(bool started)
        {
            _started = started;
            Def = Make(null, null);
            if (started) Def.StartDisplays(new NoFiles(), new NoLayouts(), _sim, Speech);
            else _ = Def.Displays;
        }

        private BBS757Definition Make(Bbs757EhsiDisplay? display, Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>? monitor)
        {
            var def = display is null
                ? new BBS757Definition { StartMonitor = StartWithoutTimer, NestRunning = () => true }
                : new BBS757Definition { StartMonitor = StartWithoutTimer, NestRunning = () => true, EhsiDisplay = display, Ehsi = monitor! };
            def.AiReadOverride = a => { AiReads.Add(a); return true; };
            return def;
        }

        /// <summary>Replaces the definition's EHSI monitor with the real one over a scripted sampler, already running on
        /// <paramref name="first"/>.</summary>
        public Flight Lit(Bbs757EhsiReading first, bool muted = false)
        {
            Def.OnSwitchedAway();
            var flight = new Flight(first, muted);
            Def = Make(flight.Display, flight.Monitor);
            _ = Def.Displays;
            flight.RunUntilRunning();
            return flight;
        }

        public bool Press(HotkeyAction action) => Def.HandleHotkeyAction(action, _sim, Speech, _parent, Hotkeys);

        public void Dispose()
        {
            Def.OnSwitchedAway();
            _parent.Dispose();
            Hotkeys.Dispose();
        }
    }

    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        switch (monitor)
        {
            case Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma: fma.Start(env, startTimer: false); break;
            case Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> eicas: eicas.Start(env, startTimer: false); break;
            case Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> ehsi: ehsi.Start(env, startTimer: false); break;
            default: monitor.Start(env); break;
        }
    }

    private static readonly Bbs757EhsiLayout Layout = Bbs757EhsiLayoutTests.Sample();

    /// <summary>The real EHSI display and monitor over a scripted sampler (whatever the module), ticked every 500 ms.</summary>
    private sealed class Flight
    {
        public Flight(Bbs757EhsiReading first, bool muted)
        {
            Sampler = new Sampler(first);
            Monitor = new Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>(new OverScript(Display, Sampler));
            Env = new Bbs757DisplayMonitorTests.FakeEnvironment
            {
                Simulator = new Bbs757SimValues(new Dictionary<string, double>
                {
                    ["PLANE LATITUDE"] = 47.442946, ["PLANE LONGITUDE"] = -122.303648,
                }),
                Analyse = () => Task.FromResult(new ModuleAnalysis(new[]
                {
                    new ModuleBuild(Bbs757Module.Description, "BBS752.wasm", "BBS752.json",
                        new[] { new ReaderResult(Bbs757Module.Ehsi, Layout, null) }),
                }, null)),
            };
            if (muted) Env.Muted.Add(Bbs757EhsiDisplay.MuteKey);
            Monitor.Start(Env, startTimer: false);
        }

        public Bbs757EhsiDisplay Display { get; } = new();
        public Sampler Sampler { get; }
        public Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> Monitor { get; }
        public Bbs757DisplayMonitorTests.FakeEnvironment Env { get; }

        public void Show(Bbs757EhsiReading reading) => Sampler.Reading = reading;

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += Bbs757EhsiDisplay.TickMs;
            }
        }

        public void RunUntilRunning(int maxTicks = 20)
        {
            for (int i = 0; i < maxTicks && Monitor.StateName != "Running"; i++) Tick();
            Assert.Equal("Running", Monitor.StateName);
        }
    }

    private sealed class Sampler(Bbs757EhsiReading reading) : IBbs757DisplaySampler<Bbs757EhsiReading>
    {
        public Bbs757EhsiReading Reading { get; set; } = reading;

        public bool TryRead(Bbs757SimValues sim, out Bbs757EhsiReading read) => TryRead(out read);

        public bool TryRead(out Bbs757EhsiReading read)
        {
            read = Reading;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class OverScript(Bbs757EhsiDisplay real, Sampler sampler) : IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>
    {
        private readonly IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading> _real = real;

        public string Name => _real.Name;
        public string MuteKey => _real.MuteKey;
        public string LogChannel => _real.LogChannel;
        public int TickMs => _real.TickMs;
        public IReadOnlyList<Bbs757SimInput> SimInputs => _real.SimInputs;
        public IReadOnlyList<Bbs757EhsiLayout> LayoutsIn(ModuleAnalysis analysis) => _real.LayoutsIn(analysis);
        public IBbs757DisplaySampler<Bbs757EhsiReading>? SamplerFor(LocatedModule module, Bbs757EhsiLayout layout) => sampler;
        public bool? Lit(Bbs757EhsiReading reading) => _real.Lit(reading);

        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757EhsiReading heard, Bbs757EhsiReading now, bool withPrefix) =>
            _real.Changes(heard, now, withPrefix);

        public IReadOnlyList<string> Rows(Bbs757EhsiReading reading) => _real.Rows(reading);
    }

    private sealed class NoFiles : IModuleHost
    {
        public long NowMs => 0;
        public string SimulatorVersion => "FS2024";
        public IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log) => Array.Empty<string>();
        public byte[] ReadFile(string path) => throw new InvalidOperationException($"the session read {path}");

        public IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure)
        {
            failure = null;
            return null;
        }

        public void Log(string line) { }

        public Task<T> RunInBackground<T>(string name, Func<T> work)
        {
            try { return Task.FromResult(work()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }
    }

    private sealed class NoLayouts : ILayoutSource
    {
        public LayoutLookup Find(string wasmFile) => new(null, "no layout");
    }
}
