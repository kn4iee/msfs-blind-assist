using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// BlueBird Simulations 757-200 (package <c>bluebirdsim-aircraft-757-200</c>, release 0.1.0).
///
/// ═══ THE CONTROL MAP ═══
///
/// The panels are not hand-written. <c>tools/b757-gen/generate_b757_map.py</c> reads the
/// aircraft's own cockpit XML and emits <c>bbs757_control_map.json</c> (embedded, loaded by
/// <see cref="Bbs757ControlMap"/>): every regular control's node, panel, spoken label, named
/// positions and the vendor's click code. This class turns each row into one variable keyed by its
/// node and each panel into a panel, in map order. Regenerate the JSON after a BlueBird update
/// rather than patching C#.
///
/// ═══ HOW IT WRITES ═══
///
/// Every control write replays the control's OWN click code through the MobiFlight calculator
/// (<see cref="SimConnectManager.ExecuteCalculatorCodeUnique"/>, the unique prefix of [SIM-17]):
/// the cockpit click updates every var the cockpit updates (the battery click sets both its state
/// var and its <c>_ON</c> legend), so replaying it keeps parity. Before any write the definition
/// asks <see cref="SimConnectManager.CalcWriteCanLand"/> and, when it is false, says
/// "{label} unavailable" and writes nothing (the MD-11's [MD11-16] pattern). It registers
/// <c>MSFSBA_BRIDGE_PROBE</c> so the calc path is verified at all. The writes themselves are in
/// the Interaction partial.
///
/// ═══ BBS NEST ═══
///
/// BlueBird's systems do not run without its companion app; see <see cref="Bbs757Nest"/>. The
/// definition says so once per connection and once per flight load when the process is missing.
///
/// ═══ THE DATA-DEFINITION BUDGET ═══
///
/// Every control is an OnRequest L:var read on demand (one individual data definition each,
/// about 420), the status rows ride the continuous batch or their own on-request read, and the
/// connection's ceiling is about 1000 (docs/architecture.md). The flight-control rows, the 14 MCP
/// lights and the EXEC light ride the batch; the engine rows, the flap angle, the squawk, the
/// altimeters, the decision height, the gear lever, the gross weight and the A/T's armed flag are 19
/// individual definitions (about 500 variables in all). Bbs757DefinitionTests keeps the total under 900.
///
/// ═══ VERIFY IN SIM ═══
///   • A panel pick moves the control and the combo then shows where it really is.
///   • A button press is heard ("… pressed") and the aircraft reacts (MCP mode lights).
///   • With BBS Nest closed, the warning speaks once after connecting; after Nest starts and the
///     flight is reloaded, nothing is said.
///   • Ctrl+H/S/A/V set the MCP heading, speed (knots), altitude and vertical speed, and what is
///     spoken afterwards is the MCP's own value; their mode buttons press as the panel does and say
///     the light once; Shift+H/S/A/V read the same values (Bbs757Mcp).
///   • Flaps, gear, speedbrake, parking brake, the MCP lights and the EXEC light speak their changes,
///     and the trim speaks BlueBird's units while it moves; L, Shift+G, B, F, Shift+F, W and
///     Shift+W read them; Ctrl+B sets the altimeters and the radio minimums,
///     Ctrl+P is the autopilot window, the transponder panel takes a squawk; the Engines panel shows
///     the stock engine values (all in BBS757Definition.Readouts.cs, .Altimeter.cs, .Autopilot.cs;
///     unmeasured parts in docs/b757/plans/01-prototype-insim-tests.md).
/// </summary>
public partial class BBS757Definition : BaseAircraftDefinition
{
    public override string AircraftName => "BlueBird 757-200";
    public override string AircraftCode => "BBS_757";

    /// <summary>The section and panel the read-only status rows are shown under.</summary>
    public const string StatusSection = "Status";

    /// <summary>The calc-path probe target the FBW defs and the MD-11 register; see <see cref="BuildVariables"/>.</summary>
    public const string BridgeProbeKey = "MSFSBA_BRIDGE_PROBE";

    /// <summary>The generated control map this profile is built from.</summary>
    internal Bbs757ControlMap Map { get; }

    /// <summary>
    /// The SimConnect manager, captured by the first panel write or hotkey that hands one in
    /// (<see cref="HandleUIVariableSet"/>, <see cref="HandleHotkeyAction"/>); ProcessSimVarUpdate is
    /// not handed one. Null until then.
    /// </summary>
    internal SimConnectManager? Sim { get; private set; }

