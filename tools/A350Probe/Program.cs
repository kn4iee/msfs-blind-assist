using System.Globalization;
using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

// Live harness for the iniBuilds A350: runs MSFSBA's real SimConnectManager and IniBuildsA350Definition and calls
// HandleUIVariableSet exactly as the panel does. See README.md before running it against a sim.
//
// A350Probe list [panel]                         - offline: every offered row (panel, key, action, name, positions)
// A350Probe live <panel|key,key> [watch,watch]       - every position of every row (presses too), read back, restore;
//                                                  logs the panel's lights and the watched L:vars that changed
// A350Probe set "key=val[#ms];..." [watch,...]   - set, read back, restore
// A350Probe keep "key=val[#ms];..." [watch,...]  - set and read back, no restore
// A350Probe cache "key,key"                     - MSFSBA's cached value, a fresh read, and how each key streams
// A350Probe disp <panel> [watch,...]             - the panel's status lines and the watched L:vars, read fresh
// A350Probe rpn "code#ms;code#ms" [watch,...]  - raw calculator strings, paced, then the watched L:vars
// A350Probe listen <seconds>                     - what MSFSBA would say on its own meanwhile (lights, switches)
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The app copies the versioned SimConnect DLL to SimConnect.dll at startup; the managed wrapper loads that name.
        string dir = AppContext.BaseDirectory, target = Path.Combine(dir, "SimConnect.dll");
        if (!File.Exists(target))
            foreach (var name in new[] { "SimConnect_msfs_2024.dll", "SimConnect_msfs_2020.dll" })
                if (File.Exists(Path.Combine(dir, name))) { File.Copy(Path.Combine(dir, name), target); break; }
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = args.Length > 0 ? args[0] : "list";
        string? arg = args.Length > 1 ? args[1] : null;
        var watch = args.Length > 2 ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        ApplicationConfiguration.Initialize();
        var f = new ProbeForm(mode, arg, watch);
        Application.Run(f);
        return f.ExitCode;
    }
}

/// <summary>The real definition plus OnRequest L:vars to read back downstream effects (key = "W:" + name).</summary>
internal sealed class ProbeDefinition : IniBuildsA350Definition
{
    private readonly string[] _watch;
    // A350_SECTIONS="Overhead,Glareshield" offers a section still being curated.
    public ProbeDefinition(string[] watch)
        : base((Environment.GetEnvironmentVariable("A350_SECTIONS") ?? "Overhead").Split(',', StringSplitOptions.RemoveEmptyEntries))
        => _watch = watch;

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = base.BuildVariables();
        foreach (var name in _watch)
            vars["W:" + name] = new SimVarDefinition
            {
                Name = name, DisplayName = name, Type = SimVarType.LVar, Units = "number",
                UpdateFrequency = UpdateFrequency.OnRequest,
            };
        return vars;
    }
}

internal sealed class Cap : ScreenReaderAnnouncer
{
    public readonly List<string> Said = new();
    public Cap(IntPtr h) : base(h) { }
    // Announce and AnnounceWithQueue drop speech while Suppressed, as ScreenReaderAnnouncer's do.
    public override void Announce(string m) { if (!Suppressed) lock (Said) Said.Add(m); }
    public override void AnnounceImmediate(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceQueued(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceWithQueue(string m) { if (!Suppressed) lock (Said) Said.Add(m); }
    public string Drain() { lock (Said) { var s = string.Join(" / ", Said); Said.Clear(); return s; } }
    public List<string> DrainList() { lock (Said) { var s = Said.ToList(); Said.Clear(); return s; } }
}

internal sealed class ProbeForm : Form
{
    private readonly string _mode; private readonly string? _arg; private readonly string[] _watch;
    private SimConnectManager? _m;
    public int ExitCode;
    private readonly StringBuilder _out = new();

    public ProbeForm(string mode, string? arg, string[] watch)
    {
        _mode = mode; _arg = arg; _watch = watch;
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; Opacity = 0;
        Shown += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex) { Line("FATAL " + ex); ExitCode = 2; }
            finally
            {
                // A copy of what was printed, in %TEMP% so a run never leaves files in the worktree.
                File.WriteAllText(Path.Combine(Path.GetTempPath(), $"a350probe_{_mode}.tsv"), _out.ToString());
                // A key still held (FIRE TEST's 3 s, a spring switch) is let go before the connection goes, as MSFSBA's
                // definition does when it is disposed; without this the last row of a run stays pressed.
                try { _def?.Dispose(); await Task.Delay(500); } catch { }
                try { _m?.Disconnect(); } catch { }
                Close();
            }
        };
    }

