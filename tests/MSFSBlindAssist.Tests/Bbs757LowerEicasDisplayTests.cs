// The 757 lower EICAS as one reader of the 757's display monitor: its names and keys, its spoken lines (page changes
// and STATUS messages, S11), its window rows, and the shared monitor settling its changes.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757LowerEicasGateTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757LowerEicasDisplayTests
{
    private const string Key = "MSFSBA_BBS757_STATUS_MESSAGES";

    private static Bbs757LowerEicasReading Status(params string[] messages) => R(page: 2, messages: messages);

    private static IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757LowerEicasReading heard, Bbs757LowerEicasReading now, bool prefix = true) =>
        new Bbs757LowerEicasDisplay().Changes(heard, now, prefix);

    private static string[] Texts(IReadOnlyList<Bbs757SpokenLine> lines) => lines.Select(l => l.Text).ToArray();

    [Fact]
    public void The_reader_is_the_lower_eicas_on_the_status_messages_row()
    {
        Assert.Equal("Lower EICAS", Bbs757LowerEicasDisplay.Name);
        Assert.Equal(Key, Bbs757LowerEicasDisplay.MuteKey);
        Assert.Equal("bbs757-lower-eicas", Bbs757LowerEicasDisplay.LogChannel);
        Assert.Equal(250, Bbs757LowerEicasDisplay.TickMs);
        Assert.Equal("EICAS status messages", Bbs757LowerEicasDisplay.MonitorRowLabel);
        Assert.Equal(new[] { "TURB ENG N2:1", "TURB ENG N2:2", "L:BBS_ECAM_Mode" }, Bbs757LowerEicasDisplay.GateKeys);
        IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> reader = new Bbs757LowerEicasDisplay();
        Assert.Equal(3, reader.GateVariables.Count);
        Assert.Equal("percent", reader.GateVariables[0].Units);
        Assert.True(reader.Lit(R(), default));
        Assert.False(reader.Lit(R(powered: false), default));
    }

    [Fact]
    public void A_page_change_is_spoken_with_the_page_words()
    {
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, Texts(Changes(R(1), R(2))));
        Assert.Equal(new[] { "Lower EICAS: engine page" }, Texts(Changes(R(2), R(1))));
        Assert.Equal(new[] { "Lower EICAS: engine page off" }, Texts(Changes(R(1), R(3))));
        Assert.Equal(new[] { "Lower EICAS: blank" }, Texts(Changes(R(1), R(0))));
        Assert.Equal(new[] { "STATUS page" }, Texts(Changes(R(1), R(2), prefix: false)));
        // Two values of one kind are the same page.
        Assert.Empty(Changes(R(0), R(9)));
        Assert.Empty(Changes(R(1), R(1, left: 70)));
    }

    [Fact]
    public void Messages_present_when_status_opens_are_not_spoken()
    {
        var lines = Changes(R(1, messages: new[] { "WARN ELEX" }), Status("WARN ELEX"));
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, Texts(lines));
        // One that appeared under another page is never news, not even when STATUS then opens.
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, Texts(Changes(R(1), Status("WARN ELEX"))));
        // Leaving STATUS says only the page change.
        Assert.Equal(new[] { "Lower EICAS: engine page" }, Texts(Changes(Status("WARN ELEX"), R(1))));
    }

    [Fact]
    public void A_new_status_message_on_the_status_page_is_spoken()
    {
        Assert.Equal(new[] { "Status: L EICAS CMPTR" }, Texts(Changes(Status("WARN ELEX"), Status("L EICAS CMPTR", "WARN ELEX"))));
        Assert.Equal(new[] { "Status gone: WARN ELEX" }, Texts(Changes(Status("L EICAS CMPTR", "WARN ELEX"), Status("L EICAS CMPTR"))));
    }

    [Fact]
    public void Several_new_messages_share_one_line()
    {
        var lines = Changes(Status("A", "B"), Status("C", "D", "A"));
        Assert.Equal(new[] { "Status: C, D", "Status gone: B" }, Texts(lines));
        Assert.Equal(new[] { "Status gone: A, B" }, Texts(Changes(Status("A", "B", "C"), Status("C"))));
    }

    [Fact]
    public void A_status_message_off_the_status_page_is_not_spoken()
    {
        Assert.Empty(Changes(R(1), R(1, messages: new[] { "WARN ELEX" })));
        Assert.Empty(Changes(R(3), R(3, messages: new[] { "WARN ELEX" })));
    }

    [Fact]
    public void A_message_gone_off_the_status_page_is_not_spoken()
    {
        Assert.Empty(Changes(R(1, messages: new[] { "WARN ELEX" }), R(1)));
    }

    [Fact]
    public void A_page_change_and_messages_come_in_order()
    {
        // From another page both readings are not STATUS: only the page change. Texts keep BlueBird's words exactly.
        var lines = Changes(Status("Z"), Status("Y  X", "Z"));
        Assert.Equal(new[] { "Status: Y  X" }, Texts(lines));
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        // Every line, page changes and messages alike, carries the one mute key the monitor checks.
        var lines = Changes(Status("A"), Status("B"));
        Assert.All(lines, l => Assert.Equal(Key, l.MuteKey));
        Assert.All(Changes(R(1), R(2)), l => Assert.Equal(Key, l.MuteKey));
    }

    [Fact]
    public void A_page_without_its_part_lists_not_read()
    {
        var layout = Bbs757LowerEicasLayoutTests.Sample() with { Status = null, StatusMissing = "no list" };
        var raw = new Bbs757LowerEicasRaw(2, true, false, true, null, null, Array.Empty<string>());
        var rows = Bbs757LowerEicasText.PageRows(layout, raw);
        var reading = new Bbs757LowerEicasReading(2, "STATUS", rows, Array.Empty<string>(), 77, 77, true, false);
        Assert.Equal(new[] { "Page: STATUS", "STATUS page: not read: no list" }, new Bbs757LowerEicasDisplay().Rows(reading));
        // It speaks nothing of its own: the same page again, and no message.
        Assert.Empty(Changes(reading, reading with { LeftHpSpool = 76 }));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Numbers_format_as_drawn_under_de_DE_and_sv_SE(string culture)
    {
        var layout = Bbs757LowerEicasLayoutTests.Sample();
        var engine = new Bbs757LowerEicasEngineRaw(true, 81.99, 77.4, 3600, 91.9, 114.7, 0.58, -1.84, 0, 0, false, false, 77.4);
        var status = new Bbs757LowerEicasStatusRaw(new double[] { 1, 1, 1 }, new[] { 0.98, 0.97, 1.0 },
            new double[] { 3000, 2880, 2950 }, -55, 0, 0.75, true, true, 1850, 0, 1760, 0.123, 0, 0, 0, new uint[8]);
        var raw = new Bbs757LowerEicasRaw(2, true, false, false, new[] { engine, engine }, status, new[] { "WARN ELEX" });
        var reading = new Bbs757LowerEicasReading(2, "STATUS", Bbs757LowerEicasText.PageRows(layout, raw),
            new[] { "WARN ELEX" }, 77, 77, true, false);
        var display = new Bbs757LowerEicasDisplay();
        var rows = TestCulture.Run(culture, () => display.Rows(reading));
        Assert.Equal("Page: STATUS", rows[0]);
        Assert.Contains("HYD QTY: L 0.98, C 0.97, R 1.00", rows);
        Assert.Contains("FF: 1.6, 1.6", rows); // kilograms, one decimal, a point
        Assert.Contains("RUD: 12 % right", rows);
        string all = TestCulture.Run(culture, () => display.DescribeAll(reading));
        Assert.StartsWith("STATUS page, Page: STATUS, HYD QTY: L 0.98, C 0.97, R 1.00", all);
    }

    [Fact]
    public void Rows_of_a_blank_display_is_the_one_row()
    {
        var display = new Bbs757LowerEicasDisplay();
        Assert.Equal(new[] { "Lower EICAS: blank" }, display.Rows(R(0)));
        Assert.Equal("blank", display.DescribeAll(R(0)));
        Assert.Equal(new[] { "Page: engine" }, display.Rows(R(1)));
        Assert.Equal("engine page, Page: engine", display.DescribeAll(R(1)));
    }

    // ---- through the monitor ------------------------------------------------------------------------------------

    /// <summary>The lower EICAS reader's words and gate with a scripted sampler in place of memory.</summary>
    private sealed class Scripted : IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>
    {
        private readonly Bbs757LowerEicasDisplay _inner = new();
        public Bbs757LowerEicasReading Current = R(1);

        public string Name => Bbs757LowerEicasDisplay.Name;
        public string MuteKey => Bbs757LowerEicasDisplay.MuteKey;
        public string LogChannel => Bbs757LowerEicasDisplay.LogChannel;
        public int TickMs => Bbs757LowerEicasDisplay.TickMs;
        public IReadOnlyList<string> GateKeys => Bbs757LowerEicasDisplay.GateKeys;
        public IReadOnlyList<Bbs757GateVariable> GateVariables => Bbs757LowerEicasGate.Variables;
        public IReadOnlyList<Bbs757LowerEicasLayout> LayoutsIn(ModuleAnalysis analysis) => new[] { Bbs757LowerEicasLayoutTests.Sample() };
        public IBbs757DisplaySampler<Bbs757LowerEicasReading>? SamplerFor(LocatedModule module, Bbs757LowerEicasLayout layout) => new Sampler(this);
        public bool Passes(IReadOnlyList<(long AtMs, Bbs757LowerEicasReading Reading)> h, Bbs757SimValues s) => _inner.Passes(h, s);
        public bool? Lit(Bbs757LowerEicasReading r, Bbs757SimValues s) => _inner.Lit(r, s);
        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757LowerEicasReading a, Bbs757LowerEicasReading b, bool p) => _inner.Changes(a, b, p);
        public IReadOnlyList<string> Rows(Bbs757LowerEicasReading r) => _inner.Rows(r);
        public string DescribeAll(Bbs757LowerEicasReading r) => _inner.DescribeAll(r);

        private sealed class Sampler(Scripted owner) : IBbs757DisplaySampler<Bbs757LowerEicasReading>
        {
            public bool TryRead(out Bbs757LowerEicasReading reading)
            {
                reading = owner.Current;
                return true;
            }

            public void Dispose()
            {
            }
        }
    }

    private static (Scripted Reader, Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> Monitor,
        Bbs757DisplayMonitorTests.FakeEnvironment Env, Action<int> Tick) Run()
    {
        var reader = new Scripted();
        var env = new Bbs757DisplayMonitorTests.FakeEnvironment { Simulator = Sim() };
        var monitor = new Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>(reader);
        monitor.Start(env, startTimer: false);
        void Tick(int n)
        {
            for (int i = 0; i < n; i++)
            {
                env.NowMs += 250;
                monitor.Tick();
            }
        }
        // The display is lit from the start (its power-up was not seen), so the first good reading is the silent baseline.
        Tick(8);
        return (reader, monitor, env, Tick);
    }

    [Fact]
    public void A_page_change_is_said_once_after_settling()
    {
        var (reader, monitor, env, tick) = Run();
        Assert.Equal("Running", monitor.StateName);
        Assert.Empty(env.Spoken);

        reader.Current = R(2);
        env.Simulator = Sim(mode: 2);
        tick(1);
        Assert.Empty(env.Spoken); // not yet settled
        tick(6);
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, env.Spoken);
        tick(10);
        Assert.Single(env.Spoken);
        Assert.Equal("Page: STATUS", monitor.View()[0]);
    }

    [Fact]
    public void Fast_page_changes_say_only_the_last()
    {
        var (reader, monitor, env, tick) = Run();
        // STATUS, back to engine and on to engine off, each shorter than the settle (300 ms): only the page that holds.
        foreach (int page in new[] { 2, 1, 3 })
        {
            reader.Current = R(page);
            env.Simulator = Sim(mode: page);
            tick(1);
        }
        tick(6);
        Assert.Equal(new[] { "Lower EICAS: engine page off" }, env.Spoken);

        // A change and back inside the settle says nothing.
        reader.Current = R(1);
        env.Simulator = Sim(mode: 1);
        tick(1);
        reader.Current = R(3);
        env.Simulator = Sim(mode: 3);
        tick(8);
        Assert.Single(env.Spoken);
    }

    [Fact]
    public void A_muted_row_says_nothing_through_the_monitor()
    {
        var (reader, monitor, env, tick) = Run();
        env.Muted.Add(Key);
        reader.Current = R(2);
        env.Simulator = Sim(mode: 2);
        tick(8);
        Assert.Empty(env.Spoken);
        Assert.Equal("Page: STATUS", monitor.View()[0]);
    }

    [Fact]
    public void A_message_while_status_shows_is_spoken_through_the_monitor()
    {
        var (reader, monitor, env, tick) = Run();
        reader.Current = Status("WARN ELEX");
        env.Simulator = Sim(mode: 2);
        tick(8);
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, env.Spoken);

        reader.Current = Status("L EICAS CMPTR", "WARN ELEX");
        tick(8);
        Assert.Equal(new[] { "Lower EICAS: STATUS page", "Status: L EICAS CMPTR" }, env.Spoken);
    }
}
