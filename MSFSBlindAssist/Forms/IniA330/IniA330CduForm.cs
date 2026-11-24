using System.ComponentModel;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using MSFSBlindAssist.Forms.FenixA320;
using MSFSBlindAssist.Forms.MD11;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.IniA330;

/// <summary>
/// The accessible MCDU window of the iniBuilds A330, either side (<see cref="CduFormProfile"/>).
/// </summary>
/// <remarks>
/// <para>The page. While the window is shown it wants its side's MCDU from the display reader
/// (<see cref="IA330Displays.Want"/>, every 250 ms) and, on a 250 ms UI timer, lists the latest complete frame
/// (<see cref="IA330Displays.Latest"/>) as its listing (<see cref="CduLineText.Listing"/>: "Title: ..." with the page
/// counter where the page draws one, each label row, each data row "n: value", "Scratchpad: ...", every row as drawn,
/// as the Fenix and FlyByWire windows list a page), reconciled in place so the pilot keeps their
/// row. Only a frame that leaves hidden rows out (spec M15) and has every text on the grid is a page. The reader sends
/// each new frame once as read and then, moments later, with its hidden rows left out, and now and then a frame with a
/// text off the grid; meanwhile, for up to <see cref="HoldPageMs"/> after the last page read, the page shown stays as
/// it is and nothing is said of it, as the Fenix window keeps its last screen. With
/// no complete frame (the client gives none once the newest is three refreshes old), a frame off the MCDU's grid, or no
/// page within that time, the list is one status row instead, in the display windows' own words
/// (<see cref="A330McduStatus"/>: "Reading the captain's MCDU" for any complete frame that does not leave hidden rows
/// out). The status box above the list ("Connection status", as the Fenix and FlyByWire windows name theirs) says the
/// same, or over a page "MCDU: Connected". It never shows half a page.</para>
/// <para>What it says: what the Fenix and FlyByWire MCDU windows say, in their words, and nothing more (owner,
/// 2026-11-23; [CORE-7]: once, only what the screen reader cannot say, through the non-interrupting announce). While
/// the window is shown and the reader cannot read the MCDU (a lasting state, or 30 s with no page): "MCDU
/// disconnected", once, and "MCDU connected" with the first page after it (spec D3, never an AI read); the status box
/// keeps the reason. A new title, said without the list's "Title: ", its words only (no slew arrows, no page counter:
/// <see cref="CduLineText.SpokenTitle"/>, as the FlyByWire window), whenever they differ from the last title said:
/// opening the window on a page changed while it was closed, or on its first page, says it too; the selection then
/// goes to the first line after the title, as on the Fenix and FlyByWire windows. A line key, a page key, a slew and
/// AIRPORT say nothing of their own: the title and the scratchpad line say what changed, and the pilot reads the list.
/// The page's
/// scratchpad line, read back when it changes (two stable refreshes, <see cref="CduScratchpadAnnouncer"/>), without the
/// F-PLN's slew arrows at its end (on the screen and listed, but no text on the scratchpad). A typed entry, confirmed
/// from the MCDU's own typed line (<see cref="IA330Displays.McduScratchpad"/>), read from 150 ms after the key before
/// (so a line key or CLR sent just before the entry has shown): each key is sent only once the one before has shown,
/// at least 150 ms apart, and waited for up to 600 ms; then the typed line is said once (what the other windows'
/// scratchpad read-back says), and a key that does not show stops the entry, the typed line then said as it stands.
/// What was said is then not said again by the scratchpad line's read-back (the line showing it, or part of it while
/// the frames catch up) until the next key that is not part of an entry. With no typed line from the reader the keys
/// go at 150 ms a key, and the page's scratchpad line read back is the confirmation. Hiding, closing or disposing the
/// window, or a side switch, stops an entry: its other keys are not sent
/// and nothing is said for it. CLR (Backspace, the CLR button) says nothing of its own: the scratchpad line's read-back
/// says what is left, "Scratchpad cleared", or the next queued message.</para>
/// <para>Keys. The aircraft's own <c>INI_MCDU1_&lt;KEY&gt;</c> (captain) or <c>INI_MCDU2_&lt;KEY&gt;</c> (first officer)
/// press flags: write 1, the aircraft consumes it and resets it to 0 (writing 0 straight after kills the press,
/// measured). Every write is sequence-prefixed so a repeated key is never coalesced by the MobiFlight channel. Keys are
/// sent whether or not a page can be read, but for a slew past a list's top or end. Same layout and keys as the other
/// Airbus CDU windows: Ctrl/Alt+1..6 (or F1..F12) line keys, Alt+letter pages, Page Up/Down slew, Alt+Left/Right page
/// (and Left/Right on the screen, as on the Fenix and FlyByWire windows), Alt+S scratchpad, Alt+Home the screen,
/// Backspace CLR, Escape closes (hides) the window and gives the foreground back.</para>
/// <para>Slews. The aircraft counts a slew pressed past a list's top or end, and the next slews the other way only undo
/// it, so the list looks stuck (measured 2026-11-20). On a page whose frame has a list the display clips
/// (<see cref="A330Frame.List"/>), Page Up and Alt+Up (the MCDU's up arrow, which moves the list on to later rows) are
/// sent only while rows of it are hidden below (<see cref="A330Frame.ListBelow"/>), Page Down and Alt+Down (back toward
/// the top) only while rows are hidden above (<see cref="A330Frame.ListAbove"/>); otherwise nothing is sent and nothing
/// is said: at a list's end a press does nothing, as on the other MCDU windows (owner, 2026-11-23: no top or end of
/// list words). A slew is judged on a page: while the newest frame is one the reader has not yet left hidden rows out
/// of, the press waits for one that has, up to <see cref="SlewShowsWithinMs"/>, then is judged on the page shown. One
/// slew at a time each way: after a slew sent on a list, the next slew press the same way waits, as long, for a page
/// whose texts differ from the newest page when that slew went out; with none, the list did not move, and the press is
/// not sent, as each later press that way is not until a page differs. A press the other way does not wait for the
/// slew before it: it is judged on the page as it is, so a slew back from a list's end goes; sent, it undoes the slew
/// before it, which no press then waits for; refused, that slew is still awaited. Any change of the page's texts
/// counts as a new page, a new scratchpad message included. With no frame (the frames stopping during a wait
/// included), no page shown, or a page whose frame has no such list (or does not say), the slew keys go as they always
/// have. The Slew up and Slew down buttons are the same keys.</para>
/// <para>Sides, as the Fenix window: a Left/Right selector after the status box, and Ctrl+Shift+L / Ctrl+Shift+R. A
/// switch lets the old side's display go, wants the new one, switches the key prefix, starts the title and scratchpad
/// read-backs afresh and, off the selector, says "Left MCDU" or "Right MCDU"; then, as the Fenix window does, the new
/// side's page title (through the title path above) and its scratchpad line when it holds something, once. The
/// shortcut to the side already shown says the side again.</para>
/// <para>Everything runs on the UI thread: the key writes, and the waits, which are awaited there
/// ([SIM-20]). The window polls; it subscribes to no event of the displays.</para>
/// </remarks>
public sealed class IniA330CduForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly LogChannel _log = Log.Channel("a330_mcdu");

    private const int RefreshMs = 250;
    private const int KeyGapMs = 150;          // today's typing pace
    private const int KeyShowsWithinMs = 600;  // how long a typed key may take to show on the typed line
    private const int KeyPollMs = 25;
    private const int TypedEchoMs = 3 * RefreshMs;   // the page's frames show an unconfirmed entry within three refreshes

    /// <summary>How long a slew may take to show as a page (the reader's frame, then the same with its hidden rows left
    /// out) before the list is taken as not moved.</summary>
    internal const int SlewShowsWithinMs = 1500;

    /// <summary>How long after the last page read the page shown stays while the newest frames are ones the reader has
    /// not left hidden rows out of yet (it does within moments, or tries again 2 s after a frame it could not): the
    /// owner's ceiling for a display to refresh.</summary>
    internal const int HoldPageMs = 3000;

    private readonly IA330Displays _displays;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private CduFormProfile _profile;

    private readonly A330McduStatus _status = new(RefreshMs);
    private readonly A330McduAvailability _availability = new();
    private readonly CduScratchpadAnnouncer _scratch = new("Scratchpad cleared", stablePolls: 2);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshMs };
    private readonly SemaphoreSlim _keyGate = new(1, 1);

    private readonly TextBox _statusBox = new()
    {
        Name = "Status", ReadOnly = true, TabStop = true, Width = 420, AccessibleName = "Connection status",
    };
    private readonly ComboBox _side = new()
    {
        Name = "Side", DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, AccessibleName = "MCDU selector",
    };
    // The Fenix and FlyByWire windows' accessible names.
    private readonly ListBox _display = new()
    {
        Name = "Screen", IntegralHeight = false, Font = new Font("Consolas", 11f), AccessibleName = "MCDU Display",
    };
    private readonly TextBox _scratchpad = new() { Name = "Scratchpad", AccessibleName = "MCDU Input" };

    private IntPtr _previousWindow;
    private bool _wanting, _disposed, _typing, _offGridLogged;
    private A330Gauge _wantedGauge;
    private IReadOnlyList<A330McduLine>? _shownPage;   // the page listed now; null while the status row is
    private A330Frame? _shownFrame;                    // the frame that page came from; null while the status row is
    private (A330Frame Frame, bool Up)? _slewPending;  // a slew sent on a list, its way and the newest page then, until a page differs
    private long _pageReadAt;                          // when the page shown was last read
    private string _lastTitle = "";
    private int? _rowBeforeGap;                        // the pilot's row when a page gave way to the status row
    private string? _alreadySaid;                      // the typed line the window last said for an entry, until the next key
    private int _entryGeneration;                      // moves on when the window hides or switches side: an entry stops
    private long? _lastKeyMs;
    private int _seq;

    /// <param name="displays">The A330's displays (the definition's <c>Displays</c>).</param>
    /// <param name="sim">For the key writes.</param>
    /// <param name="announcer">For what the window says.</param>
    /// <param name="profile">The side the window opens on, and its keys.</param>
    internal IniA330CduForm(IA330Displays displays, SimConnectManager sim, ScreenReaderAnnouncer announcer, CduFormProfile profile)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(profile);
        _displays = displays;
        _sim = sim;
        _announcer = announcer;
        _profile = profile;
        _wantedGauge = profile.Gauge;
        SendPressFlag = WritePressFlag;
        ScreenHasFocus = () => _display.Focused;

        SetTitle();
        KeyPreview = true;
        Width = 760; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        _timer.Tick += (_, _) => { if (Visible) RefreshNow(); };
        KeyDown += Form_KeyDown;
        _scratchpad.KeyDown += Scratchpad_KeyDown;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
            }
        };
    }

    // ---- test seams ----

    /// <summary>The clock, milliseconds of <see cref="Environment.TickCount64"/>; a test moves it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    /// <summary>The waits between keys and for a key to show; awaited on the UI thread. A test runs them at once.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<int, Task> Delay { get; set; } = ms => Task.Delay(ms);

    /// <summary>Writes one press flag (the whole L:var name, "INI_MCDU1_INIT"); false when it could not be sent. A test records them.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, bool> SendPressFlag { get; set; }

    /// <summary>True while the screen list has the focus (its own <see cref="Control.Focused"/>), where Left and Right turn
    /// the page; a test sets it, never taking the focus.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool> ScreenHasFocus { get; set; }

    /// <summary>True: shown without taking the focus (the tests never take the focus of whoever is at the computer).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowInactive { get; set; }

    protected override bool ShowWithoutActivation => ShowInactive;

    // ---- layout ----

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, TabIndex = 0 };
        _statusBox.TabIndex = 0;
        // No mnemonic: every Alt+letter belongs to a page key.
        var sideLabel = new Label { Text = "Side:", AutoSize = true, UseMnemonic = false, Margin = new Padding(12, 6, 0, 0), TabIndex = 1 };
        _side.TabIndex = 2;
        _side.Items.AddRange(new object[] { "Left", "Right" });
        _side.SelectedIndex = _profile.FirstOfficer ? 1 : 0;
        _side.SelectedIndexChanged += (_, _) => OnSideSelected();
        top.Controls.Add(_statusBox);
        top.Controls.Add(sideLabel);
        top.Controls.Add(_side);

        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, TabIndex = 3 };
        foreach (var (label, key, tip) in _profile.PageKeys)
        {
            var b = new Button { Text = label, AutoSize = true, AccessibleDescription = tip };
            string k = key;
            b.Click += (_, _) => { if (k == "CLR") Clear(); else Press(k); };
            panel.Controls.Add(b);
        }
        var scratchRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, TabIndex = 2 };
        scratchRow.Controls.Add(new Label { Text = "&Scratchpad:", AutoSize = true });
        _scratchpad.Width = 400;
        scratchRow.Controls.Add(_scratchpad);
        _display.Dock = DockStyle.Fill;
        _display.TabIndex = 1;
        Controls.Add(_display);
        Controls.Add(scratchRow);
        Controls.Add(panel);
        Controls.Add(top);
    }

    private string SideName => _profile.FirstOfficer ? "Right" : "Left";

    private void SetTitle()
    {
        Text = $"{_profile.Name} {_profile.Device} ({SideName})";
        AccessibleName = Text;
    }

    // ---- showing and hiding ----

    /// <summary>Shows the window, or brings the open one forward, with focus on the screen.</summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        BringToFront();
        Activate();
        // The key opens this while the simulator has the foreground, where Activate alone can leave the window behind it.
        TopMost = true;
        TopMost = false;
        _display.Focus();
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
            _wantedGauge = _profile.Gauge;
            _displays.Want(_wantedGauge, this, RefreshMs);
        }
        _status.Restart(NowMs());
        _availability.Opened();
        StartReadBacksAfresh();
        RefreshNow();
        _timer.Start();
    }

    private void StopWanting()
    {
        _timer.Stop();
        _entryGeneration++;   // an entry in progress stops: hidden, closed or disposed
        if (!_wanting) return;
        _wanting = false;
        _displays.Release(_wantedGauge, this);
    }

    /// <summary>A new open or a side switch: the scratchpad line is read silently first (a side switch then says it,
    /// <see cref="OnSideSelected"/>). The last title said is kept across an open, as the Fenix and FlyByWire windows
    /// keep it: a page that changed while the window was closed is said when it opens.</summary>
    private void StartReadBacksAfresh()
    {
        _scratch.Reset();
        _rowBeforeGap = null;
        _alreadySaid = null;
        _slewPending = null;
    }

    // ---- sides ----

    private void OnSideSelected()
    {
        bool firstOfficer = _side.SelectedIndex == 1;
        if (firstOfficer == _profile.FirstOfficer) return;
        _entryGeneration++;   // an entry in progress stops: its other keys go to neither side
        _profile = _profile.ForSide(firstOfficer);
        SetTitle();
        if (_wanting)
        {
            _displays.Release(_wantedGauge, this);
            _wantedGauge = _profile.Gauge;
            _displays.Want(_wantedGauge, this, RefreshMs);
        }
        _status.Restart(NowMs());
        StartReadBacksAfresh();
        // The Fenix window's side switch: the new side's page title is said, through the title path as any new title,
        // and its scratchpad line when it holds something: read back from an empty starting point, so a message waiting
        // on that side is said once its line reads the same twice, and an empty line says nothing.
        _lastTitle = "";
        _scratch.OnPoll("", Utc(NowMs()));
        // Off the selector, say which MCDU this is first; on it the screen reader has just read the new item ([CORE-7]).
        if (!_side.Focused) _announcer.Announce($"{SideName} {_profile.Device}");
        if (Visible) RefreshNow();
    }

    // ---- the page ----

    /// <summary>Reads the side's display and shows it (the timer). UI thread. After the window is disposed it does nothing.</summary>
    internal void RefreshNow()
    {
        if (IsDisposed || Disposing) return;
        long now = NowMs();
        var gauge = _profile.Gauge;
        var frame = _displays.Latest(gauge);
        var screen = frame is { Complete: true } ? A330McduScreen.From(frame) : null;
        if (frame is { Complete: true } && screen is null && !_offGridLogged)
        {
            _offGridLogged = true;
            _log.Warn($"{A330GaugeNames.Name(gauge)}: a complete frame with a text off the MCDU's grid ({A330McduScreen.OffGrid(frame)}, "
                + $"{(frame.HiddenRowsLeftOut ? "its hidden rows left out" : "its hidden rows not left out")}); the page shown is kept up to 3 s");
        }

        if (frame is not null && screen is { HiddenRowsLeftOut: true })
        {
            _status.Restart(now);
            _pageReadAt = now;
            var page = A330McduLines.From(screen);
            SetStatus(A330McduStatus.PageRow);
            _shownFrame = frame;
            // A page that differs from the one a slew went out on: the next slew is judged on the page as it is. Any
            // change of the texts counts (SameTexts, the client's own test of a changed frame), a new scratchpad message
            // included, so a press can then be judged on a page that has not shown the slew yet.
            if (_slewPending is { } pending && !A330ReaderClient.SameTexts(pending.Frame.Texts, frame.Texts)) _slewPending = null;
            Speak(_availability.Next(null, pageShown: true));   // "MCDU connected" before the page's title, as on the FlyByWire window
            ShowPage(page);
            FeedScratchpad(page, now);
            return;
        }

        // A complete frame that is not a page: rows the MCDU does not show may be in it (spec M15), or a text of it is off
        // the MCDU's grid. No page rows, and nothing said of them. The reader sends every new frame once before it has
        // left those rows out, and now and then a frame with a text off the grid, so the page shown stays for a while,
        // unchanged and unspoken, as the Fenix window keeps its last screen. A frame that does not leave hidden rows out
        // is the reader at work (a list page's hidden rows can sit below the screen, off the grid): it restarts the
        // "cannot read" count as a page does (as A330DisplayWindow's does); one off the grid with its rows left out
        // does not, so a layout the window does not know ends in "cannot read".
        if (frame is { Complete: true })
        {
            if (!frame.HiddenRowsLeftOut) _status.Restart(now);
            if (_shownPage is not null && now - _pageReadAt < HoldPageMs) return;
        }
        string row;
        string? reason = null;
        if (frame is { Complete: true, HiddenRowsLeftOut: false }) row = A330McduStatus.ReadingRow(gauge);
        else (row, reason) = _status.Row(_displays.State, _displays.StatusText, _displays.Newest(gauge), gauge, now);
        SetStatus(row);
        ShowStatusRow(row);
        Speak(_availability.Next(reason, pageShown: false));
    }

    /// <summary>True for a frame the window shows as a page: complete, on the MCDU's grid, its hidden rows left out.</summary>
    private static bool ShowsPage(A330Frame frame) =>
        frame.Complete && A330McduScreen.From(frame) is { HiddenRowsLeftOut: true };

    private void SetStatus(string s)
    {
        if (_statusBox.Text != s) _statusBox.Text = s;
    }

    private void ShowPage(IReadOnlyList<A330McduLine> page)
    {
        var listing = CduLineText.Listing(page);
        bool afterGap = _shownPage is null;
        int saved = _display.SelectedIndex;
        DisplayList.UpdateInPlace(_display, listing);
        _shownPage = page;

        // As said, and as a new page is told: the title's words, without the list's "Title: ", the slew arrows and the
        // page counter, as the FlyByWire window says its title apart from its page number and arrows.
        string title = CduLineText.SpokenTitle(page);
        int target;
        if (title.Length > 0 && title != _lastTitle)
        {
            // As the Fenix and FlyByWire windows: a new title is said (a temporary flight plan's carries TMPY), and the
            // selection goes to the first line after it, which the screen reader then reads.
            Speak(title);
            _lastTitle = title;
            target = 1;
        }
        else target = afterGap && _rowBeforeGap is int before ? before : Math.Max(saved, 0);
        _rowBeforeGap = null;
        if (_display.Items.Count == 0) return;
        target = Math.Min(target, _display.Items.Count - 1);
        if (_display.SelectedIndex != target) _display.SelectedIndex = target;
    }

    private void ShowStatusRow(string row)
    {
        if (_shownPage is not null && _display.SelectedIndex >= 0) _rowBeforeGap = _display.SelectedIndex;
        _shownPage = null;
        _shownFrame = null;
        DisplayList.UpdateInPlace(_display, [row]);
        if (_display.SelectedIndex != 0) _display.SelectedIndex = 0;
    }

    /// <summary>The page's scratchpad line read back when it changes; held while an entry is typed. After an entry the
    /// window has said, the line showing that text, or a start of it while the frames catch up, is taken silently as
    /// the line's starting point; any other change is said as usual.</summary>
    private void FeedScratchpad(IReadOnlyList<A330McduLine> page, long now)
    {
        if (_typing) return;
        string row = ScratchpadText(page[A330McduLines.ScratchpadRow]);
        if (_alreadySaid is { } said && said.StartsWith(row, StringComparison.Ordinal))
        {
            _scratch.Reset();
            _scratch.OnPoll(row, Utc(now));
            return;
        }
        Speak(_scratch.OnPoll(row, Utc(now)));
    }

    /// <summary>The scratchpad line as its read-back says it: its characters, without the F-PLN's slew arrows (the
    /// codes <c>e</c> and <c>f</c>, drawn at the line's end: on the screen and listed, but no text on the scratchpad), trimmed.</summary>
    private static string ScratchpadText(A330McduLine line)
    {
        var text = new System.Text.StringBuilder(line.Cells.Count);
        foreach (var cell in line.Cells)
            text.Append(cell is { } c && c.Character is not ('e' or 'f') ? A330McduCharacters.Display(c) : " ");
        return text.ToString().Trim();
    }

    /// <summary>A refresh's sentence, said only while the window is shown.</summary>
    private void Speak(string? text)
    {
        if (text is not null && _wanting && !IsDisposed) _announcer.Announce(text);
    }

    private static DateTime Utc(long ms) => new(Math.Max(0, ms) * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    // ---------------- keys ----------------

    private bool WritePressFlag(string flag)
    {
        if (!_sim.CalcWriteCanLand)
        {
            _announcer.AnnounceImmediate($"{_profile.Device} keys unavailable: the MobiFlight module is not responding.");
            return false;
        }
        _sim.ExecuteCalculatorCode($"{Interlocked.Increment(ref _seq)} 0 * 1 (>L:{flag})");
        return true;
    }

    private bool Send(CduFormProfile profile, string key)
    {
        if (!SendPressFlag(profile.KeyPrefix + key)) return false;
        _lastKeyMs = NowMs();
        return true;
    }

    /// <summary>Waits until <see cref="KeyGapMs"/> have passed since the last key.</summary>
    private async Task PaceAsync()
    {
        if (_lastKeyMs is not long last) return;
        long wait = KeyGapMs - (NowMs() - last);
        if (wait > 0) await Delay((int)wait);
    }

    private void Press(string key)
    {
        _alreadySaid = null;   // a key that is no part of an entry: whatever it brings to the scratchpad line is said
        _ = PressAsync(key);
    }

    private async Task PressAsync(string key)
    {
        var profile = _profile;   // the side the key was pressed on
        try
        {
            await _keyGate.WaitAsync();
            try
            {
                if (IsDisposed) return;
                bool slew = key == profile.SlewUp || key == profile.SlewDown;
                bool up = key == profile.SlewUp;
                A330Frame? list = null;
                if (slew && ReferenceEquals(profile, _profile))
                {
                    var (send, awaited) = await SlewAsync(profile, up);
                    if (!send) return;
                    list = awaited;
                }
                await PaceAsync();
                if (IsDisposed || !Send(profile, key)) return;
                // One slew at a time each way: on a list, the next slew this way waits for a page that differs from the
                // newest page now.
                if (slew)
                {
                    var newest = _displays.Latest(profile.Gauge);
                    _slewPending = list is not null && ReferenceEquals(profile, _profile)
                        ? (newest is not null && ShowsPage(newest) ? newest : list, up)
                        : null;
                }
            }
            finally { _keyGate.Release(); }
        }
        catch (Exception ex)
        {
            _log.Error($"key {key} failed: {ex.Message}");
        }
    }

    /// <summary>Whether a slew key of the side shown goes, and, when it goes, the list page the next slew its way is to
    /// wait on (null when the key goes as it always has, no page shown or a page whose frame has no list the display clips,
    /// and when it undoes an unshown slew the other way). On such a list Page Up goes only while rows are hidden below,
    /// Page Down only while rows are hidden above. While the newest frame is not yet a page (the reader has not left its
    /// hidden rows out), the press waits up to <see cref="SlewShowsWithinMs"/> for one, then is judged on the page shown.
    /// After a slew sent on a list, the next press the SAME way waits as long for a page that differs from the newest page
    /// then, and with none the list did not move; a press the other way does not wait for that slew. A key not sent is
    /// silent, as a press at a list's end is on the other MCDU windows; a window hidden, closed, disposed or switched to
    /// the other side during the wait sends nothing.</summary>
    private async Task<(bool Send, A330Frame? Awaited)> SlewAsync(CduFormProfile profile, bool up)
    {
        int generation = _entryGeneration;
        var pending = _slewPending is { } p && p.Up == up ? p : ((A330Frame Frame, bool Up)?)null;
        // Wait for a page: one the reader has left the hidden rows out of, and after a slew this way one that differs
        // from the page then (any change of its texts counts, see RefreshNow). No frame at all: no page, and the key goes
        // as it always has.
        long since = NowMs();
        while (_displays.Latest(profile.Gauge) is { } latest
               && (!ShowsPage(latest) || (pending is { } same && A330ReaderClient.SameTexts(latest.Texts, same.Frame.Texts))))
        {
            if (NowMs() - since >= SlewShowsWithinMs)
            {
                if (pending is not null) return (false, null);   // the slew before it never showed: the list did not move
                break;                                           // no page in time: judged on the page shown
            }
            await Delay(KeyPollMs);
            if (IsDisposed || _entryGeneration != generation) return (false, null);
        }
        if (pending is not null) _slewPending = null;
        // The page judged is the one shown, and the newest frame's.
        if (Visible) RefreshNow();
        if (_shownFrame is not { List: true } frame) return (true, null);
        // Refused, silently: an unshown slew the other way is still awaited by the next press its way.
        if (!(up ? frame.ListBelow : frame.ListAbove)) return (false, null);
        // Sent against an unshown slew the other way, it undoes that one: the list is back where the page shown has it,
        // so no press waits on either.
        bool undoes = _slewPending is { } other && other.Up != up;
        return (true, undoes ? null : frame);
    }

    /// <summary>CLR. It says nothing of its own: the scratchpad line's read-back says what CLR left.</summary>
    private void Clear()
    {
        _scratch.SuppressUntil = Utc(NowMs() + 400);
        Press("CLR");
    }

    /// <summary>Types <paramref name="text"/> on the MCDU of the side shown, confirmed from its typed line when the
    /// reader gives one. UI thread. Hiding, closing or disposing the window, or a side switch, stops it.</summary>
    internal async Task TypeAsync(string text)
    {
        var profile = _profile;
        var entry = new Entry(this, _entryGeneration);
        _alreadySaid = null;
        try
        {
            await _keyGate.WaitAsync();
            _typing = true;
            try
            {
                if (entry.Stopped) return;
                // The entry starts from the typed line as it is once the pace allows the first key, so a line key or
                // CLR sent just before the entry has shown on it.
                await PaceAsync();
                if (entry.Stopped) return;
                string? pad = _displays.McduScratchpad(profile.FirstOfficer);
                var keys = profile.KeysToType(text, pad ?? "");
                if (keys.Count == 0) return;
                if (pad is null) await TypeUnconfirmedAsync(profile, keys, entry);
                else await TypeConfirmedAsync(profile, keys, entry);
            }
            finally
            {
                _typing = false;
                _keyGate.Release();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"typing failed: {ex.Message}");
        }
    }

    /// <summary>One typed entry: stopped once the window is disposed, hidden or closed, or switched to the other side.</summary>
    private readonly record struct Entry(IniA330CduForm Window, int Generation)
    {
        public bool Stopped => Window.IsDisposed || Window._entryGeneration != Generation;
    }

    /// <summary>No typed line from the reader: every key at today's pace. The page's scratchpad line read back is the
    /// confirmation, as on the Fenix and FlyByWire windows; with no page shown nothing is said.</summary>
    private async Task TypeUnconfirmedAsync(CduFormProfile profile, IReadOnlyList<CduTypedKey> keys, Entry entry)
    {
        foreach (var k in keys)
        {
            await PaceAsync();
            if (entry.Stopped || !Send(profile, k.Key)) return;
        }
        _scratch.SuppressUntil = Utc(NowMs() + TypedEchoMs);
    }

    /// <summary>Each key once the one before has shown on the typed line; a key that does not show stops the entry, and
    /// the typed line is said as it stands (what the scratchpad read-back would say), or nothing when it is empty.</summary>
    private async Task TypeConfirmedAsync(CduFormProfile profile, IReadOnlyList<CduTypedKey> keys, Entry entry)
    {
        string? shown = null;
        foreach (var k in keys)
        {
            await PaceAsync();
            if (entry.Stopped || !Send(profile, k.Key)) return;
            (bool landed, shown) = await ShowsAsync(profile.FirstOfficer, k.Expected, NowMs(), entry);
            if (entry.Stopped) return;
            if (!landed)
            {
                _alreadySaid = shown;
                if (!string.IsNullOrEmpty(shown)) _announcer.Announce(shown);
                return;
            }
        }
        _alreadySaid = shown;
        _announcer.Announce(shown!);
    }

    /// <summary>Polls the typed line until it is <paramref name="expected"/>, for up to <see cref="KeyShowsWithinMs"/>.</summary>
    private async Task<(bool Landed, string? Pad)> ShowsAsync(bool firstOfficer, string expected, long sentAt, Entry entry)
    {
        while (true)
        {
            string? pad = _displays.McduScratchpad(firstOfficer);
            if (pad == expected) return (true, pad);
            if (NowMs() - sentAt >= KeyShowsWithinMs || entry.Stopped) return (false, pad);
            await Delay(KeyPollMs);
        }
    }

    /// <summary>A key as the message loop hands it to the window (its key handler); for the tests.</summary>
    internal void HandleKey(KeyEventArgs e) => Form_KeyDown(this, e);

    /// <summary>A key in the scratchpad entry box (its key handler); for the tests.</summary>
    internal void HandleScratchpadKey(KeyEventArgs e) => Scratchpad_KeyDown(_scratchpad, e);

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Shift+L / Ctrl+Shift+R: switch side from anywhere in the window, the selector included (the Fenix's
        // convention). Setting the selector runs its change handler, which says the side; the side already shown fires
        // no change, so it is confirmed here (off the selector only): silence would sound like a dropped key.
        if (FenixMCDUForm.SideShortcut.For(e.KeyCode, e.Control, e.Shift, e.Alt) is int side)
        {
            int index = side == FenixMcduSides.Right ? 1 : 0;
            if (index == _side.SelectedIndex)
            {
                if (!_side.Focused) _announcer.Announce($"{SideName} {_profile.Device}");
            }
            else _side.SelectedIndex = index;
            Handled(e);
            return;
        }

        // The selector keeps its own keyboard interface (Alt+Up/Down and F4 open its list, a letter picks an item).
        if (ActiveControl == _side && Md11McduForm.KeyRouting.ComboBoxNeedsKey(e.KeyCode, e.Alt, e.Control, e.Shift, _side.DroppedDown))
            return;

        bool alt = SettingsManager.Current.MCDUUseAlternateLSKKeys;
        string? key = null;
        if (alt)
        {
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6) key = $"LSK{e.KeyCode - Keys.F1 + 1}L";
            else if (!e.Control && !e.Alt && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12) key = $"LSK{e.KeyCode - Keys.F7 + 1}R";
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6) key = $"LSK{e.KeyCode - Keys.D1 + 1}L";
            else if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6) key = $"LSK{e.KeyCode - Keys.D1 + 1}R";
        }
        // The Airbus convention the A320 windows set (2026-10-17): Page Up/Down and Alt+Up/Down scroll
        // (the MCDU's up/down slew keys — the F-PLN is what a pilot scrolls most), Alt+Left/Right turn
        // the page (the MCDU's left/right arrow keys).
        if (key == null && !e.Control && ((!e.Alt && e.KeyCode == Keys.PageDown) || (e.Alt && e.KeyCode == Keys.Down))) key = _profile.SlewDown;
        if (key == null && !e.Control && ((!e.Alt && e.KeyCode == Keys.PageUp) || (e.Alt && e.KeyCode == Keys.Up))) key = _profile.SlewUp;
        if (key == null && e.Alt && !e.Control && e.KeyCode == Keys.Right) key = _profile.NextPage;
        if (key == null && e.Alt && !e.Control && e.KeyCode == Keys.Left) key = _profile.PrevPage;
        // Left and Right on the screen turn the page too, as on the Fenix and FlyByWire windows (a list has no use for them).
        if (key == null && !e.Alt && !e.Control && !e.Shift && ScreenHasFocus())
        {
            if (e.KeyCode == Keys.Right) key = _profile.NextPage;
            else if (e.KeyCode == Keys.Left) key = _profile.PrevPage;
        }
        // Alt+Shift+F: SEC F-PLN, as the Fenix and FlyByWire windows (handled here, so no mnemonic fires).
        if (key == null && e.Alt && !e.Control && e.Shift && e.KeyCode == Keys.F) key = "SEC";
        if (key == null && e.Alt && !e.Control && !e.Shift)
        {
            key = _profile.AltKeys.TryGetValue(e.KeyCode, out var page) ? page : null;
            if (key == null && e.KeyCode == Keys.S) { _scratchpad.Focus(); Handled(e); return; }
            if (key == null && e.KeyCode == Keys.Home) { _display.Focus(); Handled(e); return; }
        }
        if (key == null && e.KeyCode == Keys.Back && !e.Alt && ActiveControl == _display) { Clear(); Handled(e); return; }
        if (key == null && e.KeyCode == Keys.Escape) { Close(); Handled(e); return; }
        if (key != null) { Press(key); Handled(e); }
    }

    private static void Handled(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

    private void Scratchpad_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = _scratchpad.Text;
            _scratchpad.Clear();
            Handled(e);
            _ = TypeAsync(text);
        }
        else if (e.KeyCode == Keys.Back && _scratchpad.Text.Length == 0)
        {
            Clear();
            Handled(e);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopWanting();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            StopWanting();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
