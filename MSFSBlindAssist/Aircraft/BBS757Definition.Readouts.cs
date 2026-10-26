using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;
using R = MSFSBlindAssist.Aircraft.BBS757.Bbs757Readouts;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's flight-control readouts, its MCP lights, its EXEC light, its engine rows and its announced
/// lights and states (<see cref="Bbs757Annunciators"/>, plan 06; EXT PWR reads back on or off): what is
/// registered, where it shows, what is spoken on change, the trim in BlueBird's units, and the
/// output-mode readouts (L flaps, Shift+G gear, B the altimeters and the radio minimums, F and Shift+F
/// fuel, W and Shift+W gross weight).
///
/// Speech follows CORE-7. A change the pilot did not make (flaps, gear, speedbrake, parking brake,
/// an MCP light, the EXEC light) is spoken once, through <see cref="Bbs757ChangeTracker"/>: the
/// cockpit as found is a silent baseline, an unchanged value is silent, and a change the app's own
/// press or pick made is its echo (the press reads it back itself, the screen reader read the pick).
/// A readout is spoken at once (AnnounceImmediate), never refused silently.
///
/// The data-definition budget: the announced rows ride the 1 Hz batch (no individual definition), the
/// flap angle, the squawk and the ten engine rows are read while their display is shown, and the
/// altimeters, the decision height, the gear lever and the gross weight are read on request. The
/// whole profile stays under 900 variables (Bbs757DefinitionTests).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>How long a change of a state row after the app's own write to its control is that write's echo.</summary>
    internal const int OwnWriteEchoMs = 5000;

    /// <summary>The state call-outs' change tracker (baseline, echo, change).</summary>
    private readonly Bbs757ChangeTracker _changes = new();

    /// <summary>The gear legs as last delivered, for the composed gear call-out and status row.</summary>
    private readonly Dictionary<string, double> _gearLegs = new(StringComparer.Ordinal);

    /// <summary>The clock the echo windows and the speed sets' spacing (BBS757Definition.Mcp.cs) run on; replaced by tests.</summary>
    internal Func<long> NowMs { get; init; } = () => Environment.TickCount64;

    /// <summary>
    /// Whether the pilot muted a Ctrl+M row; replaced by tests. Only the gear call-out asks it: the
    /// three legs speak the one "Gear" row's call-out, so they are left out of MainForm's mute wrap
    /// (<see cref="IsMuteWrapExempt"/>, [VAR-8]) and check the row themselves.
    /// </summary>
    internal Func<string, bool> IsMonitorMuted { get; init; } =
        key => Settings.SettingsManager.Current.BBS757DisabledMonitorVariablesSet.Contains(key);

    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>Every variable of this partial, added after the map's (never shadowing one).</summary>
    private static void AddReadoutVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        // Announced state rows: Continuous + IsAnnounced rides the 1 Hz batch; ProcessSimVarUpdate
        // speaks them, and each keeps its Ctrl+M row.
        var detents = new Dictionary<double, string>();
        for (int i = 0; i < R.FlapDetents.Count; i++) detents[i] = R.FlapDetents[i];
        Add(R.FlapsHandleKey, StreamedRow("FLAPS HANDLE INDEX", "Flaps", SimVarType.SimVar, "number", detents));
        Add(R.SpeedbrakeKey, StreamedRow(R.SpeedbrakeLeverVar, "Speedbrake", SimVarType.LVar, "number"));
        Add(R.ParkingBrakeKey, StreamedRow(R.ParkingBrakeVar, "Parking Brake", SimVarType.LVar, "number",
            new Dictionary<double, string> { [0] = "Released", [1] = "Set" }));

        // The three gear legs: the nose leg's row is the one Ctrl+M row for the gear call-outs.
        Add(R.GearNoseKey, StreamedRow("GEAR CENTER POSITION", "Gear", SimVarType.SimVar, "percent over 100"));
        var left = StreamedRow("GEAR LEFT POSITION", "Left Main Gear", SimVarType.SimVar, "percent over 100");
        left.ExcludeFromMonitorManager = true;
        Add(R.GearLeftKey, left);
        var right = StreamedRow("GEAR RIGHT POSITION", "Right Main Gear", SimVarType.SimVar, "percent over 100");
        right.ExcludeFromMonitorManager = true;
        Add(R.GearRightKey, right);

        // The MCP button lights.
        foreach (var light in Bbs757McpLights.All)
            Add(light.Key, StreamedRow(light.LightVar, light.Label, SimVarType.LVar, "number"));

        // The announced lights and states (plan 06): status rows on their panel's display, or on no display.
        foreach (var row in Bbs757Annunciators.Rows)
        {
            Add(row.Key, new SimVarDefinition
            {
                Name = row.LVar, DisplayName = row.Label, Type = SimVarType.LVar, Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                RenderAsReadOnlyStatus = row.Panel != null,
                ValueDescriptions = row.States?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<double, string>(),
            });
        }

        // The captain's CDU EXEC light (ruling R36): spoken on change, with a Ctrl+M row of its own,
        // and on no panel or status display.
        Add(R.ExecLightKey, new SimVarDefinition
        {
            Name = R.ExecLightVar, DisplayName = "EXEC light", Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
        });

        // Silent rows, read while their status display is shown (no Ctrl+M row: they speak nothing).
        Add(R.FlapsAngleKey, DisplayedRow("TRAILING EDGE FLAPS LEFT ANGLE", "Flap Angle", "degrees"));
        foreach (var row in R.EngineRows) Add(row.Key, DisplayedRow(row.SimVar, row.Label, row.Units));
        Add(Bbs757Squawk.CodeKey, DisplayedRow("TRANSPONDER CODE:1", "Squawk", "BCO16"));

        // Read on request only: the readouts and the windows' read-backs.
        Add(R.GearHandleKey, RequestedRead("GEAR HANDLE POSITION", "Gear Lever", "bool"));
        Add(R.TotalWeightKey, RequestedRead("TOTAL WEIGHT", "Gross Weight", "pounds"));
        for (int i = 0; i < Bbs757Altimeters.KohlsmanKeys.Count; i++)
            Add(Bbs757Altimeters.KohlsmanKeys[i], RequestedRead($"KOHLSMAN SETTING MB:{i + 1}", $"Altimeter {i + 1}", "millibars"));
        Add(Bbs757Altimeters.DecisionHeightKey, RequestedRead("DECISION HEIGHT", "Decision Height", "feet"));

        // The squawk field. A key containing "_SET" is what MainForm renders as a text box Enter
        // applies; the definition claims it (HandleUIVariableSet), so the event named here is never
        // sent: it only keeps the registration an event, which takes no data definition.
        Add(Bbs757Squawk.SetKey, new SimVarDefinition
        {
            Name = "XPNDR_SET", DisplayName = "Squawk Code",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
            UnparseableTextAsNaN = true,
        });
    }

    private static SimVarDefinition StreamedRow(string name, string label, SimVarType type, string units,
        Dictionary<double, string>? descriptions = null) => new()
    {
        Name = name, DisplayName = label, Type = type, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
        RenderAsReadOnlyStatus = true,
        ValueDescriptions = descriptions ?? new Dictionary<double, string>(),
    };

    private static SimVarDefinition DisplayedRow(string name, string label, string units) => new()
    {
        Name = name, DisplayName = label, Type = SimVarType.SimVar, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = false,
        RenderAsReadOnlyStatus = true, ExcludeFromMonitorManager = true,
    };

    private static SimVarDefinition RequestedRead(string name, string label, string units) => new()
    {
        Name = name, DisplayName = label, Type = SimVarType.SimVar, Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest, ExcludeFromMonitorManager = true,
    };

    // =================================================================================
    // Panels
    // =================================================================================

    /// <summary>The panel the transponder's controls are on (the map's), which gains the squawk field.</summary>
    internal const string TransponderPanel = "TCAS and transponder";

    /// <summary>The transponder mode knob, the map's first control on <see cref="TransponderPanel"/>; the squawk field follows it.</summary>
    internal const string TransponderModeNode = "B757_PED_TCAS_MODE_KNOB";

    /// <summary>The map's MCP panel, whose status display lists the button lights.</summary>
    internal const string McpPanel = "MCP";

    /// <summary>
    /// The map's Pedestal panel with the ignition, the start selectors and the fuel control switches (task
    /// 11d), whose status display shows the engine values (R45), so an engine start (selector GND, watch N2,
    /// fuel control RUN, EGT) happens in one panel.
    /// </summary>
    internal const string EnginesPanel = "Engines";

    /// <summary>
    /// After the map's panels: the flight-control rows join the Status display (after the map's rows),
    /// the MCP's status display lists the lights, the transponder panel gains the squawk field (right
    /// after its mode knob) and its read-back row, and the Engines panel's status display shows the
    /// engine values. A display joins only a panel the map has: a display of a missing panel would be
    /// an orphan entry and its rows would silently vanish.
    /// </summary>
    private static void AddReadoutPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        List<string> Display(string panel)
        {
            if (!displays.TryGetValue(panel, out var rows)) displays[panel] = rows = new List<string>();
            return rows;
        }

        if (!controls.ContainsKey(StatusSection))
        {
            controls[StatusSection] = new List<string>();
            if (!structure.TryGetValue(StatusSection, out var statusPanels)) structure[StatusSection] = statusPanels = new List<string>();
            statusPanels.Add(StatusSection);
        }
        Display(StatusSection).AddRange(new[]
        {
            R.FlapsHandleKey, R.FlapsAngleKey, R.GearNoseKey, R.SpeedbrakeKey, R.ParkingBrakeKey,
        });

        if (controls.ContainsKey(McpPanel))
            Display(McpPanel).AddRange(Bbs757McpLights.All.Select(l => l.Key));

        if (controls.ContainsKey(EnginesPanel))
            Display(EnginesPanel).AddRange(R.EngineRows.Select(r => r.Key));

        foreach (var row in Bbs757Annunciators.Rows)
            if (row.Panel != null && controls.ContainsKey(row.Panel)) Display(row.Panel).Add(row.Key);

        if (controls.TryGetValue(TransponderPanel, out var transponder))
        {
            // The pilot reaches the squawk field second, right after the mode knob, which the map puts first on
            // this panel (curation control_order); a map without the knob leaves the field at the end.
            int mode = transponder.IndexOf(TransponderModeNode);
            transponder.Insert(mode < 0 ? transponder.Count : mode + 1, Bbs757Squawk.SetKey);
            Display(TransponderPanel).Add(Bbs757Squawk.CodeKey);
        }
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>The gear legs speak the "Gear" row's call-out, so they check its mute themselves ([VAR-8]).</summary>
    public override bool IsMuteWrapExempt(string varName)
        => varName is R.GearNoseKey or R.GearLeftKey or R.GearRightKey || base.IsMuteWrapExempt(varName);

    /// <summary>
    /// The state rows' call-outs: true when <paramref name="varName"/> is one of them (spoken or not),
    /// false for anything else, which goes on to the generic path (the silent rows' display values).
    /// </summary>
    private bool ProcessReadoutUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varName)
        {
            case R.FlapsHandleKey:
                SpeakChange(varName, R.FlapDetentName(value) == null ? null : R.FlapsPhrase(value), announcer);
                return true;
            case R.SpeedbrakeKey:
                SpeakChange(varName, R.SpeedbrakeWords(value) == null ? null : R.SpeedbrakePhrase(value), announcer);
                return true;
            case R.ParkingBrakeKey:
                SpeakChange(varName, double.IsFinite(value) ? R.ParkingBrakePhrase(value) : null, announcer);
                return true;
            case R.ExecLightKey:
                SpeakChange(varName, double.IsFinite(value) ? R.ExecLightPhrase(value) : null, announcer);
                return true;
            case R.GearNoseKey:
            case R.GearLeftKey:
            case R.GearRightKey:
                _gearLegs[varName] = value;
                if (GearState() is { } gear && _changes.Observe(R.GearNoseKey, R.GearPhrase(gear), NowMs()) is { } said
                    && !IsMonitorMuted(R.GearNoseKey))
                    announcer.Announce(said);
                return true;
        }
        if (Bbs757McpLights.ForKey(varName) is { } light)
        {
            SpeakChange(varName, double.IsFinite(value) ? Bbs757McpLights.Phrase(light.Label, value) : null, announcer);
            return true;
        }
        if (Bbs757Annunciators.ForKey(varName) is { } row)
        {
            SpeakChange(varName, row.Phrase(value, null), announcer);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The shared trim call-out (<c>MON_ElevatorTrim</c>: the stock <c>ELEVATOR TRIM POSITION</c> in
    /// degrees, on the 1 Hz batch) speaks BlueBird's indicator units
    /// (<see cref="Bbs757Readouts.StabTrimUnits"/>) in the 737s' words, "Trim 5.3"
    /// (<see cref="StabTrimUnitsCallout.Phrase"/>, their one rule), keyed on the tenth it speaks: the
    /// stabilizer is heard about once a second while it moves (ruling R35). The base class's
    /// first-reading baseline, hysteresis, Shift+T toggle and Ctrl+M row apply unchanged; no readout
    /// key and no status row.
    /// </summary>
    protected override (double Key, string Phrase) DescribeElevatorTrim(double degrees)
    {
        double units = Math.Round(R.StabTrimUnits(degrees), 1);
        return (units, StabTrimUnitsCallout.Phrase(units));
    }

    /// <summary>One row's phrase this delivery: spoken when the tracker calls it a change. A null phrase (an unreadable value) is neither spoken nor a baseline.</summary>
    private void SpeakChange(string row, string? phrase, ScreenReaderAnnouncer announcer)
    {
        if (phrase == null) return;
        if (_changes.Observe(row, phrase, NowMs()) is { } said) announcer.Announce(said);
    }

    private Bbs757GearState? GearState() => R.GearLegs(
        _gearLegs.TryGetValue(R.GearNoseKey, out double n) ? n : null,
        _gearLegs.TryGetValue(R.GearLeftKey, out double l) ? l : null,
        _gearLegs.TryGetValue(R.GearRightKey, out double r) ? r : null);

    /// <summary>The state row a control's press or pick moves, or null: an MCP button's light, a speedbrake spot's lever, the parking brake.</summary>
    internal static string? LinkedStatusKey(string node)
    {
        if (Bbs757McpLights.ForNode(node) is { } light) return light.Key;
        return node switch
        {
            "BBS757_SPOILERDOWN_CLICK" or "BBS757_SPOILERARMED_CLICK" or "BBS757_SPOILERUP_CLICK" => R.SpeedbrakeKey,
            "BBS757_PEDESTAL_Park_Brake_Lever" => R.ParkingBrakeKey,
            Bbs757Annunciators.ExtPwrButtonNode => Bbs757SharedKeys.ExtPwrOn,
            _ => null,
        };
    }

    /// <summary>A linked row's phrase for a read value: "CMD R on", "Speedbrake armed", "Parking brake set", "EXT PWR on".</summary>
    internal static string LinkedPhrase(string statusKey, double? value)
    {
        if (Bbs757McpLights.ForKey(statusKey) is { } light) return Bbs757McpLights.Phrase(light.Label, value);
        return statusKey switch
        {
            R.SpeedbrakeKey => R.SpeedbrakePhrase(value),
            Bbs757SharedKeys.ExtPwrOn => Bbs757Annunciators.ExtPwrPhrase(value),
            _ => R.ParkingBrakePhrase(value),
        };
    }

    /// <summary>The app is about to write the control that moves <paramref name="statusKey"/>: its change is the write's echo for a while.</summary>
    private void ArmOwnWriteEcho(string statusKey) => _changes.ArmEcho(statusKey, NowMs() + OwnWriteEchoMs);

    /// <summary>A read-back spoke (or showed) the row's state: that is its baseline now, and later changes speak again.</summary>
    private void SettleOwnWriteEcho(string statusKey, string phrase)
    {
        _changes.Rebaseline(statusKey, phrase);
        _changes.ArmEcho(statusKey, 0);
    }

    /// <summary>A reconnect or a flight load: every state row's next delivery is a baseline again.</summary>
    private void ResetReadoutBaselines()
    {
        _changes.Reset();
        _gearLegs.Clear();
    }

    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        ResetReadoutBaselines();
    }

    // =================================================================================
    // Status display
    // =================================================================================

    /// <summary>
    /// The rows a value description cannot say: the gear (three legs together), the speedbrake, the
    /// flap angle, the squawk (BCO16), the MCP lights and the engine values.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        // The gap plans' rows first (BBS757Definition.Hooks.cs); the first to set the text wins.
        string? text = null;
        FormatOverheadDisplay(varKey, value, ref text);                  // plan 03, then 12
        if (text == null) FormatRadioDisplay(varKey, value, ref text);   // plan 04
        if (text == null) FormatEfbDisplay(varKey, value, ref text);     // plan 07, then 11
        if (text == null) FormatThrustDisplay(varKey, value, ref text);  // plan 15
        if (text != null)
        {
            displayText = text;
            return true;
        }

        switch (varKey)
        {
            case R.GearNoseKey:
                displayText = R.GearDisplay(R.GearLegs(value,
                    _gearLegs.TryGetValue(R.GearLeftKey, out double l) ? l : null,
                    _gearLegs.TryGetValue(R.GearRightKey, out double r) ? r : null));
                return true;
            case R.FlapsHandleKey:
                displayText = R.FlapDetentName(value) ?? "unavailable";
                return true;
            case R.SpeedbrakeKey:
                displayText = R.SpeedbrakeDisplay(value);
                return true;
            case R.FlapsAngleKey:
                displayText = string.Create(CultureInfo.InvariantCulture, $"{value:0.0} degrees");
                return true;
            case Bbs757Squawk.CodeKey:
                displayText = Bbs757Squawk.Decode(value);
                return true;
        }
        if (Bbs757McpLights.ForKey(varKey) != null)
        {
            displayText = Bbs757McpLights.State(value);
            return true;
        }
        if (Bbs757Annunciators.ForKey(varKey) is { States: { } states })
        {
            displayText = double.IsFinite(value) && states.TryGetValue(Math.Round(value), out string? words) ? words : "unavailable";
            return true;
        }
        if (R.EngineRows.FirstOrDefault(r => r.Key == varKey) is { } engine)
        {
            displayText = R.EngineText(engine, value);
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // =================================================================================
    // Readouts (output mode)
    // =================================================================================

    /// <summary>The readouts' reads, replaced by tests (a SimConnectManager cannot read in them); null: <see cref="ReadoutIoFor"/>.</summary>
    internal Func<SimConnectManager, McpIo>? ReadoutIo { get; init; }

    /// <summary>
    /// The readouts' reads: a batch-covered row from the cache (at most a second old), anything else
    /// read fresh. Null when it cannot be read.
    /// </summary>
    private McpIo ReadoutIoFor(SimConnectManager sim) => new()
    {
        Read = (key, ct) =>
        {
            if (GetVariables().TryGetValue(key, out var def) && def.UpdateFrequency == UpdateFrequency.Continuous
                && def.IsAnnounced && sim.GetCachedVariableValue(key) is double cached)
                return Task.FromResult<double?>(cached);
            return sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct);
        },
        Write = sim.ExecuteCalculatorCodeUnique,
        Delay = Task.Delay,
    };

    /// <summary>
    /// L, Shift+G, B, F, Shift+F, W and Shift+W: reads and speaks at once. Never throws (fire and
    /// forget from the hotkey); a readout that fails says what could not be read.
    /// </summary>
    internal async Task ReadoutAsync(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var io = ReadoutIo?.Invoke(sim) ?? ReadoutIoFor(sim);
            string said = action switch
            {
                HotkeyAction.ReadFlaps => await R.ReadFlapsAsync(io, ct),
                HotkeyAction.ReadGear => R.GearReadout(await io.Read(R.GearHandleKey, ct), await io.Read(R.GearNoseKey, ct),
                    await io.Read(R.GearLeftKey, ct), await io.Read(R.GearRightKey, ct)),
                HotkeyAction.ReadAltimeter => Bbs757Altimeters.Readout(
                    await io.Read(Bbs757Altimeters.Kohlsman1Key, ct), await io.Read(Bbs757Altimeters.Kohlsman2Key, ct),
                    await io.Read(Bbs757Altimeters.Kohlsman3Key, ct), await io.Read(Bbs757Altimeters.DecisionHeightKey, ct)),
                HotkeyAction.ReadFuelQuantity => await ReadFuelAsync(io, kilograms: false, ct),
                HotkeyAction.ReadFuelInfo => await ReadFuelAsync(io, kilograms: true, ct),
                HotkeyAction.ReadWaypointInfo => R.WeightPhrase(await io.Read(R.TotalWeightKey, ct), kilograms: false),
                HotkeyAction.ReadGrossWeightKg => R.WeightPhrase(await io.Read(R.TotalWeightKey, ct), kilograms: true),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not a 757 readout"),
            };
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(said);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{action}: readout failed: {ex.Message}");
            announcer.AnnounceImmediate(ReadoutUnavailable(action));   // a readout never goes silent
        }
    }

    /// <summary>
    /// Plan 06's output-mode keys: Shift+1 to Shift+5 the flap maneuver speeds, Shift+6 VREF 30 and 25, from
    /// the gross weight read fresh (<see cref="Bbs757FlapSpeeds"/>); D and Shift+D the destination and the top
    /// of climb or descent from the first officer's PROG page 1 (<see cref="Bbs757Prog"/>).
    /// </summary>
    partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager, ref bool handled)
    {
        if (action is HotkeyAction.ReadDistanceToDest or HotkeyAction.ReadDistanceToTOD)
        {
            _ = ReadProgAsync(action == HotkeyAction.ReadDistanceToDest, announcer);
            handled = true;
            return;
        }
        int index = FlapSpeedIndex(action);
        if (index < 0) return;
        _ = FlapManeuverSpeeds.AnnounceAsync(simConnect, announcer, R.TotalWeightKey, "BBS757",
            lbs => Bbs757FlapSpeeds.Describe(lbs, index));
        handled = true;
    }

    /// <summary>
    /// D (<paramref name="destination"/>) or Shift+D: reads PROG page 1 on the first officer's CDU and speaks
    /// its line, or <see cref="Bbs757Prog.NoCdu"/>. Never throws (fire and forget from the hotkey).
    /// </summary>
    private async Task ReadProgAsync(bool destination, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var screen = await Bbs757Prog.ReadPageOneAsync(CduPages, ct);
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(screen == null ? Bbs757Prog.NoCdu
                : destination ? Bbs757Prog.DestinationPhrase(Bbs757Prog.Destination(screen))
                : Bbs757Prog.TopPhrase(Bbs757Prog.TopOf(screen)));
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"PROG readout failed: {ex.Message}");
            announcer.AnnounceImmediate(Bbs757Prog.NoCdu);
        }
    }

    /// <summary>The schedule index of a flap speed key (Shift+1 to Shift+6), or -1.</summary>
    internal static int FlapSpeedIndex(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadSpeedGD => 0,
        HotkeyAction.ReadSpeedS => 1,
        HotkeyAction.ReadSpeedF => 2,
        HotkeyAction.ReadSpeedVLS => 3,
        HotkeyAction.ReadSpeedVS => 4,
        HotkeyAction.ReadSpeedVFE => 5,
        _ => -1,
    };

    private static async Task<string> ReadFuelAsync(McpIo io, bool kilograms, CancellationToken ct)
        => R.FuelPhrase(
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.LeftFuelKey, ct),
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.RightFuelKey, ct),
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.CenterFuelKey, ct),
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey, ct),
            kilograms);

    private static string ReadoutUnavailable(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadFlaps => R.FlapsPhrase(null),
        HotkeyAction.ReadGear => R.GearPhrase(null),
        HotkeyAction.ReadAltimeter => Bbs757Altimeters.Readout(null, null, null, null),
        HotkeyAction.ReadFuelQuantity or HotkeyAction.ReadFuelInfo => "Fuel unavailable",
        _ => "Gross weight unavailable",
    };
}
