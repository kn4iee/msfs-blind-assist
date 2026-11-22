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
        // as the Fenix's window: 0-360, a fraction rounds to the nearest degree
        if (!NumberParsing.TryParseTyped(input, out double typed) || typed < 0 || typed > 360) return null;
        int h = (int)Math.Round(typed);   // plain Math.Round, as the Fenix's window (270.5 -> 270)
        bool trk = isTrk();
        return new(trk ? "INI_TRK_DIAL" : "INI_HEADING_DIAL", trk ? A330SharedKeys.FcuTrk : A330SharedKeys.FcuHdg, h % 360, 0.6, trk);
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

    // ---- The four FCU value windows: the Fenix A320's windows in the Fenix's words, built from a spec
    // ---- (A330FcuWindowSpec) the window class and the tests both read. Buttons carry no state text and
    // ---- say nothing after a press; the aircraft's own call-outs speak the result.

    private A330FcuButton Press(SimConnectManager sim, ScreenReaderAnnouncer announcer, string label, string description, string key)
        => new(label, description, () => PressAutopilot(sim, announcer, key));

    /// <summary>Heading or track: the label and messages follow the mode the window opened in (openedTrk);
    /// the check and the dial written follow the mode at commit time (modeNow), as the HDG-V/S / TRK-FPA
    /// key in the window changes it.</summary>
    internal A330FcuWindowSpec HeadingWindowSpec(SimConnectManager sim, ScreenReaderAnnouncer announcer, bool openedTrk, Func<bool>? modeNow = null)
    {
        Func<bool> isTrk = modeNow ?? (() => Fv(A330SharedKeys.MTrkfpa) == 1);
        string word = openedTrk ? "Track" : "Heading";
        return new(
            "Set FCU heading",
            $"{word} (0-360):",
            $"{word} Label",
            $"{word} value",
            $"Enter {word.ToLowerInvariant()} value between 0 and 360 degrees",
            new[]
            {
                Press(sim, announcer, "Heading Push", "Push FCU heading knob", "A330_HDG_PUSH"),
                Press(sim, announcer, "Heading Pull", "Pull FCU heading knob", "A330_HDG_PULL"),
                new A330FcuButton("HDG/VS TRK/FPA", "Toggle HDG/VS and TRK/FPA mode", () =>
                {
                    if (CanCalc(sim, announcer, "TRK-FPA")) Pulse(sim, "INI_FCU_HDG_VS_COMMAND");
                }),
            },
            typed =>
            {
                string now = isTrk() ? "track" : "heading";
                return A330FcuWindowSpec.Check(typed, "a", now,
                    v => v < 0 || v > 360 ? $"{char.ToUpperInvariant(now[0])}{now[1..]} must be between 0 and 360 degrees" : null,
                    _ => () =>
                    {
                        // HDG or TRK as the aircraft is NOW
                        if (HeadingSet(typed, isTrk) is not { } set) return;
                        string w = set.Alt ? "track" : "heading";
                        SetAndConfirm(sim, announcer, set.Lvar, set.CacheKey, set.Target, v => $"{w} {((int)Math.Round(v) % 360 + 360) % 360:000}", set.Tolerance);
                    });
            });
    }

    internal A330FcuWindowSpec SpeedWindowSpec(SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => new(
            "Set FCU Speed",
            "Speed (100-399 / 0.10-0.89):",
            "Speed Label",
            "Speed value",
            "Enter speed value: 100-399 knots or 0.10-0.89 Mach",
            new[]
            {
                Press(sim, announcer, "Speed Push", "Push FCU speed knob", "A330_SPD_PUSH"),
                Press(sim, announcer, "Speed Pull", "Pull FCU speed knob", "A330_SPD_PULL"),
                new A330FcuButton("SPD/MACH", "Toggle speed and mach mode",
                    () => SetInputEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", Fv(A330SharedKeys.MMach) == 1 ? 0 : 1)),
            },
            typed => A330FcuWindowSpec.Check(typed, "a", "speed",
                v => (v >= 100 && v <= 399) || (v >= 0.10 && v <= 0.89) ? null : "Speed must be 100-399 knots or 0.10-0.89 Mach",
                v => () => SetSpeed(sim, announcer, v)));

    private void SetSpeed(SimConnectManager sim, ScreenReaderAnnouncer announcer, double v)
    {
        if (SpeedManaged()) announcer.AnnounceImmediate("Speed is managed; pull the speed knob to fly this speed.");
        bool wantMach = v < 1, isMach = Fv(A330SharedKeys.MMach) == 1;
        void Set() => SetAndConfirm(sim, announcer, "INI_AIRSPEED_DIAL", A330SharedKeys.FcuSpd, wantMach ? Math.Round(v, 2) : Math.Round(v),
            s => wantMach ? FormattableString.Invariant($"Mach {s:0.00}") : $"speed {s:0} knots", wantMach ? 0.006 : 0.6);
        if (wantMach != isMach)
        {
            // one window holds both (measured: SPD/MACH turned 100 into 0.151) — swap units first
            SetInputEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", wantMach ? 1 : 0);
            // awaited on the window's (UI) context: SetAndConfirm captures that context to speak its
            // verdict, and from a pool thread it captured none and said nothing
            _ = SetAfterSwap();
            async Task SetAfterSwap()
            {
                await Task.Delay(600);
                Set();
            }
        }
        else Set();
    }

    internal A330FcuWindowSpec AltitudeWindowSpec(SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => new(
            "Set FCU altitude",
            "Altitude (100-49000):",
            "Altitude Label",
            "Altitude value",
            "Enter altitude value between 100 and 49000 feet",
            new[]
            {
                Press(sim, announcer, "Altitude Push", "Push FCU altitude knob", "A330_ALT_PUSH"),
                Press(sim, announcer, "Altitude Pull", "Pull FCU altitude knob", "A330_ALT_PULL"),
                // the A330's one extra (the A320 has no metric altitude key): no state text; the call-out says the result
                new A330FcuButton("Metric altitude", "Toggle metric altitude",
                    () => SetInputEvent(sim, announcer, "Metric altitude", "AIRLINER_METRIC", On(sim, "A330_METRIC") ? 0 : 1)),
            },
            typed => A330FcuWindowSpec.Check(typed, "an", "altitude",
                v => v < 100 || v > 49000 ? "Altitude must be between 100 and 49000 feet" : null,
                v => () => SetAndConfirm(sim, announcer, "INI_ALTITUDE_DIAL", A330SharedKeys.FcuAlt, Math.Round(v / 100.0) * 100, x => $"altitude {x:0} feet")));

    /// <summary>V/S or FPA: the label follows the mode the window opened in (openedFpa); the check, the
    /// messages and the dial written follow the mode at commit time (modeNow).</summary>
    internal A330FcuWindowSpec VsWindowSpec(SimConnectManager sim, ScreenReaderAnnouncer announcer, bool openedFpa, Func<bool>? modeNow = null)
    {
        Func<bool> isFpa = modeNow ?? (() => Fv(A330SharedKeys.MTrkfpa) == 1);
        return new(
            "Set FCU vertical speed",
            openedFpa ? "FPA (-9.9 to 9.9):" : "V/S (-6000 to 6000):",
            openedFpa ? "FPA Label" : "Vertical Speed Label",
            openedFpa ? "FPA value" : "Vertical speed value",
            openedFpa ? "Enter flight path angle value between -9.9 and 9.9 degrees" : "Enter vertical speed value between -6000 and 6000 feet per minute",
            new[]
            {
                Press(sim, announcer, "V/S Push", "Push FCU vertical speed knob", "A330_VS_PUSH"),
                Press(sim, announcer, "V/S Pull", "Pull FCU vertical speed knob", "A330_VS_PULL"),
            },
            typed =>
            {
                bool fpa = isFpa();
                return A330FcuWindowSpec.Check(typed, "a", fpa ? "flight path angle" : "vertical speed",
                    v => fpa ? (Math.Abs(v) > 9.9 ? "Flight path angle must be between -9.9 and 9.9 degrees" : null)
                             : (Math.Abs(v) > 6000 ? "Vertical speed must be between -6000 and 6000 feet per minute" : null),
                    _ => () =>
                    {
                        if (VsSet(typed, isFpa) is not { } set) return;
                        if (set.Alt) SetAndConfirm(sim, announcer, set.Lvar, set.CacheKey, set.Target, x => FormattableString.Invariant($"flight path angle {x:0.0} degrees"), set.Tolerance);
                        else SetAndConfirm(sim, announcer, set.Lvar, set.CacheKey, set.Target, x => $"vertical speed {x:0} feet per minute", set.Tolerance);
                    });
            });
    }

    private void ShowFcuWindow<T>(Func<T> factory, Action? exitInput) where T : Forms.IniA330.IniA330FcuValueWindow
    {
        exitInput?.Invoke();
        ShowTrackedWindow(factory, w => w.ShowForm());
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
            // the four value windows are the Fenix's windows (see HeadingWindowSpec and its siblings)
            case HotkeyAction.FCUSetHeading:
                ShowFcuWindow(() => new Forms.IniA330.IniA330FcuHeadingWindow(HeadingWindowSpec(sim, announcer, Fv(A330SharedKeys.MTrkfpa) == 1), announcer), hotkeys.ExitInputHotkeyMode);
                handled = true; return;
            case HotkeyAction.FCUSetSpeed:
                ShowFcuWindow(() => new Forms.IniA330.IniA330FcuSpeedWindow(SpeedWindowSpec(sim, announcer), announcer), hotkeys.ExitInputHotkeyMode);
                handled = true; return;
            case HotkeyAction.FCUSetAltitude:
                ShowFcuWindow(() => new Forms.IniA330.IniA330FcuAltitudeWindow(AltitudeWindowSpec(sim, announcer), announcer), hotkeys.ExitInputHotkeyMode);
                handled = true; return;
            case HotkeyAction.FCUSetVS:
                ShowFcuWindow(() => new Forms.IniA330.IniA330FcuVsWindow(VsWindowSpec(sim, announcer, Fv(A330SharedKeys.MTrkfpa) == 1), announcer), hotkeys.ExitInputHotkeyMode);
                handled = true; return;
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
