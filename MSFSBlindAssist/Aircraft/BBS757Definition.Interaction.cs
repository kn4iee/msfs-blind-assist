using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel writes, the BBS Nest warning and the hotkeys.
///
/// Speech follows CORE-7: a toggle or switch pick says nothing of its own (the screen reader read
/// the combo, and the re-read after the write moves the combo to where the control really is); a
/// button press, which has no readable state, is confirmed once with "{label} pressed"; a write
/// that cannot be made says "{label} unavailable" and sends nothing.
///
/// Every write runs on the UI thread: MainForm's panel handlers call in there, and the awaits below
/// keep its context (no ConfigureAwait(false)), so the calculator calls, the re-read and the speech
/// all go out from the UI thread. Nothing here blocks it: the waits are awaited, never slept.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>Spoken once per connection and flight load when BBS Nest is not running.</summary>
    public const string NestMissingWarning =
        "BBS Nest is not running. The 757's systems will not respond until it runs and the flight is reloaded.";

    /// <summary>
    /// How long a positional pick waits before it acts. Arrowing through a closed combo commits each
    /// row it passes; only the row it settles on is written, and only it is refused aloud (the
    /// MD-11's debounce, for the same reason: concurrent writes to one control step it twice).
    /// </summary>
    private const int PickSettleMs = 300;

    /// <summary>The ceiling on the fresh read a positional write starts from (an individual def answers within a frame or two).</summary>
    private const int FreshReadTimeoutMs = 1500;

    /// <summary>
    /// How long after a write the control is re-read, so its combo shows the aircraft's own state:
    /// the cockpit model takes a click at once, but a write it refused shows only once read back.
    /// </summary>
    private const int ReadBackDelayMs = 1500;

    /// <summary>
    /// Canceled (and disposed) when the pilot switches to another profile: no pending pick or press
    /// may write after that. Every user checks <c>IsCancellationRequested</c> before it touches the
    /// token, which is safe on a disposed source.
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// The batch-covered variable whose batch delivery runs the BBS Nest check while it is pending
    /// (<see cref="DeferredFlushWatchVariable"/>). A base variable, so it is registered on every
    /// profile and rides the 1 Hz continuous batch, which is delivered every second whether or not a
    /// value changed: a quiet cold-and-dark cockpit still gets its check within about a second.
    /// </summary>
    internal const string NestCheckWatchKey = "SIM_ON_GROUND";

    /// <summary>The pick in flight per node; a newer pick of the same control cancels it.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _pendingPicks =
        new(StringComparer.Ordinal);

    /// <summary>Whether BBS Nest is running, asked once per connection; replaced by tests.</summary>
    internal Func<bool> NestRunning { get; set; } = () => Bbs757Nest.IsRunning();

    // Reset by OnSimContextReset and fresh on construction, so the check runs once per connection
    // and per flight load. Null: not asked yet.
    private bool? _nestRunning;
    private bool _nestSettled;

    // =================================================================================
    // Writes
    // =================================================================================

    /// <summary>
    /// Every map control is written here, and always answered "handled", refusals included, so the
    /// generic path never sends its own L:var write. Any other key goes to the base.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (varKey == Bbs757Squawk.SetKey)
        {
            Sim = simConnect;
            _ = SetSquawkAsync(value, simConnect, announcer);   // never throws: it catches and logs
            return true;
        }
        if (!_byNode.TryGetValue(varKey, out var control))
            return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
        Sim = simConnect;
        var io = WriteIoFor(simConnect);

        if (Bbs757Write.IsMomentary(control))
        {
            _ = PressFromPanelAsync(control, io, announcer);   // never throws: it catches and logs
            return true;
        }

        _ = SetPositionAsync(control, value, io, announcer);
        return true;
    }

    /// <summary>
    /// A button pressed from a panel or from an MCP value window's button (BBS757Definition.Mcp.cs):
    /// one press per pilot action, so the refusal is judged here, once ("{label} unavailable", nothing
    /// sent); otherwise <see cref="PressAsync"/> with speech, which reads the light back once ("LNAV
    /// on") or says "{label} pressed". Never throws.
    /// </summary>
    private Task PressFromPanelAsync(Bbs757Control control, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        var press = Bbs757Write.PressFor(control);
        if (press == null || !io.CanWrite())
        {
            SayUnavailable(control, announcer);
            return Task.CompletedTask;
        }
        return PressAsync(control, press, io, announcer, speak: true);
    }

    /// <summary>The panel writes' sim access, replaced by tests (a SimConnectManager cannot read in them); null: the real one.</summary>
    internal Func<SimConnectManager, Bbs757SimIo>? WriteIo { get; init; }

    private Bbs757SimIo WriteIoFor(SimConnectManager sim) => WriteIo?.Invoke(sim) ?? new Bbs757SimIo
    {
        CanWrite = () => CanWrite(sim),
        ReadFresh = (key, ct) => sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct),
        Cached = sim.GetCachedVariableValue,
        Write = sim.ExecuteCalculatorCodeUnique,
        RequestRead = key => sim.RequestVariable(key, forceUpdate: true),
        Delay = Task.Delay,
    };

    private static bool SayUnavailable(Bbs757Control control, ScreenReaderAnnouncer announcer)
    {
        Log.Debug("BBS757", $"{control.Node}: write refused, nothing sent.");
        announcer.Announce($"{control.Label} unavailable");
        return true;
    }

    /// <summary>
    /// A button: press, hold, release, then confirm it once (CORE-7). A button that moves a state row
    /// (an MCP button's light, a speedbrake spot's lever: <see cref="LinkedStatusKey"/>) reads that row
    /// back after <see cref="ReadBackDelayMs"/> and says it ("CMD R on", "Speedbrake armed"); its
    /// change in between is this press's echo and is not announced. Any other button with no readable
    /// state says "{label} pressed". With <paramref name="speak"/> false (the autopilot window, whose
    /// button caption shows the new state) a light that changed says nothing, but one that reads back
    /// as it was before the press says so once ("CMD L did not engage", "LNAV did not disengage"): the
    /// caption did not change, so nothing else would. The echo is settled either way.
    /// The release is never canceled: a press is always let go, even across a profile switch.
    /// </summary>
    private async Task PressAsync(Bbs757Control control, Bbs757Press press, Bbs757SimIo io,
        ScreenReaderAnnouncer announcer, bool speak)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is pressed
            string? linked = LinkedStatusKey(control.Node);
            // The state the window's caption shows now, to tell a press the aircraft refused.
            double? before = linked != null && !speak && Bbs757McpLights.ForKey(linked) != null ? io.Cached(linked) : null;
            if (linked != null) ArmOwnWriteEcho(linked);
            io.Write(press.Down);
            if (press.Up != null)
            {
                await io.Delay(press.HoldMs, CancellationToken.None);
                io.Write(press.Up);
            }
            if (_lifetime.IsCancellationRequested) return;
            if (linked == null && speak && !Bbs757Write.HasReadableState(control)) announcer.Announce($"{control.Label} pressed");

            await io.Delay(ReadBackDelayMs, _lifetime.Token);
            io.RequestRead(control.Node);
            if (linked == null) return;

            double? state = await io.ReadFresh(linked, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            string phrase = LinkedPhrase(linked, state);
            if (state != null) SettleOwnWriteEcho(linked, phrase);
            if (speak) announcer.Announce(state != null ? phrase : $"{control.Label} pressed");
            else if (Bbs757McpLights.RefusedPhrase(control.Label, before, state) is { } refused) announcer.Announce(refused);
        }
        catch (OperationCanceledException) { /* switched away: nothing left to say or read */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: press failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A toggle, guard, select or switch: settle, refuse aloud if nothing can land, read the control
    /// fresh, send what <see cref="Bbs757Write.ToPosition"/> plans, then re-read it so the combo shows
    /// where it really is. A newer pick of the same control supersedes this one at any await.
    ///
    /// While the pick is in flight, <see cref="ProcessSimVarUpdate"/> consumes every delivery of the
    /// control: the fresh read answers with the position from BEFORE the write, and MainForm's
    /// control refresh would otherwise set the focused combo back to it, which the screen reader
    /// reads out over the pilot's pick (the MD-11's lesson, [MD11-8]). The one re-read that brings
    /// the combo current goes out after the pick has left the pending set, so it reaches the combo:
    /// silent when the control landed on the pick (WinForms ignores a same-index set), the real
    /// position once when it did not or nothing could be written.
    /// </summary>
    private async Task SetPositionAsync(Bbs757Control control, double target, Bbs757SimIo io,
        ScreenReaderAnnouncer announcer)
    {
        if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is written
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (_pendingPicks.TryGetValue(control.Node, out var prior))
        {
            try { prior.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        _pendingPicks[control.Node] = cts;
        var ct = cts.Token;
        bool resync = false;

        try
        {
            await io.Delay(PickSettleMs, ct);

            if (!io.CanWrite())
            {
                SayUnavailable(control, announcer);
                resync = true;
                return;
            }

            double? current = await io.ReadFresh(control.Node, ct);
            ct.ThrowIfCancellationRequested();

            var plan = Bbs757Write.ToPosition(control, current, target);
            switch (plan.Outcome)
            {
                case Bbs757WriteOutcome.AtPosition:
                    return;
                case Bbs757WriteOutcome.Refused:
                    Log.Debug("BBS757", $"{control.Node}: read {current?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}, " +
                        $"target {target.ToString(System.Globalization.CultureInfo.InvariantCulture)}: no safe write.");
                    SayUnavailable(control, announcer);
                    resync = true;
                    return;
            }

            // The parking brake's state row would announce this pick a second time ("Parking brake
            // set" after the screen reader read "Set"): its change is this write's echo, until the row
            // is read back below.
            string? linked = LinkedStatusKey(control.Node);
            if (linked != null) ArmOwnWriteEcho(linked);
            foreach (var call in plan.Calls) io.Write(call);

            await io.Delay(ReadBackDelayMs, ct);
            resync = true;
            if (linked != null)
            {
                // Read the row and close the echo window, as a press does: a change after this (the
                // brake snapping back to Set) is the aircraft's, and is spoken.
                double? state = await io.ReadFresh(linked, ct);
                if (state != null) SettleOwnWriteEcho(linked, LinkedPhrase(linked, state));
            }
        }
        catch (OperationCanceledException) { /* superseded by a newer pick, a reload, or switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: write failed: {ex.Message}");
            resync = true;   // whatever was or was not sent, the combo should show the aircraft's position
        }
        finally
        {
            // Only this pick's own entry: a newer pick of the control has replaced it otherwise, and
            // then the re-read is that pick's to make; a reload has cleared it, and then there is
            // nothing to re-read into.
            bool mine = _pendingPicks.TryRemove(new KeyValuePair<string, CancellationTokenSource>(control.Node, cts));
            cts.Dispose();
            if (mine && resync) io.RequestRead(control.Node);
        }
    }

    /// <summary>True while a pick of this control is in flight (its deliveries are consumed).</summary>
    internal bool IsPickPending(string varKey) => _pendingPicks.ContainsKey(varKey);

    /// <summary>
    /// Stops every pending pick and press: a profile switch must not let one write into the next
    /// aircraft. The outgoing definition is never used again, so its lifetime source goes too.
    /// Safe to call again (a switch that threw part-way, then app exit): a canceled and disposed
    /// source stays canceled, and Dispose is idempotent.
    /// </summary>
    public override void OnSwitchedAway()
    {
        base.OnSwitchedAway();
        CloseMcpDialogs();
        CloseAutoflightWindows();
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { /* switched away before */ }
        _lifetime.Dispose();
    }

    /// <summary>
    /// Cancels and forgets every pick in flight: one armed before a flight load or a dropped
    /// connection must not write into the flight that follows. Each canceled pick ends silently and
    /// makes no re-read (its entry is gone).
    /// </summary>
    private void CancelPendingPicks()
    {
        foreach (var cts in _pendingPicks.Values)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        _pendingPicks.Clear();
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>
    /// The BBS Nest check also rides any delivery that comes before its batch hook (see
    /// <see cref="DeferredFlushWatchVariable"/>). A control with a pick in flight is consumed
    /// silently (see <see cref="SetPositionAsync"/>). Everything else is the base's (the trim and
    /// glideslope call-outs) or the generic announcer's (the announced status rows).
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        WarnOnceIfNestMissing(announcer);
        if (IsPickPending(varName)) return true;
        // Flaps, gear, speedbrake, parking brake, the MCP lights and the EXEC light speak their own
        // changes (BBS757Definition.Readouts.cs); MainForm's mute wrap gates them ([VAR-8]).
        if (ProcessReadoutUpdate(varName, value, announcer)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// While the BBS Nest check of this connection or flight load is pending, MainForm calls
    /// <see cref="OnDeferredFlushBatchDelivered"/> at the end of every delivery of the batch carrying
    /// <see cref="NestCheckWatchKey"/>. The continuous batches arrive every second whether or not a
    /// value changed, so the check runs about a second after connecting, switching to this profile
    /// or loading a flight, even in a cold and dark cockpit where no variable moves (a value-change
    /// delivery alone could wait for minutes). Null once the check has settled, so the hook costs
    /// nothing for the rest of the session.
    /// </summary>
    public override string? DeferredFlushWatchVariable => _nestSettled ? null : NestCheckWatchKey;

    /// <summary>
    /// The batch carrying <see cref="NestCheckWatchKey"/> has been dispatched: run the BBS Nest
    /// check. This runs outside MainForm's Ctrl+M wrap, which is right: the warning is no row's, so
    /// no mute applies to it.
    /// </summary>
    public override void OnDeferredFlushBatchDelivered(ScreenReaderAnnouncer announcer)
        => WarnOnceIfNestMissing(announcer);

    /// <summary>
    /// Asks once whether BBS Nest runs and, when it does not, says so once. The warning is no
    /// Ctrl+M row's, so a delivery MainForm has wrapped in <c>announcer.Suppressed</c> (a muted row,
    /// a UI-set echo) does not use it up: it is said on the next delivery or batch instead.
    /// </summary>
    private void WarnOnceIfNestMissing(ScreenReaderAnnouncer announcer)
    {
        if (_nestSettled) return;
        _nestRunning ??= NestRunning();
        if (_nestRunning == true)
        {
            _nestSettled = true;
            return;
        }
        if (announcer.Suppressed) return;

        _nestSettled = true;
        Log.Warn("BBS757", $"{Bbs757Nest.ProcessName} is not running; the 757's systems will not respond.");
        announcer.Announce(NestMissingWarning);
    }

    /// <summary>
    /// A flight load or a dropped connection: BBS Nest is asked about again on the next batch, and
    /// every pick still in flight is dropped so none writes into the flight that follows.
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _nestRunning = null;
        _nestSettled = false;
        CancelPendingPicks();
        CancelMcpSets();
        ResetReadoutBaselines();   // the new flight's flaps, gear and lights are no change
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        Sim = simConnect;

        switch (action)
        {
            // Ctrl+M: mute individual auto-announced variables.
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowBBS757MonitorManagerDialog();
                return true;

            // Ctrl+H / S / A / V: the MCP value windows (BBS757Definition.Mcp.cs), the HS787's way.
            case HotkeyAction.FCUSetHeading:
            case HotkeyAction.FCUSetSpeed:
            case HotkeyAction.FCUSetAltitude:
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowMcpDialog(action, simConnect, announcer, parentForm);
                return true;

            // Shift+H / S / A / V: the MCP's own values, read fresh.
            case HotkeyAction.ReadHeading:
            case HotkeyAction.ReadSpeed:
            case HotkeyAction.ReadAltitude:
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                _ = ReadMcpAsync(action, simConnect, announcer);   // never throws: it catches and logs
                return true;

            // L flaps, Shift+G gear, B altimeters and minimums, F / Shift+F fuel, W / Shift+W gross
            // weight: the other Boeings' keys (BBS757Definition.Readouts.cs).
            case HotkeyAction.ReadFlaps:
            case HotkeyAction.ReadGear:
            case HotkeyAction.ReadAltimeter:
            case HotkeyAction.ReadFuelQuantity:
            case HotkeyAction.ReadFuelInfo:
            case HotkeyAction.ReadWaypointInfo:
            case HotkeyAction.ReadGrossWeightKg:
                _ = ReadoutAsync(action, simConnect, announcer);   // never throws: it catches and logs
                return true;

            // Ctrl+B: the altimeter and minimums window (BBS757Definition.Altimeter.cs).
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltimeterWindow(simConnect, announcer, parentForm);
                return true;

            // Ctrl+P: the 757 autopilot window (BBS757Definition.Autopilot.cs).
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAutopilotWindow(simConnect, announcer);
                return true;
        }

        // AI display reads, the time read-outs and the trim toggle.
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }
}
