using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Ctrl+P "757 Autopilot" window: one button per MCP engage control (<see cref="Bbs757AutopilotLayout"/>),
/// each captioned with its live state. A mode or CMD button shows its MCP light (from the 1 Hz
/// batch); A/T ARM and the two F/D switches show their map position, read on request about once a
/// second while the window is shown (they are OnRequest controls, and a status row of their own
/// would cost a data definition and a Ctrl+M row nobody asked for).
///
/// A press goes through the same writer as the panel: a button replays its click code
/// (<see cref="PressAsync"/>) without speaking when its light changes (the caption's change is the
/// feedback, as in the iFly, HS787 and MD-11 windows), its light change being that press's echo; when
/// the light reads back as it was, the caption cannot change, so the press says "CMD L did not
/// engage" or "… did not disengage" (the iFly window's words). A switch is moved to its other
/// position (<see cref="SetPositionAsync"/>). Nothing that can land: "{label} unavailable".
/// </summary>
public partial class BBS757Definition
{
    private BBS757AutopilotWindow? _autopilotWindow;

    /// <summary>The window's sim and speech, the latest a Ctrl+P handed in (a reconnect hands a new manager).</summary>
    private SimConnectManager? _autopilotSim;
    private ScreenReaderAnnouncer? _autopilotAnnouncer;

    private void ShowAutopilotWindow(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _autopilotSim = sim;
        _autopilotAnnouncer = announcer;
        if (_autopilotWindow == null || _autopilotWindow.IsDisposed)
            _autopilotWindow = new BBS757AutopilotWindow(AutopilotButtons(), RequestAutopilotSwitches);
        _autopilotWindow.ShowForm();
    }

    private List<Bbs757AutopilotButton> AutopilotButtons()
    {
        var buttons = new List<Bbs757AutopilotButton>();
        foreach (string node in Bbs757AutopilotLayout.Nodes)
        {
            if (!_byNode.TryGetValue(node, out var control))
            {
                Log.Warn("BBS757", $"Autopilot window: {node} is not in the control map; its button is left out.");
                continue;
            }
            Func<string> state = Bbs757McpLights.ForNode(node) is { } light
                ? () => Bbs757McpLights.State(_autopilotSim?.GetCachedVariableValue(light.Key))
                : () => Bbs757AutopilotLayout.SwitchState(control, _autopilotSim?.GetCachedVariableValue(node));
            buttons.Add(new Bbs757AutopilotButton(control.Label, state, () =>
            {
                if (_autopilotSim is { } sim && _autopilotAnnouncer is { } announcer)
                    _ = PressFromWindowAsync(control.Node, sim, announcer);   // never throws: it catches and logs
            }));
        }
        return buttons;
    }

    /// <summary>The switches' states are read on request: ask for them (the window does, about once a second while shown).</summary>
    private void RequestAutopilotSwitches()
    {
        var sim = _autopilotSim;
        if (sim == null || !sim.IsConnected || _lifetime.IsCancellationRequested) return;
        foreach (string node in Bbs757AutopilotLayout.SwitchNodes) sim.RequestVariable(node);
    }

    /// <summary>
    /// A window button: a button presses without speaking on success (its caption changes) and says so
    /// when the aircraft refused it ("CMD L did not engage"); a switch moves to its other position.
    /// Refused aloud when nothing can land. Never throws.
    /// </summary>
    internal async Task PressFromWindowAsync(string node, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_lifetime.IsCancellationRequested || !_byNode.TryGetValue(node, out var control)) return;
        var io = WriteIoFor(sim);

        if (Bbs757Write.IsMomentary(control))
        {
            var press = Bbs757Write.PressFor(control);
            if (press == null || !io.CanWrite())
            {
                SayUnavailable(control, announcer);
                return;
            }
            await PressAsync(control, press, io, announcer, speak: false);
            return;
        }
        await FlipFromWindowAsync(control, io, announcer);
    }

    /// <summary>
    /// A switch pressed in the window: its other position, from the cache or else a fresh read, then the
    /// panel's own pick (which refuses aloud, reads fresh again and writes only what is owed).
    /// </summary>
    private async Task FlipFromWindowAsync(Bbs757Control control, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        try
        {
            double? now = io.Cached(control.Node) ?? await io.ReadFresh(control.Node, _lifetime.Token);
            if (Bbs757AutopilotLayout.FlipTarget(control, now) is not double target)
            {
                SayUnavailable(control, announcer);
                return;
            }
            await SetPositionAsync(control, target, io, announcer);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: window press failed: {ex.Message}");
        }
    }

    /// <summary>A profile switch closes the Ctrl+B and Ctrl+P windows: their buttons write through this definition, which no longer may.</summary>
    private void CloseAutoflightWindows()
    {
        try
        {
            if (_autopilotWindow != null && !_autopilotWindow.IsDisposed) _autopilotWindow.Dispose();
        }
        catch (Exception ex) { Log.Debug("BBS757", $"Autopilot window did not close: {ex.Message}"); }
        _autopilotWindow = null;
        try
        {
            if (_altimeterForm != null && !_altimeterForm.IsDisposed) _altimeterForm.Close();
        }
        catch (Exception ex) { Log.Debug("BBS757", $"Altimeter window did not close: {ex.Message}"); }
        _altimeterForm = null;
    }
}
