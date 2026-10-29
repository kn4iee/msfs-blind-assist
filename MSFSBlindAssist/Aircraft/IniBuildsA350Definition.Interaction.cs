using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Writes: every panel row is claimed here, so MainForm's generic paths (SetLVar's data-definition write) never touch an
/// A350 control. What the pilot does is confirmed once, only with what the screen reader cannot say ([CORE-7]): a press
/// whose label shows no state says "&lt;name&gt; pressed", as the A380's momentary buttons and MainForm's generic button do; a
/// step of an encoder with a value line says the value it left; errors, and a switch the aircraft did not move, are said.
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

        if (row.Action == A350RowAction.Press && A350WritePlan.IsLatched(control))
            return HandleLatched(row, control, simConnect, announcer);

        var plan = row.Action switch
        {
            A350RowAction.Set => A350WritePlan.ForSet(control, value, CurrentValue(row.Key, simConnect)),
            A350RowAction.Press => A350WritePlan.ForPress(control),
            A350RowAction.Increase => A350WritePlan.ForStep(control, increase: true),
            A350RowAction.Decrease => A350WritePlan.ForStep(control, increase: false),
            A350RowAction.Custom when control.Id == A350RunwayCondition.KnobId => A350RunwayCondition.Plan(control, value),
            _ => A350Plan.Refused(A350WritePlan.NotSettableRefusal),
        };
        // A spring switch returns to its rest, so the position to remember is the rest.
        double? commanded = row.Action != A350RowAction.Set ? null
            : control.Kind == A350Kinds.Spring ? control.StateForPosition(control.Rest ?? 1)
            : value;
        if (row.Action == A350RowAction.Press)
            ArmUnitButtonEcho(control);   // before the write, so its echo can never arrive first
        if (!Execute(row, plan, simConnect, announcer, commanded))
            return true;   // refused, already there or unavailable: nothing was written
        NoteOwnLamps(control);   // its lights follow the pilot's own press or pick: its label says them

        if (row.Action == A350RowAction.Press && !ShowsState(row.Key))
            announcer.Announce($"{row.Name} pressed");
        else if (row.Action is A350RowAction.Increase or A350RowAction.Decrease)
            ConfirmStep(row, control, announcer);

        if (commanded is double target && control.Kind is A350Kinds.Toggle or A350Kinds.Command or A350Kinds.Selector)
            _ = CheckMovedAsync(row, target, simConnect, announcer, ++_pickSeq);
        return true;
    }

    /// <summary>Whether a press row's label shows a state after the press (its lights, its held record, its button state),
    /// which the screen reader says; otherwise the press is confirmed "&lt;name&gt; pressed" (review 2026-10-29, panels minor 3).</summary>
    private bool ShowsState(string rowKey) =>
        _held.ContainsKey(rowKey) || _buttonLamps.ContainsKey(rowKey) || _buttonStates.ContainsKey(rowKey);

    /// <summary>An encoder step: the value it leaves is said once the run of steps settles (<see cref="ReleaseStepReadbacks"/>);
    /// an encoder whose value nothing reads says "&lt;name&gt; pressed" at once.</summary>
    private void ConfirmStep(A350PlacedRow row, A350Control control, ScreenReaderAnnouncer announcer)
    {
        string valueKey = A350Readouts.KeyFor(control);
        if (_values.ContainsKey(valueKey))
        {
            _stepReadbacks[valueKey] = Clock();
            _stepAnnouncer = announcer;
        }
        else
            announcer.Announce($"{row.Name} pressed");
    }

    /// <summary>The FCU's AP 1 row, which the universal auto-autopilot engage presses.</summary>
    private const string Ap1Row = "A350_FCU_AP_1";

    /// <summary>Either autopilot engaged, as the FCU's AP 1 and AP 2 lights read it (their rules' <c>L:INI_ap1_on</c>,
    /// <c>L:INI_ap2_on</c>): an engaged one known is engaged; both read off is not; anything else is unknown
    /// ([FO-18]: unread is null, never false).</summary>
    public override bool? IsAutopilotEngaged(SimConnectManager simConnect)
    {
        double? ap1 = Cached(simConnect, KeyForInput("INI_ap1_on")), ap2 = Cached(simConnect, KeyForInput("INI_ap2_on"));
        if (ap1 >= 0.5 || ap2 >= 0.5)
            return true;
        return ap1 is double && ap2 is double ? false : null;
    }

    /// <summary>The universal auto-autopilot engage: the press the FCU's own AP 1 row makes, its lights then the pilot's
    /// (the FMA call-out says the engagement), and only on a definite OFF: the button flips, so a press while engaged would
    /// disconnect it ([FO-17], [FO-18]). No floor of its own: the A380 sets none, and the closed loop reports a press the
    /// aircraft refuses (the A350's own lowest engage height is on the sim list).</summary>
    public override void EngageAutopilot(SimConnectManager simConnect)
    {
        if (IsAutopilotEngaged(simConnect) != false || !_rows.TryGetValue(Ap1Row, out var row) || row.Control is not { } control
            || !CanLand(simConnect))
            return;
        var plan = A350WritePlan.ForPress(control);
        if (plan.Refusal != null || plan.IsEmpty)
            return;
        _sim = simConnect;
        _ = RunAsync(row.Key, plan.Steps, simConnect);
        NoteOwnLamps(control);
    }

    /// <summary>A toggle: retried at the toggle aircraft's pace, never on an unread state.</summary>
    public override int AutopilotEngageRetryIntervalMs => ToggleAutopilotEngageRetryIntervalMs;
    public override bool AutopilotEngageWaitsForState => true;

    /// <summary>Latched hold buttons MSFSBA pressed down, by row, with the run their release is owed under (sent if the
    /// aircraft is switched away or the app closes while one is held).</summary>
    private readonly Dictionary<string, int> _heldRows = new(StringComparer.Ordinal);

    /// <summary>A button the pilot presses and holds (<see cref="A350WritePlan.IsLatched"/>): a press holds it down when
    /// the cockpit shows it released, and lets it go when the cockpit shows it held (after a flight load, or a hold made
    /// in the cockpit, the cockpit is the record), never on a timer. Its label says which once the cockpit's record
    /// arrives, asked for at once so the reader says it.</summary>
    private bool HandleLatched(A350PlacedRow row, A350Control control, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            return true;
        }
        string heldKey = KeyForInput(A350WritePlan.HeldVarFor(control)!);
        bool down = _commanded.Resolve(heldKey, Cached(sim, heldKey), Clock()) is not >= 0.5;
        foreach (var step in (down ? A350WritePlan.ForHoldDown(control) : A350WritePlan.ForHoldUp(control)).Steps)
            if (step is A350CalcStep calc)
                Send(sim, calc.Rpn);
        _commanded.Record(heldKey, down ? 1 : 0, Clock());   // a second press before the record arrives lets go
        if (_heldRows.Remove(row.Key, out int earlier))
            _owed.Remove(earlier);   // one release owed per button at most
        if (down)
        {
            int run = ++_nextRunId;
            _heldRows[row.Key] = run;
            _owed[run] = A350WritePlan.ForHoldUp(control).Steps;
        }
        NoteOwnLamps(control);
        ReRead(heldKey, sim);
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
            ArmTypedEcho(row.Key);
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
            if (plan.Stepper is { } stepper)
            {
                await StepAsync(row, typed, plan, stepper, read, sim, announcer, Superseded);
                return;
            }
            string key = KeyForInput(plan.ReadBackVar);
            await Delay(TypedReadBackMs);
            if (Superseded())
                return;
            double? shown = await ReadFresh(sim, key, ReadoutTimeoutMs);
            if (shown is double landed && !plan.Reached(landed) && !Superseded())
            {
                Log.Debug("A350", $"{row.Key}: the click landed {A350WritePlan.Format(landed)}, not {A350WritePlan.Format(plan.Target)}; corrected");
                ArmTypedEcho(row.Key);
                Send(sim, plan.Correction.Rpn);   // e.g. a 1000 ft altitude step, or the managed speed's own preset
                await Delay(TypedReadBackMs);
                if (Superseded())
                    return;
                shown = await ReadFresh(sim, key, ReadoutTimeoutMs);
            }
            if (Superseded())
                return;
            ArmTypedEcho(row.Key);   // the confirmation is the value's one sentence: a late echo stays quiet
            if (shown is not double now)
                announcer.Announce(NotConfirmed(row, typed));   // no read-back: sent, never "selected"
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

    /// <summary>Clicks toward a value read between clicks (<see cref="A350TypedStepper"/>): said when the knob shows none,
    /// else confirmed as the cockpit shows it once reached, or what it shows after the last round.</summary>
    private async Task StepAsync(A350PlacedRow row, double typed, A350TypedPlan plan, A350TypedStepper stepper,
        Func<string, double?> read, SimConnectManager sim, ScreenReaderAnnouncer announcer, Func<bool> superseded)
    {
        double? now = null;
        for (int round = 0; round <= A350TypedStepper.MaxRounds; round++)
        {
            await Delay(A350TypedStepper.SettleMs);
            if (superseded())
                return;
            // An unread flag or value is no answer: never "shows no preset", never silence (review 2026-10-29).
            if (await ReadFresh(sim, KeyForInput(stepper.ShowVar), ReadoutTimeoutMs) is not double shownFlag)
            {
                if (!superseded())
                    announcer.Announce(NotConfirmed(row, typed));
                return;
            }
            if (shownFlag < 0.5)
            {
                announcer.Announce(stepper.NotShown);
                return;
            }
            now = await ReadFresh(sim, KeyForInput(stepper.ValueVar), ReadoutTimeoutMs);
            if (superseded())
                return;
            if (now is not double value)
            {
                announcer.Announce(NotConfirmed(row, typed));
                return;
            }
            int clicks = (int)Math.Round(stepper.Target - value);
            if (clicks == 0 || round == A350TypedStepper.MaxRounds)
                break;
            for (int i = 0; i < Math.Abs(clicks); i++)
            {
                ArmTypedEcho(row.Key);
                Send(sim, stepper.Click(Math.Sign(clicks)).Rpn);
                await Delay(A350TypedStepper.GapMs);
                if (superseded())
                    return;
            }
        }
        if (now is not double shown)
            return;
        ArmTypedEcho(row.Key);
        announcer.Announce(plan.Reached(shown)
            ? plan.Confirm(shown, read)
            : $"{LineName(row.Name)} did not take {A350WritePlan.Format(typed)}. {plan.Confirm(shown, read)}");
    }

    /// <summary>A typed value whose read-back never came: sent, never confirmed as taken (review 2026-10-29: a lost read
    /// after a missed click left the dial at 11900 and confirmed 12000).</summary>
    private static string NotConfirmed(A350PlacedRow row, double typed) =>
        $"{LineName(row.Name)} {A350WritePlan.Format(typed)} sent, not confirmed";

    /// <summary>The value to plan from: what MSFSBA just commanded while it is fresh, else the cache. "All thrust levers"
    /// is both levers, each so read: one position only while they agree, none while they are split, so a pick always
    /// writes (review 2026-10-29: its state variable is lever 1's angle, and with lever 2 elsewhere a pick sent nothing).</summary>
    private double? CurrentValue(string key, SimConnectManager sim)
    {
        if (key == A350LeverSpeech.AllLevers)
            return CurrentValue(A350LeverSpeech.Lever1, sim) is double one && CurrentValue(A350LeverSpeech.Lever2, sim) is double two
                && Math.Abs(one - two) < A350WritePlan.SameValueTolerance ? one : null;
        return _commanded.Resolve(CommandKey(key), Cached(sim, key), Clock());
    }

    /// <summary>What a row's commanded position is kept under: its state variable, so two rows that flip one variable
    /// share one hold (a hold kept per row let the second plan from a stale cache and flip the first back).</summary>
    private string CommandKey(string rowKey) =>
        _rows.TryGetValue(rowKey, out var row) && row.Control?.StateVar is { Length: > 0 } stateVar ? stateVar : rowKey;

    /// <summary>The holds a pick of a row sets: its own, and for "All thrust levers" each lever's.</summary>
    private IEnumerable<string> CommandKeys(string rowKey) => rowKey == A350LeverSpeech.AllLevers
        ? new[] { CommandKey(A350LeverSpeech.Lever1), CommandKey(A350LeverSpeech.Lever2) }
        : new[] { CommandKey(rowKey) };

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
            foreach (var key in CommandKeys(row.Key))
                _commanded.Record(key, target, Clock());
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
                        if (delay.OwesRest)
                            _owed[run] = steps.Skip(i + 1).ToList();   // a hold's release, sent if the definition goes
                        else
                            _owed.Remove(run);                          // a menu walk's next clicks: abandoned, never burst
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
        if (row.Key == A350LeverSpeech.AllLevers)
        {
            await CheckLeversMovedAsync(target, sim, announcer, pick);
            return;
        }
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

    /// <summary>"All thrust levers" read back as both levers: the one that stayed is named ("Thrust lever 2 did not move"),
    /// both "All thrust levers did not move", and each stayed lever's position is remembered. Never cut short by a delivery
    /// reaching the pick: the row's own variable is lever 1's angle, which says nothing of lever 2 (review 2026-10-29: a
    /// lever 2 that did not follow was never said); only a newer pick of the row supersedes it.</summary>
    private async Task CheckLeversMovedAsync(double target, SimConnectManager sim, ScreenReaderAnnouncer announcer, int pick)
    {
        string all = A350LeverSpeech.AllLevers;
        _pendingPicks[all] = (pick, target, false);
        bool Superseded() => !_pendingPicks.TryGetValue(all, out var p) || p.Pick != pick;
        try
        {
            await Delay(ToggleReadBackMs);
            if (_disposed || Superseded())
                return;
            var stayed = new List<int>();
            foreach (var (lever, number) in new[] { (A350LeverSpeech.Lever1, 1), (A350LeverSpeech.Lever2, 2) })
            {
                if (await ReadFresh(sim, lever, ReadoutTimeoutMs) is not double now || _disposed || Superseded())
                    return;   // unread: nothing is known to have stayed
                if (Math.Abs(now - target) >= A350WritePlan.SameValueTolerance)
                {
                    stayed.Add(number);
                    _commanded.Record(CommandKey(lever), now, Clock());
                    ReRead(lever, sim);
                }
            }
            if (stayed.Count == 0)
                return;
            ReRead(all, sim);
            announcer.Announce(stayed.Count == 2 ? "All thrust levers did not move" : $"Thrust lever {stayed[0]} did not move");
        }
        catch (Exception ex)
        {
            Log.Warn("A350", $"Read-back of the thrust levers failed: {ex.Message}");
        }
        finally
        {
            if (_pendingPicks.TryGetValue(all, out var p) && p.Pick == pick)
                _pendingPicks.Remove(all);
        }
    }
}
