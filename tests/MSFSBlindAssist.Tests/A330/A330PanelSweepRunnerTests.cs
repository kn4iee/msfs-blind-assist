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

    [Fact]
    public void A_rerun_in_the_same_minute_never_overwrites_its_files()
    {
        const string stamp = "2026-11-27-1430";
        Assert.Equal(new[] { "sweep-2026-11-27-1430.tsv", "journal-2026-11-27-1430.tsv" },
            SweepTsv.FreeNames(stamp, _ => false, "sweep", "journal"));

        // a clash on either file moves both to the same next suffix, so a sweep and its journal stay a pair
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "journal-2026-11-27-1430.tsv" };
        Assert.Equal(new[] { "sweep-2026-11-27-1430-2.tsv", "journal-2026-11-27-1430-2.tsv" },
            SweepTsv.FreeNames(stamp, taken.Contains, "sweep", "journal"));
        taken.Add("sweep-2026-11-27-1430-2.tsv");
        Assert.Equal(new[] { "sweep-2026-11-27-1430-3.tsv", "journal-2026-11-27-1430-3.tsv" },
            SweepTsv.FreeNames(stamp, taken.Contains, "sweep", "journal"));
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

        /// <summary>For each entry of <see cref="Sent"/>, the uninterrupted call it went in (-1: outside any).</summary>
        public readonly List<int> CallOf = new();
        private int _call = -1, _calls;

        public void Uninterrupted(Action call)
        {
            _call = ++_calls;
            try { call(); }
            finally { _call = -1; }
        }

        private void Record(string sent)
        {
            Sent.Add(sent);
            CallOf.Add(_call);
        }

        /// <summary>A sleep of this length throws, once (a hold cut short).</summary>
        public int? ThrowOnSleepOf;
        /// <summary>The input event set of this number (1 = the first) throws, once.</summary>
        public int? ThrowOnSet;
        private int _sets;

        /// <summary>Push-buttons that toggle, by the model the 2026-11-27 sweep and its 23:05 trial fit
        /// (tools/a330-gen/README.md says what was measured on which keys): every set of the event is a press that flips
        /// the variable it drives, whatever the value.</summary>
        public readonly HashSet<string> PressToggles = new(StringComparer.Ordinal);
        /// <summary>Push-buttons whose press changes the event's own value but never flips what it drives.</summary>
        public readonly HashSet<string> Stuck = new(StringComparer.Ordinal);

        public bool TrySetInputEvent(string inputEvent, double value)
        {
            if (!Events.ContainsKey(inputEvent)) return false;
            if (++_sets == ThrowOnSet) throw new InvalidOperationException("the link failed");
            Record(FormattableString.Invariant($"{inputEvent}={value}"));
            if (PressToggles.Contains(inputEvent))
            {
                Events[inputEvent] = value;
                if (!Stuck.Contains(inputEvent) && Drives.TryGetValue(inputEvent, out var flipped))
                    Vars[flipped] = Vars[flipped] > 0.5 ? 0 : 1;
                return true;
            }
            if (Ignores.TryGetValue(inputEvent, out double ignored) && ignored == value) return true;
            Events[inputEvent] = value;
            if (Drives.TryGetValue(inputEvent, out var v)) Vars[v] = value;
            return true;
        }

        /// <summary>A key's push and what it selects: the state variable and the value it sets it to.</summary>
        public readonly Dictionary<string, (string Var, double Value)> Presses = new(StringComparer.Ordinal);
        /// <summary>The input events pushed, in order.</summary>
        public readonly List<string> Pushed = new();

        public void Calc(string rpn)
        {
            Record(rpn);
            var push = Regex.Match(rpn, @"^\(>B:(.+)_Push\)$");
            if (!push.Success) return;
            Pushed.Add(push.Groups[1].Value);
            if (Presses.TryGetValue(push.Groups[1].Value, out var p)) Vars[p.Var] = p.Value;
        }

        /// <summary>Variables that read nothing (a read that did not answer).</summary>
        public readonly HashSet<string> Unreadable = new(StringComparer.Ordinal);

        public double? Read(string read)
        {
            var (kind, name, _) = A330ControlMap.ParseReadKind(read);
            if (Unreadable.Contains(name)) return null;
            var from = kind == A330ControlMap.ReadKind.InputEvent ? Events : Vars;
            return from.TryGetValue(name, out double v) ? v : null;
        }

        public IReadOnlyDictionary<string, double> Snapshot() => new Dictionary<string, double>(Vars);
        public IReadOnlyList<string> TakeWriteErrors() => Array.Empty<string>();
        public long NowMs() => Now;

        public void Sleep(int ms)
        {
            if (ms == ThrowOnSleepOf) { ThrowOnSleepOf = null; throw new InvalidOperationException("the hold was cut short"); }
            Now += ms;
        }

        /// <summary>A control at its start: its input event, and the L:var it drives (none when it reads its own event).</summary>
        public void Add(Control c, double start)
        {
            Events[c.Event!] = start;
            if (c.Read!.StartsWith("B:", StringComparison.Ordinal)) return;
            Vars[c.Read] = start;
            Drives[c.Event!] = c.Read;
        }

        /// <summary>A push-button that toggles, reading <paramref name="read"/>, its input event's own value at
        /// <paramref name="own"/> (which says nothing about the switch).</summary>
        public void AddToggle(Control c, double read, double own)
        {
            Events[c.Event!] = own;
            Vars[c.Read!] = read;
            Drives[c.Event!] = c.Read!;
            PressToggles.Add(c.Event!);
        }
    }

    /// <summary>A push-button the aircraft toggles, as the map has it: On read 1, written 0; pressed by one set of 1.</summary>
    private static Control Toggle(string key)
    {
        var c = Select(key, P(1, 0, "On"), P(0, 1, "Off"));
        c.Mode = "toggle";
        c.Press = 1;
        return c;
    }

    // ---------------- push-buttons that toggle ----------------

    [Fact]
    public void A_toggle_is_restored_by_its_read_not_its_events_value()
    {
        // On at the start, its event's own value at 0: after the two presses the event reads 1, which says nothing
        var t = Toggle("T");
        var aircraft = new FakeAircraft();
        aircraft.AddToggle(t, read: 1, own: 0);

        var (outcome, rows, journal) = Sweep(aircraft, t);

        Assert.Null(outcome.StopReason);
        Assert.Equal(1, aircraft.Vars["INI_T"]);
        // the journal keeps the read; the put-back compares the read and sends nothing, though the event's own value
        // is no longer the journal's
        Assert.Contains("start\tT\ttoggle\tEV_T\t0\tINI_T\t1", journal);
        Assert.Equal(1, aircraft.Events["EV_T"]);
        var back = rows.Single(r => r.Position == "(restore) start");
        Assert.Equal("at start", back.Verdict);
        Assert.Equal("", back.Sent);
        Assert.Equal(new[] { "read-only", "read-only" }, rows.Where(r => r.Position.EndsWith("(press)", StringComparison.Ordinal)).Select(r => r.Verdict).ToArray());
        Assert.Equal(new[] { "Off (press)", "On (press)" }, rows.Where(r => r.Position.EndsWith("(press)", StringComparison.Ordinal)).Select(r => r.Position).ToArray());
        Assert.DoesNotContain(rows, r => r.Verdict == "toggle");   // not written twice any more
    }

    [Fact]
    public void A_toggle_press_is_one_set()
    {
        // every set to these events is a press (the 23:05 trial: set 1, a hold and set 0 left the read where it was), so
        // the planned press, the press back and the put-back by the read are each ONE set of the press value, alone in
        // its own uninterrupted call, with no hold and nothing after it
        var t = Toggle("T");
        var aircraft = new FakeAircraft { ThrowOnSleepOf = IniA330Definition.DefaultPushMs };   // a hold would throw
        aircraft.AddToggle(t, read: 1, own: 0);

        var (outcome, rows, _) = Sweep(aircraft, t);

        Assert.Null(outcome.StopReason);
        Assert.Equal(new[] { "EV_T=1", "EV_T=1" }, aircraft.Sent);
        Assert.NotEqual(aircraft.CallOf[0], aircraft.CallOf[1]);
        Assert.DoesNotContain(-1, aircraft.CallOf);
        Assert.Equal(1, aircraft.Vars["INI_T"]);
        Assert.Equal(new[] { "press EV_T (set 1)", "press EV_T (set 1)" },
            rows.Where(r => r.Position.EndsWith("(press)", StringComparison.Ordinal)).Select(r => r.Sent).ToArray());

        // a sweep stopped between the two presses: the put-back presses it once by its read, with one set
        var cut = new FakeAircraft();
        cut.AddToggle(t, read: 1, own: 0);
        var (stopped, cutRows, _) = SweepUntil(cut, () => cut.Sent.Count > 0 ? "stopped with Ctrl+C" : null, t);
        Assert.Equal(new[] { "EV_T=1", "EV_T=1" }, cut.Sent);
        Assert.Equal(1, cut.Vars["INI_T"]);
        var back = cutRows.Single(r => r.Position == "(restore) start");
        Assert.Equal("restored", back.Verdict);
        Assert.Equal("press EV_T (set 1)", back.Sent);
        Assert.Empty(stopped.RestoreFailures);
    }

    [Fact]
    public void A_toggle_press_that_did_not_flip_is_dead()
    {
        var t = Toggle("T");
        var aircraft = new FakeAircraft();
        aircraft.AddToggle(t, read: 1, own: 0);
        aircraft.Stuck.Add("EV_T");

        var (outcome, rows, _) = Sweep(aircraft, t);

        // one press; the press back is not sent, since the read never left On
        Assert.Equal(new[] { "EV_T=1" }, aircraft.Sent);
        Assert.Equal("dead", rows.Single(r => r.Position == "Off (press)").Verdict);
        Assert.Equal(SweepSession.NothingSentWord, rows.Single(r => r.Position == "On (press)").Verdict);
        Assert.Equal("at start", rows.Single(r => r.Position == "(restore) start").Verdict);
        Assert.Null(outcome.StopReason);

        // dead whatever else moved: a press that did not flip its read did not do what the switch does
        var j = SweepVerdicts.Classify(new WriteObservation { Toggle = true, ReadBefore = 1, Read = Still(1), ExpectedRead = 0, SideEffects = 2 });
        Assert.Equal(SweepVerdict.Dead, j.Verdict);
        Assert.Contains("did not flip", j.Note);
        Assert.Contains("2 other", j.Note);
        // and a press that flipped it is judged as any write
        Assert.Equal(SweepVerdict.ReadOnly,
            SweepVerdicts.Classify(new WriteObservation { Toggle = true, ReadBefore = 1, Read = Moved(0), ExpectedRead = 0 }).Verdict);
    }

    [Fact]
    public void Restore_stops_at_a_toggle_that_did_not_come_back()
    {
        // put back last first: U (a selector that will not come back: listed, and the restore goes on), then T (a
        // push-button pressed once that did not come back: the restore stops there), then S (never written)
        var s = Select("S", P(0, 0, "Off"), P(1, 1, "On"));
        var t = Toggle("T");
        var u = Select("U", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft();
        aircraft.Add(s, 1);   // S is On, its journal says Off
        aircraft.AddToggle(t, read: 0, own: 0);
        aircraft.Stuck.Add("EV_T");
        aircraft.Add(u, 1);
        aircraft.Ignores["EV_U"] = 0;   // U will not go back Off
        var entries = SweepJournal.Parse(
            "start\tS\tinput-event\tEV_S\t0\tINI_S\t0\n" +
            "start\tT\ttoggle\tEV_T\t0\tINI_T\t1\n" +
            "start\tU\tinput-event\tEV_U\t0\tINI_U\t0\n");
        var said = new List<string>();
        var session = new SweepSession(new[] { s, t, u }, new Dictionary<string, string>(), aircraft, new SweepSinks(_ => { }, _ => { }, said.Add));

        var run = session.RestoreJournal(entries, () => null, _ => false);

        Assert.Equal("T", run.StoppedAt);
        Assert.Equal(2, run.Tried);
        Assert.DoesNotContain(aircraft.Sent, x => x.StartsWith("EV_S", StringComparison.Ordinal));
        Assert.Equal(new[] { "EV_T=1" }, aircraft.Sent.Where(x => x.StartsWith("EV_T", StringComparison.Ordinal)).ToArray());   // once, one set
        Assert.Equal(2, run.NotPutBack.Count);
        Assert.StartsWith("U", run.NotPutBack[0], StringComparison.Ordinal);
        Assert.Contains("T (T)", run.NotPutBack[1], StringComparison.Ordinal);
        Assert.Contains("by hand", run.NotPutBack[1], StringComparison.Ordinal);
        Assert.Contains("On", run.NotPutBack[1], StringComparison.Ordinal);   // the position it started at
        Assert.Contains(said, l => l.StartsWith("Stopped at T", StringComparison.Ordinal) && l.Contains("run --restore again", StringComparison.Ordinal));

        // a toggle that came back does not stop it: every entry is tried
        var fine = new FakeAircraft();
        fine.Add(s, 0);
        fine.AddToggle(t, read: 0, own: 0);
        fine.Add(u, 0);
        var all = new SweepSession(new[] { s, t, u }, new Dictionary<string, string>(), fine, new SweepSinks(_ => { }, _ => { }, _ => { }))
            .RestoreJournal(entries, () => null, _ => false);
        Assert.Null(all.StoppedAt);
        Assert.Equal(3, all.Tried);
        Assert.Empty(all.NotPutBack);
    }

    [Fact]
    public void Restore_presses_a_toggle_that_reads_off_its_start_once_with_one_set()
    {
        // the 2026-11-27 journal's form (an input-event line) and the toggle line: both put back by the READ
        foreach (string line in new[] { "start\tT\tinput-event\tEV_T\t0\tINI_T\t1\n", "start\tT\ttoggle\tEV_T\t0\tINI_T\t1\n" })
        {
            var entry = Assert.Single(SweepJournal.Parse(line));
            var t = Toggle("T");

            // reads Off, its event's own value at its start (0): pressed once, by one set, and read back On
            var off = new FakeAircraft();
            off.AddToggle(t, read: 0, own: 0);
            var rows = new List<SweepRow>();
            var session = new SweepSession(new[] { t }, new Dictionary<string, string>(), off, new SweepSinks(rows.Add, _ => { }, _ => { }));
            Assert.True(session.Restore(entry));
            Assert.Equal(new[] { "EV_T=1" }, off.Sent);
            Assert.Equal(1, off.Vars["INI_T"]);
            Assert.Equal("restored", rows.Single().Verdict);

            // reads its start, its event's own value not (1): nothing sent
            var on = new FakeAircraft();
            on.AddToggle(t, read: 1, own: 1);
            Assert.True(new SweepSession(new[] { t }, new Dictionary<string, string>(), on, new SweepSinks(_ => { }, _ => { }, _ => { })).Restore(entry));
            Assert.Empty(on.Sent);

            // a press that does not bring it back: one press, never a second, and not held (the sweep and --restore stop)
            var stuck = new FakeAircraft();
            stuck.AddToggle(t, read: 0, own: 0);
            stuck.Stuck.Add("EV_T");
            var stuckRows = new List<SweepRow>();
            Assert.False(new SweepSession(new[] { t }, new Dictionary<string, string>(), stuck, new SweepSinks(stuckRows.Add, _ => { }, _ => { })).Restore(entry));
            Assert.Equal(new[] { "EV_T=1" }, stuck.Sent);
            Assert.StartsWith("RESTORE FAILED", stuckRows.Single().Verdict, StringComparison.Ordinal);
        }

        // the toggle line round-trips, its event's own value kept for the record only (it may be missing)
        var kept = new JournalEntry("T", JournalKind.Toggle, "EV_T", null, "INI_T", 1);
        Assert.Equal(kept, Assert.Single(SweepJournal.Parse(SweepJournal.Line(kept))));
        Assert.Equal(kept, SweepJournal.EntryFor(Toggle("T"), eventValue: null, readValue: 1));
        // with no read it cannot be put back, so it is never journaled
        Assert.Null(SweepJournal.EntryFor(Toggle("T"), eventValue: 0, readValue: null));
    }

    private static (SweepOutcome outcome, List<SweepRow> rows, string journal) Sweep(FakeAircraft aircraft, params Control[] controls) =>
        SweepUntil(aircraft, () => null, controls);

    private static (SweepOutcome outcome, List<SweepRow> rows, string journal) SweepUntil(FakeAircraft aircraft, Func<string?> stop, params Control[] controls)
    {
        var rows = new List<SweepRow>();
        var journal = new StringBuilder();
        var session = new SweepSession(controls, new Dictionary<string, string>(), aircraft,
            new SweepSinks(rows.Add, line => journal.Append(line), _ => { }), stop);
        var outcome = session.Run(session.Prepare());
        return (outcome, rows, journal.ToString());
    }

    // ---------------- the safety paths ----------------

    [Fact]
    public void An_exception_mid_control_still_puts_it_back()
    {
        var sel = Select("SEL", P(0, 0, "Off"), P(1, 1, "On"));
        var next = Select("NEXT", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft { ThrowOnSet = 2 };   // the second write (the repeat) fails on the link
        aircraft.Add(sel, 0);
        aircraft.Add(next, 0);

        var (outcome, rows, _) = Sweep(aircraft, sel, next);

        Assert.Equal(0, aircraft.Events["EV_SEL"]);
        Assert.Contains(rows, r => r.Key == "SEL" && r.Verdict == "error: the link failed");
        Assert.Contains(rows, r => r.Key == "SEL" && r.Verdict == "restored");
        Assert.Contains("the link failed", outcome.StopReason);
        Assert.DoesNotContain(aircraft.Sent, s => s.StartsWith("EV_NEXT", StringComparison.Ordinal));
        Assert.Equal("SEL", outcome.ResumeFrom);   // it did not finish: go on from it
    }

    [Fact]
    public void Ctrl_C_stops_once_the_control_in_progress_is_put_back()
    {
        var a = Select("A", P(0, 0, "Low"), P(1, 1, "Normal"), P(2, 2, "High"));
        var b = Select("B", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft();
        aircraft.Add(a, 0);
        aircraft.Add(b, 0);

        // Ctrl+C comes once the first position has been written
        var (outcome, rows, _) = SweepUntil(aircraft, () => aircraft.Sent.Count > 0 ? "stopped with Ctrl+C" : null, a, b);

        Assert.Equal(new[] { "EV_A=1", "EV_A=1", "EV_A=0" }, aircraft.Sent);   // Normal twice, then put back; High never
        Assert.Contains(rows, r => r.Key == "A" && r.Verdict == "interrupted (stopped with Ctrl+C)");
        Assert.Contains(rows, r => r.Key == "A" && r.Verdict == "restored");
        Assert.Contains(rows, r => r.Key == "B" && r.Verdict == "not reached (stopped with Ctrl+C)");
        Assert.Equal("A", outcome.ResumeFrom);
    }

    [Fact]
    public void The_guard_is_closed_even_when_its_switch_did_not_come_back()
    {
        var (guard, sw) = Guarded();
        var aircraft = new FakeAircraft();
        aircraft.Add(guard, 0);
        aircraft.Add(sw, 0);
        aircraft.Ignores["EV_SW"] = 0;   // the switch will not go back Off

        var (outcome, rows, _) = Sweep(aircraft, guard, sw);

        Assert.Contains(rows, r => r.Key == "SW" && r.Verdict.StartsWith("RESTORE FAILED", StringComparison.Ordinal));
        Assert.Equal("EV_COVER=0", aircraft.Sent[^1]);   // the guard is still put back, last
        Assert.Equal(0, aircraft.Events["EV_COVER"]);
        Assert.Equal("SW (SW)", Assert.Single(outcome.RestoreFailures));
        Assert.NotNull(outcome.StopReason);
    }

    [Fact]
    public void A_denied_control_is_never_written_whatever_asks()
    {
        // a plan that names a denied control (the planner never makes one): the write is refused before it leaves
        var bat = Select("BAT", P(0, 0, "Auto"), P(1, 1, "Off"));
        var aircraft = new FakeAircraft();
        aircraft.Add(bat, 0);
        var rows = new List<SweepRow>();
        var session = new SweepSession(new[] { bat }, new Dictionary<string, string> { ["BAT"] = "the cockpit goes dark" }, aircraft,
            new SweepSinks(rows.Add, _ => { }, _ => { }));
        var plan = new SweepPlan(new[]
        {
            new ControlPlan("BAT", "BAT", new[] { new SweepStep(SweepStepKind.SetPosition, "BAT", 1, 1, "set BAT to Off") }, null),
        });
        var entry = SweepJournal.EntryFor(bat, eventValue: 0, readValue: 0)!;

        var outcome = session.Run(new SweepPrepared(plan, new[] { entry }));

        Assert.Empty(aircraft.Sent);
        Assert.Contains(rows, r => r.Key == "BAT" && r.Verdict.Contains("is denied", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Key == "BAT" && r.Verdict == "at start");
        Assert.NotNull(outcome.StopReason);
    }

    [Fact]
    public void A_push_is_always_released_in_the_same_uninterrupted_call()
    {
        var key = new Control { Key = "KEY", Label = "KEY", Kind = "button", Event = "EV_KEY", Mode = "push", State = "INI_KEY_STATE" };
        var aircraft = new FakeAircraft { ThrowOnSleepOf = IniA330Definition.DefaultPushMs };   // the hold is cut short
        aircraft.Vars["INI_KEY_STATE"] = 0;

        var (outcome, _, _) = Sweep(aircraft, key);

        int push = aircraft.Sent.IndexOf("(>B:EV_KEY_Push)");
        Assert.True(push >= 0, "the key was never pushed");
        Assert.Equal("(>B:EV_KEY_Release)", aircraft.Sent.ElementAtOrDefault(push + 1));
        // the push and its release went in one call, which a second Ctrl+C waits for; no write went outside one
        Assert.Equal(aircraft.CallOf[push], aircraft.CallOf[push + 1]);
        Assert.DoesNotContain(-1, aircraft.CallOf);
        Assert.NotNull(outcome.StopReason);
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
        // A was swept to the end; once it is put back by hand, the sweep goes on from B
        Assert.Equal("B", outcome.ResumeFrom);
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

    private static (Control guard, Control sw) Guarded()
    {
        var guard = Select("COVER", P(0, 0, "Closed"), P(1, 1, "Open"));
        guard.Read = "B:EV_COVER";
        var sw = Select("SW", P(0, 0, "Off"), P(1, 1, "On"));
        sw.Guard = "COVER";
        return (guard, sw);
    }

    [Fact]
    public void A_switch_whose_guard_did_not_open_is_never_written()
    {
        var (guard, sw) = Guarded();
        var other = Select("NEXT", P(0, 0, "Off"), P(1, 1, "On"));
        var aircraft = new FakeAircraft();
        aircraft.Add(guard, 0);
        aircraft.Add(sw, 0);
        aircraft.Add(other, 0);
        aircraft.Ignores["EV_COVER"] = 1;   // the guard will not open

        var (outcome, rows, _) = Sweep(aircraft, guard, sw, other);

        Assert.DoesNotContain(aircraft.Sent, s => s.StartsWith("EV_SW", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Key == "SW" && r.Verdict == "skipped: its guard did not open (dead)");
        // the rest of the sweep goes on
        Assert.Null(outcome.StopReason);
        Assert.Contains(aircraft.Sent, s => s.StartsWith("EV_NEXT", StringComparison.Ordinal));
    }

    // ---------------- keys that share one state ----------------

    private static Control RadioKey(string key, string label) => new()
    {
        Key = key, Label = label, Kind = "button", Event = "EV_" + key, Mode = "push", State = "INI_RADIO",
    };

    private static Control[] Radios() => new[]
    {
        RadioKey("K1", "Radio VHF 1"), RadioKey("K2", "Radio VHF 2"), RadioKey("K3", "Radio VHF 3"),
        new Control { Key = "RADIO_SEL", Label = "Radio panel selected radio", Kind = "status", Read = "INI_RADIO",
            Positions = new() { P(1, 1, "VHF 1"), P(2, 2, "VHF 2"), P(3, 3, "VHF 3") } },
    };

    private static FakeAircraft RadioAircraft(double start)
    {
        var aircraft = new FakeAircraft();
        aircraft.Vars["INI_RADIO"] = start;
        foreach (var (k, v) in new[] { ("K1", 1.0), ("K2", 2.0), ("K3", 3.0) }) aircraft.Presses["EV_" + k] = ("INI_RADIO", v);
        return aircraft;
    }

    [Fact]
    public void A_family_is_put_back_by_pressing_the_one_key_seen_to_give_its_start()
    {
        var aircraft = RadioAircraft(2);
        var (outcome, rows, journal) = Sweep(aircraft, Radios());

        Assert.Null(outcome.StopReason);
        Assert.Contains("start\tINI_RADIO\tfamily\t\t\tINI_RADIO\t2", journal);
        Assert.Equal(new[] { "EV_K1", "EV_K2", "EV_K3", "EV_K2" }, aircraft.Pushed);
        Assert.Equal(2, aircraft.Vars["INI_RADIO"]);
        Assert.Contains(rows, r => r.Key == "INI_RADIO" && r.Verdict == "restored");
        Assert.Equal(3, rows.Count(r => r.Key is "K1" or "K2" or "K3" && r.Verdict == "read-only"));

        // the key already selected is not pressed (a second press could undo it), and it is the one that puts it back
        var atFirst = RadioAircraft(1);
        Sweep(atFirst, Radios());
        Assert.Equal(new[] { "EV_K2", "EV_K3", "EV_K1" }, atFirst.Pushed);
    }

    [Fact]
    public void A_family_that_did_not_come_back_stops_the_sweep_and_nothing_more_is_pressed()
    {
        var aircraft = RadioAircraft(2);
        aircraft.Presses.Remove("EV_K2");   // VHF 2's key does nothing
        var next = Select("NEXT", P(0, 0, "Off"), P(1, 1, "On"));
        aircraft.Add(next, 0);

        var (outcome, rows, _) = Sweep(aircraft, Radios().Append(next).ToArray());

        Assert.Equal(new[] { "EV_K1", "EV_K2", "EV_K3", "EV_K2" }, aircraft.Pushed);   // one press to put it back, no more
        Assert.NotNull(outcome.StopReason);
        Assert.Contains(rows, r => r.Key == "INI_RADIO" && r.Verdict.StartsWith("RESTORE FAILED", StringComparison.Ordinal));
        Assert.DoesNotContain(aircraft.Sent, s => s.StartsWith("EV_NEXT", StringComparison.Ordinal));
        Assert.Contains(outcome.ByHand, h => h.Contains("VHF 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_family_key_is_never_pressed_while_the_shared_state_reads_nothing()
    {
        // the start was read, then the shared state stops answering: a press could select anything, and the key already
        // selected could be pressed a second time (an ECAM page key's second press can undo its page), so none is pressed
        var aircraft = RadioAircraft(2);
        var rows = new List<SweepRow>();
        var session = new SweepSession(Radios(), new Dictionary<string, string>(), aircraft, new SweepSinks(rows.Add, _ => { }, _ => { }));
        var prepared = session.Prepare();
        aircraft.Unreadable.Add("INI_RADIO");

        var outcome = session.Run(prepared);

        Assert.Empty(aircraft.Pushed);
        Assert.Equal(new[] { "K1", "K2", "K3" },
            rows.Where(r => r.Position == "press" && r.Verdict == "not read before the press").Select(r => r.Key).ToArray());
        // and it is not put back blind either: the sweep stops there, for the owner
        Assert.NotNull(outcome.StopReason);
    }

    [Fact]
    public void A_family_is_never_put_back_blind()
    {
        // its state reads nothing: whether it is at its start is not known, so its key is not pressed (a second press of
        // the key already selected could undo it); the put-back fails and says why
        var entry = Assert.Single(SweepJournal.Parse("start\tINI_RADIO\tfamily\t\t\tINI_RADIO\t2\n"));
        var aircraft = RadioAircraft(3);
        aircraft.Unreadable.Add("INI_RADIO");
        var rows = new List<SweepRow>();
        var session = new SweepSession(Radios(), new Dictionary<string, string>(), aircraft, new SweepSinks(rows.Add, _ => { }, _ => { }));

        Assert.False(session.Restore(entry));
        Assert.Empty(aircraft.Pushed);
        var row = Assert.Single(rows);
        Assert.StartsWith("RESTORE FAILED", row.Verdict, StringComparison.Ordinal);
        Assert.Contains("could not be read", row.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void The_restore_of_a_family_from_its_journal_presses_the_key_the_map_names_or_nothing()
    {
        var entry = Assert.Single(SweepJournal.Parse("start\tINI_RADIO\tfamily\t\t\tINI_RADIO\t2\n"));
        Assert.Equal(JournalKind.Family, entry.Kind);
        Assert.Equal(entry, Assert.Single(SweepJournal.Parse(SweepJournal.Line(entry))));

        var aircraft = RadioAircraft(3);
        var session = new SweepSession(Radios(), new Dictionary<string, string>(), aircraft, new SweepSinks(_ => { }, _ => { }, _ => { }));
        Assert.True(session.Restore(entry));
        Assert.Equal(new[] { "EV_K2" }, aircraft.Pushed);

        // a start no key is known to select: said so, and nothing is pressed
        var none = RadioAircraft(3);
        var rows = new List<SweepRow>();
        var lost = new SweepSession(Radios(), new Dictionary<string, string>(), none, new SweepSinks(rows.Add, _ => { }, _ => { }));
        Assert.False(lost.Restore(SweepJournal.Parse("start\tINI_RADIO\tfamily\t\t\tINI_RADIO\t9\n").Single()));
        Assert.Empty(none.Pushed);
        Assert.Contains(rows, r => r.Verdict.Contains("no key", StringComparison.Ordinal));
    }

    [Fact]
    public void The_snapshot_blocks_are_padded_to_their_full_size()
    {
        // SimConnect sends a block only as many doubles as its definition has, and the managed library copies the whole
        // fixed-size struct out of the message: a short last block reads past the message's end (the app pads its
        // continuous batches for the same reason, SimConnectManager.Setup.cs). So every block is exactly full.
        var names = Enumerable.Range(0, 177).Select(i => FormattableString.Invariant($"INI_V{176 - i:000}")).ToList();
        var blocks = SweepSnapshot.Blocks(names);
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(SweepSnapshot.SlotsPerBlock, b.Count));
        // the names in ordinal order, first; the fillers (null) only at the end of the last block
        var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, blocks.SelectMany(b => b).Where(n => n != null));
        Assert.Equal(sorted.Take(SweepSnapshot.SlotsPerBlock), blocks[0]);
        Assert.Equal(Enumerable.Repeat<string?>(null, 2 * SweepSnapshot.SlotsPerBlock - 177), blocks[1].Skip(177 - SweepSnapshot.SlotsPerBlock));
        // a filler sorts after every L:var, so the names keep their slots whichever order SimConnect returns them in
        Assert.All(sorted, n => Assert.True(string.CompareOrdinal("L:" + n, SweepSnapshot.FillerDatum) < 0, n));
        Assert.Equal(2, SweepSnapshot.Blocks(Enumerable.Range(0, 200).Select(i => $"INI_{i}").ToList()).Count);
        Assert.Empty(SweepSnapshot.Blocks(Array.Empty<string>()));
    }

    [Fact]
    public void The_sweep_tool_registers_the_padded_snapshot_blocks()
    {
        // the tool cannot be referenced from here, so its link is checked by its text: the block struct is the core's
        // size, and the snapshot is defined from the core's padded blocks
        string link = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "a330-gen", "PanelSweep", "SimLink.cs"));
        Assert.Matches(@"ByValArray,\s*SizeConst\s*=\s*SweepSnapshot\.SlotsPerBlock", link);
        Assert.Contains("SweepSnapshot.Blocks(", link, StringComparison.Ordinal);
        Assert.Contains("SweepSnapshot.FillerDatum", link, StringComparison.Ordinal);
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
