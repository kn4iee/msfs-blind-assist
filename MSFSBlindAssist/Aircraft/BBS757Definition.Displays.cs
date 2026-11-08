using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's displays read from BBS752.wasm's memory (plan 23, S2): the definition owns the one module session every
/// reader shares (WASM-11), the tablet window's EFB read included, and every reader's monitor. MainForm starts them on a
/// new session (<see cref="StartDisplays"/>: at startup, after a switch to the 757, and when the simulator changes); a
/// flight reload or a dropped connection invalidates the session and restarts them (<see cref="ResetDisplays"/>, from
/// OnSimContextReset); a switch away or the app's exit stops them and disposes the session (<see cref="StopDisplays"/>,
/// from OnSwitchedAway, before the tablet client goes).
///
/// The readers are listed once, in <see cref="Displays"/>, on its first use: the FMA registers itself, then each later
/// plan's reader through its seam, in order (S16). So Ctrl+M (<see cref="MonitorRows"/>, the readers' rows last) and the
/// hotkeys (<see cref="HandleHotkeyAction"/>, the readers' keys first) see every reader before the readers start, and a
/// monitor not started yet sends its key to the AI read.
///
/// Each reader's gate compares memory with BlueBird's own variables (S3), read from SimConnect's batch cache: a gate
/// variable no batched variable carries yet gets a hidden batched copy (<see cref="AddDisplayVariables"/>), consumed
/// silently (<see cref="IsDisplayGateCopy"/>), never a second batched variable of one name ([VAR-7]).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The key prefix of a gate variable's hidden batched copy.</summary>
    internal const string DisplayGateCopyPrefix = "BBS757_DISPLAY_GATE_";

    /// <summary>The readers' gate variables (BlueBird's L:vars) that must ride the batch.</summary>
    private static IEnumerable<string> DisplayGateVariables => Bbs757FmaDisplay.GateKeys;

    /// <summary>The flight mode annunciator, spoken as it changes and listed first in the Ctrl+P window.</summary>
    internal Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> Fma { get; } = new(new Bbs757FmaDisplay());

    /// <summary>The session every 757 reader reads through, the tablet's EFB read included; null until
    /// <see cref="StartDisplays"/> and after a switch away.</summary>
    internal ModuleSession? DisplaySession { get; private set; }

    /// <summary>Starts a monitor on its environment; replaced by tests (the default starts its WinForms timer).</summary>
    internal Action<IBbs757DisplayMonitor, IBbs757DisplayEnvironment> StartMonitor { get; init; } =
        static (monitor, env) => monitor.Start(env);

    /// <summary>Test seam: stands in for the base's AI read when a reader's key falls back to it.</summary>
    internal Func<HotkeyAction, bool>? AiReadOverride { get; set; }

    private Bbs757DisplayRegistry? _displays;

    // Set by a switch away: the readers never start again on this definition.
    private bool _displaysRetired;

    /// <summary>Every 757 display reader, registered on first use.</summary>
    internal Bbs757DisplayRegistry Displays => _displays ??= RegisterDisplays();

    private Bbs757DisplayRegistry RegisterDisplays()
    {
        var r = new Bbs757DisplayRegistry();
        r.Add(Fma);
        r.AddMonitorRow(new MonitorRow(Bbs757FmaDisplay.MuteKey, Bbs757FmaDisplay.MonitorRowLabel));
        RegisterUpperEicas(r);   // plan 24
        RegisterLowerEicas(r);   // plan 25
        RegisterEhsi(r);         // plan 26, then 28
        RegisterAdi(r);          // plan 27
        return r;
    }

    // The later readers' seams (S16): each plan implements its own in a partial of its own.
    partial void RegisterUpperEicas(Bbs757DisplayRegistry r);
    partial void RegisterLowerEicas(Bbs757DisplayRegistry r);
    partial void RegisterEhsi(Bbs757DisplayRegistry r);
    partial void RegisterAdi(Bbs757DisplayRegistry r);

    /// <summary>
    /// A new module session over <paramref name="host"/> and <paramref name="layouts"/> (the old one is disposed: a
    /// reader's lease keeps its handle until it lets go), with one environment, and so one files notice (S14), for every
    /// reader; then every monitor starts on it, already running ones again. Nothing after a switch away. On the UI thread.
    /// </summary>
    internal void StartDisplays(IModuleHost host, ILayoutSource layouts, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_displaysRetired) return;
        DisplaySession?.Dispose();
        var session = DisplaySession = new ModuleSession(DisplayLayouts.Bbs757Module.Description, host, layouts);
        var env = new Bbs757DisplayEnvironment(sim, announcer, session, new Bbs757DisplayFilesNotice(),
            DisplayCacheKeys(GetVariables()));
        foreach (var monitor in Displays.Monitors)
        {
            try { StartMonitor(monitor, env); }
            catch (Exception ex) { Log.Error("BBS757", "A display reader did not start.", ex); }
        }
    }

    /// <summary>A flight reload or a dropped connection: the session forgets the memory it located (and an analysis that
    /// found nothing), and every monitor searches again, keeping its layouts; the new flight's first reading is a baseline.</summary>
    private void ResetDisplays()
    {
        DisplaySession?.Invalidate();
        foreach (var monitor in Displays.Monitors)
        {
            try { monitor.Restart(); }
            catch (Exception ex) { Log.Error("BBS757", "A display reader did not restart.", ex); }
        }
    }

    /// <summary>A switch away or the app's exit: every monitor stops (its timer and leases), then the session goes, and the
    /// readers never start again on this definition. Never throws.</summary>
    private void StopDisplays()
    {
        _displaysRetired = true;
        foreach (var monitor in Displays.Monitors)
        {
            try { monitor.Stop(); }
            catch (Exception ex) { Log.Error("BBS757", "A display reader did not stop.", ex); }
        }
        try { DisplaySession?.Dispose(); }
        catch (Exception ex) { Log.Error("BBS757", "The display readers' module session did not close.", ex); }
        DisplaySession = null;
    }

    /// <summary>A reader's key, offered before anything else the definition does with it; its fallback is the base's AI
    /// read of the same key (S15).</summary>
    private bool HandleDisplayKey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager) =>
        Displays.TryHandle(action, new Bbs757KeyContext(simConnect, announcer, parentForm, hotkeyManager,
            () => AiReadOverride?.Invoke(action) ?? base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager)));

    /// <summary>
    /// Each gate variable's name, to the key of the batched variable whose cached value it is: an L:var on the
    /// continuous batch (<see cref="ContinuousBatchLayout.RidesBatch"/>), of which there is one per name ([VAR-7]).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> DisplayCacheKeys(IEnumerable<KeyValuePair<string, SimVarDefinition>> vars)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, def) in vars)
            if (def.Type == SimVarType.LVar && ContinuousBatchLayout.RidesBatch(def)) keys.TryAdd(def.Name, key);
        return keys;
    }

    /// <summary>
    /// Plan 23 Task 7 Step 1: a gate variable no Continuous batched variable carries yet gets a hidden batched copy (Continuous and
    /// IsAnnounced, which is what puts it on the batch; no Ctrl+M row, [VAR-9]); one that some variable already carries on
    /// the batch gets none, as two batched variables of one name shift every later slot ([VAR-7]). Called last in
    /// <see cref="BuildVariables"/>, so it sees every other registration. Today the F/D switches, on-request map controls,
    /// get copies; the MCP's altitude and heading ride the batch for the autoflight call-outs.
    /// </summary>
    private static void AddDisplayVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (string name in DisplayGateVariables)
        {
            bool batched = vars.Values.Any(v => v.Name == name
                && v.UpdateFrequency == UpdateFrequency.Continuous && !v.ExcludeFromBatch);
            if (batched) continue;
            vars.TryAdd(DisplayGateCopyPrefix + name, new SimVarDefinition
            {
                Name = name, DisplayName = $"{name} (display reader gate)",
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true, ExcludeFromMonitorManager = true,
            });
        }
    }

    /// <summary>A gate variable's hidden copy: consumed silently by ProcessSimVarUpdate, read from the cache.</summary>
    private static bool IsDisplayGateCopy(string varName) => varName.StartsWith(DisplayGateCopyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The tablet window's read of BlueBird's EFB control tree, through <see cref="DisplaySession"/> (one search for every
    /// reader, WASM-11): the build whose EFB layout the generator wrote, its memory walked read-only through that layout
    /// (WASM-1, WASM-2). No session (before the readers start, after a switch away) and no located build are "aircraft
    /// memory not found", a build without a current EFB layout "BlueBird file not recognised", and the session's own
    /// failures are worded for the 757. Runs on the bridge's loop, off the UI thread; never throws.
    /// </summary>
    internal async Task<Bbs757EfbModelRead> ReadEfbModelAsync()
    {
        try
        {
            if (DisplaySession is not { } session) return Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound);
            var location = await session.LocateAsync();
            var module = location.Found.FirstOrDefault(m =>
                m.Build.Layout<Bbs757EfbMemLayout>(DisplayLayouts.Bbs757Module.Efb) is not null);
            if (module is null)
            {
                // The session's reasons are worded for the PMDG 737; the window says the 757's own.
                string reason = location.Failure is { } failure
                    ? Bbs757DisplayReasons.FromSession(failure)
                    : location.Found.Count > 0 ? Bbs757DisplayReasons.FileNotRecognised : Bbs757DisplayReasons.MemoryNotFound;
                return Bbs757EfbModelRead.Unreadable(reason);
            }
            var layout = module.Build.Layout<Bbs757EfbMemLayout>(DisplayLayouts.Bbs757Module.Efb)!;
            using var lease = module.AcquireLease();
            var memory = new Bbs757EfbMemory(new ModuleEfbMemory(module.Memory), layout);
            var model = new Bbs757EfbModelReader(memory).Read();
            return model is null ? Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound) : new Bbs757EfbModelRead(model, null);
        }
        catch (ObjectDisposedException)
        {
            // The session went with a switch away or a simulator change while this read ran.
            return Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound);
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", "EFB memory read failed: " + ex.Message);
            return Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.InternalError);
        }
    }
}
