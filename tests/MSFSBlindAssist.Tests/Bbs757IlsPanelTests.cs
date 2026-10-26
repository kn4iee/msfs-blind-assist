using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's ILS control panel (plan 04, task 6a), counted. No variable holds the tuned
/// frequency, and the panel's two knobs are step requests BlueBird consumes within a frame, so the app
/// keeps its own count of the panel, from the state a flight loads in (PARK, fraction .10), and turns the
/// knobs one click per write, each after a fresh read and 250 ms apart. The knob model is round 1's
/// measurement (docs/b757/evidence/flight-04.md, "C. ILS panel on the ground"), copied here as data; the
/// step timing is round 4's ("Round 4: ILS step timing and localizer detection"). CI runs under en-US, so
/// the de-DE and sv-SE cases are the ones that catch a culture leak.
/// </summary>
public class Bbs757IlsPanelTests
{
    private const Bbs757IlsKnob W = Bbs757IlsKnob.Whole;
    private const Bbs757IlsKnob F = Bbs757IlsKnob.Fraction;

    private const string Invalid = "ILS frequency must be 108.10 to 111.95, on an ILS channel.";
    private const string Empty = "Type a frequency first.";

    private const string WholeDown = "-1 (>L:B757_ILS_FREQ_WHOLE_INC)";
    private const string WholeUp = "1 (>L:B757_ILS_FREQ_WHOLE_INC)";
    private const string FractionDown = "-1 (>L:B757_ILS_FREQ_FRAC_INC)";
    private const string FractionUp = "1 (>L:B757_ILS_FREQ_FRAC_INC)";

    /// <summary>The fraction knob's ten channels in hundredths, in the order +1 turns them (measured).</summary>
    private static readonly int[] Hundredths = { 10, 15, 30, 35, 50, 55, 70, 75, 90, 95 };

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static Bbs757IlsDial Dial(double mhz)
    {
        Assert.True(Bbs757IlsPanel.TryDialFor(mhz, out var dial), $"{mhz} is not an ILS channel");
        return dial;
    }

    /// <summary>What the panel's frequency window shows: the frequency with two decimals, or dashes when parked.</summary>
    private static string Shown(Bbs757IlsDial dial)
        => dial.Mhz is double mhz ? mhz.ToString("0.00", CultureInfo.InvariantCulture) : "-----";

    private static string[] Repeat(int times, params string[] cycle)
        => Enumerable.Range(0, times).SelectMany(_ => cycle).ToArray();

    // ---- The typed frequency --------------------------------------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Ils_entry_parsing(string culture)
    {
        InCulture(culture, () =>
        {
            foreach (var (typed, mhz) in new[]
            {
                ("110.50", 110.5), ("110.5", 110.5), ("110,55", 110.55), ("110,5", 110.5), ("110.55", 110.55),
                ("108.10", 108.1), ("108.1", 108.1), ("111.95", 111.95), (" 109.30 ", 109.3), ("110.550", 110.55),
                ("109,90", 109.9),
            })
            {
                Assert.True(Bbs757IlsPanel.TryParseIls(typed, out double parsed, out string error), $"\"{typed}\" refused: {error}");
                Assert.Equal(mhz, parsed);
                Assert.Equal("", error);
            }

            foreach (string typed in new[]
            {
                "108.00", "108.05", "108.20", "110.45", "110.52", "110.501", "110.555", "107.95", "112.10", "112", "110",
                "108", "1105", "11050", "-110.5", "+110.5", "110.5 MHz", "1.105e2", "abc", "NaN", "Infinity", "110.5.5",
                "1,110.50", "110.50.",
            })
            {
                Assert.False(Bbs757IlsPanel.TryParseIls(typed, out double parsed, out string error), $"\"{typed}\" accepted");
                Assert.Equal(0, parsed);
                Assert.Equal(Invalid, error);
            }

            foreach (string? typed in new[] { null, "", "   " })
            {
                Assert.False(Bbs757IlsPanel.TryParseIls(typed, out double parsed, out string error));
                Assert.Equal(0, parsed);
                Assert.Equal(Empty, error);
            }
        });
    }

