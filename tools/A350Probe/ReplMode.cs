using System.Globalization;
using System.Text;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.SimConnect;


/// <summary>
/// `repl DIR`: one connection, one display reader, MSFSBA's background speech wired as MainForm wires it, and commands from a file
/// (each a line "id|command" appended to DIR\in.txt; its answer in DIR\id.txt, then DIR\id.done). A step takes a moment instead of a
/// probe's start of about 12 s, which counts on the A350: its sim grows by the wall-clock minute. tools/A350Probe/README.md lists the
/// commands. Everything goes through the same paths as the other modes.
/// </summary>
internal sealed partial class ProbeForm
{
    private StringBuilder? _capture;

    private static Keys? WindowKey(string t) => t switch
    {
        "enter" => Keys.Enter, "back" => Keys.Back, "esc" => Keys.Escape, "f5" => Keys.F5, "." => Keys.OemPeriod, "up" => Keys.Up, "down" => Keys.Down,
        "left" => Keys.Left, "right" => Keys.Right, "pgup" => Keys.PageUp, "pgdn" => Keys.PageDown,
        _ when t.StartsWith("ctrl+", StringComparison.Ordinal) => Keys.Control | WindowKey(t[5..]),
        _ when t.StartsWith("alt+", StringComparison.Ordinal) => Keys.Alt | WindowKey(t[4..]),
        _ when t.Length == 1 && char.IsDigit(t[0]) => Keys.D0 + (t[0] - '0'),
        _ when t.Length == 1 && char.IsLetter(t[0]) => Keys.A + (char.ToUpperInvariant(t[0]) - 'A'),
        _ => null,
    };

