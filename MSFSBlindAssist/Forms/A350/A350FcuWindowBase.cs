using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A350's FCU windows (input Ctrl+S, H, A, V, P, B), the A380's: non-modal, focus restored on close, Escape closes.
/// Every control drives the FCU panel's own row (<see cref="IniBuildsA350Definition.PressRow"/>), so a window acts exactly
/// as that row, and every state shown is the one the panel shows (<see cref="IniBuildsA350Definition.TryDescribeControlState"/>).
/// </summary>
public class A350FcuWindowBase : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    protected readonly IniBuildsA350Definition aircraft;
    protected readonly SimConnectManager simConnect;
    protected readonly ScreenReaderAnnouncer announcer;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private readonly List<(Button Button, string Row, string Name)> _stateButtons = new();
    private IntPtr _previousWindow;

    protected A350FcuWindowBase(IniBuildsA350Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        this.aircraft = aircraft;
        this.simConnect = simConnect;
        this.announcer = announcer;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        FormClosing += (s, e) => { if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow); };
        _refresh.Tick += (s, e) => RefreshStates();
    }

    /// <summary>Shows the window non-modal (other windows stay reachable, as the A380's) and starts its live states.</summary>
    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        RefreshStates();
        _refresh.Start();
        OnShownForm();
    }

    protected virtual void OnShownForm() { }

    /// <summary>A button pressing a row, labelled with the row's state as the panel shows it ("AP 1: On").</summary>
    protected Button RowButton(string name, string row, int x, int y, int width, int tab, double value = 1)
    {
        var b = new Button { Text = name, Location = new Point(x, y), Size = new Size(width, 35), TabIndex = tab, AccessibleName = name };
        b.Click += (s, e) => { aircraft.PressRow(row, simConnect, announcer, value); RefreshStates(); };
        _stateButtons.Add((b, row, name));
        return b;
    }

    /// <summary>A switch row with two positions (0 and 1) pressed to its other position, labelled with its state.</summary>
    protected Button ToggleRowButton(string name, string row, int x, int y, int width, int tab)
    {
        var b = new Button { Text = name, Location = new Point(x, y), Size = new Size(width, 35), TabIndex = tab, AccessibleName = name };
        b.Click += (s, e) =>
        {
            double now = simConnect.GetCachedVariableValue(row) ?? 0;
            aircraft.PressRow(row, simConnect, announcer, now >= 0.5 ? 0 : 1);
            RefreshStates();
        };
        _stateButtons.Add((b, row, name));
        return b;
    }

    /// <summary>A read-only line the window keeps current (a TextBox, so it is in the tab order [VAT-13]).</summary>
    protected static TextBox StatusBox(string name, int x, int y, int width, int tab) =>
        new() { ReadOnly = true, Location = new Point(x, y), Size = new Size(width, 25), TabIndex = tab, AccessibleName = name, TabStop = true };

    /// <summary>Repaints every state: the buttons' labels and whatever the window adds.</summary>
    protected virtual void RefreshStates()
    {
        foreach (var (button, row, name) in _stateButtons)
        {
            string label = aircraft.TryDescribeControlState(row, out var state) && state.Length > 0 ? $"{name}: {state}"
                : simConnect.GetCachedVariableValue(row) is double v && aircraft.GetVariables().TryGetValue(row, out var def)
                  && def.ValueDescriptions.TryGetValue(Math.Round(v), out var word) ? $"{name}: {word}" : name;
            if (button.Text != label) { button.Text = label; button.AccessibleName = label; }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refresh.Stop();
        _refresh.Dispose();
        base.OnFormClosing(e);
    }
}
