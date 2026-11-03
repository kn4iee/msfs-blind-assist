using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A350 MFD as a window (Shift+M, A380 parity): the page's lines (A350MfdPage) in a list, kept in place as
/// the page refreshes. Enter acts on the selected line: a field takes what is in the scratchpad box (clicked,
/// typed on the KCCU, ENTER), anything else is clicked. Backspace presses CLR; PageUp/PageDown scroll; F5
/// refreshes; Alt+letter presses a KCCU page key. A new scratchpad message is spoken (<see cref="A350MfdWindowMessages"/>,
/// handed over with the background's call-outs as the window opens and closes). The side box chooses the captain's or the
/// first officer's MFD and KCCU.
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
        [Keys.O] = "DATA", [Keys.U] = "MENU", [Keys.B] = "AIRPORT", [Keys.Y] = "REF", [Keys.X] = "ESC", [Keys.S] = "OIS",
    };

    /// <summary>The page buttons (plan 09 Task 1, the A380 window's two rows), each carrying its Alt letter as its mnemonic and pressing
    /// the same KCCU key; the window's other Alt letters (N, U, B, Y, X, S) stay keys.</summary>
    internal static readonly IReadOnlyList<(string Text, string Key)> PageButtons = new[]
    {
        ("&INIT", "INIT"), ("&F-PLN", "FPLAN"), ("P&ERF", "PERF"), ("FUE&L", "FUEL"), ("&RAD NAV", "RADNAV"), ("SEC (&P)", "SEC"),
        ("&DIR", "DIR"), ("&ATC", "ATC"), ("SUR&V", "SURV"), ("&CLR", "CLR"), ("&MAILBOX", "MAILBOX"), ("C/L MENU (&K)", "CL_MENU"),
        ("PRO&G", "PROG"), ("DES&T", "DEST"), ("DATA (&O)", "DATA"),
    };

    /// <summary>Control+arrow presses the KCCU's own arrows (F-PLN line by line, a list's options).</summary>
    internal static readonly IReadOnlyDictionary<Keys, string> ArrowKeys = new Dictionary<Keys, string>
    {
        [Keys.Control | Keys.Up] = "UP", [Keys.Control | Keys.Down] = "DOWN", [Keys.Control | Keys.Left] = "LEFT", [Keys.Control | Keys.Right] = "RIGHT",
    };

    private readonly Func<A350DrawCapture?> _latest;
    private readonly Func<Task<A350DrawCapture?>> _drawNow;
    private readonly Func<int, bool> _isDark;
    private readonly A350MfdInput _input;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly DisplayListBox _list;
    private readonly TextBox _scratchpad;
    private readonly ComboBox _side, _goTo;
    private readonly A350SettledChoice _goToChoice = new(TimeSpan.FromMilliseconds(350));
    private System.Windows.Forms.Timer? _goToTimer;
    private readonly IntPtr _previousWindow;
    private IReadOnlyList<MfdLine> _lines = Array.Empty<MfdLine>();
    private readonly A350MfdWindowMessages _messages;
    private readonly Action<string?>? _closed;
    private readonly Action? _openMailbox;
    private readonly A350ShownCapture _shown = new();
    private System.Windows.Forms.Timer? _timer;
    private bool _busy;
    private readonly HashSet<string> _refreshFailures = new();

    /// <param name="isDark">Whether a side's screen is dark (0 the captain's, 1 the first officer's).</param>
    /// <param name="backgroundAgreed">The background's agreed read of the captain's FMS message line as the window opens
    /// (<see cref="A350PfdMonitor.MfdWindowOpening"/>): a message it does not hold is said on the window's first read.</param>
    /// <param name="closed">Called as the window closes with its last read of the captain's message line, for the
    /// background (<see cref="A350PfdMonitor.MfdWindowClosed"/>).</param>
    /// <param name="openMailbox">Opens the mailbox window: the KCCU's MAILBOX key moves the cursor to a screen this window does not read.</param>
    public A350MfdWindow(Func<A350DrawCapture?> latest, Func<Task<A350DrawCapture?>> drawNow, Func<int, bool> isDark,
        A350MfdInput input, ScreenReaderAnnouncer announcer, string? backgroundAgreed = null, Action<string?>? closed = null, Action? openMailbox = null)
    {
        _previousWindow = GetForegroundWindow();
        _latest = latest;
        _drawNow = drawNow;
        _isDark = isDark;
        _input = input;
        _announcer = announcer;
        _messages = new A350MfdWindowMessages(backgroundAgreed);
        _closed = closed;
        _openMailbox = openMailbox;

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

        // Every page the menus reach (plan 09 Task 1, the A380 window's list), taken once the arrowing has settled.
        _goTo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Go to MFD page", TabIndex = 1 };
        foreach (var p in A350MfdPages.All) _goTo.Items.Add(p.Label);
        _goTo.SelectionChangeCommitted += (s, e) =>
        {
            _goToChoice.Changed(_goTo.SelectedIndex, DateTime.UtcNow);
            _goToTimer ??= NewGoToTimer();
            _goToTimer.Start();
        };

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), TabIndex = 2, AccessibleName = "MFD page",
            AccessibleDescription = "Enter acts on the line; a field takes the scratchpad. Backspace CLR, PageUp and PageDown scroll, F5 refreshes, Alt plus a letter presses a page key.",
        };
        _list.SetText("Loading the MFD...");

        _scratchpad = new TextBox { Dock = DockStyle.Bottom, TabIndex = 3, AccessibleName = "Scratchpad: type, then Enter on a field" };
        var pageButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, TabIndex = 4, WrapContents = true };
        foreach (var (text, key) in PageButtons)
        {
            var button = new Button { Text = text, AutoSize = true, TabIndex = pageButtons.Controls.Count };
            button.Click += (s, e) =>
            {
                PressKey(key);
                if (key == "MAILBOX") _openMailbox?.Invoke();
            };
            pageButtons.Controls.Add(button);
        }
        _scratchpad.KeyDown += async (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _list.Focus();
            await ActivateAsync();
        };

        Controls.Add(_list);
        Controls.Add(_scratchpad);
        Controls.Add(pageButtons);
        Controls.Add(_goTo);
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

    private System.Windows.Forms.Timer NewGoToTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += async (s, e) =>
        {
            if (_goToChoice.Due(DateTime.UtcNow) is not int i) return;
            timer.Stop();
            if (i >= 0 && i < A350MfdPages.All.Count) await GoToAsync(A350MfdPages.All[i]);
        };
        return timer;
    }

    /// <summary>Opens a page as a pilot does: its KCCU key where one opens it, else its menu clicks; a list item drawn grey (not
    /// offered in this phase) is said and nothing more is pressed, the open list closed.</summary>
    private async Task GoToAsync(A350MfdPageNav page)
    {
        if (_input.Busy)
        {
            _announcer.AnnounceImmediate("The MFD is still busy with the last entry; choose the page again");
            return;
        }
        if (page.KccuKey != null)
        {
            PressKey(page.KccuKey);
            return;
        }
        for (int i = 0; i < page.MenuClicks.Count; i++)
        {
            var (x, y) = page.MenuClicks[i];
            // The last click is a list's item: only when it is drawn and offered.
            if (i == page.MenuClicks.Count - 1 && i >= 3)
            {
                var cap = await _drawNow();
                if (cap is { Ok: true } && !A350MfdPages.Offered(A350MfdPage.Read(cap, Side), x, y))
                {
                    await _input.ActAsync(Side, 400, 600, null);   // a click off the list closes it
                    _announcer.AnnounceImmediate(page.Label + " not available");
                    await AfterActionAsync();
                    return;
                }
            }
            if (await _input.ActAsync(Side, x, y, null) == MfdAction.Unreachable)
            {
                _announcer.AnnounceImmediate("The MFD cannot be reached: is the Coherent debugger running?");
                return;
            }
            await Task.Delay(700);
        }
        await AfterActionAsync();
    }

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
            (_lines, var shown) = PageLines(cap, Side, _isDark(Side), _linesSide == Side ? before : null);
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
            if (_messages.Observe(_lines, _linesSide) is { } message)
                _announcer.Announce(message);
        }
        catch (Exception ex)
        {
            // Best-effort: the last good page stays; each distinct failure once a window, so a report can name it.
            if (_refreshFailures.Add(ex.Message)) Utils.Logging.Log.Warn("A350Display", "MFD window refresh: " + ex);
        }
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
    /// click either (review 2026-10-29: Enter clicked where the last page had its fields). A line a torn text left unreadable
    /// keeps its reading from <paramref name="before"/>, the same side's last lines.</summary>
    internal static (IReadOnlyList<MfdLine> Lines, IReadOnlyList<string> Shown) PageLines(A350DrawCapture? cap, int side, bool dark,
        IReadOnlyList<MfdLine>? before = null)
    {
        if (cap == null)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.NotAvailableYet });
        if (dark)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.DarkScreen });
        var lines = A350MfdPage.KeepReadable(before ?? Array.Empty<MfdLine>(), A350MfdPage.Read(cap, side));
        return (lines, lines.Count == 0 ? new[] { "The MFD is off: no power." } : lines.Select(A350MfdPage.Shown).ToList());
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
    /// window on the A350's own page). While a line's action runs the key is refused aloud, never dropped unheard (final
    /// review 2026-10-29: the window came forward on the page it was on).</summary>
    internal void PressPage(string key)
    {
        if (_input.Busy)
        {
            _announcer.AnnounceImmediate("The MFD is still busy with the last entry; press the key again");
            return;
        }
        PressKey(key);
    }

    /// <summary>The pace of the KCCU's list keys: 350 ms moved a ten-row list to its end in fourteen presses (live 2026-11-03).</summary>
    private const int ListKeyPaceMs = 350;

    /// <summary>PageDown and PageUp (plan 09 Task 4; the owner's answer to question 14): a long list open on the MFD pages a screen at a
    /// time with the KCCU's own DOWN and UP at the measured pace, read again once its draw shows it moved, the selection on its first
    /// new row; at its end "End of list" (its top "Top of list") and nothing sent. With no list open they scroll the page as before.</summary>
    private async Task PageAsync(bool down)
    {
        if (_input.Busy) return;
        var cap = await _drawNow();
        if (cap is not { Ok: true } || A350MfdPage.ListOf(cap, Side) is not { } list)
        {
            PressKey(down ? "SCROLL_DOWN" : "SCROLL_UP");
            return;
        }
        if ((down ? A350MfdPage.PageDownPresses(list) : A350MfdPage.PageUpPresses(list)) is not int presses)
        {
            _announcer.AnnounceImmediate(down ? "End of list" : "Top of list");
            return;
        }
        for (int i = 0; i < presses; i++)
        {
            _input.Press(Side, down ? "DOWN" : "UP");
            await Task.Delay(ListKeyPaceMs);
        }
        for (int waited = 0; waited <= 3000; waited += 300)
        {
            if (await _drawNow() is { Ok: true } now && A350MfdPage.ListOf(now, Side) is { } moved && A350MfdPage.ListMoved(list, moved))
            {
                await RefreshAsync(true);
                // The first new row: under the rows kept going down, the top row going up.
                int scrolled = Math.Abs(moved.First - list.First);
                int row = down ? Math.Max(0, moved.Visible - scrolled) : 0;
                var items = _lines.Select((l, i) => (l, i)).Where(p => p.l.Kind == "menuitem").ToList();
                if (row < items.Count && items[row].i < _list.Items.Count) _list.SelectedIndex = items[row].i;
                return;
            }
            await Task.Delay(300);
        }
        await RefreshAsync(true);
    }

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
            if (page == "MAILBOX") _openMailbox?.Invoke();
            return true;
        }
        if (ActiveControl == _list)
        {
            if (ArrowKeys.TryGetValue(keyData, out var arrow))
            {
                PressKey(arrow);
                return true;
            }
            switch (keyData)
            {
                case Keys.Enter: _ = ActivateAsync(); return true;
                case Keys.Back: PressKey("CLR"); return true;
                case Keys.PageUp: _ = PageAsync(down: false); return true;
                case Keys.PageDown: _ = PageAsync(down: true); return true;
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
        _goToTimer?.Stop();
        _goToTimer?.Dispose();
        _goToTimer = null;
        _closed?.Invoke(_messages.Captain);
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
