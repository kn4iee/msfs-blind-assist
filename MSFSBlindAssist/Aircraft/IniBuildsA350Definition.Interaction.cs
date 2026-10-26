using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Writes: every panel row is claimed here, so MainForm's generic paths (SetLVar's data-definition write, "pressed"
/// announcements) never touch an A350 control. Nothing the pilot does on a panel is spoken back; only errors are,
/// and a switch the aircraft did not move ([CORE-7]).
/// </summary>
public partial class IniBuildsA350Definition
{
    /// <summary>How long a switch pick waits before it is read back: one batch period plus the aircraft applying it.
    /// The iniBuilds A300's judgment; the A350's own read-back measured 1.5 s for GALLEY (2026-10-26).</summary>
    public const int ToggleReadBackMs = 1500;

    /// <summary>How long a read-back waits for a fresh value.</summary>
    public const int ReadoutTimeoutMs = 2500;

    private readonly A350CommandedState _commanded = new();

    /// <summary>Steps still owed after a wait that has not finished (a hold button's release, a spring switch's
    /// return), keyed by run, so <see cref="Dispose"/> can let go first.</summary>
    private readonly Dictionary<int, IReadOnlyList<A350Step>> _owed = new();
    private int _nextRunId;

    /// <summary>Each row's latest run: an earlier run of the same row stops at its next wait.</summary>
    private readonly Dictionary<string, int> _latestRunByRow = new(StringComparer.Ordinal);

