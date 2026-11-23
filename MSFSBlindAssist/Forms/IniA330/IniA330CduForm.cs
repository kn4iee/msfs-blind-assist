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
/// counter where the page draws one, each label row, each data row "n: value", a nameless F-PLN waypoint "unnamed",
/// "Scratchpad: ...", as the Fenix and FlyByWire windows list a page), reconciled in place so the pilot keeps their
/// row. With no complete frame (the client gives none once the newest is three refreshes old), a frame off the MCDU's grid, or one
/// that does not leave hidden rows out (spec M15), the list is one status row instead (<see cref="A330McduStatus"/>:
/// the display windows' own words, or "List rows not read yet" for any complete frame that does not leave them out, on
/// the grid or off it, as a list page whose hidden rows sit below the screen is). The status box above the list says
/// the same, or over a page the display's name ("Captain's MCDU"), or "Colours not read yet" while the frame has no
/// colour (spec M14), when no prompt is marked unavailable. It never shows half a page.</para>
/// <para>What it says ([CORE-7]: once, only what the screen reader cannot say, through the non-interrupting announce).
/// Opening the window while the reader cannot read the MCDU: "A330 MCDU unavailable: &lt;reason&gt;", again only when
/// the reason changes, and "MCDU ready" with the first page after it (spec D3; never an AI read). A new title, unless
/// moving the focused list to it makes the screen reader read it; one that carries TMPY adds ", temporary flight plan",
/// or, when the screen reader reads the title, the window says "Temporary flight plan" alone; the title is said
/// without the list's "Title: ". After a line key with a page shown, what the pressed line now holds, or "No change"
/// when nothing on the page changed within 1.5 s, or nothing when only other rows changed
/// (<see cref="A330McduLineKeyWatch"/>). After AIRPORT with a page shown, the first waypoint of the F-PLN it moved to,
/// once (<see cref="A330McduAirportWatch"/>). The page's
/// scratchpad line, read back when it changes (two stable refreshes, <see cref="CduScratchpadAnnouncer"/>), without the
/// F-PLN's slew arrows at its end (on the screen and listed, but no text on the scratchpad). A typed entry, confirmed
/// from the MCDU's own typed line (<see cref="IA330Displays.McduScratchpad"/>), read from 150 ms after the key before
/// (so a line key or CLR sent just before the entry has shown): each key is sent only once the one before has shown,
/// at least 150 ms apart, and waited for up to 600 ms; then the typed line is said, or
/// "Missed &lt;KEY&gt;. Scratchpad: ..." and the rest of the entry is not sent. What was said is then not said again by
/// the scratchpad line's read-back (the line showing it, or part of it while the frames catch up) until the next key
/// that is not part of an entry. With no typed line from the reader the keys go at 150 ms a key, and the page's
/// scratchpad line read back is the confirmation; with no page shown either, the window says "Typed ..., not
/// confirmed". Hiding, closing or disposing the window, or a side switch, stops an entry: its other keys are not sent
/// and nothing is said for it. CLR (Backspace, the CLR button) while the scratchpad line shows a message (the line, not
/// the typed line; never a key's own echo: the CLR that CLR writes on an empty scratchpad, or the overfly mark alone):
/// "Message cleared: &lt;message&gt;" once a frame shows the line without it, within 1.5 s, and no
/// "Scratchpad cleared" for that change; the next queued message is read back as usual. With no typed line from the
/// reader, CLR says no "Message cleared". A slew key the window does not send (below): "End of list" or "Top of list",
/// once a press.</para>
/// <para>Keys. The aircraft's own <c>INI_MCDU1_&lt;KEY&gt;</c> (captain) or <c>INI_MCDU2_&lt;KEY&gt;</c> (first officer)
/// press flags: write 1, the aircraft consumes it and resets it to 0 (writing 0 straight after kills the press,
/// measured). Every write is sequence-prefixed so a repeated key is never coalesced by the MobiFlight channel. Keys are
/// sent whether or not a page can be read, but for a slew past a list's top or end. Same layout and keys as the other
/// Airbus CDU windows: Ctrl/Alt+1..6 (or F1..F12) line keys, Alt+letter pages, Page Up/Down slew, Alt+Left/Right page,
/// Alt+S scratchpad, Alt+Home the screen, Backspace CLR, Escape closes (hides) the window and gives the foreground back.</para>
/// <para>Slews. The aircraft counts a slew pressed past a list's top or end, and the next slews the other way only undo
/// it, so the list looks stuck (measured 2026-11-20). On a page whose frame has a list the display clips
/// (<see cref="A330Frame.List"/>), Page Up and Alt+Up (the MCDU's up arrow, which moves the list on to later rows) are
/// sent only while rows of it are hidden below (<see cref="A330Frame.ListBelow"/>), Page Down and Alt+Down (back toward
/// the top) only while rows are hidden above (<see cref="A330Frame.ListAbove"/>); otherwise the window says "End of
/// list" or "Top of list" and sends nothing. One slew at a time: after a slew sent on a list, the next slew press is
/// judged on the first frame that differs from the newest one when that slew went out, waited for up to 1 s; with none,
/// the list did not move, and the press says "End of list" or "Top of list" for its direction and sends nothing, as
/// each later press does until a frame differs. With no page shown, or on a page whose frame has no such list (or does
/// not say), the slew keys go as they always have. The Slew up and Slew down buttons are the same keys.</para>
/// <para>Sides, as the Fenix window: a Left/Right selector after the status box, and Ctrl+Shift+L / Ctrl+Shift+R. A
/// switch lets the old side's display go, wants the new one, switches the key prefix, starts the title and scratchpad
/// read-backs afresh and, off the selector, says "Left MCDU" or "Right MCDU"; then, as the Fenix window does, the new
/// side's page title (through the title path above) and its scratchpad line when it holds something, once. The
/// shortcut to the side already shown says the side again. Opening the window says no title.</para>
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
    private const int ClearShowsWithinMs = 1500;     // how long a cleared message may take to leave the scratchpad line
    private const int SlewShowsWithinMs = 1000;      // how long a slew may take to show before the list is taken as not moved

    /// <summary>Said for a slew key not sent, its list showing its last row (Page Up moves on to later rows).</summary>
    internal const string EndOfList = "End of list";

    /// <summary>Said for a slew key not sent, its list showing its first row (Page Down moves back toward the top).</summary>
    internal const string TopOfList = "Top of list";

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
        Name = "Status", ReadOnly = true, TabStop = true, Width = 420, AccessibleName = "A330 MCDU status",
    };
    private readonly ComboBox _side = new()
    {
        Name = "Side", DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, AccessibleName = "MCDU side",
    };
    private readonly ListBox _display = new() { Name = "Screen", IntegralHeight = false, Font = new Font("Consolas", 11f) };
    private readonly TextBox _scratchpad = new() { Name = "Scratchpad", AccessibleName = "Scratchpad entry" };

    private IntPtr _previousWindow;
    private bool _wanting, _disposed, _typing, _offGridLogged;
    private A330Gauge _wantedGauge;
    private IReadOnlyList<A330McduLine>? _shownPage;   // the page listed now; null while the status row is
    private A330Frame? _shownFrame;                    // the frame that page came from; null while the status row is
    private A330Frame? _slewSentOn;                    // a list's newest frame when a slew went out, until a frame differs from it
    private string _lastTitle = "";
    private int? _rowBeforeGap;                        // the pilot's row when a page gave way to the status row
    private A330McduLineKeyWatch? _lineWatch;
    private A330McduAirportWatch? _airportWatch;
    private bool _titleAfterSwitch;                    // a side switch: the new side's first title is said
    private string? _alreadySaid;                      // the typed line the window last said for an entry, until the next key
    private (string Message, long Until)? _clearing;   // CLR pressed on a message: said once its line no longer holds it
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

    /// <summary>True while the screen list has the focus (its own <see cref="Control.Focused"/>); a test sets it, never taking the focus.</summary>
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
        _display.AccessibleName = $"{_profile.Device} screen";
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
        _lineWatch = null;
        _airportWatch = null;
        _entryGeneration++;   // an entry in progress stops: hidden, closed or disposed
        if (!_wanting) return;
        _wanting = false;
        _displays.Release(_wantedGauge, this);
    }

    /// <summary>A new open: its first page is not a title change, and its scratchpad line is read silently first (a
    /// side switch then says both, <see cref="OnSideSelected"/>).</summary>
    private void StartReadBacksAfresh()
    {
        _scratch.Reset();
        _lastTitle = "";
        _titleAfterSwitch = false;
        _lineWatch = null;
        _airportWatch = null;
        _rowBeforeGap = null;
        _alreadySaid = null;
        _clearing = null;
        _slewSentOn = null;
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
        _titleAfterSwitch = true;
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
            _log.Warn($"{A330GaugeNames.Name(gauge)}: a complete frame with a text off the MCDU's grid; the status row is shown");
        }

        if (frame is not null && screen is { HiddenRowsLeftOut: true })
        {
            _status.Restart(now);
            var page = A330McduLines.From(screen);
            SetStatus(A330McduStatus.PageRow(gauge, frame.Texts.Any(t => t.Colour is not null)));
            _shownFrame = frame;
            // A frame that differs from the one a slew went out on: the next slew is judged on the page as it is.
            if (_slewSentOn is { } sentOn && !A330ReaderClient.SameTexts(sentOn.Texts, frame.Texts)) _slewSentOn = null;
            ShowPage(page);
            Speak(_availability.Next(null, pageShown: true));
            FeedScratchpad(page, now);
            FollowLineKey(page, now);
            FollowAirportKey(page, now);
            return;
        }

        string row;
        string? reason = null;
        if (frame is { Complete: true, HiddenRowsLeftOut: false })
        {
            // Read, but rows the MCDU does not show may be in it (spec M15): no page rows, and nothing said of them. A
            // list page's hidden rows can sit below the screen, which puts the frame off the grid; the reader is at work
            // all the same, so the frame restarts the "cannot read" count as a page does (as A330DisplayWindow's does).
            _status.Restart(now);
            row = A330McduStatus.ListRowsNotReadRow;
        }
        else (row, reason) = _status.Row(_displays.State, _displays.StatusText, _displays.Newest(gauge), gauge, now);
        SetStatus(row);
        ShowStatusRow(row);
        Speak(_availability.Next(reason, pageShown: false));
        if (_lineWatch?.Expired(now) == true) _lineWatch = null;
        if (_airportWatch?.Expired(now) == true) _airportWatch = null;
        if (_clearing is { } c && now > c.Until) _clearing = null;
    }

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

        string title = CduLineText.Title(page);   // as said: without the list's "Title: "
        int target;
        if (title.Length > 0 && title != _lastTitle)
        {
            // Moving the focused list to the title row makes the screen reader read it; announce only when that will
            // not happen (focus elsewhere, or already on row 0), never both. A temporary flight plan is said either way,
            // so a pilot knows INSERT is waiting: after the title, or alone when the screen reader reads the title.
            // The first page of an open is not a title change; the first of a side switch is.
            bool selectionSpeaks = ScreenHasFocus() && saved != 0 && _display.Items.Count > 0;
            bool temporary = title.Contains("TMPY", StringComparison.Ordinal);
            if (_lastTitle.Length > 0 || _titleAfterSwitch)
            {
                if (!selectionSpeaks) Speak(temporary ? title + ", temporary flight plan" : title);
                else if (temporary) Speak("Temporary flight plan");
            }
            _lastTitle = title;
            _titleAfterSwitch = false;
            target = 0;
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
        if (_clearing is { } clearing)
        {
            if (now > clearing.Until) _clearing = null;
            else if (row != clearing.Message)
            {
                // CLR took the message off the line: say which, once. An emptied line is then the read-back's silent
                // starting point (no "Scratchpad cleared" for it); the next queued message is read back as usual.
                _clearing = null;
                Speak("Message cleared: " + clearing.Message);
                if (row.Length == 0)
                {
                    _scratch.Reset();
                    _scratch.OnPoll(row, Utc(now));
                    return;
                }
            }
        }
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

    private void FollowLineKey(IReadOnlyList<A330McduLine> page, long now)
    {
        if (_lineWatch is null) return;
        var (over, say) = _lineWatch.OnPage(page, now);
        if (over) _lineWatch = null;
        Speak(say);
    }

    private void FollowAirportKey(IReadOnlyList<A330McduLine> page, long now)
    {
        if (_airportWatch is null) return;
        var (over, say) = _airportWatch.OnPage(page, now);
        if (over) _airportWatch = null;
        Speak(say);
    }

    /// <summary>A refresh's sentence, said only while the window is shown.</summary>
    private void Speak(string? text)
    {
        if (text is not null && _wanting && !IsDisposed) _announcer.Announce(text);
    }

    private static DateTime Utc(long ms) => new(Math.Max(0, ms) * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    /// <summary>Collapses the wide layout gaps between a line's left and right fields to a
    /// readable separator, keeping single word spaces.</summary>
    internal static string Tidy(string text)
    {
        var t = text.Trim();
        return System.Text.RegularExpressions.Regex.Replace(t, " {3,}", "   ");
    }

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

    private static int? LineOf(string key) =>
        key.Length == 5 && key.StartsWith("LSK", StringComparison.Ordinal) && key[3] is >= '1' and <= '6' && key[4] is 'L' or 'R'
            ? key[3] - '0'
            : null;

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
                A330Frame? list = null;
                if (slew && ReferenceEquals(profile, _profile))
                {
                    var (send, on) = await SlewAsync(profile, up: key == profile.SlewUp);
                    if (!send) return;
                    list = on;
                }
                await PaceAsync();
                if (IsDisposed || !Send(profile, key)) return;
                // One slew at a time: on a list, the next slew waits for a frame that differs from the newest one now.
                if (slew) _slewSentOn = list is not null && ReferenceEquals(profile, _profile) ? _displays.Latest(profile.Gauge) ?? list : null;
                if (LineOf(key) is int line && _shownPage is { } page && ReferenceEquals(profile, _profile))
                    _lineWatch = new A330McduLineKeyWatch(line, page, NowMs());
                if (key == profile.AirportKey && _shownPage is { } shown && ReferenceEquals(profile, _profile))
                    _airportWatch = new A330McduAirportWatch(shown, NowMs());
            }
            finally { _keyGate.Release(); }
        }
        catch (Exception ex)
        {
            _log.Error($"key {key} failed: {ex.Message}");
        }
    }

    /// <summary>Whether a slew key of the side shown goes, and the list frame it was judged on (null when the key goes as it
    /// always has: no page shown, or a page whose frame has no list the display clips). On such a list Page Up goes only
    /// while rows are hidden below, Page Down only while rows are hidden above; after a slew sent on a list the press waits
    /// up to <see cref="SlewShowsWithinMs"/> for a frame that differs from the newest one then. A key not sent says
    /// <see cref="EndOfList"/> or <see cref="TopOfList"/> for its direction, once ([CORE-7]: the screen reader cannot say
    /// it); a window hidden, closed, disposed or switched to the other side during the wait sends and says nothing.</summary>
    private async Task<(bool Send, A330Frame? List)> SlewAsync(CduFormProfile profile, bool up)
    {
        string notSent = up ? EndOfList : TopOfList;
        int generation = _entryGeneration;
        if (_slewSentOn is { } sentOn)
        {
            // No frame has shown the last slew yet: wait for one that differs. None in time: the list did not move.
            long since = NowMs();
            while (_displays.Latest(profile.Gauge) is { } latest && A330ReaderClient.SameTexts(latest.Texts, sentOn.Texts))
            {
                if (NowMs() - since >= SlewShowsWithinMs)
                {
                    Speak(notSent);
                    return (false, null);
                }
                await Delay(KeyPollMs);
                if (IsDisposed || _entryGeneration != generation) return (false, null);
            }
            _slewSentOn = null;
        }
        // The page judged is the one shown, and the newest frame's.
        if (Visible) RefreshNow();
        if (_shownFrame is not { List: true } frame) return (true, null);
        if (up ? frame.ListBelow : frame.ListAbove) return (true, frame);
        Speak(notSent);
        return (false, null);
    }

    /// <summary>CLR. On a message (the page's scratchpad line, not the MCDU's typed line, <see cref="A330McduMessages.Message"/>)
    /// the window says "Message cleared: ..." once a frame shows the line without it, within 1.5 s, or nothing. With no
    /// typed line from the reader, typed text cannot be told from a message, so it says nothing of the kind.</summary>
    private void Clear()
    {
        long now = NowMs();
        _scratch.SuppressUntil = Utc(now + 400);
        _clearing = _shownPage is { } page
            && _displays.McduScratchpad(_profile.FirstOfficer) is { } typed
            && A330McduMessages.Message(ScratchpadText(page[A330McduLines.ScratchpadRow]), typed) is { } message
                ? (message, now + ClearShowsWithinMs)
                : null;
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

    /// <summary>No typed line from the reader: every key at today's pace. With a page shown, its scratchpad line read
    /// back is the confirmation; with none, the window says what it typed.</summary>
    private async Task TypeUnconfirmedAsync(CduFormProfile profile, IReadOnlyList<CduTypedKey> keys, Entry entry)
    {
        foreach (var k in keys)
        {
            await PaceAsync();
            if (entry.Stopped || !Send(profile, k.Key)) return;
        }
        _scratch.SuppressUntil = Utc(NowMs() + TypedEchoMs);
        if (_shownPage is null) _announcer.Announce($"Typed {keys[^1].Expected}, not confirmed");
    }

    /// <summary>Each key once the one before has shown on the typed line; a key that does not show stops the entry.</summary>
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
                _announcer.Announce($"Missed {k.Key}. {PadWords(shown)}");
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

    private static string PadWords(string? pad) => pad switch
    {
        null => "Scratchpad not read",
        "" => "Scratchpad empty",
        _ => "Scratchpad: " + pad,
    };

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
