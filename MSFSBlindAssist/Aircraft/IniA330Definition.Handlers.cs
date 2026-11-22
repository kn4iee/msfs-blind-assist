using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A momentary key's press: its own _Push and _Release actions. The release waits so the
    /// aircraft sees the press on a frame of its own.</summary>
    internal const int DefaultPushMs = 150;

    private static bool CanCalc(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what)
    {
        if (sim.CalcWriteCanLand) return true;
        announcer.AnnounceImmediate($"{what} unavailable: the MobiFlight module is not responding.");
        return false;
    }

    private void PushEvent(SimConnectManager sim, string inputEvent, int holdMs = DefaultPushMs)
    {
        Write(sim, $"(>B:{inputEvent}_Push)");
        _ = ReleaseLater();
        // awaited on the caller's context (the UI thread), so the release is sent from there too
        async Task ReleaseLater() { await Task.Delay(holdMs); Write(sim, $"(>B:{inputEvent}_Release)"); }
    }

    /// <summary>A press done by value (set 1, then back to 0): the FCU events that answer a value
    /// and not their _Push action (measured: V/S pull, ALT, the autobrake levels).</summary>
    private static void PressByValue(SimConnectManager sim, string inputEvent)
    {
        if (!sim.TrySetInputEvent(inputEvent, 1)) return;
        _ = ReleaseLater();
        // the input-event hashes and the SimConnect handle belong to the UI thread: release from there
        async Task ReleaseLater() { await Task.Delay(DefaultPushMs); sim.TrySetInputEvent(inputEvent, 0); }
    }

    private static bool SetInputEvent(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what, string inputEvent, double value)
    {
        if (sim.TrySetInputEvent(inputEvent, value)) return true;
        announcer.AnnounceImmediate($"{what} unavailable: this aircraft does not have that control.");
        return false;
    }

    // =====================================================================
    // Panel writes
    // =====================================================================

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        bool handled = false;
        // hooks run in this order and stop at the first that handles the key
        HandleFcuSet(varKey, value, varDef, simConnect, announcer, ref handled);   // plan 05
        if (handled) return true;
        HandleEcamSet(varKey, value, varDef, simConnect, announcer, ref handled);   // plan 08
        if (handled) return true;
        HandleFlightDisplaySet(varKey, value, varDef, simConnect, announcer, ref handled);   // plan 09
        if (handled) return true;
        HandlePanelSet(varKey, value, varDef, simConnect, announcer, ref handled);   // plan 04: the map's catch-all
        return handled;
    }

    // =====================================================================
    // Announcements
    // =====================================================================

    public override void ResetAnnouncementBaselines()
    {
        ResetPanelBaselines(false);   // plan 04
        ResetFlightDisplayBaselines(false);   // plan 09
        ResetReadoutBaselines(false);   // plan 10
        ResetFcuBaselines(false);   // plan 05
        ResetEcamBaselines(false);   // plan 08
        ResetMcduBaselines(false);   // plan 03
        ResetReader(false);   // plan 02
    }

    public override void OnSimContextReset()
    {
        ResetPanelBaselines(true);   // plan 04
        ResetFlightDisplayBaselines(true);   // plan 09
        ResetReadoutBaselines(true);   // plan 10
        ResetFcuBaselines(true);   // plan 05
        ResetEcamBaselines(true);   // plan 08
        ResetMcduBaselines(true);   // plan 03
        ResetReader(true);   // plan 02
    }

    /// <summary>The aircraft was switched away from: each owner closes what it opened (the hooks run in
    /// this order, then the base class).</summary>
    public override void OnSwitchedAway()
    {
        CloseMcduWindows();   // plan 03
        CloseEfb();   // plan 07
        CloseEcamWindows();   // plan 08
        CloseFlightDisplayWindows();   // plan 09
        CloseFcuWindows();   // plan 05
        StopReader();   // plan 02
        base.OnSwitchedAway();
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // hooks run in this order and stop at the first that handles the variable; SIM_ON_GROUND below
        // still falls through to the silent keys and the base class
        bool handled = false;
        ProcessReadoutUpdate(varName, value, announcer, ref handled);   // plan 10
        if (handled) return true;
        ProcessPanelUpdate(varName, value, announcer, ref handled);   // plan 04
        if (handled) return true;
        ProcessFlightDisplayUpdate(varName, value, announcer, ref handled);   // plan 09
        if (handled) return true;
        ProcessFcuUpdate(varName, value, announcer, ref handled);   // plan 05
        if (handled) return true;
        ProcessEcamUpdate(varName, value, announcer, ref handled);   // plan 08
        if (handled) return true;
        ProcessMcduUpdate(varName, value, announcer, ref handled);   // plan 03
        if (handled) return true;
        if (_silentKeys.Contains(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    private SimConnectManager? _sim;

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        // hooks run in this order; the first to set text wins
        string? text = null;
        FormatFlightDisplay(varKey, value, ref text);   // plan 09
        if (text == null) FormatFcuDisplay(varKey, value, ref text);   // plan 05
        if (text == null) FormatPanelDisplay(varKey, value, ref text);   // plan 04
        if (text == null) FormatReadoutDisplay(varKey, value, ref text);   // plan 10
        if (text == null) FormatEcamDisplay(varKey, value, ref text);   // plan 08
        if (text != null) { displayText = text; return true; }
        displayText = "";
        return false;
    }

    public override void OnDisplayPanelShown(string panelKey, SimConnectManager simConnect)
    {
        _sim = simConnect;
        base.OnDisplayPanelShown(panelKey, simConnect);
    }

    // =====================================================================
    // Hotkeys
    // =====================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        // hooks run in this order and stop at the first that handles the action
        bool handled = false;
        HandleReadoutHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 10
        if (handled) return true;
        HandleFcuHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 05
        if (handled) return true;
        HandleFlightDisplayHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 09
        if (handled) return true;
        HandleEcamHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 08
        if (handled) return true;
        HandleMcduHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 03
        if (handled) return true;
        HandleEfbHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 07
        if (handled) return true;
        switch (action)
        {
            // ---- windows ----
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA330.A330MonitorManagerForm(GetVariables(), ShortName,
                    DisabledMonitorList), m => m.ShowForm());
                return true;
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    private static bool On(SimConnectManager sim, string key) => (sim.GetCachedVariableValue(key) ?? 0) > 0.5;
}
