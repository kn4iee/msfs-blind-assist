using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A350 MFD as a window (Shift+M, A380 parity): the page's lines (A350MfdPage) in a list, kept in place as
/// the page refreshes. Enter acts on the selected line: a field takes what is in the scratchpad box (clicked,
/// typed on the KCCU, ENTER), anything else is clicked. Backspace presses CLR; PageUp/PageDown scroll; F5
/// refreshes; Alt+letter presses a KCCU page key. A new scratchpad message is spoken. The side box chooses the
/// captain's or the first officer's MFD and KCCU.
/// </summary>
public sealed class A350MfdWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Alt+letter KCCU page keys (A380 letters where the A380 has the page).</summary>
    internal static readonly IReadOnlyDictionary<Keys, string> PageKeys = new Dictionary<Keys, string>
    {
        [Keys.I] = "INIT", [Keys.F] = "FPLAN", [Keys.E] = "PERF", [Keys.L] = "FUEL", [Keys.R] = "RADNAV",
        [Keys.P] = "SEC", [Keys.D] = "DIR", [Keys.A] = "ATC", [Keys.V] = "SURV", [Keys.C] = "CLR",
        [Keys.T] = "DEST", [Keys.G] = "PROG", [Keys.M] = "MAILBOX", [Keys.K] = "CL_MENU", [Keys.N] = "NOTEPAD",
        [Keys.O] = "DATA", [Keys.U] = "MENU", [Keys.B] = "AIRPORT", [Keys.Y] = "REF", [Keys.X] = "ESC",
    };

    private readonly Func<A350DrawCapture?> _latest;
    private readonly Func<Task<A350DrawCapture?>> _drawNow;
    private readonly Func<int, bool> _isDark;
    private readonly A350MfdInput _input;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly DisplayListBox _list;
    private readonly TextBox _scratchpad;
    private readonly ComboBox _side;
    private readonly IntPtr _previousWindow;
    private IReadOnlyList<MfdLine> _lines = Array.Empty<MfdLine>();
    private string? _lastScratchpad;
    private readonly A350ShownCapture _shown = new();
    private System.Windows.Forms.Timer? _timer;
    private bool _busy;

    /// <param name="isDark">Whether a side's screen is dark (0 the captain's, 1 the first officer's).</param>
    public A350MfdWindow(Func<A350DrawCapture?> latest, Func<Task<A350DrawCapture?>> drawNow, Func<int, bool> isDark,
        A350MfdInput input, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _latest = latest;
        _drawNow = drawNow;
        _isDark = isDark;
        _input = input;
        _announcer = announcer;

        Text = "A350 MFD";
        Size = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _side = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Side", TabIndex = 0 };
        _side.Items.AddRange(new object[] { "Captain", "First officer" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += (s, e) => _ = RefreshAsync(false);

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), TabIndex = 1, AccessibleName = "MFD page",
            AccessibleDescription = "Enter acts on the line; a field takes the scratchpad. Backspace CLR, PageUp and PageDown scroll, F5 refreshes, Alt plus a letter presses a page key.",
        };
        _list.SetText("Loading the MFD...");

        _scratchpad = new TextBox { Dock = DockStyle.Bottom, TabIndex = 2, AccessibleName = "Scratchpad: type, then Enter on a field" };
        _scratchpad.KeyDown += async (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _list.Focus();
            await ActivateAsync();
        };

        Controls.Add(_list);
        Controls.Add(_scratchpad);
        Controls.Add(_side);

        Load += async (s, e) =>
        {
            Activate();
            _list.Focus();
            await RefreshAsync(false);
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += async (s2, e2) => await RefreshAsync(false);
            _timer.Start();
        };
    }

    private int Side => _side.SelectedIndex < 0 ? 0 : _side.SelectedIndex;

    /// <summary>The side <see cref="_lines"/> were read for: Enter acts only on lines of the side the box shows.</summary>
    private int _linesSide;

    /// <summary>A refresh asked for while one runs (the side changed mid-draw), run once that one ends: the running one may
    /// keep the last good page, so without it the lines stayed the other side's (review 2026-10-29).</summary>
    private bool _refreshAgain, _refreshAgainNow;

    private async Task RefreshAsync(bool now)
    {
        if (IsDisposed) return;
        if (_busy)
        {
            _refreshAgain = true;
            _refreshAgainNow |= now;
            return;
        }
        _busy = true;
        try
        {
            // A failed draw (a torn copy traps the draw code: no texts) is no page: keep to the last good one, never
            // "off", which only a good, empty draw means; the monitor's latest from before a fresh draw is older than it.
            var cap = now ? await _drawNow() : null;
            if (cap is { Ok: true }) _shown.Fresh(_latest());
            else
            {
                cap = _latest();
                if (_shown.Superseded(cap)) return;
            }
            if (IsDisposed) return;
            var before = _lines;
            (_lines, var shown) = PageLines(cap, Side, _isDark(Side));
            _linesSide = Side;
            _list.SetLines(shown);
            // A new page says its title once and takes the selection to it, as the A380's MFD window does.
            if (A350MfdPage.PageJustChanged(before, _lines) is { } title)
            {
                _announcer.Announce(title);
                int at = _lines.ToList().FindIndex(l => l.Spoken.StartsWith(title, StringComparison.Ordinal));
                if (at >= 0 && at < _list.Items.Count) _list.SelectedIndex = at;
            }
            // A menu that has just opened takes the selection to its first item, where a sighted pilot looks.
            if (A350MfdPage.MenuJustOpened(before, _lines) is int first && first < _list.Items.Count)
                _list.SelectedIndex = first;
            // The FMS message, in the scratchpad or, on the checklists, in its place: said as it appears.
            var pad = A350MfdPage.Scratchpad(_lines);
            if (A350MfdPage.MessageJustArrived(_lastScratchpad, pad) is { } message)
                _announcer.Announce(message);
            if (pad != null) _lastScratchpad = pad;
        }
        catch { /* best-effort; keep the last good page */ }
        finally
        {
            _busy = false;
            if (_refreshAgain && !IsDisposed)
            {
                bool again = _refreshAgainNow;
                _refreshAgain = _refreshAgainNow = false;
                _ = RefreshAsync(again);
            }
        }
    }

    /// <summary>The page's lines (what Enter acts on) and what the list shows: a dark screen shows dark, with nothing
    /// to click, as the status display reads it; with no capture (none yet, or none young enough) there is nothing to
    /// click either (review 2026-10-29: Enter clicked where the last page had its fields).</summary>
    internal static (IReadOnlyList<MfdLine> Lines, IReadOnlyList<string> Shown) PageLines(A350DrawCapture? cap, int side, bool dark)
    {
        if (cap == null)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.NotAvailableYet });
        if (dark)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.DarkScreen });
        var lines = A350MfdPage.Read(cap, side);
        return (lines, lines.Count == 0 ? new[] { "The MFD is off: no power." } : lines.Select(l => l.Spoken).ToList());
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
            // The list still shows the other side's page (its refresh is on its way): its places are not this side's.
            _announcer.AnnounceImmediate("The list was the other side's MFD; reading this side's now");
            await RefreshAsync(false);
            return;
        }
        var line = _lines[i];
        string typed = _scratchpad.Text.Trim();
        bool types = line.Kind is "field" or "scratchpad" && typed.Length > 0;
        switch (await _input.ActAsync(Side, line.ClickX, line.ClickY, types ? typed : null))
        {
            case MfdAction.Busy:
                return;   // the action running already says how it went
            case MfdAction.Unreachable:
                _announcer.AnnounceImmediate("The MFD cannot be reached: is the Coherent debugger running?");
                return;
            case MfdAction.NoKey:
                _announcer.AnnounceImmediate("The KCCU has no key for a character in " + typed);
                return;
        }
        if (types) _scratchpad.Clear();
        await AfterActionAsync();
    }

    /// <summary>Shows a page as its KCCU page key does (A350Hotkeys.MfdPages: another aircraft's window key opening the
    /// window on the A350's own page).</summary>
    internal void PressPage(string key) => PressKey(key);

    /// <summary>Presses a KCCU key, unless a line's action is running: its keys would land among the action's.</summary>
    private void PressKey(string key)
    {
        if (_input.Busy) return;
        _input.Press(Side, key);
        _ = AfterActionAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if ((keyData & Keys.Alt) == Keys.Alt && PageKeys.TryGetValue(key, out var page))
        {
            PressKey(page);
            return true;
        }
        if (ActiveControl == _list)
        {
            switch (keyData)
            {
                case Keys.Enter: _ = ActivateAsync(); return true;
                case Keys.Back: PressKey("CLR"); return true;
                case Keys.PageUp: PressKey("SCROLL_UP"); return true;
                case Keys.PageDown: PressKey("SCROLL_DOWN"); return true;
            }
        }
        if (keyData == Keys.F5) { _ = RefreshAsync(true); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
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
