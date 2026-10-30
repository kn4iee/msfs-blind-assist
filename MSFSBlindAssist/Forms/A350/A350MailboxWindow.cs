using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The ATC mailbox as lines (Input Ctrl+Shift+D, the DCDU's key; also in the A350's window list): the message it shows, line by line,
/// then its buttons (<see cref="A350Mailbox"/>). Enter presses a button with the KCCU cursor brought to the mailbox. F5 reads now,
/// Escape closes. A SEND here sends to ATC, as it does for a sighted pilot.
/// </summary>
public sealed class A350MailboxWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Func<A350DrawCapture?> _latest;
    private readonly Func<Task<A350DrawCapture?>> _drawNow;
    private readonly Func<bool> _isDark;
    private readonly A350MailboxInput _input;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly DisplayListBox _list;
    private readonly IntPtr _previousWindow;
    private readonly A350ShownCapture _shown = new();
    private readonly HashSet<string> _failures = new();
    private IReadOnlyList<MfdLine> _lines = Array.Empty<MfdLine>();
    private System.Windows.Forms.Timer? _timer;
    private bool _busy, _refreshAgain;

    public A350MailboxWindow(Func<A350DrawCapture?> latest, Func<Task<A350DrawCapture?>> drawNow, Func<bool> isDark,
        A350MailboxInput input, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _latest = latest;
        _drawNow = drawNow;
        _isDark = isDark;
        _input = input;
        _announcer = announcer;

        Text = "A350 ATC mailbox";
        Size = new Size(700, 480);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), AccessibleName = "Mailbox",
            AccessibleDescription = "Enter presses the button on the line. F5 reads now, Escape closes.",
        };
        _list.SetText("Loading the mailbox...");
        Controls.Add(_list);

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

    /// <summary>The mailbox's lines and what the list shows: a dark screen shows dark and with no capture there is nothing to press.</summary>
    internal static (IReadOnlyList<MfdLine> Lines, IReadOnlyList<string> Shown) PageLines(A350DrawCapture? cap, bool dark)
    {
        if (cap == null)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.NotAvailableYet });
        if (dark)
            return (Array.Empty<MfdLine>(), new[] { Aircraft.IniBuildsA350Definition.DarkScreen });
        var lines = A350Mailbox.Read(cap);
        return (lines, lines.Count == 0 ? new[] { "The mailbox is empty." } : lines.Select(l => l.Spoken).ToList());
    }

    private async Task RefreshAsync(bool now)
    {
        if (IsDisposed) return;
        if (_busy)
        {
            _refreshAgain = true;
            return;
        }
        _busy = true;
        try
        {
            var cap = now ? await _drawNow() : null;
            if (cap is { Ok: true }) _shown.Fresh(_latest());
            else
            {
                cap = _latest();
                if (_shown.Superseded(cap)) return;
            }
            if (IsDisposed) return;
            (_lines, var shown) = PageLines(cap, _isDark());
            _list.SetLines(shown);
        }
        catch (Exception ex)
        {
            // Best-effort: the last good read stays; each distinct failure once a window, so a report can name it.
            if (_failures.Add(ex.Message)) Utils.Logging.Log.Warn("A350Display", "Mailbox window refresh: " + ex);
        }
        finally
        {
            _busy = false;
            if (_refreshAgain && !IsDisposed)
            {
                _refreshAgain = false;
                _ = RefreshAsync(false);
            }
        }
    }

    private async Task ActivateAsync()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _lines.Count) return;
        var line = _lines[i];
        if (line.ClickX < 0)
        {
            _announcer.AnnounceImmediate("Nothing to press on this line");
            return;
        }
        switch (await _input.ActAsync(line.ClickX, line.ClickY))
        {
            case MfdAction.Busy:
                return;
            case MfdAction.Unreachable:
                _announcer.AnnounceImmediate("The mailbox cannot be reached: is the Coherent debugger running?");
                return;
        }
        await Task.Delay(600);
        await RefreshAsync(true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5: _ = RefreshAsync(true); return true;
            case Keys.Escape: Close(); return true;
            case Keys.Enter when ActiveControl == _list: _ = ActivateAsync(); return true;
        }
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
