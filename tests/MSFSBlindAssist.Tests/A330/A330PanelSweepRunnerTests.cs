using System.Text;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using Xunit;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;
using Position = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Position;

namespace MSFSBlindAssist.Tests;

/// <summary>The panel sweep's runner core (plan 04, Task 4 Steps 2 and 2b): the verdicts, the settle
/// tracker, the journal and its restore, the snapshot diff, the row format and the session that drives
/// them, on in-memory controls and a fake aircraft. Nothing here touches a simulator.</summary>
public class A330PanelSweepRunnerTests
{
    private static Position P(double value, double input, string label) => new() { Value = value, Input = input, Label = label };

    private static Control Select(string key, params Position[] ps) => new()
    {
        Key = key, Label = key, Kind = "select", Event = "EV_" + key, Read = "INI_" + key, Positions = ps.ToList(),
    };

    private static SettleResult Moved(double to, int ms = 200) => new(to, ms, Moved: true, TimedOut: false);

    private static SettleResult Still(double at) => new(at, null, Moved: false, TimedOut: false);

    // ---------------- verdicts ----------------

    [Fact]
    public void A_proof_that_follows_the_positions_name_is_proven()
    {
        // Off is read 1; the memo shows at Off. Written from On to Off, the read went 0 -> 1 and the memo came on.
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 0, Read = Moved(1), ExpectedRead = 1,
            Proof = new ProofReading(ProofReach.Variable, Before: 0, After: Moved(1), ExpectedShown: true, ExpectedShownBefore: false),
        });
        Assert.Equal(SweepVerdict.Proven, j.Verdict);
        Assert.Equal("proven", j.Word);
    }

    [Fact]
    public void A_proof_that_follows_the_other_name_is_inverted()
    {
        // Written from Off to On (the memo should go off), the memo came on: it followed the other name.
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 1, Read = Moved(0), ExpectedRead = 0,
            Proof = new ProofReading(ProofReach.Variable, Before: 0, After: Moved(1), ExpectedShown: false, ExpectedShownBefore: true),
        });
        Assert.Equal(SweepVerdict.Inverted, j.Verdict);
    }

    [Fact]
    public void A_proof_that_cannot_tell_the_two_positions_apart_leaves_the_row_read_only()
    {
        // A three-position selector, B to C, the memo showing only at A: the memo stays off, as it should, and that
        // says nothing about the names. It is never proven.
        var ps = new[] { P(0, 0, "A"), P(1, 1, "B"), P(2, 2, "C") };
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 1, Read = Moved(2), ExpectedRead = 2, Positions = ps,
            Proof = new ProofReading(ProofReach.Variable, Before: 0, After: Still(0), ExpectedShown: false, ExpectedShownBefore: false),
        });
        Assert.Equal(SweepVerdict.ReadOnly, j.Verdict);
        Assert.Contains("cannot tell", j.Note);

        // and a memo that came on between two positions where it should stay off is no pass
        var on = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 1, Read = Moved(2), ExpectedRead = 2, Positions = ps,
            Proof = new ProofReading(ProofReach.Variable, Before: 0, After: Moved(1), ExpectedShown: false, ExpectedShownBefore: false),
        });
        Assert.False(SweepVerdicts.IsPass(on.Verdict));
    }

    [Fact]
    public void A_proof_that_should_have_changed_and_did_not_is_not_a_pass()
    {
        // Written from On to Off, the read (the control's own variable) moved, but the memo never came on: the
        // aircraft may have ignored the write.
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 0, Read = Moved(1), ExpectedRead = 1,
            Proof = new ProofReading(ProofReach.Variable, Before: 0, After: Still(0), ExpectedShown: true, ExpectedShownBefore: false),
        });
        Assert.False(SweepVerdicts.IsPass(j.Verdict));
        Assert.Equal(SweepVerdict.Dead, j.Verdict);
        Assert.Contains("did not", j.Note);
    }

    [Fact]
    public void A_proof_not_read_before_the_write_is_never_counted_as_moved()
    {
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 0, Read = Moved(1), ExpectedRead = 1,
            Proof = new ProofReading(ProofReach.Variable, Before: null, After: Still(1), ExpectedShown: true, ExpectedShownBefore: false),
        });
        Assert.NotEqual(SweepVerdict.Proven, j.Verdict);
        Assert.Contains("not read before", j.Note);

        // nor does the settle tracker call a reading moved when there was nothing to compare it with
        long now = 0;
        var r = SettleTracker.Track(() => 1, baseline: null, () => now, ms => now += ms, quietMs: 300);
        Assert.False(r.Moved);
    }

    [Fact]
    public void A_read_that_moves_with_no_proof_is_read_only()
    {
        var j = SweepVerdicts.Classify(new WriteObservation { ReadBefore = 0, Read = Moved(1), ExpectedRead = 1 });
        Assert.Equal(SweepVerdict.ReadOnly, j.Verdict);
        Assert.Equal("read-only", j.Word);
    }

    [Fact]
    public void A_proof_only_the_display_reader_sees_is_never_proven_by_the_runner()
    {
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 0, Read = Moved(1), ExpectedRead = 1,
            Proof = new ProofReading(ProofReach.DisplayReader, Text: "PRED W/S OFF"),
        });
        Assert.Equal(SweepVerdict.ReadOnly, j.Verdict);
        Assert.Contains("needs the display reader", j.Note);
    }

    [Fact]
    public void A_write_that_moves_nothing_is_dead()
    {
        var j = SweepVerdicts.Classify(new WriteObservation { ReadBefore = 0, Read = Still(0), ExpectedRead = 1 });
        Assert.Equal(SweepVerdict.Dead, j.Verdict);

        // something else moving is not "nothing moved": the read is the suspect then
        var other = SweepVerdicts.Classify(new WriteObservation { ReadBefore = 0, Read = Still(0), ExpectedRead = 1, SideEffects = 1 });
        Assert.NotEqual(SweepVerdict.Dead, other.Verdict);
    }

    [Fact]
    public void A_pass_that_settles_after_one_and_a_half_seconds_is_lagged()
    {
        var j = SweepVerdicts.Classify(new WriteObservation { ReadBefore = 0, Read = Moved(1, ms: 2300), ExpectedRead = 1 });
        Assert.Equal(SweepVerdict.Lagged, j.Verdict);
        Assert.Contains("read-only", j.Note);   // still a pass: what it would have been
        Assert.Contains("2300", j.Note);
        Assert.True(SweepVerdicts.IsPass(j.Verdict));

        // at the threshold it is not lagged
        Assert.Equal(SweepVerdict.ReadOnly, SweepVerdicts.Classify(new WriteObservation { ReadBefore = 0, Read = Moved(1, ms: SweepTiming.LaggedMs), ExpectedRead = 1 }).Verdict);
    }

    [Fact]
    public void A_second_write_of_the_same_position_that_flips_the_read_back_is_a_toggle()
    {
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 0, Read = Moved(1), ExpectedRead = 1, Repeat = Moved(0),
        });
        Assert.Equal(SweepVerdict.Toggle, j.Verdict);

        // a second write that holds is no toggle
        Assert.Equal(SweepVerdict.ReadOnly, SweepVerdicts.Classify(new WriteObservation
        {
            ReadBefore = 0, Read = Moved(1), ExpectedRead = 1, Repeat = Still(1),
        }).Verdict);
    }

    [Fact]
    public void A_write_refused_before_it_left_is_not_sent_never_dead()
    {
        var j = SweepVerdicts.Classify(new WriteObservation
        {
            Sent = false, NotSentReason = "the aircraft does not list EV_X", ReadBefore = 0, Read = Still(0), ExpectedRead = 1,
        });
        Assert.Equal(SweepVerdict.NotSent, j.Verdict);
        Assert.Equal("not sent", j.Word);
        Assert.Contains("does not list", j.Note);
    }

    [Fact]
    public void A_read_that_lands_on_another_position_is_mislabeled()
    {
        var ps = new[] { P(0, 0, "A"), P(1, 1, "B"), P(2, 2, "C") };
        var j = SweepVerdicts.Classify(new WriteObservation { ReadBefore = 1, Read = Moved(2), ExpectedRead = 0, Positions = ps });
        Assert.Equal(SweepVerdict.Mislabeled, j.Verdict);
        Assert.Contains("C", j.Note);
    }

    // ---------------- settle tracker ----------------

    private sealed class Clock
    {
        public long Now;
        public long NowMs() => Now;
        public void Sleep(int ms) => Now += ms;
    }

    [Fact]
    public void The_settle_tracker_waits_until_two_reads_agree_or_the_limit()
    {
        // The aircraft is slow to start: two equal reads of the OLD value are not a settle.
        var clock = new Clock();
        var reads = new Queue<double?>(new double?[] { 0, 0, 1, 1 });
        var r = SettleTracker.Track(() => reads.Count > 1 ? reads.Dequeue() : reads.Peek(), baseline: 0, clock.NowMs, clock.Sleep);
        Assert.Equal(1, r.Value);
        Assert.True(r.Moved);
        Assert.Equal(300, r.SettledMs);   // polled every 100 ms: the settled value was first seen at 300 ms
        Assert.Equal(400, clock.Now);     // and confirmed by the read after it

        // a read that keeps changing: given up at the limit, not settled
        clock = new Clock();
        int n = 0;
        var moving = SettleTracker.Track(() => n++, baseline: -1, clock.NowMs, clock.Sleep);
        Assert.True(moving.TimedOut);
        Assert.Null(moving.SettledMs);
        Assert.Equal(SweepTiming.LimitMs, clock.Now);

        // a read that never moves: held for the quiet window, then settled where it was, with no settle time
        clock = new Clock();
        var still = SettleTracker.Track(() => 0, baseline: 0, clock.NowMs, clock.Sleep, quietMs: 1500);
        Assert.False(still.Moved);
        Assert.Null(still.SettledMs);
        Assert.Equal(1500, clock.Now);

        // two channels: done only when both settle
        clock = new Clock();
        var a = new Queue<double?>(new double?[] { 1, 1 });
        var b = new Queue<double?>(new double?[] { 0, 0, 0, 5, 5 });
        var both = SettleTracker.Track(new[]
        {
            new SettleChannel(() => a.Count > 1 ? a.Dequeue() : a.Peek(), 0),
            new SettleChannel(() => b.Count > 1 ? b.Dequeue() : b.Peek(), 0),
        }, clock.NowMs, clock.Sleep);
        Assert.Equal(100, both[0].SettledMs);
        Assert.Equal(400, both[1].SettledMs);
        Assert.Equal(500, clock.Now);
    }

    [Fact]
    public void A_second_write_waits_twice_the_first_writes_settle_time_within_the_limits()
    {
        Assert.Equal(SweepTiming.LaggedMs, SweepTiming.RepeatQuietMs(200));
        Assert.Equal(4000, SweepTiming.RepeatQuietMs(2000));
        Assert.Equal(SweepTiming.LimitMs, SweepTiming.RepeatQuietMs(4000));
        Assert.Equal(SweepTiming.LimitMs, SweepTiming.RepeatQuietMs(null));
    }

    // ---------------- journal and restore ----------------

    [Fact]
    public void The_journal_restores_by_the_events_own_value_not_through_the_pairing()
    {
        // The map pairs read 1 (On) with input 0. The aircraft's own input event read 1 at the start,
        // so the pairing is wrong: restoring "On" through it would write 0 and leave the switch off.
        var pack = Select("PACK", P(1, 0, "On"), P(0, 1, "Off"));
        var entry = SweepJournal.EntryFor(pack, eventValue: 1, readValue: 1);
        Assert.NotNull(entry);

        var parsed = Assert.Single(SweepJournal.Parse(SweepJournal.Header() + SweepJournal.Line(entry!)));
        Assert.Equal(entry, parsed);

        var write = Assert.IsType<RestoreStep.Write>(JournalRestore.Next(parsed, pack, eventNow: 0, readNow: 0, actionsSoFar: 0));
        var call = Assert.IsType<A330InputEventCall>(Assert.Single(write.Plan.Calls));
        Assert.Equal("EV_PACK", call.Event);
        Assert.Equal(1, call.Value);
        Assert.NotEqual(A330ControlMap.InputFor(pack, 1), call.Value);

        Assert.IsType<RestoreStep.Held>(JournalRestore.Next(parsed, pack, eventNow: 1, readNow: 1, actionsSoFar: 1));
        // the event back at its start but its read not: not held
        Assert.IsNotType<RestoreStep.Held>(JournalRestore.Next(parsed, pack, eventNow: 1, readNow: 0, actionsSoFar: 1));
        // never written more than twice
        Assert.IsType<RestoreStep.Failed>(JournalRestore.Next(parsed, pack, eventNow: 0, readNow: 0, actionsSoFar: JournalRestore.MaxWrites));
        // an input event whose own value was not read cannot be put back: not journaled
        Assert.Null(SweepJournal.EntryFor(pack, eventValue: null, readValue: 1));
    }

    [Fact]
    public void A_multi_press_key_is_restored_by_its_presses_to_cycle()
    {
        var chrono = new Control { Key = "CH", Kind = "button", Event = "EV_CH", Mode = "push", State = "INI_CH", PressesToCycle = 3 };
        var entry = SweepJournal.EntryFor(chrono, eventValue: null, readValue: 0)!;
        Assert.Equal(JournalKind.Press, entry.Kind);
        Assert.Equal(entry, Assert.Single(SweepJournal.Parse(SweepJournal.Line(entry))));

        var first = Assert.IsType<RestoreStep.Write>(JournalRestore.Next(entry, chrono, null, readNow: 1, actionsSoFar: 0));
        Assert.Equal("EV_CH", Assert.IsType<A330PressCall>(Assert.Single(first.Plan.Calls)).Event);
        Assert.IsType<RestoreStep.Write>(JournalRestore.Next(entry, chrono, null, readNow: 2, actionsSoFar: 1));
        Assert.IsType<RestoreStep.Held>(JournalRestore.Next(entry, chrono, null, readNow: 0, actionsSoFar: 2));
        // two presses are all a cycle of three allows
        Assert.IsType<RestoreStep.Failed>(JournalRestore.Next(entry, chrono, null, readNow: 1, actionsSoFar: 2));

        // a key with no count is a push-on/push-off key: one press back, no more
        var key = new Control { Key = "K", Kind = "button", Event = "EV_K", Mode = "push", State = "INI_K" };
        var k = SweepJournal.EntryFor(key, null, 0)!;
        Assert.IsType<RestoreStep.Write>(JournalRestore.Next(k, key, null, readNow: 1, actionsSoFar: 0));
        Assert.IsType<RestoreStep.Failed>(JournalRestore.Next(k, key, null, readNow: 1, actionsSoFar: 1));
    }

    [Fact]
    public void The_journal_refuses_a_line_it_cannot_read()
    {
        Assert.Throws<FormatException>(() => SweepJournal.Parse("start\tK\tinput-event\tEV\n"));
        Assert.Throws<FormatException>(() => SweepJournal.Parse("restored\tK\n"));
        Assert.Empty(SweepJournal.Parse("# a comment\n\n"));
    }

    // ---------------- snapshot ----------------

    [Fact]
    public void A_variable_that_moved_besides_the_read_is_a_side_effect()
    {
        var before = new Dictionary<string, double> { ["INI_OWN"] = 0, ["INI_RMP1_NAV"] = 0, ["INI_STILL"] = 4, ["INI_CLOCK"] = 10 };
        var after = new Dictionary<string, double> { ["INI_OWN"] = 1, ["INI_RMP1_NAV"] = 1, ["INI_STILL"] = 4, ["INI_CLOCK"] = 11 };
        var noisy = SweepSnapshot.Noisy(new Dictionary<string, double> { ["INI_CLOCK"] = 9 }, new Dictionary<string, double> { ["INI_CLOCK"] = 10 });

        var moved = SweepSnapshot.Moved(before, after, own: new[] { "INI_OWN" }, noisy);

        var effect = Assert.Single(moved);
        Assert.Equal(new SideEffect("INI_RMP1_NAV", 0, 1), effect);

        // the snapshot covers every L:var the map names (reads, lamps, states, proofs), the aircraft's own
        // values the runner records, and the extra names given; no input event or simvar
        var ctl = Select("SEL", P(0, 0, "A"), P(1, 1, "B"));
        ctl.Lamps = new() { new A330ControlMap.Lamp { Read = "INI_SEL_LAMP", Legend = "fault" } };
        var eventRead = new Control { Key = "B", Kind = "select", Read = "B:AIRLINER_B" };
        var names = SweepSnapshot.Names(new[] { ctl, eventRead }, SweepSnapshot.ParseVarsFile("# extra\nL:INI_EXTRA\n\n"));
        Assert.Contains("INI_SEL", names);
        Assert.Contains("INI_SEL_LAMP", names);
        Assert.Contains("INI_EXTRA", names);
        Assert.Contains("INI_LANDING_ELEVATION", names);
        Assert.Contains("INI_WXR_GAIN", names);
        Assert.DoesNotContain("AIRLINER_B", names);
    }

    [Fact]
    public void The_apu_bleed_pass_sweeps_only_the_packs_bleeds_and_crossbleed_and_needs_the_apu_bleed_on()
    {
        Assert.Equal(new[] { "A330_PACK1", "A330_PACK2", "A330_ENG1_BLEED", "A330_ENG2_BLEED", "A330_XBLEED" }, SweepSnapshot.ApuBleedPassKeys);
        var apuBleed = Select("A330_APU_BLEED", P(0, 0, "Off"), P(1, 1, "On"));
        Assert.True(SweepSnapshot.ApuBleedIsOn(apuBleed, 1));
        Assert.False(SweepSnapshot.ApuBleedIsOn(apuBleed, 0));
        Assert.False(SweepSnapshot.ApuBleedIsOn(apuBleed, null));
    }

    // ---------------- the row ----------------

    [Fact]
    public void A_rows_numbers_are_invariant_under_a_comma_culture()
    {
        var row = new SweepRow
        {
            Key = "A330_WX_GAIN", Label = "Weather radar gain", Position = "its middle (write 50)",
            Sent = SweepTsv.Describe(new A330InputEventCall("AIRLINER_WX_GAIN", 0.5)),
            ReadBack = 0.5, SecondReadBack = 0.25, SettleMs = 1200, ProofBefore = SweepTsv.Num(0.5), ProofAfter = SweepTsv.Num(1.5),
            Verdict = "read-only", AircraftValueName = "INI_WXR_GAIN", AircraftValue = 7.5,
            SideEffects = new[] { new SideEffect("INI_X", 0.5, 1.5) },
            SystemState = new[] { ("EXT_A", (double?)1.5) },
        };

        string line = TestCulture.Run("de-DE", () => SweepTsv.Format(row));

        var cells = line.Split('\t');
        Assert.Equal(SweepTsv.Columns.Length, cells.Length);
        Assert.DoesNotContain(",5", line);
        Assert.DoesNotContain(",25", line);
        Assert.Equal("set AIRLINER_WX_GAIN = 0.5", cells[Array.IndexOf(SweepTsv.Columns, "sent")]);
        Assert.Equal("0.5", cells[Array.IndexOf(SweepTsv.Columns, "read back")]);
        Assert.Equal("0.25", cells[Array.IndexOf(SweepTsv.Columns, "second read back")]);
        Assert.Equal("INI_WXR_GAIN 7.5", cells[Array.IndexOf(SweepTsv.Columns, "aircraft value")]);
        Assert.Equal("INI_X 0.5 -> 1.5", cells[Array.IndexOf(SweepTsv.Columns, "side effects")]);
        Assert.Equal("EXT_A 1.5", cells[Array.IndexOf(SweepTsv.Columns, "system state")]);

        // the journal's numbers too
        var e = new JournalEntry("A330_CPT_MCDU_BRT", JournalKind.InputEvent, "AIRLINER_CPT_MCDU_BRT", 0.7, "B:AIRLINER_CPT_MCDU_BRT", 0.7);
        string journal = TestCulture.Run("de-DE", () => SweepJournal.Line(e));
        Assert.Contains("\t0.7\t", journal);
        Assert.Equal(e, TestCulture.Run("de-DE", () => SweepJournal.Parse(journal).Single()));
    }

    // ---------------- the planner's second write ----------------

    [Fact]
    public void The_planner_writes_each_position_twice()
    {
        var sel = Select("SEL", P(0, 1, "A"), P(1, 0, "B"), P(2, 2, "C"));
        var cont = new Control { Key = "C", Kind = "continuous", Event = "E", Read = "INI_C", Min = 0, Max = 100, Step = 5 };
        var plan = SweepPlanner.Plan(new[] { sel, cont }, new Dictionary<string, double?> { ["SEL"] = 0, ["C"] = 30 },
            new Dictionary<string, string>(), new Dictionary<string, string>());

        foreach (var cp in plan.Controls)
        {
            var steps = cp.Steps;
            var sets = Enumerable.Range(0, steps.Count).Where(i => steps[i].Kind == SweepStepKind.SetPosition).ToList();
            Assert.NotEmpty(sets);
            foreach (int i in sets)
            {
                var again = steps[i + 1];
                Assert.Equal(SweepStepKind.RepeatPosition, again.Kind);
                Assert.Equal(steps[i].ControlKey, again.ControlKey);
                Assert.Equal(steps[i].Input, again.Input);
                Assert.Equal(steps[i].ExpectedRead, again.ExpectedRead);
            }
            Assert.Equal(sets.Count, steps.Count(s => s.Kind == SweepStepKind.RepeatPosition));
        }
    }

    // ---------------- the session on a fake aircraft ----------------

    /// <summary>An aircraft in memory: input events with their own values, L:vars, and which event drives
    /// which variable. Time passes only when the session sleeps.</summary>
    private sealed class FakeAircraft : ISweepIo
    {
        public readonly Dictionary<string, double> Events = new(StringComparer.Ordinal);
        public readonly Dictionary<string, double> Vars = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Drives = new(StringComparer.Ordinal);
        /// <summary>An input event the aircraft does not take this value on (a write of it changes nothing).</summary>
        public readonly Dictionary<string, double> Ignores = new(StringComparer.Ordinal);
        public readonly List<string> Sent = new();
        public long Now;

        public bool CalcAnswering { get; set; } = true;

        public bool TrySetInputEvent(string inputEvent, double value)
        {
            if (!Events.ContainsKey(inputEvent)) return false;
            Sent.Add(FormattableString.Invariant($"{inputEvent}={value}"));
            if (Ignores.TryGetValue(inputEvent, out double ignored) && ignored == value) return true;
            Events[inputEvent] = value;
            if (Drives.TryGetValue(inputEvent, out var v)) Vars[v] = value;
            return true;
        }

        public void Calc(string rpn) => Sent.Add(rpn);

        public double? Read(string read)
        {
            var (kind, name, _) = A330ControlMap.ParseReadKind(read);
            var from = kind == A330ControlMap.ReadKind.InputEvent ? Events : Vars;
            return from.TryGetValue(name, out double v) ? v : null;
        }

        public IReadOnlyDictionary<string, double> Snapshot() => new Dictionary<string, double>(Vars);
        public IReadOnlyList<string> TakeWriteErrors() => Array.Empty<string>();
        public long NowMs() => Now;
        public void Sleep(int ms) => Now += ms;

        public void Add(Control c, double start)
        {
            Events[c.Event!] = start;
            Vars[c.Read!] = start;
            Drives[c.Event!] = c.Read!;
        }
    }

    private static (SweepOutcome outcome, List<SweepRow> rows, string journal) Sweep(FakeAircraft aircraft, params Control[] controls)
    {
        var rows = new List<SweepRow>();
        var journal = new StringBuilder();
        var session = new SweepSession(controls, new Dictionary<string, string>(), aircraft,
            new SweepSinks(rows.Add, line => journal.Append(line), _ => { }));
        var outcome = session.Run(session.Prepare());
        return (outcome, rows, journal.ToString());
    }

    [Fact]
    public void A_swept_selector_is_written_twice_and_put_back_by_its_own_value()
    {
        var sel = Select("SEL", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft();
        aircraft.Add(sel, 0);

        var (outcome, rows, journal) = Sweep(aircraft, sel);

        Assert.Null(outcome.StopReason);
        Assert.Equal(new[] { "EV_SEL=1", "EV_SEL=1", "EV_SEL=0" }, aircraft.Sent);
        Assert.Contains("start\tSEL\tinput-event\tEV_SEL\t0\tINI_SEL\t0", journal);
        var write = rows.Single(r => r.Position.StartsWith("On", StringComparison.Ordinal));
        Assert.Equal("read-only", write.Verdict);
        Assert.Equal(1, write.SecondReadBack);
        Assert.Contains(rows, r => r.Position.StartsWith("(restore)", StringComparison.Ordinal) && r.Verdict == "restored");
        Assert.Equal(0, aircraft.Events["EV_SEL"]);
    }

    [Fact]
    public void A_restore_that_did_not_hold_stops_the_sweep()
    {
        var a = Select("A", P(0, 0, "Off"), P(1, 1, "On"));
        var b = Select("B", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft();
        aircraft.Add(a, 0);
        aircraft.Add(b, 0);
        aircraft.Ignores["EV_A"] = 0;   // A goes On and will not come back Off

        var (outcome, rows, journal) = Sweep(aircraft, a, b);

        Assert.NotNull(outcome.StopReason);
        Assert.Contains("A", outcome.StopReason);
        Assert.Single(outcome.RestoreFailures);
        Assert.DoesNotContain(aircraft.Sent, s => s.StartsWith("EV_B", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Key == "A" && r.Verdict.StartsWith("RESTORE FAILED", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Key == "B" && r.Verdict.StartsWith("not reached", StringComparison.Ordinal));
        // the journal was written before the first write, and covers B too: --restore can put back what was not reached
        Assert.Contains("start\tA\t", journal);
        Assert.Contains("start\tB\t", journal);
    }

    [Fact]
    public void A_press_while_the_calculator_does_not_answer_is_not_sent_and_an_unread_event_is_never_written()
    {
        var key = new Control { Key = "KEY", Label = "KEY", Kind = "button", Event = "EV_KEY", Mode = "push", State = "INI_KEY_STATE" };
        var sel = Select("SEL", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft { CalcAnswering = false };
        aircraft.Vars["INI_KEY_STATE"] = 0;
        aircraft.Vars["INI_SEL"] = 0;   // the aircraft lists no EV_SEL: its own value cannot be read, so it cannot be put back

        var (outcome, rows, _) = Sweep(aircraft, key, sel);

        Assert.Empty(aircraft.Sent);
        Assert.Null(outcome.StopReason);
        var press = rows.Single(r => r.Key == "KEY" && !r.Position.StartsWith("(restore)", StringComparison.Ordinal));
        Assert.Equal("not sent", press.Verdict);
        Assert.Contains("calculator", press.Note);
        Assert.Contains("could not be read", Assert.Single(rows, r => r.Key == "SEL").Verdict);
    }

    [Fact]
    public void The_sweep_tools_press_hold_is_the_apps()
    {
        // The tool cannot link IniA330Definition (it needs the whole app), so its shim restates the press hold
        // A330Write plans with; this keeps the two equal.
        string shim = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "a330-gen", "PanelSweep", "IniA330Shim.cs"));
        var m = Regex.Match(shim, @"DefaultPushMs\s*=\s*(\d+)\s*;");
        Assert.True(m.Success, "IniA330Shim.cs declares no DefaultPushMs");
        Assert.Equal(IniA330Definition.DefaultPushMs, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
