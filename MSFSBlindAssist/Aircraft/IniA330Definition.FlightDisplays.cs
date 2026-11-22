// Plan 09 owns this file: the flight displays (the FMA, the PFD and the ND).

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    internal static readonly string[] FmaDisplayKeys =
        { "A330_FMA_THRUST", "A330_FMA_VERT", "A330_FMA_LAT", "A330_FMA_ARMED", "A330_FMA_CAP", A330SharedKeys.FmaAp };

    internal const string FmaMonitorKey = "A330_M_PITCH";

    // FMA inputs: silent caches composed in ProcessSimVarUpdate
    internal static readonly (string key, string lvar)[] FmaInputVars =
    {
        ("A330_M_AT", "INI_AT_MODE"), ("A330_M_PITCH", "INI_PITCH_MODE"), ("A330_M_PITCH_ARM", "INI_PITCH_MODE_ARM"),
        ("A330_M_PITCH_ARM2", "INI_PITCH_MODE_ARM2"), ("A330_M_ROLL", "INI_ROLL_MODE"), ("A330_M_ROLL_ARM", "INI_ROLL_MODE_ARM"),
        ("A330_M_APPR", "INI_PITCH_ROLL_MODE"), (A330SharedKeys.MMach, "INI_AIRSPEED_IS_MACH"), (A330SharedKeys.MTrkfpa, "INI_TRACK_FPA_STATE"),
        ("A330_M_TOGA_LOCK", "INI_TOGA_LOCK_ACTIVE"), ("A330_M_THR_LATCH", "INI_FORCE_THRUST_LATCH"),
        ("A330_M_CAP", "INI_AUTOPILOT_CAPABILITY"), (A330SharedKeys.MSpdManaged, "INI_SPEED_MANAGED"),
    };

    private A330FmaText? _fmaAnnounced;
    private System.Windows.Forms.Timer? _fmaTimer;
    private ScreenReaderAnnouncer? _fmaAnnouncer;
    private readonly Dictionary<string, double> _fmaValues = new();

    // Used outside this file too: Fcu (plan 05) and Readouts (plan 10) call Fv and CurrentFma; keep their signatures.
    // After a context reset (_fmaValues cleared) a flight load re-delivers only CHANGED vars, so an
    // unchanged one falls back to the cache rather than reading 0 until it next moves.
    private int Fv(string key) => _fmaValues.TryGetValue(key, out var v) ? (int)Math.Round(v)
        : (int)Math.Round(_sim?.GetCachedVariableValue(key) ?? 0);

    internal A330FmaText CurrentFma()
    {
        double fcuAlt = _sim?.GetCachedVariableValue(A330SharedKeys.FcuAlt) ?? 0;
        double alt = _sim?.GetCachedVariableValue(A330SharedKeys.Altitude) ?? 0;
        return A330Fma.Compose(new A330FmaInputs(
            Fv("A330_M_AT"), Fv("A330_M_PITCH"), Fv("A330_M_PITCH_ARM"), Fv("A330_M_PITCH_ARM2"),
            Fv("A330_M_ROLL"), Fv("A330_M_ROLL_ARM"), Fv("A330_M_APPR"),
            Fv(A330SharedKeys.MMach) != 0, Fv(A330SharedKeys.MTrkfpa) != 0, Fv("A330_M_TOGA_LOCK") != 0 || Fv("A330_M_THR_LATCH") != 0,
            Fv("A330_M_CAP"), fcuAlt > alt));
    }

    /// <summary>Capture modes flip within a frame or two; settle 300 ms so one change is spoken once.</summary>
    private void ScheduleFma(ScreenReaderAnnouncer announcer)
    {
        _fmaAnnouncer = announcer;
        if (_fmaTimer == null)
        {
            _fmaTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _fmaTimer.Tick += (_, _) => { _fmaTimer!.Stop(); AnnounceFmaChange(); };
        }
        _fmaTimer.Stop();
        _fmaTimer.Start();
    }

    private void AnnounceFmaChange()
    {
        var now = CurrentFma();
        var prev = _fmaAnnounced;
        _fmaAnnounced = now;
        Log.Channel("fma").Info($"{ShortName} FMA: {now.Summary()} [at={Fv("A330_M_AT")} pitch={Fv("A330_M_PITCH")}/{Fv("A330_M_PITCH_ARM")}/{Fv("A330_M_PITCH_ARM2")} roll={Fv("A330_M_ROLL")}/{Fv("A330_M_ROLL_ARM")} appr={Fv("A330_M_APPR")}]");
        if (prev == null || _fmaAnnouncer == null) return;   // baseline-first
        var parts = new List<string>();
        if (now.Thrust != prev.Thrust && now.Thrust.Length > 0) parts.Add(now.Thrust);
        if (now.Vertical != prev.Vertical && now.Vertical.Length > 0) parts.Add(now.Vertical);
        if (now.Lateral != prev.Lateral && now.Lateral.Length > 0) parts.Add(now.Lateral);
        if (now.Armed != prev.Armed && now.Armed.Length > 0) parts.Add("armed " + now.Armed);
        if (now.Capability != prev.Capability && now.Capability.Length > 0) parts.Add(now.Capability);
        if (parts.Count > 0 && !DisabledMonitorSet.Contains(FmaMonitorKey)) _fmaAnnouncer.Announce(string.Join(", ", parts));
    }

    /// <summary>Instrument camera view (0-based), found by sweeping all 38 on 2026-10-16: view 6 the
    /// captain's PFD and ND under the FCU.</summary>
    internal const int PfdNdView = 6;

    partial void AddFlightDisplayVariables(Dictionary<string, SimVarDefinition> vars)
    {
        // ---------------- Flight modes ----------------
        foreach (var (key, lvar) in FmaInputVars) Silent(vars, key, lvar, key);
        vars[FmaMonitorKey].ExcludeFromMonitorManager = false;   // one Ctrl+M row mutes every flight-mode change
        vars[FmaMonitorKey].DisplayName = "Flight mode changes";
        Synthetic(vars, "A330_FMA_THRUST", "Thrust mode");
        Synthetic(vars, "A330_FMA_VERT", "Vertical mode");
        Synthetic(vars, "A330_FMA_LAT", "Lateral mode");
        Synthetic(vars, "A330_FMA_ARMED", "Armed modes");
        Synthetic(vars, "A330_FMA_CAP", "Landing capability");
        Synthetic(vars, A330SharedKeys.FmaAp, "Autopilot and autothrust");

        // The Speed window's units and the HDG-V/S or TRK-FPA controls read a second key on these two names.
        // Two Continuous batched keys with one Name shift every later struct slot in the batch ([VAR-7]), so
        // each silent cache copy takes its own once-a-second subscription instead: the same sample rate, so
        // nothing a pilot hears or sees changes.
        vars[A330SharedKeys.MMach].ExcludeFromBatch = true;
        vars[A330SharedKeys.MTrkfpa].ExcludeFromBatch = true;
    }

    partial void ProcessFlightDisplayUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (FmaInputVars.Any(f => f.key == varName))
        {
            _fmaValues[varName] = value;
            ScheduleFma(announcer);
            handled = true;
        }
    }

    partial void FormatFlightDisplay(string varKey, double value, ref string? text)
    {
        var f = CurrentFma();
        switch (varKey)
        {
            case "A330_FMA_THRUST": text = Or(f.Thrust); return;
            case "A330_FMA_VERT": text = Or(f.Vertical); return;
            case "A330_FMA_LAT": text = Or(f.Lateral); return;
            case "A330_FMA_ARMED": text = Or(f.Armed); return;
            case "A330_FMA_CAP": text = Or(f.Capability); return;
        }
        static string Or(string s) => s.Length > 0 ? s : "none";
    }

    partial void HandleFlightDisplayHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ReadApproachCapability:
            {
                string cap = A330Fma.Capability(Fv("A330_M_CAP"));
                announcer.AnnounceImmediate(cap.Length > 0 ? cap : "No landing capability shown");
                handled = true; return;
            }
            // ---- display reads (AI vision on the aircraft's own instrument camera) ----
            case HotkeyAction.ReadDisplayPFD:
                ReadDisplay(Services.GeminiService.DisplayType.PFD, "PFD", announcer, parentForm,
                    new Services.InstrumentViewRequest(sim, PfdNdView));
                handled = true; return;
            case HotkeyAction.ReadDisplayND:
                ReadDisplay(Services.GeminiService.DisplayType.ND, "Navigation Display", announcer, parentForm,
                    new Services.InstrumentViewRequest(sim, PfdNdView));
                handled = true; return;
        }
    }

    partial void ResetFlightDisplayBaselines(bool contextReset)
    {
        _fmaAnnounced = null;
        if (contextReset) _fmaValues.Clear();
    }
}
