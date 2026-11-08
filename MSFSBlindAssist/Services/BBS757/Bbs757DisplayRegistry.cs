using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// What a 757 display reader's key handler is handed: the definition's hotkey arguments, and <see cref="AiRead"/>, which
/// runs the base's AI read of the same key (S15: the fallback when the reader cannot run).
/// </summary>
internal readonly record struct Bbs757KeyContext(SimConnectManager Sim, ScreenReaderAnnouncer Announcer, Form Parent,
    HotkeyManager Hotkeys, Func<bool> AiRead);

/// <summary>
/// The 757's display readers, as <c>BBS757Definition</c> registers them (the FMA itself, then one seam per later reader,
/// S16): their monitors, which the definition starts on its one module session, restarts on a flight reload and stops on a
/// switch away; their Ctrl+M rows, listed last in the 757's Monitor Manager; and their keys, which the definition offers
/// a reader before anything else it does with a key. Filled once, on the UI thread, and read there.
/// </summary>
internal sealed class Bbs757DisplayRegistry
{
    private readonly List<IBbs757DisplayMonitor> _monitors = new();
    private readonly List<MonitorRow> _rows = new();
    private readonly List<Bbs757GateVariable> _gates = new();
    private readonly Dictionary<HotkeyAction, Func<Bbs757KeyContext, bool>> _keys = new();

    /// <summary>Every reader's monitor, in registration order (the FMA first).</summary>
    public IReadOnlyList<IBbs757DisplayMonitor> Monitors => _monitors;

    /// <summary>Every reader's Ctrl+M rows, in registration order.</summary>
    public IReadOnlyList<MonitorRow> Rows => _rows;

    /// <summary>Every reader's gate variables (its monitor's <see cref="IBbs757DisplayMonitor.GateVariables"/>), each key
    /// once, in registration order: the definition puts each on the continuous batch and reads it from there.</summary>
    public IReadOnlyList<Bbs757GateVariable> GateVariables => _gates;

    /// <summary>
    /// A reader's monitor, with the gate variables its reader declares (taken from the monitor, so they can never differ
    /// from the reader's). A key two readers share must name the same variable; a gate variable must be an L:var or a
    /// stock variable with its units. Either mistake is a wiring error, so it throws, and registers nothing of the reader.
    /// </summary>
    public void Add(IBbs757DisplayMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var gates = monitor.GateVariables ?? throw new ArgumentException("the monitor has no gate variables list", nameof(monitor));
        var added = new List<Bbs757GateVariable>();
        foreach (var gate in gates)
        {
            Check(gate);
            var known = _gates.Concat(added).Where(g => string.Equals(g.Key, gate.Key, StringComparison.Ordinal)).ToList();
            if (known.Count == 0) added.Add(gate);
            else if (!known[0].SameVariable(gate))
                throw new InvalidOperationException(
                    $"757 display gate key {gate.Key} is already {Describe(known[0])}, not {Describe(gate)}.");
        }
        _gates.AddRange(added);
        _monitors.Add(monitor);
    }

    private static void Check(Bbs757GateVariable gate)
    {
        if (string.IsNullOrEmpty(gate.Key) || string.IsNullOrEmpty(gate.Name))
            throw new ArgumentException("a 757 display gate variable needs a key and a name");
        if (gate.Type is not (SimVarType.LVar or SimVarType.SimVar))
            throw new ArgumentException($"757 display gate variable {gate.Key} is a {gate.Type}: only an L:var or a stock variable");
        if (gate.Type == SimVarType.SimVar && string.IsNullOrEmpty(gate.Units))
            throw new ArgumentException($"757 display gate variable {gate.Key} is a stock variable with no units");
    }

    private static string Describe(Bbs757GateVariable gate) =>
        gate.Type == SimVarType.LVar ? $"L:{gate.Name}" : $"{gate.Name} in {gate.Units}";

    /// <summary>The handler of <paramref name="action"/>: true when it took the key. One per key: two readers on one key
    /// is a wiring error, so a second throws.</summary>
    public void OnKey(HotkeyAction action, Func<Bbs757KeyContext, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_keys.TryAdd(action, handler))
            throw new InvalidOperationException($"A 757 display reader already handles {action}.");
    }

    public void AddMonitorRow(MonitorRow row) => _rows.Add(row);

    /// <summary>Runs the key's handler: true when there is one and it took the key; false leaves the key to the
    /// definition.</summary>
    public bool TryHandle(HotkeyAction action, Bbs757KeyContext context) =>
        _keys.TryGetValue(action, out var handler) && handler(context);
}
