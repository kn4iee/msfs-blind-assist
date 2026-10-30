using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>One row of the A350's window list.</summary>
public sealed record A350WindowEntry(string Name, string Description, Action Open);

/// <summary>
/// The A350's window list (Input Ctrl+Shift+F, the Fenix's key): the windows with no hotkey of their own. Enter or a double-click
/// closes the list and opens the row's window; Escape closes it and hands the foreground back. As the Fenix's list, it says nothing
/// but a failure to open: the screen reader reads the title, the row and the window that opens next.
/// </summary>
public sealed class A350WindowsForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IReadOnlyList<A350WindowEntry> _entries;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly ListBox _list;
    private IntPtr _previousWindow;

    public A350WindowsForm(IReadOnlyList<A350WindowEntry> entries, ScreenReaderAnnouncer announcer)
    {
        _entries = entries;
        _announcer = announcer;

        Text = "A350 windows";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 320);

        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Windows" };
        foreach (var entry in entries) _list.Items.Add(ItemText(entry));
        _list.SelectedIndex = 0;
        // Claim Enter as an input key so KeyDown reaches us, whatever the form does with dialog keys.
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            OpenSelected();
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (_list.IndexFromPoint(e.Location) >= 0) OpenSelected();
        };
        Controls.Add(_list);
        ActiveControl = _list;
    }

    /// <summary>The shown and spoken text of one row.</summary>
    internal static string ItemText(A350WindowEntry entry) => $"{entry.Name}. {entry.Description}";

    /// <summary>Shows the list with focus on its first row; on an open list, brings it forward.</summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _list.Focus();
    }

    // The list closes first, so the window that opens is never behind it.
    private void OpenSelected()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _entries.Count) return;
        var entry = _entries[index];
        Close();
        try
        {
            entry.Open();
        }
        catch (Exception ex)
        {
            Log.Error("A350", $"Could not open the A350 window '{entry.Name}'.", ex);
            _announcer.AnnounceImmediate($"Could not open {entry.Name}.");
        }
    }

    // The foreground goes back BEFORE the close, while this process still holds it. Enter does not do this, as the window that
    // opens next takes the foreground itself.
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
}
