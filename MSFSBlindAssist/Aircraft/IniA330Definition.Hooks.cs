using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The seam: one partial-void hook per owner at each dispatch point of the A330 definition, so each plan
/// adds its behaviour in its own file. A hook nobody implements compiles to nothing, so the order the
/// dispatchers call them in (named beside each group) is the whole contract. Declarations only.
/// </summary>
public partial class IniA330Definition
{
    // BuildVariables: called in this order, before the base variables.
    partial void AddPanelVariables(Dictionary<string, SimVarDefinition> vars);          // plan 04
    partial void AddFcuVariables(Dictionary<string, SimVarDefinition> vars);            // plan 05
    partial void AddFlightDisplayVariables(Dictionary<string, SimVarDefinition> vars);  // plan 09
    partial void AddEcamVariables(Dictionary<string, SimVarDefinition> vars);           // plan 08
    partial void AddMcduVariables(Dictionary<string, SimVarDefinition> vars);           // plan 03
    partial void AddReadoutVariables(Dictionary<string, SimVarDefinition> vars);        // plan 10
    partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars);   // plan 06

    // HandleUIVariableSet: Fcu, Ecam, FlightDisplay, then Panel (the map's catch-all); stops at the first handled.
    partial void HandleFcuSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled);            // plan 05
    partial void HandleEcamSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled);           // plan 08
    partial void HandleFlightDisplaySet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled);   // plan 09
    partial void HandlePanelSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled);           // plan 04

    // ProcessSimVarUpdate: Readout, Panel, FlightDisplay, Fcu, Ecam, Mcdu; stops at the first handled; then the silent keys, then the base class.
    partial void ProcessReadoutUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);         // plan 10
    partial void ProcessPanelUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);           // plan 04
    partial void ProcessFlightDisplayUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);   // plan 09
    partial void ProcessFcuUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);             // plan 05
    partial void ProcessEcamUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);            // plan 08
    partial void ProcessMcduUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);            // plan 03

    // TryGetDisplayOverride: FlightDisplay, Fcu, Panel, Readout, Ecam; the first to set text wins.
    partial void FormatFlightDisplay(string varKey, double value, ref string? text);   // plan 09
    partial void FormatFcuDisplay(string varKey, double value, ref string? text);      // plan 05
    partial void FormatPanelDisplay(string varKey, double value, ref string? text);    // plan 04
    partial void FormatReadoutDisplay(string varKey, double value, ref string? text);  // plan 10
    partial void FormatEcamDisplay(string varKey, double value, ref string? text);     // plan 08

    // HandleHotkeyAction, after `_sim = simConnect;`: Readout, Fcu, FlightDisplay, Ecam, Mcdu, Efb; stops at the first handled.
    partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);        // plan 10
    partial void HandleFcuHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);            // plan 05
    partial void HandleFlightDisplayHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);  // plan 09
    partial void HandleEcamHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);           // plan 08
    partial void HandleMcduHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);           // plan 03
    partial void HandleEfbHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);            // plan 07

    // ResetAnnouncementBaselines (contextReset false) and OnSimContextReset (true).
    partial void ResetPanelBaselines(bool contextReset);          // plan 04
    partial void ResetFlightDisplayBaselines(bool contextReset);  // plan 09
    partial void ResetReadoutBaselines(bool contextReset);        // plan 10
    partial void ResetFcuBaselines(bool contextReset);            // plan 05
    partial void ResetEcamBaselines(bool contextReset);           // plan 08
    partial void ResetMcduBaselines(bool contextReset);           // plan 03
    partial void ResetReader(bool contextReset);                  // plan 02

    // OnSwitchedAway, in this order, then the base class.
    partial void CloseMcduWindows();           // plan 03
    partial void CloseEfb();                   // plan 07
    partial void CloseEcamWindows();           // plan 08
    partial void CloseFlightDisplayWindows();  // plan 09
    partial void CloseFcuWindows();            // plan 05
    partial void StopReader();                 // plan 02
}
