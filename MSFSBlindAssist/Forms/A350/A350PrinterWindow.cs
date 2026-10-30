using System.Runtime.InteropServices;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The printer's paper, in a text box to read line by line (<see cref="A350Printout"/>). While the window is open the paper is read
/// every few seconds, so a new printout shows at once; the background says it (<see cref="A350PrinterWatch"/>). F5 reads it now, Escape
/// closes.
/// </summary>
public sealed class A350PrinterWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int ReadEveryMs = 3000;

    private readonly Func<Task<string?>> _read;
    private readonly TextBox _paper;
    private readonly System.Windows.Forms.Timer _timer;
    private IntPtr _previousWindow;
    private string? _shown;
    private bool _reading;

    public A350PrinterWindow(Func<Task<string?>> read)
    {
        _read = read;

        Text = "A350 printer";
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _paper = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            AccessibleName = "Printout",
            Font = new Font(FontFamily.GenericMonospace, 10),
        };
        Controls.Add(_paper);
        ActiveControl = _paper;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = e.SuppressKeyPress = true;
                _ = RefreshAsync();
            }
        };

        _timer = new System.Windows.Forms.Timer { Interval = ReadEveryMs };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        Shown += (_, _) => { _ = RefreshAsync(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Stop();
    }

    /// <summary>Shows the window with focus on the paper; on an open one, brings it forward.</summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        _paper.Focus();
    }

    private async Task RefreshAsync()
    {
        if (_reading) return;
        _reading = true;
        try
        {
            string? raw = await _read();
            if (IsDisposed || raw == null) return;
            string text = A350Printout.Text(raw);
            if (text == _shown) return;
            _shown = text;
            _paper.Text = text;
            _paper.SelectionStart = 0;
            _paper.SelectionLength = 0;
        }
        catch (Exception ex)
        {
            Log.Debug("A350", $"Printer window: {ex.Message}");
        }
        finally { _reading = false; }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
