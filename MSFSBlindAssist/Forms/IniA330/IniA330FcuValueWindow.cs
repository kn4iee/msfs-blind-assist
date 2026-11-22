using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;

namespace MSFSBlindAssist.Forms.IniA330;

/// <summary>
/// The A330's FCU value window (Ctrl+H, Ctrl+S, Ctrl+A, Ctrl+V), laid out and behaving like the
/// Fenix A320's: a value box, the knob buttons, Close. Enter sets the value and keeps the window open
/// with the text selected; Escape or Close closes it and returns focus to the window that had it.
/// A button press says nothing here: the aircraft's own call-outs speak the result.
/// </summary>
public class IniA330FcuValueWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly A330FcuWindowSpec _spec;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TextBox _valueBox;
    private IntPtr _previousWindow;

    public IniA330FcuValueWindow(A330FcuWindowSpec spec, ScreenReaderAnnouncer announcer)
    {
        _spec = spec;
        _announcer = announcer;
        Text = spec.Title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;

        var label = new Label
        {
            Text = spec.FieldLabel, Location = new Point(20, 25), Size = new Size(190, 20),
            AccessibleName = spec.ValueName.Replace(" value", " Label"),
        };
        _valueBox = new TextBox
        {
            Location = new Point(220, 22), Size = new Size(100, 25),
            AccessibleName = spec.ValueName, AccessibleDescription = spec.ValueDescription, TabIndex = 0,
        };
        _valueBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Commit();
        };
        Controls.Add(label);
        Controls.Add(_valueBox);

        // two buttons to a row, a lone last one across the row, as on the Fenix's windows
        int y = 65, tab = 2;
        for (int i = 0; i < spec.Buttons.Count; i += 2)
        {
            bool pair = i + 1 < spec.Buttons.Count;
            for (int k = 0; k < (pair ? 2 : 1); k++)
            {
                var def = spec.Buttons[i + k];
                var b = new Button
                {
                    Text = def.Label, AccessibleName = def.Label, AccessibleDescription = def.Description, TabIndex = tab++,
                    Location = new Point(k == 0 ? 20 : 200, y), Size = pair ? new Size(160, 35) : new Size(340, 35),
                };
                b.Click += (_, _) => def.Press();
                Controls.Add(b);
            }
            y += 50;
        }
        var close = new Button
        {
            Text = "Close", Location = new Point(130, y + 45), Size = new Size(140, 35),
            DialogResult = DialogResult.OK, AccessibleName = "Close", AccessibleDescription = "Close window", TabIndex = tab,
        };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        CancelButton = close;
        ClientSize = new Size(380, y + 100);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
        };
        FormClosing += (_, _) =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private void Commit()
    {
        var c = _spec.Commit(_valueBox.Text);
        if (c.Error != null)
        {
            _announcer.AnnounceImmediate(c.Error);
            _valueBox.Focus();
            if (_valueBox.Text.Trim().Length > 0) _valueBox.SelectAll();
            return;
        }
        c.Set?.Invoke();
        _valueBox.SelectAll();
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _valueBox.Focus();
    }
}

// One type per window, so the definition's tracked-window cache holds one of each.
public sealed class IniA330FcuHeadingWindow(A330FcuWindowSpec spec, ScreenReaderAnnouncer announcer) : IniA330FcuValueWindow(spec, announcer);
public sealed class IniA330FcuSpeedWindow(A330FcuWindowSpec spec, ScreenReaderAnnouncer announcer) : IniA330FcuValueWindow(spec, announcer);
public sealed class IniA330FcuAltitudeWindow(A330FcuWindowSpec spec, ScreenReaderAnnouncer announcer) : IniA330FcuValueWindow(spec, announcer);
public sealed class IniA330FcuVsWindow(A330FcuWindowSpec spec, ScreenReaderAnnouncer announcer) : IniA330FcuValueWindow(spec, announcer);
