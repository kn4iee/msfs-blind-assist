// Plan 04 owns this file: the panels (the generated controls and the definition's own).

using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private readonly List<(string panel, string key)> _mapDisplayRows = new();
    private readonly Dictionary<string, A330ControlMap.Control> _controlsByKey = new();

    internal static readonly (string section, string panel, string key)[] OwnPlacedControls =
    {
        ("Pedestal", "Flaps and Speedbrake", "A330_FLAPS"), ("Pedestal", "Flaps and Speedbrake", "A330_SPDBRK"),
        ("Pedestal", "Flaps and Speedbrake", "A330_SPLR_ARM"), ("Pedestal", "Parking Brake", "A330_PARKBRK"),
        ("Main Instrument Panel", "Landing Gear", "A330_GEAR"), ("Main Instrument Panel", "Brakes", "A330_AUTOBRAKE"),
    };

    internal static readonly (string panel, string key)[] OwnPlacedDisplays =
    {
        ("Flaps and Speedbrake", "A330_SLAT_ANGLE"), ("Flaps and Speedbrake", "A330_FLAP_ANGLE"),
        ("Flaps and Speedbrake", "A330_TRIM"), ("Landing Gear", "A330_GEAR_POS"),
    };

    /// <summary>The A330's flap lever (FS flight model: 4 handle positions after UP; 1+F is automatic).</summary>
    internal static readonly string[] FlapDetents = { "Flaps up", "Flaps 1", "Flaps 2", "Flaps 3", "Flaps full" };

    /// <summary>What INI_AUTOBRAKE_LEVEL reads for each autobrake setting (0 off, 1 LO, 2 MED, 3 MAX).
    /// The panel combo writes the raw value back to <c>SetAutobrake</c>.</summary>
    internal static readonly Dictionary<double, string> AutobrakeLevels = new() { [0] = "Off", [1] = "Low", [2] = "Medium", [3] = "Max" };

    /// <summary>A flap lever reading that is one of its detents (the handle index is whole).</summary>
    internal static bool IsFlapDetent(double v) =>
        !double.IsNaN(v) && Math.Abs(v - Math.Round(v)) < 0.01 && Math.Round(v) >= 0 && Math.Round(v) < FlapDetents.Length;

    /// <summary>A speedbrake lever at one of the positions its combo names (retracted, half, full), never
    /// between them where it is travelling ([VAR-6]).</summary>
    internal static bool IsSpeedbrakeAtRest(double v) =>
        !double.IsNaN(v) && (Math.Abs(v) < 0.02 || Math.Abs(v - 0.5) < 0.02 || Math.Abs(v - 1) < 0.02);

    internal static string? AutobrakeEvent(int level) => level switch
    {
        1 => "AIRLINER_AUTOBRK_LO", 2 => "AIRLINER_AUTOBRK_MED", 3 => "AIRLINER_AUTOBRK_HI", _ => null,
    };

    /// <summary>Measured: pressing a level button (by value) arms it; Off = press the ACTIVE level again.
    /// The level buttons are input events, so no MobiFlight is needed.</summary>
    private static void SetAutobrake(SimConnectManager sim, int level, double? current)
    {
        if (AutobrakeEvent(level) is { } a) { PressByValue(sim, a); return; }
        if (current is double c && AutobrakeEvent((int)Math.Round(c)) is { } b) PressByValue(sim, b);
    }

    /// <summary>The parking brake lever's input for set / released: 0 = set (measured: input 0 with
    /// the brake set, INI status 1).</summary>
    internal static double ParkingBrakeInput(bool set) => set ? 0 : 1;

    /// <summary>Where the parking brake's state is read (A330_PARKBRK: 1 = set): iniBuilds' own
    /// INI_PARKING_BRAKE_STATUS.</summary>
    internal static readonly (string name, string units, SimVarType type) ParkingBrakeRead =
        ("INI_PARKING_BRAKE_STATUS", "number", SimVarType.LVar);

    private int _lastFlapIndex = -1;

    internal static string FormatTrim(double deg) =>
        Math.Abs(deg) < 0.05 ? "0.0" : $"{Math.Abs(deg).ToString("0.0", CultureInfo.InvariantCulture)} {(deg > 0 ? "up" : "down")}";

    partial void AddPanelVariables(Dictionary<string, SimVarDefinition> vars)
    {
        // ---------------- Flight controls (the definition's own) ----------------
        Add(vars, "A330_FLAPS", new SimVarDefinition
        {
            Name = "FLAPS HANDLE INDEX", DisplayName = "Flaps", Type = SimVarType.SimVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = Enumerable.Range(0, FlapDetents.Length).ToDictionary(i => (double)i, i => FlapDetents[i]),
        });
        _silentKeys.Add("A330_FLAPS");   // detent call-outs come from ProcessSimVarUpdate
        Add(vars, "A330_SPDBRK", new SimVarDefinition
        {
            Name = "SPOILERS HANDLE POSITION", DisplayName = "Speedbrake lever", Type = SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [0.5] = "Half", [1] = "Full" },
            ValueToDescriptionKey = v => v < 0.25 ? 0 : v < 0.75 ? 0.5 : 1,
            ExcludeFromMonitorManager = true,   // consumed silently (the panel combo's read-back): no Ctrl+M row [VAR-9]
        });
        _silentKeys.Add("A330_SPDBRK");
        Watched(vars, "A330_SPLR_ARM", "SPOILERS ARMED", "Ground spoilers", new() { [0] = "Disarmed", [1] = "Armed" }, units: "Bool", type: SimVarType.SimVar);
        Watched(vars, "A330_GEAR", "INI_GEAR_HANDLE_STATUS", "Landing gear lever", new() { [0] = "Up", [1] = "Down" });
        Watched(vars, "A330_PARKBRK", ParkingBrakeRead.name, "Parking brake", new() { [0] = "Off", [1] = "Set" },
            units: ParkingBrakeRead.units, type: ParkingBrakeRead.type);
        Watched(vars, "A330_AUTOBRAKE", "INI_AUTOBRAKE_LEVEL", "Autobrake", new(AutobrakeLevels));
        Silent(vars, "A330_FLAP_ANGLE", "TRAILING EDGE FLAPS LEFT ANGLE", "Flap angle", "degrees", SimVarType.SimVar);
        Silent(vars, "A330_SLAT_ANGLE", "LEADING EDGE FLAPS LEFT ANGLE", "Slat angle", "degrees", SimVarType.SimVar);
        Silent(vars, "A330_GEAR_POS", "GEAR CENTER POSITION", "Nose gear", "percent", SimVarType.SimVar);
        Silent(vars, "A330_TRIM", "ELEVATOR TRIM POSITION", "Stabilizer trim", "degrees", SimVarType.SimVar);

        // ---------------- Generated cockpit controls ----------------
        foreach (var c in _map.Controls)
        {
            if (string.IsNullOrEmpty(c.Key) || vars.ContainsKey(c.Key)) continue;
            _controlsByKey[c.Key] = c;
            var read = c.Read is { Length: > 0 } ? A330ControlMap.ParseReadKind(c.Read) : ((A330ControlMap.ReadKind, string, string)?)null;
            var positions = c.Positions?.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Label) ?? new Dictionary<double, string>();
            SimVarType TypeOf(A330ControlMap.ReadKind k) => k switch
            {
                A330ControlMap.ReadKind.SimVar => SimVarType.SimVar,
                A330ControlMap.ReadKind.InputEvent => SimVarType.InputEvent,
                _ => SimVarType.LVar,
            };
            switch (c.Kind)
            {
                case "select" when read is { } r && positions.Count >= 2:
                    Add(vars, c.Key, new SimVarDefinition
                    {
                        Name = r.Item2, DisplayName = c.Label, Type = TypeOf(r.Item1), Units = r.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ValueDescriptions = positions,
                        // a guard is operated on the way to its switch: never narrate it
                        ExcludeFromMonitorManager = c.Label.EndsWith(" guard", StringComparison.Ordinal),
                    });
                    if (c.Label.EndsWith(" guard", StringComparison.Ordinal)) _silentKeys.Add(c.Key);
                    break;
                case "button":
                    Button(vars, c.Key, c.Label);
                    break;
                case "continuous" when read is { } rc:
                    Add(vars, c.Key, new SimVarDefinition
                    {
                        Name = rc.Item2, DisplayName = c.Label, Type = TypeOf(rc.Item1), Units = rc.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromMonitorManager = true,
                        RenderAsSlider = true, SliderMin = c.Min ?? 0, SliderMax = c.Max ?? 100,
                    });
                    _silentKeys.Add(c.Key);
                    break;
                case "status" when read is { } rs:
                    if (_usedNames.Contains(TypeOf(rs.Item1) + ":" + rs.Item2)) break;
                    Add(vars, c.Key, new SimVarDefinition
                    {
                        Name = rs.Item2, DisplayName = c.Label, Type = TypeOf(rs.Item1), Units = rs.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = positions.Count > 0, ValueDescriptions = positions,
                        ExcludeFromMonitorManager = positions.Count == 0,
                    });
                    if (positions.Count == 0) _silentKeys.Add(c.Key);
                    _mapDisplayRows.Add((c.Panel, c.Key));
                    break;
            }
            if (c.Lamps == null) continue;
            int n = 0;
            foreach (var lamp in c.Lamps)
            {
                n++;
                if (string.IsNullOrWhiteSpace(lamp.Read)) continue;
                var lr = A330ControlMap.ParseReadKind(lamp.Read);
                if (_usedNames.Contains(TypeOf(lr.kind) + ":" + lr.name)) continue;
                string lk = $"{c.Key}_LAMP{n}";
                string legend = string.IsNullOrWhiteSpace(lamp.Legend) ? "light" : $"{lamp.Legend} light";
                Add(vars, lk, new SimVarDefinition
                {
                    Name = lr.name, DisplayName = $"{c.Label} {legend}", Type = TypeOf(lr.kind), Units = lr.units,
                    UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Lit" },
                    ValueToDescriptionKey = v => v > 0.5 ? 1 : 0,
                });
                _mapDisplayRows.Add((c.Panel, lk));
            }
        }

        // ProcessSimVarUpdate consumes these (it returns true), which skips MainForm's control refresh, so an
        // open panel kept the position it was built with. Each control below follows a change made elsewhere
        // through RefreshControlWhenDefHandled: its write fires on a user commit alone, so a refresh sends nothing.
        vars["A330_FLAPS"].RefreshControlWhenDefHandled = IsFlapDetent;
        vars["A330_SPDBRK"].RefreshControlWhenDefHandled = IsSpeedbrakeAtRest;
        foreach (var def in vars.Values)
        {
            if (def.RenderAsSlider)
                def.RefreshControlWhenDefHandled = v => !double.IsNaN(v);
            else if (def.DisplayName.EndsWith(" guard", StringComparison.Ordinal) && def.ValueDescriptions.Count > 0)
            {
                var d = def;
                d.RefreshControlWhenDefHandled = v => !double.IsNaN(v) && d.ValueDescriptions.ContainsKey(d.DescriptionKeyFor(v));
            }
        }
    }

    partial void HandlePanelSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        double? cur = sim.GetCachedVariableValue(varKey);
        switch (varKey)
        {
            case "A330_FLAPS":
                if (CanCalc(sim, announcer, "Flaps"))
                    Write(sim, $"{Num(Math.Round(Math.Clamp(Math.Round(value), 0, FlapDetents.Length - 1) * 16383.0 / (FlapDetents.Length - 1)))} (>K:FLAPS_SET)");
                handled = true; return;
            case "A330_SPDBRK":
                if (CanCalc(sim, announcer, "Speedbrake"))
                    Write(sim, $"{Num(Math.Round(Math.Clamp(value, 0, 1) * 16383))} (>K:SPOILERS_SET)");
                handled = true; return;
            case "A330_SPLR_ARM":
                if (CanCalc(sim, announcer, "Ground spoilers"))
                    Write(sim, value > 0.5 ? "(>K:SPOILERS_ARM_ON)" : "(>K:SPOILERS_ARM_OFF)");
                handled = true; return;
            case "A330_GEAR":
                if (CanCalc(sim, announcer, "Gear"))
                    Write(sim, value > 0.5 ? "(>K:GEAR_DOWN)" : "(>K:GEAR_UP)");
                handled = true; return;
            case "A330_PARKBRK":
                SetInputEvent(sim, announcer, "Parking brake", "AIRLINER_PARKINGBRAKE", ParkingBrakeInput(value > 0.5));
                handled = true; return;
            case "A330_AUTOBRAKE":
                SetAutobrake(sim, (int)Math.Round(value), cur);
                handled = true; return;
        }

        if (!_controlsByKey.TryGetValue(varKey, out var c)) return;
        // an "rpn" control writes its own calculator code; every other mode drives an input event
        if (c.Mode != "rpn" && string.IsNullOrEmpty(c.Event) && string.IsNullOrEmpty(c.Rpn)) return;
        switch (c.Mode)
        {
            case "toggle":
                // a momentary push-button that reports its state: press once, only when the pick differs
                // (an unknown state counts as differing, so a cold cache still actuates). The press is the
                // control's own rpn when it has one, else its input event's _Push.
                if ((cur is null || Math.Abs(cur.Value - value) > 0.5) && CanCalc(sim, announcer, c.Label))
                {
                    // {value} = the pick (the engine masters pass it to their input event's Set, right
                    // whether that Set toggles or sets by value); a press-flag rpn carries none
                    if (!string.IsNullOrWhiteSpace(c.Rpn)) Write(sim, c.Rpn.Replace("{value}", Num(value)));
                    else PushEvent(sim, c.Event!, c.HoldMs ?? DefaultPushMs);
                }
                handled = true; return;
            case "push" when string.IsNullOrEmpty(c.Event):
                return;
            case "push":
                if (CanCalc(sim, announcer, c.Label)) PushEvent(sim, c.Event, c.HoldMs ?? DefaultPushMs);
                handled = true; return;
            case "rpn":
                if (!string.IsNullOrWhiteSpace(c.Rpn) && CanCalc(sim, announcer, c.Label))
                    Write(sim, c.Rpn.Replace("{value}", Num(value)));
                handled = true; return;
            default:
                if (string.IsNullOrEmpty(c.Event)) return;
                double input = c.Kind == "continuous" ? value : A330ControlMap.InputFor(c, value) ?? value;
                SetInputEvent(sim, announcer, c.Label, c.Event, input);
                handled = true; return;
        }
    }

    partial void ProcessPanelUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        switch (varName)
        {
            case "A330_FLAPS":
            {
                int idx = (int)Math.Round(value);
                if (_lastFlapIndex < 0 || idx == _lastFlapIndex) { _lastFlapIndex = idx; handled = true; return; }
                _lastFlapIndex = idx;
                if (idx >= 0 && idx < FlapDetents.Length) announcer.Announce(FlapDetents[idx]);
                handled = true; return;
            }
        }
    }

    partial void FormatPanelDisplay(string varKey, double value, ref string? text)
    {
        switch (varKey)
        {
            case "A330_FLAP_ANGLE": case "A330_SLAT_ANGLE": text = $"{value:0} degrees"; return;
            case "A330_GEAR_POS": text = $"{value:0} percent down"; return;
            case "A330_TRIM": text = FormatTrim(value); return;
            case "A330_LDG_ELEV_FT": text = $"{value:0} feet"; return;
        }
    }

    partial void ResetPanelBaselines(bool contextReset)
    {
        _lastFlapIndex = -1;
    }
}
