using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
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
    private readonly DisplayListBox _list;
    private readonly TextBox _scratchpad;
    private readonly ComboBox _side, _mode, _range;
    private readonly IntPtr _previousWindow;
    private readonly A350ShownCapture _shown = new();
    private readonly HashSet<string> _failures = new();
    private IReadOnlyList<MfdLine> _lines = Array.Empty<MfdLine>();
    private int _linesSide;
    private System.Windows.Forms.Timer? _timer;
    private bool _busy, _refreshAgain, _settingKnobs;

    private static readonly string[] Modes = { "Rose LS", "Rose VOR", "Rose NAV", "Arc", "Plan" };
    private static readonly string[] Ranges = { "Zoom 0.2 NM", "Zoom 0.5 NM", "Zoom 1 NM", "Zoom 2 NM", "Zoom 5 NM", "10 NM", "20 NM", "40 NM", "80 NM", "160 NM", "320 NM", "640 NM" };

    /// <param name="setKnob">Turns a side's ND mode (true) or range knob to a position, the EFIS panel's own row.</param>
    /// <param name="knob">A side's ND mode (true) or range knob as it stands, or null while unread.</param>
    public A350NdWindow(Func<int, A350DrawCapture?> latest, Func<int, Task<A350DrawCapture?>> drawNow, Func<int, bool> isDark,
        A350NdInput input, ScreenReaderAnnouncer announcer, Action<int, bool, double> setKnob, Func<int, bool, double?> knob)
    {
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

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), TabIndex = 3, AccessibleName = "ND",
            AccessibleDescription = "Enter acts on the line; a field takes the box's text. Control plus an arrow drags the map, Alt plus X presses the KCCU's ESC, Backspace its CLR, Page Up and Page Down scroll a list, F5 reads now, Escape closes.",
        };
        _list.SetText("Loading the ND...");

        _scratchpad = new TextBox { Dock = DockStyle.Bottom, TabIndex = 4, AccessibleName = "Text: type, then Enter on a field" };
        _scratchpad.KeyDown += async (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _list.Focus();
            await ActivateAsync();
        };

        Controls.Add(_list);
        Controls.Add(_scratchpad);
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
            if (!_range.DroppedDown && _knob(Side, false) is double r && (int)r >= 0 && (int)r < Ranges.Length) _range.SelectedIndex = (int)r;
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