    /// <summary>Each row's latest pick, its target, and whether a delivery has reached it since.</summary>
    private readonly Dictionary<string, (int Pick, double Target, bool Reached)> _pendingPicks = new(StringComparer.Ordinal);
    private int _pickSeq;

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(varKey, out var row) || row.Control is not { } control)
            return false;   // a base variable: MainForm's generic path
        _sim = simConnect;

        if (_typed.TryGetValue(varKey, out var field))
            return HandleTyped(row, field, value, simConnect, announcer);

        // An option the aircraft is built without: the cockpit hides its controls and clears their variables.
        if (A350Options.For(control.Id) is { } option && Cached(simConnect, option.Key) is double fitted && fitted < 0.5)
        {
            announcer.Announce(option.NotFitted);
            ReRead(row.Key, simConnect);
            return true;
        }

        var plan = row.Action switch
        {
            A350RowAction.Set => A350WritePlan.ForSet(control, value, CurrentValue(row.Key, simConnect)),
            A350RowAction.Press => A350WritePlan.ForPress(control),
            A350RowAction.Increase => A350WritePlan.ForStep(control, increase: true),
            A350RowAction.Decrease => A350WritePlan.ForStep(control, increase: false),
            _ => A350Plan.Refused(A350WritePlan.NotSettableRefusal),
        };
        // A spring switch returns to its rest, so the position to remember is the rest.
        double? commanded = row.Action != A350RowAction.Set ? null
            : control.Kind == A350Kinds.Spring ? control.StateForPosition(control.Rest ?? 1)
            : value;
        if (!Execute(row, plan, simConnect, announcer, commanded))
            return true;   // refused, already there or unavailable: nothing was written

        if (commanded is double target && control.Kind is A350Kinds.Toggle or A350Kinds.Command or A350Kinds.Selector)
            _ = CheckMovedAsync(row, target, simConnect, announcer, ++_pickSeq);
        return true;
    }

    /// <summary>How long a typed value's click is given to land before it is read back.</summary>
    public const int TypedReadBackMs = 600;

    /// <summary>A typed value (<see cref="A350TypedEntry"/>): refused aloud, or written one step short and clicked to,
    /// then read back, corrected when the click landed elsewhere, and confirmed as the cockpit shows it (a numeric
    /// confirmation always speaks, [CORE-7]).</summary>
    private bool HandleTyped(A350PlacedRow row, A350TypedField field, double value, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        double? Read(string name) => Cached(sim, KeyForInput(name));
        var plan = A350TypedEntry.Plan(field, value, Read);
        if (plan.Refusal != null)
        {
            announcer.Announce(plan.Refusal);
            return true;
        }
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            return true;
        }
        _ = TypedAsync(row, value, plan, Read, sim, announcer);
        return true;
    }

    private async Task TypedAsync(A350PlacedRow row, double typed, A350TypedPlan plan, Func<string, double?> read,
        SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        int run = ++_nextRunId;
        _latestRunByRow[row.Key] = run;
        bool Superseded() => _disposed || _latestRunByRow.GetValueOrDefault(row.Key) != run;
        try
        {
            foreach (var step in plan.Steps)
            {
                if (step is A350CalcStep calc)
                    Send(sim, calc.Rpn);
                else if (step is A350DelayStep delay)
                {
                    await Delay(delay.Milliseconds);
                    if (Superseded())
                        return;
                }
            }
            string key = KeyForInput(plan.ReadBackVar);
            await Delay(TypedReadBackMs);
            if (Superseded())
                return;
            double? shown = await ReadFresh(sim, key, ReadoutTimeoutMs);
            if (shown is double landed && !plan.Reached(landed) && !Superseded())
            {
                Send(sim, plan.Correction.Rpn);   // e.g. a 1000 ft altitude step, or the managed speed's own preset
                await Delay(TypedReadBackMs);
                if (Superseded())
                    return;
                shown = await ReadFresh(sim, key, ReadoutTimeoutMs);
            }
            if (Superseded())
                return;
            if (shown is not double now)
                announcer.Announce(plan.Confirm(plan.Target, read));   // no read-back: the value sent, as the A320 says it
            else if (plan.Reached(now))
                announcer.Announce(plan.Confirm(now, read));
            else
                announcer.Announce($"{LineName(row.Name)} did not take {A350WritePlan.Format(typed)}. {plan.Confirm(now, read)}");
        }
        catch (Exception ex)
        {
            Log.Warn("A350", $"A typed value for {row.Key} failed: {ex.Message}");
        }
    }

    /// <summary>The value to plan from: what MSFSBA just commanded while it is fresh, else the cache.</summary>
    private double? CurrentValue(string key, SimConnectManager sim) =>
        _commanded.Resolve(CommandKey(key), Cached(sim, key), Clock());

    /// <summary>What a row's commanded position is kept under: its state variable, so two rows that flip one variable
    /// share one hold (a hold kept per row let the second plan from a stale cache and flip the first back).</summary>
    private string CommandKey(string rowKey) =>
        _rows.TryGetValue(rowKey, out var row) && row.Control?.StateVar is { Length: > 0 } stateVar ? stateVar : rowKey;

    /// <summary>Runs a plan, or refuses it aloud; true when its steps were sent.</summary>
    private bool Execute(A350PlacedRow row, A350Plan plan, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        double? commandedValue)
    {
        if (plan.Refusal != null)
        {
            announcer.Announce(A350WritePlan.SpokenRefusal(row.Name, plan.Refusal));
            ReRead(row.Key, sim);
            return false;
        }
        if (plan.IsEmpty)
            return false;   // already there; the screen reader has said so
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            ReRead(row.Key, sim);
            return false;
        }
        if (commandedValue is double target)
            _commanded.Record(CommandKey(row.Key), target, Clock());
        _ = RunAsync(row.Key, plan.Steps, sim);
        return true;
    }

    /// <summary>Runs a plan's steps on the UI thread (each await resumes there, so SimConnect is never used from a
    /// pool thread, [SIM-20]). A later pick of the same row takes over: the earlier run's steps after its wait are
    /// dropped.</summary>
    private async Task RunAsync(string rowKey, IReadOnlyList<A350Step> steps, SimConnectManager sim)
    {
        int run = ++_nextRunId;
        _latestRunByRow[rowKey] = run;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (_disposed)
                    return;
                switch (steps[i])
                {
                    case A350CalcStep calc:
                        Send(sim, calc.Rpn);
                        break;
                    case A350DelayStep delay:
                        _owed[run] = steps.Skip(i + 1).ToList();
                        await Delay(delay.Milliseconds);
                        _owed.Remove(run);
                        if (_latestRunByRow.GetValueOrDefault(rowKey) != run)
                            return;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A350", $"A control write failed: {ex.Message}");
        }
        finally
        {
            _owed.Remove(run);
        }
    }

    /// <summary>Sends every owed step at once (the aircraft is switched away or the app is closing).</summary>
    private void ReleaseOwedSteps()
    {
        var sim = _sim;
        if (sim == null)
            return;
        foreach (var steps in _owed.Values.ToList())
            foreach (var step in steps)
                if (step is A350CalcStep calc)
                    Send(sim, calc.Rpn);
        _owed.Clear();
    }

    /// <summary>A delivery that reaches a pending pick's target: the switch moved, whatever it does next.</summary>
    private void NotePickReached(string key, double value)
    {
        if (_pendingPicks.TryGetValue(key, out var p) && !p.Reached && Math.Abs(value - p.Target) < A350WritePlan.SameValueTolerance)
            _pendingPicks[key] = p with { Reached = true };
    }

    /// <summary>
    /// Reads a picked switch back once it has had time to move. When the aircraft ignored the write, the combo would
    /// keep the pick while the switch stayed put, and the next pick would be planned from it: so the row is read again,
    /// the stayed position is remembered, and it is said once, as the MD-11, the iFly 737 and the A300 say it
    /// ("BAT 1 did not move"). A reading that is none of the switch's positions is neither said nor remembered.
    /// </summary>
    private async Task CheckMovedAsync(A350PlacedRow row, double target, SimConnectManager sim,
        ScreenReaderAnnouncer announcer, int pick)
    {
        _pendingPicks[row.Key] = (pick, target, false);
        bool Superseded() => !_pendingPicks.TryGetValue(row.Key, out var p) || p.Pick != pick || p.Reached;
        try
        {
            await Delay(ToggleReadBackMs);
            if (_disposed || Superseded())
                return;
            if (await ReadFresh(sim, row.Key, ReadoutTimeoutMs) is not double now
                || _disposed
                || Superseded()
                || row.Control!.PositionForState(now) is null
                || Math.Abs(now - target) < A350WritePlan.SameValueTolerance)
                return;
            _commanded.Record(CommandKey(row.Key), now, Clock());
            ReRead(row.Key, sim);
            announcer.Announce($"{row.Name} did not move");
        }
        catch (Exception ex)
        {
            Log.Warn("A350", $"Read-back of {row.Key} failed: {ex.Message}");
        }
        finally
        {
            if (_pendingPicks.TryGetValue(row.Key, out var p) && p.Pick == pick)
                _pendingPicks.Remove(row.Key);
        }
    }
}
