// Plan 05 owns this file: the FCU, the autopilot and the altimeter.

using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;
using MSFSBlindAssist.Aircraft.A330;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>FCU buttons: key -> what a press does (an L:var press flag, or an input event).</summary>
    internal static readonly (string key, string label, string? pulse, string? inputEvent)[] AutopilotButtons =
    {
        ("A330_AP1", "AP 1", "INI_AP1_BUTTON", null),
        ("A330_AP2", "AP 2", "INI_AP2_BUTTON", null),
        ("A330_ATHR", "A/THR", null, "AIRLINER_ATHR_PUSH"),
        ("A330_SPD_PULL", "Speed knob pull (selected speed)", "INI_FCU_SELECTED_SPEED_BUTTON", null),
        ("A330_SPD_PUSH", "Speed knob push (managed speed)", "INI_FCU_MANAGED_SPEED_BUTTON", null),
        ("A330_HDG_PULL", "Heading knob pull (selected heading)", "INI_FCU_SELECTED_HEADING_BUTTON", null),
        ("A330_HDG_PUSH", "Heading knob push (managed, NAV)", "INI_FCU_MANAGED_HEADING_BUTTON", null),
        ("A330_ALT_PULL", "Altitude knob pull (open climb or descent)", "INI_FCU_ALTITUDE_PULL_COMMAND", null),
        ("A330_ALT_PUSH", "Altitude knob push (managed climb or descent)", "INI_FCU_ALTITUDE_PUSH_COMMAND", null),
        ("A330_VS_PULL", "V/S knob pull (selected vertical speed)", null, "AIRLINER_MCU_VS_PULL"),
        ("A330_VS_PUSH", "V/S knob push (level off)", null, "AIRLINER_MCU_VS_PUSH"),
        ("A330_LOC", "LOC", "INI_LOCALIZER_BUTTON", null),
        ("A330_APPR", "APPR", "INI_APPROACH_BUTTON", null),
        ("A330_ALT_BTN", "ALT (hold the current altitude)", null, "AIRLINER_ALT_PUSH"),
    };

    internal static readonly string[] AutopilotSelectKeys = { "A330_SPD_MACH", "A330_TRK_FPA", "A330_ALT_STEP", "A330_METRIC" };

    internal static IEnumerable<string> AutopilotControlKeys =>
        AutopilotButtons.Select(b => b.key).Concat(AutopilotSelectKeys);

    internal static readonly string[] AutopilotDisplayKeys =
        { "A330_AP1_ON", "A330_AP2_ON", "A330_ATHR_ON", A330SharedKeys.FcuSpd, A330SharedKeys.FcuHdg, A330SharedKeys.FcuAlt, A330SharedKeys.FcuVs };

    /// <summary>The ALT knob step selector's input for 100-foot / 1000-foot steps: input 0 = 100 ft
    /// (measured: input 0 reads 1 and the knob steps 100 feet).</summary>
    internal static double AltStepInput(bool hundredFeet) => hundredFeet ? 0 : 1;

    /// <summary>True for an FCU event pressed with its own _Push/_Release actions rather than by value
    /// (measured: the V/S knob push).</summary>
    internal static bool PressesWithPushAction(string inputEvent) => inputEvent == "AIRLINER_MCU_VS_PUSH";

    private void PressAutopilot(SimConnectManager sim, ScreenReaderAnnouncer announcer, string key)
    {
        var b = AutopilotButtons.FirstOrDefault(x => x.key == key);
        if (b.key == null) return;
        if (b.pulse != null)
        {
            if (CanCalc(sim, announcer, b.label)) Pulse(sim, b.pulse);
        }
        else if (b.inputEvent != null)
        {
            if (PressesWithPushAction(b.inputEvent)) { if (CanCalc(sim, announcer, b.label)) PushEvent(sim, b.inputEvent); }
            else PressByValue(sim, b.inputEvent);
        }
    }

    // Also used by Readouts (plan 10).
    private bool SpeedManaged() => (_sim?.GetCachedVariableValue(A330SharedKeys.MSpdManaged) ?? 0) > 0.5;

    private string ApStatus()
    {
        var sim = _sim;
        if (sim == null) return "unknown";
        bool ap1 = (sim.GetCachedVariableValue("A330_AP1_ON") ?? 0) > 0.5;
        bool ap2 = (sim.GetCachedVariableValue("A330_AP2_ON") ?? 0) > 0.5;
        bool at = (sim.GetCachedVariableValue("A330_ATHR_ON") ?? 0) > 0.5;
        return (ap1 && ap2 ? "AP 1 and 2" : ap1 ? "AP 1" : ap2 ? "AP 2" : "AP off") + (at ? ", A/THR on" : ", A/THR off");
    }

    internal IEnumerable<Forms.IniA330.IniA330AutopilotWindow.Row> AutopilotRows(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        bool On(string key) => (sim.GetCachedVariableValue(key) ?? 0) > 0.5;
        Forms.IniA330.IniA330AutopilotWindow.Row R(string label, Func<string> state, string key)
            => new(label, state, () => PressAutopilot(sim, announcer, key));
        // The Fenix window's buttons first, in its order (the A330 has no EXPED or disconnect buttons),
        // then the A330's own knob keys; no Alt access keys, as on the Fenix.
        yield return R("AP1", () => On("A330_AP1_ON") ? "engaged" : "off", "A330_AP1");
        yield return R("AP2", () => On("A330_AP2_ON") ? "engaged" : "off", "A330_AP2");
        yield return R("ATHR", () => On("A330_ATHR_ON") ? "on" : "off", "A330_ATHR");
        yield return R("APPR", () => "", "A330_APPR");
        yield return R("LOC", () => CurrentFma().Lateral.StartsWith("LOC") ? "active" : "", "A330_LOC");
        yield return R("Speed Push", () => "", "A330_SPD_PUSH");
        yield return R("Speed Pull", () => SpeedManaged() ? "managed now" : "selected now", "A330_SPD_PULL");
        yield return R("Heading Push", () => "", "A330_HDG_PUSH");
        yield return R("Heading Pull", () => CurrentFma().Lateral, "A330_HDG_PULL");
        yield return R("Altitude Push", () => "", "A330_ALT_PUSH");
        yield return R("Altitude Pull", () => CurrentFma().Vertical, "A330_ALT_PULL");
        yield return R("V/S Push", () => "", "A330_VS_PUSH");
        yield return R("V/S Pull", () => "", "A330_VS_PULL");
        yield return R("ALT (hold this altitude)", () => "", AltButtonKey);
        yield return new("Read flight modes", () => "", () => announcer.AnnounceImmediate(CurrentFma().Summary()));
    }

    // =====================================================================
    // Altimeter: the B key and its set dialog (captain's side; a set writes all three altimeters)
    // =====================================================================

    /// <summary>STD from the cockpit's own mode var; the stock STD flag only when that did not answer.</summary>
    private static async Task<double?> ReadBaroModeAsync(SimConnectManager sim)
    {
        double? mode = await sim.ReadFreshAsync("A330_BARO_MODE_FRESH", 1000);
        if (mode is not null) return mode;
        double? std = await sim.ReadFreshAsync("A330_BARO_STD_FRESH", 1000);
        return std is double s ? (s > 0.5 ? 3 : 0) : null;
    }

    private static void ReadAltimeter(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            double? mb = await sim.ReadFreshAsync("A330_BARO_FRESH", 1000);
            double? mode = await ReadBaroModeAsync(sim);
            string msg = DescribeAltimeter(mb, mode);
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    /// <summary>The ways the aircraft might take a baro knob pull (STD) or push (QNH), tried in order
    /// until the mode reads back changed; the walk is closed-loop and logs which one worked. Measured
    /// 2026-11-19 on the ground, powered (captain's knob): the input event pressed BY VALUE works (PULL
    /// set 1 then 0 turned XMLVAR_Baro1_Mode 0 → 3, STD; PUSH the same way 3 → 0, QNH), so it goes first.
    /// The event's own _Push/_Release action did nothing; it and the L:var commands stay as
    /// fallbacks.</summary>
    private IEnumerable<(string name, Action<SimConnectManager> press)> BaroPresses(bool toStd)
    {
        string knob = toStd ? "PULL" : "PUSH";
        yield return ($"input {knob} value", s => { PressByValue(s, $"AIRLINER_CPT_BARO_SET_{knob}"); PressByValue(s, $"AIRLINER_FO_BARO_SET_{knob}"); });
        yield return ($"input {knob} action", s => { PushEvent(s, $"AIRLINER_CPT_BARO_SET_{knob}"); PushEvent(s, $"AIRLINER_FO_BARO_SET_{knob}"); });
        foreach (var p in AltimeterCommandPresses(toStd)) yield return p;
    }

    /// <summary>iniBuilds' altimeter command L:vars, a fallback (not measured). Which flag does which is
    /// not proven, so both are offered, the likely one first.</summary>
    private IEnumerable<(string name, Action<SimConnectManager> press)> AltimeterCommandPresses(bool toStd)
    {
        foreach (var flag in toStd ? new[] { "STD", "PULL" } : new[] { "PULL", "STD" })
            yield return ($"L:var {flag}", s => { Pulse(s, $"INI_CPT_ALTIMETER_{flag}_COMMAND"); Pulse(s, $"INI_FO_ALTIMETER_{flag}_COMMAND"); });
    }

    private void ToggleStandard(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!CanCalc(sim, announcer, "Altimeter standard")) return;
        var ui = SynchronizationContext.Current;
        string log = ShortName.ToLowerInvariant();
        _ = Task.Run(async () =>
        {
            double? start = await ReadBaroModeAsync(sim);
            if (start is null)
            {
                ui?.Post(_ => announcer.AnnounceImmediate("Altimeter did not report back"), null);
                return;
            }
            bool wasStd = IsStdMode(start);
            double? mode = start;
            foreach (var (name, press) in BaroPresses(!wasStd))
            {
                // the presses touch the input-event hashes and the SimConnect handle: send from the UI thread
                if (ui != null) ui.Post(_ => press(sim), null); else press(sim);
                await Task.Delay(700);
                mode = await ReadBaroModeAsync(sim);
                Log.Info(log, $"altimeter {(wasStd ? "QNH" : "STD")} via {name}: mode {start} -> {mode?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
                if (mode is null || IsStdMode(mode) != wasStd) break;
            }
            double? mb = await sim.ReadFreshAsync("A330_BARO_FRESH", 1000);
            string msg = mode is null ? "Altimeter did not report back"
                : IsStdMode(mode) == wasStd ? $"Altimeter did not change. {DescribeAltimeter(mb, mode)}"
                : DescribeAltimeter(mb, mode);
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    internal List<ToggleButtonDef> BaroToggles(SimConnectManager sim, ScreenReaderAnnouncer announcer) => new()
    {
        new("Standard",() => IsStdMode(sim.GetCachedVariableValue("A330_BARO_MODE_FRESH")) ? "STD" : "QNH",
            () => ToggleStandard(sim, announcer))
        {
            // ToggleStandard speaks its own closed-loop verdict (and a refusal): the dialog's 1.2 s
            // "<label> <state>" would repeat it and cut the real one off
            SuppressStateAnnounce = () => true,
        },
    };

    private void ShowBaroDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        _ = sim.ReadFreshAsync("A330_BARO_MODE_FRESH", 1000);   // so the Standard button opens with the live state
        string log = ShortName.ToLowerInvariant();
        var toggles = BaroToggles(sim, announcer);
        var dlg = new ValueInputForm("Altimeter Setting", "altimeter", "hPa (900-1100) or inHg (26.00-32.00)", announcer,
            input => TryParseBaro(input, out _)
                ? (true, "") : (false, "Enter hPa (900-1100) or inHg (26.00-32.00)"),
            toggles,
            input =>
            {
                if (!TryParseBaro(input, out double hpa)) return;
                if (!CanCalc(sim, announcer, "Altimeter")) return;
                string mb16 = Num(Math.Round(hpa * 16));
                // captain, first officer and standby: index first, then millibars x 16
                for (int i = 1; i <= 3; i++) Write(sim, $"{i} {mb16} (>K:2:KOHLSMAN_SET)");
                var ui = SynchronizationContext.Current;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    double? got = await sim.ReadFreshAsync("A330_BARO_FRESH", 1500);
                    double? mode = await ReadBaroModeAsync(sim);
                    Log.Info(log, $"altimeter set {hpa:0.0} hPa: reads {got?.ToString(CultureInfo.InvariantCulture) ?? "none"}, mode {mode?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
                    string msg = got is null ? "Altimeter sent, it did not report back"
                        : Math.Abs(got.Value - hpa) > 0.6 ? $"Altimeter shows {got.Value:0} hectopascals, not {hpa:0}"
                        : IsStdMode(mode) ? $"{DescribeAltimeter(got, 0)} set behind standard. Standard is still selected."
                        : DescribeAltimeter(got, mode);
                    if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
                });
            });
        dlg.ShowCancelButton = false;
        dlg.Show(parent);
    }

    // =====================================================================
    // FCU set dialogs — the windows are written directly (measured: heading 270 written, the stock
    // autopilot heading followed) and confirmed by a fresh read-back.
    // =====================================================================

    private void SetAndConfirm(SimConnectManager sim, ScreenReaderAnnouncer announcer, string lvar, string cacheKey,
        double target, Func<double, string> describe, double tolerance = 0.6)
    {
        if (!CanCalc(sim, announcer, "FCU")) return;
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            Write(sim, $"{Num(target)} (>L:{lvar})");
            await Task.Delay(400);
            double? got = await sim.ReadFreshAsync(cacheKey, 1500);
            string msg = got is null ? $"{describe(target)} sent, the FCU did not report back"
                : Math.Abs(got.Value - target) < tolerance ? describe(got.Value)
                : $"FCU shows {describe(got.Value)}, not {describe(target)}";
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    // The typed-value rules of the FCU dialogs, apart from the forms so a test can reach them.

    /// <summary>Altimeter: hPa (900-1100) or inHg (26-32); hpa is always in hectopascals.</summary>
    internal static bool TryParseBaro(string? input, out double hpa)
    {
        hpa = 0;
        if (!NumberParsing.TryParseTyped(input, out double v)) return false;
        if (!((v >= 900 && v <= 1100) || (v >= 26 && v <= 32))) return false;
        hpa = v >= 900 ? v : v * 33.8639;
        return true;
    }

    /// <summary>Speed: knots (100-399) or Mach (0.1-0.89).</summary>
    internal static bool TryParseSpeed(string? input, out double v)
        => NumberParsing.TryParseTyped(input, out v) && ((v >= 100 && v <= 399) || (v >= 0.1 && v < 0.9));

    /// <summary>Heading or track, whole degrees 0-360.</summary>
    internal static bool TryParseHeading(string? input, out int h)
    {
        h = 0;
        if (!NumberParsing.TryParseTyped(input, out double v) || v != Math.Floor(v) || v < 0 || v > 360) return false;
        h = (int)v;
        return true;
    }

    /// <summary>Altitude, whole feet 100-49000.</summary>
    internal static bool TryParseAltitude(string? input, out int a)
    {
        a = 0;
        if (!NumberParsing.TryParseTyped(input, out double v) || v != Math.Floor(v) || v < 100 || v > 49000) return false;
        a = (int)v;
        return true;
    }

    /// <summary>Vertical speed (+-6000 feet per minute) or, with fpa, flight path angle (+-9.9 degrees).</summary>
    internal static bool TryParseVs(string? input, bool fpa, out double v)
        => NumberParsing.TryParseTyped(input, out v) && (fpa ? Math.Abs(v) <= 9.9 : Math.Abs(v) <= 6000);

    /// <summary>One dial write a dialog's commit decided on. Alt is TRK (heading dialog) or FPA (V/S dialog).</summary>
    internal sealed record FcuDialSet(string Lvar, string CacheKey, double Target, double Tolerance, bool Alt);

    /// <summary>The heading dialog's write: the track dial when isTrk() says the aircraft is in TRK-FPA, read now.</summary>
    internal static FcuDialSet? HeadingSet(string? input, Func<bool> isTrk)
    {
        if (!TryParseHeading(input, out int h)) return null;
        bool trk = isTrk();
        return new(trk ? "INI_TRK_DIAL" : "INI_HEADING_DIAL", trk ? A330SharedKeys.FcuTrk : A330SharedKeys.FcuHdg, h % 360, 0.6, trk);
    }

    /// <summary>The V/S dialog's check, in whichever of V/S and FPA isFpa() says the aircraft is in, read now.</summary>
    internal static (bool isValid, string message) VsValidator(string? input, Func<bool> isFpa)
    {
        bool fpa = isFpa();
        return TryParseVs(input, fpa, out _) ? (true, "") : (false, fpa ? "Enter -9.9 to 9.9" : "Enter -6000 to 6000");
    }

    /// <summary>The V/S dialog's write: the FPA dial or the V/S dial by isFpa(), read now.</summary>
    internal static FcuDialSet? VsSet(string? input, Func<bool> isFpa)
    {
        bool fpa = isFpa();
        if (!TryParseVs(input, fpa, out double v)) return null;
        return fpa ? new("INI_FPA_DIAL", A330SharedKeys.FcuFpa, Math.Round(v, 1), 0.06, true)
                   : new("INI_VVI_DIAL", A330SharedKeys.FcuVs, Math.Round(v / 100.0) * 100, 60, false);
    }

    internal const string AltButtonKey = "A330_ALT_BTN";

    /// <summary>LOC / APPR show what the FMA says they did — active, armed, or off.</summary>
    private string LocState()
    {
        var f = CurrentFma();
        return f.Lateral == "LOC" || f.Lateral.StartsWith("LOC ") ? "Active" : f.Armed.Contains("LOC") ? "Armed" : "Off";
    }

    private string ApprState()
    {
        var f = CurrentFma();
        if (f.Vertical is "LAND" or "G S" or "G S star") return "Active";
        return f.Armed.Contains("G S") ? "Armed" : "Off";
    }

    /// <summary>The HDG-V/S / TRK-FPA key: a toggle on the aircraft, so one press flips it. The dialog
    /// it sits in was opened in one mode; the button shows the live one.</summary>
    private ToggleButtonDef TrkFpaToggle(SimConnectManager sim, ScreenReaderAnnouncer announcer) =>
        new("HDG/VS TRK/FPA", () => Fv(A330SharedKeys.MTrkfpa) == 1 ? "TRK FPA" : "HDG V/S", () =>
        {
            if (CanCalc(sim, announcer, "TRK-FPA")) Pulse(sim, "INI_FCU_HDG_VS_COMMAND");
        });

    // The knob push/pull buttons whose state is a constant, or whose result the flight-mode call-out
    // speaks, turn the dialog's interrupting 1.2 s read-back off: it repeated the label NVDA had just
    // read and cut the call-out off. Buttons with a state the screen reader cannot say keep it.

    internal List<ToggleButtonDef> HeadingToggles(SimConnectManager sim, ScreenReaderAnnouncer announcer) => new()
    {
        new("Heading Push", () => CurrentFma().Lateral == "NAV" ? "Active" : "", () => PressAutopilot(sim, announcer, "A330_HDG_PUSH")) { SuppressStateAnnounce = () => true },
        new("Heading Pull", () => "", () => PressAutopilot(sim, announcer, "A330_HDG_PULL")) { SuppressStateAnnounce = () => true },
        TrkFpaToggle(sim, announcer),
        new("LOC", LocState, () => PressAutopilot(sim, announcer, "A330_LOC")),
    };

    internal List<ToggleButtonDef> SpeedToggles(SimConnectManager sim, ScreenReaderAnnouncer announcer) => new()
    {
        new("Speed Push", () => SpeedManaged() ? "Managed" : "Selected", () => PressAutopilot(sim, announcer, "A330_SPD_PUSH")),
        new("Speed Pull", () => SpeedManaged() ? "Managed" : "Selected", () => PressAutopilot(sim, announcer, "A330_SPD_PULL")),
        new("SPD/MACH", () => Fv(A330SharedKeys.MMach) == 1 ? "Mach" : "Knots",
            () => SetInputEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", Fv(A330SharedKeys.MMach) == 1 ? 0 : 1)),
        new("ATHR", () => On(sim, "A330_ATHR_ON") ? "On" : "Off", () => PressAutopilot(sim, announcer, "A330_ATHR")),
    };

    internal List<ToggleButtonDef> AltitudeToggles(SimConnectManager sim, ScreenReaderAnnouncer announcer) => new()
    {
        new("Altitude Push", () => "", () => PressAutopilot(sim, announcer, "A330_ALT_PUSH")) { SuppressStateAnnounce = () => true },
        new("Altitude Pull", () => "", () => PressAutopilot(sim, announcer, "A330_ALT_PULL")) { SuppressStateAnnounce = () => true },
        new("ALT (hold the current altitude)", () => CurrentFma().Vertical == "ALT" ? "Active" : "", () => PressAutopilot(sim, announcer, AltButtonKey)) { SuppressStateAnnounce = () => true },
        new("Knob steps (100 or 1000 feet)", () => (sim.GetCachedVariableValue("A330_ALT_STEP") ?? 0) > 0.5 ? "100 feet" : "1000 feet",
            () => SetInputEvent(sim, announcer, "Altitude knob steps", "AIRLINER_MCU_ALTSELECT", AltStepInput(!((sim.GetCachedVariableValue("A330_ALT_STEP") ?? 0) > 0.5)))),
        new("Metric altitude",() => On(sim, "A330_METRIC") ? "On" : "Off",
            () => SetInputEvent(sim, announcer, "Metric altitude", "AIRLINER_METRIC", On(sim, "A330_METRIC") ? 0 : 1)),
        new("AP1", () => On(sim, "A330_AP1_ON") ? "On" : "Off", () => PressAutopilot(sim, announcer, "A330_AP1")),
        new("AP2",() => On(sim, "A330_AP2_ON") ? "On" : "Off", () => PressAutopilot(sim, announcer, "A330_AP2")),
    };

    internal List<ToggleButtonDef> VsToggles(SimConnectManager sim, ScreenReaderAnnouncer announcer) => new()
    {
        new("V/S Push", () => "", () => PressAutopilot(sim, announcer, "A330_VS_PUSH")) { SuppressStateAnnounce = () => true },
        new("V/S Pull", () => "", () => PressAutopilot(sim, announcer, "A330_VS_PULL")) { SuppressStateAnnounce = () => true },
        TrkFpaToggle(sim, announcer),
        new("APPR",ApprState, () => PressAutopilot(sim, announcer, "A330_APPR")),
    };

    private void ShowHeadingDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        bool trk = Fv(A330SharedKeys.MTrkfpa) == 1;
        var toggles = HeadingToggles(sim, announcer);
        var dlg = new ValueInputForm(trk ? "FCU Track" : "FCU Heading", trk ? "track" : "heading", "0-359", announcer,
            input => TryParseHeading(input, out _) ? (true, "") : (false, "Enter a heading between 0 and 359"),
            toggles,
            input =>
            {
                // HDG or TRK as the aircraft is NOW (the TRK/FPA button in this dialog changes it)
                if (HeadingSet(input, () => Fv(A330SharedKeys.MTrkfpa) == 1) is not { } set) return;
                string word = set.Alt ? "track" : "heading";
                SetAndConfirm(sim, announcer, set.Lvar, set.CacheKey, set.Target, v => $"{word} {((int)Math.Round(v) % 360 + 360) % 360:000}", set.Tolerance);
            });
        dlg.ShowCancelButton = true;   // closes and sends nothing (a value is sent only by Enter in the box)
        dlg.Show(parent);
    }

    private void ShowSpeedDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        var toggles = SpeedToggles(sim, announcer);
        var dlg = new ValueInputForm("FCU Speed", "speed", "Knots: 100-399 / Mach: 0.10-0.89", announcer,
            input => TryParseSpeed(input, out _)
                ? (true, "") : (false, "Enter knots (100-399) or Mach (0.10-0.89)"),
            toggles,
            input =>
            {
                if (!TryParseSpeed(input, out double v)) return;
                if (SpeedManaged()) announcer.AnnounceImmediate("Speed is managed; pull the speed knob to fly this speed.");
                bool wantMach = v < 1, isMach = Fv(A330SharedKeys.MMach) == 1;
                if (wantMach != isMach)
                {
                    // one window holds both (measured: SPD/MACH turned 100 into 0.151) — swap units first
                    SetInputEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", wantMach ? 1 : 0);
                    // awaited on the dialog's (UI) context: SetAndConfirm captures that context to
                    // speak its verdict, and from a pool thread it captured none and said nothing
                    _ = SetAfterSwap();
                    async Task SetAfterSwap()
                    {
                        await Task.Delay(600);
                        SetAndConfirm(sim, announcer, "INI_AIRSPEED_DIAL", A330SharedKeys.FcuSpd, wantMach ? Math.Round(v, 2) : Math.Round(v),
                            s => wantMach ? FormattableString.Invariant($"Mach {s:0.00}") : $"speed {s:0} knots", wantMach ? 0.006 : 0.6);
                    }
                }
                else
                    SetAndConfirm(sim, announcer, "INI_AIRSPEED_DIAL", A330SharedKeys.FcuSpd, wantMach ? Math.Round(v, 2) : Math.Round(v),
                        s => wantMach ? FormattableString.Invariant($"Mach {s:0.00}") : $"speed {s:0} knots", wantMach ? 0.006 : 0.6);
            });
        dlg.ShowCancelButton = true;
        dlg.Show(parent);
    }

    private void ShowAltitudeDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        var toggles = AltitudeToggles(sim, announcer);
        var dlg = new ValueInputForm("FCU Altitude", "altitude", "100-49000 feet", announcer,
            input => TryParseAltitude(input, out _) ? (true, "") : (false, "Enter an altitude between 100 and 49000 feet"),
            toggles,
            input =>
            {
                if (TryParseAltitude(input, out int a))
                    SetAndConfirm(sim, announcer, "INI_ALTITUDE_DIAL", A330SharedKeys.FcuAlt, Math.Round(a / 100.0) * 100, v => $"altitude {v:0} feet");
            });
        dlg.ShowCancelButton = true;
        dlg.Show(parent);
    }

    private void ShowVsDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        bool fpa = Fv(A330SharedKeys.MTrkfpa) == 1;
        var toggles = VsToggles(sim, announcer);
        var dlg = new ValueInputForm(fpa ? "FCU Flight Path Angle" : "FCU Vertical Speed", fpa ? "flight path angle" : "vertical speed",
            fpa ? "-9.9 to 9.9 degrees" : "-6000 to 6000 feet per minute", announcer,
            // the title and range are as the dialog opened; the mode the value is checked and written for
            // is the aircraft's own at that moment, since the TRK/FPA button in this dialog changes it
            input => VsValidator(input, () => Fv(A330SharedKeys.MTrkfpa) == 1),
            toggles,
            input =>
            {
                if (VsSet(input, () => Fv(A330SharedKeys.MTrkfpa) == 1) is not { } set) return;
                if (set.Alt) SetAndConfirm(sim, announcer, set.Lvar, set.CacheKey, set.Target, x => FormattableString.Invariant($"flight path angle {x:0.0} degrees"), set.Tolerance);
                else SetAndConfirm(sim, announcer, set.Lvar, set.CacheKey, set.Target, x => $"vertical speed {x:0} feet per minute", set.Tolerance);
            });
        dlg.ShowCancelButton = true;
        dlg.Show(parent);
    }

    /// <summary>The cockpit's baro mode (XMLVAR_Baro1_Mode): 2 or more is STD. Measured: 3 in STD (2026-10-17
    /// at FL220, and 2026-11-19 after a knob pull on the ground), 0 in QNH.</summary>
    internal static bool IsStdMode(double? mode) => mode is double m && m >= 1.5;

    internal static string DescribeAltimeter(double? millibars, double? mode)
    {
        if (IsStdMode(mode)) return "Altimeter standard";
        if (millibars is not double mb || mb <= 0) return "Altimeter not available";
        return $"Altimeter {mb.ToString("0", CultureInfo.InvariantCulture)} hectopascals, "
             + $"{(mb / 33.8639).ToString("0.00", CultureInfo.InvariantCulture)} inches";
    }

    partial void AddFcuVariables(Dictionary<string, SimVarDefinition> vars)
    {
        var engaged = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" };

        // ---------------- Autopilot (FCU) ----------------
        foreach (var (key, label, _, _) in AutopilotButtons) Button(vars, key, label);
        Watched(vars, "A330_SPD_MACH", "INI_AIRSPEED_IS_MACH", "Speed window units", new() { [0] = "Knots", [1] = "Mach" });
        Watched(vars, "A330_TRK_FPA", "INI_TRACK_FPA_STATE", "HDG-V/S or TRK-FPA", new() { [0] = "HDG V/S", [1] = "TRK FPA" });
        // ALT knob step (measured: input 0 reads 1 and the knob steps 100 feet)
        Watched(vars, "A330_ALT_STEP", "INI_ALTITUDE_STATE", "Altitude knob steps", new() { [1] = "100 feet", [0] = "1000 feet" });
        Watched(vars, "A330_METRIC", "INI_FCU_METRIC_STATE", "Metric altitude", new() { [0] = "Off", [1] = "On" });
        Watched(vars, "A330_AP1_ON", "INI_AP1_ON", "Autopilot 1", engaged);
        Watched(vars, "A330_AP2_ON", "INI_AP2_ON", "Autopilot 2", engaged);
        Watched(vars, "A330_ATHR_ON", "INI_AT_ON", "Autothrust", engaged);
        Silent(vars, A330SharedKeys.FcuSpd, "INI_AIRSPEED_DIAL", "FCU speed");
        Silent(vars, A330SharedKeys.FcuHdg, "INI_HEADING_DIAL", "FCU heading");
        Silent(vars, A330SharedKeys.FcuTrk, "INI_TRK_DIAL", "FCU track");
        Silent(vars, A330SharedKeys.FcuAlt, "INI_ALTITUDE_DIAL", "FCU altitude");
        Silent(vars, A330SharedKeys.FcuVs, "INI_VVI_DIAL", "FCU vertical speed");
        Silent(vars, A330SharedKeys.FcuFpa, "INI_FPA_DIAL", "FCU flight path angle");
        Silent(vars, A330SharedKeys.Altitude, "INDICATED ALTITUDE", "Altitude", "feet", SimVarType.SimVar);
        // The base class also reads this name; two Continuous batched keys with one Name shift every later
        // struct slot in the batch ([VAR-7]), so this silent copy takes its own once-a-second subscription
        // instead: the same sample rate, so nothing a pilot hears or sees changes.
        vars[A330SharedKeys.Altitude].ExcludeFromBatch = true;

        OnRequestVar(vars, "A330_BARO_FRESH", "KOHLSMAN SETTING MB:1", "Captain altimeter setting", "millibars", SimVarType.SimVar);
        OnRequestVar(vars, "A330_BARO_STD_FRESH", "KOHLSMAN SETTING STD:1", "Captain altimeter standard", "Bool", SimVarType.SimVar);
        // The cockpit's own baro mode (2/3 = STD). Measured live 2026-10-17 on the
        // A330 at FL220 in STD: this read 3 while KOHLSMAN SETTING STD:1 read 0, so STD comes from here.
        OnRequestVar(vars, "A330_BARO_MODE_FRESH", "XMLVAR_Baro1_Mode", "Captain altimeter mode");
    }

    partial void HandleFcuSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        double? cur = sim.GetCachedVariableValue(varKey);
        var button = AutopilotButtons.FirstOrDefault(b => b.key == varKey);
        if (button.key != null)
        {
            PressAutopilot(sim, announcer, varKey);
            handled = true; return;
        }
        switch (varKey)
        {
            case "A330_SPD_MACH" or "A330_METRIC" or "A330_ALT_STEP" when cur is double same && Math.Abs(same - value) < 0.5:
                handled = true; return;
            case "A330_SPD_MACH":
                SetInputEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", value > 0.5 ? 1 : 0);
                handled = true; return;
            case "A330_METRIC":
                SetInputEvent(sim, announcer, "Metric altitude", "AIRLINER_METRIC", value > 0.5 ? 1 : 0);
                handled = true; return;
            case "A330_ALT_STEP":
                SetInputEvent(sim, announcer, "Altitude knob steps", "AIRLINER_MCU_ALTSELECT", AltStepInput(value > 0.5));
                handled = true; return;
            case "A330_TRK_FPA":
                // the HDG-V/S/TRK-FPA key toggles: press only when the pick differs (unknown counts as differing)
                if ((cur is null || Math.Abs(cur.Value - value) > 0.5) && CanCalc(sim, announcer, "TRK-FPA"))
                    Pulse(sim, "INI_FCU_HDG_VS_COMMAND");
                handled = true; return;
        }
    }

    partial void FormatFcuDisplay(string varKey, double value, ref string? text)
    {
        bool fpa = Fv(A330SharedKeys.MTrkfpa) == 1;
        switch (varKey)
        {
            case A330SharedKeys.FmaAp: text = ApStatus(); return;
            case A330SharedKeys.FcuSpd:
                text = SpeedManaged() ? "Managed" : Fv(A330SharedKeys.MMach) == 1 ? FormattableString.Invariant($"Mach {value:0.00}") : $"{value:0} knots";
                return;
            case A330SharedKeys.FcuHdg:
            {
                double v = fpa ? _sim?.GetCachedVariableValue(A330SharedKeys.FcuTrk) ?? value : value;
                text = $"{(fpa ? "track" : "heading")} {((int)Math.Round(v) % 360 + 360) % 360:000}";
                return;
            }
            case A330SharedKeys.FcuAlt: text = $"{value:0} feet"; return;
            case A330SharedKeys.FcuVs:
                text = fpa ? FormattableString.Invariant($"flight path angle {(_sim?.GetCachedVariableValue(A330SharedKeys.FcuFpa) ?? 0):0.0} degrees") : $"{value:0} feet per minute";
                return;
        }
    }

    partial void HandleFcuHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ReadAltimeter: ReadAltimeter(sim, announcer); handled = true; return;
            // ---- dialogs ----
            case HotkeyAction.FCUSetHeading: ShowHeadingDialog(sim, announcer, parentForm); handled = true; return;
            case HotkeyAction.FCUSetSpeed: ShowSpeedDialog(sim, announcer, parentForm); handled = true; return;
            case HotkeyAction.FCUSetAltitude: ShowAltitudeDialog(sim, announcer, parentForm); handled = true; return;
            case HotkeyAction.FCUSetVS: ShowVsDialog(sim, announcer, parentForm); handled = true; return;
            case HotkeyAction.FCUSetBaro: ShowBaroDialog(sim, announcer, parentForm); handled = true; return;
            case HotkeyAction.FCUSetAutopilot:
                hotkeys.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA330.IniA330AutopilotWindow(AutopilotRows(sim, announcer), ShortName), w => w.ShowForm());
                handled = true; return;

            // ---- knob push / pull keys (the result is announced by the monitor) ----
            // The input-mode engage toggles (AP 1, AP 2, A/THR, LOC, APPR) are not handled, as on the Fenix:
            // those buttons are in the Ctrl+P window and the value windows.
            case HotkeyAction.FCUHeadingPull: PressAutopilot(sim, announcer, "A330_HDG_PULL"); handled = true; return;
            case HotkeyAction.FCUHeadingPush: PressAutopilot(sim, announcer, "A330_HDG_PUSH"); handled = true; return;
            case HotkeyAction.FCUSpeedPull: PressAutopilot(sim, announcer, "A330_SPD_PULL"); handled = true; return;
            case HotkeyAction.FCUSpeedPush: PressAutopilot(sim, announcer, "A330_SPD_PUSH"); handled = true; return;
            case HotkeyAction.FCUAltitudePull: PressAutopilot(sim, announcer, "A330_ALT_PULL"); handled = true; return;
            case HotkeyAction.FCUAltitudePush: PressAutopilot(sim, announcer, "A330_ALT_PUSH"); handled = true; return;
            case HotkeyAction.FCUVSPull: PressAutopilot(sim, announcer, "A330_VS_PULL"); handled = true; return;
            case HotkeyAction.FCUVSPush: PressAutopilot(sim, announcer, "A330_VS_PUSH"); handled = true; return;
        }
    }
}
