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
    private readonly Dictionary<HotkeyAction, Func<Bbs757KeyContext, bool>> _keys = new();

    /// <summary>Every reader's monitor, in registration order (the FMA first).</summary>
    public IReadOnlyList<IBbs757DisplayMonitor> Monitors => _monitors;

    /// <summary>Every reader's Ctrl+M rows, in registration order.</summary>
    public IReadOnlyList<MonitorRow> Rows => _rows;

    public void Add(IBbs757DisplayMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitors.Add(monitor);
    }

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
