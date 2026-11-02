using System.Globalization;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Nothing compared a value the reader takes from the copy with the same value the A350 publishes as an L:var, so after an
// update a layout that looks right but reads shifted values would be spoken with confidence (the probe's sweeps once named
// every value 24 entries off while the table's own check passed). The check runs at every start.
public class A350FactsCheckTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);

    private static A350FactsPair Table(string name, double copy, double sim) => new(A350FactsPart.Table, name, copy, sim);
    private static A350FactsPair Asked(string name, double copy, double sim) => new(A350FactsPart.Variables, name, copy, sim);

    /// <summary>A first copy the pairs can be counted against: a pair counts only with the sim value of the copy before.</summary>
    private static A350FactsCheck Started(params A350FactsPair[] pairs)
    {
        var check = new A350FactsCheck();
        check.Add(pairs, T0);
        return check;
    }

    [Fact]
    public void Three_agreeing_copies_two_seconds_apart_agree()
    {
        var pair = Table("INI_VLS_SPEED", 166, 166);
        var check = Started(pair);
        Assert.Empty(check.Add(new[] { pair }, T0.AddSeconds(2)));
        Assert.Empty(check.Add(new[] { pair }, T0.AddSeconds(4)));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Table));
        Assert.Equal(new[] { A350FactsPart.Table }, check.Add(new[] { pair }, T0.AddSeconds(6)));
        Assert.Equal(A350FactsVerdict.Agrees, check.Verdict(A350FactsPart.Table));
        Assert.Equal("facts check, table: agrees on 1 pair (INI_VLS_SPEED 166 in the copy, 166 in the sim)", check.LogLine(A350FactsPart.Table));
    }

    [Fact]
    public void Three_disagreeing_copies_disagree_and_keep_their_pairs()
    {
        var good = Table("INI_VFE_SPEED", 200, 200);
        var bad = Table("INI_VLS_SPEED", 240.5, 166);
        var check = Started(good, bad);
        for (int i = 1; i <= 3; i++) check.Add(new[] { good, bad }, T0.AddSeconds(2 * i));
        Assert.Equal(A350FactsVerdict.Disagrees, check.Verdict(A350FactsPart.Table));
        Assert.Equal("facts check, table: disagrees on 1 of 2 pairs (INI_VLS_SPEED 240.5 in the copy, 166 in the sim)", check.LogLine(A350FactsPart.Table));
    }

    [Fact]
    public void A_pair_whose_sim_value_moved_since_the_last_copy_does_not_count()
    {
        var check = Started(Table("INI_VLS_SPEED", 160, 160));
        for (int i = 1; i <= 6; i++)
            check.Add(new[] { Table("INI_VLS_SPEED", 160 + i, 160 + i + 5) }, T0.AddSeconds(2 * i));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Table));
    }

    [Fact]
    public void A_pair_of_two_zeros_does_not_count()
    {
        var pair = Table("INI_VLS_SPEED", 0, 0);
        var check = Started(pair);
        for (int i = 1; i <= 6; i++) check.Add(new[] { pair }, T0.AddSeconds(2 * i));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Table));
    }

    [Fact]
    public void A_copy_of_the_other_kind_starts_the_run_again()
    {
        var good = Table("INI_VLS_SPEED", 166, 166);
        var bad = Table("INI_VLS_SPEED", 240, 166);
        var check = Started(good);
        check.Add(new[] { good }, T0.AddSeconds(2));
        check.Add(new[] { good }, T0.AddSeconds(4));
        check.Add(new[] { bad }, T0.AddSeconds(6));
        check.Add(new[] { good }, T0.AddSeconds(8));
        check.Add(new[] { good }, T0.AddSeconds(10));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Table));
        check.Add(new[] { good }, T0.AddSeconds(12));
        Assert.Equal(A350FactsVerdict.Agrees, check.Verdict(A350FactsPart.Table));
    }

    [Fact]
    public void Copies_closer_than_two_seconds_count_once()
    {
        var pair = Table("INI_VLS_SPEED", 166, 166);
        var check = Started(pair);
        for (int i = 1; i <= 20; i++) check.Add(new[] { pair }, T0.AddMilliseconds(100 * i));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Table));
    }

    [Fact]
    public void The_table_and_the_variables_decide_apart()
    {
        var table = Table("INI_VLS_SPEED", 240, 166);
        var asked = Asked("INI_PACK", 1, 1);
        var check = Started(table, asked);
        for (int i = 1; i <= 2; i++) check.Add(new[] { table, asked }, T0.AddSeconds(2 * i));
        var decided = check.Add(new[] { table }, T0.AddSeconds(6));
        Assert.Equal(new[] { A350FactsPart.Table }, decided);
        Assert.Equal(A350FactsVerdict.Disagrees, check.Verdict(A350FactsPart.Table));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Variables));
        Assert.Equal("facts: table disagrees, variables undecided", check.MinuteWords);
        Assert.Equal(new[] { A350FactsPart.Variables }, check.Add(new[] { asked }, T0.AddSeconds(8)));
        Assert.Equal("facts: table disagrees, variables agrees", check.MinuteWords);
    }

    // Each part decides once: a value that moves later says nothing new.
    [Fact]
    public void A_part_decided_stays_decided()
    {
        var pair = Table("INI_VLS_SPEED", 166, 166);
        var check = Started(pair);
        for (int i = 1; i <= 3; i++) check.Add(new[] { pair }, T0.AddSeconds(2 * i));
        var bad = Table("INI_VLS_SPEED", 240, 166);
        for (int i = 4; i <= 9; i++) Assert.Empty(check.Add(new[] { bad }, T0.AddSeconds(2 * i)));
        Assert.Equal(A350FactsVerdict.Agrees, check.Verdict(A350FactsPart.Table));
    }

    // A check part with no pair (an L:var MSFSBA streams that the copy lacks, or draws that ask for none it streams).
    [Fact]
    public void With_no_pair_a_part_stays_undecided()
    {
        var check = new A350FactsCheck();
        for (int i = 0; i <= 9; i++) Assert.Empty(check.Add(Array.Empty<A350FactsPair>(), T0.AddSeconds(2 * i)));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Table));
        Assert.Equal(A350FactsVerdict.Undecided, check.Verdict(A350FactsPart.Variables));
        Assert.Equal("facts: table undecided, variables undecided", check.MinuteWords);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_check_lines_read_the_same_under_de_DE_and_sv_SE(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var bad = Table("INI_VLS_SPEED", 240.5, 166.25);
            var check = Started(bad);
            for (int i = 1; i <= 3; i++) check.Add(new[] { bad }, T0.AddSeconds(2 * i));
            Assert.Equal("facts check, table: disagrees on 1 of 1 pairs (INI_VLS_SPEED 240.5 in the copy, 166.25 in the sim)", check.LogLine(A350FactsPart.Table));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData(true, false, "MSFSBA does not match this A350 version yet: pause alerts off")]
    [InlineData(false, true, "MSFSBA does not match this A350 version yet: display values may be wrong")]
    [InlineData(true, true, "MSFSBA does not match this A350 version yet: pause alerts off, display values may be wrong")]
    public void The_sentence_names_what_is_lost(bool table, bool variables, string spoken) =>
        Assert.Equal(spoken, A350DisplayStatus.FactsMismatch(table, variables));

    [Fact]
    public void A_names_table_slot_is_where_PauseOf_finds_the_pause()
    {
        var facts = A350ModuleFacts.From(A350ModuleFactsTests.WithVariableNames());
        Assert.Equal(facts.Pause!.IsPaused, facts.Table!.SlotOf("INI_IS_PAUSED"));
        Assert.Equal(facts.Pause.Type, facts.Table.SlotOf("FMGS_PAUSE_TYPE"));
        Assert.Equal(1343616u, facts.Table.SlotOf("INI_OTHER"));
        Assert.Null(facts.Table.SlotOf("INI_NOT_THERE"));
        Assert.Null(A350ModuleFacts.From(A350ModuleFactsTests.WithVariableNames(withNames: false)).Table);
    }

    [Fact]
    public void The_engine_keeps_what_it_answered_each_asked_variable()
    {
        using var engine = A350DrawEngineTests.Engine();
        engine.Load(A350DrawEngineTests.CopyWithVariable(8000, 1.5));
        Assert.Empty(engine.AskedVariables);
        Assert.Equal(1.5, engine.LVarValue(77));
        Assert.Equal(new[] { ("INI_TESTV", 1.5) }, engine.AskedVariables);
        engine.Load(A350DrawEngineTests.CopyWithVariable(8000, 2.5));   // the next copy's own
        Assert.Empty(engine.AskedVariables);
    }
}

