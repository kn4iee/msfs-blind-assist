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
            // Alt+E: the whole E/WD (engine block, warning display with its cursor and boxes, SD, permanent
            // data) in the shared E/WD window, refreshed from the background monitor's latest capture.
            case HotkeyAction.ReadDisplayUpperECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(
                    () => new Forms.FbwEwdWindow("A350 E/WD — Engine / Warning Display",
                        () => Task.FromResult(A350.Display.A350EwdText.Build(Ecam?.LatestCapture,
                            simConnect.GetCachedVariableValue(SdPageKey))), announcer),
                    w => { w.Show(); w.BringToFront(); w.Activate(); });
                return true;
            // Shift+M: the MFD window (A380 parity): the page's lines, Enter acts, a field takes the scratchpad,
            // Alt+letter page keys; clicks through the Coherent mouse route, typing on the KCCU.
            case HotkeyAction.ShowFenixMCDU:
            {
                hotkeyManager.ExitOutputHotkeyMode();
                var input = new A350.Display.A350MfdInput((name, value) => { simConnect.SetLVar(name, value); return true; });
                ShowTrackedWindow(
                    () => new Forms.A350.A350MfdWindow(() => Pfd?.Latest("MFD", 0),
                        async () => Displays == null ? null : (await Displays.DrawAsync(("MFD", 0)))?.FirstOrDefault(), input, announcer),
                    w => { w.Show(); w.BringToFront(); w.Activate(); });
                return true;
            }
            // Ctrl+Shift+R: the RMP window (A380 parity): the chosen RMP's screen, a frequency typed on its keypad, Enter
            // loads it as standby, Alt+1/2/3 swaps it in; Alt+T a squawk.
            case HotkeyAction.ShowRMP:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(
                    () => new Forms.A350.A350RmpWindow(rmp => Pfd?.Latest(A350.A350Rmp.Gauge(rmp).Gauge, A350.A350Rmp.Gauge(rmp).Index),
                        async rmp => Displays == null ? null : (await Displays.DrawAsync(A350.A350Rmp.Gauge(rmp)))?.FirstOrDefault(),
                        rpn => simConnect.ExecuteCalculatorCodeUnique(rpn), announcer),
                    w => { w.Show(); w.BringToFront(); w.Activate(); });
                return true;
            // Ctrl+E: the E/WD call-outs on or off, the same row Ctrl+M shows (the A380's toggle).
            case HotkeyAction.ToggleECAMMonitoring:
            {
                bool enabled = A350.Display.A350EcamMonitor.Toggle(Settings.SettingsManager.Current.A350DisabledMonitorVariables);
                Settings.SettingsManager.Save();
                announcer.AnnounceImmediate(A350.Display.A350EcamMonitor.ToggleSpoken(enabled));
                return true;
            }
            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }
}
