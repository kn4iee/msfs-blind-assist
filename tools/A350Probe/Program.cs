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
        if (DisplayModes.Handles(mode)) return DisplayModes.Run(mode, args.Skip(1).ToArray());
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

    /// <summary>`ois cpt|fo [text]`: the A350 OIS through MSFSBA's own client (side probe, agent, pushes),
    /// printing what the window would get; with a text, presses that element and prints the next push.</summary>
    private async Task RunOis()
    {
        var parts = (_arg ?? "cpt").Split(' ', 2);
        bool fo = parts[0].Equals("fo", StringComparison.OrdinalIgnoreCase);
        var client = MSFSBlindAssist.SimConnect.CoherentPmdgEfbClient.ForA350Ois(fo);
        var pushes = new System.Collections.Concurrent.ConcurrentQueue<Dictionary<string, string>>();
        client.StateUpdated += (_, e) => { if (e.Type == "fbw_efb_elements") pushes.Enqueue(e.Data); };
        client.Start();
        Dictionary<string, string>? last = null;
        async Task<Dictionary<string, string>?> Next(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                while (pushes.TryDequeue(out var d)) last = d;
                if (last != null) return last;
                await Task.Delay(100);
            }
            return last;
        }
        void Print(Dictionary<string, string> d)
        {
            int n = int.Parse(d["count"]);
            Line($"page={d["page"]} items={n}");
            for (int i = 0; i < n; i++)
                Line($"{d[$"items.{i}.aidx"]}	{d[$"items.{i}.kind"]}{d[$"items.{i}.type"]}	{d[$"items.{i}.text"]}	{d[$"items.{i}.value"]}"
                    + (d.TryGetValue($"items.{i}.options", out var opts) && opts.Length > 0 ? $"	[{opts}]" : ""));
        }
        var first = await Next(15000);
        if (first == null) { Line("no push"); client.Dispose(); return; }
        if (parts.Length < 2) { Print(first); client.Dispose(); return; }
        // Steps joined by " > ": "TEXT" presses the element whose text is TEXT (exact, else the first
        // starting with it), "TEXT=VALUE" sets it, "@N" presses agent index N; the page after the last
        // step is printed (A350_OIS_ALL=1 prints after every step).
        var cur = first;
        bool all = Environment.GetEnvironmentVariable("A350_OIS_ALL") == "1";
        var steps = parts[1].Split(" > ");
        for (int s = 0; s < steps.Length; s++)
        {
            string step = steps[s].Trim(), find = step, val = "";
            if (step.StartsWith("eval:"))
            {
                // A diagnostic expression in the OIS page, over the client's own inspector socket (one per page, [DBG-9]).
                var eval = typeof(MSFSBlindAssist.SimConnect.CoherentPmdgEfbClient).GetMethod("EvalAsync",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(CancellationToken) }, null)!;
                var result = await (Task<string>)eval.Invoke(client, new object[] { step[5..], CancellationToken.None })!;
                Line($"--- eval\n{result}");
                continue;
            }
            int eq = step.IndexOf('=');
            if (eq > 0) { find = step[..eq]; val = step[(eq + 1)..]; }
            int count = int.Parse(cur["count"]);
            string? idx = find.StartsWith('@') ? find[1..] : null;
            for (int i = 0; i < count && idx == null; i++)
                if (cur[$"items.{i}.text"].Equals(find, StringComparison.OrdinalIgnoreCase)) idx = cur[$"items.{i}.aidx"];
            for (int i = 0; i < count && idx == null; i++)
                if (cur[$"items.{i}.text"].StartsWith(find, StringComparison.OrdinalIgnoreCase)) idx = cur[$"items.{i}.aidx"];
            if (idx == null) { Line($"no element {find}"); Print(cur); client.Dispose(); return; }
            last = null;
            if (eq > 0)
                client.EnqueueCommand("set_element_value", new Dictionary<string, string> { ["index"] = idx, ["value"] = val });
            else
                client.EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = idx });
            await Task.Delay(2500);
            client.EnqueueCommand("get_display_elements");
            var after = await Next(8000);
            if (after == null) { Line($"no push after {step}"); break; }
            cur = after;
            if (all || s == steps.Length - 1) { Line($"--- after {step}"); Print(cur); }
        }
        client.Dispose();
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.###") : "null";

    /// <summary>`gsx "open;pick:TEXT;wait:MS;close"`: GSX through MSFSBA's own Remote API client, state and menu model (the
    /// Access GSX window's path): open the menu, pick the entry holding TEXT, wait; each menu and GSX's message slot are
    /// printed as they change.</summary>
    private async Task RunGsx()
    {
        using var gsx = new MSFSBlindAssist.Services.Gsx.Remote.GsxRemoteConnection();
        var state = new MSFSBlindAssist.Services.Gsx.Remote.GsxRemoteState();
        var menu = MSFSBlindAssist.Services.Gsx.Remote.GsxMenuModel.Empty;
        var started = DateTime.Now;
        string Stamp() => $"{(DateTime.Now - started).TotalSeconds,6:0.0}s";
        state.Changed += key =>
        {
            if (key == "menu")
            {
                menu = state.TryGet("menu", out var v) ? MSFSBlindAssist.Services.Gsx.Remote.GsxMenuModel.Parse(v)
                    : MSFSBlindAssist.Services.Gsx.Remote.GsxMenuModel.Empty;
                Line($"{Stamp()}\tmenu: {menu.Title}{(menu.Header.Length > 0 ? " / " + menu.Header : "")}");
                for (int i = 0; i < menu.Entries.Count; i++)
                    Line($"\t  {i}: {menu.Entries[i]}{(menu.Disabled.ElementAtOrDefault(i) ? " (disabled)" : "")}");
            }
            else if (key == "message" && state.TryGet("message", out var m))
                Line($"{Stamp()}\tmessage: {m}");
        };
        gsx.FrameReceived += f => BeginInvoke(() => state.Apply(f));   // the socket loop's thread; the state is the form's
        gsx.Start();
        for (int i = 0; i < 40 && !gsx.IsConnected; i++) await Task.Delay(250);
        Line($"gsx connected={gsx.IsConnected}");
        await Task.Delay(1500);   // the snapshot
        foreach (var step in (_arg ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (step.StartsWith("wait:")) { await Task.Delay(int.Parse(step[5..])); continue; }
            MSFSBlindAssist.Services.Gsx.Remote.GsxResult result;
            if (step.StartsWith("pick:"))
            {
                string text = step[5..];
                int index = menu.Entries.ToList().FindIndex(e => e.Contains(text, StringComparison.OrdinalIgnoreCase));
                if (index < 0) { Line($"{Stamp()}\tno entry holds \"{text}\""); continue; }
                result = await gsx.SendAsync("menu.pick", new { index });
                Line($"{Stamp()}\tpick {index} ({menu.Entries[index]}): ok={result.Ok} {result.ErrorMessage}");
            }
            else
            {
                result = await gsx.SendAsync("menu." + step);
                Line($"{Stamp()}\t{step}: ok={result.Ok} {result.ErrorMessage}");
            }
            await Task.Delay(1500);
        }
        await Task.Delay(1000);
    }

    /// <summary>`displays [name,name]`: every display on demand (the Displays section's panels) as its status display reads
    /// it, each from a good live draw (a torn copy's trapped draw is drawn again), the draw code's sim asks answered from
    /// SimConnect's cache as MainForm answers them (the PFD's baro is the sim's); a filter keeps the panels whose name
    /// holds one of the names.</summary>
    private async Task RunDisplays()
    {
        using var service = new MSFSBlindAssist.Aircraft.A350.Display.A350DisplayService();
        service.AVarAnswer = new MSFSBlindAssist.Aircraft.A350.Display.A350AVarAnswers(
            key => _m!.GetCachedVariableValue(key), ask => Line("unanswered ask: " + ask)).Answer;
        service.Start();
        for (int i = 0; i < 240 && service.State is MSFSBlindAssist.Aircraft.A350.Display.A350DisplayState.Idle
            or MSFSBlindAssist.Aircraft.A350.Display.A350DisplayState.Preparing; i++)
            await Task.Delay(500);
        var filters = (_arg ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var d in IniBuildsA350Definition.DisplayPanels)
        {
            if (filters.Length > 0 && !filters.Any(f => d.Panel.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            MSFSBlindAssist.Aircraft.A350.Display.A350DrawCapture? capture = null;
            for (int i = 0; i < 5 && capture is not { Ok: true }; i++)
                capture = (await service.DrawAsync((d.Gauge, d.Index)))?.FirstOrDefault();
            Line($"== {d.Panel}{(capture is { Ok: true } ? "" : ": no good draw")}");
            if (capture is { Ok: true })
                foreach (var line in d.Read(capture))
                    Line("  " + line);
        }
        // The E/WD (Alt+E's window, not a display panel), with the SD page the A350_SD_PAGE row streams.
        if (filters.Length == 0 || filters.Any(f => "E/WD EWD".Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            MSFSBlindAssist.Aircraft.A350.Display.A350DrawCapture? ctr = null;
            for (int i = 0; i < 5 && ctr is not { Ok: true }; i++)
                ctr = (await service.DrawAsync(("CTR", 0)))?.FirstOrDefault();
            Line("== E/WD" + (ctr is { Ok: true } ? "" : ": no good draw"));
            if (ctr is { Ok: true } && Environment.GetEnvironmentVariable("A350_DRAW_JSON") is { } jsonDir)   // a fixture, sim asks answered
                File.WriteAllText(Path.Combine(jsonDir, "CTR_0.json"), System.Text.Json.JsonSerializer.Serialize(ctr));
            if (ctr is { Ok: true })
                foreach (var line in MSFSBlindAssist.Aircraft.A350.Display.A350EwdText.Build(ctr, _m!.GetCachedVariableValue("A350_SD_PAGE"))
                             .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    Line("  " + line);
        }
    }

    /// <summary>`rmpwin "keys"`: MSFSBA's RMP window (Ctrl+Shift+R) with the delegates the hotkey passes (the background
    /// monitor's latest draw, a fresh draw, the unique calculator sender), fed keys as the pilot types them, space-separated:
    /// a digit or ".", enter, back, esc, f5, ctrl+N, alt+N, alt+LETTER, wait:MS, rmp:N (the RMP box). Prints what the window
    /// says, then its lines.</summary>
    private async Task RunRmpWindow(Cap cap)
    {
        using var service = new MSFSBlindAssist.Aircraft.A350.Display.A350DisplayService();
        service.Start();
        for (int i = 0; i < 240 && service.State is MSFSBlindAssist.Aircraft.A350.Display.A350DisplayState.Idle
            or MSFSBlindAssist.Aircraft.A350.Display.A350DisplayState.Preparing; i++)
            await Task.Delay(500);
        Line($"displays: {service.State} {service.Reason}");
        using var monitor = new MSFSBlindAssist.Aircraft.A350.Display.A350PfdMonitor(
            async (gauge, index) => (await service.DrawAsync((gauge, index)))?.FirstOrDefault(), _ => true, _ => { });
        monitor.Start();
        await Task.Delay(4000);   // a turn of the monitor's rotation, so the window opens on a latest draw
        var window = new MSFSBlindAssist.Forms.A350.A350RmpWindow(
            rmp => monitor.Latest(MSFSBlindAssist.Aircraft.A350.A350Rmp.Gauge(rmp).Gauge, MSFSBlindAssist.Aircraft.A350.A350Rmp.Gauge(rmp).Index),
            async rmp => (await service.DrawAsync(MSFSBlindAssist.Aircraft.A350.A350Rmp.Gauge(rmp)))?.FirstOrDefault(),
            rpn => _m!.ExecuteCalculatorCodeUnique(rpn), cap);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var list = (Control)typeof(MSFSBlindAssist.Forms.A350.A350RmpWindow).GetField("_list", flags)!.GetValue(window)!;
        var side = (ComboBox)typeof(MSFSBlindAssist.Forms.A350.A350RmpWindow).GetField("_side", flags)!.GetValue(window)!;
        var process = typeof(Control).GetMethod("ProcessCmdKey", flags)!;
        window.Show();
        await Task.Delay(1500);
        window.ActiveControl = list;
        var started = DateTime.Now;
        void Said() { foreach (var s in cap.DrainList()) Line($"{(DateTime.Now - started).TotalSeconds,6:0.0}s\tsaid: {s}"); }
        static Keys? KeyOf(string t) => t switch
        {
            "enter" => Keys.Enter, "back" => Keys.Back, "esc" => Keys.Escape, "f5" => Keys.F5, "." => Keys.OemPeriod,
            _ when t.StartsWith("ctrl+") => Keys.Control | KeyOf(t[5..]),
            _ when t.StartsWith("alt+") => Keys.Alt | KeyOf(t[4..]),
            _ when t.Length == 1 && char.IsDigit(t[0]) => Keys.D0 + (t[0] - '0'),
            _ when t.Length == 1 && char.IsLetter(t[0]) => Keys.A + (char.ToUpperInvariant(t[0]) - 'A'),
            _ => null,
        };
        foreach (var token in _arg!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("wait:")) { await Task.Delay(int.Parse(token[5..])); Said(); continue; }
            if (token.StartsWith("rmp:")) { side.SelectedIndex = int.Parse(token[4..]) - 1; Line($"rmp {token[4..]}"); await Task.Delay(250); Said(); continue; }
            if (KeyOf(token) is not Keys keys) { Line($"unknown key {token}"); continue; }
            if (window.IsDisposed) { Line($"{token}: the window is closed"); continue; }
            process.Invoke(window, new object[] { new Message(), keys });
            Line($"{(DateTime.Now - started).TotalSeconds,6:0.0}s\tkey {token}");
            await Task.Delay(250);   // a pilot's typing pace
            Said();
        }
        await Task.Delay(3000);
        Said();
        if (!window.IsDisposed)
        {
            foreach (var line in (IReadOnlyList<string>)typeof(MSFSBlindAssist.Forms.A350.A350RmpWindow).GetField("_lines", flags)!.GetValue(window)!)
                Line($"screen\t{line}");
            window.Close();
        }
        await Task.Delay(2500);   // the entry's deletion on close is sent after the window goes
        Said();
    }

    private async Task Run()
    {
        if (_mode == "ois")
        {
            await RunOis();
            return;
        }
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

        if (_mode == "mfd")
        {
            // MFD operations through MSFSBA's A350MfdInput, ";"-separated: click:side:x:y, type:side:TEXT, key:side:KEY.
            var input = new MSFSBlindAssist.Aircraft.A350.Display.A350MfdInput((name, v) =>
            {
                _m.ExecuteCalculatorCodeUnique($"{v.ToString(CultureInfo.InvariantCulture)} (>L:{name})");
                return true;
            });
            foreach (var op in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = op.Split(':');
                int side = int.Parse(p[1]);
                string result = p[0] switch
                {
                    "click" => (await input.ClickAsync(side, float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture))).ToString(),
                    "type" => (await input.TypeAsync(side, p[2])).ToString(),
                    "key" => input.Press(side, p[2]).ToString(),
                    _ => "unknown",
                };
                Line($"{op}	{result}");
                await Task.Delay(700);
            }
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

        if (_mode == "rmpwin")
        {
            await RunRmpWindow(cap);
            return;
        }

        if (_mode == "displays")
        {
            await RunDisplays();
            return;
        }

        if (_mode == "gsx")
        {
            await RunGsx();
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
