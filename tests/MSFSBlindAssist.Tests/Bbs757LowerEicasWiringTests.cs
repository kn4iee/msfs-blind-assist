// The 757 definition's lower EICAS seam (plan 25 Task 6): the monitor, its Ctrl+M row, its gate variables on the batch, and
// Alt+S, which opens the one "Lower EICAS" window while the reader runs and falls back to the AI read when it cannot.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Bbs757LowerEicasWiringTests
{
    private static Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> LowerMonitor(BBS757Definition def) =>
        def.Displays.Monitors.OfType<Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>>().Single();

    // Starts every monitor without its WinForms timer (the tests never tick them).
    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        if (monitor is Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> lower) lower.Start(env, startTimer: false);
        else if (monitor is Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma) fma.Start(env, startTimer: false);
    }

    [Fact]
    public void Alt_S_opens_the_lower_eicas_window_while_the_reader_runs()
    {
        var def = new BBS757Definition { StartMonitor = StartWithoutTimer };
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        def.StartDisplays(new FakeHost(), new NoLayouts(), new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        var monitor = LowerMonitor(def);
        Assert.False(monitor.KeyFallsBackToAi());   // started: it waits for the simulator, and the window says so

        try
        {
            Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayLowerECAM, new SimConnectManager(IntPtr.Zero),
                new SpeechCapture(), null!, null!));

            Assert.Empty(aiReads);
            var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(def.Displays.Windows));
            Assert.Equal("Lower EICAS", window.Text);
            Assert.True(window.Visible);

            // Again: the same window, made once.
            window.Hide();
            Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayLowerECAM, new SimConnectManager(IntPtr.Zero),
                new SpeechCapture(), null!, null!));
            Assert.Same(window, Assert.Single(def.Displays.Windows));
            Assert.True(window.Visible);
            Assert.Empty(aiReads);
        }
        finally
        {
            def.OnSwitchedAway();
        }
        Assert.All(def.Displays.Windows, w => Assert.True(w.IsDisposed));   // owned: a switch away closes it for good
    }

    [Fact]
    public void Alt_S_falls_back_to_the_ai_read_when_unavailable()
    {
        var def = new BBS757Definition();   // the readers never started: the AI read
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        Assert.True(LowerMonitor(def).KeyFallsBackToAi());

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayLowerECAM, new SimConnectManager(IntPtr.Zero),
            new SpeechCapture(), null!, null!));

        Assert.Equal(new[] { HotkeyAction.ReadDisplayLowerECAM }, aiReads);
        Assert.Empty(def.Displays.Windows);   // no window made
    }

    [Fact]
    public void The_ctrl_m_row_is_listed()
    {
        var def = new BBS757Definition();

        var row = new MonitorRow(Bbs757LowerEicasDisplay.MuteKey, "EICAS status messages");

        Assert.Contains(row, def.Displays.Rows);
        Assert.Contains(row, def.MonitorRows());
    }

    [Fact]
    public void The_gate_variables_are_registered_hidden_and_counted()
    {
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        foreach (string key in Bbs757LowerEicasDisplay.GateKeys)
            Assert.Contains(key, def.Displays.GateVariables.Select(g => g.Key));

        // Every gate variable resolves to one batched variable, hidden from Ctrl+M when it is a copy ([VAR-7], [VAR-9]).
        var copies = new List<string>();
        foreach (string key in Bbs757LowerEicasDisplay.GateKeys)
        {
            var cacheKey = keys[key];
            Assert.StartsWith("BBS757_", cacheKey);
            if (cacheKey.StartsWith(BBS757Definition.DisplayGateCopyPrefix, StringComparison.Ordinal))
            {
                copies.Add(cacheKey);
                Assert.True(vars[cacheKey].ExcludeFromMonitorManager);
            }
        }
        // The count this plan adds (the plan allows at most 8).
        Assert.Equal(new[]
        {
            BBS757Definition.DisplayGateCopyPrefix + Bbs757LowerEicasGate.LeftN2Key,
            BBS757Definition.DisplayGateCopyPrefix + Bbs757LowerEicasGate.RightN2Key,
            BBS757Definition.DisplayGateCopyPrefix + Bbs757LowerEicasGate.PageKey,
        }, copies);
        Assert.Equal(SimVarType.SimVar, vars[copies[0]].Type);
        Assert.Equal("percent", vars[copies[0]].Units);
        Assert.Equal(SimVarType.LVar, vars[copies[2]].Type);
        Assert.Single(vars.Values, v => v.Name == "BBS_ECAM_Mode" && ContinuousBatchLayout.RidesBatch(v));
    }

    [Fact]
    public void The_n2_gate_copies_stream_off_the_batch_because_the_engines_panel_already_streams_those_names()
    {
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        foreach (var (gateKey, rowKey) in new[]
                 {
                     (Bbs757LowerEicasGate.LeftN2Key, "BBS757_ENG1_N2"), (Bbs757LowerEicasGate.RightN2Key, "BBS757_ENG2_N2"),
                 })
        {
            var row = vars[rowKey];   // the Engines panel's silent row: Continuous, not announced, so never on the batch
            Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
            Assert.False(ContinuousBatchLayout.RidesBatch(row));
            var copy = vars[keys[gateKey]];
            Assert.Equal(row.Name, copy.Name);
            Assert.True(copy.ExcludeFromBatch);
            Assert.False(ContinuousBatchLayout.RidesBatch(copy));
        }
        // The page copy has no such twin: it rides the batch.
        Assert.False(vars[keys[Bbs757LowerEicasGate.PageKey]].ExcludeFromBatch);
    }

    private sealed class FakeHost : IModuleHost
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
