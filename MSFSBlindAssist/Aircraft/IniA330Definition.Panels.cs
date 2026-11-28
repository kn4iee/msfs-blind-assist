// Plan 04 owns this file: the panels (the generated controls and the definition's own).

using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private readonly List<(string panel, string key)> _mapDisplayRows = new();
    private readonly Dictionary<string, A330ControlMap.Control> _controlsByKey = new();

    internal static readonly (string section, string panel, string key)[] OwnPlacedControls =
    {
        ("Pedestal", "Flaps and Speedbrake", A330SharedKeys.Flaps), ("Pedestal", "Flaps and Speedbrake", "A330_SPDBRK"),
        ("Pedestal", "Flaps and Speedbrake", "A330_SPLR_ARM"), ("Pedestal", "Parking Brake", "A330_PARKBRK"),
        ("Main Instrument Panel", "Landing Gear", A330SharedKeys.Gear), ("Main Instrument Panel", "Brakes", A330SharedKeys.Autobrake),
    };

    internal static readonly (string panel, string key)[] OwnPlacedDisplays =
    {
        ("Flaps and Speedbrake", A330SharedKeys.SlatAngle), ("Flaps and Speedbrake", A330SharedKeys.FlapAngle),
        ("Flaps and Speedbrake", A330SharedKeys.Trim), ("Landing Gear", "A330_GEAR_POS"),
    };

    /// <summary>The A330's flap lever (FS flight model: 4 handle positions after UP; 1+F is automatic).</summary>
    // Also used by Readouts (plan 10).
    internal static readonly string[] FlapDetents = { "Flaps up", "Flaps 1", "Flaps 2", "Flaps 3", "Flaps full" };

    /// <summary>What INI_AUTOBRAKE_LEVEL reads for each autobrake setting (0 off, 1 LO, 2 MED, 3 MAX).
    /// The panel combo writes the raw value back to <c>SetAutobrake</c>. The words are the cockpit's, which the
    /// A32NX says too (its A32NX_AUTOBRAKES_ARMED_MODE row: LO, MED, MAX); the A330 has no DIS word, so its rest
    /// position stays "Off".</summary>
    internal static readonly Dictionary<double, string> AutobrakeLevels = new() { [0] = "Off", [1] = "LO", [2] = "MED", [3] = "MAX" };

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
    private int _lastSpoilerBand = -1;   // the speedbrake lever's 10 % band last seen; -1 until the first (silent) reading

    /// <summary>How long after a pilot's own lever pick the travel stays quiet if the lever never reaches the picked
    /// band: twice the read-back delay, the same window a key's own-press echo holds ([CORE-9]'s time-only match is
    /// consumed by the first delivery, so the rest of a sweep through several bands would speak).</summary>
    internal const int SpeedbrakePickQuietMs = 2 * ReadBackDelayMs;

    /// <summary>The pilot's own lever pick in flight: its band and when the quiet ends. Null: none.</summary>
    private (int band, long until)? _speedbrakePick;

    private static int BandOf(double v) => (int)Math.Round(Math.Max(0.0, Math.Min(1.0, v)) * 10.0);

    internal static string FormatTrim(double deg) =>
        Math.Abs(deg) < 0.05 ? "0.0" : $"{Math.Abs(deg).ToString("0.0", CultureInfo.InvariantCulture)} {(deg > 0 ? "up" : "down")}";

    partial void AddPanelVariables(Dictionary<string, SimVarDefinition> vars)
    {
        // ---------------- Flight controls (the definition's own) ----------------
        Add(vars, A330SharedKeys.Flaps, new SimVarDefinition
        {
            Name = "FLAPS HANDLE INDEX", DisplayName = "Flaps", Type = SimVarType.SimVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = Enumerable.Range(0, FlapDetents.Length).ToDictionary(i => (double)i, i => FlapDetents[i]),
        });
        _silentKeys.Add(A330SharedKeys.Flaps);   // detent call-outs come from ProcessSimVarUpdate
        Add(vars, "A330_SPDBRK", new SimVarDefinition
        {
            Name = "SPOILERS HANDLE POSITION", DisplayName = "Speedbrake lever", Type = SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [0.5] = "Half", [1] = "Full" },
            ValueToDescriptionKey = v => v < 0.25 ? 0 : v < 0.75 ? 0.5 : 1,
            // said by ProcessPanelUpdate in the A32NX's 10 % bands, so it has its Ctrl+M row (MainForm's mute wrap covers it)
        });
        Watched(vars, "A330_SPLR_ARM", "SPOILERS ARMED", "Ground spoilers", new() { [0] = "Disarmed", [1] = "Armed" }, units: "Bool", type: SimVarType.SimVar);
        Watched(vars, A330SharedKeys.Gear, "INI_GEAR_HANDLE_STATUS", "Landing gear lever", new() { [0] = "Up", [1] = "Down" });
        Watched(vars, "A330_PARKBRK", ParkingBrakeRead.name, "Parking brake", new() { [0] = "Off", [1] = "On" },   // the A32NX's "Parking Brake: On" (A32NX_PARK_BRAKE_LEVER_POS), the Fenix's too
            units: ParkingBrakeRead.units, type: ParkingBrakeRead.type);
        Watched(vars, A330SharedKeys.Autobrake, "INI_AUTOBRAKE_LEVEL", "Autobrake", new(AutobrakeLevels));
        Silent(vars, A330SharedKeys.FlapAngle, "TRAILING EDGE FLAPS LEFT ANGLE", "Flap angle", "degrees", SimVarType.SimVar);
        Silent(vars, A330SharedKeys.SlatAngle, "LEADING EDGE FLAPS LEFT ANGLE", "Slat angle", "degrees", SimVarType.SimVar);
        Silent(vars, "A330_GEAR_POS", "GEAR CENTER POSITION", "Nose gear", "percent", SimVarType.SimVar);
        Silent(vars, A330SharedKeys.Trim, "ELEVATOR TRIM POSITION", "Stabilizer trim", "degrees", SimVarType.SimVar);
        // The base class also reads this name, and two Continuous batched keys with one Name shift every later
        // struct slot in the batch ([VAR-7]), so this silent copy takes its own once-a-second subscription
        // instead: the same sample rate, so nothing a pilot hears or sees changes.
        vars[A330SharedKeys.Trim].ExcludeFromBatch = true;

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
                case "continuous" when read is { } ru && c.Unit is { } && !string.IsNullOrEmpty(c.Event):
                {
                    // offered as a combo of its steps, each in the aircraft's unit (a TrackBar can only say 0..100);
                    // the reading snaps to the nearest step. Quiet like the slider it replaces: a pilot's own knob
                    // turn is said by the combo itself (CORE-7).
                    var unitControl = c;
                    Add(vars, c.Key, new SimVarDefinition
                    {
                        Name = ru.Item2, DisplayName = c.Label, Type = TypeOf(ru.Item1), Units = ru.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromMonitorManager = true,
                        ValueDescriptions = A330Units.Descriptions(c),
                        ValueToDescriptionKey = v => A330Units.NearestStep(unitControl, v),
                    });
                    _silentKeys.Add(c.Key);
                    break;
                }
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
                    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },   // as the Fenix says a light ("<name>: On"), never "Lit"
                    ValueToDescriptionKey = v => v > 0.5 ? 1 : 0,
                });
                _mapDisplayRows.Add((c.Panel, lk));
            }
        }

        // ProcessSimVarUpdate consumes these (it returns true), which skips MainForm's control refresh, so an
        // open panel kept the position it was built with. Each control below follows a change made elsewhere
        // through RefreshControlWhenDefHandled: its write fires on a user commit alone, so a refresh sends nothing.
        vars[A330SharedKeys.Flaps].RefreshControlWhenDefHandled = IsFlapDetent;
        vars["A330_SPDBRK"].RefreshControlWhenDefHandled = IsSpeedbrakeAtRest;
        foreach (var (defKey, def) in vars)
        {
            if (def.RenderAsSlider)
                def.RefreshControlWhenDefHandled = v => !double.IsNaN(v);
            else if (_controlsByKey.TryGetValue(defKey, out var uc) && uc.Unit != null && uc.Kind == "continuous")
                def.RefreshControlWhenDefHandled = v => !double.IsNaN(v);   // the combo follows any reading, to its nearest step
            else if (def.DisplayName.EndsWith(" guard", StringComparison.Ordinal) && def.ValueDescriptions.Count > 0)
            {
                var d = def;
                d.RefreshControlWhenDefHandled = v => !double.IsNaN(v) && d.ValueDescriptions.ContainsKey(d.DescriptionKeyFor(v));
            }
        }
    }

    /// <summary>How long after a pick the control is read back, so the row shows where the aircraft really is.
    /// Until Task 4 measures each control's settle time: the BlueBird 757's read-back delay, which is also
    /// the plan's "lagged" threshold. A measured per-control time replaces it.</summary>
    internal const int ReadBackDelayMs = 1500;

    // The master warning and master caution keys (A330_MASTER_WARN, A330_MASTER_CAUT) read back through the key
    // mechanism, by the variables their `state` names, INI_MASTER_WARNING_ACTIVE and INI_MASTER_CAUTION_ACTIVE.
    // Those two names are UNMEASURED: until the sim sweep reads them, a wrong name reads nothing and the key
    // stays silent (never a guess).

    private const int FreshReadTimeoutMs = 1500;

    /// <summary>MainForm's quiet start: <c>simVarMonitor.EnableAnnouncements</c> waits 5000 ms after a connect
    /// (MainForm.AircraftSwitch.cs, <c>announcementGracePeriodTimer.Interval</c>) and after an aircraft switch
    /// (<c>gracePeriodTimer.Interval</c>), so the settling burst of a load is never spoken. The key state rows are
    /// announced by this definition and never reach that monitor, so they keep the same quiet start themselves.</summary>
    internal const int StartGraceMs = 5000;

    /// <summary>The picks' sim access, replaced by tests; null: the real one.</summary>
    internal Func<SimConnectManager, A330PanelIo>? PanelIo { get; init; }

    private A330PanelIo IoFor(SimConnectManager sim) => PanelIo?.Invoke(sim) ?? new A330PanelIo
    {
        CanCalc = () => sim.CalcWriteCanLand,
        TrySetInput = sim.TrySetInputEvent,
        Press = (inputEvent, holdMs) => PushEvent(sim, inputEvent, holdMs),
        Calc = rpn => Write(sim, rpn),
        Cached = sim.GetCachedVariableValue,
        ReadFresh = (key, ct) => sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct),
        Request = key => sim.RequestVariable(key, forceUpdate: true),
        // awaited on the caller's context (the UI thread), as PushEvent's release is (SIM-20)
        Delay = Task.Delay,
    };

    /// <summary>True while a display window's press is being handled: the window has no combo that follows the
    /// row, so a pick that did not land is said by the definition itself.</summary>
    private bool _windowPress;

    /// <summary>Set by a write the panel refused; read by <see cref="PressFromWindow"/> for the write in progress.</summary>
    private bool _writeRefused;

    private bool Can(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what)
    {
        if (CanCalc(sim, announcer, what)) return true;
        _writeRefused = true;
        return false;
    }

    /// <summary>Sends a plan through the write paths. A refused write (nothing could be sent) says so once,
    /// resyncs the row with a fresh read and answers false; a sent one is read back after the settle time.</summary>
    private bool SendPick(string key, SimVarDefinition varDef, double target, A330WritePlan plan, A330PanelIo io, ScreenReaderAnnouncer announcer)
    {
        if (plan.Outcome == A330WriteOutcome.AtPosition) return true;   // the control already reads it: nothing to send or check
        // a push-button still held by an earlier press: a second press now would be a set of 1 with its event at 1 (a
        // press) and its release a set of 0 with the event already at 0 (a press too), so nothing more is sent and the
        // earlier pick's read-back puts the row where the aircraft is
        if (plan.Calls.Any(call => call is A330InputEventPressCall p && _heldPresses.Contains(p.Event))) return true;
        string? refusal = Dispatch(varDef, plan, io);
        if (refusal != null)
        {
            announcer.AnnounceImmediate(refusal);
            _writeRefused = true;
            io.Request(key);   // the row goes back to where the aircraft is
            return false;
        }
        // a newer pick of this row supersedes this one's read-back (arrowing through a selector commits one per step)
        _pickGeneration.TryGetValue(key, out int generation);
        _pickGeneration[key] = ++generation;
        _ = ReadBackAsync(key, varDef, target, io, announcer, _windowPress, generation);
        return true;
    }

    /// <summary>The input events of the push-buttons pressed by value whose release is not sent yet; UI thread only.</summary>
    private readonly HashSet<string> _heldPresses = new(StringComparer.Ordinal);

    /// <summary>Sends a plan's calls through the write paths; the refusal to say when nothing could be sent (null: sent).</summary>
    private string? Dispatch(SimVarDefinition varDef, A330WritePlan plan, A330PanelIo io)
    {
        if (plan.Outcome == A330WriteOutcome.Refused)
            return plan.Reason is { } why ? $"{varDef.DisplayName} unavailable: {why}." : $"{varDef.DisplayName} unavailable";
        foreach (var call in plan.Calls)
        {
            string? refusal = null;
            if (call is A330InputEventCall ie)
            {
                if (!io.TrySetInput(ie.Event, ie.Value)) refusal = $"{varDef.DisplayName} unavailable: this aircraft does not have that control.";
            }
            else if (call is A330InputEventPressCall vp)
            {
                if (!io.TrySetInput(vp.Event, vp.Press)) refusal = $"{varDef.DisplayName} unavailable: this aircraft does not have that control.";
            }
            else if (!io.CanCalc())
            {
                refusal = $"{varDef.DisplayName} unavailable: the MobiFlight module is not responding.";
            }
            if (refusal != null) return refusal;
            switch (call)
            {
                case A330PressCall p: io.Press(p.Event, p.HoldMs); break;
                case A330CalcCall c: io.Calc(c.Rpn); break;
                case A330InputEventPressCall vp:
                    _heldPresses.Add(vp.Event);
                    _ = ReleaseAfterHoldAsync(vp, io);
                    break;
            }
        }
        return null;
    }

    /// <summary>A press by value's release, after its hold. Always sent, whatever pick comes in meanwhile (a press left at
    /// 1 would make the next set of 1 a second press). Awaited on the caller's context, the UI thread, as
    /// <c>PressByValue</c>'s release is: the input-event hashes and the SimConnect handle belong to it.</summary>
    private async Task ReleaseAfterHoldAsync(A330InputEventPressCall press, A330PanelIo io)
    {
        try
        {
            await io.Delay(press.HoldMs);
            io.TrySetInput(press.Event, press.Release);
        }
        catch (Exception ex)
        {
            Log.Error("A330", $"{press.Event}: the release after the press failed: {ex.Message}");
        }
        finally
        {
            _heldPresses.Remove(press.Event);
        }
    }

    /// <summary>A key's press (a <c>button</c> control). A refused write says so and answers false; a sent one is
    /// confirmed once: a key with a state row reads it after the settle time and says its description, a key with
    /// none says "{label} pressed" at once. Never both. Nothing is resynced (a key has no combo).</summary>
    private bool SendPress(string key, A330ControlMap.Control c, SimVarDefinition varDef, double value, A330PanelIo io, ScreenReaderAnnouncer announcer)
    {
        string? refusal = Dispatch(varDef, A330Write.Press(c, value), io);
        if (refusal != null)
        {
            announcer.AnnounceImmediate(refusal);
            _writeRefused = true;
            return false;
        }
        string? stateKey = StateRowKey(c);
        if (stateKey == null)
        {
            announcer.Announce(A330Write.PressedSentence(varDef.DisplayName));
            return true;
        }
        // an automated press (the First Officer's: announcer suppressed) is not the pilot's: nothing is armed, nothing read back (CORE-7)
        if (announcer.Suppressed) return true;
        // keys that read one row (the ECAM pages) share a generation: the newest press's read-back is the one said
        _pickGeneration.TryGetValue(stateKey, out int generation);
        _pickGeneration[stateKey] = ++generation;
        // the row's own change is this press's echo until the read-back says it (or the window ends): MainForm's
        // generic announce of the row would be a second voice for it
        _pressEchoUntil[stateKey] = NowMs() + 2L * ReadBackDelayMs;
        _ = ConfirmPressAsync(stateKey, io, announcer, generation);
        return true;
    }

    /// <summary>The rows the keys' <c>State</c> variables are read through (built once, from the map).</summary>
    private HashSet<string>? _stateRowKeys;

    private HashSet<string> StateRowKeys => _stateRowKeys ??= _map.Controls
        .Select(StateRowKey).Where(k => k != null).Select(k => k!).ToHashSet(StringComparer.Ordinal);

    /// <summary>Until when (NowMs) a change of a state row is a load's settling and stays unspoken (its baseline still
    /// moves). Started by a reset (a connect: <c>ResetAnnouncementBaselines</c>; a flight load or a lost
    /// connection: <c>OnSimContextReset</c>) and by the first delivery of a definition no reset has reached (an
    /// aircraft switch builds a new definition and resets nothing); MinValue: not started.</summary>
    private long _quietUntil = long.MinValue;

    /// <summary>What the definition last saw of each state row; the first delivery seeds it silently.</summary>
    private readonly Dictionary<string, double> _stateBaseline = new(StringComparer.Ordinal);

    /// <summary>Until when (NowMs) a change of a state row is the pilot's own press's echo, consumed silently.</summary>
    private readonly Dictionary<string, long> _pressEchoUntil = new(StringComparer.Ordinal);

    /// <summary>True while <paramref name="stateKey"/>'s change is a press's echo; an expired window is dropped.</summary>
    private bool IsPressEcho(string stateKey)
    {
        if (!_pressEchoUntil.TryGetValue(stateKey, out long until)) return false;
        if (NowMs() < until) return true;
        _pressEchoUntil.Remove(stateKey);
        return false;
    }

    /// <summary>The row a key's <c>State</c> variable is read through: the row of this definition that reads that
    /// L:var, preferring one that names its values. Null for a key with no state, or whose state no row reads.</summary>
    private string? StateRowKey(A330ControlMap.Control c)
    {
        if (string.IsNullOrWhiteSpace(c.State)) return null;
        string name = A330ControlMap.ParseReadKind(c.State).name;
        string? any = null;
        foreach (var (rowKey, row) in GetVariables())
        {
            if (row.Type != SimVarType.LVar || row.UpdateFrequency != UpdateFrequency.Continuous
                || !string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.ValueDescriptions.Count > 0) return rowKey;
            any ??= rowKey;
        }
        return any;
    }

    /// <summary>Waits the settle time, then says what the state row reads, once, unless a newer press of a key
    /// reading that row came in. A state that cannot be read says nothing (a guessed word would be a fiction).</summary>
    private async Task ConfirmPressAsync(string stateKey, A330PanelIo io, ScreenReaderAnnouncer announcer, int generation)
    {
        try
        {
            await io.Delay(ReadBackDelayMs);
            if (_pickGeneration[stateKey] != generation) return;
            double? read = await io.ReadFresh(stateKey, CancellationToken.None);
            if (_pickGeneration[stateKey] != generation) return;
            _pressEchoUntil.Remove(stateKey);   // this read-back is the row's state now: later changes speak again
            if (read is double v && double.IsFinite(v) && GetVariables().TryGetValue(stateKey, out var row))
            {
                _stateBaseline[stateKey] = v;
                announcer.AnnounceImmediate(A330Write.StateSentence(row, v));
            }
            else
                Log.Debug("A330", $"{stateKey}: the state was not readable after the press");
        }
        catch (Exception ex)
        {
            Log.Error("A330", $"{stateKey}: press read-back failed: {ex.Message}");
        }
    }

    /// <summary>The newest pick per row; UI thread only, like every pick.</summary>
    private readonly Dictionary<string, int> _pickGeneration = new(StringComparer.Ordinal);

    /// <summary>Waits the settle time, then resyncs the row with one fresh read. The row's combo follows that
    /// delivery, which the screen reader reads once and which is silent when the pick landed (WinForms ignores
    /// a same-index set); a window has no combo, so its press says the real position once when it did not land.</summary>
    private async Task ReadBackAsync(string key, SimVarDefinition varDef, double target, A330PanelIo io, ScreenReaderAnnouncer announcer, bool fromWindow, int generation)
    {
        try
        {
            await io.Delay(ReadBackDelayMs);
            if (_pickGeneration[key] != generation) return;   // a newer pick is settling: it resyncs the row
            double? now = fromWindow ? await io.ReadFresh(key, CancellationToken.None) : null;
            if (_pickGeneration[key] != generation) return;   // a pick came in while the fresh read was out
            io.Request(key);
            if (now is double read && double.IsFinite(read) && Math.Abs(varDef.DescriptionKeyFor(read) - target) > 0.01)   // a reading snaps to its step first
                announcer.Announce(A330Write.PositionSentence(varDef, read));
        }
        catch (Exception ex)
        {
            Log.Error("A330", $"{key}: read-back failed: {ex.Message}");
        }
    }

    partial void HandlePanelSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        _writeRefused = false;
        var io = IoFor(sim);
        double? cur = io.Cached(varKey);
        switch (varKey)
        {
            case A330SharedKeys.Flaps:
                if (Can(sim, announcer, "Flaps"))
                    Write(sim, $"{Num(Math.Round(Math.Clamp(Math.Round(value), 0, FlapDetents.Length - 1) * 16383.0 / (FlapDetents.Length - 1)))} (>K:FLAPS_SET)");
                handled = true; return;
            case "A330_SPDBRK":
                if (!io.CanCalc())
                {
                    announcer.AnnounceImmediate("Speedbrake unavailable: the MobiFlight module is not responding.");
                    _writeRefused = true;
                }
                else
                {
                    io.Calc($"{Num(Math.Round(Math.Clamp(value, 0, 1) * 16383))} (>K:SPOILERS_SET)");
                    // the pilot's own pick (not a First Officer write, which is Suppressed) was said by the screen reader:
                    // the lever's travel to it is not spoken band by band
                    if (!announcer.Suppressed) _speedbrakePick = (BandOf(value), NowMs() + SpeedbrakePickQuietMs);
                    else _speedbrakePick = null;
                }
                handled = true; return;
            case "A330_SPLR_ARM":
                if (Can(sim, announcer, "Ground spoilers"))
                    Write(sim, value > 0.5 ? "(>K:SPOILERS_ARM_ON)" : "(>K:SPOILERS_ARM_OFF)");
                handled = true; return;
            case A330SharedKeys.Gear:
                if (Can(sim, announcer, "Gear"))
                    Write(sim, value > 0.5 ? "(>K:GEAR_DOWN)" : "(>K:GEAR_UP)");
                handled = true; return;
            case "A330_PARKBRK":
                SendPick(varKey, varDef, value > 0.5 ? 1 : 0, A330Write.ParkingBrake(cur, value > 0.5), io, announcer);
                handled = true; return;
            case A330SharedKeys.Autobrake:
                SetAutobrake(sim, (int)Math.Round(value), cur);
                handled = true; return;
        }

        if (!_controlsByKey.TryGetValue(varKey, out var c)) return;
        // an "rpn" control writes its own calculator code; every other mode drives an input event
        if (c.Mode != "rpn" && string.IsNullOrEmpty(c.Event) && string.IsNullOrEmpty(c.Rpn)) return;
        switch (c.Mode)
        {
            case "toggle":
                SendPick(varKey, varDef, value, A330Write.ToPosition(c, cur, value), io, announcer);
                handled = true; return;
            case "push" when string.IsNullOrEmpty(c.Event):
                return;
            case "push" when c.Kind == "button":
            case "rpn" when c.Kind == "button":
                SendPress(varKey, c, varDef, value, io, announcer);
                handled = true; return;
            case "push":
                if (Can(sim, announcer, c.Label)) PushEvent(sim, c.Event, c.HoldMs ?? DefaultPushMs);
                handled = true; return;
            case "rpn":
                if (!string.IsNullOrWhiteSpace(c.Rpn) && Can(sim, announcer, c.Label))
                    Write(sim, c.Rpn.Replace("{value}", Num(value)));
                handled = true; return;
            default:
                if (string.IsNullOrEmpty(c.Event)) return;
                if (c.Kind == "continuous" && c.Unit == null)
                {
                    // a slider writes its own value; its read-back is sub-step 4's
                    if (!SetInputEvent(sim, announcer, c.Label, c.Event, value)) _writeRefused = true;
                }
                else SendPick(varKey, varDef, value, A330Write.ToPosition(c, cur, value), io, announcer);
                handled = true; return;
        }
    }

    partial void ProcessPanelUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        // a row a key's state names: the definition announces it itself, with a baseline of its own that every
        // delivery moves (an echo included), so a change back after a press is still a change; MainForm's generic
        // gate is never reached, so it is never a second voice (the row's display followed in MainForm already)
        if (StateRowKeys.Contains(varName))
        {
            bool echo = IsPressEcho(varName);
            bool seen = _stateBaseline.TryGetValue(varName, out double old);
            _stateBaseline[varName] = value;
            handled = true;
            // a definition that was never reset (a fresh one after an aircraft switch) starts its quiet time here
            if (_quietUntil == long.MinValue) _quietUntil = NowMs() + StartGraceMs;
            if (echo || !seen || Math.Abs(old - value) <= 0.001 || NowMs() < _quietUntil) return;
            if (!GetVariables().TryGetValue(varName, out var row) || !row.IsAnnounced || announcer.Suppressed) return;
            // the generic gate's sentence and call (MainForm.Announcers.cs, the description it builds, then Announce)
            announcer.Announce(row.ValueDescriptions.TryGetValue(row.DescriptionKeyFor(value), out var described)
                ? $"{row.DisplayName}: {described}"
                : FormattableString.Invariant($"{row.DisplayName}: {value}"));
            return;
        }
        switch (varName)
        {
            case "A330_SPDBRK":
            {
                // as the A32NX says its handle (FlyByWireA320Definition.ProcessSimVarUpdate, A32NX_SPOILERS_HANDLE_POSITION):
                // 10 % bands, "Spoilers retracted" / "Spoilers 30 percent" / "Spoilers full", the first reading silent.
                // A muted row (Suppressed) says nothing but still moves the band.
                int band = BandOf(value);
                bool first = _lastSpoilerBand < 0;
                int before = _lastSpoilerBand;
                _lastSpoilerBand = band;
                handled = true;
                bool ownPick = false;
                if (_speedbrakePick is { } pick)
                {
                    // the pilot's own pick is travelling: band changes are silent until the lever reaches the picked
                    // band (the screen reader said the pick) or the quiet runs out
                    if (NowMs() >= pick.until) _speedbrakePick = null;
                    else
                    {
                        ownPick = true;
                        if (band == pick.band) _speedbrakePick = null;
                    }
                }
                if (first || band == before || ownPick || announcer.Suppressed) return;
                announcer.Announce(band == 0 ? "Spoilers retracted"
                    : band == 10 ? "Spoilers full"
                    : $"Spoilers {band * 10} percent");
                return;
            }
            case A330SharedKeys.Flaps:
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
            case A330SharedKeys.FlapAngle: case A330SharedKeys.SlatAngle: text = $"{value:0} degrees"; return;
            case "A330_GEAR_POS": text = $"{value:0} percent down"; return;
            case A330SharedKeys.Trim: text = FormatTrim(value); return;
            case "A330_LDG_ELEV_FT": text = $"{value:0} feet"; return;
        }
    }

    partial void ResetPanelBaselines(bool contextReset)
    {
        _lastFlapIndex = -1;
        _lastSpoilerBand = -1;
        _speedbrakePick = null;
        _pressEchoUntil.Clear();
        _stateBaseline.Clear();
        _quietUntil = NowMs() + StartGraceMs;
    }
}
