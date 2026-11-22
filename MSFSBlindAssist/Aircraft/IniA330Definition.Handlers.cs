using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

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

    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();
    private bool _calloutOnGround = true;

    public override void ResetAnnouncementBaselines()
    {
        _takeoffCallouts.Reset();
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
        _takeoffCallouts.Reset();
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

    /// <summary>The feed, the speeds and their Ctrl+M rule ([TKO-5]): each call is muted by its speed's row.</summary>
    internal static readonly TakeoffCalloutKeys TakeoffKeys = new(TakeoffIasKey, TakeoffV1Key, TakeoffVrKey, TakeoffV2Key);

    /// <inheritdoc />
    public override string? TakeoffCalloutFeedKey => TakeoffIasKey;
    /// <inheritdoc />
    public override bool TakeoffCalloutFeedNeeded => _takeoffCallouts.NeedsSamples(_calloutOnGround);

    /// <summary>The airspeed feed speaks V1, Rotate and V2, whose Ctrl+M rows are the V-speeds', so it is
    /// kept out of MainForm's mute wrap and checks each call's row itself.</summary>
    public override bool IsMuteWrapExempt(string varName) => varName == TakeoffIasKey;

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
        switch (varName)
        {
            case TakeoffIasKey:
                var crossed = _takeoffCallouts.ProcessSample(value, _calloutOnGround);
                if (crossed.Count > 0 && !announcer.Suppressed
                    && TakeoffVSpeedCallouts.Compose(crossed, c => TakeoffKeys.IsMuted(c, DisabledMonitorSet)) is string sentence)
                    announcer.AnnounceImmediate(sentence);
                return true;
            case "SIM_ON_GROUND":
                _calloutOnGround = value >= 0.5;
                break;
            case TakeoffV1Key: _takeoffCallouts.SetV1(value); return true;
            case TakeoffVrKey: _takeoffCallouts.SetVR(value); return true;
            case TakeoffV2Key: _takeoffCallouts.SetV2(value); return true;
        }
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

    /// <summary>Instrument camera view (0-based), found by sweeping all 38 on 2026-10-16: view 3 frames
    /// both ECAM screens.</summary>
    internal const int EcamView = 3;

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
        double Cur(string key) => simConnect.GetCachedVariableValue(key) ?? 0;
        var fma = CurrentFma();
        string Suffix(string mode) => mode.Length > 0 ? $", {mode}" : "";

        switch (action)
        {
            case HotkeyAction.ReadHeading:
            {
                bool trk = Fv("A330_M_TRKFPA") == 1;
                int v = ((int)Math.Round(Cur(trk ? "A330_FCU_TRK" : "A330_FCU_HDG")) % 360 + 360) % 360;
                announcer.AnnounceImmediate($"FCU {(trk ? "track" : "heading")} {v:000}{Suffix(fma.Lateral)}");
                return true;
            }
            case HotkeyAction.ReadSpeed:
                if (SpeedManaged())
                {
                    double m = Cur("A330_MANAGED_SPEED");
                    announcer.AnnounceImmediate($"Speed managed{(m > 0 ? $", target {m:0} knots" : "")}{Suffix(fma.Thrust)}");
                }
                else if (Fv("A330_M_MACH") == 1)
                    announcer.AnnounceImmediate(FormattableString.Invariant($"FCU Mach {Cur("A330_FCU_SPD"):0.00}{Suffix(fma.Thrust)}"));
                else
                    announcer.AnnounceImmediate($"FCU speed {Cur("A330_FCU_SPD"):0} knots{Suffix(fma.Thrust)}");
                return true;
            case HotkeyAction.ReadAltitude:
                announcer.AnnounceImmediate($"FCU altitude {Cur("A330_FCU_ALT"):0} feet{Suffix(fma.Vertical)}");
                return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                bool fpa = Fv("A330_M_TRKFPA") == 1;
                string window = fpa ? FormattableString.Invariant($"flight path angle {Cur("A330_FCU_FPA"):0.0} degrees") : $"vertical speed {Cur("A330_FCU_VS"):0} feet per minute";
                announcer.AnnounceImmediate($"FCU {window}{Suffix(fma.Vertical)}");
                return true;
            }
            case HotkeyAction.ReadFlaps:
            {
                int idx = (int)Math.Round(Cur("A330_FLAPS"));
                string lever = idx >= 0 && idx < FlapDetents.Length ? FlapDetents[idx] : $"Flap lever {idx}";
                announcer.AnnounceImmediate($"{lever}. Slats {Cur("A330_SLAT_ANGLE"):0}, flaps {Cur("A330_FLAP_ANGLE"):0} degrees");
                return true;
            }
            case HotkeyAction.ReadGear:
                announcer.AnnounceImmediate(Cur("A330_GEAR") > 0.5 ? "Gear lever down" : "Gear lever up");
                return true;
            case HotkeyAction.ReadFuelQuantity:
                announcer.AnnounceImmediate(FuelSummary(simConnect));
                return true;
            case HotkeyAction.ReadNavRadioInfo: ReadRadios(simConnect, announcer); return true;
            case HotkeyAction.ReadDistanceToDest: ReadFmsDistance(simConnect, announcer, toTod: false); return true;
            case HotkeyAction.ReadDistanceToTOD: ReadFmsDistance(simConnect, announcer, toTod: true); return true;
            case HotkeyAction.ReadSpeedGD: ReadSpeed(simConnect, announcer, "A330_GREEN_DOT", "Green dot"); return true;
            case HotkeyAction.ReadSpeedS: ReadSpeed(simConnect, announcer, "A330_S_SPEED", "S speed"); return true;
            case HotkeyAction.ReadSpeedF: ReadSpeed(simConnect, announcer, "A330_F_SPEED", "F speed"); return true;
            case HotkeyAction.ReadSpeedVLS: ReadSpeed(simConnect, announcer, "A330_VLS", "VLS"); return true;
            case HotkeyAction.ReadSpeedVFE: ReadSpeed(simConnect, announcer, "A330_VMAX", "Maximum speed"); return true;
            case HotkeyAction.ReadDisplayUpperECAM:
                ReadDisplay(Services.GeminiService.DisplayType.UpperECAM, "Upper ECAM", announcer, parentForm,
                    new Services.InstrumentViewRequest(simConnect, EcamView));
                return true;
            case HotkeyAction.ReadDisplayLowerECAM:
                ReadDisplay(Services.GeminiService.DisplayType.LowerECAM, "System display", announcer, parentForm,
                    new Services.InstrumentViewRequest(simConnect, EcamView));
                return true;
            // ---- windows ----
            case HotkeyAction.ShowFenixMCDU:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA330.IniA330CduForm(simConnect, announcer, CduProfile()), f => f.ShowForm());
                return true;
            case HotkeyAction.ShowPMDGEFB:
                hotkeyManager.ExitInputHotkeyMode();
                ShowEfb(announcer);
                return true;
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA330.A330MonitorManagerForm(GetVariables(), ShortName,
                    DisabledMonitorList), m => m.ShowForm());
                return true;
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    private SimConnect.CoherentPmdgEfbClient? _efbClient;

    private static Forms.IniA330.CduFormProfile CduProfile() => Forms.IniA330.CduFormProfile.A330(A330CduGlyphLibrary.A330);

    private void ShowEfb(ScreenReaderAnnouncer announcer)
    {
        ShowTrackedWindow(() =>
        {
            _efbClient?.Dispose();
            _efbClient = SimConnect.CoherentPmdgEfbClient.ForIniA330();
            _efbClient.Start();
            var client = _efbClient;
            var f = new Forms.FBWA380.FbwEfbForm(client, announcer, $"{ShortName} EFB", "EFB", "EFB");
            f.VisibleChanged += (_, _) => client.SetActive(!f.IsDisposed && f.Visible);
            f.Disposed += (_, _) => { client.Dispose(); if (ReferenceEquals(_efbClient, client)) _efbClient = null; };
            return f;
        }, f => { _efbClient?.SetActive(true); f.ShowForm(); });
    }

    internal string FuelSummary(SimConnectManager sim)
    {
        double V(string key) => sim.GetCachedVariableValue(key) ?? 0;
        bool metric = (sim.GetCachedVariableValue("A330_IS_METRIC") ?? 1) > 0.5;
        string W(double kg) => metric ? $"{kg:0} kilograms" : $"{kg * 2.20462:0} pounds";
        double centreKg = V("A330_FUEL_CENTER") * V("A330_FUEL_WPG") / 2.20462;
        double tanks = FuelVars.Sum(f => V(f.key)) + centreKg;
        double total = V("A330_FUEL_TOTAL_LB") > 0 ? V("A330_FUEL_TOTAL_LB") / 2.20462 : tanks;
        var parts = FuelVars.Where(f => V(f.key) > 1).Select(f => $"{f.name} {W(V(f.key))}").ToList();
        if (centreKg > 1) parts.Insert(Math.Min(2, parts.Count), $"centre {W(centreKg)}");
        return $"Fuel {W(total)}. " + string.Join(", ", parts);
    }

    private static void ReadSpeed(SimConnectManager sim, ScreenReaderAnnouncer announcer, string key, string name)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            double? v = await sim.ReadFreshAsync(key, 1000);
            string msg = v is double k && k > 20 ? $"{name} {k:0} knots" : $"{name} not available";
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    private static void ReadFmsDistance(SimConnectManager sim, ScreenReaderAnnouncer announcer, bool toTod)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            double? dist = await sim.ReadFreshAsync(toTod ? "A330_TD_DIST_TOTAL" : "A330_DIST_DEST", 1000);
            if (!toTod && (dist is not double d0 || d0 <= 0.5)) dist = await sim.ReadFreshAsync("A330_APPR_DIST", 1000);
            double gs = await sim.ReadFreshAsync("A330_GS_FRESH", 1000) ?? 0;
            Log.Info("a330", $"{(toTod ? "T/D" : "dest")} distance raw={dist?.ToString(CultureInfo.InvariantCulture) ?? "none"} gs={gs:0}");
            string msg = DescribeFmsDistance(dist, gs, toTod);
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    private static void ReadRadios(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            async Task<double?> R(string k) => await sim.ReadFreshAsync(k, 1000);
            string Mhz(double? v) => v is double f && f > 100 ? f.ToString("0.00#", CultureInfo.InvariantCulture) : "not tuned";
            var parts = new List<string>();
            double? ils = await R("A330_NAV3_FREQ");
            bool loc = (await R("A330_NAV3_LOC") ?? 0) > 0.5, gs = (await R("A330_NAV3_GS") ?? 0) > 0.5;
            parts.Add($"ILS {Mhz(ils)}{(loc ? (gs ? ", localizer and glide slope received" : ", localizer received") : ", not received")}");
            parts.Add($"VOR 1 {Mhz(await R("A330_NAV1_FREQ"))}");
            parts.Add($"VOR 2 {Mhz(await R("A330_NAV2_FREQ"))}");
            parts.Add($"VHF 1 {Mhz(await R("A330_COM1_ACT"))}, standby {Mhz(await R("A330_COM1_STBY"))}");
            string msg = string.Join(". ", parts);
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    private static bool On(SimConnectManager sim, string key) => (sim.GetCachedVariableValue(key) ?? 0) > 0.5;

    internal static string DescribeFmsDistance(double? nm, double gsKnots, bool toTod)
    {
        if (nm is not double d || d <= 0.5 || double.IsNaN(d))
            return toTod ? "Top of descent not available" : "Distance to destination not available. Enter a flight plan in the MCDU.";
        string time = "";
        if (gsKnots >= 30)
        {
            int min = (int)Math.Round(d / gsKnots * 60);
            time = min >= 60 ? $", {min / 60} hour{(min / 60 == 1 ? "" : "s")} {min % 60} minutes" : $", {min} minute{(min == 1 ? "" : "s")}";
        }
        return toTod ? $"{d:0} miles to top of descent{time}" : $"Destination {d:0} miles{time}";
    }
}
