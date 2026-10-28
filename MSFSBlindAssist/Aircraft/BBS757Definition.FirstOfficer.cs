using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The First Officer's side of the 757 (plan 10): the one variable it registers, and a SILENT,
/// AWAITABLE write path. The panel path (<see cref="HandleUIVariableSet"/>) is fire-and-forget and
/// speaks its own refusals; a flow step must instead know whether its switch landed, and the flow
/// speaks for itself. So <see cref="FoSetAsync"/> and <see cref="FoPressAsync"/> reuse the panel's
/// own decisions (<see cref="Bbs757Write.ToPosition"/>, <see cref="Bbs757Write.PressFor"/>,
/// <see cref="Bbs757Guarded.Plan"/>) and its sim access (<see cref="WriteIoFor"/>), speak nothing,
/// and return true only when the control reads its target afterwards.
///
/// A write runs as a pick (<see cref="BeginPick"/>): the control's deliveries are consumed while it
/// is in flight, so an open panel's combo is not moved under the pilot, and a pilot's pick of the
/// same control supersedes it (the write then reports false). A linked status row (the parking
/// brake, EXT PWR) has its change taken as this write's echo, as for a panel pick.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The engine variant, BlueBird's <c>L:B757_ENG_VARIANT</c> (0 RB211, 1 PW2000), OnRequest.</summary>
    public const string FoEngineVariantKey = "BBS757_FO_ENG_VARIANT";

    static partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars)
    {
        if (vars.ContainsKey(FoEngineVariantKey)) return;
        vars[FoEngineVariantKey] = new SimVarDefinition
        {
            Name = "B757_ENG_VARIANT", DisplayName = "Engine Variant",
            Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.OnRequest,
        };
    }

    /// <summary>True when the map knows this node (the First Officer's tables are checked against it).</summary>
    internal bool HasControl(string node) => _byNode.ContainsKey(node);

    /// <summary>The map control for a node, or null.</summary>
    internal Bbs757Control? ControlFor(string node) => _byNode.TryGetValue(node, out var c) ? c : null;

    /// <summary>
    /// Sets a positional control (toggle, switch, select, or a guarded one) to <paramref name="target"/>,
    /// silently. True when the control already reads it, or reads it after the write; false when the
    /// control is unknown, a write cannot land, the control or its guard was not read, the target is
    /// none of its positions, the write did not take, or a newer pick superseded this one.
    /// </summary>
    internal async Task<bool> FoSetAsync(SimConnectManager sim, string node, double target, CancellationToken ct)
    {
        if (!_byNode.TryGetValue(node, out var control)) return false;
        if (Bbs757Write.IsMomentary(control)) return false;   // a button has no position: FoPressAsync
        Sim = sim;
        return await FoSetCoreAsync(control, target, WriteIoFor(sim), ct);
    }

    /// <summary>The testable core of <see cref="FoSetAsync"/>.</summary>
    internal async Task<bool> FoSetCoreAsync(Bbs757Control control, double target, Bbs757SimIo io, CancellationToken ct)
    {
        if (!io.CanWrite()) return false;
        var pick = BeginPick(control.Node);
        if (pick == null) return false;   // switched away
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(pick.Token, ct);
        var token = linked.Token;
        bool landed = false;
        Bbs757Control? guard = null;
        try
        {
            IReadOnlyList<string> calls;
            if (!string.IsNullOrWhiteSpace(control.Guard))
            {
                if (!_byNode.TryGetValue(control.Guard, out guard)) return false;
                var reads = await Task.WhenAll(io.ReadFresh(control.Node, token), io.ReadFresh(guard.Node, token));
                token.ThrowIfCancellationRequested();
                var gplan = Bbs757Guarded.Plan(control, guard, reads[0], reads[1], target);
                if (gplan.Refused) return false;
                if (gplan.Calls.Count == 0) return landed = true;
                calls = gplan.Calls;
            }
            else
            {
                double? current = await io.ReadFresh(control.Node, token);
                token.ThrowIfCancellationRequested();
                var plan = Bbs757Write.ToPosition(control, current, target);
                if (plan.Outcome == Bbs757WriteOutcome.AtPosition) return landed = true;
                if (plan.Outcome == Bbs757WriteOutcome.Refused)
                {
                    Log.Debug("BBS757", $"FO {control.Node}: read {current?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}, " +
                        $"target {target.ToString(System.Globalization.CultureInfo.InvariantCulture)}: no safe write.");
                    return false;
                }
                calls = plan.Calls;
            }

            string? status = LinkedStatusKey(control.Node);
            if (status != null) ArmOwnWriteEcho(status);
            OnOwnWrite(control.Node);   // plan 13
            if (guard != null) OnOwnWrite(guard.Node);
            for (int i = 0; i < calls.Count; i++)
            {
                if (i > 0 && guard != null) await io.Delay(Bbs757Guarded.StepGapMs, token);
                io.Write(calls[i]);
            }

            await io.Delay(ReadBackDelayMs, token);
            double? after = await io.ReadFresh(control.Node, token);
            landed = LandedOnPick(control, after, target);
            if (status != null)
            {
                double? state = await io.ReadFresh(status, token);
                if (state != null) SettleOwnWriteEcho(status, LinkedPhrase(status, state));
            }
            if (!landed)
                Log.Info("BBS757", $"FO {control.Node}: target {target.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"not reached, reads {after?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}.");
            return landed;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"FO {control.Node}: write failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (EndPick(control.Node, pick))
            {
                io.RequestRead(control.Node);
                if (guard != null) io.RequestRead(guard.Node);
            }
        }
    }

    /// <summary>
    /// Presses a momentary button (or holds a held one for its hold time), silently: true when the press
    /// was sent. A button has no position to read back; the caller confirms what the press did by the
    /// state it drives (EXT PWR ON, the speedbrake lever). False when the control is unknown, is not a
    /// button, has no press code, or a write cannot land.
    /// </summary>
    internal async Task<bool> FoPressAsync(SimConnectManager sim, string node, CancellationToken ct)
    {
        if (!_byNode.TryGetValue(node, out var control) || !Bbs757Write.IsMomentary(control)) return false;
        Sim = sim;
        return await FoPressCoreAsync(control, WriteIoFor(sim), ct);
    }

    internal async Task<bool> FoPressCoreAsync(Bbs757Control control, Bbs757SimIo io, CancellationToken ct)
    {
        var press = Bbs757Write.PressFor(control);
        if (press == null || !io.CanWrite() || _lifetime.IsCancellationRequested) return false;
        string? status = LinkedStatusKey(control.Node);
        if (status != null) ArmOwnWriteEcho(status);
        OnOwnWrite(control.Node);   // plan 13
        io.Write(press.Down);
        if (press.Up != null)
        {
            await io.Delay(press.HoldMs, CancellationToken.None);   // a press is always let go
            io.Write(press.Up);
        }
        RelockIfUnlock(control, io);
        if (status != null)
        {
            try
            {
                await io.Delay(ReadBackDelayMs, ct);
                double? state = await io.ReadFresh(status, ct);
                if (state != null) SettleOwnWriteEcho(status, LinkedPhrase(status, state));
            }
            catch (OperationCanceledException) { /* the press went; only the echo read-back stopped */ }
        }
        return true;
    }

    /// <summary>
    /// All three altimeters to standard, the Ctrl+B window's own way (one <c>KOHLSMAN_SET</c> 16212, its
    /// echo armed so the altimeter watch does not announce it), silently. True when all three already
    /// read standard or do after the set; false when a write cannot land or they did not all reach it.
    /// </summary>
    internal async Task<bool> FoSetAltimetersStandardAsync(SimConnectManager sim, CancellationToken ct)
    {
        try
        {
            var io = McpIoFor(sim);
            double std = Bbs757Altimeters.StandardParameter / 16.0;
            async Task<bool> AllStandard()
            {
                foreach (var key in Bbs757Altimeters.KohlsmanKeys)
                    if (await io.Read(key, ct) is not double v || Math.Abs(v - std) > 0.5) return false;
                return true;
            }
            if (await AllStandard()) return true;
            if (!CanWrite(sim) || _lifetime.IsCancellationRequested) return false;
            OnOwnAltimeterSet();   // plan 06, then 14: the watch takes the change as this set's echo
            await Bbs757Altimeters.SetAltimetersAsync(io, Bbs757Altimeters.StandardParameter, ct);
            return await AllStandard();
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"FO altimeters to standard failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The parking brake SET, silently: the lever's own write first (all a brake in EASY mode needs), then, only
    /// if it did not set, full braking in the same frame as the lever (REALISTIC mode, <see cref="Bbs757ParkingBrake"/>).
    /// True when the brake reads set; false when it cannot be written or did not set.
    /// </summary>
    internal async Task<bool> FoSetParkingBrakeAsync(SimConnectManager sim, CancellationToken ct)
    {
        Sim = sim;
        return await FoSetParkingBrakeCoreAsync(WriteIoFor(sim), ct);
    }

    internal async Task<bool> FoSetParkingBrakeCoreAsync(Bbs757SimIo io, CancellationToken ct)
    {
        if (!_byNode.TryGetValue(Bbs757ParkingBrake.Node, out var control)) return false;
        if (await FoSetCoreAsync(control, 1, io, ct)) return true;
        if (!io.CanWrite() || _lifetime.IsCancellationRequested || ct.IsCancellationRequested) return false;
        if (!Bbs757ParkingBrake.ShouldRetry(await io.ReadFresh(control.Node, ct), set: true, ParkingBrakeRealistic()))
            return false;   // unread, already set, or EASY mode: never retry blind
        try
        {
            OnOwnWrite(control.Node);   // plan 13
            return await Bbs757ParkingBrake.RetryWithFullBrakingAsync(io, set: true, ReadBackDelayMs, ct);
        }
        catch (OperationCanceledException) { return false; }
        finally { io.RequestRead(control.Node); }
    }

    /// <summary>A registered variable read fresh, for the First Officer's verifications; null unread.</summary>
    internal Task<double?> FoReadAsync(SimConnectManager sim, string key, CancellationToken ct)
        => WriteIoFor(sim).ReadFresh(key, ct);

    /// <summary>A registered variable's cached value (the window polls the First Officer's controls each second); null unread.</summary>
    internal double? FoCached(SimConnectManager sim, string key) => WriteIoFor(sim).Cached(key);

    /// <summary>
    /// A positional control written NOW, planned from the cache, with no read-back: the First Officer
    /// window is closing and an engine start a stopped flow left short of its fuel must reach the sim
    /// before the window lets go of the aircraft (the MD-11 pushes its START switch in the same way).
    /// True when sent or already there; false for an unknown, guarded or momentary control, an unread
    /// value, or a write that cannot land.
    /// </summary>
    internal bool FoWriteNow(SimConnectManager sim, string node, double target)
        => _byNode.TryGetValue(node, out var control) && FoWriteNowCore(control, target, WriteIoFor(sim));

    /// <summary>The testable core of <see cref="FoWriteNow"/>.</summary>
    internal bool FoWriteNowCore(Bbs757Control control, double target, Bbs757SimIo io)
    {
        if (Bbs757Write.IsMomentary(control) || !string.IsNullOrWhiteSpace(control.Guard) || !io.CanWrite()) return false;
        var plan = Bbs757Write.ToPosition(control, io.Cached(control.Node), target);
        if (plan.Outcome == Bbs757WriteOutcome.AtPosition) return true;
        if (plan.Outcome == Bbs757WriteOutcome.Refused) return false;
        OnOwnWrite(control.Node);   // plan 13
        foreach (var call in plan.Calls) io.Write(call);
        return true;
    }
}
