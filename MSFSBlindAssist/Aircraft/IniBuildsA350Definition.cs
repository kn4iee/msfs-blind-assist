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
/// <item>Switch positions ride the continuous batches and are consumed silently: the screen reader already speaks the
/// pilot's own picks. (Announcing a change made elsewhere, with its Ctrl+M row, comes with the A380-parity
/// announcements.)</item>
/// </list>
/// </summary>
public partial class IniBuildsA350Definition : BaseAircraftDefinition, IDisposable
{
    public const string Code = "INI_A350";

    public override string AircraftName => "iniBuilds Airbus A350";
    public override string AircraftCode => Code;

    /// <summary>The sections offered: a section joins once its panels, names and positions are curated and live-tested
    /// (the generated names elsewhere still collide, [VAR-1]).</summary>
    internal static readonly string[] CuratedSections = { "Overhead" };

    private readonly A350Placement _placement;

    /// <summary>Variable key → the panel row it drives (every operable row).</summary>
    private readonly Dictionary<string, A350PlacedRow> _rows = new(StringComparer.Ordinal);

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

    public IniBuildsA350Definition()
    {
        _placement = A350PanelLayout.Place(A350ControlMap.Load());
        foreach (var section in CuratedSections)
            foreach (var panel in _placement.Structure[section])
                foreach (var row in _placement.RowsByPanel[panel])
                    _rows[row.Key] = row;
    }

    /// <summary>The SimConnect manager the definition writes through; MainForm calls this when the A350 is selected,
    /// and every write path refreshes it.</summary>
    public void Attach(SimConnectManager sim) => _sim = sim;

    // The A350 has no stock COM radio: COM AVAILABLE:1 reads 0 and COM_STBY_RADIO_SET_HZ left COM 1 at 0 (live,
    // 2026-10-26), so the surroundings window's Enter must not pretend to tune.
    public override string? StockComTuningRefusal =>
        "This aircraft tunes its radios only through its radio management panels.";

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    public override Dictionary<string, List<string>> GetPanelStructure() =>
        CuratedSections.ToDictionary(s => s, s => new List<string>(_placement.Structure[s]));

    /// <summary>Every panel gets an entry, even an empty one: a panel with no controls entry is never built (HS-5).</summary>
    protected override Dictionary<string, List<string>> BuildPanelControls() =>
        CuratedSections.SelectMany(s => _placement.Structure[s])
            .ToDictionary(p => p, p => _placement.RowsByPanel[p].Select(r => r.Key).ToList());

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();

    /// <summary>
    /// One variable per panel row. Switch positions ride the continuous batches (no individual definitions); knobs
    /// are read when their panel opens; buttons are write-only. A position variable two rows share rides the batch
    /// once ([VAR-7]) and the second row streams it on its own subscription.
    /// </summary>
    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();
        var batchNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in vars.Values)
            if (ContinuousBatchLayout.RidesBatch(def))
                batchNames.Add(ContinuousBatchLayout.FullName(def));

        foreach (var row in _rows.Values)
        {
            if (vars.ContainsKey(row.Key))
                continue;   // never shadow a base variable
            var def = BuildRowVariable(row);
            if (ContinuousBatchLayout.RidesBatch(def) && !batchNames.Add(ContinuousBatchLayout.FullName(def)))
                def.ExcludeFromBatch = true;
            vars[row.Key] = def;
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
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar),
                    DisplayName = row.Name,
                    Type = stockVar ? SimVarType.SimVar : SimVarType.LVar,
                    Units = stockVar ? control.StateUnit ?? "Bool" : "number",
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true,                    // batch-covered; consumed silently in ProcessSimVarUpdate
                    ExcludeFromMonitorManager = true,      // a checkbox here would mute nothing ([VAR-9])
                    ValueDescriptions = new Dictionary<double, string>(row.Positions),
                    // The combo follows a change made elsewhere: the write fires only on a user commit.
                    RefreshControlWhenDefHandled = _ => true,
                };
            }

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

    /// <summary>A row's position delivery is consumed silently: the screen reader has said the pilot's own pick, and
    /// <see cref="NotePickReached"/> learns the switch moved.</summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (_rows.ContainsKey(varName))
        {
            NotePickReached(varName, value);
            return true;
        }
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseOwedSteps();   // a held button or a spring switch is let go before the definition goes away
        _sim = null;
        GC.SuppressFinalize(this);
    }
}
