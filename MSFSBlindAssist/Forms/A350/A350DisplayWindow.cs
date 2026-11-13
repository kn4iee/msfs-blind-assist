using System.Runtime.InteropServices;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>Where a display window's rows come from: a side's latest draw (the background monitor's, <see cref="A350PfdMonitor.Latest"/>,
/// which keeps the display drawn every tick while a window reads it), a fresh draw after a press, whether the side's screen is dark, the
/// display's lines from a draw, and why the display is not there at all (an option not fitted), or null.</summary>
public sealed record A350DisplayFeed(Func<int, A350DrawCapture?> Latest, Func<int, Task<A350DrawCapture?>> DrawNow, Func<int, bool> Dark,
    Func<A350DrawCapture, int, IReadOnlyList<string>> Read, Func<int, string?>? Unavailable = null)
{
    /// <summary>Why the draw shown cannot be read (re-review M3-M5: the CTR's draw showing no power, the SD's naming no page with the page
    /// variable unread), judged on that draw, a press's fresh draw included; null to read it.</summary>
    public Func<A350DrawCapture, int, string?>? Unread { get; init; }
}

/// <summary>
/// The live window every A350 display key opens ("A350 PFD", "A350 SD" and the rest; plan 26), the Fenix's display window
/// (`FenixChannelDisplayForm`) on the A350's draws: one list, named "&lt;display&gt; display", that fills the window and shows what the
/// display shows (<see cref="A350DisplayRows"/>), read again every second and reconciled in place so the row the pilot is on stays under
/// the reader, found again by its words when its value changed (<see cref="A350DisplayRows.RowToFollow"/>); one status row while the
/// display is dark or unread. Under the list, in the tab order after it: the side box when the display has two sides, then the cockpit
/// controls that change the display, each a panel row (<see cref="A350KeyRow"/>, <see cref="IA350WindowRows"/>). F5 reads a fresh draw
/// now, once per press of the key; Escape closes, unless a combo's list is open; the window the pilot came from comes back on close.
/// </summary>
/// <remarks>
/// <para>Silence ([CORE-7]). The window has no announcer: the screen reader reads the list as it takes focus and a row that changes under
/// the pilot, and a button's new text after its press. A refresh, F5, opening and closing say nothing.</para>
/// <para>Draws. A tick shows the monitor's latest draw; a press shows a fresh draw 600 ms later, which the monitor's latest from before
/// it never replaces (<see cref="A350FreshDraws"/>, per side: a draw asked for one side is never shown for the other, and a side
/// switched back to shows its own). Presses close together read one fresh draw, after the last, and one draw at a time: each is a full
/// memory copy on the reader's one queue, where the background's E/WD and FMA reads wait too (final review); a draw that fails costs
/// only itself. A display that is not there (an option not fitted) is never asked of the monitor, nor drawn. Why a draw cannot be read
/// (<see cref="A350DisplayFeed.Unread"/>) is judged on the draw shown, a press's fresh draw included.</para>
/// <para>One class for each display: windows are tracked one for each type (<c>ShowTrackedWindow</c>), so each display's window derives
/// from this one in a class of its own. Not abstract only so that a test can build it.</para>
/// </remarks>
public class A350DisplayWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const long KeyWasDown = 0x40000000;   // bit 30 of a key message's lParam: an auto-repeat
    private const int RefreshIntervalMs = 1000, AfterPressMs = 600;

    private readonly string _display;
    private readonly A350DisplayFeed _feed;
    private readonly IA350WindowRows _rows;
    private readonly DisplayListBox _list;
    private readonly ComboBox? _side;
    private readonly List<A350KeyRow> _keyRows = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly A350FreshDraws _draws;
    private int _pressGeneration, _drawAgainSide;
    private bool _drawing, _drawAgain;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private IntPtr _previousWindow;
    private bool _started;
    private string? _followedRow;   // the row the pilot is on, by its text, as the last refresh with content found it

    /// <param name="display">The display's short name: "PFD", "ND", "E/WD", "SD", "ISIS" or "SND". It makes the title ("A350 PFD"),
    /// names the list ("PFD display") and starts the status row.</param>
    /// <param name="sides">2 for a display each pilot has (the PFD): a side box picks the captain's or the first officer's.</param>
    public A350DisplayWindow(string display, A350DisplayFeed feed, IA350WindowRows rows, int sides = 1)
    {
        _display = display;
        _feed = feed;
        _rows = rows;
        _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
        _timer.Tick += (_, _) => RefreshNow();
        _draws = new A350FreshDraws(() => Clock(), (long)A350PfdMonitor.MaxAge.TotalMilliseconds);

        Text = "A350 " + display;
        Size = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = true;
        KeyPreview = true;

        _list = new DisplayListBox { Dock = DockStyle.Fill, AccessibleName = display + " display", TabIndex = 0 };
        Controls.Add(_list);
        if (sides == 2)
        {
            _side = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Side" };
            _side.Items.AddRange(new object[] { "Captain", "First officer" });
            _side.SelectedIndex = 0;
            // The pilot's own pick reads the other side at once; a pick in code (the tests) raises it too.
            _side.SelectedIndexChanged += (_, _) => RefreshNow();
            AddControlAfterList(_side);
        }

    }

    /// <summary>Closing: the timer stops and the window the pilot came from comes back, unless a handler cancelled the close (re-review
    /// M7, review m4: judged after every handler, as the ND window does).</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        _timer.Stop();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    /// <summary>The side shown: 0 the captain's, 1 the first officer's.</summary>
    internal int Side => _side is { SelectedIndex: 1 } ? 1 : 0;

    /// <summary>The wait between a press and its fresh draw (tests replace it).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<int, CancellationToken, Task> Pause { get; set; } = Task.Delay;

    /// <summary>Milliseconds, steadily rising, for how long a fresh draw stands (tests replace it).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>Every key row's buttons, in the order added.</summary>
    internal IReadOnlyList<Button> KeyButtons => _keyRows.SelectMany(r => r.Buttons).ToList();

    /// <summary>Shows the window, or brings the open one forward, with focus on the list; the first call also starts it.</summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        StartLive();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        BringToFront();
        Activate();
        // The key opens this while the sim has the foreground, where Activate alone can leave the window behind it.
        TopMost = true;
        TopMost = false;
        _list.Focus();
    }

    /// <summary>The first open's work, once: the rows read, row 0 selected so the reader has a current row, the timer started. A second
    /// call does nothing, so a second press of the key never moves the pilot to the top again.</summary>
    internal void StartLive()
    {
        if (_started) return;
        _started = true;
        RefreshNow();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _timer.Start();
    }

    /// <summary>Reads the display again, from <paramref name="fresh"/> or the monitor's latest draw, and reconciles the list in place,
    /// the pilot kept on their row; the controls' states with it. Never speaks, never takes focus; nothing once the window is gone.</summary>
    /// <param name="fresh">A fresh draw, of <paramref name="drawnFor"/>'s side (the side shown when null).</param>
    internal void RefreshNow(A350DrawCapture? fresh = null, int? drawnFor = null)
    {
        if (IsDisposed || Disposing) return;
        var current = _list.Items.Cast<string>().ToList();
        if (!A350DisplayRows.IsStatusRow(_display, current)) _followedRow = _list.SelectedItem as string;

        // The controls first, whatever draw the list keeps: a button's new label is what the pilot hears for a press (final review).
        // A fault in a derived window's controls costs only them: this runs on the timer, every second.
        try
        {
            foreach (var row in _keyRows) row.RefreshStates();
            OnRefresh();
        }
        catch (Exception ex)
        {
            Report($"controls failed: {ex.GetType().Name}: {ex.Message}");
        }

        int side = Side;
        IReadOnlyList<string> rows;
        // The feed's own questions cost only the list when they throw (a torn draw's dark test), as a read does: this is the timer's.
        try
        {
            // A display not there (an option not fitted) is never asked of the monitor, which would keep drawing it.
            if (_feed.Unavailable?.Invoke(side) is { } why)
                rows = A350DisplayRows.Status(_display, why);
            else
            {
                if (fresh is { Ok: true })
                    _draws.Took(drawnFor ?? side, fresh, _feed.Latest(drawnFor ?? side));   // kept for its own side, shown only there
                var capture = _draws.ToShow(side, _feed.Latest(side));
                rows = A350DisplayRows.For(_display, capture, _feed.Dark(side), c => _feed.Read(c, side), Report,
                    unread: _feed.Unread is { } unread ? c => unread(c, side) : null);
            }
        }
        catch (Exception ex)
        {
            Report($"read failed: {ex.GetType().Name}: {ex.Message}");
            rows = A350DisplayRows.Status(_display, "read failed");
        }
        _list.SetLines(rows);
        if (A350DisplayRows.IsStatusRow(_display, rows)) return;
        int best = A350DisplayRows.RowToFollow(rows, _followedRow, _list.SelectedIndex);
        if (best != -1 && best != _list.SelectedIndex) _list.SelectedIndex = best;
    }

    /// <summary>After a press: a fresh draw once the aircraft has taken it, shown over the monitor's older one. A later press within
    /// the wait takes this one's place, so presses close together read one draw, after the last (final review).</summary>
    internal async Task AfterPressAsync()
    {
        int generation = ++_pressGeneration;
        int side = Side;
        try
        {
            await Pause(AfterPressMs, CancellationToken.None);
            if (IsDisposed || generation != _pressGeneration) return;
            await DrawFreshAsync(side);
        }
        catch (Exception ex)
        {
            Report($"fresh draw failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>One fresh draw at a time: one asked for while another is on its way follows it, once, for the side last asked.</summary>
    private async Task DrawFreshAsync(int side)
    {
        if (_drawing)
        {
            _drawAgain = true;
            _drawAgainSide = side;
            return;
        }
        _drawing = true;
        try
        {
            while (true)
            {
                _drawAgain = false;
                try
                {
                    // A display not there is never drawn either: each draw is a full memory copy (re-review M3).
                    var fresh = _feed.Unavailable?.Invoke(side) is null ? await _feed.DrawNow(side) : null;
                    if (IsDisposed) return;
                    RefreshNow(fresh, side);
                }
                catch (Exception ex)
                {
                    // A draw that fails costs only itself: the press queued behind it still has its own (re-review M9).
                    Report($"fresh draw failed: {ex.GetType().Name}: {ex.Message}");
                }
                if (!_drawAgain || IsDisposed) return;
                side = _drawAgainSide;
            }
        }
        finally { _drawing = false; }
    }

    /// <summary>Whether Escape closes the window: not while it is closing a combo's open list, so a pilot cancelling the list keeps the
    /// window (the Fenix display window's rule).</summary>
    internal static bool EscapeCloses(bool comboListOpen) => !comboListOpen;

    /// <summary>Whether a key message is the key's own press, not an auto-repeat of a key held down (bit 30 of its lParam set).</summary>
    internal static bool FirstPress(IntPtr lParam) => (lParam.ToInt64() & KeyWasDown) == 0;

    /// <summary>Called at every refresh, before the rows are set, for a derived window's own controls. The base does nothing.</summary>
    protected virtual void OnRefresh() { }

    /// <summary>A derived window's chance at a key before the base's own (Escape, F5); true when it took the key.</summary>
    protected virtual bool HandleWindowKey(Keys keyData) => false;

    /// <summary>Adds a control below the list, in the tab order after the controls added before it and before the side box, which
    /// stays last as on the ND window: the list first, then the controls in the order added, then the side box.</summary>
    protected void AddControlAfterList(Control control)
    {
        // The list fills what the docked controls leave, and Windows Forms docks the last control added first, so the controls end
        // up below the list in the order they were added: the side box is taken out and added again after each.
        control.Dock = DockStyle.Bottom;
        bool sideLast = _side != null && control != _side && Controls.Contains(_side);
        if (sideLast) Controls.Remove(_side!);
        Controls.Add(control);
        control.TabIndex = Controls.Count - 1;
        if (sideLast)
        {
            Controls.Add(_side!);
            _side!.TabIndex = Controls.Count - 1;
        }
    }

    /// <summary>Adds a row of the panel's keys after the list (<see cref="A350KeyRow"/>): each reads its row's state, and a press reads
    /// the display again from a fresh draw.</summary>
    /// <param name="name">The row's name, as a group the reader can say ("EFIS", "ECAM keys").</param>
    protected A350KeyRow AddKeyRow(IReadOnlyList<A350WindowKey> keys, string name)
    {
        var row = new A350KeyRow(keys, _rows, () => Side, () => _ = AfterPressAsync()) { AccessibleName = name };
        _keyRows.Add(row);
        AddControlAfterList(row);
        return row;
    }

    /// <summary>Adds a typed row's box after the list (<see cref="A350TypedBox"/>): Enter applies it, then the display is read again.</summary>
    protected A350TypedBox AddTypedBox(string label, Func<int, string> row)
    {
        var box = new A350TypedBox(label, row, _rows, () => Side, () => _ = AfterPressAsync());
        AddControlAfterList(box);
        return box;
    }

    /// <summary>A problem of this window, in the log once per distinct text.</summary>
    protected void Report(string problem)
    {
        if (_reported.Add(problem)) Log.Warn("A350Display", $"{_display} window: {problem}");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            if (HandleWindowKey(keyData)) return true;
            if (keyData == Keys.Escape && EscapeCloses(ActiveControl is ComboBox { DroppedDown: true }))
            {
                Close();
                return true;
            }
            if (keyData == Keys.F5)
            {
                // Once per press: a held key repeats, and each repeat has the already-down bit set.
                if (FirstPress(msg.LParam)) _ = RefreshFreshAsync();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task RefreshFreshAsync()
    {
        try
        {
            await DrawFreshAsync(Side);
        }
        catch (Exception ex)
        {
            Report($"fresh draw failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
