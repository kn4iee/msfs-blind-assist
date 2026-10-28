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

    private async Task RefreshAsync(bool now)
    {
        if (_busy || IsDisposed) return;
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
            if (cap == null) { _list.SetText("The A350 displays are not available yet."); return; }
            var before = _lines;
            (_lines, var shown) = PageLines(cap, Side, _isDark(Side));
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
            var pad = _lines.FirstOrDefault(l => l.Kind == "scratchpad")?.Spoken;
            if (_lastScratchpad != null && pad != null && pad != _lastScratchpad && pad != "Scratchpad: empty")
                _announcer.Announce(pad);
            _lastScratchpad = pad;
        }
        catch { /* best-effort; keep the last good page */ }
        finally { _busy = false; }
    }

    /// <summary>The page's lines (what Enter acts on) and what the list shows: a dark screen shows dark, with nothing
    /// to click, as the status display reads it.</summary>
    internal static (IReadOnlyList<MfdLine> Lines, IReadOnlyList<string> Shown) PageLines(A350DrawCapture cap, int side, bool dark)
    {
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
        var line = _lines[i];
        string typed = _scratchpad.Text.Trim();
        if (!await _input.ClickAsync(Side, line.ClickX, line.ClickY))
        {
            _announcer.AnnounceImmediate("The MFD cannot be reached: is the Coherent debugger running?");
            return;
        }
        if (line.Kind is "field" or "scratchpad" && typed.Length > 0)
        {
            if (!await _input.TypeAsync(Side, typed))
            {
                _announcer.AnnounceImmediate("The KCCU has no key for a character in " + typed);
                return;
            }
            _input.Press(Side, "ENTER");
            _scratchpad.Clear();
        }
        await AfterActionAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if ((keyData & Keys.Alt) == Keys.Alt && PageKeys.TryGetValue(key, out var page))
        {
            _input.Press(Side, page);
            _ = AfterActionAsync();
            return true;
        }
        if (ActiveControl == _list)
        {
            switch (keyData)
            {
                case Keys.Enter: _ = ActivateAsync(); return true;
                case Keys.Back: _input.Press(Side, "CLR"); _ = AfterActionAsync(); return true;
                case Keys.PageUp: _input.Press(Side, "SCROLL_UP"); _ = AfterActionAsync(); return true;
                case Keys.PageDown: _input.Press(Side, "SCROLL_DOWN"); _ = AfterActionAsync(); return true;
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