    /// <summary>Map controls by node, the variable key.</summary>
    private readonly Dictionary<string, Bbs757Control> _byNode = new(StringComparer.Ordinal);

    public BBS757Definition() : this(Bbs757ControlMap.Load()) { }

    internal BBS757Definition(Bbs757ControlMap map)
    {
        Map = map;
        foreach (var c in map.Controls)
            if (!_byNode.TryAdd(c.Node, c))
                Log.Warn("BBS757", $"Control map lists node {c.Node} twice; the second row is ignored.");
    }

    // The MCP takes exact values (docs/b757/spec.md §3.2): Ctrl+H/S/A/V open the entry windows
    // (BBS757Definition.Mcp.cs), Shift+H/S/A/V read the MCP's own variables.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    /// <summary>
    /// The 757 plays its own pilot-monitoring 80 knots, V1 and rotate (docs/b757/spec.md §4), so
    /// Takeoff Assist's "80 knots" and "100 knots" are not spoken on it.
    /// </summary>
    public override bool PlaysOwnTakeoffSpeedCallouts => true;

    /// <summary>
    /// Alt+P / Alt+N / Alt+E / Alt+S read the ADI, the EHSI and the upper and lower EICAS by AI vision
    /// on the measured instrument views; <c>BaseAircraftDefinition.HandleHotkeyAction</c> dispatches the table.
    /// </summary>
    protected override IReadOnlyList<AiDisplayRead> DisplayReads => Bbs757DisplayReads.All;

    // =================================================================================
    // Variables
    // =================================================================================

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        foreach (var c in _byNode.Values)
        {
            if (vars.ContainsKey(c.Node)) continue;   // never shadow a base var
            vars[c.Node] = ControlVariable(c);
        }

        foreach (var row in Map.Status)
        {
            if (vars.ContainsKey(row.Key)) continue;
            vars[row.Key] = StatusVariable(row);
        }

        // End-to-end MobiFlight probe target, as the MD-11 registers it. MainForm calc-writes a
        // nonce here and reads it back over the data-def path; a match is the only signal that the
        // WASM module runs our RPN, and every 757 control write is a calculator write. Registering
        // it is what opts the 757 into the probe, its verdict (CalcWriteCanLand) and the spoken
        // warning when the round trip never succeeds. OnRequest, in no panel, never announced.
        vars[BridgeProbeKey] = new SimVarDefinition
        {
            Name = BridgeProbeKey, DisplayName = "Bridge Probe",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        };

        // The MCP values the entry windows and readouts read fresh; see Bbs757Mcp.
        AddMcpVariables(vars);

        // The A/T's armed flag the Ctrl+P window shows; see BBS757Definition.Autopilot.cs.
        AddAutopilotVariables(vars);

        // The tanks and payload stations the EFB window's SimBrief load reads back; see Bbs757Loadsheet.
        // F and Shift+F read the same four.
        AddLoadsheetVariables(vars);

        // Flaps, gear, speedbrake, parking brake, the MCP lights, the engines, the altimeters, the
        // radio minimums, the gross weight and the squawk; see BBS757Definition.Readouts.cs.
        AddReadoutVariables(vars);