/// <summary>The check in the service, on the memory source of <see cref="A350TrapRetryTests"/>.</summary>
public class A350FactsCheckServiceTests
{
    /// <summary>A memory source whose copies hold the test gauge and the pause's two slots, 1 in INI_IS_PAUSED.</summary>
    private sealed class PausedCopies : IA350MemorySource
    {
        private readonly A350MemoryCopy _copy;
        public PausedCopies()
        {
            var low = A350DrawEngineTests.Copy(trap: false).Buffer;
            var all = new byte[low.Length + 64];
            low.CopyTo(all, 0);
            BitConverter.GetBytes(1.0).CopyTo(all, low.Length + 8);
            _copy = new A350MemoryCopy();
            _copy.Set(new[] { (0u, low.Length), (1343616u, 64) }, all);
        }

        public ulong Base => 1;
        public int LastReread => 0;
        public long LastBytes => 65536;
        public int LastLeftOut => 0;
        public A350ReaderFailure? LastFailure => null;
        public bool TryLocate() => true;
        public bool IsStillValid() => true;
        public A350MemoryCopy Snapshot() => _copy;
        public void Dispose() { }
    }

    private static (A350DisplayService Service, List<string> Said, List<A350SimPause> Pauses) Service(double simSaysPaused)
    {
        DateTime now = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
        var facts = A350ModuleFacts.From(A350ModuleFactsTests.WithVariableNames());
        var service = new A350DisplayService(new PausedCopies(), A350DrawEngineTests.Engine(), facts)
        {
            Clock = () => now = now.AddSeconds(3),
            LVarCached = name => name == "INI_IS_PAUSED" ? simSaysPaused : null,
            StreamedLVars = new[] { "INI_IS_PAUSED", "INI_NOT_IN_THE_TABLE" },
        };
        var said = new List<string>();
        var pauses = new List<A350SimPause>();
        service.Spoken += said.Add;
        service.PauseRead += pauses.Add;
        return (service, said, pauses);
    }

    private static async Task Draws(A350DisplayService service, int count)
    {
        for (int i = 0; i < count; i++) await service.DrawAsync(("CTR", 0));
    }

    [Fact]
    public async Task A_table_disagreement_stops_the_pause_reads()
    {
        var (service, _, pauses) = Service(simSaysPaused: 0);
        using (service)
        {
            await Draws(service, 4);
            Assert.Equal(4, pauses.Count);   // until it decides, everything reads as today
            await Draws(service, 3);
            Assert.Equal(4, pauses.Count);
        }
    }

    [Fact]
    public async Task A_disagreement_is_said_once_and_an_agreement_never()
    {
        var (bad, badSaid, _) = Service(simSaysPaused: 0);
        using (bad)
        {
            await Draws(bad, 8);
            Assert.Equal(new[] { "MSFSBA does not match this A350 version yet: pause alerts off" }, badSaid);
        }
        var (good, goodSaid, goodPauses) = Service(simSaysPaused: 1);
        using (good)
        {
            await Draws(good, 8);
            Assert.Empty(goodSaid);
            Assert.Equal(8, goodPauses.Count);
        }
    }
}
