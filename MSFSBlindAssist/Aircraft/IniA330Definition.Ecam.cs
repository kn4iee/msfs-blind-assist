// Plan 08 owns this file: the ECAM.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>Instrument camera view (0-based), found by sweeping all 38 on 2026-10-16: view 3 frames
    /// both ECAM screens.</summary>
    internal const int EcamView = 3;

    partial void HandleEcamHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ReadDisplayUpperECAM:
                ReadDisplay(Services.GeminiService.DisplayType.UpperECAM, "Upper ECAM", announcer, parentForm,
                    new Services.InstrumentViewRequest(sim, EcamView));
                handled = true; return;
            case HotkeyAction.ReadDisplayLowerECAM:
                ReadDisplay(Services.GeminiService.DisplayType.LowerECAM, "System display", announcer, parentForm,
                    new Services.InstrumentViewRequest(sim, EcamView));
                handled = true; return;
        }
    }
}
