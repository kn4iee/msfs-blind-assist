using MSFSBlindAssist.Utils;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>
/// The sweep's whole contact with the aircraft, injected so the session is pinned without a sim. The tool
/// (<c>tools/a330-gen/PanelSweep</c>) implements it over SimConnect: an input event set by SetInputEvent with the hash
/// the aircraft's own enumeration gave it, a calculator line through MobiFlight's channel made unique as the app's
/// <c>Write</c> makes it ([SIM-17]).
/// </summary>
internal interface ISweepIo
{
    /// <summary>False while the calculator channel does not answer (the app's <c>CalcWriteCanLand</c> false):
    /// every press and calculator line is then not sent.</summary>
    bool CalcAnswering { get; }

    /// <summary>SetInputEvent; false when the aircraft does not list the event (nothing left).</summary>
    bool TrySetInputEvent(string inputEvent, double value);

    /// <summary>One calculator line, sent once.</summary>
    void Calc(string rpn);

    /// <summary>A fresh read of a map read (<c>INI_x</c>, <c>A:NAME, unit</c>, <c>B:EVENT</c>); null when none came.</summary>
    double? Read(string read);

    /// <summary>Every snapshot variable's value now (names that did not answer are left out).</summary>
    IReadOnlyDictionary<string, double> Snapshot();

    /// <summary>The simulator's refusals of the packets sent since the last call (SimConnect exceptions), and forgets them.</summary>
    IReadOnlyList<string> TakeWriteErrors();

    long NowMs();

    void Sleep(int ms);
}

/// <summary>Where the session's output goes: each row, each journal line (written as it comes, before any write),
/// and a short line of progress.</summary>
internal sealed record SweepSinks(Action<SweepRow> Row, Action<string> Journal, Action<string> Say);

/// <summary>A plan built from the aircraft's starting reads, and the journal of every control it will touch.</summary>
internal sealed record SweepPrepared(SweepPlan Plan, IReadOnlyList<JournalEntry> Journal);

internal sealed class SweepOutcome
{
    public Dictionary<string, int> Verdicts { get; } = new(StringComparer.Ordinal);
    public List<string> RestoreFailures { get; } = new();
    /// <summary>Why the sweep stopped early (a restore that did not hold, Ctrl+C, a fault), or null.</summary>
    public string? StopReason { get; set; }
    public int Swept { get; set; }
    public int NotReached { get; set; }
    public IReadOnlySet<string> Noisy { get; set; } = new HashSet<string>();
}

/// <summary>
/// Runs a sweep plan against the aircraft (plan 04, Task 4 Steps 2 and 2b). For each planned control: every write
/// through the app's own decision (<see cref="A330Write"/>) and route, at least <see cref="SweepTiming.WriteGapMs"/>
/// apart; after each, the control's read and its proof watched until they settle (<see cref="SettleTracker"/>), a
/// snapshot before and after for the side effects, the system state, and a verdict (<see cref="SweepVerdicts"/>). Each
/// position is written twice, to find a toggle. Then every control the plan touched is put back from the journal
/// (the switch, then its guard, then an ECAM page key), read back, and the whole sweep STOPS when one did not hold.
/// A denied control is never written, whatever asks.
/// </summary>
internal sealed class SweepSession
{
    private readonly IReadOnlyList<Control> _controls;
    private readonly Dictionary<string, Control> _byKey;
    private readonly IReadOnlyDictionary<string, string> _denied;
    private readonly ISweepIo _io;
    private readonly SweepSinks _out;
    private readonly Func<string?> _stopRequested;
    private Dictionary<string, JournalEntry> _journal = new(StringComparer.Ordinal);
    private IReadOnlySet<string> _noisy = new HashSet<string>();
    private long? _lastWriteAt;

    public SweepSession(IReadOnlyList<Control> controls, IReadOnlyDictionary<string, string> denied, ISweepIo io, SweepSinks sinks,
        Func<string?>? stopRequested = null)
    {
        _controls = controls;
        _byKey = controls.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _denied = denied;
        _io = io;
        _out = sinks;
        _stopRequested = stopRequested ?? (() => null);
    }

