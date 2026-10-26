using System.Diagnostics;
using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Output N on the BlueBird 757 (plan 04, task 1): the VOR tunings come from the NAV RADIO page of the
/// first officer's CDU (<see cref="Bbs757NavRad"/>), the courses from BlueBird's own L:vars, and the
/// stock NAV radios, which BlueBird does not use, are never read. The screens are session 8's: row 2 of
/// the page holds the tunings ("116.90AGSU" auto, "113.90R" tuned from the CDU, "------" not tuned).
/// </summary>
public class Bbs757NavRadTests
{
    private const int Fo = Bbs757CduPages.FirstOfficer;

    // ---- Screens ------------------------------------------------------------------------------

    /// <summary>A left text and a right text on one 24-column row, the right one flush to the edge.</summary>
    private static string Row(string left, string right = "")
        => left.PadRight(Bbs757CduFrame.Columns - right.Length) + right;

    private static Bbs757CduScreen Screen(string title, string tunings)
    {
        var lines = Enumerable.Repeat(new string(' ', Bbs757CduFrame.Columns), Bbs757CduFrame.Rows).ToArray();
        lines[0] = title.PadRight(Bbs757CduFrame.Columns);
        lines[1] = Row(" VOR L", "VOR R ");
        lines[2] = tunings;
        lines[3] = Row(" CRS", "CRS ");
        lines[4] = Row(" 095", "000 ");
        return new Bbs757CduScreen(lines);
    }

    /// <summary>The NAV RADIO page with the given VOR L and VOR R texts on its tuning row.</summary>
    private static Bbs757CduScreen NavRadio(string left, string right = "------")
        => Screen("        NAV RADIO       ", Row(left, right));

    private static Bbs757CduScreen Legs() => Screen("  ACT RTE 1 LEGS     1/7", Row("", ""));

    private static readonly Bbs757VorTuning NotTuned = new(null, null, null);

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

    // ---- The page -------------------------------------------------------------------------------

    [Fact]
    public void Parses_auto_and_remote()
    {
        // 116.90AGSU: auto (the FMC tuned it), ident GSU. 113.90R: tuned from the CDU, no ident yet.
        // VOR R reads ------ when it is not tuned.
        var auto = Bbs757NavRad.Parse(NavRadio("116.90AGSU"));
        Assert.Equal(new Bbs757VorTuning(116.90, 'A', "GSU"), auto!.Left);
        Assert.Equal(NotTuned, auto.Right);

        var remote = Bbs757NavRad.Parse(NavRadio("113.90R"));
        Assert.Equal(new Bbs757VorTuning(113.90, 'R', null), remote!.Left);
        Assert.Equal(NotTuned, remote.Right);
    }

    [Theory]
    [InlineData("114.20MABC", 114.20, 'M', "ABC")]   // any other letter is a manual tuning
    [InlineData("108.00", 108.00, null, null)]        // no letter, no ident
    public void Parses_other_modes(string text, double mhz, char? mode, string? ident)
    {
        var page = Bbs757NavRad.Parse(NavRadio(text));

        Assert.Equal(new Bbs757VorTuning(mhz, mode, ident), page!.Left);
    }

    [Fact]
    public void Parses_the_exact_rows_session_8_read()
    {
        // 24 columns each, as the page service hands them over.
        Assert.Equal(new Bbs757VorTuning(116.90, 'A', "GSU"),
            Bbs757NavRad.Parse(Screen("        NAV RADIO       ", "116.90AGSU        ------"))!.Left);
        Assert.Equal(new Bbs757VorTuning(113.90, 'R', null),
            Bbs757NavRad.Parse(Screen("        NAV RADIO       ", "113.90R           ------"))!.Left);
    }

    [Fact]
    public void Not_the_page_is_null()
    {
        Assert.True(Bbs757NavRad.IsNavRadio(NavRadio("116.90AGSU")));
        Assert.False(Bbs757NavRad.IsNavRadio(Legs()));
        Assert.Null(Bbs757NavRad.Parse(Legs()));
        Assert.Null(Bbs757NavRad.Parse(Bbs757CduFrame.Blank));
    }

    [Theory]
    [InlineData("GARBAGE")]   // neither a tuning nor dashes
    [InlineData("")]          // the row has not been drawn
    public void A_tuning_row_it_cannot_read_gives_no_page(string left)
    {
        // "Frequencies unavailable" is honest; "VOR L not tuned" for a row not read would not be.
        Assert.Null(Bbs757NavRad.Parse(NavRadio(left)));
        Assert.Null(Bbs757NavRad.Parse(NavRadio("116.90AGSU", "GARBAGE")));
    }

    // ---- The sentences ------------------------------------------------------------------------

