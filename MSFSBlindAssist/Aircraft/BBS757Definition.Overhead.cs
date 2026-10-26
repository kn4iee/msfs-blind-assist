using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The overhead panels' writes: plan 03's guarded controls (the emergency lights, the stab trim
/// cutouts), and later plan 12's overhead panels. A guarded control is any map control whose
/// <see cref="Bbs757Control.Guard"/> names another map control (BlueBird's guard cover): it moves
/// only with the cover open, so a pick is planned from fresh reads of both by
/// <see cref="Bbs757Guarded.Plan"/> and sent here.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>
    /// A pick on a guarded control: settle, refuse aloud if nothing can land, read the control and its
    /// guard fresh, send what <see cref="Bbs757Guarded.Plan"/> plans (the guard's press and the state
    /// write, <see cref="Bbs757Guarded.StepGapMs"/> apart), then read the control back. It follows
    /// <see cref="SetPositionAsync"/>: a newer pick of the same control supersedes this one at any
    /// await before its first call, and both rows are re-read at the end so their combos show where
    /// the aircraft really is.
    ///
    /// Once the first call has gone, a newer pick does not stop the sequence: a half-run one would
    /// leave the guard open with no state write behind it. Only a profile switch ends it. The newer
    /// pick still cancels this one's read-back, so only the newer pick speaks.
    ///
    /// Speech follows CORE-7: the screen reader read the combo, so a pick that lands says nothing and
    /// one that does not says "{label} did not move" once (an unreadable read-back counts as not
    /// landed); a pick that cannot be planned or written says "{label} unavailable" and sends nothing.
    ///
    /// True when it wrote and waited out the read-back delay; false when it refused, found the control
    /// already there, or was superseded or switched away. Never throws.
    /// </summary>
    private async Task<bool> SetGuardedAsync(Bbs757Control control, Bbs757Control guard, double target,
        Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        if (_lifetime.IsCancellationRequested) return false;   // switched away: nothing is written
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (_pendingPicks.TryGetValue(control.Node, out var prior))
        {
            try { prior.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        _pendingPicks[control.Node] = cts;
        var ct = cts.Token;
        bool resync = false, wrote = false;

        try
        {
            await io.Delay(PickSettleMs, ct);

            if (!io.CanWrite())
            {
                SayUnavailable(control, announcer);
                resync = true;
                return false;
            }

            // Both at once: an unread variable then costs one read timeout, not two.
            var reads = await Task.WhenAll(io.ReadFresh(control.Node, ct), io.ReadFresh(guard.Node, ct));
            ct.ThrowIfCancellationRequested();
            double? current = reads[0], guardState = reads[1];

            var plan = Bbs757Guarded.Plan(control, guard, current, guardState, target);
            if (plan.Refused)
            {
                Log.Debug("BBS757", $"{control.Node}: read {GuardedLogValue(current)}, guard {guard.Node} read {GuardedLogValue(guardState)}, " +
                    $"target {GuardedLogValue(target)}: no safe write.");
                SayUnavailable(control, announcer);
                resync = true;
                return false;
            }
            if (plan.Calls.Count == 0) return false;   // already there, guard where the pick needs it

            OnOwnWrite(control.Node);   // plan 13
            OnOwnWrite(guard.Node);
            for (int i = 0; i < plan.Calls.Count; i++)
            {
                if (i > 0)
                {
                    // The first call has gone: finish even if a newer pick has superseded this one
                    // (its token is not awaited here). Only a profile switch stops the sequence.
                    if (_lifetime.IsCancellationRequested) return false;
                    await io.Delay(Bbs757Guarded.StepGapMs, _lifetime.Token);
                }
                io.Write(plan.Calls[i]);
            }

            await io.Delay(ReadBackDelayMs, ct);
            resync = true;
            wrote = true;

            double? after = await io.ReadFresh(control.Node, ct);
            ct.ThrowIfCancellationRequested();
            if (!LandedOnPick(control, after, target)) announcer.Announce($"{control.Label} did not move");
        }
        catch (OperationCanceledException) { /* superseded by a newer pick, a reload, or switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: guarded write failed: {ex.Message}");
            resync = true;   // whatever was or was not sent, the combos should show the aircraft's position
        }
        finally
        {
            // Only this pick's own entry, as SetPositionAsync: a newer pick of the control has replaced
            // it otherwise, and then the re-read is that pick's to make.
            bool mine = _pendingPicks.TryRemove(new KeyValuePair<string, CancellationTokenSource>(control.Node, cts));
            cts.Dispose();
            if (mine && resync)
            {
                io.RequestRead(control.Node);
                io.RequestRead(guard.Node);
            }
        }
        return wrote;
    }

    /// <summary>True when <paramref name="reading"/> is a number holding the position <paramref name="target"/> (within the read-back slack).</summary>
    private static bool LandedOnPick(Bbs757Control control, double? reading, double target)
    {
        if (reading is not double value || !double.IsFinite(value)) return false;
        int to = Bbs757Rpn.PositionIndex(control, target);
        return to >= 0 && Bbs757Rpn.PositionIndex(control, value) == to;
    }

    private static string GuardedLogValue(double? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing";
}