    private async Task RunRepl(ProbeDefinition def, Cap cap, Dictionary<string, SimVarDefinition> vars)
    {
        string dir = _arg ?? throw new ArgumentException("repl needs a folder");
        Directory.CreateDirectory(dir);
        string inFile = Path.Combine(dir, "in.txt");
        File.WriteAllText(inFile, "");
        var started = DateTime.Now;
        cap.Stamp = s => $"{(DateTime.Now - started).TotalSeconds,7:0.0}s {s}";

        // What MSFSBA says on its own, as MainForm hands it on: the definition's speech at each batch end, the generic path's.
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        var baseline = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, d0) in vars)
            if (d0.IsAnnounced && d0.UpdateFrequency == UpdateFrequency.Continuous && _m!.GetCachedVariableValue(key) is double v0)
                baseline[key] = v0;
        _m!.ContinuousBatchDelivered += (_, n) => def.OnContinuousBatchDelivered(n);
        _m.SimVarUpdated += (_, e) =>
        {
            if (!vars.TryGetValue(e.VarName, out var d)) return;
            if (!e.VarName.StartsWith("A350_", StringComparison.Ordinal))
            {
                if (e.VarName == "SIM_ON_GROUND") def.ProcessSimVarUpdate(e.VarName, e.Value, cap);
                return;
            }
            bool handled;
            try { handled = def.ProcessSimVarUpdate(e.VarName, e.Value, cap); } catch { return; }
            if (handled || !d.IsAnnounced || d.UpdateFrequency != UpdateFrequency.Continuous) return;
            bool first = !baseline.TryGetValue(e.VarName, out var was);
            baseline[e.VarName] = e.Value;
            if (first || Math.Abs(was - e.Value) < 0.001) return;
            string word = d.ValueDescriptions.TryGetValue(e.Value, out var w) ? w : Fmt(e.Value);
            if (!settings.A350DisabledMonitorVariablesSet.Contains(e.VarName))
                cap.Announce($"[generic] {d.DisplayName}: {word}");
        };

        using var service = new A350DisplayService();
        service.AVarAnswer = new A350AVarAnswers(key => _m.GetCachedVariableValue(key), ask => cap.Announce("[asked] " + ask)).Answer;
        var idents = new A350CalcStrings();
        service.CalcStringAnswer = idents.Answer;
        var identTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        identTimer.Tick += (_, _) => _m.RequestNavRadioInfo(d => idents.Update(d.Nav1Ident ?? "", d.Nav2Ident ?? "", d.Adf1Ident ?? "", d.Adf2Ident ?? ""));
        identTimer.Start();
        service.Spoken += line => BeginInvoke(() => cap.Announce("[reader] " + line));
        service.TakeoffSpeedsRead += speeds => BeginInvoke(() => def.ObserveTakeoffSpeeds(speeds, cap));
        service.MinimumsRead += minimums => BeginInvoke(() => def.ObserveMinimums(minimums, cap));
        service.Start();
        await WaitForDisplays(service);
        using var ecam = new A350EcamMonitor(() => false, line => BeginInvoke(() => cap.Announce("[ecam] " + line)));
        using var pfd = new A350PfdMonitor(async gauges => await service.DrawAsync(gauges), _ => false,
            line => BeginInvoke(() => cap.Announce("[pfd] " + line))) { CtrDrawn = ecam.Observe };
        def.Displays = service;
        def.Ecam = ecam;
        def.Pfd = pfd;
        pfd.MfdWindowOpen = () => def.MfdWindowOpen;
        pfd.MfdDark = () => def.IsScreenDark("Captain MFD");
        pfd.Start();

        var mfd = new A350MfdInput(_m.ExecuteCalculatorCodeUnique);
        var nd = new A350NdInput(_m.ExecuteCalculatorCodeUnique);
        var mailbox = new A350MailboxInput(_m.ExecuteCalculatorCodeUnique);
        var hotkeys = new MSFSBlindAssist.Hotkeys.HotkeyManager();
        File.WriteAllText(Path.Combine(dir, "ready"), service.State.ToString());
        Console.WriteLine("ready");

        async Task<A350DrawCapture?> Draw(string gauge, int index)
        {
            A350DrawCapture? capture = null;
            for (int i = 0; i < 5 && capture is not { Ok: true }; i++)
                capture = (await service.DrawAsync((gauge, index)))?.FirstOrDefault();
            return capture is { Ok: true } ? capture : null;
        }
        void Lines(IEnumerable<MfdLine> lines)
        {
            foreach (var l in lines) Line($"{l.Kind,-9} {l.ClickX,6:0} {l.ClickY,6:0}  {l.Spoken}");
        }
        async Task<string> Eval(string js) => await CoherentEvalClient.EvalAsync(MSFSBlindAssist.Aircraft.A350.A350Printout.ViewNeedle, js, CancellationToken.None) ?? "";
        Form? Window() => Application.OpenForms.OfType<Form>().LastOrDefault(f => f is not ProbeForm);

        long offset = 0;
        while (true)
        {
            string[] lines;
            using (var fs = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                string text = await reader.ReadToEndAsync();
                int end = text.LastIndexOf('\n');
                if (end < 0) { await Task.Delay(50); continue; }
                offset += Encoding.UTF8.GetByteCount(text[..(end + 1)]);
                lines = text[..end].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            foreach (var raw in lines)
            {
                var at = raw.IndexOf('|');
                if (at < 0) continue;   // the tail of a command that held a line break: no answer is waited for under an id
                string id = raw[..at], command = raw[(at + 1)..].Trim();
                var space = command.IndexOf(' ');
                string verb = space < 0 ? command : command[..space], rest = space < 0 ? "" : command[(space + 1)..].Trim();
                _capture = new StringBuilder();
                try
                {
                    switch (verb)
                    {
                        case "quit":
                            File.WriteAllText(Path.Combine(dir, id + ".txt"), "bye\n");
                            File.WriteAllText(Path.Combine(dir, id + ".done"), "");
                            return;
                        case "row":
                            foreach (var item in rest.Split(';', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var kv = item.Split('=', 2);
                                if (kv.Length != 2 || !vars.TryGetValue(kv[0].Trim(), out var d) || !double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                                {
                                    Line($"{item}\trefused");
                                    continue;
                                }
                                def.HandleUIVariableSet(kv[0].Trim(), value, d, _m, cap);
                                await Task.Delay(400);
                                Line($"{item}\tpressed");
                            }
                            break;
                        case "get":
                            foreach (var key in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                Line($"{key}\t{Fmt(_m.GetCachedVariableValue(key.Trim()))}");
                            break;
                        case "lv":
                            foreach (var name in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                Line($"{name}\t{await Eval($"String(SimVar.GetSimVarValue('L:{name.Trim()}','number'))")}");
                            break;
                        case "a":   // a NAME|UNIT[,NAME|UNIT]
                            foreach (var name in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var p = name.Split('|');
                                Line($"{name}\t{await Eval($"String(SimVar.GetSimVarValue('A:{p[0].Trim()}','{(p.Length > 1 ? p[1].Trim() : "number")}'))")}");
                            }
                            break;
                        case "rpn":
                            _m.ExecuteCalculatorCodeUnique(rest);
                            Line("sent");
                            break;
                        case "mfd":
                            foreach (var op in rest.Split(';', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var p = op.Split(':');
                                int side = int.Parse(p[1]);
                                string result = p[0] switch
                                {
                                    "click" => (await mfd.ClickAsync(side, float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture))).ToString(),
                                    "type" => (await mfd.TypeAsync(side, p[2])).ToString(),
                                    "key" => await Pressed(mfd, side, p[2]),
                                    _ => "unknown",
                                };
                                Line($"{op}\t{result}");
                                await Task.Delay(500);
                            }
                            break;
                        case "lines":
                        {
                            int side = rest.Length > 0 ? int.Parse(rest) : 0;
                            if (await Draw("MFD", 0) is { } c) Lines(A350MfdPage.Read(c, side)); else Line("no draw");
                            break;
                        }
                        case "ndpage":
                        {
                            int side = rest.Length > 0 ? int.Parse(rest) : 0;
                            int? Knob(string key) => (int?)_m.GetCachedVariableValue(key);
                            if (await Draw("EFIS", side) is { } c)
                                Lines(A350NdPage.Read(c, side, Knob(side == 0 ? "A350_EFIS_MODE_LEFT" : "A350_EFIS_MODE_RIGHT"), Knob(side == 0 ? "A350_EFIS_RANGE_LEFT" : "A350_EFIS_RANGE_RIGHT")));
                            else Line("no draw");
                            break;
                        }
                        case "nd":   // click:side:x:y[:TEXT]; key:side:KEY; drag:side:x0:y0:x1:y1 (the ND's own points)
                            foreach (var op in rest.Split(';', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var p = op.Split(':');
                                int side = int.Parse(p[1]);
                                static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
                                string result = p[0] switch
                                {
                                    "click" => (await nd.ActAsync(side, F(p[2]), F(p[3]), p.Length > 4 ? p[4] : null)).ToString(),
                                    "drag" => (await nd.DragAsync(side, F(p[2]), F(p[3]), F(p[4]), F(p[5]))).ToString(),
                                    "key" => "sent",
                                    _ => "unknown",
                                };
                                if (p[0] == "key") nd.Press(side, p[2]);
                                Line($"{op}\t{result}");
                                await Task.Delay(500);
                            }
                            break;
                        case "mailbox":
                        {
                            if (rest.Length > 0)
                            {
                                var now = await Draw("CTR", 0);
                                var hit = now == null ? null : A350Mailbox.Read(now).FirstOrDefault(l => l.Kind == "button" && l.Spoken.StartsWith(rest + " ", StringComparison.Ordinal));
                                if (hit == null) Line($"no {rest}");
                                else
                                {
                                    Line($"press {hit.Spoken}: {await mailbox.ActAsync(hit.ClickX, hit.ClickY)}");
                                    await Task.Delay(900);
                                }
                            }
                            if (await Draw("CTR", 0) is { } c) Lines(A350Mailbox.Read(c)); else Line("no draw");
                            break;
                        }
                        case "disp":
                        {
                            var filters = rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            foreach (var d in IniBuildsA350Definition.DisplayPanels)
                            {
                                if (filters.Length > 0 && !filters.Any(f => d.Panel.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
                                var c = await Draw(d.Gauge, d.Index);
                                Line($"== {d.Panel}{(c != null ? "" : ": no good draw")}");
                                if (c != null) foreach (var line in d.Read(c)) Line("  " + line);
                            }
                            break;
                        }
                        case "ewd":
                            if (await Draw("CTR", 0) is { } ctr)
                                foreach (var line in A350EwdText.Build(ctr, _m.GetCachedVariableValue("A350_SD_PAGE")).Split('\n', StringSplitOptions.RemoveEmptyEntries)) Line(line);
                            else Line("no draw");
                            break;
                        case "panel":   // a panel's status display as MSFSBA shows it: its rows' states and lights
                            foreach (var key in (def.GetPanelDisplayVariables().GetValueOrDefault(rest) ?? new List<string>()))
                                Line($"{vars[key].DisplayName}\t{(def.TryDescribeControlState(key, out var state) ? state : Fmt(_m.GetCachedVariableValue(key)))}");
                            break;
                        case "ois":
                        case "fap":
                            _mode = verb;
                            _arg = rest.Length == 0 ? null : rest;
                            await RunOis();
                            _mode = "repl";
                            break;
                        case "say":
                            foreach (var s in cap.DrainList()) Line(s);
                            break;
                        case "hotkey":
                        {
                            if (!Enum.TryParse<MSFSBlindAssist.Hotkeys.HotkeyAction>(rest, out var action)) { Line("unknown " + rest); break; }
                            if (action == MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleECAMMonitoring) { Line("not run: it saves the pilot's setting"); break; }
                            bool handled = def.HandleHotkeyAction(action, _m, cap, this, hotkeys);
                            await Task.Delay(900);
                            Line($"{rest}\thandled={handled}");
                            break;
                        }
                        case "window":   // window read | close
                            if (Window() is not { } w) { Line("no window"); break; }
                            if (rest == "close") { w.Close(); await Task.Delay(300); Line("closed"); break; }
                            Line("window\t" + w.Text);
                            foreach (Control c in w.Controls.Cast<Control>().OrderBy(c => c.TabIndex))
                                if (c is not Label)
                                {
                                    Line($"  {c.GetType().Name}\t{c.Text}");
                                    if (c is ListBox box)
                                        for (int k = 0; k < box.Items.Count; k++) Line($"    {(k == box.SelectedIndex ? ">" : " ")} {box.Items[k]}");
                                }
                            break;
                        case "key":   // key TOKEN[ TOKEN]: keys to the top window, as the pilot types them: a digit or ".", enter, back, esc, f5, ctrl+N, alt+N, alt+LETTER, up, down
                        {
                            if (Window() is not { } target) { Line("no window"); break; }
                            var process = typeof(Control).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                            foreach (var token in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (WindowKey(token) is not Keys keys) { Line("unknown key " + token); continue; }
                                process.Invoke(target, new object[] { new Message(), keys });
                                await Task.Delay(250);
                            }
                            Line("sent");
                            break;
                        }
                        case "select":   // select N: the top window's list to line N (0 based), as the arrow keys put it
                        {
                            var box = Window()?.Controls.OfType<ListBox>().FirstOrDefault();
                            if (box == null) { Line("no list"); break; }
                            box.FindForm()!.ActiveControl = box;
                            box.SelectedIndex = int.Parse(rest);
                            Line($"{box.SelectedIndex}: {box.SelectedItem}");
                            break;
                        }
                        case "tune":   // tune HZ [active]: what the Surroundings window's Enter (Shift+Enter) does
                        {
                            var p = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var read = await def.TuneCom1Own(_m!, int.Parse(p[0], CultureInfo.InvariantCulture), p.Length > 1)!;
                            Line($"read {read?.ToString("F0", CultureInfo.InvariantCulture) ?? "none"}");
                            break;
                        }
                        case "text":   // text WORDS: into the top window's text box, as the pilot types it there
                        {
                            var box = Window()?.Controls.OfType<TextBox>().FirstOrDefault();
                            if (box == null) { Line("no text box"); break; }
                            box.Text = rest;
                            Line("typed");
                            break;
                        }
                        case "raw":   // raw GAUGE INDEX [Y0 Y1 [X0 X1]]: what a draw holds in a band: its texts and its controls, each with its index
                        {
                            var p = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            float F(int i, float d) => p.Length > i ? float.Parse(p[i], CultureInfo.InvariantCulture) : d;
                            float y0 = F(2, 0), y1 = F(3, 99999), x0 = F(4, 0), x1 = F(5, 99999);
                            if (await Draw(p[0], int.Parse(p[1])) is not { } c) { Line("no draw"); break; }
                            for (int k = 0; k < c.Texts.Count; k++)
                                if (c.Texts[k] is var t && t.Y >= y0 && t.Y < y1 && t.X >= x0 && t.X < x1 && t.Text.Trim().Length > 0)
                                    Line(FormattableString.Invariant($"T{k,-4} {t.X,7:0} {t.Y,6:0} {t.ColourName,-7} {t.Text}"));
                            foreach (var e in c.Elements.Where(e => e.Y >= y0 && e.Y < y1 && e.X >= x0 && e.X < x1))
                                Line(FormattableString.Invariant($"E{e.TextStart,-4} {e.X,7:0} {e.Y,6:0} {e.RectW,5:0}x{e.RectH,-4:0} {e.Class}"));
                            break;
                        }
                        case "save":   // save GAUGE INDEX PATH: a draw as a fixture
                        {
                            var p = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                            if (await Draw(p[0], int.Parse(p[1])) is not { } c) { Line("no draw"); break; }
                            File.WriteAllText(p[2], System.Text.Json.JsonSerializer.Serialize(c));
                            Line("saved " + p[2]);
                            break;
                        }
                        case "stats":
                        {
                            var sim = System.Diagnostics.Process.GetProcessesByName("FlightSimulator2024").FirstOrDefault();
                            var machine = A350MachineMemory.Read();
                            Line(FormattableString.Invariant($"sim {(sim?.PrivateMemorySize64 ?? 0) / 1e9:0.0} GB, {machine}, reader {service.State}"));
                            break;
                        }
                        default:
                            Line("unknown: " + verb);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Line("ERROR " + ex.Message);
                }
                string answer = _capture.ToString();
                _capture = null;
                File.WriteAllText(Path.Combine(dir, id + ".txt"), answer);
                File.WriteAllText(Path.Combine(dir, id + ".done"), "");
            }
        }
    }
}