        Log.Info("BBS757", $"Built {vars.Count} variables from {Map.Controls.Count} controls and {Map.Status.Count} status rows.");
        return vars;
    }

    /// <summary>
    /// One map control → one OnRequest L:var keyed by its node. A button or held button renders as
    /// a button (its press is a momentary click, never a position). A button with no state var is
    /// keyed on a read of its node's own name: harmless (an unknown L:var reads 0), and it gives the
    /// press's re-read something to ask for.
    /// </summary>
    private static SimVarDefinition ControlVariable(Bbs757Control c)
    {
        bool momentary = Bbs757Write.IsMomentary(c);
        return new SimVarDefinition
        {
            Name = string.IsNullOrWhiteSpace(c.StateVar) ? c.Node : c.StateVar,
            DisplayName = c.Label,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = DescriptionsFor(c.Positions),
            RenderAsButton = momentary,
            // A momentary push has no meaningful resting value (CORE-10's opt-in).
            SuppressRestingButtonState = momentary,
        };
    }

    /// <summary>
    /// One status row → a read-only row on the Status display. An announced row rides the continuous
    /// batch and keeps its Ctrl+M row; a silent one (bus sources, APU speed, cabin altitude: bare
    /// numbers) is read while the display is shown and has no Ctrl+M row, which would mute nothing.
    /// </summary>
    private static SimVarDefinition StatusVariable(Bbs757StatusRow row) => new()
    {
        Name = row.Key,
        DisplayName = row.Label,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = row.Announce,
        ValueDescriptions = DescriptionsFor(row.Positions),
        RenderAsReadOnlyStatus = true,
        ExcludeFromMonitorManager = !row.Announce,
    };

    /// <summary>
    /// Registers the stock tank, fuel-density and payload-station variables the SimBrief load reads
    /// back (<see cref="SimConnect.BBS757.Bbs757Loadsheet.ReadBackVariables"/>), each under its own
    /// key: <c>ReadFreshAsync</c> delivers only a registered key. OnRequest, never announced, no
    /// Ctrl+M row ([VAR-9]) and on no panel; eight individual definitions.
    /// </summary>
    private static void AddLoadsheetVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var v in SimConnect.BBS757.Bbs757Loadsheet.ReadBackVariables)
        {
            if (vars.ContainsKey(v.Key)) continue;   // never shadow a map control or a base var
            vars[v.Key] = new SimVarDefinition
            {
                Name = v.SimVar, DisplayName = v.Label, Units = v.Units,
                Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
        }
    }

    private static Dictionary<double, string> DescriptionsFor(IEnumerable<Bbs757Position> positions)
    {
        var d = new Dictionary<double, string>();
        foreach (var p in positions) d.TryAdd(p.Value, p.Label);
        return d;
    }

    // =================================================================================
    // Panels
    // =================================================================================

    private Dictionary<string, List<string>>? _panelStructure;
    private Dictionary<string, List<string>>? _panelControls;
    private Dictionary<string, List<string>>? _panelDisplays;

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        BuildPanelsOnce();
        return _panelStructure!;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        BuildPanelsOnce();
        return _panelControls!;
    }

    /// <summary>The status rows, as the Status display of their panel ("Status" today).</summary>
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        BuildPanelsOnce();
        return _panelDisplays!;
    }

    /// <summary>
    /// Sections and panels from the map, in map order (the generator sorts by section, panel and
    /// cockpit file line), then the status rows. A status row whose panel is a control panel joins
    /// that panel's Status display; any other panel is a display-only panel under the Status
    /// section, with an empty controls entry so the panel builds at all ([HS-5]).
    /// </summary>
    private void BuildPanelsOnce()
    {
        if (_panelStructure != null) return;

        var structure = new Dictionary<string, List<string>>();
        var controls = new Dictionary<string, List<string>>();
        var sectionOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in _byNode.Values)
        {
            if (!controls.TryGetValue(c.Panel, out var keys))
            {
                controls[c.Panel] = keys = new List<string>();
                sectionOf[c.Panel] = c.Section;
                if (!structure.TryGetValue(c.Section, out var panels)) structure[c.Section] = panels = new List<string>();
                panels.Add(c.Panel);
            }
            else if (sectionOf[c.Panel] != c.Section)
            {
                Log.Warn("BBS757", $"Panel {c.Panel} appears in {sectionOf[c.Panel]} and {c.Section}; {c.Node} is listed under {sectionOf[c.Panel]}.");
            }
            keys.Add(c.Node);
        }

        var displays = new Dictionary<string, List<string>>();
        foreach (var row in Map.Status)
        {
            string panel = string.IsNullOrWhiteSpace(row.Panel) ? StatusSection : row.Panel;
            if (!displays.TryGetValue(panel, out var rows)) displays[panel] = rows = new List<string>();
            rows.Add(row.Key);
            if (controls.ContainsKey(panel)) continue;
            controls[panel] = new List<string>();
            if (!structure.TryGetValue(StatusSection, out var statusPanels)) structure[StatusSection] = statusPanels = new List<string>();
            statusPanels.Add(panel);
        }

        // The app's own rows and fields, after the map's (BBS757Definition.Readouts.cs).
        AddReadoutPanels(structure, controls, displays);

        _panelControls = controls;
        _panelDisplays = displays;
        _panelStructure = structure;
    }
}