    [Fact]
    public void Every_ils_channel_is_accepted_and_every_other_hundredth_refused()
    {
        int accepted = 0;
        for (int h = 10800; h <= 11199; h++)
        {
            string typed = $"{h / 100}.{h % 100:00}";
            bool channel = Hundredths.Contains(h % 100);

            Assert.Equal(channel, Bbs757IlsPanel.TryParseIls(typed, out double mhz, out _));
            Assert.Equal(channel, Bbs757IlsPanel.TryDialFor(h / 100.0, out var dial));
            if (!channel) continue;
            accepted++;
            Assert.Equal(h / 100.0, mhz);
            Assert.Equal(h / 100.0, dial.Mhz);
            Assert.Equal((h / 100 - 108, Array.IndexOf(Hundredths, h % 100)), (dial.Whole, dial.Fraction));
        }
        Assert.Equal(40, accepted);   // 108.10 to 111.95: four MHz, ten channels each

        foreach (double off in new[] { 107.95, 112.10, 110.52, 110.505, double.NaN, double.PositiveInfinity })
            Assert.False(Bbs757IlsPanel.TryDialFor(off, out _), $"{off} is no channel");
    }

    // ---- The dial and its phrases -----------------------------------------------------------------------

    [Fact]
    public void A_flight_loads_with_the_ils_parked_on_10()
    {
        // The owner's flight was in its starting panel state: PARK, and the first step proved the
        // fraction PARK remembered was .10 (PARK -1 gave 111.10).
        Assert.Equal(new Bbs757IlsDial(4, 0), Bbs757IlsDial.LoadState);
        Assert.True(Bbs757IlsDial.LoadState.IsParked);
        Assert.Null(Bbs757IlsDial.LoadState.Mhz);

        Assert.Equal(110.5, new Bbs757IlsDial(2, 4).Mhz);
        Assert.Equal(108.1, new Bbs757IlsDial(0, 0).Mhz);
        Assert.Equal(111.95, new Bbs757IlsDial(3, 9).Mhz);
        Assert.False(new Bbs757IlsDial(3, 9).IsParked);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(0, -1));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Phrases_say_what_the_count_says(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("ILS 110.50", Bbs757IlsPanel.Phrase(Dial(110.50)));
            Assert.Equal("ILS 108.10", Bbs757IlsPanel.Phrase(Dial(108.10)));   // two decimals and a dot, whatever the PC's culture
            Assert.Equal("ILS parked", Bbs757IlsPanel.Phrase(Bbs757IlsDial.LoadState));
            Assert.Equal("ILS parked", Bbs757IlsPanel.Phrase(new Bbs757IlsDial(4, 7)));   // whatever fraction PARK remembers
            Assert.Equal("ILS tuning unknown", Bbs757IlsPanel.Phrase(null));
        });
    }

    // ---- The knob model -----------------------------------------------------------------------------------

    /// <summary>
    /// Round 1 (flight-04.md, "C. ILS panel on the ground", "Knob behaviour", 11:40-11:49), in the order the knobs were turned
    /// from the panel as the flight loaded (PARK, .10): each row's knob and direction, and what the panel's
    /// frequency window showed after each of its clicks.
    /// </summary>
    private static readonly (Bbs757IlsKnob Knob, int Direction, string[] Shown)[] RoundOne =
    {
        (W, -1, Repeat(4, "111.10", "110.10", "109.10", "108.10", "-----")),   // WHOLE -1 x20 from PARK: a ring with PARK in it
        (W, +1, new[] { "108.10", "109.10", "110.10", "111.10", "-----", "108.10" }),   // WHOLE +1 x6
        (F, +1, Repeat(2, "108.15", "108.30", "108.35", "108.50", "108.55",
                          "108.70", "108.75", "108.90", "108.95", "108.10")),           // FRAC +1 x20: ILS channels only, no carry
        (F, -1, new[] { "108.95", "108.90", "108.75" }),                                // FRAC -1 x3
        (W, +1, new[] { "109.75", "110.75", "111.75", "-----" }),                       // WHOLE +1 x4 keeps the fraction
        (F, +1, new[] { "-----" }),                                                     // FRAC +1 at PARK: nothing
        (W, +1, new[] { "108.75" }),                                                    // PARK remembered .75
        (F, -1, new[] { "108.70" }),
        (F, +1, new[] { "108.75", "108.90", "108.95", "108.10" }),                      // the restore: FRAC +1 x4,
        (W, -1, new[] { "-----", "111.10" }),                                           // WHOLE -1, then -1 and +1 to prove it
        (W, +1, new[] { "-----" }),
    };

    [Fact]
    public void Dial_model_matches_the_measurement()
    {
        var dial = Bbs757IlsDial.LoadState;
        int clicks = 0;
        foreach (var (knob, direction, shown) in RoundOne)
        {
            foreach (string expected in shown)
            {
                dial = Bbs757IlsPanel.Step(dial, knob, direction);
                clicks++;
                Assert.True(expected == Shown(dial),
                    $"click {clicks} ({knob} {(direction > 0 ? "+1" : "-1")}): the panel showed {expected}, the model {Shown(dial)}");
            }
        }

        Assert.Equal(63, clicks);
        Assert.Equal(Bbs757IlsDial.LoadState, dial);   // round 1 ended as it began: PARK with .10
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]    // a write of 2 moved one channel (measured): one write is one click, so a click is +1 or -1
    [InlineData(-2)]
    public void A_click_is_one_step_either_way(int direction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757IlsPanel.Step(Bbs757IlsDial.LoadState, W, direction));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757IlsPanel.StepCode(F, direction));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]   // .NET writes -1 as "−1" (U+2212) under sv-SE: the RPN parser would not read it
    public void A_click_is_one_calculator_write_with_no_culture_in_it(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal(WholeDown, Bbs757IlsPanel.StepCode(W, -1));
            Assert.Equal(WholeUp, Bbs757IlsPanel.StepCode(W, +1));
            Assert.Equal(FractionDown, Bbs757IlsPanel.StepCode(F, -1));
            Assert.Equal(FractionUp, Bbs757IlsPanel.StepCode(F, +1));
        });
        Assert.Equal("B757_ILS_FREQ_WHOLE_INC", Bbs757IlsPanel.WholeVar);
        Assert.Equal("B757_ILS_FREQ_FRAC_INC", Bbs757IlsPanel.FractionVar);
    }

    // ---- The plan -----------------------------------------------------------------------------------------

    /// <summary>Every state the panel can be in: the 40 channels, and PARK with each of the ten fractions it may remember.</summary>
    private static IEnumerable<Bbs757IlsDial> EveryState()
        => from whole in Enumerable.Range(0, 5) from fraction in Enumerable.Range(0, 10) select new Bbs757IlsDial(whole, fraction);

    /// <summary>Every target: the 40 channels, and PARK (null).</summary>
    private static IEnumerable<Bbs757IlsDial?> EveryTarget()
        => EveryState().Where(d => !d.IsParked).Select(d => (Bbs757IlsDial?)d).Append(null);

    /// <summary>
    /// The fewest clicks from <paramref name="from"/> to <paramref name="to"/> (null: PARK) by the knob model
    /// (<see cref="Bbs757IlsPanel.Step"/>, checked against the measurement above), never passing PARK on the
    /// way: a breadth-first search over the panel's 50 states.
    /// </summary>
    private static int FewestClicks(Bbs757IlsDial from, Bbs757IlsDial? to)
    {
        bool Arrived(Bbs757IlsDial d) => to is Bbs757IlsDial target ? d == target : d.IsParked;
        var seen = new HashSet<Bbs757IlsDial> { from };
        var frontier = new List<Bbs757IlsDial> { from };
        for (int clicks = 0; frontier.Count > 0; clicks++)
        {
            if (frontier.Any(Arrived)) return clicks;
            var next = new List<Bbs757IlsDial>();
            foreach (var d in frontier)
                foreach (var knob in new[] { W, F })
                    foreach (int direction in new[] { +1, -1 })
                    {
                        var n = Bbs757IlsPanel.Step(d, knob, direction);
                        if (n.IsParked && !Arrived(n)) continue;   // never through PARK
                        if (seen.Add(n)) next.Add(n);
                    }
            frontier = next;
        }
        throw new InvalidOperationException("no way there");
    }

    [Fact]
    public void Plan_reaches_every_target()
    {
        int pairs = 0;
        foreach (var from in EveryState())
        {
            foreach (var to in EveryTarget())
            {
                var plan = Bbs757IlsPanel.PlanSteps(from, to);
                var path = new List<Bbs757IlsDial> { from };
                foreach (var (knob, direction) in plan) path.Add(Bbs757IlsPanel.Step(path[^1], knob, direction));
                var reached = path[^1];
                string pair = $"{Shown(from)} (fraction {from.Fraction}) to {(to is Bbs757IlsDial t ? Shown(t) : "PARK")}";

                // It gets there...
                Assert.True(to is Bbs757IlsDial target ? reached == target : reached.IsParked, $"{pair}: reached {Shown(reached)}");
                // ...with the ILS parked only where it starts or ends, never on the way...
                Assert.True(path.Skip(1).SkipLast(1).All(d => !d.IsParked), $"{pair}: passes PARK");
                // ...in the fewest clicks that never pass PARK...
                Assert.True(FewestClicks(from, to) == plan.Count, $"{pair}: {plan.Count} clicks, {FewestClicks(from, to)} would do");
                // ...in the stated order: to PARK the whole knob only; out of PARK the whole knob first (the
                // fraction knob does nothing while parked); between two frequencies the fraction first.
                var knobs = plan.Select(s => s.Knob).ToList();
                if (to is null) Assert.All(knobs, k => Assert.Equal(W, k));
                else if (from.IsParked) Assert.True(knobs.SkipWhile(k => k == W).All(k => k == F), $"{pair}: {string.Join(",", knobs)}");
                else Assert.True(knobs.SkipWhile(k => k == F).All(k => k == W), $"{pair}: {string.Join(",", knobs)}");
                pairs++;
            }
        }

        // The 41 x 41 pairs (PARK as a flight loads it among them), and PARK with its other nine fractions too.
        Assert.Equal(50 * 41, pairs);
    }

    [Fact]
    public void Between_108_and_111_the_whole_knob_never_passes_park()
    {
        // The way round through PARK is one click shorter here (two against three), and the only place it
        // is: one click is not worth parking the ILS on the way.
        Assert.Equal(new[] { (F, +1), (W, +1), (W, +1), (W, +1) }, Bbs757IlsPanel.PlanSteps(Dial(108.10), Dial(111.15)));
        Assert.Equal(new[] { (W, -1), (W, -1), (W, -1) }, Bbs757IlsPanel.PlanSteps(Dial(111.50), Dial(108.50)));
    }

    [Fact]
    public void Out_of_park_the_whole_knob_turns_first_and_the_load_state_to_110_50_is_what_round_3_turned()
    {
        // Round 3 (flight-04.md, "E3. The FMC as an ILS comparator, on the ground") tuned 110.50 by hand from
        // the load state: WHOLE -1 x2, FRAC +1 x4.
        Assert.Equal(new[] { (W, -1), (W, -1), (F, +1), (F, +1), (F, +1), (F, +1) },
            Bbs757IlsPanel.PlanSteps(Bbs757IlsDial.LoadState, Dial(110.50)));
        // A tie round the fraction ring (five either way) goes up.
        Assert.Equal(new[] { (W, +1), (F, +1), (F, +1), (F, +1), (F, +1), (F, +1) },
            Bbs757IlsPanel.PlanSteps(Bbs757IlsDial.LoadState, Dial(108.55)));
        // Already there: no click.
        Assert.Empty(Bbs757IlsPanel.PlanSteps(Dial(110.50), Dial(110.50)));
    }

    [Theory]
    [InlineData(108.10, -1, 1)]   // 108's -1 is PARK
    [InlineData(109.50, -1, 2)]   // 109, 108, PARK
    [InlineData(110.50, +1, 2)]   // 110, 111, PARK: how round 3 parked 110.50 by hand (WHOLE +1 x2)
    [InlineData(111.95, +1, 1)]   // 111's +1 is PARK
    public void Park_steps(double mhz, int direction, int clicks)
    {
        var from = Dial(mhz);

        var plan = Bbs757IlsPanel.PlanSteps(from, null);

        // The whole knob only, the shorter way round its ring of five...
        Assert.Equal(Enumerable.Repeat((W, direction), clicks), plan);
        Assert.All(plan, s => Assert.Equal(direction > 0 ? WholeUp : WholeDown, Bbs757IlsPanel.StepCode(s.Knob, s.Direction)));
        // ...and PARK remembers the fraction.
        var parked = plan.Aggregate(from, (d, s) => Bbs757IlsPanel.Step(d, s.Knob, s.Direction));
        Assert.True(parked.IsParked);
        Assert.Equal(from.Fraction, parked.Fraction);
        // A parked panel as the target is PARK too, whatever fraction it names.
        Assert.Equal(plan, Bbs757IlsPanel.PlanSteps(from, Bbs757IlsDial.LoadState));
        // Parked already, with any fraction: no click.
        Assert.All(Enumerable.Range(0, 10), f => Assert.Empty(Bbs757IlsPanel.PlanSteps(new Bbs757IlsDial(4, f), null)));
    }

    // ==== The definition: the count and the set ===========================================================

    /// <summary>The fresh read made before every click: the ILS course, the variable the course set reads back.</summary>
    private const string ReadBefore = "read BBS757_ILS_COURSE";

    /// <summary>
    /// The sim behind the definition's ILS set, as the set sees it: whether a write can land, the answer to a
    /// fresh read, a clock the waits move, and, in order, every read, wait and write it makes.
    /// </summary>
    private sealed class PanelSim
    {
        public bool CanWrite = true;
        public long Now = 1_000_000;

        /// <summary>What a fresh read answers (null: the sim did not answer it in time).</summary>
        public double? ReadAnswer = 95;

        public readonly List<string> Writes = new();
        public readonly List<string> Log = new();

        /// <summary>Runs at each wait, with the wait's number (1 for the first), before the wait ends.</summary>
        public Action<int>? OnWait;

        /// <summary>Runs at each write, with the write's number (1 for the first), before it is recorded.</summary>
        public Action<int>? OnWrite;

        private int _waits;

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, ct) =>
            {
                Log.Add($"read {key}");
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(ReadAnswer);
            },
            Cached = key => throw new InvalidOperationException($"the ILS set read the cache of {key}"),
            Write = rpn =>
            {
                OnWrite?.Invoke(Writes.Count + 1);
                Writes.Add(rpn);
                Log.Add(rpn);
            },
            RequestRead = key => throw new InvalidOperationException($"the ILS set re-read {key}"),
            Delay = (ms, ct) =>
            {
                Log.Add($"wait {ms}");
                Now += ms;
                OnWait?.Invoke(++_waits);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>A definition whose writes go to <see cref="Sim"/> on its clock, and what it said and gave the window's status box.</summary>
    private sealed class Rig
    {
        public readonly PanelSim Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly List<string> Status = new();
        public readonly BBS757Definition Def;

        public Rig() => Def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => Sim.Io, NowMs = () => Sim.Now };

        /// <summary>A set from the window: a frequency, or PARK (null).</summary>
        public Task Set(double? mhz) => Def.SetIlsAsync(mhz, new SimConnectManager(IntPtr.Zero), Speech, Status.Add);

        /// <summary>MainForm's AircraftLoaded: a flight is loading, and BlueBird's panel may not run yet.</summary>
        public void LoadBegins() => Def.OnSimContextReset();

        /// <summary>A COM row delivered (the 1 Hz batch): the flight runs.</summary>
        public void ComDelivered() => Def.ProcessSimVarUpdate(Bbs757Radios.ActiveKey(1), 121.9, new SpeechCapture());

        /// <summary>A flight load, then its first COM delivery.</summary>
        public void FlightLoad()
        {
            LoadBegins();
            ComDelivered();
        }

        /// <summary>A dropped connection: MainForm.OnConnectionLost calls both, in this order.</summary>
        public void ConnectionDrop()
        {
            Def.OnSimContextReset();
            Def.OnVariableCacheCleared();
        }
    }

    /// <summary>The clicks from the load state to 110.50: WHOLE -1 x2, FRAC +1 x4 (as round 3 turned them by hand).</summary>
    private static readonly string[] LoadStateTo11050 = { WholeDown, WholeDown, FractionUp, FractionUp, FractionUp, FractionUp };

    [Fact]
    public async Task Count_after_a_flight_load_and_unknown_after_a_dropped_connection()
    {
        var rig = new Rig();

        // A new definition (MSFSBA started, or the 757 profile picked, after the flight loaded): the app
        // has not seen the panel as it loaded.
        Assert.Null(rig.Def.IlsDial);

        rig.LoadBegins();
        Assert.Null(rig.Def.IlsDial);   // loading: BlueBird's panel may not run yet
        rig.ComDelivered();
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);   // the flight runs: the panel as it loads
        rig.ComDelivered();
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);

        await rig.Set(110.50);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);   // the count follows the clicks sent
        rig.ComDelivered();
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);   // a later COM delivery is no flight load

        rig.FlightLoad();   // the next flight's panel loads parked again
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);

        await rig.Set(109.30);
        rig.ConnectionDrop();   // whatever happens to the panel until the app is back is unseen
        Assert.Null(rig.Def.IlsDial);
        rig.ComDelivered();     // the reconnect's re-fire: still no flight load seen
        Assert.Null(rig.Def.IlsDial);

        rig.FlightLoad();   // reconnected, and a flight loaded: counted again
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);
    }

    [Fact]
    public async Task A_set_between_a_flight_load_and_the_first_com_delivery_is_refused()
    {
        // AircraftLoaded fires during the loading screen, before BlueBird's panel runs: a click then could be
        // lost while the count moved. The count starts once the flight runs (a COM row delivered).
        var rig = new Rig();
        rig.LoadBegins();

        await rig.Set(110.50);

        Assert.Equal(new[] { "ILS tuning unknown: use ILS sync first." }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Log);

        rig.ComDelivered();
        await rig.Set(110.50);

        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);
        Assert.Equal("ILS 110.50 set", rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public async Task Each_click_follows_a_fresh_read_and_back_to_back_sets_keep_the_gap()
    {
        // A wall-clock gap alone cannot keep two clicks in separate sim frames across a stall: each click
        // waits for a fresh read (answered by the sim in a frame after the last click went out), then the gap.
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(108.10);   // PARK to 108: one click
        await rig.Set(null);     // at once: its first click waits out the gap since the last one
        rig.Sim.Now += 100;
        await rig.Set(108.10);   // 100 ms later: the rest of the gap
        rig.Sim.Now += 400;
        await rig.Set(null);     // 400 ms later: no wait

        Assert.Equal(new[]
        {
            ReadBefore, WholeUp,
            ReadBefore, "wait 250", WholeDown,
            ReadBefore, "wait 150", WholeUp,
            ReadBefore, WholeDown,
        }, rig.Sim.Log);
    }

    [Fact]
    public async Task A_click_whose_read_the_sim_does_not_answer_is_not_sent()
    {
        // No answer: nothing shows the sim moved on from the last click, so the next could share its frame.
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWrite = n =>
        {
            if (n == 1) rig.Sim.ReadAnswer = null;   // the sim stops answering after the first click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { ReadBefore, WholeDown, ReadBefore }, rig.Sim.Log);
        Assert.Equal(new[] { WholeDown }, rig.Sim.Writes);
        Assert.Equal(new[] { "ILS unavailable" }, rig.Speech.Interrupts);
        Assert.Equal(new Bbs757IlsDial(3, 0), rig.Def.IlsDial);   // the click sent is counted: 111.10
    }

    [Theory]
    [InlineData("flight load")]
    [InlineData("dropped connection")]
    public async Task A_set_stopped_by_a_flight_load_or_a_drop_shows_the_count_without_saying_it(string reset)
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n != 2) return;
            if (reset == "flight load") rig.LoadBegins();
            else rig.ConnectionDrop();
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);                                // nothing said
        Assert.Equal(new[] { "ILS tuning unknown" }, rig.Status);    // the window no longer says it is setting
    }

    [Fact]
    public async Task Set_refused_while_unknown()
    {
        var rig = new Rig();

        await rig.Set(110.50);
        await rig.Set(null);

        const string refusal = "ILS tuning unknown: use ILS sync first.";
        Assert.Equal(new[] { refusal, refusal }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(new[] { refusal, refusal }, rig.Status);   // the window's status says it too
        Assert.Empty(rig.Sim.Log);                               // nothing written, nothing waited
        Assert.Null(rig.Def.IlsDial);
    }

    [Fact]
    public async Task Steps_are_sent_250_ms_apart()
    {
        // BlueBird takes a click within a frame, and two landing in one frame count once (20 ms apart lost
        // steps; 50 and 150 ms none; a 192 ms frame was seen): one write per click, 250 ms between them.
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(110.50);

        // Each click after a fresh read; from the second on, the gap after it.
        Assert.Equal(new[]
        {
            ReadBefore, WholeDown,
            ReadBefore, "wait 250", WholeDown,
            ReadBefore, "wait 250", FractionUp,
            ReadBefore, "wait 250", FractionUp,
            ReadBefore, "wait 250", FractionUp,
            ReadBefore, "wait 250", FractionUp,
        }, rig.Sim.Log);
        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);
        Assert.Equal(250, BBS757Definition.IlsStepGapMs);
        // Said once, from the count: never "tuned" (nothing can read the panel back).
        Assert.Equal(new[] { "ILS 110.50 set" }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(new[] { "ILS 110.50 set" }, rig.Status);
    }

    [Fact]
    public async Task Park_is_the_whole_knob_and_says_parked()
    {
        var rig = new Rig();
        rig.FlightLoad();
        await rig.Set(110.50);
        rig.Sim.Log.Clear();
        rig.Speech.Interrupts.Clear();

        await rig.Set(null);

        // 110, 111, PARK; pressed right after the set, so its first click waits out the gap too.
        Assert.Equal(new[] { ReadBefore, "wait 250", WholeUp, ReadBefore, "wait 250", WholeUp }, rig.Sim.Log);
        Assert.Equal(new[] { "ILS parked" }, rig.Speech.Interrupts);
        Assert.Equal(new Bbs757IlsDial(4, 4), rig.Def.IlsDial);              // PARK, remembering .50
    }

    [Fact]
    public async Task Already_there_sends_nothing()
    {
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(null);   // parked as the flight loaded
        await rig.Set(110.50);
        rig.Sim.Writes.Clear();
        await rig.Set(110.50);

        Assert.Empty(rig.Sim.Writes);
        Assert.Equal(new[] { "ILS already parked", "ILS 110.50 set", "ILS already 110.50" }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Status);
    }

    [Fact]
    public async Task A_set_that_cannot_land_is_refused_aloud_and_nothing_is_written()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.CanWrite = false;

        await rig.Set(110.50);
        await rig.Set(null);

        Assert.Equal(new[] { "ILS unavailable", "ILS unavailable" }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Status);
        Assert.Empty(rig.Sim.Log);
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);
    }

    [Fact]
    public async Task A_frequency_that_is_no_channel_is_refused_aloud()
    {
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(110.52);
        await rig.Set(118.0);

        Assert.Equal(new[] { Invalid, Invalid }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Log);
    }

    [Fact]
    public async Task A_set_while_another_turns_the_knobs_is_refused()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n == 1) _ = rig.Set(null);   // the park button, pressed between the first two clicks
        };

        await rig.Set(110.50);

        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);   // the park sent nothing
        Assert.Equal(new[] { "ILS tuning in progress", "ILS 110.50 set" }, rig.Speech.Interrupts);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);

        await rig.Set(null);   // finished: the next set runs
        Assert.Equal("ILS parked", rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public async Task A_flight_load_stops_a_set_between_clicks_and_none_turns_the_next_flights_knobs()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n == 2) rig.LoadBegins();   // after the second click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
        Assert.Null(rig.Def.IlsDial);   // loading

        rig.ComDelivered();             // the new flight runs: its panel as it loads
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);
        rig.Sim.OnWait = null;
        await rig.Set(110.50);          // counted from there
        Assert.Equal(LoadStateTo11050, rig.Sim.Writes.Skip(2));
    }

    [Fact]
    public async Task A_profile_switch_stops_a_set_between_clicks_and_the_count_keeps_the_clicks_sent()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n == 3) rig.Def.OnSwitchedAway();   // after the third click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown, FractionUp }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);   // nothing said into the next aircraft
        Assert.Empty(rig.Status);       // nor shown: the window is closed
        Assert.Equal(Dial(110.15), rig.Def.IlsDial);
    }

    [Fact]
    public async Task A_click_that_throws_says_unavailable_and_leaves_the_count_unknown()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWrite = n =>
        {
            if (n == 3) throw new InvalidOperationException("no link");
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown }, rig.Sim.Writes);
        Assert.Equal(new[] { "ILS unavailable" }, rig.Speech.Interrupts);
        Assert.Null(rig.Def.IlsDial);   // whether the third click went out cannot be known

        rig.Sim.OnWrite = null;
        await rig.Set(110.50);           // and a set from an unknown count is refused
        Assert.Equal("ILS tuning unknown: use ILS sync first.", rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public async Task A_set_after_a_switch_away_does_nothing()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Def.OnSwitchedAway();

        await rig.Set(110.50);
        await rig.Set(null);

        Assert.Empty(rig.Sim.Log);
        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_set_writes_and_says_no_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var rig = new Rig();
            rig.FlightLoad();

            await rig.Set(110.50);
            await rig.Set(110.50);

            Assert.Equal(LoadStateTo11050, rig.Sim.Writes);   // "-1", never "−1"
            Assert.Equal(new[] { "ILS 110.50 set", "ILS already 110.50" }, rig.Speech.Interrupts);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
