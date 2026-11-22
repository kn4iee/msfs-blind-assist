// Plan 03 owns this file: the MCDU.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private static Forms.IniA330.CduFormProfile CduProfile() => Forms.IniA330.CduFormProfile.A330(A330CduGlyphLibrary.A330);

    partial void HandleMcduHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ShowFenixMCDU:
                hotkeys.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA330.IniA330CduForm(sim, announcer, CduProfile()), f => f.ShowForm());
                handled = true; return;
        }
    }
}
