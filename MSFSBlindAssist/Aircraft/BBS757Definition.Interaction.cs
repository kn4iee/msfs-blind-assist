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

    /// <summary>Cancelled when the pilot switches to another profile: no pending pick may write after that.</summary>
    private readonly CancellationTokenSource _lifetime = new();

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
        if (!_byNode.TryGetValue(varKey, out var control))
            return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
        Sim = simConnect;

        if (Bbs757Write.IsMomentary(control))
        {
            // One press per pilot action, so the refusal is judged here, once.
            var press = Bbs757Write.PressFor(control);
            if (press == null || !simConnect.CalcWriteCanLand) return SayUnavailable(control, announcer);
            _ = PressAsync(control, press, simConnect, announcer);
            return true;
        }

        _ = SetPositionAsync(control, value, simConnect, announcer);
        return true;
    }

    private static bool SayUnavailable(Bbs757Control control, ScreenReaderAnnouncer announcer)
    {
        Log.Debug("BBS757", $"{control.Node}: write refused, nothing sent.");
        announcer.Announce($"{control.Label} unavailable");
        return true;
    }

    /// <summary>
    /// A button: press, hold, release, then "{label} pressed" when the button has no readable state.
    /// The release is never cancelled: a press is always let go, even across a profile switch.
    /// </summary>
    private async Task PressAsync(Bbs757Control control, Bbs757Press press, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        try
        {
            sim.ExecuteCalculatorCodeUnique(press.Down);
            if (press.Up != null)
            {
                await Task.Delay(press.HoldMs);
                sim.ExecuteCalculatorCodeUnique(press.Up);
            }
            if (_lifetime.IsCancellationRequested) return;
            if (!Bbs757Write.HasReadableState(control)) announcer.Announce($"{control.Label} pressed");

            await Task.Delay(ReadBackDelayMs, _lifetime.Token);
            sim.RequestVariable(control.Node, forceUpdate: true);
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
    private async Task SetPositionAsync(Bbs757Control control, double target, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
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
            await Task.Delay(PickSettleMs, ct);

            if (!sim.CalcWriteCanLand)
            {
                SayUnavailable(control, announcer);
                resync = true;
                return;
            }

            double? current = await sim.ReadFreshAsync(control.Node, FreshReadTimeoutMs, ct);
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

            foreach (var call in plan.Calls) sim.ExecuteCalculatorCodeUnique(call);

            await Task.Delay(ReadBackDelayMs, ct);
            resync = true;
        }
        catch (OperationCanceledException) { /* superseded by a newer pick, or switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: write failed: {ex.Message}");
        }
        finally
        {
            // Only this pick's own entry: a newer pick of the control has replaced it otherwise, and
            // then the re-read is that pick's to make.
            bool mine = _pendingPicks.TryRemove(new KeyValuePair<string, CancellationTokenSource>(control.Node, cts));
            cts.Dispose();
            if (mine && resync) sim.RequestVariable(control.Node, forceUpdate: true);
        }
    }

    /// <summary>True while a pick of this control is in flight (its deliveries are consumed).</summary>
    internal bool IsPickPending(string varKey) => _pendingPicks.ContainsKey(varKey);

    /// <summary>Stops every pending pick: a profile switch must not let one write into the next aircraft.</summary>
    public override void OnSwitchedAway()
    {
        base.OnSwitchedAway();
        _lifetime.Cancel();
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>
    /// The BBS Nest check rides the first delivery of each connection. A control with a pick in
    /// flight is consumed silently (see <see cref="SetPositionAsync"/>). Everything else is the
    /// base's (the trim and glideslope call-outs) or the generic announcer's (the announced status
    /// rows).
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        WarnOnceIfNestMissing(announcer);
        if (IsPickPending(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// Asks once whether BBS Nest runs and, when it does not, says so once. The warning is no
    /// Ctrl+M row's, so a delivery MainForm has wrapped in <c>announcer.Suppressed</c> (a muted row,
    /// a UI-set echo) does not use it up: it is said on the next delivery instead.
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

    /// <summary>A flight load or a dropped connection: BBS Nest is asked about again on the next delivery.</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _nestRunning = null;
        _nestSettled = false;
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
        }

        // AI display reads, the time read-outs and the trim toggle.
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }
}
