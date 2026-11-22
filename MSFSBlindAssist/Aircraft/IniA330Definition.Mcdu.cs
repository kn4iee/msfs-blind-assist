// Plan 03 owns this file: the MCDU.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>The MCDU window this definition opened (also a tracked window), closed on a switch away.</summary>
    private Forms.IniA330.IniA330CduForm? _mcduWindow;

    partial void HandleMcduHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ShowFenixMCDU:
                hotkeys.ExitInputHotkeyMode();
                // The captain's side first; the window switches sides itself (Ctrl+Shift+L / Ctrl+Shift+R).
                ShowTrackedWindow(
                    () => _mcduWindow = new Forms.IniA330.IniA330CduForm(Displays, sim, announcer, Forms.IniA330.CduFormProfile.A330(firstOfficer: false)),
                    f => f.ShowForm());
                handled = true; return;
        }
    }

    /// <summary>The aircraft was switched away from: the MCDU window is disposed, which lets its display go (the
    /// tracked windows' own teardown then skips it).</summary>
    partial void CloseMcduWindows()
    {
        var window = _mcduWindow;
        _mcduWindow = null;
        if (window is null || window.IsDisposed) return;
        window.Dispose();
    }
}