    protected override void WndProc(ref Message m)
    {
        _m?.ProcessWindowMessage(ref m);
        base.WndProc(ref m);
    }

    private void Line(string s) { _out.AppendLine(s); Console.WriteLine(s); }

    private ProbeDefinition? _def;

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.###") : "null";

    private async Task Run()
    {
        var cap = new Cap(Handle);
        var def = new ProbeDefinition(_watch);
        _def = def;
        var vars = def.GetVariables();
        var panels = def.GetPanelControls();
        var displays = def.GetPanelDisplayVariables();

        if (_mode == "list")
        {
            foreach (var (panel, keys) in panels)
            {
                if (_arg != null && !panel.Equals(_arg, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var key in keys)
                {
                    var d = vars[key];
                    string kind = d.RenderAsButton ? "press" : d.RenderAsSlider ? "slider" : "set";
                    string pos = d.ValueDescriptions == null ? "" : string.Join(", ", d.ValueDescriptions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
                    Line($"{panel}\t{key}\t{kind}\t{d.Name}\t{d.DisplayName}\t{pos}");
                }
                foreach (var key in displays.GetValueOrDefault(panel) ?? new List<string>())
                    Line($"{panel}\t{key}\tlight\t{vars[key].Name}\t{vars[key].DisplayName}");
            }
            return;
        }

        _m = new SimConnectManager(Handle) { CurrentAircraft = def };
        def.Attach(_m);
        if (_mode != "listen")   // listen feeds the definition itself, with its own announcer
            _m.SimVarUpdated += (_, e) => { try { def.ProcessSimVarUpdate(e.VarName, e.Value, cap); } catch { } };
        _m.Connect();
        Line("connecting...");
        for (int i = 0; i < 40 && !_m.IsFullyConnected; i++) await Task.Delay(500);
        await Task.Delay(8000);   // registration, the first batches, the calculator-path probe
        Line($"connected={_m.IsConnected} full={_m.IsFullyConnected} calcOk={_m.CanExecuteCalculatorCode} canLand={_m.CalcWriteCanLand}");
        cap.Drain();

        if (_mode == "cache")
        {
            // What MSFSBA's cache holds for each key (what a plan is made from), then a fresh read.
            foreach (var key in _arg!.Split(',', StringSplitOptions.RemoveEmptyEntries))
                Line($"{key}	cache={Fmt(_m.GetCachedVariableValue(key))}	fresh={Fmt(await Read(key))}	batch={(vars[key].ExcludeFromBatch ? "own" : "batch")}");
            return;
        }

        if (_mode == "listen")
        {
            // What MSFSBA would say on its own for _arg seconds: the definition's own speech (lights, at each batch
            // end) and MainForm's generic announce of a row the definition leaves to it (a switch changed by anything
            // but the pilot's pick), baseline-first like SimVarMonitor. Move things from outside meanwhile.
            // MainForm's monitor has seen every value delivered since the connect: seed from the cache.
            var baseline = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (key, d0) in vars)
                if (d0.IsAnnounced && d0.UpdateFrequency == UpdateFrequency.Continuous && _m.GetCachedVariableValue(key) is double v0)
                    baseline[key] = v0;
            // A350_MUTE="key,key" unticks those Ctrl+M rows in memory only (never saved), so MainForm's two gates can be
            // checked: the def's mute wrap (DefAnnounceMuteSets.ShouldWrap) and the generic Step-6 gate.
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            foreach (var key in (Environment.GetEnvironmentVariable("A350_MUTE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                settings.A350DisabledMonitorVariables.Add(key);
            settings.RebuildDisabledMonitorVariableCaches();
            Line($"muted: {string.Join(", ", settings.A350DisabledMonitorVariables)}");
            var started = DateTime.Now;
            void Say(string text) => Line($"{(DateTime.Now - started).TotalSeconds,6:0.0}s\t{text}");
            var said = new Cap(Handle);
            _m.ContinuousBatchDelivered += (_, n) =>
            {
                def.OnContinuousBatchDelivered(n);
                foreach (var text in said.DrainList()) Say(text);
            };
            _m.SimVarUpdated += (_, e) =>
            {
                if (!vars.TryGetValue(e.VarName, out var d)) return;
                // The base variables (ground speed, altitude…) are MainForm's own readouts, never this generic path.
                if (!e.VarName.StartsWith("A350_", StringComparison.Ordinal)) return;
                bool handled;
                bool wrap = MSFSBlindAssist.Services.DefAnnounceMuteSets.ShouldWrap(def, e.VarName, settings);
                if (wrap) said.Suppressed = true;
                try { handled = def.ProcessSimVarUpdate(e.VarName, e.Value, said); } catch { return; }
                finally { if (wrap) said.Suppressed = false; }
                foreach (var text in said.DrainList()) Say(text);
                if (handled || !d.IsAnnounced || d.UpdateFrequency != UpdateFrequency.Continuous) return;
                bool muted = settings.A350DisabledMonitorVariablesSet.Contains(e.VarName);   // MainForm's Step-6 gate
                bool first = !baseline.TryGetValue(e.VarName, out var was);
                baseline[e.VarName] = e.Value;
                if (first || Math.Abs(was - e.Value) < 0.001) return;
                string word = d.ValueDescriptions.TryGetValue(e.Value, out var w) ? w : Fmt(e.Value);
                Say(muted ? $"[generic, muted] ({d.DisplayName}: {word})" : $"[generic] {d.DisplayName}: {word}");
            };
            await Task.Delay(int.Parse(_arg!) * 1000);
            Line("done");
            return;
        }

        if (_mode == "rpn")
        {
            // Raw calculator strings through MSFSBA's own sender, each followed by its pause ("code#ms;code#ms"), then
            // the watched L:vars read fresh: for timing measurements the MCP's round trips cannot pace.
            foreach (var item in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var hash = item.Split('#');
                _m.ExecuteCalculatorCodeUnique(hash[0]);
                await Task.Delay(hash.Length > 1 ? int.Parse(hash[1]) : 100);
            }
            foreach (var name in _watch)
                Line($"{name}\t{Fmt(await Read("W:" + name))}");
            return;
        }

        if (_mode == "labels")
        {
            // What each row of a panel reads on its label beside its name, from live values (the button states).
            var keys = panels.GetValueOrDefault(_arg!) ?? new List<string>();
            foreach (var key in keys.Concat(keys.SelectMany(k => vars[k].StateVariables ?? Array.Empty<string>())).Distinct())
                if (vars.TryGetValue(key, out var v) && v.UpdateFrequency == UpdateFrequency.Continuous)
                    await Read(key);
            foreach (var key in keys)
                Line($"{vars[key].DisplayName}\t{(def.TryDescribeControlState(key, out var state) ? state : "-")}");
            return;
        }

        if (_mode == "disp")
        {
            foreach (var line in await Snapshot(def, displays.GetValueOrDefault(_arg!) ?? new List<string>()))
                Line($"{line.Key}\t{line.Value}");
            return;
        }

        if (_mode is "set" or "keep")
        {
            foreach (var item in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = item.Split('=');
                var hash = kv[1].Split('#');
                int waitMs = hash.Length > 1 ? int.Parse(hash[1]) : 2000;
                double val = double.Parse(hash[0], CultureInfo.InvariantCulture);
                string key = kv[0];
                if (!vars.TryGetValue(key, out var d)) { Line($"{key}\tMISSING"); continue; }
                string panel = panels.First(p => p.Value.Contains(key)).Key;
                double? before = d.RenderAsButton ? null : await Read(key);
                var lightsBefore = await Snapshot(def, displays.GetValueOrDefault(panel) ?? new List<string>());
                def.HandleUIVariableSet(key, val, d, _m, cap);
                await Task.Delay(waitMs);
                double? after = d.RenderAsButton ? null : await Read(key);
                var lightsAfter = await Snapshot(def, displays.GetValueOrDefault(panel) ?? new List<string>());
                string restore = "";
                if (_mode == "set" && before is double b && Math.Abs(b - val) > 0.01)
                {
                    def.HandleUIVariableSet(key, Math.Round(b), d, _m, cap);
                    await Task.Delay(2500);
                    restore = $"\trestored={Fmt(await Read(key))}";
                }
                var watched = new List<string>();
                foreach (var name in _watch)
                    watched.Add($"{name}={Fmt(await Read("W:" + name))}");   // a data read: sends no calculator string
                Line($"{key}\tset {val}\tbefore={Fmt(before)} after={Fmt(after)}{restore}\t{Diff(lightsBefore, lightsAfter)}\tsaid: {cap.Drain()}\t{string.Join(" ", watched)}");
            }
            return;
        }

        // Lasting effects a restore cannot undo: driven by hand with `keep`, never in a live pass.
        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "A350_EMER_ELEC_RAT",
            "A350_FIRE_ENG_1_AGENT_1", "A350_FIRE_ENG_1_AGENT_2", "A350_FIRE_ENG_2_AGENT_1", "A350_FIRE_ENG_2_AGENT_2",
            "A350_FIRE_APU_AGENT", "A350_CARGO_SMOKE_AGENT_TO_AFT", "A350_CARGO_SMOKE_AGENT_TO_FWD",
            "A350_ELEC_IDG_1A", "A350_ELEC_IDG_1B", "A350_ELEC_IDG_2A", "A350_ELEC_IDG_2B",
            "A350_FIRE_ENG_1", "A350_FIRE_ENG_2", "A350_FIRE_APU",
            "A350_HYD_SUPPLY_ENG_1_GREEN", "A350_HYD_SUPPLY_ENG_1_YELLOW", "A350_HYD_SUPPLY_ENG_2_GREEN", "A350_HYD_SUPPLY_ENG_2_YELLOW",
            // Driven as whole procedures instead: a master's restore cannot restart its engine without IGN START, the
            // thrust levers would reach TOGA on the stand, and the gravity gear takes Down only with its guards open.
            "A350_ENG_MASTER_1", "A350_ENG_MASTER_2", "A350_ENGINE_THROTTLE_1", "A350_ENGINE_THROTTLE_2", "A350_THRUST_LEVERS_ALL",
            "A350_GRAVITY_GEAR_HANDLE",
        };

        if (_mode == "live")
        {
            foreach (var (panel, keys) in panels)
            {
                bool wholePanel = panel.Equals(_arg, StringComparison.OrdinalIgnoreCase);
                var lights = displays.GetValueOrDefault(panel) ?? new List<string>();
                foreach (var key in keys)
                {
                    if (!wholePanel && !_arg!.Split(',').Contains(key)) continue;
                    var d = vars[key];
                    if (skip.Contains(key)) { Line($"{panel}	{key}	SKIPPED (lasting effect: drive by hand)"); continue; }
                    if (d.RenderAsSlider) { Line($"{panel}\t{key}\tslider\tSKIPPED (knob: drive by hand)"); continue; }
                    var lightsBefore = await Snapshot(def, lights);
                    if (d.RenderAsButton)
                    {
                        def.HandleUIVariableSet(key, 1, d, _m, cap);
                        await Task.Delay(2000);
                        Line($"{panel}\t{key}\tpress\t{d.DisplayName}\t{Diff(lightsBefore, await Snapshot(def, lights))}\tsaid: {cap.Drain()}");
                        continue;
                    }
                    double? before = await Read(key);
                    var results = new List<string>();
                    bool allOk = true;
                    foreach (var v in d.ValueDescriptions!.Keys.OrderBy(x => x))
                    {
                        if (before is double b && Math.Abs(b - v) < 0.01) continue;
                        var pre = await Snapshot(def, lights);
                        def.HandleUIVariableSet(key, v, d, _m, cap);
                        await Task.Delay(2000);
                        double? after = await Read(key);
                        bool ok = after is double a && Math.Abs(a - v) < 0.01;
                        allOk &= ok;
                        results.Add($"{d.ValueDescriptions[v]}={Fmt(after)}{(ok ? "" : "!")} [{Diff(pre, await Snapshot(def, lights))}]");
                    }
                    if (before is double back && d.ValueDescriptions.ContainsKey(Math.Round(back)))
                    {
                        def.HandleUIVariableSet(key, Math.Round(back), d, _m, cap);
                        await Task.Delay(2000);
                    }
                    Line($"{panel}\t{key}\t{(allOk ? "PASS" : "FAIL")}\t{d.DisplayName}\tbefore={Fmt(before)}\t{string.Join(", ", results)}\trestored={Fmt(await Read(key))}\tsaid: {cap.Drain()}");
                }
            }
            Line("done");
        }
    }

    /// <summary>The panel's status lines as MSFSBA shows them, and the watched L:vars, each read fresh.</summary>
    private async Task<Dictionary<string, string>> Snapshot(ProbeDefinition def, List<string> lights)
    {
        var shown = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in lights)
        {
            // A light's inputs stream every second: the cache is what the status display shows.
            double? v = _m!.GetCachedVariableValue(key);
            shown[def.GetVariables()[key].DisplayName] = v is double x && def.TryGetDisplayOverride(key, x, out var text) ? text : Fmt(v);
        }
        foreach (var name in _watch)
            shown[name] = Fmt(await Read("W:" + name));
        return shown;
    }

    private static string Diff(Dictionary<string, string> before, Dictionary<string, string> after) =>
        string.Join("; ", after.Where(p => !before.TryGetValue(p.Key, out var b) || b != p.Value)
            .Select(p => $"{p.Key}: {before.GetValueOrDefault(p.Key, "-")} -> {p.Value}"));

    private async Task<double?> Read(string key)
    {
        try { return await _m!.ReadFreshAsync(key, 2000); } catch { return null; }
    }
}
