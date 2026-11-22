using System.ComponentModel;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;
using WinKeys = System.Windows.Forms.Keys;

namespace MSFSBlindAssist.Forms.IniA330;

/// <summary>A cockpit control under a display window's list (spec D9). With <see cref="PressValue"/>: a button that
/// presses the control to that value. Without: a choice list of the control's positions.</summary>
internal sealed record A330KeyRow(string VarKey, double? PressValue = null);

/// <summary>
/// The live window every A330 display window derives from (MCDU, E/WD, lower ECAM, PFD, ND, ISIS, DCDU): one list,
/// named <c>listName</c>, that shows the display, then the cockpit controls that change it. Frozen after plan 02: what
/// follows is the contract the derived windows build on.
/// </summary>
/// <remarks>
/// <para>The list. At every refresh (every <c>refreshMs</c> on a UI timer while the window is shown, and on F5) the
/// list is reconciled in place (<see cref="DisplayListBox.SetLines"/>), so only a changed row is rewritten and the
/// pilot keeps their row. It shows, in this order of precedence:
/// (1) the display's latest frame, complete: <see cref="Rows"/> of it, preceded by "Colours not read yet" when the
/// window <see cref="NeedsColours"/> and no text of the frame has a colour, and by "List rows not read yet" when it
/// <see cref="NeedsHiddenRowsLeftOut"/> and the frame does not leave hidden rows out (both: colours first; spec M14, M15);
/// (2) else, while the reader is not Running, one row, its status ("A330 displays starting");
/// (3) else, when the display's newest frame is not drawn, one row, "&lt;Display&gt; is off" (cold and dark no display
/// draws anything: a state, not a failure);
/// (4) else one row, "Reading the &lt;display&gt;", and once <see cref="CannotReadAfterMs"/> have passed with no
/// complete frame, "The display reader cannot read the &lt;display&gt;". That count runs from when the window began
/// wanting the display or from when the reader became Running, whichever is later; a complete or a not-drawn frame
/// starts it again. The first refresh with rows selects row 0, so the reader has a current row. A
/// <see cref="Rows"/> that throws, or gives a null row, keeps the rows shown (logged once).</para>
/// <para>The display. While the window is shown it wants its display (<see cref="IA330Displays.Want"/>, with itself
/// as the owner and <c>refreshMs</c>); hidden, closed or disposed, it releases it, once. It polls on its UI timer and
/// subscribes to no event of the displays, so nothing here marshals from another thread.</para>
/// <para>The keys under the list, after it in the tab order and in <see cref="Keys"/> order: a button labelled from
/// <see cref="IniA330Definition.DescribeControl"/> ("STS: On"; the label alone while the state is unread or the key has
/// none) and pressed through <see cref="IniA330Definition.PressFromWindow"/>; or a label and a choice list of the
/// control's positions, in the panel's order, which shows the control's position (none while unread), follows a change
/// of it, and presses only on the pilot's own choice, never on a selection the window sets. A label is set only when
/// its text changes. A key that is no control is left out.</para>
/// <para>Silence. The window never calls the announcer: the screen reader reads the title, the list and the controls,
/// so nothing is spoken on open, refresh, F5 or close ([CORE-7]). A press's read-back is the definition's own.</para>
/// <para>Keys. F5 refreshes now (once per press); Escape closes the window, unless it is closing a choice list's open
/// list, and the window that had the foreground before <see cref="ShowForm"/> gets it back.</para>
/// <para>For a derived window. <see cref="Rows"/>, <see cref="Keys"/>, <see cref="NeedsColours"/> and
/// <see cref="NeedsHiddenRowsLeftOut"/> are read only after the constructors have run (<see cref="Keys"/> once, as the
/// window first loads or refreshes), so they may answer from the derived window's own fields. A derived window takes its
/// own keys by overriding <see cref="ProcessCmdKey"/> and calling the base for the rest. Inside it, <c>Keys</c> is the
/// property: name the key enumeration <see cref="System.Windows.Forms.Keys"/> in full.</para>
/// </remarks>
internal abstract class A330DisplayWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Bit 30 of a key message's lParam: set when the key was already down, so the message is an auto-repeat.
    private const long KeyWasDown = 1L << 30;

    /// <summary>How long a wanted display may give no complete frame before the window says the reader cannot read it.</summary>
    internal const int CannotReadAfterMs = 30_000;

    private const string ColoursNotReadRow = "Colours not read yet";
    private const string ListRowsNotReadRow = "List rows not read yet";

    private readonly IA330Displays _displays;
    private readonly int _refreshMs;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<Action<bool>> _keyFollowers = new();
    private bool _keysBuilt;
    private bool _wanting;
    private long? _countFrom;
    private bool _rowsFailureLogged;
    private IntPtr _previousWindow;

    /// <param name="title">The window's title.</param>
    /// <param name="listName">The list's name and accessible name.</param>
    /// <param name="aircraft">The A330 whose displays (<see cref="IniA330Definition.Displays"/>, taken as the window is
    /// built, so a test sets its replay first) and controls the window uses.</param>
    /// <param name="sim">For the controls' states and presses.</param>
    /// <param name="announcer">For the presses' own read-backs; the window itself never speaks.</param>
    /// <param name="gauge">The display the window shows.</param>
    /// <param name="refreshMs">How often the window refreshes, and the refresh it wants the display at.</param>
    protected A330DisplayWindow(string title, string listName, IniA330Definition aircraft, SimConnectManager sim,
        ScreenReaderAnnouncer announcer, A330Gauge gauge, int refreshMs = 250)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(listName);
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshMs);

        Aircraft = aircraft;
        Sim = sim;
        Announcer = announcer;
        Gauge = gauge;
        _refreshMs = refreshMs;
        _displays = aircraft.Displays;

        Text = title;
        Size = new Size(560, 440);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = true;
        KeyPreview = true;

        RowList = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Name = listName,
            AccessibleName = listName,
            TabIndex = 0,
        };
        Controls.Add(RowList);

        _timer = new System.Windows.Forms.Timer { Interval = refreshMs };
        _timer.Tick += (_, _) => { if (Visible) RefreshNow(); };
    }

    /// <summary>The A330 the window belongs to.</summary>
    protected IniA330Definition Aircraft { get; }

    protected SimConnectManager Sim { get; }

    /// <summary>For a derived window's read-back of what the screen reader cannot say ([CORE-7]); the base never speaks.</summary>
    protected ScreenReaderAnnouncer Announcer { get; }

    /// <summary>The display the window shows.</summary>
    protected A330Gauge Gauge { get; }

    /// <summary>The display's list, for a derived window to set up from its constructor (for example
    /// <see cref="DisplayListBox.SuppressTypeAhead"/> where its character keys are input).</summary>
    protected DisplayListBox RowList { get; }

    /// <summary>The clock, milliseconds of <see cref="Environment.TickCount64"/>; a test moves it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    /// <summary>The list's lines from a complete frame, the top of the display first. UI thread, at every refresh.</summary>
    protected abstract IReadOnlyList<string> Rows(A330Frame frame);

    /// <summary>The cockpit controls under the list (spec D9), in order. Read once.</summary>
    protected virtual IReadOnlyList<A330KeyRow> Keys => [];

    /// <summary>True for a window whose rows need the frame's colours: until the reader gives them it says so (spec M14).</summary>
    protected virtual bool NeedsColours => false;

    /// <summary>True for a window whose rows need the hidden list rows left out: until the reader does it says so (spec M15).</summary>
    protected virtual bool NeedsHiddenRowsLeftOut => false;

    /// <summary>Shows the window, or brings the open one forward, with focus on the list: the pilot's own open, and the
    /// only focus call in the window.</summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        // A second press of the key brings the window forward even when the pilot had minimized it.
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        BringToFront();
        Activate();
        // The key opens this while the simulator has the foreground, where Activate alone can leave the window behind it.
        TopMost = true;
        TopMost = false;
        RowList.Focus();
    }

    /// <summary>Reads the display and the controls again and shows them (the timer and F5). UI thread. Never speaks,
    /// never takes focus; after the window is disposed it does nothing.</summary>
    internal void RefreshNow() => ShowCurrent(resyncChoices: false);

    private void ShowCurrent(bool resyncChoices)
    {
        if (IsDisposed || Disposing) return;
        EnsureKeys();
        if (CurrentRows() is { } rows)
        {
            RowList.SetLines(rows);
            if (RowList.SelectedIndex < 0 && RowList.Items.Count > 0) RowList.SelectedIndex = 0;
        }
        foreach (var follow in _keyFollowers) follow(resyncChoices);
    }

    /// <summary>The rows to show now, or null to keep the rows shown.</summary>
    private IReadOnlyList<string>? CurrentRows()
    {
        long now = NowMs();
        string name = A330GaugeNames.Name(Gauge);

        if (_displays.Latest(Gauge) is { Complete: true } frame)
        {
            _countFrom = now;
            IReadOnlyList<string> rows;
            try
            {
                rows = Rows(frame);
                if (rows is null || rows.Any(r => r is null)) throw new InvalidOperationException("a row is null");
            }
            catch (Exception ex)
            {
                if (!_rowsFailureLogged)
                {
                    _rowsFailureLogged = true;
                    Log.Warn("a330", $"{Text}: the rows of a frame failed, the rows shown stay ({ex.GetType().Name}: {ex.Message})");
                }
                return null;
            }

            bool colours = NeedsColours && !frame.Texts.Any(t => t.Colour is not null);
            bool listRows = NeedsHiddenRowsLeftOut && !frame.HiddenRowsLeftOut;
            if (!colours && !listRows) return rows;
            var shown = new List<string>(rows.Count + 2);
            if (colours) shown.Add(ColoursNotReadRow);
            if (listRows) shown.Add(ListRowsNotReadRow);
            shown.AddRange(rows);
            return shown;
        }

        if (_displays.State != A330ReaderState.Running)
        {
            _countFrom = null;   // the count runs from when the reader is Running again
            return [_displays.StatusText];
        }

        if (_displays.Newest(Gauge) is { Drawn: false })
        {
            _countFrom = now;
            return [char.ToUpperInvariant(name[0]) + name[1..] + " is off"];
        }

        _countFrom ??= now;
        return now - _countFrom.Value >= CannotReadAfterMs
            ? ["The display reader cannot read the " + name]
            : ["Reading the " + name];
    }

    // ---- the keys under the list ----

    private void EnsureKeys()
    {
        if (_keysBuilt) return;
        _keysBuilt = true;

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Bottom,
            Name = RowList.Name + " controls",
        };
        foreach (var key in Keys)
        {
            if (Aircraft.DescribeControl(key.VarKey, Sim) is not { } described)
            {
                Log.Warn("a330", $"{Text}: {key.VarKey} is no cockpit control; it is left out.");
                continue;
            }
            if (key.PressValue is double value) AddButton(panel, key.VarKey, value, described);
            else AddChoice(panel, key.VarKey, described);
        }
        if (panel.Controls.Count == 0)
        {
            panel.Dispose();
            return;
        }
        for (int i = 0; i < panel.Controls.Count; i++) panel.Controls[i].TabIndex = i;
        // The list fills what the docked panel leaves, and Windows Forms docks the last control added first, so the panel
        // sits below the list; it follows the list in the tab order.
        Controls.Add(panel);
        panel.TabIndex = 1;
    }

    private static string Caption((string Label, string? State) control) =>
        string.IsNullOrEmpty(control.State) ? control.Label : control.Label + ": " + control.State;

    private void AddButton(FlowLayoutPanel panel, string varKey, double value, (string Label, string? State) described)
    {
        var button = new Button { Name = varKey, AutoSize = true, UseMnemonic = false, Text = Caption(described) };
        button.Click += (_, _) => Aircraft.PressFromWindow(varKey, value, Sim, Announcer);
        panel.Controls.Add(button);
        _keyFollowers.Add(_ =>
        {
            if (Aircraft.DescribeControl(varKey, Sim) is not { } now) return;
            string caption = Caption(now);
            if (!string.Equals(button.Text, caption, StringComparison.Ordinal)) button.Text = caption;
        });
    }

    private void AddChoice(FlowLayoutPanel panel, string varKey, (string Label, string? State) described)
    {
        if (!Aircraft.GetVariables().TryGetValue(varKey, out var def) || def.ValueDescriptions.Count == 0)
        {
            Log.Warn("a330", $"{Text}: {varKey} has no positions; its choice list is left out.");
            return;
        }

        // The panel's own list: by value (reversed when the control says so), one entry for each distinct position name,
        // which presses the first value of that name.
        var ordered = def.ReverseDisplayOrder
            ? def.ValueDescriptions.OrderByDescending(p => p.Key)
            : def.ValueDescriptions.OrderBy(p => p.Key);
        var names = new List<string>();
        var values = new List<double>();
        foreach (var (position, positionName) in ordered)
        {
            if (names.Contains(positionName)) continue;
            names.Add(positionName);
            values.Add(position);
        }

        var label = new Label
        {
            Name = varKey + " label", Text = described.Label, AutoSize = true, UseMnemonic = false,
            Margin = new Padding(3, 7, 0, 3),
        };
        var combo = new ComboBox { Name = varKey, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = described.Label };
        foreach (var positionName in names) combo.Items.Add(positionName);
        combo.Width = Math.Max(120, names.Max(n => TextRenderer.MeasureText(n, combo.Font).Width) + 30);
        panel.Controls.Add(label);
        panel.Controls.Add(combo);

        int shown = -1;                       // the position the list shows: set by the window or chosen by the pilot
        bool showing = false;                 // true while the window sets it
        string? followed = described.State;   // the control's state the list last followed
        void ShowPosition(string? state)
        {
            int i = state is null ? -1 : names.IndexOf(state);
            shown = i;
            if (combo.SelectedIndex == i) return;
            showing = true;
            try { combo.SelectedIndex = i; }
            finally { showing = false; }
        }

        combo.SelectedIndexChanged += (_, _) =>
        {
            int i = combo.SelectedIndex;
            // A selection the window set, the list's own replay of it when its handle is made, or none: never a press.
            if (showing || i < 0 || i == shown) return;
            shown = i;
            Aircraft.PressFromWindow(varKey, values[i], Sim, Announcer);
        };
        ShowPosition(described.State);

        _keyFollowers.Add(resync =>
        {
            if (Aircraft.DescribeControl(varKey, Sim) is not { } now) return;
            if (!string.Equals(combo.AccessibleName, now.Label, StringComparison.Ordinal))
            {
                combo.AccessibleName = now.Label;
                label.Text = now.Label;
            }
            // The pilot's choice stays shown until the control's state changes (its read-back), as on the panel; while the
            // pilot has the list open it is left alone, and it follows once the list is closed.
            if (combo.DroppedDown) return;
            if (!resync && string.Equals(now.State, followed, StringComparison.Ordinal)) return;
            followed = now.State;
            ShowPosition(now.State);
        });
    }

    // ---- showing, hiding and closing ----

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshNow();   // the keys and the rows are there before the window first shows
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (IsDisposed || Disposing) return;
        if (Visible) StartWanting();
        else StopWanting();
    }

    private void StartWanting()
    {
        if (!_wanting)
        {
            _wanting = true;
            _countFrom = NowMs();
            _displays.Want(Gauge, this, _refreshMs);
        }
        ShowCurrent(resyncChoices: true);   // each choice list shows the control's position as it is now
        _timer.Start();
    }

    private void StopWanting()
    {
        _timer?.Stop();
        if (!_wanting) return;
        _wanting = false;
        _displays.Release(Gauge, this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopWanting();
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        _previousWindow = IntPtr.Zero;
    }

    protected override bool ProcessCmdKey(ref Message msg, WinKeys keyData)
    {
        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            // Escape closes, unless it is closing a choice list's open list.
            if (keyData == WinKeys.Escape && ActiveControl is not ComboBox { DroppedDown: true })
            {
                Close();
                return true;
            }
            if (keyData == WinKeys.F5)
            {
                // Once per press: a held key repeats, and each repeat has the already-down bit set.
                if ((msg.LParam.ToInt64() & KeyWasDown) == 0) RefreshNow();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWanting();
            _timer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
