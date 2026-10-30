using System.Globalization;
using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

// Live harness for the iniBuilds A350: runs MSFSBA's real SimConnectManager and IniBuildsA350Definition and calls
// HandleUIVariableSet exactly as the panel does, and MSFSBA's display engine on a live or saved memory copy. Every
// mode (A350Probe <mode> ...) and environment variable is in README.md; read it before running one against a sim.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // A350_LOG=1: this run writes the log folder as the app does (a check of the logging itself).
        if (Environment.GetEnvironmentVariable("A350_LOG") == "1") MSFSBlindAssist.Utils.Logging.Log.Init();
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
        if (!ProbeForm.Modes.Contains(mode))
        {
            // A typo connected and ran nothing for half a minute (tools review 2026-10-30).
            Console.Error.WriteLine($"A350Probe: no mode \"{mode}\". The modes (README.md describes each): "
                + string.Join(", ", ProbeForm.Modes.Concat(DisplayModes.Names).Order(StringComparer.Ordinal)));
            return 2;
        }
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
    // The sections MSFSBA offers (all five) unless A350_SECTIONS names some ("Overhead,Glareshield"); a name that is
    // not one of them is refused with the list (tools review 2026-10-30: the default was Overhead alone, so rows and
    // variables differed from the app's, and an unknown name ended the run with a KeyNotFound).
    public ProbeDefinition(string[] watch) : base(Sections()) => _watch = watch;

    private static string[] Sections()
    {
        var known = IniBuildsA350Definition.CuratedSections;
        var asked = Environment.GetEnvironmentVariable("A350_SECTIONS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (asked == null || asked.Length == 0) return known.ToArray();
        var unknown = asked.Where(s => !known.Contains(s, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"A350_SECTIONS: unknown section {string.Join(", ", unknown)}; the sections are "
                + string.Join(", ", known) + " (Displays is always offered)");
        return asked;
    }

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
    public override void AnnounceQueued(string m) { if (!Suppressed) lock (Said) Said.Add(m); }   // through Announce, as the real one
    public override void AnnounceWithQueue(string m) { if (!Suppressed) lock (Said) Said.Add(m); }
    public string Drain() { lock (Said) { var s = string.Join(" / ", Said); Said.Clear(); return s; } }
    public List<string> DrainList() { lock (Said) { var s = Said.ToList(); Said.Clear(); return s; } }
}

internal sealed class ProbeForm : Form
{
    private readonly string _mode; private readonly string? _arg; private readonly string[] _watch;
    private SimConnectManager? _m;
    public int ExitCode;

    /// <summary>The modes this form runs; the display-engine modes are <see cref="DisplayModes.Names"/>.</summary>
    internal static readonly IReadOnlySet<string> Modes = new HashSet<string>(StringComparer.Ordinal)
    {
        "list", "vars", "live", "set", "keep", "disp", "labels", "listen", "cache", "rpn", "sample", "ois", "mfd", "nd",
        "rmpwin", "hotkey", "displays", "gsx",
    };
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

    /// <summary>A KCCU key pressed through MSFSBA's A350MfdInput, for the op's result column: the cursor keys (ND, MFD)
    /// are held and let go, as the aircraft never resets them [A350-3] (tools review 2026-10-30: one held at 1 left the
    /// next press without its rising edge); any other key is one press the aircraft resets.</summary>
    private static async Task<string> Pressed(MSFSBlindAssist.Aircraft.A350.Display.A350MfdInput input, int side, string key)
    {
        // A name the KCCU has no key for writes an L:var nothing reads (FPLN for FPLAN did nothing, 2026-10-30): refused.
        if (!KccuKeys.Contains(key)) return "refused: no such KCCU key (" + string.Join(" ", KccuKeys.Order(StringComparer.Ordinal)) + ")";
        if (key is "ND" or "MFD") await input.MoveCursorAsync(side, key);
        else input.Press(side, key);
        return "sent";
    }

    /// <summary>Every KCCU key: the page and function keys, and the one each typed character presses.</summary>
    private static readonly HashSet<string> KccuKeys = new(MSFSBlindAssist.Aircraft.A350.Display.A350Kccu.FunctionKeys
        .Concat(Enumerable.Range(32, 95).Select(c => MSFSBlindAssist.Aircraft.A350.Display.A350Kccu.KeysFor(((char)c).ToString()))
            .Where(k => k is { Count: 1 }).Select(k => k![0])), StringComparer.Ordinal);

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
        // A field holding a credential (OPTIONS > 3RD PARTY: SimBrief ID, API keys, logon codes) prints its length only,
        // whatever STREAMER MODE is set to, on the console and in the %TEMP% copy alike ([A350-12], A350OisSecrets).
        static bool Secret(Dictionary<string, string> d, int i) =>
            d.TryGetValue($"items.{i}.secret", out var s) && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase));
        void Print(Dictionary<string, string> d)
        {
            int n = int.Parse(d["count"]);
            Line($"page={d["page"]} items={n}");
            for (int i = 0; i < n; i++)
            {
                string text = d[$"items.{i}.text"];
                string value = MSFSBlindAssist.Aircraft.A350.A350OisSecrets.Printable(text, d[$"items.{i}.value"], Secret(d, i));
                Line($"{d[$"items.{i}.aidx"]}	{d[$"items.{i}.kind"]}{d[$"items.{i}.type"]}	{text}	{value}"
                    + (d.TryGetValue($"items.{i}.options", out var opts) && opts.Length > 0 ? $"	[{opts}]" : ""));
            }
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
                var result = await client.EvalAsync(step[5..], CancellationToken.None);
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
            // The step as printed: a value set into a credential field is never echoed.
            string shown = step;
            if (eq > 0)
                for (int i = 0; i < count; i++)
                    if (cur[$"items.{i}.aidx"] == idx)
                        shown = find + "=" + MSFSBlindAssist.Aircraft.A350.A350OisSecrets.Printable(cur[$"items.{i}.text"], val, Secret(cur, i));
            if (eq > 0 && shown == step && MSFSBlindAssist.Aircraft.A350.A350OisSecrets.IsSecretLabel(find))
                shown = find + "=" + MSFSBlindAssist.Aircraft.A350.A350OisSecrets.Printable(find, val, true);
            last = null;
            if (eq > 0)
                client.EnqueueCommand("set_element_value", new Dictionary<string, string> { ["index"] = idx, ["value"] = val });
            else
                client.EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = idx });
            await Task.Delay(2500);
            client.EnqueueCommand("get_display_elements");
            var after = await Next(8000);
            if (after == null) { Line($"no push after {shown}"); break; }
            cur = after;
            if (all || s == steps.Length - 1) { Line($"--- after {shown}"); Print(cur); }
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

    /// <summary>Waits up to 2 minutes for the display service to be ready (the module's first compile takes about 10 s).</summary>
    private static async Task WaitForDisplays(MSFSBlindAssist.Aircraft.A350.Display.A350DisplayService service)
    {
        for (int i = 0; i < 240 && service.State is MSFSBlindAssist.Aircraft.A350.Display.A350DisplayState.Idle
            or MSFSBlindAssist.Aircraft.A350.Display.A350DisplayState.Preparing; i++)
            await Task.Delay(500);
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
        await WaitForDisplays(service);
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
                DisplayModes.SaveNew(Path.Combine(jsonDir, "CTR_0.json"), System.Text.Json.JsonSerializer.Serialize(ctr));
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
        await WaitForDisplays(service);
        Line($"displays: {service.State} {service.Reason}");
        using var monitor = new MSFSBlindAssist.Aircraft.A350.Display.A350PfdMonitor(
            async gauges => await service.DrawAsync(gauges), _ => true, _ => { });
        monitor.Start();
        await Task.Delay(4000);   // a turn of the monitor's rotation, so the window opens on a latest draw
        var window = new MSFSBlindAssist.Forms.A350.A350RmpWindow(
            rmp => monitor.Latest(MSFSBlindAssist.Aircraft.A350.A350Rmp.Gauge(rmp).Gauge, MSFSBlindAssist.Aircraft.A350.A350Rmp.Gauge(rmp).Index),
            async rmp => (await service.DrawAsync(MSFSBlindAssist.Aircraft.A350.A350Rmp.Gauge(rmp)))?.FirstOrDefault(),
            _ => false, rpn => _m!.ExecuteCalculatorCodeUnique(rpn), cap);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var list = window.ListControl;
        var side = window.SideBox;
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
            foreach (var line in window.ShownLines)
                Line($"screen\t{line}");
            window.Close();
        }
        await Task.Delay(2500);   // the entry's deletion on close is sent after the window goes
        Said();
    }

    /// <summary>The A350's hotkeys through the definition's own HandleHotkeyAction, with the display service and its
    /// monitors wired as MainForm wires them (their call-outs muted): ';'-separated HotkeyAction names, and for a window
    /// a hotkey opens, type:VALUE (into its first box, then Enter), click:TEXT (the button whose label starts so), read
    /// (every control's text), close; wait:MS between.</summary>
    private async Task RunHotkeys(ProbeDefinition def, Cap cap)
    {
        using var service = new MSFSBlindAssist.Aircraft.A350.Display.A350DisplayService();
        service.Start();
        await WaitForDisplays(service);
        Line($"displays: {service.State} {service.Reason}");
        using var ecam = new MSFSBlindAssist.Aircraft.A350.Display.A350EcamMonitor(() => true, _ => { });
        // A350_HEAR=1: the background monitor's call-outs heard too, marked, and told when the MFD window is open as
        // MainForm tells it (an FMS message said once, by the window while it is open).
        bool hear = Environment.GetEnvironmentVariable("A350_HEAR") == "1";
        using var pfd = new MSFSBlindAssist.Aircraft.A350.Display.A350PfdMonitor(
            async gauges => await service.DrawAsync(gauges), _ => !hear,
            line => BeginInvoke(() => cap.Announce("[background] " + line))) { CtrDrawn = ecam.Observe };
        if (hear)
        {
            pfd.MfdWindowOpen = () => def.MfdWindowOpen;
            pfd.MfdDark = () => def.IsScreenDark("Captain MFD");
        }
        def.Displays = service;
        def.Ecam = ecam;
        def.Pfd = pfd;
        pfd.Start();
        await Task.Delay(6000);   // a turn of the rotations, so every display has a latest draw
        Line($"start\tsaid: {cap.Drain()}");
        var hotkeys = new MSFSBlindAssist.Hotkeys.HotkeyManager();
        MSFSBlindAssist.Forms.A350.A350FcuWindowBase? Window() =>
            Application.OpenForms.OfType<MSFSBlindAssist.Forms.A350.A350FcuWindowBase>().LastOrDefault();
        Form? AnyWindow() => Application.OpenForms.OfType<Form>().LastOrDefault(f => f is not ProbeForm);
        foreach (var token in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("wait:")) { await Task.Delay(int.Parse(token[5..])); Line($"wait\tsaid: {cap.Drain()}"); continue; }
            if (token.StartsWith("type:") && Window() is { } tw)
            {
                var box = tw.Controls.OfType<TextBox>().First(t => !t.ReadOnly);
                box.Text = token[5..];
                typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(box, new object[] { new KeyEventArgs(Keys.Enter) });
                await Task.Delay(2500);
                Line($"{token}\tsaid: {cap.Drain()}");
                continue;
            }
            if (token.StartsWith("click:") && Window() is { } cw)
            {
                var button = cw.Controls.OfType<Button>().FirstOrDefault(b => b.Text.StartsWith(token[6..], StringComparison.OrdinalIgnoreCase));
                if (button == null) { Line($"{token}\tno such button"); continue; }
                button.PerformClick();
                await Task.Delay(2500);
                Line($"{token}\tsaid: {cap.Drain()}");
                continue;
            }
            if (token == "read" && AnyWindow() is { } rw)
            {
                await Task.Delay(700);   // a refresh tick
                Line($"window\t{rw.Text}");
                foreach (Control c in rw.Controls.Cast<Control>().OrderBy(c => c.TabIndex))
                    if (c is not Label) Line($"  {c.GetType().Name}\t{c.Text}");
                continue;
            }
            if (token == "close" && AnyWindow() is { } xw) { xw.Close(); await Task.Delay(300); Line($"closed\tsaid: {cap.Drain()}"); continue; }
            if (!Enum.TryParse<MSFSBlindAssist.Hotkeys.HotkeyAction>(token, out var action)) { Line($"{token}\tunknown"); continue; }
            // A hotkey that saves the pilot's settings file (Ctrl+E's E/WD call-outs) is not run from a probe.
            if (action == MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleECAMMonitoring) { Line($"{token}\tnot run: it saves the pilot's settings file"); continue; }
            bool handled = def.HandleHotkeyAction(action, _m!, cap, this, hotkeys);
            await Task.Delay(1500);
            Line($"{token}\thandled={handled}\tsaid: {cap.Drain()}");
        }
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

        if (_mode == "vars")
        {
            // Every variable the definition registers (key, sim name), to find a cockpit indicator nothing reads.
            foreach (var (key, d) in vars.OrderBy(v => v.Key, StringComparer.Ordinal))
                Line($"{key}\t{d.Name}\t{d.DisplayName}");
            return;
        }

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
        for (int i = 0; i < 40 && !_m.IsFullyConnected && (i < 4 || _m.IsConnected); i++) await Task.Delay(500);
        if (!_m.IsConnected)
        {
            // With no sim every row waited out its read and printed a FAIL (tools review 2026-10-30).
            Line("not connected: start the sim with the A350 loaded, then run again (nothing was driven)");
            ExitCode = 3;
            return;
        }
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
            // A350_TRACE="key,key" prints each delivery of those keys, timed, to see what the speech was made from.
            var trace = new HashSet<string>((Environment.GetEnvironmentVariable("A350_TRACE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            _m.ContinuousBatchDelivered += (_, n) =>
            {
                def.OnContinuousBatchDelivered(n);
                foreach (var text in said.DrainList()) Say(text);
            };
            _m.SimVarUpdated += (_, e) =>
            {
                if (trace.Contains(e.VarName)) Say($"[trace] {e.VarName}={Fmt(e.Value)}");
                if (!vars.TryGetValue(e.VarName, out var d)) return;
                // The base variables (ground speed, altitude…) are MainForm's own readouts, never this generic path; the
                // definition still sees SIM_ON_GROUND, as MainForm hands it on (the roll call-outs' ground state).
                if (!e.VarName.StartsWith("A350_", StringComparison.Ordinal))
                {
                    if (e.VarName == "SIM_ON_GROUND") def.ProcessSimVarUpdate(e.VarName, e.Value, said);
                    return;
                }
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
            // The FMS's take-off speeds, handed on as MainForm hands them: read from each copy the display reader's ticks
            // take (its own call-outs muted here: pfdlisten and ecamlisten hear those). A350_SPEEDS=0 leaves the reader out.
            MSFSBlindAssist.Aircraft.A350.Display.A350DisplayService? service = null;
            MSFSBlindAssist.Aircraft.A350.Display.A350PfdMonitor? ticks = null;
            if (Environment.GetEnvironmentVariable("A350_SPEEDS") != "0")
            {
                service = new MSFSBlindAssist.Aircraft.A350.Display.A350DisplayService();
                service.TakeoffSpeedsRead += speeds => BeginInvoke(() =>
                {
                    def.ObserveTakeoffSpeeds(speeds, said);
                    foreach (var text in said.DrainList()) Say(text);
                });
                service.MinimumsRead += minimums => BeginInvoke(() =>
                {
                    def.ObserveMinimums(minimums, said);
                    foreach (var text in said.DrainList()) Say(text);
                });
                service.Start();
                await WaitForDisplays(service);
                Line($"displays: {service.State} {service.Reason}");
                var drawn = service;
                ticks = new MSFSBlindAssist.Aircraft.A350.Display.A350PfdMonitor(async gauges => await drawn.DrawAsync(gauges), _ => true, _ => { });
                ticks.Start();
            }
            await Task.Delay(int.Parse(_arg!) * 1000);
            ticks?.Dispose();
            service?.Dispose();
            Line("done");
            return;
        }

        if (_mode == "mfd")
        {
            // MFD operations through MSFSBA's A350MfdInput, ";"-separated: click:side:x:y, type:side:TEXT, key:side:KEY.
            var input = new MSFSBlindAssist.Aircraft.A350.Display.A350MfdInput(_m.ExecuteCalculatorCodeUnique);
            foreach (var op in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = op.Split(':');
                int side = int.Parse(p[1]);
                string result = p[0] switch
                {
                    "click" => (await input.ClickAsync(side, float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture))).ToString(),
                    "type" => (await input.TypeAsync(side, p[2])).ToString(),
                    "key" => await Pressed(input, side, p[2]),
                    _ => "unknown",
                };
                Line($"{op}	{result}");
                await Task.Delay(700);
            }
            return;
        }

        if (_mode == "nd")
        {
            // ND operations, ";"-separated: click:side:x:y (the EFIS gauge's point as drawn: the captain's ND is right of
            // 836, the first officer's left of it),
            // key:side:KEY, type:side:TEXT, views (each WasmInstrument view's guid|url). The ND takes the mouse only with
            // the side's KCCU cursor on it [A350-4], so a click presses the side's ND key first.
            var input = new MSFSBlindAssist.Aircraft.A350.Display.A350MfdInput(_m.ExecuteCalculatorCodeUnique);
            const string Find = "(function(){var e=document.querySelector('wasm-instrument');return e?e.getAttribute('guid')+'|'+e.getAttribute('url'):''})()";
            var views = new List<(string Title, string GuidUrl)>();
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
            {
                var list = await http.GetStringAsync("http://127.0.0.1:19999/pagelist.json");
                using var doc = System.Text.Json.JsonDocument.Parse(list);
                foreach (var page in doc.RootElement.EnumerateArray())
                {
                    var title = page.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    if (title.Contains("WasmInstrument", StringComparison.Ordinal))
                        views.Add((title, await CoherentEvalClient.EvalAsync(title, Find, CancellationToken.None) ?? ""));
                }
            }
            // The EFIS views in panel.cfg order, which is the guids' order: the captain's (parameter L) first, the first
            // officer's (R) second. The page shows neither parameter (live 2026-10-30: its url stops at wasm_gauge=EFIS).
            var efis = views.Where(v => v.GuidUrl.Contains("wasm_gauge=EFIS", StringComparison.Ordinal))
                .OrderBy(v => int.Parse(v.GuidUrl.Split('|')[0], CultureInfo.InvariantCulture)).ToList();
            foreach (var op in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = op.Split(':');
                if (p[0] == "views") { foreach (var v in views) Line($"{v.Title}\t{v.GuidUrl}"); continue; }
                int side = int.Parse(p[1]);
                string result;
                if (p[0] == "click")
                {
                    var view = efis.ElementAtOrDefault(side);
                    // Never by the views' titles: VCockpit10 sorts before VCockpit9, and the number is no side [A350-8].
                    if (view.Title == null) { Line($"{op}\tno EFIS view for side {side} ({efis.Count} found; `views` lists them)"); continue; }
                    await input.MoveCursorAsync(side, "ND");
                    await Task.Delay(200);
                    int guid = int.Parse(view.GuidUrl.Split('|')[0], CultureInfo.InvariantCulture);
                    string at = $"{guid},{p[2]},{p[3]},0";
                    result = await CoherentEvalClient.EvalAsync(view.Title, $"(function(){{Coherent.call('WASM_MOUSE_MOVE',{at});Coherent.call('WASM_MOUSE_DOWN',{at});" +
                        $"setTimeout(function(){{Coherent.call('WASM_MOUSE_UP',{at});Coherent.call('WASM_CLICK',{at});}},120);return 'ok'}})()", CancellationToken.None) ?? "null";
                    result += " via " + view.GuidUrl;
                }
                else result = p[0] switch
                {
                    "type" => (await input.TypeAsync(side, p[2])).ToString(),
                    "key" => await Pressed(input, side, p[2]),
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
                await Task.Delay(hash.Length > 1 ? int.Parse(hash[1], System.Globalization.CultureInfo.InvariantCulture) : 100);
            }
            foreach (var name in _watch)
                Line($"{name}\t{Fmt(await Read("W:" + name))}");
            return;
        }

        if (_mode == "sample")
        {
            // "code#ms;code#ms" run in the background while the watched L:vars are read every 100 ms, each change printed
            // with its time, until the strings have all run: how a value moves inside a hold (a flashing light, a timed test).
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var run = Task.Run(async () =>
            {
                foreach (var item in _arg!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var hash = item.Split('#');
                    _m.ExecuteCalculatorCodeUnique(hash[0]);
                    await Task.Delay(hash.Length > 1 ? int.Parse(hash[1], System.Globalization.CultureInfo.InvariantCulture) : 100);
                }
            });
            string last = "";
            while (!run.IsCompleted)
            {
                var values = new List<string>();
                foreach (var name in _watch)
                    values.Add($"{name}={Fmt(await Read("W:" + name))}");
                string now = string.Join(" ", values);
                if (now != last) Line($"{sw.Elapsed.TotalSeconds,6:0.00}s\t{now}");
                last = now;
                await Task.Delay(100);
            }
            if (run.Exception?.InnerException is { } failed)   // a bad "#ms" ended the run: say so, never end silently
                Line("sample: the strings stopped: " + failed.Message);
            return;
        }

        if (_mode == "rmpwin")
        {
            await RunRmpWindow(cap);
            return;
        }

        if (_mode == "hotkey")
        {
            await RunHotkeys(def, cap);
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
                string? panel = panels.FirstOrDefault(p => p.Value.Contains(key)).Key;
                if (panel == null) { Line($"{key}\tnot on an offered panel (A350_SECTIONS?)"); continue; }
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
            // Fuel jettisoned is gone (with ARM on, a sweep of ACTIVE opens the valves), and MASK MAN ON drops the cabin's
            // masks (tools review 2026-10-30).
            "A350_JETTISON_ARM", "A350_JETTISON_ACTIVE", "A350_OXYGEN_MASK_MAN",
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
                    // A typed value (the FCU windows, the QNHs, the cabin targets) has no positions to sweep: never a PASS.
                    if (!d.RenderAsButton && d.ValueDescriptions is not { Count: > 0 })
                    { Line($"{panel}\t{key}\ttyped\tUNTESTED (a typed value: drive it with set)"); continue; }
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
