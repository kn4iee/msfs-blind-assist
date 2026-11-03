using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The ND and its airport map (ANF) as lines (Output Ctrl+Shift+B, the key of the A380's airport map window): what the side's ND
/// shows, each name on the map and where it lies, and every control of its function panel and of the menu a click opens
/// (<see cref="A350NdPage"/>). Enter does on a line what the sighted pilot's click does there, with the KCCU cursor brought to the
/// ND: a tab, a button, a menu item, a name on the map (its menu: a stand's, a runway's, a taxiway's on the landing runway with
/// SET BTV EXIT). A field takes the box's text on the KCCU. The ND's mode and range are its own EFIS knobs. Ctrl plus an arrow
/// drags the map; on PLAN it stays where it was dragged. F5 reads now, Escape closes.
/// </summary>
public sealed class A350NdWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>How far one Ctrl+arrow drags the map, and from where (the middle of a closed map's rose).</summary>
    private const float DragPixels = 180, DragFromX = 404, DragFromY = 450;

    private readonly Func<int, A350DrawCapture?> _latest;
    private readonly Func<int, Task<A350DrawCapture?>> _drawNow;
    private readonly Func<int, bool> _isDark;
    private readonly A350NdInput _input;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Action<int, bool, double> _setKnob;
    private readonly Func<int, bool, double?> _knob;
    private readonly Action? _pressAutobrake;
    private readonly DisplayListBox _list;
    private readonly TextBox _scratchpad, _btvStatus;
    private readonly ComboBox _side, _mode, _range, _exits, _runways;
    private readonly Button _armExit, _clearBtv, _armRunway;
    private A350BtvArming.ExitsRead? _exitsRead;
    private bool _arming;
    private readonly IntPtr _previousWindow;
    private readonly A350ShownCapture _shown = new();
    private readonly HashSet<string> _failures = new();
    private IReadOnlyList<MfdLine> _lines = Array.Empty<MfdLine>();
    private int _linesSide;
    private System.Windows.Forms.Timer? _timer;
    private bool _busy, _refreshAgain, _settingKnobs;

    private static readonly IReadOnlyList<string> Modes = A350BtvArming.ModeNames;
    private static readonly IReadOnlyList<string> Ranges = A350BtvArming.RangeNames;

    /// <param name="setKnob">Turns a side's ND mode (true) or range knob to a position, the EFIS panel's own row.</param>
    /// <param name="knob">A side's ND mode (true) or range knob as it stands, or null while unread.</param>
    /// <param name="pressAutobrake">Presses the A/BRK pushbutton, for BTV's arming (<see cref="A350BtvArming"/>).</param>
    public A350NdWindow(Func<int, A350DrawCapture?> latest, Func<int, Task<A350DrawCapture?>> drawNow, Func<int, bool> isDark,
        A350NdInput input, ScreenReaderAnnouncer announcer, Action<int, bool, double> setKnob, Func<int, bool, double?> knob,
        Action? pressAutobrake = null)
    {
        _pressAutobrake = pressAutobrake;
        _previousWindow = GetForegroundWindow();
        _latest = latest;
        _drawNow = drawNow;
        _isDark = isDark;
        _input = input;
        _announcer = announcer;
        _setKnob = setKnob;
        _knob = knob;

        Text = "A350 ND and airport map";
        Size = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _side = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Side", TabIndex = 0 };
        _side.Items.AddRange(new object[] { "Captain", "First officer" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += (s, e) => { ReadKnobs(); _ = RefreshAsync(false); };

        _mode = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "ND mode", TabIndex = 1 };
        _mode.Items.AddRange(Modes.Cast<object>().ToArray());
        _mode.SelectionChangeCommitted += (s, e) => TurnKnob(true, _mode.SelectedIndex);

        _range = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "ND range", TabIndex = 2 };
        _range.Items.AddRange(Ranges.Cast<object>().ToArray());
        _range.SelectionChangeCommitted += (s, e) => TurnKnob(false, _range.SelectedIndex);

        // BTV in one step (plan 08, the A380 OANS window's Map and BTV controls): the exit list fills from the zoomed airport map the
        // first time it is entered; Arm exit, Clear BTV and Arm runway take the aircraft's own steps and put the ND back.
        var btv = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, TabIndex = 3, WrapContents = true };
        _exits = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV exit", Width = 200, TabIndex = 0 };
        _exits.Enter += async (s, e) => { if (_exitsRead == null) await ReadExitsAsync(); };
        _armExit = new Button { Text = "Arm exit", AutoSize = true, TabIndex = 1 };
        _armExit.Click += async (s, e) => await ArmExitAsync();
        _clearBtv = new Button { Text = "Clear BTV", AutoSize = true, TabIndex = 2 };
        _clearBtv.Click += async (s, e) => await ClearBtvAsync();
        _runways = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Landing runway", Width = 120, TabIndex = 3 };
        _runways.Enter += async (s, e) => { if (_exitsRead == null) await ReadExitsAsync(); };
        _armRunway = new Button { Text = "Arm runway", AutoSize = true, TabIndex = 4 };
        _armRunway.Click += async (s, e) =>
        {
            if (_runways.SelectedItem is string rwy)
                await RunAsync(A350BtvArming.RunwaySteps(rwy, KnobNow(true), KnobNow(false)), $"Landing runway {rwy} set." + A350BtvArming.Back(KnobNow(true), KnobNow(false)));
        };
        _btvStatus = new TextBox { ReadOnly = true, AccessibleName = "BTV", Width = 300, TabIndex = 5 };
        btv.Controls.AddRange(new Control[] { _exits, _armExit, _clearBtv, _runways, _armRunway, _btvStatus });

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), TabIndex = 4, AccessibleName = "ND",
            AccessibleDescription = "Enter acts on the line; a field takes the box's text. Control plus an arrow drags the map, Alt plus X presses the KCCU's ESC, Backspace its CLR, Page Up and Page Down scroll a list, F5 reads now, Escape closes.",
        };
        _list.SetText("Loading the ND...");

        _scratchpad = new TextBox { Dock = DockStyle.Bottom, TabIndex = 5, AccessibleName = "Text: type, then Enter on a field" };
        _scratchpad.KeyDown += async (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _list.Focus();
            await ActivateAsync();
        };

        Controls.Add(_list);
        Controls.Add(_scratchpad);
        Controls.Add(btv);
        Controls.Add(_range);
        Controls.Add(_mode);
        Controls.Add(_side);

        Load += async (s, e) =>
        {
            Activate();
            _list.Focus();
            ReadKnobs();
            await RefreshAsync(false);
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += async (s2, e2) => await RefreshAsync(false);
            _timer.Start();
        };
    }

    private int Side => _side.SelectedIndex < 0 ? 0 : _side.SelectedIndex;

    private void TurnKnob(bool mode, int position)
    {
        if (_settingKnobs || position < 0) return;
        _setKnob(Side, mode, position);
        _ = AfterActionAsync();
    }

    /// <summary>The boxes follow the knobs (moved in the cockpit too), never while one has the keyboard's list open.</summary>
    private void ReadKnobs()
    {
        _settingKnobs = true;
        try
        {
            if (!_mode.DroppedDown && _knob(Side, true) is double m && (int)m is >= 0 and < 5) _mode.SelectedIndex = (int)m;
            if (!_range.DroppedDown && _knob(Side, false) is double r && (int)r >= 0 && (int)r < Ranges.Count) _range.SelectedIndex = (int)r;
        }
        finally { _settingKnobs = false; }
    }

    private async Task RefreshAsync(bool now)
    {
        if (IsDisposed) return;
        if (_busy)
        {
            _refreshAgain = true;
            return;
        }
        _busy = true;
        try
        {
            var cap = now ? await _drawNow(Side) : null;
            if (cap is { Ok: true }) _shown.Fresh(_latest(Side));
            else
            {
                cap = _latest(Side);
                if (_shown.Superseded(cap)) return;
            }
            if (IsDisposed) return;
            ReadKnobs();
            var before = _lines;
            (_lines, var shown) = PageLines(cap, Side, _isDark(Side), (int?)_knob(Side, true), (int?)_knob(Side, false));
            _linesSide = Side;
            _list.SetLines(shown);
            if (cap != null && BtvStatus(cap, Side) is var status && _btvStatus.Text != status) _btvStatus.Text = status;
            // A menu or dialog just opened takes the selection to where a sighted pilot looks, and a tab just opened
            // says itself once, as the MFD's pages do.
            if (A350NdPage.SelectionFor(before, _lines) is int opened && opened < _list.Items.Count)
                _list.SelectedIndex = opened;
            if (TabOf(_lines) is { } tab && tab != TabOf(before))
            {
                _announcer.Announce(tab);
                int at = _lines.ToList().FindIndex(l => l.Spoken == tab);
                if (at >= 0 && at < _list.Items.Count) _list.SelectedIndex = at;
            }
        }
        catch (Exception ex)
        {
            // Best-effort: the last good page stays; each distinct failure once a window, so a report can name it.
            if (_failures.Add(ex.Message)) Utils.Logging.Log.Warn("A350Display", "ND window refresh: " + ex);
        }
        finally
        {
            _busy = false;
            if (_refreshAgain && !IsDisposed)
            {
                _refreshAgain = false;
                _ = RefreshAsync(false);
            }
        }
    }

    /// <summary>The BTV read-out: the exit the ND's BTV block shows (on a zoomed map) and the FMA's autobrake word.</summary>
    internal static string BtvStatus(A350DrawCapture cap, int side)
    {
        string? brake = A350BtvArming.AutobrakeOf(A350Pfd.Read(cap, side));
        string exit = A350BtvArming.ExitSetOf(cap, side) is int m ? $"exit {m} m" : "exit not shown";
        return $"BTV {exit}; autobrake {brake ?? "off"}";
    }

    private int KnobNow(bool mode) => _knob(Side, mode) is double v ? (int)v : -1;

    /// <summary>A fresh read of the side's ND as lines, with the knobs as they stand.</summary>
    private async Task<(A350DrawCapture? Cap, IReadOnlyList<MfdLine> Lines)> ReadNowAsync()
    {
        var cap = await _drawNow(Side);
        if (cap is not { Ok: true }) cap = _latest(Side);
        return (cap, cap == null ? Array.Empty<MfdLine>() : A350NdPage.Read(cap, Side, KnobNow(true), KnobNow(false)));
    }

    /// <summary>Waits up to <paramref name="ms"/> for a line the test takes, reading the ND afresh each half second.</summary>
    private async Task<MfdLine?> FindAsync(Func<MfdLine, bool> test, int ms = 4000)
    {
        for (int waited = 0; ; waited += 500)
        {
            var (_, lines) = await ReadNowAsync();
            if (lines.FirstOrDefault(test) is { } found) return found;
            if (waited >= ms) return null;
            await Task.Delay(500);
        }
    }

    private async Task<bool> ClickAsync(MfdLine line, string? typed = null) =>
        await _input.ActAsync(Side, line.ClickX, line.ClickY, typed) == MfdAction.Done;

    /// <summary>The exits of the landing runway, from the airport map zoomed for the read and the ND put back: fills the two lists
    /// and says them.</summary>
    private async Task ReadExitsAsync()
    {
        if (_arming) return;
        int mode = KnobNow(true), range = KnobNow(false);
        _arming = true;
        try
        {
            bool move = (mode, range) != (A350BtvArming.PlanMode, A350BtvArming.ZoomRange);
            if (move) await SetEfisAsync(A350BtvArming.PlanMode, A350BtvArming.ZoomRange);
            // The airport's name comes before the exits are placed along the runway (its scale): wait for them a while.
            A350DrawCapture? cap = null;
            IReadOnlyList<MfdLine> lines = Array.Empty<MfdLine>();
            for (int waited = 0; waited <= 6000; waited += 500)
            {
                (cap, lines) = await ReadNowAsync();
                if (cap != null && A350BtvArming.ExitsOf(cap, Side).Exits.Count > 0) break;
                await Task.Delay(500);
            }
            _exitsRead = cap == null ? new A350BtvArming.ExitsRead(null, Array.Empty<A350BtvArming.Exit>()) : A350BtvArming.ExitsOf(cap, Side);
            var runways = lines.Select(l => System.Text.RegularExpressions.Regex.Match(l.Spoken, @"^Runway (\d{2}[LCR]?)\b")).Where(m => m.Success)
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            if (move) await SetEfisAsync(mode, range);
            _exits.Items.Clear();
            foreach (var e in _exitsRead?.Exits ?? Array.Empty<A350BtvArming.Exit>()) _exits.Items.Add($"{e.Name}, {e.Metres} m");
            if (_exits.Items.Count > 0) _exits.SelectedIndex = 0;
            _runways.Items.Clear();
            foreach (var r in runways) _runways.Items.Add(r);
            if (_exitsRead?.Runway is { } landing && _runways.Items.IndexOf(landing) is int at && at >= 0) _runways.SelectedIndex = at;
            _announcer.AnnounceImmediate(cap == null ? "The airport map could not be read." : A350BtvArming.ExitsSentence(_exitsRead));
        }
        finally { _arming = false; }
    }

    private async Task ArmExitAsync()
    {
        if (_exitsRead?.Runway is not { } runway || _exits.SelectedIndex < 0 || _exits.SelectedIndex >= _exitsRead.Exits.Count)
        {
            await ReadExitsAsync();
            if (_exitsRead?.Exits.Count > 0) _announcer.Announce("Choose an exit, then Arm exit.");
            return;
        }
        var exit = _exitsRead.Exits[_exits.SelectedIndex];
        var (cap, _) = await ReadNowAsync();
        string? brake = cap == null ? null : A350BtvArming.AutobrakeOf(A350Pfd.Read(cap, Side));
        int mode = KnobNow(true), range = KnobNow(false);
        if (!await RunAsync(A350BtvArming.ArmSteps(runway, exit, mode, range, brake), null)) return;
        // Read back what the aircraft shows: BTV on the FMA.
        await Task.Delay(1500);
        var (after, _) = await ReadNowAsync();
        string? now = after == null ? null : A350BtvArming.AutobrakeOf(A350Pfd.Read(after, Side));
        _announcer.AnnounceImmediate(now == "BTV" ? A350BtvArming.ArmedSentence(exit, mode, range)
            : $"Exit {exit.Name} set, but the FMA shows {(now ?? "no autobrake")}, not BTV." + A350BtvArming.Back(mode, range));
    }

    /// <summary>Clear BTV, then what the aircraft shows: BTV off the FMA, and whether the exit still stands on the map.</summary>
    private async Task ClearBtvAsync()
    {
        var (cap, _) = await ReadNowAsync();
        string? brake = cap == null ? null : A350BtvArming.AutobrakeOf(A350Pfd.Read(cap, Side));
        int mode = KnobNow(true), range = KnobNow(false);
        if (!await RunAsync(A350BtvArming.ClearSteps(mode, range, brake), null)) return;
        await Task.Delay(1500);
        var (after, _) = await ReadNowAsync();
        string? now = after == null ? null : A350BtvArming.AutobrakeOf(A350Pfd.Read(after, Side));
        _announcer.AnnounceImmediate((now == "BTV" ? "BTV still armed." : "BTV off.") + A350BtvArming.Back(mode, range));
    }

    private async Task SetEfisAsync(int mode, int range)
    {
        // One knob at a time, each read back: written together, the range stopped a step short (live 2026-11-03).
        for (int attempt = 0; attempt < 3 && KnobNow(true) != mode; attempt++) { _setKnob(Side, true, mode); await Task.Delay(700); }
        for (int attempt = 0; attempt < 3 && KnobNow(false) != range; attempt++) { _setKnob(Side, false, range); await Task.Delay(700); }
        // On the zoom the airport map loads ("ARPT MAP LOADING") before its names are drawn.
        if ((mode, range) == (A350BtvArming.PlanMode, A350BtvArming.ZoomRange))
            await FindAsync(l => l.Spoken.StartsWith("Airport: ", StringComparison.Ordinal), 8000);
    }

    /// <summary>Runs the steps against fresh reads, saying a step that fails and putting the ND back even then; says
    /// <paramref name="done"/> at the end when given. False when a step failed.</summary>
    private async Task<bool> RunAsync(IReadOnlyList<A350BtvArming.Step> steps, string? done)
    {
        if (_arming || _input.Busy) { _announcer.AnnounceImmediate("The ND is still busy; press it again"); return false; }
        _arming = true;
        var back = steps.OfType<A350BtvArming.SetEfis>().Skip(1).FirstOrDefault();
        try
        {
            foreach (var step in steps)
            {
                if (await DoAsync(step) is { } failed)
                {
                    if (back != null) await SetEfisAsync(back.Mode, back.Range);
                    _announcer.AnnounceImmediate(failed + A350BtvArming.Back(back?.Mode ?? -1, back?.Range ?? -1));
                    return false;
                }
                await Task.Delay(600);
            }
            if (done != null) _announcer.AnnounceImmediate(done);
            return true;
        }
        finally
        {
            _arming = false;
            _ = AfterActionAsync();
        }
    }

    /// <summary>One step, as the pilot's clicks take it; the reason it could not be taken, or null.</summary>
    private async Task<string?> DoAsync(A350BtvArming.Step step)
    {
        switch (step)
        {
            case A350BtvArming.SetEfis s:
                await SetEfisAsync(s.Mode, s.Range);
                return null;
            case A350BtvArming.SetLandingRunway r:
                var ident = await FindAsync(l => l.Kind == "map" && System.Text.RegularExpressions.Regex.IsMatch(l.Spoken, $@"^Runway {System.Text.RegularExpressions.Regex.Escape(r.Runway)}[ ,]"));
                if (ident == null) return $"Runway {r.Runway} is not on the airport map.";
                if (!await ClickAsync(ident)) return "The ND cannot be reached.";
                var set = await FindAsync(l => l.Kind == "menu" && l.Spoken.StartsWith("SET LDG RWY", StringComparison.Ordinal), 3000);
                if (set == null || set.Spoken.EndsWith(", grey", StringComparison.Ordinal))
                {
                    // A click with a menu open only closes it (live 2026-10-30).
                    if (set != null) await ClickAsync(set with { ClickX = 300, ClickY = 860 });
                    return "SET LDG RWY is not available: is the airport in the flight plan?";
                }
                return await ClickAsync(set) ? null : "The ND cannot be reached.";
            case A350BtvArming.OpenLandingTab:
                if (await FindAsync(l => l.Kind == "tab" && l.Spoken.StartsWith("LANDING tab", StringComparison.Ordinal), 0) == null
                    && await FindAsync(l => l.Spoken == "Functions button", 2000) is { } functions)
                    await ClickAsync(functions);
                var tab = await FindAsync(l => l.Kind == "tab" && l.Spoken.StartsWith("LANDING tab", StringComparison.Ordinal), 3000);
                if (tab == null) return "The map's LANDING tab did not open.";
                return tab.Spoken.EndsWith(", selected", StringComparison.Ordinal) || await ClickAsync(tab) ? null : "The ND cannot be reached.";
            case A350BtvArming.TypeExit t:
                var field = await FindAsync(l => l.Kind == "field" && l.Spoken.StartsWith("BTV EXIT AT:", StringComparison.Ordinal), 3000);
                if (field == null || field.Spoken.EndsWith(", grey", StringComparison.Ordinal))
                    return "BTV EXIT AT is not available: set the landing runway first.";
                return await ClickAsync(field, t.Metres.ToString(System.Globalization.CultureInfo.InvariantCulture)) ? null : "The ND cannot be reached.";
            case A350BtvArming.ClearExit:
                var box = await FindAsync(l => l.Kind == "field" && l.Spoken.StartsWith("BTV EXIT AT:", StringComparison.Ordinal), 3000);
                if (box == null) return "BTV EXIT AT is not on the map's LANDING tab.";
                if (!await ClickAsync(box)) return "The ND cannot be reached.";
                _input.Press(Side, "CLR");
                await Task.Delay(300);
                _input.Press(Side, "ENTER");
                return null;
            case A350BtvArming.CloseFunctions:
                if (await FindAsync(l => l.Kind == "tab", 0) != null && await FindAsync(l => l.Spoken == "Functions button", 1000) is { } open)
                    await ClickAsync(open);
                return null;
            case A350BtvArming.PressAutobrake p:
                if (_pressAutobrake == null) return "The A/BRK pushbutton cannot be reached from here.";
                for (int i = 0; i < p.Times; i++)
                {
                    _pressAutobrake();
                    await Task.Delay(1500);
                }
                return null;
        }
        return null;
    }

    private static string? TabOf(IReadOnlyList<MfdLine> lines) =>
        lines.FirstOrDefault(l => l.Kind == "tab" && l.Spoken.EndsWith(", selected", StringComparison.Ordinal))?.Spoken;

    /// <summary>The ND's lines and what the list shows: a dark screen shows dark, with nothing to click, and with no capture
    /// (none yet, or none young enough) there is nothing to click either.</summary>
    internal static (IReadOnlyList<MfdLine> Lines, IReadOnlyList<string> Shown) PageLines(A350DrawCapture? cap, int side, bool dark,
        int? mode = null, int? range = null)
    {
        if (cap == null)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.NotAvailableYet });
        if (dark)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.DarkScreen });
        var lines = A350NdPage.Read(cap, side, mode, range);
        return (lines, lines.Count == 0 ? new[] { "The ND shows nothing." } : lines.Select(l => l.Spoken).ToList());
    }

    private async Task AfterActionAsync()
    {
        await Task.Delay(600);
        await RefreshAsync(true);
    }

    private async Task ActivateAsync()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _lines.Count) return;
        if (_linesSide != Side)
        {
            _announcer.AnnounceImmediate("The list was the other side's ND; reading this side's now");
            await RefreshAsync(false);
            return;
        }
        var line = _lines[i];
        if (line.ClickX < 0)
        {
            _announcer.AnnounceImmediate("Nothing to press on this line");
            return;
        }
        string typed = _scratchpad.Text.Trim();
        bool types = line.Kind == "field" && typed.Length > 0;
        switch (await _input.ActAsync(Side, line.ClickX, line.ClickY, types ? typed : null))
        {
            case MfdAction.Busy:
                return;
            case MfdAction.Unreachable:
                _announcer.AnnounceImmediate("The ND cannot be reached: is the Coherent debugger running?");
                return;
            case MfdAction.NoKey:
                _announcer.AnnounceImmediate("The KCCU has no key for a character in " + typed);
                return;
        }
        if (types) _scratchpad.Clear();
        await AfterActionAsync();
    }

    private async Task DragAsync(float dx, float dy)
    {
        if (await _input.DragAsync(Side, DragFromX, DragFromY, DragFromX + dx, DragFromY + dy) == MfdAction.Unreachable)
        {
            _announcer.AnnounceImmediate("The ND cannot be reached: is the Coherent debugger running?");
            return;
        }
        await AfterActionAsync();
    }

    private void PressKey(string key)
    {
        if (_input.Busy) return;
        _input.Press(Side, key);
        _ = AfterActionAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            // The view moves the way the arrow says, so the map is dragged the other way.
            case Keys.Control | Keys.Up: _ = DragAsync(0, DragPixels); return true;
            case Keys.Control | Keys.Down: _ = DragAsync(0, -DragPixels); return true;
            case Keys.Control | Keys.Left: _ = DragAsync(DragPixels, 0); return true;
            case Keys.Control | Keys.Right: _ = DragAsync(-DragPixels, 0); return true;
            case Keys.Alt | Keys.X: PressKey("ESC"); return true;
            case Keys.F5: _ = RefreshAsync(true); return true;
            case Keys.Escape: Close(); return true;
        }
        if (ActiveControl == _list)
        {
            switch (keyData)
            {
                case Keys.Enter: _ = ActivateAsync(); return true;
                case Keys.Back: PressKey("CLR"); return true;
                // An option list's own scrollbar takes no mouse: the KCCU's arrows move through it (live: LDG PERF CODE's 6 in 4 rows).
                case Keys.PageUp: PressKey("UP"); return true;
                case Keys.PageDown: PressKey("DOWN"); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }
        base.Dispose(disposing);
    }
}
