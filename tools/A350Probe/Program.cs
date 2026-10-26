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
// A350Probe disp <panel> [watch,...]             - the panel's status lines and the watched L:vars, read fresh
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
    public ProbeDefinition(string[] watch) => _watch = watch;

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
    public override void Announce(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceImmediate(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceQueued(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceWithQueue(string m) { lock (Said) Said.Add(m); }
    public string Drain() { lock (Said) { var s = string.Join(" / ", Said); Said.Clear(); return s; } }
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
                File.WriteAllText($"probe_{_mode}.tsv", _out.ToString());
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

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.###") : "null";

    private async Task Run()
    {
        var cap = new Cap(Handle);
        var def = new ProbeDefinition(_watch);
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
        _m.SimVarUpdated += (_, e) => { try { def.ProcessSimVarUpdate(e.VarName, e.Value, cap); } catch { } };
        _m.Connect();
        Line("connecting...");
        for (int i = 0; i < 40 && !_m.IsFullyConnected; i++) await Task.Delay(500);
        await Task.Delay(8000);   // registration, the first batches, the calculator-path probe
        Line($"connected={_m.IsConnected} full={_m.IsFullyConnected} calcOk={_m.CanExecuteCalculatorCode} canLand={_m.CalcWriteCanLand}");
        cap.Drain();

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
                Line($"{key}\tset {val}\tbefore={Fmt(before)} after={Fmt(after)}{restore}\t{Diff(lightsBefore, lightsAfter)}\tsaid: {cap.Drain()}");
            }
            return;
        }

        // Lasting effects a restore cannot undo: driven by hand with `keep`, never in a live pass.
        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "A350_EMER_ELEC_RAT",
            "A350_FIRE_ENG_1_AGENT_1", "A350_FIRE_ENG_1_AGENT_2", "A350_FIRE_ENG_2_AGENT_1", "A350_FIRE_ENG_2_AGENT_2",
            "A350_FIRE_APU_AGENT", "A350_CARGO_SMOKE_AGENT_TO_AFT", "A350_CARGO_SMOKE_AGENT_TO_FWD",
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
            // A light is composed from every key it is made of: refresh them all so the cache is current.
            foreach (var input in def.GetVariables()[key].StateVariables ?? new[] { key })
                await Read(input);
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
