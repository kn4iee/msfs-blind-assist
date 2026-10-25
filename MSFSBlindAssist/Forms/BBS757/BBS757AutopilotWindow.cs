using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>One button of the 757 autopilot window: its label, its live state and what a press does.</summary>
public sealed record Bbs757AutopilotButton(string Label, Func<string> State, Action Press);

/// <summary>
/// The "757 Autopilot" window (Ctrl+P in input mode), the iFly, HS787 and MD-11 windows' shape:
/// one button per MCP engage control (CMD L, C and R, A/T Arm, the two F/D switches, THR, SPD,
/// LNAV, VNAV, FLCH, HDG HOLD, ALT HOLD, V/S, LOC, APP), each captioned with its live state
/// ("CMD L: On"), refreshed twice a second and rewritten only on change. A press goes through the
/// definition, which replays the cockpit's own click; it says nothing on success (CORE-7: the
/// caption's change is read by the screen reader), only "… unavailable" when nothing could be sent.
/// What each button shows and does is the definition's (BBS757Definition.Autopilot.cs); this
/// window only lays them out.
///
/// Hide on close (Escape and Close), reused for the life of the aircraft; the definition disposes
/// it on a profile switch.
/// </summary>
public sealed class BBS757AutopilotWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int RefreshMs = 500;

    private readonly IReadOnlyList<Bbs757AutopilotButton> _items;
    private readonly Action? _requestStates;
    private readonly List<(Button Button, Bbs757AutopilotButton Item)> _buttons = new();
    private readonly System.Windows.Forms.Timer _refresh;
    private IntPtr _previousWindow;
    private int _tick;

    /// <param name="items">The buttons, in order.</param>
    /// <param name="requestStates">Asks the sim for the states the cache does not stream; called about once a second while shown.</param>
    public BBS757AutopilotWindow(IReadOnlyList<Bbs757AutopilotButton> items, Action? requestStates)
    {
        _items = items;
        _requestStates = requestStates;
        BuildForm();
        _refresh = new System.Windows.Forms.Timer { Interval = RefreshMs };
        _refresh.Tick += (_, _) => Tick();
    }

    private void BuildForm()
    {
        Text = "757 Autopilot";
        AccessibleName = "757 Autopilot";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        const int left = 15, col2 = 215, width = 190, height = 34, row = 42;
        int y = 15, tab = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var button = new Button
            {
                Location = new Point(i % 2 == 0 ? left : col2, y),
                Size = new Size(width, height),
                TabIndex = tab++,
            };
            button.Click += (_, _) =>
            {
                item.Press();
                RefreshCaptions(force: false);
            };
            _buttons.Add((button, item));
            Controls.Add(button);
            if (i % 2 == 1 || i == _items.Count - 1) y += row;
        }

        var close = new Button
        {
            Text = "Close",
            Location = new Point(left, y),
            Size = new Size(col2 + width - left, height),
            TabIndex = tab,
        };
        close.Click += (_, _) => HideWindow();
        Controls.Add(close);
        CancelButton = close;   // Escape
        ClientSize = new Size(col2 + width + left, y + height + 15);

        // Real shutdown goes through; any other close hides, so the window keeps its place.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            HideWindow();
        };
        RefreshCaptions(force: true);
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        _requestStates?.Invoke();
        RefreshCaptions(force: true);
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        if (_buttons.Count > 0) _buttons[0].Button.Focus();
        _refresh.Start();
    }

    private void HideWindow()
    {
        _refresh.Stop();
        Hide();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    private void Tick()
    {
        if (IsDisposed || !Visible) return;
        if (++_tick % 2 == 0) _requestStates?.Invoke();
        RefreshCaptions(force: false);
    }

    /// <summary>
    /// Every caption from its state, "Label: State", rewritten only when it changed so a screen reader
    /// is never re-read a stable caption.
    /// </summary>
    private void RefreshCaptions(bool force)
    {
        foreach (var (button, item) in _buttons)
        {
            string text;
            try { text = $"{item.Label}: {item.State()}"; }
            catch (Exception) { text = $"{item.Label}: unavailable"; }
            if (!force && button.Text == text) continue;
            button.Text = text;
            button.AccessibleName = text;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _refresh?.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        // Hide-on-close: Close() is canceled, so the timer is torn down here.
        if (disposing)
        {
            _refresh?.Stop();
            _refresh?.Dispose();
        }
        base.Dispose(disposing);
    }
}
