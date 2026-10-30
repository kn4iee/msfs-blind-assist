using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iniBuilds A350 Airliner (Community package <c>inibuilds-aircraft-a350</c>, MSFS 2024; the A350-900, -900 ULR and
/// -1000 share one cockpit and one module). Full notes: docs/inibuilds-a350.md.
/// <list type="bullet">
/// <item>Every clickable control comes from the generated map (<c>Resources/a350_control_map.json</c>, built by
/// <c>tools/a350-gen</c> from the aircraft's own compiled cockpit behaviour and tooltips); <see cref="A350PanelLayout"/>
/// places them on panels in the manual's cockpit layout, with spoken names. Regenerate the map after an iniBuilds
/// update; never hand-edit it.</item>
/// <item>A write fires the control's OWN B: event, <c>value (&gt;B:AIRLINER_…_Set)</c>, through the calculator path,
/// unique-ified so a repeat is never coalesced (<see cref="A350WritePlan"/>). A push button's event FLIPS it whatever
/// value it is given, so a write is planned from the position the switch is in now.</item>
/// <item>Switch positions ride the continuous batches. A change made elsewhere is spoken by MainForm's generic path, one
/// Ctrl+M row each (the A380's convention); the pilot's own pick is the echo the screen reader already said.</item>
/// </list>
/// </summary>
public partial class IniBuildsA350Definition : BaseAircraftDefinition, IDisposable
{
    public const string Code = "INI_A350";

    public override string AircraftName => "iniBuilds Airbus A350";
    public override string AircraftCode => Code;

    /// <summary>The sections offered: a section joins once its panels, names and positions are curated and live-tested
    /// (all six are). Public for the A350Probe harness, which offers the same.</summary>
    public static readonly IReadOnlyList<string> CuratedSections = new[] { "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit", "Exterior" };

    private readonly A350Placement _placement;

    /// <summary>The sections this instance offers: <see cref="CuratedSections"/>, or a harness's own list.</summary>
    private readonly IReadOnlyList<string> _sections;

    /// <summary>The cockpit lights the curated panels show (<see cref="A350PanelLamps"/>), by key.</summary>
    private readonly Dictionary<string, A350Lamp> _lamps = new(StringComparer.Ordinal);

    /// <summary>Every key a light is made of: the lights' own, their rules' other variables, the two bus powers.</summary>
    private readonly HashSet<string> _lampInputKeys = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<string>> _panelDisplays;

    /// <summary>A button row's own lights: the row reads them, as a sighted pilot sees the button lit
    /// (<see cref="A350ButtonStates.LegendState"/>).</summary>
    private readonly Dictionary<string, List<A350Lamp>> _buttonLamps = new(StringComparer.Ordinal);

    /// <summary>A button row with no light whose state the cockpit shows elsewhere (<see cref="A350ButtonStates"/>).</summary>
    private readonly Dictionary<string, A350ButtonState> _buttonStates = new(StringComparer.Ordinal);

    /// <summary>A button row the pilot presses and holds: the L:var the cockpit holds at 1 while it is held down
    /// (<see cref="A350WritePlan.HeldVarFor"/>).</summary>
    private readonly Dictionary<string, string> _held = new(StringComparer.Ordinal);

    /// <summary>The status lines beside offered rows (<see cref="A350StatusLines"/>), by key.</summary>
    private readonly Dictionary<string, A350StatusLine> _statusLines = new(StringComparer.Ordinal);

    /// <summary>The doors shown and said (<see cref="A350Doors"/>), and whether each was last open (absent until delivered
    /// or seeded).</summary>
    private readonly Dictionary<string, A350Doors.Door> _doors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _doorOpen = new(StringComparer.Ordinal);

    /// <summary>The VHF frequencies and the squawk last seen (<see cref="A350RadioCallouts"/>), and the squawk the RMP window
    /// set and said itself, or -1.</summary>
    private readonly Dictionary<string, double> _radios = new(StringComparer.Ordinal);
    private int _windowSquawk = -1;

    /// <summary>The RMP window set this squawk (BCO16) and said it: its delivery is not said again.</summary>
    public void NoteWindowSquawk(int bcd) => _windowSquawk = bcd;

    /// <summary>The FCU's windows, while its panel is offered (<see cref="A350FcuWindows"/>).</summary>
    private readonly Dictionary<string, A350FcuWindow> _fcuWindows = new(StringComparer.Ordinal);

    /// <summary>Each encoder's value line (<see cref="A350Readouts"/>) by key: its control and its name.</summary>
    private readonly Dictionary<string, (A350Control Control, string Name)> _values = new(StringComparer.Ordinal);

    /// <summary>Variable key → the panel row it drives (every operable row).</summary>
    private readonly Dictionary<string, A350PlacedRow> _rows = new(StringComparer.Ordinal);

    /// <summary>The typed rows offered (<see cref="A350TypedEntry"/>), by key.</summary>
    private readonly Dictionary<string, A350TypedField> _typed = new(StringComparer.Ordinal);

    /// <summary>The key each streamed variable is read under, by its name (L:var names are case-insensitive).</summary>
    private readonly Dictionary<string, string> _keyByName = new(StringComparer.OrdinalIgnoreCase);

    private SimConnectManager? _sim;
    private bool _disposed;

    /// <summary>The clock the commanded-position hold reads; tests replace it.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>Whether a write can reach the aircraft (<see cref="SimConnectManager.CalcWriteCanLand"/>); tests replace it.</summary>
    internal Func<SimConnectManager, bool> CanLand { get; set; } = sim => sim.CalcWriteCanLand;

    /// <summary>Asks for a row's value again so the next delivery puts its combo back; tests replace it.</summary>
    internal Action<string, SimConnectManager> ReRead { get; set; } = (key, sim) =>
    {
        if (sim.IsConnected)
            sim.RequestVariable(key, forceUpdate: true);
    };

    /// <summary>Reads a key from the SimConnect cache; tests replace it.</summary>
    internal Func<SimConnectManager, string, double?> Cached { get; set; } = (sim, key) => sim.GetCachedVariableValue(key);

    /// <summary>Reads a key fresh (<see cref="SimConnectManager.ReadFreshAsync"/>); tests replace it.</summary>
    internal Func<SimConnectManager, string, int, Task<double?>> ReadFresh { get; set; } =
        (sim, key, timeoutMs) => sim.ReadFreshAsync(key, timeoutMs);

    /// <summary>Sends one calculator string; tests replace it.</summary>
    internal Action<SimConnectManager, string> Send { get; set; } = (sim, rpn) => sim.ExecuteCalculatorCodeUnique(rpn);

    /// <summary>The waits inside a plan (a hold, a spring) and before a read-back; tests replace it.</summary>
    internal Func<int, Task> Delay { get; set; } = Task.Delay;

    public IniBuildsA350Definition() : this(CuratedSections) { }

    /// <summary>Offers <paramref name="sections"/>: <c>tools/A350Probe</c> drives a section still being curated.</summary>
    protected IniBuildsA350Definition(IReadOnlyList<string> sections)
    {
        _sections = sections;
        var map = A350ControlMap.Load();
        _placement = A350PanelLayout.Place(map);
        foreach (var section in _sections)
            foreach (var panel in _placement.Structure[section])
                foreach (var row in _placement.RowsByPanel[panel])
                    _rows[row.Key] = row;

        _panelDisplays = new Dictionary<string, List<string>>();
        foreach (var row in _rows.Values)
        {
            if (row.Control is not { } control)
                continue;
            string panel = _placement.RowsByPanel.First(p => p.Value.Contains(row)).Key;
            if (A350Options.For(control.Id) is { } option)
                AddDisplay(panel, option.Key);
            if (row.Action == A350RowAction.Typed && A350TypedEntry.ForControl(control.Id) is { } typed)
                _typed[row.Key] = typed;
            if (row.Action is A350RowAction.Increase or A350RowAction.Typed && A350Readouts.HasValueLine(control))
            {
                string key = A350Readouts.KeyFor(control);
                _values[key] = (control, LineName(row.Name));
                AddDisplay(panel, key);
            }
        }
        foreach (var lamp in A350PanelLamps.Resolve(map, _placement, _sections))
        {
            _lamps[lamp.Key] = lamp;
            _lampInputKeys.UnionWith(lamp.InputKeys);
            AddDisplay(lamp.Panel, lamp.Key);
        }
        FindEchoLamps();
        FindSetTwins();

        var offeredPanels = _sections.SelectMany(s => _placement.Structure[s]).ToHashSet(StringComparer.Ordinal);
        foreach (var window in A350FcuWindows.All)
            if (offeredPanels.Contains(window.Panel))
            {
                _fcuWindows[window.Key] = window;
                _lampInputKeys.Add(window.Key);
                _lampInputKeys.UnionWith(window.Inputs.Skip(1).Select(A350PanelLamps.InputKey));
                AddDisplay(window.Panel, window.Key);
            }
        FindSpeakingWindows();

        foreach (var line in A350StatusLines.All)
            if (_rows.ContainsKey(line.Beside))
            {
                _statusLines[line.Key] = line;
                _lampInputKeys.Add(line.Key);   // consumed silently: shown, never spoken
                AddDisplay(_placement.RowsByPanel.First(p => p.Value.Any(r => r.Key == line.Beside)).Key, line.Key);
            }

        // The doors on the Doors and Hatches panel (A350Doors), each said as it opens and closes.
        if (_rows.ContainsKey(A350Doors.PanelRow))
            foreach (var door in A350Doors.All)
            {
                _doors[door.Key] = door;
                AddDisplay(_placement.RowsByPanel.First(p => p.Value.Any(r => r.Key == A350Doors.PanelRow)).Key, door.Key);
            }

        // The ECAM Control Panel's status display reads the SD page shown (A350SdText), as the A380's does:
        // the page row's line carries the page's name and content.
        if (_rows.ContainsKey(SdPageKey))
            AddDisplay(_placement.RowsByPanel.First(p => p.Value.Any(r => r.Key == SdPageKey)).Key, SdPageKey);
        foreach (var d in DisplayPanels)
            AddDisplay(d.Panel, d.Key);

        _lampBoard = new A350LampBoard(_lamps.Values);
        ArmSeed();   // a first connection seeds too, once its deliveries settle

        foreach (var group in _lamps.Values.Where(l => l.ControlId != null).GroupBy(l => l.ControlId!))
            foreach (var row in _rows.Values.Where(r => r.Action == A350RowAction.Press && r.Control?.Id == group.Key))
                _buttonLamps[row.Key] = group.OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
        foreach (var (rowKey, state) in A350ButtonStates.ByRow)
            if (_rows.ContainsKey(rowKey) && !_buttonLamps.ContainsKey(rowKey))
                _buttonStates[rowKey] = state;
        foreach (var (rowKey, state) in A350ButtonStates.OverLamps)
            if (_rows.ContainsKey(rowKey))
                _buttonStates[rowKey] = state;   // read before its light, which says less
        foreach (var row in _rows.Values)
            if (row.Action == A350RowAction.Press && row.Control is { } held && A350WritePlan.HeldVarFor(held) is { } heldVar)
                _held[row.Key] = heldVar;

        void AddDisplay(string panel, string key)
        {
            if (!_panelDisplays.TryGetValue(panel, out var lines))
                _panelDisplays[panel] = lines = new List<string>();
            if (!lines.Contains(key))
                lines.Add(key);
        }
    }

    /// <summary>A value line's name from its row's: "Cabin altitude target increase" and "Cabin altitude target
    /// (feet)" both read "Cabin altitude target".</summary>
    private static string LineName(string rowName)
    {
        if (rowName.EndsWith(" increase", StringComparison.Ordinal))
            return rowName[..^" increase".Length];
        int unit = rowName.IndexOf(" (", StringComparison.Ordinal);
        return unit > 0 ? rowName[..unit] : rowName;
    }

    /// <summary>The key a streamed variable is read under: the row, light, window or value line that streams it.</summary>
    internal string KeyForInput(string name)
    {
        _ = GetVariables();   // the keys are known once the variables are built
        return _keyByName.TryGetValue(name, out var key) ? key : A350PanelLamps.InputKey(name);
    }

    /// <summary>The SimConnect manager the definition writes through; MainForm calls this when the A350 is selected,
    /// and every write path refreshes it.</summary>
    public void Attach(SimConnectManager sim) => _sim = sim;

    // The A350 has no stock COM radio: COM AVAILABLE:1 reads 0 and COM_STBY_RADIO_SET_HZ left COM 1 at 0 (live,
    // 2026-10-26), so the surroundings window's Enter tunes VHF 1 on the captain's RMP, as a pilot does, and reads its screen back.
    public override Task<double?>? TuneCom1Own(SimConnectManager sim, int frequencyHz, bool active) => TuneVhf1(sim, frequencyHz, active);

    private async Task<double?> TuneVhf1(SimConnectManager sim, int frequencyHz, bool active)
    {
        foreach (string key in A350Rmp.ComTuningKeys(frequencyHz, active))
        {
            sim.ExecuteCalculatorCodeUnique(A350Rmp.Press(1, key));
            await Delay(RmpKeyGapMs);
        }
        double? read = null;
        for (int attempt = 0; attempt < RmpReadAttempts && !Services.Com1Tuning.Holds(read, frequencyHz); attempt++)
        {
            await Delay(RmpReadGapMs);
            if (Displays != null && (await Displays.DrawAsync(A350Rmp.Gauge(1)))?.FirstOrDefault() is { Ok: true } capture)
                read = A350.Display.A350RmpPage.Vhf1Hz(A350.Display.A350RmpPage.Read(capture), active);
        }
        return read;
    }

    /// <summary>Between two RMP keys (the RMP window's own gap); from the last key to a read of the screen, and how many such reads.</summary>
    private const int RmpKeyGapMs = 150, RmpReadGapMs = 300, RmpReadAttempts = 4;

    /// <summary>The FCU's METRIC ALT pushbutton row (INI_FCU_METRIC_STATE: 0 only feet, 1 showing meters).</summary>
    public const string MetricAltitudeKey = "A350_FCU_METER";

    // METRIC ALT is the A350's metric altitude, as MTRS is the A380's (parity task 10): MainForm's A / Q read-outs and
    // altitude call-outs ask it here. Unread is feet, the switch's rest.
    public override bool? MetricAltitude =>
        _rows.ContainsKey(MetricAltitudeKey) ? _sim is { } sim && Cached(sim, MetricAltitudeKey) is double on && on >= 0.5 : null;

    // Visual landing guidance: the A330neo's widebody profile, the closest Airbus fly-by-wire widebody (its glidepath and
    // flare biases are estimates pending an in-sim coupled-ILS calibration, the A330's status too), not the A320 baseline;
    // the flare at about 40 ft radio altitude is Airbus's figure for the A330/A340/A350/A380 family (Safety First).
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg = 4.5,
        ReferenceVrefKnots = 145.0,
        MaxPitchRateDegPerSec = 2.0,
        MaxBankRateDegPerSec = 2.5,
        GlideslopeAltitudeBiasFt = 70.0,   // estimate
        FlareAltitudeBiasFt = 30.0,        // estimate
        FlareTriggerWheelHeightFt = 40.0,
        FlareTargetPitchDeg = 5.0,
    };

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = _sections.ToDictionary(s => s, s => new List<string>(_placement.Structure[s]));
        structure[DisplaysSection] = DisplayPanels.Select(d => d.Panel).ToList();
        return structure;
    }

    /// <summary>Every panel gets an entry, even an empty one: a panel with no controls entry is never built (HS-5).
    /// The Displays panels hold only their status display.</summary>
    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = _sections.SelectMany(s => _placement.Structure[s])
            .ToDictionary(p => p, p => _placement.RowsByPanel[p].Select(r => r.Key).ToList());
        foreach (var d in DisplayPanels)
            controls[d.Panel] = new List<string>();
        return controls;
    }

    /// <summary>Each panel's status display: its lights, built once ([SIM-16]).</summary>
    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => _panelDisplays;

    /// <summary>A button row reads its state as the cockpit shows it: the state it switches where that says more than its
    /// lights or it has none (<see cref="A350ButtonStates"/>), else its lit legends. Nothing while an input is unread.</summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        if (_sim is { } sim && _rows.TryGetValue(varKey, out var row))
        {
            // A button the pilot presses and holds: the cockpit's own record of it held down, and its lights if it has
            // any. It streams, so the label repaints when the press lands, or a flight load lets go of it.
            if (_held.TryGetValue(varKey, out var heldVar))
            {
                bool lit = _buttonLamps.ContainsKey(varKey);
                string? lights = lit ? Legends(row, sim) : null;
                if (Cached(sim, KeyForInput(heldVar)) is double held && (!lit || lights != null))
                {
                    stateText = A350ButtonStates.HeldState(held >= 0.5, lights);
                    return true;
                }
            }
            else if (_buttonStates.TryGetValue(varKey, out var state)
                     && state.Vars.Select(v => Cached(sim, KeyForInput(v))).ToList() is var values && values.All(v => v.HasValue))
            {
                stateText = state.Words(values.Select(v => v!.Value).ToList());
                return true;
            }
            else if (_buttonLamps.ContainsKey(varKey))
            {
                if (Legends(row, sim) is { } legends)
                {
                    stateText = legends;
                    return true;
                }
            }
        }
        return base.TryDescribeControlState(varKey, out stateText);
    }

    /// <summary>A button's lights as <see cref="A350ButtonStates.LegendState"/> reads them, or null while one is unread.</summary>
    private string? Legends(A350PlacedRow row, SimConnectManager sim)
    {
        var lit = _buttonLamps[row.Key].Select(l => (l.Name, Lit: l.IsLit(key => Cached(sim, key)))).ToList();
        return lit.All(l => l.Lit.HasValue)
            ? A350ButtonStates.LegendState(row.Name, lit.Select(l => (l.Name, l.Lit!.Value)).ToList())
            : null;
    }

    /// <summary>A light's status line as the cockpit shows it, its rule AND its bus's light power: "On" or "Off".
    /// While an input is unread it is not composed, as on the iniBuilds A300.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (DisplayPanels.FirstOrDefault(d => d.Key == varKey) is { } display)
        {
            displayText = DisplayText(display);
            return true;
        }
        if (_lamps.TryGetValue(varKey, out var lamp) && _sim is { } sim && lamp.IsLit(key => Cached(sim, key)) is bool lit)
        {
            displayText = lit ? "On" : "Off";
            return true;
        }
        if (_fcuWindows.TryGetValue(varKey, out var window) && _sim is { } fcuSim
            && window.Compose(input => Cached(fcuSim, WindowInputKey(window, input))) is string shown)
        {
            displayText = shown;
            return true;
        }
        if (varKey == SdPageKey)
        {
            displayText = A350.Display.A350SdText.Build(Ecam?.LatestCapture, value);
            return true;
        }
        if (_statusLines.TryGetValue(varKey, out var line))
        {
            displayText = line.Format(value);
            return true;
        }
        if (_doors.TryGetValue(varKey, out var door))
        {
            displayText = A350Doors.State(door, A350Doors.IsOpen(value));
            return true;
        }
        if (_values.TryGetValue(varKey, out var readout))
        {
            displayText = A350Readouts.Format(readout.Control, value);
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>Rows that share a variable with another row and leave it the batch: the All thrust levers row reads lever 1's
    /// angle and says nothing on its own delivery, while the two levers must arrive in one message, or a lone lever's
    /// sentence comes before "All thrust levers" (review 2026-10-29: lever 1 streamed on its own 1 Hz subscription).</summary>
    private static readonly HashSet<string> BatchYields = new(StringComparer.Ordinal) { A350.A350LeverSpeech.AllLevers };

    /// <summary>
    /// One variable per panel row. Switch positions ride the continuous batches (no individual definitions); knobs
    /// are read when their panel opens; buttons are write-only. A position variable two rows share rides the batch
    /// once ([VAR-7]) and the second row streams it on its own subscription; a row in <see cref="BatchYields"/> is
    /// always the second.
    /// </summary>
    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();
        var batchNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in vars.Values)
            if (ContinuousBatchLayout.RidesBatch(def))
                batchNames.Add(ContinuousBatchLayout.FullName(def));

        var rowDefs = new List<(string Key, SimVarDefinition Def)>();
        foreach (var row in _rows.Values)
        {
            if (vars.ContainsKey(row.Key))
                continue;   // never shadow a base variable
            var def = BuildRowVariable(row);
            if (_setTwins.ContainsKey(row.Key))
                def.ExcludeFromMonitorManager = true;   // its twin speaks for both: a Ctrl+M row here would mute nothing ([VAR-9])
            if (_buttonLamps.TryGetValue(row.Key, out var buttonLamps))
                def.StateVariables = buttonLamps.SelectMany(l => l.InputKeys).Distinct().ToList();   // the label repaints when a light changes
            rowDefs.Add((row.Key, def));
            vars[row.Key] = def;
            if (def.UpdateFrequency == UpdateFrequency.Continuous)
                _keyByName.TryAdd(def.Name, row.Key);
        }
        foreach (var (key, def) in rowDefs.OrderBy(r => BatchYields.Contains(r.Key) ? 1 : 0))
            if (ContinuousBatchLayout.RidesBatch(def) && !batchNames.Add(ContinuousBatchLayout.FullName(def)))
                def.ExcludeFromBatch = true;

        foreach (var lamp in _lamps.Values)
        {
            Register(lamp.Key, lamp.Rule.Inputs[0], SpokenName(lamp), lamp.InputKeys);
            // Spoken by the definition as the cockpit lights it, one Ctrl+M row each; an echo of another light or row is not.
            vars[lamp.Key].ExcludeFromMonitorManager = IsEcho(lamp);
        }
        foreach (var lamp in _lamps.Values)
            foreach (var input in lamp.Rule.Inputs.Skip(1))
                if (!vars.ContainsKey(lamp.KeyFor(input)))
                    Register(lamp.KeyFor(input), input, input, null);
        foreach (var option in A350Options.All)
        {
            Register(option.Key, option.Var, option.Name + " option", Array.Empty<string>());
            vars[option.Key].ValueDescriptions = new Dictionary<double, string> { [0] = "Not fitted", [1] = "Fitted" };
        }
        foreach (var window in _fcuWindows.Values)
        {
            var keys = new List<string> { window.Key };
            keys.AddRange(window.Inputs.Skip(1).Select(A350PanelLamps.InputKey));
            Register(window.Key, window.Inputs[0], window.Name, keys);
            vars[window.Key].ValueDescriptions = new Dictionary<double, string>();
            // A window that speaks on its own (A350FcuCallouts) has its Ctrl+M row; the call-out checks it ([A320-37]).
            vars[window.Key].ExcludeFromMonitorManager = !A350FcuCallouts.Speaking.Contains(window.Name);
            foreach (var input in window.Inputs.Skip(1))
                if (!vars.ContainsKey(A350PanelLamps.InputKey(input)))
                    Register(A350PanelLamps.InputKey(input), input, input, null);
        }
        foreach (var (key, value) in _values)
        {
            Register(key, Bare(value.Control.StateVar!), value.Name, Array.Empty<string>());
            vars[key].ValueDescriptions = new Dictionary<double, string>();
        }
        foreach (var line in _statusLines.Values)
        {
            Register(line.Key, line.Var, line.Name, Array.Empty<string>(), line.Unit);
            vars[line.Key].ValueDescriptions = new Dictionary<double, string>();
        }
        // The IRS alignment call-out's Ctrl+M row (A350IrsAlignment), on the A350's own overall flag, consumed silently.
        if (HasIrsPanel)
        {
            Register(IrsAlignmentKey, "INI_IRS_ALIGNED", "IRS alignment", null);
            vars[IrsAlignmentKey].ExcludeFromMonitorManager = false;
            _lampInputKeys.Add(IrsAlignmentKey);
        }
        // The OIS's cover banners (A350CoverAlerts): the engine covers on the call-out's Ctrl+M row, the pitot covers, both
        // consumed and judged at each batch end.
        Register(A350CoverAlerts.EnginesKey, "INI_COVERS_ENABLED", A350CoverAlerts.MuteLabel, null);
        vars[A350CoverAlerts.EnginesKey].ExcludeFromMonitorManager = false;
        _lampInputKeys.Add(A350CoverAlerts.EnginesKey);
        Register(A350CoverAlerts.PitotKey, "COVER ON:2", "Pitot covers", null, "bool");
        _lampInputKeys.Add(A350CoverAlerts.PitotKey);
        // The VHF 1 and 2 frequencies and the squawk (A350RadioCallouts), each on its own Ctrl+M row, consumed and said.
        foreach (var (key, name, label, units) in A350RadioCallouts.Rows)
        {
            Register(key, name, label, null, units);
            vars[key].ExcludeFromMonitorManager = false;
            vars[key].ValueDescriptions = new Dictionary<double, string>();
        }
        // The doors (A350Doors): each its interactive point, on its own Ctrl+M row, consumed and said as it moves.
        foreach (var door in _doors.Values)
        {
            Register(door.Key, door.Var, door.Name, null, "percent over 100");
            vars[door.Key].ExcludeFromMonitorManager = false;
            vars[door.Key].ValueDescriptions = new Dictionary<double, string>();
        }
        // The TCAS's advisories (A350TcasCallouts): each aural's flag and the vertical speed it commands, consumed and said at
        // a batch end, all on one Ctrl+M row.
        foreach (var advisory in A350TcasCallouts.Advisories)
            Register(advisory.Key, advisory.Var, "TCAS " + advisory.Said, null);
        Register(A350TcasCallouts.TargetKey, A350TcasCallouts.TargetVar, "TCAS target vertical speed", null);
        vars[A350TcasCallouts.TargetKey].ValueDescriptions = new Dictionary<double, string>();
        // The module watchdog (A350ModuleWatchdog): the heartbeat on the call-out's own Ctrl+M row and the sim's clock, both
        // consumed silently and judged at each batch end.
        Register(A350ModuleWatchdog.HeartbeatKey, A350ModuleWatchdog.HeartbeatVar, A350ModuleWatchdog.MuteLabel, null);
        vars[A350ModuleWatchdog.HeartbeatKey].ExcludeFromMonitorManager = false;
        vars[A350ModuleWatchdog.HeartbeatKey].ValueDescriptions = new Dictionary<double, string>();
        _lampInputKeys.Add(A350ModuleWatchdog.HeartbeatKey);
        Register(A350ModuleWatchdog.SimTimeKey, "ABSOLUTE TIME", "Sim time", null, "seconds");
        vars[A350ModuleWatchdog.SimTimeKey].ValueDescriptions = new Dictionary<double, string>();
        _lampInputKeys.Add(A350ModuleWatchdog.SimTimeKey);
        Register(A350PanelLamps.AcPowerKey, A350PanelLamps.AcPowerVar, "AC light power", null);
        Register(A350PanelLamps.DcPowerKey, A350PanelLamps.DcPowerVar, "DC light power", null);
        Register(A350PanelLamps.AnnLtKey, A350PanelLamps.AnnLtVar, "Annunciator light test", null);

        // What the display draw code asks the sim for itself (engine N2, the baro settings): streamed, consumed
        // silently, and answered to the draw engine from the cache (AnswerAVar).
        foreach (var a in A350.Display.A350AVars.All)
        {
            Register(a.Key, a.Name, a.Name, null, a.Units);
            _lampInputKeys.Add(a.Key);
        }

        // The display lines: carried by the FD switch's variable (any streamed one would do); the text is the
        // display's latest read (DisplayText), refreshed by the status display's tick. Consumed silently.
        foreach (var d in DisplayPanels)
        {
            Register(d.Key, "INI_FD_ON", d.Panel, null);
            _lampInputKeys.Add(d.Key);
        }
        // Each screen's power (IsDark), streamed here unless a row above already streams that variable (a brightness knob's
        // row is read on request, so it never does): consumed silently.
        foreach (var power in DisplayPanels.Select(d => d.Power).OfType<string>().Distinct(StringComparer.Ordinal))
            if (!_keyByName.ContainsKey(power))
            {
                Register(A350PanelLamps.InputKey(power), power, power, null);
                _lampInputKeys.Add(A350PanelLamps.InputKey(power));
            }

        // What a typed value is planned from and read back in, where nothing above streams it yet.
        foreach (var field in _typed.Values)
            foreach (var input in A350TypedEntry.Inputs(field))
                if (!_keyByName.ContainsKey(input))
                {
                    Register(A350PanelLamps.InputKey(input), input, input, null);
                    _lampInputKeys.Add(A350PanelLamps.InputKey(input));   // consumed silently
                }

        // What the read-out hotkeys say where nothing above streams it (the characteristic speeds, A350Hotkeys).
        foreach (var name in A350Hotkeys.ReadoutVars.Where(v => !_keyByName.ContainsKey(v)))
        {
            Register(A350PanelLamps.InputKey(name), name, name, null);
            _lampInputKeys.Add(A350PanelLamps.InputKey(name));   // consumed silently
        }

        // The take-off roll call-outs (A350TakeoffCallouts), as the A380's: the indicated airspeed per sim frame on its own
        // subscription (the 1 Hz batch would call "Rotate" up to a second late), consumed. The FMS's V-speeds are no sim
        // variable: the display reader reads them (ObserveTakeoffSpeeds), INI_V1, INI_VR and INI_V2 being never written.
        vars[A350TakeoffCallouts.IasKey] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Take-off callout airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,
            HighFrequency = true,
            ExcludeFromMonitorManager = true,
        };

        // What a button reads on its label from the state it switches (A350ButtonStates); the label repaints with it.
        foreach (var (rowKey, state) in _buttonStates)
        {
            foreach (var name in state.Vars.Where(v => !_keyByName.ContainsKey(v)))
            {
                Register(A350PanelLamps.InputKey(name), name, name, null);
                _lampInputKeys.Add(A350PanelLamps.InputKey(name));   // consumed silently
            }
            if (vars.TryGetValue(rowKey, out var rowDef))
                rowDef.StateVariables = state.Vars.Select(v => _keyByName[v]).ToArray();
        }

        // What a held button reads (the cockpit's record of it held down): its label repaints with it, and with its lights.
        foreach (var (rowKey, heldVar) in _held)
        {
            if (!_keyByName.ContainsKey(heldVar))
            {
                Register(A350PanelLamps.InputKey(heldVar), heldVar, heldVar, null);
                _lampInputKeys.Add(A350PanelLamps.InputKey(heldVar));   // consumed silently
            }
            if (vars.TryGetValue(rowKey, out var rowDef))
                rowDef.StateVariables = (rowDef.StateVariables ?? Array.Empty<string>()).Append(_keyByName[heldVar]).Distinct().ToList();
        }

        // A light streams its rule's first variable under its own key, which carries its status line; its rule's other
        // variables and the bus powers stream once each, consumed silently. A name already batched streams on its own
        // subscription ([VAR-7]).
        void Register(string key, string name, string displayName, IReadOnlyList<string>? stateVariables, string? stockUnit = null)
        {
            var def = new SimVarDefinition
            {
                Name = name,
                DisplayName = displayName,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,                     // consumed silently in ProcessSimVarUpdate
                ExcludeFromMonitorManager = true,       // a row only where something speaks: the lights, FCU windows and IRS rows set theirs ([VAR-9])
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
                RenderAsReadOnlyStatus = stateVariables != null,
                StateVariables = stateVariables is { Count: > 0 } ? stateVariables : null,   // a power change repaints a light
            };
            if (stockUnit is { } units || A350FcuWindows.StockUnits.TryGetValue(name, out units))
            {
                def.Type = SimVarType.SimVar;   // a window input that is a sim variable (the baro setting), in its unit
                def.Units = units;
            }
            if (!batchNames.Add(ContinuousBatchLayout.FullName(def)))
                def.ExcludeFromBatch = true;
            vars[key] = def;
            _keyByName.TryAdd(name, key);
        }

        // Every A350 write is a calculator-path string, dropped without a word when MobiFlight's WASM module is
        // missing; registering this opts the A350 into MainForm's probe and its spoken warning.
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE",
            DisplayName = "Bridge Probe",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
        };

        Log.Info("A350", $"Built {vars.Count} variables, {_rows.Count} panel rows.");
        return vars;
    }

    /// <summary>A map control's row.</summary>
    private static SimVarDefinition BuildRowVariable(A350PlacedRow row)
    {
        var control = row.Control!;
        switch (row.Action)
        {
            case A350RowAction.Set when control.Kind == A350Kinds.Knob:
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar!),
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.OnRequest,   // read when its panel opens
                    RenderAsSlider = true,
                    SliderMin = 0,
                    SliderMax = 100 * (control.Scale ?? 1),
                    RefreshControlWhenDefHandled = _ => true,
                };

            case A350RowAction.Set:
            {
                bool stockVar = control.StateVar!.StartsWith("A:", StringComparison.Ordinal);
                var positions = row.Positions.Keys.ToArray();
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar),
                    DisplayName = row.Name,
                    Type = stockVar ? SimVarType.SimVar : SimVarType.LVar,
                    Units = stockVar ? control.StateUnit ?? "Bool" : "number",
                    UpdateFrequency = UpdateFrequency.Continuous,
                    // Spoken by MainForm's generic path when something other than the pilot's own pick moves it, one
                    // Ctrl+M row each (the A380's switch combos); the pick itself is the echo the screen reader said.
                    // A spring switch's return to rest follows the pilot's own press: consumed silently ([VAR-9]).
                    IsAnnounced = true,
                    ExcludeFromMonitorManager = control.Kind == A350Kinds.Spring,
                    ValueDescriptions = new Dictionary<double, string>(row.Positions),
                    SpokenOnlyDescriptions = A350Levers.SpokenOnlyWordsFor(control.Id),
                    // The combo follows a change made elsewhere (the write fires only on a user commit), and only to one of
                    // its positions: a lever sweeping between detents never moves it through values it does not hold
                    // ([VAR-6]; review 2026-10-29).
                    RefreshControlWhenDefHandled = v => positions.Any(p => Math.Abs(p - v) < A350WritePlan.SameValueTolerance),
                };
            }

            case A350RowAction.Custom:   // a choice the knob's own clicks make (A350RunwayCondition); nothing reads it back
                return new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key,
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    ValueDescriptions = new Dictionary<double, string>(row.Positions),
                };

            case A350RowAction.Typed:   // a "_SET" key: MainForm renders a typed field, applied by Enter
                return new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key,
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    UnparseableTextAsNaN = true,   // A350TypedEntry refuses NaN; a 0 would be north or level off
                };

            default:   // Press, Increase, Decrease: write-only buttons, never registered
                return new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key.Replace('#', '_'),
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    RenderAsButton = true,
                };
        }
    }

    /// <summary>"L:NAME" → "NAME", "A:NAME:2" → "NAME:2" (SimVarDefinition names carry no prefix).</summary>
    private static string Bare(string stateVar) => stateVar.Length > 2 && stateVar[1] == ':' ? stateVar.Substring(2) : stateVar;

    /// <summary>A switch row's delivery tells <see cref="NotePickReached"/> the switch moved, then goes on to MainForm's
    /// generic announce, which speaks a change the pilot did not pick (its echo gate keeps the pick quiet); a spring
    /// switch's is consumed, its return to rest being the pilot's own press.</summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        _batchAnnouncer = announcer;   // MainForm's one announcer: what a batch end speaks with
        // A seed still pending counts this delivery as evidence for or against the cache having settled (all the A350's own).
        if (_seedGate.Armed && _seedKeys.Contains(varName))
            _seedGate.NoteValue(varName, value, ownedByAircraft: true);
        if (HandleTakeoffCallouts(varName, value, announcer))
            return true;
        if (HandleLeverDelivery(varName, value, announcer))
            return true;
        if (_rows.TryGetValue(varName, out var row))
        {
            NotePickReached(varName, value);
            return row.Control?.Kind == A350Kinds.Spring || _setTwins.ContainsKey(varName) || IsTwinPick(varName, value);
        }
        if (_lampBoard.Handles(varName))
            HandleLampDelivery(varName, value, announcer);
        if (_speakingByInputKey.TryGetValue(varName, out var windows))
            foreach (var window in windows)
                ObserveWindow(window, varName, value, announcer);
        // A VHF frequency or the squawk, as the A380's: said as it changes, never while the seed settles; the squawk the RMP
        // window just set (and said) once not.
        if (A350RadioCallouts.IsRadioKey(varName))
        {
            double? was = _radios.TryGetValue(varName, out var last) ? last : null;
            _radios[varName] = value;
            if (_seedGate.Armed || was is not double before)
                return true;
            bool squawk = varName == A350RadioCallouts.SquawkKey;
            if (squawk ? (int)Math.Round(value) == (int)Math.Round(before) : Math.Abs(value - before) <= 0.0004)
                return true;
            if (squawk && (int)Math.Round(value) == _windowSquawk)
            {
                _windowSquawk = -1;
                return true;
            }
            if ((squawk || A350RadioCallouts.InVhfBand(value)) && !IsMuted(varName) && !_disposed)
                announcer.Announce(squawk ? A350RadioCallouts.Squawk((int)Math.Round(value)) : A350RadioCallouts.Frequency(varName, value));
            return true;
        }
        // A door, as the A380's: said once per transition, never while the seed settles (the new flight's state).
        if (_doors.TryGetValue(varName, out var door))
        {
            bool open = A350Doors.IsOpen(value);
            bool? was = _doorOpen.TryGetValue(varName, out var before) ? before : null;
            _doorOpen[varName] = open;
            if (!_seedGate.Armed && was is bool closedOrOpen && closedOrOpen != open && !IsMuted(varName) && !_disposed)
                announcer.Announce(A350Doors.Said(door, open));
            return true;
        }
        if (A350TcasCallouts.IsAdvisory(varName))
        {
            _tcas.Deliver(varName, value, Clock());   // said at a batch end, with the vertical speed to fly
            return true;
        }
        if (varName == A350TcasCallouts.TargetKey)
            return true;
        if (varName.StartsWith("A350_STAT_IR", StringComparison.Ordinal))
            _irsAnnouncer = announcer;   // the IRS call-out speaks at the batch end
        else if (varName is A350ModuleWatchdog.HeartbeatKey or A350ModuleWatchdog.SimTimeKey)
            _watchdogAnnouncer = announcer;   // so does the module watchdog
        if (_lampInputKeys.Contains(varName) || _values.ContainsKey(varName) || A350Options.All.Any(o => o.Key == varName))
            return true;   // a light's input: the status display composes it on its next paint
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseOwedSteps();   // a held button or a spring switch is let go before the definition goes away
        _seedGate.Disarm();   // nor may a pending seed run for it
        _sim = null;
        GC.SuppressFinalize(this);
    }
}
