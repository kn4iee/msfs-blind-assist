// Plan 10 owns this file: the read-outs and the take-off call-outs.

using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;
using MSFSBlindAssist.Aircraft.A330;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>The fuel tanks the module publishes in kilograms (centre comes from the sim, see FuelSummary).</summary>
    internal static readonly (string key, string lvar, string name)[] FuelVars =
    {
        ("A330_FUEL_LO", "INI_FUEL_WEIGHT_LEFT_OUTER", "left outer"), ("A330_FUEL_LI", "INI_FUEL_WEIGHT_LEFT_INNER", "left inner"),
        ("A330_FUEL_RI", "INI_FUEL_WEIGHT_RIGHT_INNER", "right inner"), ("A330_FUEL_RO", "INI_FUEL_WEIGHT_RIGHT_OUTER", "right outer"),
        ("A330_FUEL_TRIM", "INI_FUEL_WEIGHT_TRIM", "trim tank"),
    };

    internal const string TakeoffIasKey = "A330_TO_IAS", TakeoffV1Key = "A330_TO_V1", TakeoffVrKey = "A330_TO_VR", TakeoffV2Key = "A330_TO_V2";

    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();
    private bool _calloutOnGround = true;

    /// <summary>The feed, the speeds and their Ctrl+M rule ([TKO-5]): each call is muted by its speed's row.</summary>
    internal static readonly TakeoffCalloutKeys TakeoffKeys = new(TakeoffIasKey, TakeoffV1Key, TakeoffVrKey, TakeoffV2Key);

    /// <inheritdoc />
    public override string? TakeoffCalloutFeedKey => TakeoffIasKey;
    /// <inheritdoc />
    public override bool TakeoffCalloutFeedNeeded => _takeoffCallouts.NeedsSamples(_calloutOnGround);

    /// <summary>The airspeed feed speaks V1, Rotate and V2, whose Ctrl+M rows are the V-speeds', so it is
    /// kept out of MainForm's mute wrap and checks each call's row itself.</summary>
    partial void IsReadoutMuteWrapExempt(string varKey, ref bool exempt)
    {
        if (varKey == TakeoffIasKey) exempt = true;
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

    partial void AddReadoutVariables(Dictionary<string, SimVarDefinition> vars)
    {
        // ---------------- Fuel caches ----------------
        foreach (var (key, lvar, name) in FuelVars) Silent(vars, key, lvar, name);
        Silent(vars, "A330_FUEL_CENTER", "FUEL TANK CENTER QUANTITY", "Centre tank", "gallons", SimVarType.SimVar);
        Silent(vars, "A330_FUEL_TOTAL_LB", "FUEL TOTAL QUANTITY WEIGHT", "Total fuel", "pounds", SimVarType.SimVar);
        Silent(vars, "A330_FUEL_WPG", "FUEL WEIGHT PER GALLON", "Fuel weight per gallon", "pounds", SimVarType.SimVar);
        Silent(vars, "A330_IS_METRIC", "INI_IS_METRIC", "Weight units");

        // ---------------- Read-outs (fresh on the key press) ----------------
        foreach (var (key, lvar, display) in new[]
                 {
                     ("A330_DIST_DEST", "INI_FMC_DIST_TO_DEST", "Distance to destination"),
                     ("A330_APPR_DIST", "INI_TOTAL_APPR_DIST", "Distance along the route to the destination"),
                     ("A330_TD_DIST_TOTAL", "INI_TD_DIST_TOTAL", "Distance to top of descent"),
                     ("A330_GREEN_DOT", "INI_GREENDOT_SPEED", "Green dot speed"), ("A330_S_SPEED", "INI_S_SPEED", "S speed"),
                     ("A330_F_SPEED", "INI_F_SPEED", "F speed"), ("A330_VLS", "INI_VLS_SPEED", "VLS"),
                     ("A330_VMAX", "INI_MAX_SPEED", "Maximum speed"), ("A330_VAPP", "INI_VAPP_SPEED", "Approach speed"),
                     ("A330_MANAGED_SPEED", "INI_MANAGED_SPEED", "Managed speed target"),
                     ("A330_MINIMUMS", "INI_MINIMUMS_PILOT", "Captain minimums"), ("A330_MDA", "INI_FMS_MDA", "FMS MDA"),
                     ("A330_ILS_FREQ", "INI_ILS_FREQUENCY", "ILS frequency"),
                 })
            OnRequestVar(vars, key, lvar, display);
        OnRequestVar(vars, "A330_GS_FRESH", "GROUND VELOCITY", "Ground speed", "knots", SimVarType.SimVar);
        foreach (var (key, simvar, units) in new[]
                 {
                     ("A330_NAV1_FREQ", "NAV ACTIVE FREQUENCY:1", "MHz"), ("A330_NAV2_FREQ", "NAV ACTIVE FREQUENCY:2", "MHz"),
                     ("A330_NAV3_FREQ", "NAV ACTIVE FREQUENCY:3", "MHz"), ("A330_NAV3_LOC", "NAV HAS LOCALIZER:3", "Bool"),
                     ("A330_NAV3_GS", "NAV HAS GLIDE SLOPE:3", "Bool"), ("A330_NAV3_DME", "NAV DME:3", "nautical miles"),
                     ("A330_COM1_ACT", "COM ACTIVE FREQUENCY:1", "MHz"), ("A330_COM1_STBY", "COM STANDBY FREQUENCY:1", "MHz"),
                 })
            OnRequestVar(vars, key, simvar, key, units, SimVarType.SimVar);

        // ---------------- Take-off call-outs ----------------
        Watched(vars, TakeoffV1Key, "INI_V1", "V1 call-out");
        Watched(vars, TakeoffVrKey, "INI_VR", "Rotate call-out");
        Watched(vars, TakeoffV2Key, "INI_V2", "V2 call-out");
        Add(vars, TakeoffIasKey, new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED", DisplayName = "Take-off call-out airspeed", Type = SimVarType.SimVar, Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ExcludeFromBatch = true, HighFrequency = true, ExcludeFromMonitorManager = true,
        });
    }

    partial void ProcessReadoutUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        switch (varName)
        {
            case TakeoffIasKey:
                var crossed = _takeoffCallouts.ProcessSample(value, _calloutOnGround);
                if (crossed.Count > 0 && !announcer.Suppressed
                    && TakeoffVSpeedCallouts.Compose(crossed, c => TakeoffKeys.IsMuted(c, DisabledMonitorSet)) is string sentence)
                    announcer.AnnounceImmediate(sentence);
                handled = true; return;
            case "SIM_ON_GROUND":
                _calloutOnGround = value >= 0.5;
                break;
            case TakeoffV1Key: _takeoffCallouts.SetV1(value); handled = true; return;
            case TakeoffVrKey: _takeoffCallouts.SetVR(value); handled = true; return;
            case TakeoffV2Key: _takeoffCallouts.SetV2(value); handled = true; return;
        }
    }

    partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        double Cur(string key) => sim.GetCachedVariableValue(key) ?? 0;
        var fma = CurrentFma();
        string Suffix(string mode) => mode.Length > 0 ? $", {mode}" : "";

        switch (action)
        {
            case HotkeyAction.ReadHeading:
            {
                bool trk = Fv(A330SharedKeys.MTrkfpa) == 1;
                int v = ((int)Math.Round(Cur(trk ? A330SharedKeys.FcuTrk : A330SharedKeys.FcuHdg)) % 360 + 360) % 360;
                announcer.AnnounceImmediate($"FCU {(trk ? "track" : "heading")} {v:000}{Suffix(fma.Lateral)}");
                handled = true; return;
            }
            case HotkeyAction.ReadSpeed:
                if (SpeedManaged())
                {
                    double m = Cur("A330_MANAGED_SPEED");
                    announcer.AnnounceImmediate($"Speed managed{(m > 0 ? $", target {m:0} knots" : "")}{Suffix(fma.Thrust)}");
                }
                else if (Fv(A330SharedKeys.MMach) == 1)
                    announcer.AnnounceImmediate(FormattableString.Invariant($"FCU Mach {Cur(A330SharedKeys.FcuSpd):0.00}{Suffix(fma.Thrust)}"));
                else
                    announcer.AnnounceImmediate($"FCU speed {Cur(A330SharedKeys.FcuSpd):0} knots{Suffix(fma.Thrust)}");
                handled = true; return;
            case HotkeyAction.ReadAltitude:
                announcer.AnnounceImmediate($"FCU altitude {Cur(A330SharedKeys.FcuAlt):0} feet{Suffix(fma.Vertical)}");
                handled = true; return;
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                bool fpa = Fv(A330SharedKeys.MTrkfpa) == 1;
                string window = fpa ? FormattableString.Invariant($"flight path angle {Cur(A330SharedKeys.FcuFpa):0.0} degrees") : $"vertical speed {Cur(A330SharedKeys.FcuVs):0} feet per minute";
                announcer.AnnounceImmediate($"FCU {window}{Suffix(fma.Vertical)}");
                handled = true; return;
            }
            case HotkeyAction.ReadFlaps:
            {
                int idx = (int)Math.Round(Cur(A330SharedKeys.Flaps));
                string lever = idx >= 0 && idx < FlapDetents.Length ? FlapDetents[idx] : $"Flap lever {idx}";
                announcer.AnnounceImmediate($"{lever}. Slats {Cur(A330SharedKeys.SlatAngle):0}, flaps {Cur(A330SharedKeys.FlapAngle):0} degrees");
                handled = true; return;
            }
            case HotkeyAction.ReadGear:
                announcer.AnnounceImmediate(Cur(A330SharedKeys.Gear) > 0.5 ? "Gear lever down" : "Gear lever up");
                handled = true; return;
            case HotkeyAction.ReadFuelQuantity:
                announcer.AnnounceImmediate(FuelSummary(sim));
                handled = true; return;
            case HotkeyAction.ReadNavRadioInfo: ReadRadios(sim, announcer); handled = true; return;
            case HotkeyAction.ReadDistanceToDest: ReadFmsDistance(sim, announcer, toTod: false); handled = true; return;
            case HotkeyAction.ReadDistanceToTOD: ReadFmsDistance(sim, announcer, toTod: true); handled = true; return;
            case HotkeyAction.ReadSpeedGD: ReadSpeed(sim, announcer, "A330_GREEN_DOT", "Green dot"); handled = true; return;
            case HotkeyAction.ReadSpeedS: ReadSpeed(sim, announcer, "A330_S_SPEED", "S speed"); handled = true; return;
            case HotkeyAction.ReadSpeedF: ReadSpeed(sim, announcer, "A330_F_SPEED", "F speed"); handled = true; return;
            case HotkeyAction.ReadSpeedVLS: ReadSpeed(sim, announcer, "A330_VLS", "VLS"); handled = true; return;
            case HotkeyAction.ReadSpeedVFE: ReadSpeed(sim, announcer, "A330_VMAX", "Maximum speed"); handled = true; return;
        }
    }

    partial void ResetReadoutBaselines(bool contextReset)
    {
        _takeoffCallouts.Reset();
    }
}
