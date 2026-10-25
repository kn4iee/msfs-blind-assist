using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The BlueBird 757's two CDUs as text (Shift+M). A clone of the HS787 FMC window (same list
/// layout, same keys), fed by the shared <see cref="CoherentBbs757TabletClient"/>, which reads the
/// screens off the aircraft's Comm Bus and presses keys as held L:vars. Ctrl+Tab switches between
/// the captain's and the first officer's CDU; the two are independent, as on the aircraft.
///
/// The window never disposes the client: MainForm owns it (the EFB window shares it). The window
/// only subscribes, and unsubscribes in Dispose.
///
/// Speech (CORE-7): a page title is spoken when it changes, and the scratchpad is read back by
/// <see cref="CduScratchpadAnnouncer"/> once every queued key has had its cycle, which is the
/// state the screen reader cannot know. A key the window cannot deliver is refused aloud. Ctrl+Tab
/// says only which CDU is shown. While the window is hidden only one thing speaks: keys the pilot
/// sent that were dropped because the link went ("CDU connection lost, keys not sent").
/// </summary>
public partial class BBS757CduForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public const string WaitingText = "Waiting for the 757's CDU. Is the aircraft powered?";
    public const string WaitingFirstOfficerText =
        "Waiting for the first officer's CDU. It shows once its screen changes: press a page key, such as Alt+R for RTE.";
    public const string NotReachableText = "The 757's CDU is not reachable.";
    public const string KeysDroppedText = "CDU connection lost, keys not sent";

    private const int PollTickMs = 350;
    // After the last queued key, how long the scratchpad waits before it is read back.
    private const int ScratchpadSettleMs = 300;

    private readonly CoherentBbs757TabletClient _client;
    private readonly ScreenReaderAnnouncer _announcer;

    // The latest screen per CDU (index 1 captain, 2 first officer); null until one arrives.
    private readonly Bbs757CduScreen?[] _screens = new Bbs757CduScreen?[3];
    private int _cdu = 1;
    private string _previousTitle = "";
    private readonly CduScratchpadAnnouncer _scratchpad = new();

    // True while the key now being handled is an auto-repeat (held down): one physical press is
    // one CDU key, so a held Backspace never queues a run of CLRs.
    private bool _keyRepeat;

    private IntPtr _previousWindow = IntPtr.Zero;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool _disposed;

    public BBS757CduForm(CoherentBbs757TabletClient client, ScreenReaderAnnouncer announcer)
    {
        _client = client;
        _announcer = announcer;

        InitializeComponent();
        SetupEventHandlers();

        // Status and scratchpad read-back run on every tick while the window is shown; screens
        // themselves are pushed by the client when they change.
        _pollTimer = new System.Windows.Forms.Timer { Interval = PollTickMs };
        _pollTimer.Tick += (_, _) => OnPollTick();

        _client.ScreenUpdated += Client_ScreenUpdated;
        _client.ScreenLost += Client_ScreenLost;
        _client.KeysDropped += Client_KeysDropped;
    }

    // ------------------------------------------------------------------
    // Event wiring
    // ------------------------------------------------------------------

    private void SetupEventHandlers()
    {
        Load += (s, e) => cduDisplay.Focus();

        FormClosing += (s, e) =>
        {
            // Let real app/OS shutdown through (an unconditional cancel stalls Application.Exit);
            // everything else hides, as the HS787 window does.
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }

            e.Cancel = true;
            HideAndRestore();
        };

        this.KeyDown += Form_KeyDown;
        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;

        btnInitRef.Click  += (s, e) => SendKey(Bbs757CduKeys.Init);
        btnRte.Click      += (s, e) => SendKey(Bbs757CduKeys.Rte);
        btnDepArr.Click   += (s, e) => SendKey(Bbs757CduKeys.DepArr);
        btnAtc.Click      += (s, e) => SendKey(Bbs757CduKeys.Atc);
        btnVnav.Click     += (s, e) => SendKey(Bbs757CduKeys.Vnav);
        btnFix.Click      += (s, e) => SendKey(Bbs757CduKeys.Fix);
        btnLegs.Click     += (s, e) => SendKey(Bbs757CduKeys.Legs);
        btnHold.Click     += (s, e) => SendKey(Bbs757CduKeys.Hold);
        btnFmcComm.Click  += (s, e) => SendKey(Bbs757CduKeys.FmcComm);
        btnProg.Click     += (s, e) => SendKey(Bbs757CduKeys.Prog);
        btnMenu.Click     += (s, e) => SendKey(Bbs757CduKeys.Menu);
        btnNavRad.Click   += (s, e) => SendKey(Bbs757CduKeys.NavRad);
        btnPrevPage.Click += (s, e) => SendKey(Bbs757CduKeys.Prev);
        btnNextPage.Click += (s, e) => SendKey(Bbs757CduKeys.Next);
        btnExec.Click     += (s, e) => SendKey(Bbs757CduKeys.Exec);
        btnClr.Click      += (s, e) => SendKey(Bbs757CduKeys.Clr);
        btnDel.Click      += (s, e) => SendKey(Bbs757CduKeys.Del);
        btnSwitchCdu.Click += (s, e) => SwitchCdu();
    }

    // ------------------------------------------------------------------
    // Client pushes (posted to the UI context; marshalled again so a push that lands while the
    // handle is being destroyed is dropped instead of thrown)
    // ------------------------------------------------------------------

    private void Client_ScreenUpdated(int cdu, Bbs757CduScreen screen) => SafeBeginInvoke(() =>
    {
        if (_disposed) return;
        _screens[cdu] = screen;
        if (cdu == _cdu) Render();
    });

    private void Client_ScreenLost(int cdu) => SafeBeginInvoke(() =>
    {
        if (_disposed) return;
        _screens[cdu] = null;
        if (cdu != _cdu) return;
        _scratchpad.Reset();   // the next screen seeds it silently
        Render();
    });

    // Keys the pilot sent were thrown away when the link went: a background change he did not
    // see coming, so it is said (once per outage, the client's guarantee), window shown or not.
    private void Client_KeysDropped(int count) => SafeBeginInvoke(() =>
    {
        if (_disposed) return;
        _announcer.Announce(KeysDroppedText);
    });

    // ObjectDisposedException derives from InvalidOperationException, so one catch covers both;
    // the IsHandleCreated check alone races a handle destroy ([A380C-6]).
    private void SafeBeginInvoke(Action action)
    {
        try { if (IsHandleCreated) BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------------
    // Display
    // ------------------------------------------------------------------

    private void OnPollTick()
    {
        UpdateStatus();
        var now = DateTime.UtcNow;
        // While keys are still queued or in their cycle, the scratchpad is mid-entry: hold its
        // read-back, so typing is read back once, settled, ScratchpadSettleMs after the last key.
        if (_client.PendingKeyCount > 0)
        {
            var until = now.AddMilliseconds(ScratchpadSettleMs);
            if (until > _scratchpad.SuppressUntil) _scratchpad.SuppressUntil = until;
        }
        var screen = _screens[_cdu];
        if (screen != null && _scratchpad.OnPoll(screen.Scratchpad, now) is { } say)
            _announcer.Announce(say);
    }

    private void UpdateStatus()
    {
        string desired;
        if (!_client.IsReadable)
            desired = WaitingText;
        else if (_screens[_cdu] == null)
            desired = _cdu == 1 ? WaitingText : WaitingFirstOfficerText;
        else
            desired = CduName(_cdu);

        if (statusBox.Text != desired)
            statusBox.Text = desired;
    }

    private static string CduName(int cdu) => cdu == 1 ? "Captain CDU" : "First officer CDU";

    private string WaitingTextFor(int cdu) => cdu == 1 ? WaitingText : WaitingFirstOfficerText;

    /// <summary>
    /// Shows the selected CDU's screen. A changed page title is spoken (while the window is shown)
    /// and moves the list to its first line, as in the HS787 window; otherwise the reading position
    /// is kept, since CDU lines are positional. <paramref name="announceTitle"/> false shows the
    /// screen as it is and leaves the title to the next push (the CDU switch).
    /// </summary>
    private void Render(bool announceTitle = true)
    {
        UpdateStatus();

        var screen = _screens[_cdu];
        if (screen == null)
        {
            Forms.DisplayList.UpdateInPlace(cduDisplay, new[] { WaitingTextFor(_cdu) });
            if (cduDisplay.Items.Count > 0 && cduDisplay.SelectedIndex != 0) cduDisplay.SelectedIndex = 0;
            _previousTitle = "";   // the first page that arrives is announced
            return;
        }

        int savedIndex = cduDisplay.SelectedIndex;
        Forms.DisplayList.UpdateInPlace(cduDisplay, Bbs757CduFrame.ToDisplayLines(screen));

        string title = screen.Title;
        if (!announceTitle)
        {
            if (savedIndex >= 0 && savedIndex < cduDisplay.Items.Count && cduDisplay.SelectedIndex != savedIndex)
                cduDisplay.SelectedIndex = savedIndex;
        }
        else if (title.Length > 0 && title != _previousTitle)
        {
            if (Visible) _announcer.Announce(title);
            _previousTitle = title;
            if (cduDisplay.Items.Count > 0)
                cduDisplay.SelectedIndex = 0;
        }
        else if (savedIndex >= 0 && savedIndex < cduDisplay.Items.Count && cduDisplay.SelectedIndex != savedIndex)
        {
            cduDisplay.SelectedIndex = savedIndex;
        }
    }

    /// <summary>Re-seeds the scratchpad read-back silently on what the selected CDU shows now.</summary>
    private void ReseedScratchpad()
    {
        _scratchpad.Reset();
        var screen = _screens[_cdu];
        if (screen != null) _scratchpad.OnPoll(screen.Scratchpad, DateTime.UtcNow);
    }

    // ------------------------------------------------------------------
    // CDU switch
    // ------------------------------------------------------------------

    /// <summary>
    /// Ctrl+Tab: says only which CDU is now shown. Its page title is spoken by the fresh push the
    /// client sends for it (setting ActiveCdu asks for one), never from the copy held here, which
    /// can be older; until then the held copy is shown as it is. A CDU with no screen yet says so
    /// in its list row and the status box.
    /// </summary>
    private void SwitchCdu()
    {
        _cdu = _cdu == 1 ? 2 : 1;
        _client.ActiveCdu = _cdu;
        _announcer.AnnounceImmediate(CduName(_cdu));
        _previousTitle = "";   // so the fresh push speaks its title
        ReseedScratchpad();
        Render(announceTitle: false);
    }

    // ------------------------------------------------------------------
    // Keys to the CDU
    // ------------------------------------------------------------------

    /// <summary>Queues one key for the selected CDU, or says why it cannot.</summary>
    private void SendKey(string key)
    {
        if (!_client.IsReadable)
        {
            _announcer.AnnounceImmediate(NotReachableText);
            return;
        }
        _client.PressKey(_cdu, key);
    }

    /// <summary>Types the scratchpad box's text into the selected CDU (Bbs757CduKeys.ForChar; characters with no key are skipped).</summary>
    private void SendText(string text)
    {
        var keys = text.SelectMany(Bbs757CduKeys.ForChar).ToList();
        if (keys.Count == 0) return;
        if (!_client.IsReadable)
        {
            _announcer.AnnounceImmediate(NotReachableText);
            return;
        }
        foreach (var key in keys) _client.PressKey(_cdu, key);
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    // The repeat bit of the key message that is about to reach Form_KeyDown (KeyPreview raises
    // the form's KeyDown from here).
    protected override bool ProcessKeyPreview(ref Message m)
    {
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
            _keyRepeat = ((long)m.LParam & 0x40000000) != 0;
        return base.ProcessKeyPreview(ref m);
    }

    // Ctrl+Tab is a dialog key: it never reaches KeyDown, so it is caught here.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0)
        {
            bool repeat = ((long)msg.LParam & 0x40000000) != 0;
            if (!repeat) SwitchCdu();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        string? key = CduKeyFor(e);
        if (key != null)
        {
            if (!_keyRepeat) SendKey(key);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+S: focus scratchpad
        if (e.Alt && !e.Shift && !e.Control && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+Home: focus display
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            cduDisplay.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape && !e.Alt && !e.Control && !e.Shift)
        {
            HideAndRestore();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// The CDU key a window shortcut presses, or null. The HS787 window's layout:
    /// Ctrl+1-6 / Alt+1-6 the left and right line select keys (F1-F6 / F7-F12 in the alternate
    /// layout, read on every press), PageUp / PageDown (or Alt+Up / Alt+Down) PREV / NEXT PAGE,
    /// Ctrl+Enter or Alt+E EXEC, Alt+letters the page keys, and with the display focused
    /// Backspace CLR and Delete DEL.
    /// </summary>
    private string? CduKeyFor(KeyEventArgs e)
    {
        if (SettingsManager.Current.MCDUUseAlternateLSKKeys)
        {
            if (!e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
                return Bbs757CduKeys.Lsk(right: false, e.KeyCode - Keys.F1 + 1);
            if (!e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12)
                return Bbs757CduKeys.Lsk(right: true, e.KeyCode - Keys.F7 + 1);
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
                return Bbs757CduKeys.Lsk(right: false, e.KeyCode - Keys.D1 + 1);
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
                return Bbs757CduKeys.Lsk(right: true, e.KeyCode - Keys.D1 + 1);
        }

        if ((e.KeyCode == Keys.PageUp && !e.Control && !e.Alt) || (e.Alt && e.KeyCode == Keys.Up))
            return Bbs757CduKeys.Prev;
        if ((e.KeyCode == Keys.PageDown && !e.Control && !e.Alt) || (e.Alt && e.KeyCode == Keys.Down))
            return Bbs757CduKeys.Next;
        if (e.Control && !e.Alt && e.KeyCode == Keys.Return)
            return Bbs757CduKeys.Exec;

        if (ActiveControl == cduDisplay && !e.Control && !e.Alt && !e.Shift)
        {
            if (e.KeyCode == Keys.Back) return Bbs757CduKeys.Clr;
            if (e.KeyCode == Keys.Delete) return Bbs757CduKeys.Del;
        }

        if (e.Alt && !e.Control && !e.Shift)
            return Bbs757CduKeys.ForAltLetter(e.KeyCode);
        return null;
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return && !e.Control && !e.Alt)
        {
            string text = scratchpadInput.Text;
            scratchpadInput.Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;
            SendText(text);
        }
        else if (scratchpadInput.Text.Length == 0 && !e.Control && !e.Alt && !e.Shift
                 && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete))
        {
            // An empty box: Backspace is CLR and Delete is DEL, as on the display.
            if (!_keyRepeat) SendKey(e.KeyCode == Keys.Back ? Bbs757CduKeys.Clr : Bbs757CduKeys.Del);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        _client.ActiveCdu = _cdu;   // asks for a fresh copy of its screen
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        cduDisplay.Focus();
        ReseedScratchpad();
        Render();
    }

    private void HideAndRestore()
    {
        Hide();
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    // The client scrapes at its visible rate only while the window is shown; the read-back
    // timer runs only then too (nothing speaks from a hidden window).
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_disposed) return;
        _client.SetActive(Visible);
        if (Visible) _pollTimer.Start();
        else _pollTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _client.ScreenUpdated -= Client_ScreenUpdated;
            _client.ScreenLost -= Client_ScreenLost;
            _client.KeysDropped -= Client_KeysDropped;
            _client.SetActive(false);
            // The client is MainForm's, shared with the EFB window: never disposed here.
        }
        base.Dispose(disposing);
    }
}