    /// <summary>The variable a control is read by: a key's state, any other control's read.</summary>
    internal static string? ReadOf(Control c) => c.Kind == "button" ? c.State : c.Read;

    /// <summary>An ECAM page's word ("FUEL") to the key of its button, for a proof on a lower ECAM page.</summary>
    internal static Dictionary<string, string> EcamPageKeys(IEnumerable<Control> controls) =>
        controls.Where(c => c.Kind == "button" && c.Key.StartsWith("A330_ECAM_", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(c.State) && c.Label.StartsWith("ECAM ", StringComparison.Ordinal))
            .GroupBy(c => c.Label[5..].Trim()).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

    /// <summary>The keys a control's steps write, in the order they are first written (its ECAM page key, its guard, itself).</summary>
    internal static IReadOnlyList<string> Touched(ControlPlan cp) => cp.Steps.Select(s => s.ControlKey).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Reads every control's start (its read, and an input event's own value), plans, and journals every key the
    /// plan will write. A control a key of which cannot be journaled is skipped: what cannot be put back is never written.
    /// <paramref name="include"/> keeps only some controls (a pass, <c>--only</c>, <c>--from</c>).</summary>
    public SweepPrepared Prepare(Func<ControlPlan, bool>? include = null)
    {
        var starts = new Dictionary<string, double?>(StringComparer.Ordinal);
        var events = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (var c in _controls)
        {
            if (_denied.ContainsKey(c.Key)) continue;
            if (ReadOf(c) is { Length: > 0 } read) starts[c.Key] = _io.Read(read);
            if (c.Kind != "button" && !string.IsNullOrEmpty(c.Event)) events[c.Key] = _io.Read("B:" + c.Event);
        }

        var plan = SweepPlanner.Plan(_controls, starts, _denied, EcamPageKeys(_controls));
        var kept = new List<ControlPlan>();
        var journal = new List<JournalEntry>();
        var journaled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cp in plan.Controls)
        {
            if (include != null && !include(cp)) continue;
            if (!cp.IsPlanned) { kept.Add(cp); continue; }
            var mine = new List<JournalEntry>();
            string? why = null;
            foreach (string key in Touched(cp))
            {
                var entry = _byKey.TryGetValue(key, out var kc) ? SweepJournal.EntryFor(kc, events.GetValueOrDefault(key), starts.GetValueOrDefault(key)) : null;
                if (entry == null) { why = $"{key}'s starting value could not be read, so it could not be put back"; break; }
                mine.Add(entry);
            }
            if (why != null) { kept.Add(cp with { Steps = Array.Empty<SweepStep>(), SkipReason = why }); continue; }
            foreach (var e in mine) if (journaled.Add(e.Key)) journal.Add(e);
            kept.Add(cp);
        }
        return new SweepPrepared(new SweepPlan(kept), journal);
    }

    /// <summary>Writes the journal, takes the noise baseline, then sweeps every planned control in order.</summary>
    public SweepOutcome Run(SweepPrepared prepared)
    {
        var outcome = new SweepOutcome();
        _journal = prepared.Journal.ToDictionary(e => e.Key, StringComparer.Ordinal);
        _out.Journal(SweepJournal.Header());
        foreach (var e in prepared.Journal) _out.Journal(SweepJournal.Line(e));

        var first = _io.Snapshot();
        _io.Sleep(SweepTiming.NoiseWindowMs);
        _noisy = outcome.Noisy = SweepSnapshot.Noisy(first, _io.Snapshot());
        if (_noisy.Count > 0)
            _out.Journal($"# moved with nothing written (left out of the side effects): {string.Join(", ", _noisy.OrderBy(n => n, StringComparer.Ordinal))}\n");

        int total = prepared.Plan.PlannedCount, number = 0;
        foreach (var cp in prepared.Plan.Controls)
        {
            if (!cp.IsPlanned)
            {
                _out.Row(new SweepRow { Key = cp.Key, Label = cp.Label, Verdict = "skipped: " + cp.SkipReason });
                continue;
            }
            number++;
            if (outcome.StopReason != null)
            {
                _out.Row(new SweepRow { Key = cp.Key, Label = cp.Label, Verdict = $"not reached ({outcome.StopReason})" });
                outcome.NotReached++;
                continue;
            }
            _out.Say(FormattableString.Invariant($"[{number}/{total}] {cp.Key} ({cp.Label})"));
            RunControl(cp, outcome);
        }
        return outcome;
    }

    /// <summary>A row's word when the control already read the target and nothing was sent.</summary>
    internal const string AlreadyThere = "already there";

    private void RunControl(ControlPlan cp, SweepOutcome outcome)
    {
        var steps = cp.Steps;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                // a repeat goes with its SetPosition; the start and the guard's close come from the journal below
                if (s.Kind is SweepStepKind.RepeatPosition or SweepStepKind.ReturnToStart or SweepStepKind.CloseGuard) continue;
                if (_stopRequested() is string stop)
                {
                    outcome.StopReason ??= stop;
                    _out.Row(new SweepRow { Key = cp.Key, Label = cp.Label, Verdict = $"interrupted ({stop})" });
                    break;
                }
                var target = _byKey[s.ControlKey];
                string verdict = s.Kind switch
                {
                    SweepStepKind.SetPosition or SweepStepKind.OpenGuard => WritePosition(target, s,
                        repeat: i + 1 < steps.Count && steps[i + 1].Kind == SweepStepKind.RepeatPosition && steps[i + 1].ControlKey == s.ControlKey),
                    _ => Press(target, s),
                };
                outcome.Verdicts[verdict] = outcome.Verdicts.GetValueOrDefault(verdict) + 1;
                // a switch behind a guard that did not open is never written: the rest of this control is skipped
                if (s.Kind == SweepStepKind.OpenGuard && !SweepVerdicts.IsPass(verdict) && verdict != AlreadyThere)
                {
                    _out.Row(new SweepRow { Key = cp.Key, Label = cp.Label, Verdict = $"skipped: its guard did not open ({verdict})" });
                    _out.Say($"  {cp.Key}: skipped, its guard {s.ControlKey} did not open ({verdict})");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            outcome.StopReason ??= $"{cp.Key}: {ex.Message}";
            _out.Row(new SweepRow { Key = cp.Key, Label = cp.Label, Verdict = "error: " + ex.Message });
            _out.Say($"  {cp.Key}: ERROR {ex.Message}");
        }
        finally
        {
            // everything this control's steps wrote, put back from the journal: itself, then its guard, then its page key
            foreach (string key in Touched(cp).Reverse())
            {
                bool held;
                try { held = Restore(key); }
                catch (Exception ex)
                {
                    held = false;
                    _out.Row(new SweepRow { Key = key, Label = LabelOf(key), Position = "(restore) start", Verdict = "RESTORE FAILED: " + ex.Message });
                }
                if (!held)
                {
                    outcome.RestoreFailures.Add($"{key} ({LabelOf(key)})");
                    outcome.StopReason ??= $"the restore of {key} did not hold";
                    _out.Say($"  RESTORE FAILED: {key} ({LabelOf(key)}): put it back by hand.");
                }
            }
            outcome.Swept++;
        }
    }

    /// <summary>One position (or an opened guard): read, plan with the app's decision, snapshot, send, settle, snapshot,
    /// write it again where planned, judge, record. Returns the row's verdict.</summary>
    private string WritePosition(Control c, SweepStep s, bool repeat)
    {
        string read = ReadOf(c) ?? throw new InvalidOperationException($"{c.Key} has no read");
        string position = $"{PositionLabel(c, s.ExpectedRead)}(write {SweepTsv.Num(s.Input)})";
        double? before = _io.Read(read);
        if (before == null) return Record(c, position, verdict: "not read before the write");

        var plan = A330Write.ToPosition(c, before, s.ExpectedRead ?? s.Input!.Value);
        if (plan.Outcome == A330WriteOutcome.AtPosition) return Record(c, position, verdict: AlreadyThere, readBack: before);

        var proof = ProofOf(c, s.ExpectedRead, before);
        double? proofBefore = proof.Reach == ProofReach.Variable ? _io.Read(c.Proof!.Read!) : null;
        var snapBefore = _io.Snapshot();
        var sent = Dispatch(c.Key, plan);
        if (!sent.Sent)
            return Judge(c, position, sent.Text, new WriteObservation { Sent = false, NotSentReason = sent.Reason, ReadBefore = before }, proof, snapBefore, null);

        // a proof not read before has nothing to settle from: it is read once afterwards, for the row, and never taken as moved
        bool trackProof = proof.Reach == ProofReach.Variable && proofBefore != null;
        var channels = new List<SettleChannel> { new(() => _io.Read(read), before) };
        if (trackProof) channels.Add(new(() => _io.Read(c.Proof!.Read!), proofBefore));
        var settled = SettleTracker.Track(channels, _io.NowMs, _io.Sleep);
        var refused = _io.TakeWriteErrors();
        var snapAfter = _io.Snapshot();
        SettleResult? proofAfter = trackProof ? settled[1]
            : proof.Reach == ProofReach.Variable ? new SettleResult(_io.Read(c.Proof!.Read!), null, Moved: false, TimedOut: false) : null;
        proof = proof with { Before = proofBefore, After = proofAfter };
        if (refused.Count > 0)
            return Judge(c, position, sent.Text, new WriteObservation { Sent = false, NotSentReason = "the simulator refused it: " + string.Join(", ", refused), ReadBefore = before },
                proof, snapAfter, settled[0]);

        SettleResult? again = null;
        string? repeatNote = null;
        if (repeat && !settled[0].Moved) repeatNote = "not written again: the first write moved nothing";
        else if (repeat)
        {
            var second = Dispatch(c.Key, plan);
            if (second.Sent)
                again = SettleTracker.Track(() => _io.Read(read), settled[0].Value, _io.NowMs, _io.Sleep, SweepTiming.RepeatQuietMs(settled[0].SettledMs));
            else repeatNote = "the second write was not sent: " + second.Reason;
        }

        var effects = SweepSnapshot.Moved(snapBefore, snapAfter, new[] { LVarName(read), LVarName(c.Proof?.Read) }, _noisy);
        var observation = new WriteObservation
        {
            ReadBefore = before, Read = settled[0], ExpectedRead = s.ExpectedRead, Positions = c.Positions ?? new(),
            Proof = proof, Repeat = again, SideEffects = effects.Count,
        };
        return Judge(c, position, sent.Text, observation, proof, snapAfter, settled[0], again, repeatNote, effects);
    }

    /// <summary>One press of a key (its state is its read): sent through the app's own press.</summary>
    private string Press(Control c, SweepStep s)
    {
        string state = ReadOf(c) ?? throw new InvalidOperationException($"{c.Key} has no state");
        string position = s.Kind switch
        {
            SweepStepKind.PressEcamPage => "press (the ECAM page for the proof)",
            SweepStepKind.RestorePress => "press again (cycling back)",
            _ => "press",
        };
        double? before = _io.Read(state);
        var snapBefore = _io.Snapshot();
        var sent = Dispatch(c.Key, A330Write.Press(c, 1));
        var none = new ProofReading(ProofReach.None);
        if (!sent.Sent)
            return Judge(c, position, sent.Text, new WriteObservation { Sent = false, NotSentReason = sent.Reason, ReadBefore = before }, none, snapBefore, null);
        var settled = SettleTracker.Track(() => _io.Read(state), before, _io.NowMs, _io.Sleep);
        var refused = _io.TakeWriteErrors();
        var snapAfter = _io.Snapshot();
        var effects = SweepSnapshot.Moved(snapBefore, snapAfter, new[] { LVarName(state) }, _noisy);
        var observation = refused.Count > 0
            ? new WriteObservation { Sent = false, NotSentReason = "the simulator refused it: " + string.Join(", ", refused), ReadBefore = before }
            : new WriteObservation { ReadBefore = before, Read = settled, SideEffects = effects.Count };
        return Judge(c, position, sent.Text, observation, none, snapAfter, settled, effects: effects);
    }

    /// <summary>Classifies a write and records its row (with the system state of <paramref name="snapAfter"/>).</summary>
    private string Judge(Control c, string position, string sentText, WriteObservation o, ProofReading proof,
        IReadOnlyDictionary<string, double> snapAfter, SettleResult? read,
        SettleResult? again = null, string? repeatNote = null, IReadOnlyList<SideEffect>? effects = null)
    {
        var j = SweepVerdicts.Classify(o);
        string note = string.Join("; ", new[] { j.Note, repeatNote }.Where(n => !string.IsNullOrEmpty(n)));
        string? valueName = SweepSnapshot.AircraftValues.GetValueOrDefault(c.Key);
        _out.Row(new SweepRow
        {
            Key = c.Key, Label = c.Label, Position = position, Sent = sentText, ReadBack = read?.Value, SettleMs = read?.SettledMs,
            ProofBefore = ProofText(proof, proof.Before), ProofAfter = ProofText(proof, proof.After?.Value), ProofSettleMs = proof.After?.SettledMs,
            Verdict = j.Word, Note = note, SecondReadBack = again?.Value,
            AircraftValueName = valueName, AircraftValue = valueName != null && snapAfter.TryGetValue(valueName, out double v) ? v : null,
            SideEffects = effects ?? Array.Empty<SideEffect>(), SystemState = SweepSnapshot.SystemState(_controls, snapAfter),
        });
        if (!SweepVerdicts.IsPass(j.Verdict)) _out.Say($"  {c.Key} {position}: {j.Word}{(note.Length > 0 ? " (" + note + ")" : "")}");
        return j.Word;
    }

    private string Record(Control c, string position, string verdict, double? readBack = null)
    {
        _out.Row(new SweepRow { Key = c.Key, Label = c.Label, Position = position, ReadBack = readBack, Verdict = verdict });
        if (verdict != AlreadyThere) _out.Say($"  {c.Key} {position}: {verdict}");
        return verdict;
    }

    /// <summary>Puts one journaled control back and reads it back; false when it did not hold.</summary>
    public bool Restore(string key)
    {
        if (!_journal.TryGetValue(key, out var e) || !_byKey.TryGetValue(key, out var c))
        {
            _out.Row(new SweepRow { Key = key, Position = "(restore) start", Verdict = "RESTORE FAILED: not in the journal" });
            return false;
        }
        return Restore(e, c);
    }

    /// <summary>Puts a journal entry back (also <c>--restore</c>'s path): its own starting value, never the pairing.</summary>
    public bool Restore(JournalEntry e, Control c)
    {
        int actions = 0;
        var sentTexts = new List<string>();
        while (true)
        {
            double? own = e.Kind == JournalKind.InputEvent ? _io.Read("B:" + e.Event) : null;
            double? read = e.Read != null ? _io.Read(e.Read) : null;
            string note = e.Kind == JournalKind.InputEvent
                ? $"{e.Event} {SweepTsv.Num(own)} (start {SweepTsv.Num(e.EventValue)})" + (e.Read != null ? $", {e.Read} {SweepTsv.Num(read)} (start {SweepTsv.Num(e.ReadValue)})" : "")
                : $"{e.Read} {SweepTsv.Num(read)} (start {SweepTsv.Num(e.ReadValue)})";
            switch (JournalRestore.Next(e, c, own, read, actions))
            {
                case RestoreStep.Held:
                    _out.Row(new SweepRow { Key = e.Key, Label = c.Label, Position = "(restore) start", Sent = string.Join(" | ", sentTexts), ReadBack = read ?? own,
                        Verdict = actions == 0 ? "at start" : "restored", Note = note });
                    return true;
                case RestoreStep.Failed f:
                    _out.Row(new SweepRow { Key = e.Key, Label = c.Label, Position = "(restore) start", Sent = string.Join(" | ", sentTexts), ReadBack = read ?? own,
                        Verdict = "RESTORE FAILED: " + f.Why, Note = note });
                    return false;
                case RestoreStep.Write w:
                    var sent = Dispatch(e.Key, w.Plan);
                    actions++;
                    sentTexts.Add(sent.Text);
                    if (!sent.Sent)
                    {
                        _out.Row(new SweepRow { Key = e.Key, Label = c.Label, Position = "(restore) start", Sent = string.Join(" | ", sentTexts),
                            Verdict = "RESTORE FAILED: not sent: " + sent.Reason, Note = note });
                        return false;
                    }
                    var channels = new List<SettleChannel>();
                    if (e.Kind == JournalKind.InputEvent) channels.Add(new(() => _io.Read("B:" + e.Event), own));
                    if (e.Read != null) channels.Add(new(() => _io.Read(e.Read), read));
                    if (channels.Count > 0) SettleTracker.Track(channels, _io.NowMs, _io.Sleep);
                    _io.TakeWriteErrors();
                    break;
            }
        }
    }

    private sealed record Dispatched(bool Sent, string? Reason, string Text);

    /// <summary>Sends a plan's calls by the app's routes: an input event by SetInputEvent; a press as the event's own
    /// <c>_Push</c>, then after its hold <c>_Release</c>, over the calculator (as <c>IniA330Definition.PushEvent</c>
    /// writes them); a calculator line as it is. Never a write to a denied control.</summary>
    private Dispatched Dispatch(string key, A330WritePlan plan)
    {
        if (_denied.TryGetValue(key, out var why))
            throw new InvalidOperationException($"{key} is denied ({why}): it is never written");
        string text = string.Join(" | ", plan.Calls.Select(SweepTsv.Describe));
        if (plan.Outcome == A330WriteOutcome.Refused)
            return new(false, "the app's write decision refused it" + (plan.Reason is { } r ? ": " + r : ""), text);
        _io.TakeWriteErrors();
        foreach (var call in plan.Calls)
        {
            if (call is not A330InputEventCall && !_io.CalcAnswering)
                return new(false, "the calculator channel is not answering (the app's CalcWriteCanLand would be false)", text);
            WaitForGap();
            switch (call)
            {
                case A330InputEventCall ie:
                    if (!_io.TrySetInputEvent(ie.Event, ie.Value)) return new(false, $"the aircraft does not list {ie.Event}", text);
                    break;
                case A330PressCall p:
                    _io.Calc($"(>B:{p.Event}_Push)");
                    _io.Sleep(p.HoldMs);
                    _io.Calc($"(>B:{p.Event}_Release)");
                    break;
                case A330CalcCall cc:
                    _io.Calc(cc.Rpn);
                    break;
            }
            _lastWriteAt = _io.NowMs();
        }
        return new(true, null, text);
    }

    private void WaitForGap()
    {
        if (_lastWriteAt is not long last) return;
        long since = _io.NowMs() - last;
        if (since < SweepTiming.WriteGapMs) _io.Sleep((int)(SweepTiming.WriteGapMs - since));
    }

    /// <summary>A control's proof as the runner reaches it. A lamp or simvar proof is shown while it reads over 0.5;
    /// it should show at the position whose read value is its <c>when</c>: at the position written, and at the one the
    /// control read before the write.</summary>
    private static ProofReading ProofOf(Control c, double? expectedRead, double? readBefore)
    {
        if (c.Proof is not { } p) return new(ProofReach.None);
        if (p.Kind is "ewd_memo" or "sd_page") return new(ProofReach.DisplayReader, Text: p.Text ?? p.Page);
        if (p.Kind is "lamp" or "simvar" && !string.IsNullOrWhiteSpace(p.Read))
        {
            bool known = NumberParsing.TryParseExternal(p.When, out double when);
            bool? ShowsAt(double? read) => known && read is double r ? SweepTiming.Same(r, when) : null;
            return new(ProofReach.Variable, ExpectedShown: ShowsAt(expectedRead), Text: p.Text, ExpectedShownBefore: ShowsAt(readBefore));
        }
        return new(ProofReach.None);
    }

    private static string ProofText(ProofReading proof, double? value) => proof.Reach switch
    {
        ProofReach.Variable => SweepTsv.Num(value),
        ProofReach.DisplayReader => "needs the display reader",
        _ => "",
    };

    private static string? LVarName(string? read)
    {
        if (string.IsNullOrWhiteSpace(read)) return null;
        var (kind, name, _) = A330ControlMap.ParseReadKind(read);
        return kind == A330ControlMap.ReadKind.LVar ? name : null;
    }

    private static string PositionLabel(Control c, double? expectedRead) =>
        expectedRead is double v && c.Positions?.FirstOrDefault(p => SweepTiming.Same(p.Value, v)) is { } p ? p.Label + " " : "";

    private string LabelOf(string key) => _byKey.TryGetValue(key, out var c) ? c.Label : "";
}
