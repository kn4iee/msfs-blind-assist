using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>The A350's own hotkeys; every other action falls through to the base class (the shared ones).</summary>
public partial class IniBuildsA350Definition
{
    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        Attach(simConnect);
        switch (action)
        {
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowA350MonitorManagerDialog();
                return true;
            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }
}
