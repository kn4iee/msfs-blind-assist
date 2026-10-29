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
            // Alt+letter page keys; clicks through the Coherent mouse route, typing on the KCCU. While it is open it says
            // the FMS messages itself, and the background follows them silently (MfdWindowOpen).
            case HotkeyAction.ShowFenixMCDU:
            {
                hotkeyManager.ExitOutputHotkeyMode();
                var input = NewMfdInput(simConnect);
                ShowTrackedWindow(
                    () =>
                    {
                        var window = new Forms.A350.A350MfdWindow(() => Pfd?.Latest("MFD", 0),
                            async () => Displays == null ? null : (await Displays.DrawAsync(("MFD", 0)))?.FirstOrDefault(),
                            side => IsScreenDark(side == 0 ? "Captain MFD" : "First Officer MFD"), input, announcer);
                        window.FormClosed += (_, _) => _mfdWindowOpen = false;
                        return window;
                    },
                    w => { _mfdWindowOpen = true; w.Show(); w.BringToFront(); w.Activate(); });
                return true;
            }
            // Ctrl+Shift+R: the RMP window (A380 parity): the chosen RMP's screen, a frequency typed on its keypad, Enter
            // loads it as standby, Alt+1/2/3 swaps it in; Alt+T a squawk.
            case HotkeyAction.ShowRMP:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(
                    () => new Forms.A350.A350RmpWindow(rmp => Pfd?.Latest(A350.A350Rmp.Gauge(rmp).Gauge, A350.A350Rmp.Gauge(rmp).Index),
                        async rmp => Displays == null ? null : (await Displays.DrawAsync(A350.A350Rmp.Gauge(rmp)))?.FirstOrDefault(),
                        rmp => IsScreenDark($"RMP {rmp} display"), rpn => simConnect.ExecuteCalculatorCodeUnique(rpn), announcer),
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

            // The A380's FCU and autopilot hotkeys (FBW_A380_Hotkeys.txt). A press drives the FCU panel's own row
            // (AP, A/THR, LOC, APPR, a knob pushed or pulled), so it acts exactly as that row; what changed is said by the
            // FMA call-out, as on the A380, never by the press itself [CORE-7].
            case var press when A350.A350Hotkeys.PressRows.TryGetValue(press, out var pressRow):
                PressRow(pressRow, simConnect, announcer);
                return true;
            case HotkeyAction.ReadSpeed: AnnounceFcuWindow("Speed window", simConnect, announcer); return true;
            case HotkeyAction.ReadHeading: AnnounceFcuWindow("Heading window", simConnect, announcer); return true;
            case HotkeyAction.ReadAltitude: AnnounceFcuWindow("Altitude window", simConnect, announcer); return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA: AnnounceFcuWindow("V/S window", simConnect, announcer); return true;
            case HotkeyAction.FCUSetSpeed:
            case HotkeyAction.FCUSetHeading:
            case HotkeyAction.FCUSetAltitude:
            case HotkeyAction.FCUSetVS:
            {
                hotkeyManager.ExitInputHotkeyMode();
                var spec = Forms.A350.A350FcuValueWindow.For(action);
                ShowTrackedWindow(() => new Forms.A350.A350FcuValueWindow(this, spec, simConnect, announcer), w => w.ShowForm());
                return true;
            }
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.A350.A350AutopilotWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.A350.A350BaroWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            // The A380's read-outs, from what the A350 itself shows.
            case HotkeyAction.ReadAltimeter:
                if (CachedInput(simConnect, "XMLVAR_Baro1_Mode") is double mode && CachedInput(simConnect, "KOHLSMAN SETTING MB:1") is double mb)
                    announcer.AnnounceImmediate(A350.A350Hotkeys.Altimeter(mode, mb));
                else announcer.AnnounceImmediate("Altimeter not available");
                return true;
            case HotkeyAction.ReadFlaps:
                announcer.AnnounceImmediate(Cached(simConnect, "A350_FLAPS") is double flaps ? A350.A350Hotkeys.Flaps(flaps) : "Flaps not available");
                return true;
            case HotkeyAction.ReadGear:
                announcer.AnnounceImmediate(Cached(simConnect, "A350_LDG_LEVER") is double gear ? A350.A350Hotkeys.Gear(gear) : "Gear not available");
                return true;
            case HotkeyAction.ReadSpeedVLS: AnnounceSpeed("V L S", A350.A350Hotkeys.Vls, simConnect, announcer); return true;
            case HotkeyAction.ReadSpeedVFE: AnnounceSpeed("V F E next", A350.A350Hotkeys.VfeNext, simConnect, announcer); return true;
            // The A350's PFD shows no stall speed: its low-speed band starts at alpha protection, then alpha max.
            case HotkeyAction.ReadSpeedVS:
                announcer.AnnounceImmediate(A350.A350Hotkeys.Speed("Alpha protection speed", CachedInput(simConnect, A350.A350Hotkeys.AlphaProt) ?? 0)
                    + ", " + A350.A350Hotkeys.Speed("alpha max", CachedInput(simConnect, A350.A350Hotkeys.AlphaMax) ?? 0));
                return true;
            // Green dot, S and F are symbols on the PFD's speed tape with no value of their own in the A350: read off the
            // captain's tape (A350Pfd.TapeSymbol), as Shift+1 to 3 read the A380's.
            case HotkeyAction.ReadSpeedGD: AnnounceTapeSpeed(A350.Display.A350Pfd.GreenDot, "Green dot speed", announcer); return true;
            case HotkeyAction.ReadSpeedS: AnnounceTapeSpeed(A350.Display.A350Pfd.SSpeed, "S speed", announcer); return true;
            case HotkeyAction.ReadSpeedF: AnnounceTapeSpeed(A350.Display.A350Pfd.FSpeed, "F speed", announcer); return true;
            // Fuel: F pounds, Shift+F kilograms, the fleet's shared requests (the A380's phrasing).
            case HotkeyAction.ReadFuelQuantity:
                RequestFuelQuantity(simConnect, "A350");
                return true;
            case HotkeyAction.ReadFuelInfo:
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG, "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY_KG");
                return true;
            // W pounds, Shift+W kilograms: the SD's own gross weight and CG (the A380 repurposes W the same way).
            case HotkeyAction.ReadWaypointInfo:
            case HotkeyAction.ReadGrossWeightKg:
                announcer.AnnounceImmediate(Ecam?.LatestCapture is { } ewd
                    ? A350.A350Hotkeys.GrossWeight(A350.Display.A350SdPage.ReadPermanentData(A350.Display.A350Ewd.Parse(ewd).PermanentData),
                        pounds: action == HotkeyAction.ReadWaypointInfo)
                    : "Gross weight not available");
                return true;

            // Alt+P, N, S, I: a display read aloud, as its status display reads it (the MD-11's and PMDG's keys).
            case HotkeyAction.ReadDisplayPFD: AnnounceDisplay(PfdCaptainKey, announcer); return true;
            case HotkeyAction.ReadDisplayND: AnnounceDisplay("A350_DISP_ND_CPT", announcer); return true;
            case HotkeyAction.ReadDisplayISIS: AnnounceDisplay("A350_DISP_SFD", announcer); return true;
            case HotkeyAction.ReadDisplayLowerECAM:
                announcer.AnnounceImmediate(Ecam?.LatestCapture is { } sd && simConnect.GetCachedVariableValue(SdPageKey) is double sdPage
                    ? A350.A350Hotkeys.Sentences(A350.Display.A350SdText.Build(sd, sdPage))
                    : NotAvailableYet);
                return true;
            // X: the squawk as the captain's RMP shows it (the A350 leaves the stock transponder code at 0).
            // A readout from a dark screen says so (a dark PFD's "none shown" read as no capability, review 2026-10-28).
            case HotkeyAction.ReadSquawkCode:
                announcer.AnnounceImmediate(DarkReadout("A350_DISP_RMP_1") ?? (DisplayLines("A350_DISP_RMP_1") is { } rmp ? A350.A350Hotkeys.Squawk(rmp)
                    : NotAvailableYet));
                return true;
            // Ctrl+W: the ND's TO waypoint; Ctrl+0: the FMA's approach capability column. With no capture yet, the displays
            // are not available, never "none shown" (review 2026-10-29).
            case HotkeyAction.ReadNDWaypoint:
                announcer.AnnounceImmediate(DarkReadout("A350_DISP_ND_CPT")
                    ?? (DisplayLines("A350_DISP_ND_CPT") is not { } nd ? NotAvailableYet
                        : nd.FirstOrDefault(l => l.StartsWith("To waypoint", StringComparison.Ordinal)) ?? "No TO waypoint shown"));
                return true;
            // D and Shift+D: the aircraft's own VNAV distances (A350RouteDistances), the time at the ground speed; an unread
            // distance is no route, never 0 miles.
            case HotkeyAction.ReadDistanceToDest:
                announcer.AnnounceImmediate(A350.A350RouteDistances.Destination(
                    CachedInput(simConnect, A350.A350RouteDistances.DestinationVar) ?? 0, Cached(simConnect, "GROUND_VELOCITY") ?? 0,
                    FormatEtaFromDistance));
                return true;
            case HotkeyAction.ReadDistanceToTOD:
                announcer.AnnounceImmediate(A350.A350RouteDistances.TopOfDescent(
                    CachedInput(simConnect, A350.A350RouteDistances.DestinationVar) ?? 0,
                    CachedInput(simConnect, A350.A350RouteDistances.TopOfDescentVar) ?? double.NaN,
                    Cached(simConnect, "GROUND_VELOCITY") ?? 0, FormatEtaFromDistance));
                return true;
            case HotkeyAction.ReadApproachCapability:
                announcer.AnnounceImmediate(DarkReadout(PfdCaptainKey)
                    ?? (DisplayLines(PfdCaptainKey) is not { } pfd ? NotAvailableYet
                        : pfd.FirstOrDefault(l => l.StartsWith("FMA approach:", StringComparison.Ordinal)) is { } cap
                            ? "Approach capability " + cap["FMA approach:".Length..].Trim()
                            : "Approach capability none shown"));
                return true;
            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    /// <summary>Presses a panel row from code, exactly as the panel does (its write plan, its read-back).</summary>
    internal void PressRow(string key, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, double value = 1)
    {
        if (GetVariables().TryGetValue(key, out var def))
            HandleUIVariableSet(key, value, def, simConnect, announcer);
        else
            announcer.AnnounceImmediate("Not available on this aircraft");
    }

    /// <summary>A two-position row's state as a window's toggle flips it: what MSFSBA last commanded while that holds, else
    /// the cache, else null (review 2026-10-29: flipped from the raw cache, a second FD press within a second asked for On
    /// again and sent nothing, and an unread FD was taken for Off).</summary>
    internal double? ToggleState(string rowKey, SimConnectManager simConnect) => CurrentValue(rowKey, simConnect);

    /// <summary>The MFD window's KCCU and mouse: its key presses go out through <see cref="Send"/>, so a repeated key
    /// is a second press, never a duplicate the MobiFlight channel drops [VAR-4].</summary>
    internal A350.Display.A350MfdInput NewMfdInput(SimConnectManager simConnect) => new(rpn => Send(simConnect, rpn));

    /// <summary>An FCU window as the cockpit shows it now (<see cref="A350.A350FcuWindows"/>), or null while unread.</summary>
    internal string? FcuWindowText(string windowName, SimConnectManager simConnect)
    {
        var window = A350.A350FcuWindows.All.FirstOrDefault(w => w.Name == windowName);
        return window?.Compose(input => Cached(simConnect, input == window.Inputs[0] ? window.Key : KeyForInput(input)));
    }

    private void AnnounceFcuWindow(string windowName, SimConnectManager simConnect, ScreenReaderAnnouncer announcer) =>
        announcer.AnnounceImmediate(FcuWindowText(windowName, simConnect) is { } shown
            ? A350.A350Hotkeys.FcuReadout(windowName, shown)
            : "FCU not available");

    /// <summary>A streamed A350 variable by its name, wherever it streams (a row, a window, a light's input).</summary>
    internal double? CachedInput(SimConnectManager simConnect, string name) => Cached(simConnect, KeyForInput(name));

    private void AnnounceSpeed(string name, string var, SimConnectManager simConnect, ScreenReaderAnnouncer announcer) =>
        announcer.AnnounceImmediate(CachedInput(simConnect, var) is double knots ? A350.A350Hotkeys.Speed(name, knots) : name + " not available");

    /// <summary>A speed the captain's PFD shows only as a symbol on its tape, as the pilot sees it there: its value on the
    /// visible tape, above or below the speed scale while it is drawn outside it, "not shown" while the tape does not draw
    /// it, dark while the screen is.</summary>
    private void AnnounceTapeSpeed(string symbol, string name, ScreenReaderAnnouncer announcer)
    {
        var pfd = DisplayPanels.First(d => d.Key == PfdCaptainKey);
        announcer.AnnounceImmediate(DarkReadout(PfdCaptainKey)
            ?? (Pfd?.Latest(pfd.Gauge, pfd.Index) is not { } capture ? name + " not available"
                : A350.Display.A350Pfd.TapeSymbol(capture, 0, symbol) is not { } shown ? name + " not shown"
                : shown.OffScale != 0 ? $"{name} {(shown.OffScale > 0 ? "above" : "below")} the speed scale"
                : $"{name} {shown.Knots.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} knots"));
    }

    /// <summary>A display's latest lines, as its status display reads them (none while its screen is dark), or null while
    /// the displays are not running.</summary>
    private IReadOnlyList<string>? DisplayLines(string displayKey) =>
        DisplayPanels.FirstOrDefault(d => d.Key == displayKey) is not { } d ? null
            : IsDark(d) ? Array.Empty<string>()
            : Pfd?.Latest(d.Gauge, d.Index) is { } capture ? d.Read(capture) : null;

    /// <summary>"Captain PFD dark: ..." while that display's screen is dark, else null.</summary>
    private string? DarkReadout(string displayKey) =>
        DisplayPanels.FirstOrDefault(d => d.Key == displayKey) is { } display && IsDark(display)
            ? display.Panel + " " + char.ToLowerInvariant(DarkScreen[0]) + DarkScreen[1..]
            : null;

    private void AnnounceDisplay(string displayKey, ScreenReaderAnnouncer announcer)
    {
        if (DarkReadout(displayKey) is { } dark)
        {
            announcer.AnnounceImmediate(dark);
            return;
        }
        var lines = DisplayLines(displayKey);
        announcer.AnnounceImmediate(lines == null ? NotAvailableYet
            : lines.Count == 0 ? "Off: no power, or nothing shown." : string.Join(". ", lines));
    }
}