    [Theory]
    [InlineData(116.90, 'A', "GSU", "VOR L 116.90 auto, GSU")]
    [InlineData(113.90, 'R', null, "VOR L 113.90 remote")]
    [InlineData(114.20, 'M', "ABC", "VOR L 114.20 manual, ABC")]
    [InlineData(108.00, null, null, "VOR L 108.00")]
    public void Tuning_phrases(double mhz, char? mode, string? ident, string said)
    {
        Assert.Equal(said, Bbs757NavRad.TuningPhrase("VOR L", new Bbs757VorTuning(mhz, mode, ident)));
    }

    [Fact]
    public void An_untuned_side_says_so()
    {
        Assert.Equal("VOR R not tuned", Bbs757NavRad.TuningPhrase("VOR R", NotTuned));
    }

    [Fact]
    public void Readout_sentences()
    {
        var page = new Bbs757NavRadPage(new Bbs757VorTuning(116.90, 'A', "GSU"), NotTuned);

        // With the page: each VOR's tuning and course, then the ILS course.
        Assert.Equal("VOR L 116.90 auto, GSU, course 095. VOR R not tuned, course 000. ILS course 095.",
            Bbs757NavRad.Readout(page, 95, 0, 95));
        // Without it (the first officer's CDU busy or unreachable): the courses, and why there is no more.
        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. Frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, 0, 95));
        // A course that cannot be read says so, in both forms.
        Assert.Equal("VOR L 116.90 auto, GSU, course unavailable. VOR R not tuned, course 000. ILS course 095.",
            Bbs757NavRad.Readout(page, null, 0, 95));
        Assert.Equal("VOR L course 095, VOR R course unavailable, ILS course unavailable. Frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, null, null));
        Assert.Equal("VOR L course unavailable, VOR R course unavailable, ILS course unavailable. Frequencies unavailable.",
            Bbs757NavRad.Readout(null, null, null, null));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_page_and_the_sentences_do_not_depend_on_the_pcs_culture(string culture)
    {
        // CI runs under en-US: under a comma-decimal culture "116.90" must still be 116.9 (not 11690)
        // and be spoken with a dot.
        InCulture(culture, () =>
        {
            var page = Bbs757NavRad.Parse(NavRadio("116.90AGSU"));

            Assert.Equal(116.90, page!.Left.Mhz);
            Assert.Equal("VOR L 116.90 auto, GSU, course 095. VOR R not tuned, course 000. ILS course 095.",
                Bbs757NavRad.Readout(page, 95, 0, 95));
        });
    }

    [Theory]
    [InlineData(95.0, 95)]
    [InlineData(0.0, 0)]
    [InlineData(359.4, 359)]
    [InlineData(94.6, 95)]
    public void A_course_is_whole_degrees(double raw, int degrees)
    {
        Assert.Equal(degrees, Bbs757NavRad.Course(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    [InlineData(361.0)]
    public void A_course_that_is_not_one_is_unavailable(double? raw)
    {
        Assert.Null(Bbs757NavRad.Course(raw));
    }

    [Fact]
    public void Courses_are_three_digits()
    {
        Assert.Equal("VOR L course 005, VOR R course 000, ILS course 360. Frequencies unavailable.",
            Bbs757NavRad.Readout(null, 5, 0, 360));
    }

    // ---- The variables ------------------------------------------------------------------------

    [Fact]
    public void The_course_variables_are_read_on_request_and_have_no_Ctrl_M_row()
    {
        var vars = new BBS757Definition().GetVariables();
        var expected = new[]
        {
            (Bbs757NavRad.VorLCourseKey, "BBS_VOR_L_COURSE", "VOR L Course"),
            (Bbs757NavRad.VorRCourseKey, "BBS_VOR_R_COURSE", "VOR R Course"),
            (Bbs757NavRad.IlsCourseKey, "BBS_ILS_COURSE", "ILS Course"),
        };

        foreach (var (key, name, label) in expected)
        {
            var v = vars[key];
            Assert.Equal(name, v.Name);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.True(v.ExcludeFromMonitorManager);   // read on request, never announced: a Ctrl+M row would mute nothing
        }
    }

    // ---- Output N -------------------------------------------------------------------------------

    private static McpIo Courses(double? vorL, double? vorR, double? ils) => new()
    {
        Read = (key, _) => Task.FromResult(key switch
        {
            Bbs757NavRad.VorLCourseKey => vorL,
            Bbs757NavRad.VorRCourseKey => vorR,
            Bbs757NavRad.IlsCourseKey => ils,
            _ => throw new InvalidOperationException($"N read {key}, which is not a BlueBird nav course"),
        }),
        Write = _ => throw new InvalidOperationException("N writes nothing"),
        Delay = (_, _) => Task.CompletedTask,
    };

    /// <summary>A definition whose courses read as given and whose tablet client is gone: the CDU service answers null.</summary>
    private static BBS757Definition DefWithNoCdu(McpIo io) => new()
    {
        NestRunning = () => true,
        ReadoutIo = _ => io,
        TabletClientFactory = () =>
        {
            var client = new CoherentBbs757TabletClient(() => "");
            client.Dispose();
            return client;
        },
    };

    private static async Task<bool> WaitUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(10);
        }
        return true;
    }

    [Fact]
    public async Task N_is_handled_by_the_757()
    {
        // Until now N fell through to MainForm's readout of the stock NAV radios, which BlueBird does
        // not use. With the CDU service answering null, the action is still the 757's, and it still
        // speaks the courses.
        var def = DefWithNoCdu(Courses(95, 0, 95));
        var speech = new SpeechCapture();

        bool handled = def.HandleHotkeyAction(HotkeyAction.ReadNavRadioInfo, new SimConnectManager(IntPtr.Zero), speech, null!, null!);

        Assert.True(handled);
        Assert.True(await WaitUntil(() => speech.Interrupts.Count > 0), "N said nothing");
        Assert.Equal(new[] { "VOR L course 095, VOR R course 000, ILS course 095. Frequencies unavailable." }, speech.Interrupts.ToArray());
    }

    [Fact]
    public async Task N_reads_the_page_the_first_officers_cdu_shows()
    {
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 116.90 auto, GSU, course 095. VOR R not tuned, course 000. ILS course 095." }, speech.Interrupts);
        Assert.Empty(cdu.Presses);   // already on the page: no key
    }

    [Fact]
    public async Task N_brings_up_the_page_on_the_first_officers_cdu_only()
    {
        var cdu = new FakeCdu { Screen = Legs(), ScreenAfterNavRadKey = NavRadio("113.90R") };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 113.90 remote, course 095. VOR R not tuned, course 000. ILS course 095." }, speech.Interrupts);
        Assert.Equal(new[] { (Fo, Bbs757NavRad.PageKey, 250) }, cdu.Presses);   // R52: the captain's CDU is never touched
    }

    [Fact]
    public async Task N_with_the_cdu_busy_still_reads_the_courses()
    {
        // The pilot is typing in the CDU window: keys are queued, so the page service presses nothing.
        var cdu = new FakeCdu { Screen = Legs(), ScreenAfterNavRadKey = NavRadio("116.90AGSU"), PendingKeyCount = 3 };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L course 095, VOR R course 000, ILS course 095. Frequencies unavailable." }, speech.Interrupts);
        Assert.Empty(cdu.Presses);
    }

    [Fact]
    public async Task N_with_the_tablet_view_unreachable_still_reads_the_courses()
    {
        var cdu = new FakeCdu { Screen = null, IsReadable = false };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L course 095, VOR R course 000, ILS course 095. Frequencies unavailable." }, speech.Interrupts);
    }

    [Fact]
    public async Task N_with_a_course_that_cannot_be_read_says_so()
    {
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(null, 0, double.NaN) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 116.90 auto, GSU, course unavailable. VOR R not tuned, course 000. ILS course unavailable." }, speech.Interrupts);
    }

    [Fact]
    public async Task N_that_fails_still_says_something()
    {
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            ReadoutIo = _ => new McpIo
            {
                Read = (_, _) => throw new InvalidOperationException("no link"),
                Write = _ => { },
                Delay = (_, _) => Task.CompletedTask,
            },
        };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(new FakeCdu()));

        Assert.Equal(new[] { "VOR L course unavailable, VOR R course unavailable, ILS course unavailable. Frequencies unavailable." },
            speech.Interrupts);
    }

    [Fact]
    public async Task N_after_switching_away_says_nothing()
    {
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();
        def.OnSwitchedAway();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(new FakeCdu { Screen = NavRadio("116.90AGSU") }));

        Assert.Empty(speech.Interrupts);
    }

    // ---- A fake first officer's CDU ---------------------------------------------------------------

    private static Bbs757CduPages Pages(FakeCdu cdu) => new(cdu, (_, _) => Task.CompletedTask);

    private sealed class FakeCdu : IBbs757CduLink
    {
        public bool IsReadable { get; set; } = true;
        public int PendingKeyCount { get; set; }

        /// <summary>What the CDU shows now; null: no frame.</summary>
        public Bbs757CduScreen? Screen { get; set; }

        /// <summary>What it shows once the NAV RAD key was pressed; null: the key changes nothing.</summary>
        public Bbs757CduScreen? ScreenAfterNavRadKey { get; set; }

        public List<(int Cdu, string Key, int HoldMs)> Presses { get; } = new();

        public void PressKey(int cdu, string key, int holdMs)
        {
            Presses.Add((cdu, key, holdMs));
            if (key == Bbs757CduKeys.NavRad && ScreenAfterNavRadKey != null) Screen = ScreenAfterNavRadKey;
        }

        public Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct) => Task.FromResult(Screen);
    }
}
