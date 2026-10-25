using System.Collections.Concurrent;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The MCP's heading, speed, altitude and vertical speed: the value windows (Ctrl+H/S/A/V in input
/// mode, the HS787's modeless <see cref="ValueInputForm"/>, with the mode buttons that go with each
/// value, as the PMDG 737's windows have) and the readouts (Shift+H/S/A/V in output mode). How each
/// value is set and why is in <see cref="Bbs757Mcp"/>; this partial is the sim side.
///
/// Every set runs from the UI thread (the window's callback), refuses aloud when the calculator
/// cannot land a write ([MD11-16]: the MCP is written only through
/// <see cref="SimConnectManager.ExecuteCalculatorCodeUnique"/>, never SendEvent, so [SIM-20] has
/// nothing to say here), never blocks (the waits are awaited), and checks the profile's lifetime
/// before it writes. A newer set of the same value supersedes one still in flight, so two windows
/// of corrections never fight over one variable. What is spoken is the value read back (CORE-7:
/// a numeric confirmation always speaks).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>One set in flight: its token source and the value it is setting.</summary>
    private sealed record McpSetInFlight(CancellationTokenSource Cts, int Value);

    /// <summary>
    /// The set in flight per MCP variable; a newer set of the same variable cancels it, except a
    /// repeat of the same speed (see <see cref="RunMcpSetAsync"/>).
    /// </summary>
    private readonly ConcurrentDictionary<string, McpSetInFlight> _mcpSets = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a set can land; replaced by tests at construction (init only), since a
    /// SimConnectManager cannot be connected in them. Nothing can swap it once the definition runs.
    /// </summary>
    internal Func<SimConnectManager, bool> CanWrite { get; init; } = sim => sim.CalcWriteCanLand;

    /// <summary>The open value windows, one per kind: a second hotkey press brings it forward, never stacks another.</summary>
    private readonly Dictionary<HotkeyAction, ValueInputForm> _mcpDialogs = new();

    private static readonly (string Key, string Label)[] McpVariables =
    {
        (Bbs757Mcp.HeadingVar, "MCP Heading"),
        (Bbs757Mcp.AltitudeVar, "MCP Altitude"),
        (Bbs757Mcp.SpeedVar, "MCP Speed"),
        (Bbs757Mcp.VsVar, "MCP Vertical Speed"),
        (Bbs757Mcp.MachModeVar, "MCP Speed Mach Mode"),
    };

    /// <summary>
    /// Registers the MCP variables as OnRequest L:vars: <c>ReadFreshAsync</c> delivers only a
    /// registered key. Read for the readouts and the set loops only, so they carry no Ctrl+M row
    /// ([VAR-9]) and sit on no panel.
    /// </summary>
    private static void AddMcpVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var (key, label) in McpVariables)
        {
            if (vars.ContainsKey(key)) continue;   // never shadow a map control or a base var
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = label,
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
        }
    }

    /// <summary>The MCP sets' and readouts' sim access, replaced by tests (a SimConnectManager cannot read in them); null: the real one.</summary>
    internal Func<SimConnectManager, McpIo>? McpSetIo { get; init; }

    private McpIo McpIoFor(SimConnectManager sim) => McpSetIo?.Invoke(sim) ?? new McpIo
    {
        Read = (key, ct) => sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct),
        Write = sim.ExecuteCalculatorCodeUnique,
        Delay = Task.Delay,
    };

    // =================================================================================
    // Value windows
    // =================================================================================

    private delegate bool McpParser(string? input, out int value, out string? error);

    /// <summary>What one value window is: its words, its validator and what an accepted value does.</summary>
    private sealed record McpEntry(string Title, string Parameter, string Range, McpParser Parse,
        Func<int, Task> Apply);

    private McpEntry EntryFor(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer) => action switch
    {
        HotkeyAction.FCUSetHeading => new("MCP Heading", "heading", "0-360 (360 is north)", Bbs757Mcp.TryHeadingInput,
            v => SetMcpHeadingAsync(v, sim, announcer)),
        HotkeyAction.FCUSetSpeed => new("MCP Speed", "speed", "100-399 knots", Bbs757Mcp.TrySpeedInput,
            v => SetMcpSpeedAsync(v, sim, announcer)),
        HotkeyAction.FCUSetAltitude => new("MCP Altitude", "altitude", "100-49000 feet, rounded to 100", Bbs757Mcp.TryAltitudeInput,
            v => SetMcpAltitudeAsync(v, sim, announcer)),
        HotkeyAction.FCUSetVS => new("MCP Vertical Speed", "vertical speed", "-8000 to 6000 feet per minute, rounded to 100",
            Bbs757Mcp.TryVerticalSpeedInput, v => SetMcpVerticalSpeedAsync(v, sim, announcer)),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not an MCP value window"),
    };

    /// <summary>
    /// Opens the modeless value window for <paramref name="action"/>, the HS787's way (the caller
    /// has left input hotkey mode first): Enter sets the value and the window stays open for the next.
    /// </summary>
    private void ShowMcpDialog(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (_mcpDialogs.TryGetValue(action, out var open) && !open.IsDisposed)
        {
            open.Activate();
            return;
        }

        var entry = EntryFor(action, sim, announcer);
        var dialog = new ValueInputForm(entry.Title, entry.Parameter, entry.Range, announcer,
            input => entry.Parse(input, out _, out string? error) ? (true, "") : (false, error ?? ""),
            McpWindowButtons(action, sim, announcer),
            input =>
            {
                // The validator has accepted this text; parsing again is the only way the callback gets the number.
                if (entry.Parse(input, out int value, out _)) _ = entry.Apply(value);
            });

        dialog.ShowCancelButton = false;
        dialog.FormClosed += (_, _) =>
        {
            if (_mcpDialogs.TryGetValue(action, out var current) && ReferenceEquals(current, dialog))
                _mcpDialogs.Remove(action);
        };
        _mcpDialogs[action] = dialog;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// The mode buttons of one value window (<see cref="Bbs757McpWindowLayout"/>), each with its map
    /// label and an access key, captioned "LNAV: On" from its cached MCP light, or "HDG SEL" alone for a
    /// button with no light (the window refreshes the captions). A press is the panel's
    /// (<see cref="PressFromPanelAsync"/>): it reads the light back once ("LNAV on") or says "HDG SEL
    /// pressed", or refuses aloud when nothing can land. The window's own read-out after a press is
    /// suppressed, or the press would be said twice (CORE-7, ruling R34). A node missing from the map
    /// is left out with a warning.
    /// </summary>
    internal List<ToggleButtonDef> McpWindowButtons(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var controls = new List<Bbs757Control>();
        foreach (string node in Bbs757McpWindowLayout.NodesFor(action))
        {
            if (_byNode.TryGetValue(node, out var control)) controls.Add(control);
            else Log.Warn("BBS757", $"MCP window: {node} is not in the control map; its button is left out.");
        }

        var io = WriteIoFor(sim);
        var captions = Bbs757McpWindowLayout.Captions(controls.Select(c => c.Label).ToList());
        var buttons = new List<ToggleButtonDef>();
        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            Func<string> state = Bbs757McpLights.ForNode(control.Node) is { } light
                ? () => Bbs757McpLights.State(io.Cached(light.Key))
                : () => "";
            buttons.Add(new ToggleButtonDef(captions[i], state,
                () => _ = PressFromPanelAsync(control, io, announcer))   // never throws: it catches and logs
            {
                SuppressStateAnnounce = () => true,
            });
        }
        return buttons;
    }

    /// <summary>A profile switch closes the windows: their callbacks write through this definition, which no longer may.</summary>
    private void CloseMcpDialogs()
    {
        foreach (var dialog in _mcpDialogs.Values.ToList())
        {
            try { if (!dialog.IsDisposed) dialog.Close(); }
            catch (Exception ex) { Log.Debug("BBS757", $"MCP window did not close: {ex.Message}"); }
        }
        _mcpDialogs.Clear();
    }

    // =================================================================================
    // Sets
    // =================================================================================

    internal Task SetMcpHeadingAsync(int degrees, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.HeadingVar, degrees, ignoreSameValueInFlight: false, Bbs757Mcp.HeadingPhrase(null), sim, announcer,
            (io, ct) => Bbs757Mcp.SetHeadingAsync(io, degrees, ct));

    /// <summary>
    /// A speed set is the only slow one (steps, settle, corrections: seconds), and Enter pressed again
    /// on the same speed would cancel it and start the closed loop over; the one in flight is
    /// already going there and will speak, so the repeat is ignored. A different speed supersedes.
    /// </summary>
    internal Task SetMcpSpeedAsync(int knots, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.SpeedVar, knots, ignoreSameValueInFlight: true, Bbs757Mcp.SpeedPhrase(null), sim, announcer,
            (io, ct) => SetSpeedSpacedAsync(io, knots, ct));

    /// <summary>When the last speed burst went out, on <see cref="NowMs"/>, whichever set sent it; null before the first.</summary>
    private long? _lastSpeedBurstMs;

    /// <summary>
    /// A speed set kept out of the burst before it. BlueBird counts one burst across writes less than
    /// about 0.3 s apart (<see cref="Bbs757Mcp"/>), and a set superseded just after its burst leaves that
    /// burst counting: a newer set's first write inside <see cref="Bbs757Mcp.SpeedBurstGapMs"/> of it would
    /// move 2 or 5 kt an event. So the rest of that gap is waited out first, before the target is read,
    /// which also lets the last burst land; and every speed write of this set is timed for the next one.
    /// </summary>
    private async Task<string> SetSpeedSpacedAsync(McpIo io, int knots, CancellationToken ct)
    {
        if (_lastSpeedBurstMs is long last)
        {
            long wait = Math.Clamp(Bbs757Mcp.SpeedBurstGapMs - (NowMs() - last), 0, Bbs757Mcp.SpeedBurstGapMs);
            if (wait > 0) await io.Delay((int)wait, ct);
        }
        var timed = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                _lastSpeedBurstMs = NowMs();
            },
        };
        return await Bbs757Mcp.SetSpeedAsync(timed, knots, ct);
    }

    internal Task SetMcpAltitudeAsync(int feet, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.AltitudeVar, feet, ignoreSameValueInFlight: false, Bbs757Mcp.AltitudePhrase(null), sim, announcer,
            (io, ct) => Bbs757Mcp.SetAltitudeAsync(io, feet, ct));

    internal Task SetMcpVerticalSpeedAsync(int fpm, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.VsVar, fpm, ignoreSameValueInFlight: false, Bbs757Mcp.VsPhrase(null), sim, announcer,
            (io, ct) => Bbs757Mcp.SetVerticalSpeedAsync(io, fpm, ct));

    /// <summary>
    /// One set: refuse aloud when nothing can land ("{value} unavailable", nothing written), else run
    /// the loop and speak its result unless a newer set, a flight load or a profile switch has
    /// superseded it. A repeat of the value already being set is ignored when
    /// <paramref name="ignoreSameValueInFlight"/>; a different value supersedes the one in flight.
    /// Whatever is spoken also goes to <paramref name="onSaid"/> (the altimeter window's status box).
    /// Never throws: it is fire-and-forget from a window callback.
    /// </summary>
    internal async Task RunMcpSetAsync(string key, int value, bool ignoreSameValueInFlight, string refusal,
        SimConnectManager sim, ScreenReaderAnnouncer announcer, Func<McpIo, CancellationToken, Task<string>> set,
        Action<string>? onSaid = null)
    {
        void Say(string text)
        {
            announcer.AnnounceImmediate(text);
            onSaid?.Invoke(text);
        }

        if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is written
        if (_mcpSets.TryGetValue(key, out var inFlight))
        {
            if (ignoreSameValueInFlight && inFlight.Value == value && !inFlight.Cts.IsCancellationRequested)
            {
                Log.Debug("BBS757", $"{key}: {value} is already being set; the repeat is ignored.");
                return;
            }
            try { inFlight.Cts.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var mine = new McpSetInFlight(cts, value);
        _mcpSets[key] = mine;
        var ct = cts.Token;

        try
        {
            if (!CanWrite(sim))
            {
                Log.Debug("BBS757", $"{key}: write refused, nothing sent.");
                Say(refusal);
                return;
            }

            string said = await set(McpIoFor(sim), ct);
            ct.ThrowIfCancellationRequested();
            Say(said);
        }
        catch (OperationCanceledException) { /* superseded, reloaded, or switched away: nothing to say */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{key}: set failed: {ex.Message}");
            Say(refusal);   // whatever was sent, the pilot is not left waiting for a value
        }
        finally
        {
            _mcpSets.TryRemove(new KeyValuePair<string, McpSetInFlight>(key, mine));
            cts.Dispose();
        }
    }

    /// <summary>Cancels every set in flight: one armed before a flight load must not write into the flight that follows.</summary>
    private void CancelMcpSets()
    {
        foreach (var inFlight in _mcpSets.Values)
        {
            try { inFlight.Cts.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
    }

    // =================================================================================
    // Readouts
    // =================================================================================

    /// <summary>
    /// Shift+H / S / A / V: reads the MCP's own variable fresh and speaks it. The simulator's
    /// autopilot values are never read; they sit at unrelated offsets from BlueBird's MCP.
    /// </summary>
    internal async Task ReadMcpAsync(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var io = McpIoFor(sim);

            string said = action switch
            {
                HotkeyAction.ReadHeading => await Bbs757Mcp.ReadHeadingAsync(io, ct),
                HotkeyAction.ReadSpeed => await Bbs757Mcp.ReadSpeedAsync(io, ct),
                HotkeyAction.ReadAltitude => await Bbs757Mcp.ReadAltitudeAsync(io, ct),
                HotkeyAction.ReadFCUVerticalSpeedFPA => await Bbs757Mcp.ReadVerticalSpeedAsync(io, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not an MCP readout"),
            };
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(said);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{action}: read failed: {ex.Message}");
            announcer.AnnounceImmediate(UnavailablePhrase(action));   // a readout never goes silent
        }
    }

    private static string UnavailablePhrase(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadHeading => Bbs757Mcp.HeadingPhrase(null),
        HotkeyAction.ReadSpeed => Bbs757Mcp.SpeedPhrase(null),
        HotkeyAction.ReadAltitude => Bbs757Mcp.AltitudePhrase(null),
        _ => Bbs757Mcp.VsPhrase(null),
    };
}
