using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using MSFSBlindAssist.Forms.IniA330;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330McduLinesTests
{
    private static readonly string[] Fixtures = { "p03_init.json", "p03_menu.json", "p03_fpln.json", "p03_departures.json", "p03_xload.json" };

    private static string FixturesDir() => Path.Combine(
        ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A330", "Fixtures");

    private static A330Frame Fixture(string name) =>
        A330ReaderProtocol.ParseFrame(File.ReadAllText(Path.Combine(FixturesDir(), name)), 0)!;

    private static IReadOnlyList<A330McduLine> Lines(string name) => A330McduLines.From(A330McduScreen.From(Fixture(name))!);

    private static float X(int col) => A330McduGeometry.Left + col * A330McduGeometry.ColumnPitch;
    private static float Y(int row) => A330McduGeometry.Top + row * A330McduGeometry.RowPitch;

    private static A330Text T(int order, int col, int row, string text, float? grey, string font = "FMGSfont_A") =>
        new(order, X(col), Y(row), text, font, 29.5f, grey is { } g ? new A330Rgb(g, g, g) : new A330Rgb(1, 1, 1), new float[] { X(col) });

    // One row of in-memory screen (row 4) from (column, text, grey level or null for white).
    private static A330McduLine Row(params (int Col, string Text, float? Grey)[] parts)
    {
        var texts = parts.Select((p, i) => T(i + 1, p.Col, 4, p.Text, p.Grey)).ToList();
        var screen = A330McduScreen.From(new A330Frame(A330Gauge.McduCaptain, texts, true, 0, 0))!;
        return A330McduLines.From(screen)[4];
    }

    private static readonly string[] IniListing =
    {
        "Title: INIT        \U00002190\U00002192",
        "    CO RTE          FROM/TO",
        "1: ----------     ----/----",
        "   ALTN/CO RTE",
        "2: ----/----------",
        "   FLT NBR",
        "3: --------       IRS INIT>",
        "4:",
        "   COST INDEX",
        "5: 20                 WIND>",
        "   CRZ FL/TEMP        TROPO",
        "6: ---- /---\U000000B0         36090",
        "Scratchpad: NAV ACCUR DOWNGRADED",
    };

    [Fact]
    public void INIT_reads_as_title_labels_numbered_data_and_scratchpad()
    {
        Assert.Equal(IniListing, CduLineText.Listing(Lines("p03_init.json")));
    }

    [Fact]
    public void MCDU_MENU_marks_the_greyed_prompts_unavailable()
    {
        var listing = CduLineText.Listing(Lines("p03_menu.json"));
        Assert.Contains("                 SELECT (unavailable)".Insert(0, "   "), listing);
        Assert.Contains("1: <FM1           NAV B/UP>(unavailable)", listing);
        Assert.Contains("3: <AIDS (unavailable)", listing);
        Assert.Contains("4: <CFDS (unavailable)", listing);
        Assert.Contains("2: <ATSU", listing);
        Assert.DoesNotContain(listing, l => l.Contains("MCDU MENU") && l.Contains("unavailable"));
        Assert.StartsWith("Scratchpad: SELECT DE", listing[^1]);
    }

    [Fact]
    public void A_row_with_left_and_right_fields_keeps_both()
    {
        var listing = CduLineText.Listing(Lines("p03_init.json"));
        Assert.Contains("3: --------       IRS INIT>", listing);
        Assert.Contains("6: ---- /---\U000000B0         36090", listing);
    }

    [Fact]
    public void An_empty_scratchpad_reads_empty()
    {
        var screen = A330McduScreen.From(new A330Frame(A330Gauge.McduCaptain, new[] { T(1, 0, 0, "A", null) }, true, 0, 0))!;
        var listing = CduLineText.Listing(A330McduLines.From(screen));
        Assert.Equal("Scratchpad:", listing[^1]);
        Assert.Equal("1:", listing[1]);
        Assert.Equal("Title: A", listing[0]);   // the Fenix and FlyByWire windows' prefix

        // An empty title row reads "Title:", as an empty scratchpad reads "Scratchpad:".
        var untitled = A330McduScreen.From(new A330Frame(A330Gauge.McduCaptain, new[] { T(1, 0, 2, "A", null) }, true, 0, 0))!;
        Assert.Equal("Title:", CduLineText.Listing(A330McduLines.From(untitled))[0]);
    }

    [Fact]
    public void No_line_carries_a_colour_word()
    {
        string[] words = { "white", "grey", "gray", "green", "amber", "cyan", "magenta", "yellow", "red", "blue" };
        foreach (var name in Fixtures)
            foreach (var line in CduLineText.Listing(Lines(name)))
                foreach (var w in words) Assert.DoesNotMatch($@"\b{w}\b", line.ToLowerInvariant());
    }

    [Fact]
    public void The_listing_is_the_same_under_de_DE()
    {
        var de = TestCulture.Run("de-DE", () => CduLineText.Listing(Lines("p03_init.json")).ToArray());
        Assert.Equal(IniListing, de);
        var menuInvariant = CduLineText.Listing(Lines("p03_menu.json"));
        Assert.Equal(menuInvariant, TestCulture.Run("de-DE", () => CduLineText.Listing(Lines("p03_menu.json"))));
    }

    [Theory]
    [InlineData("p03_init.json")]
    [InlineData("p03_fpln.json")]
    public void A_right_hand_field_keeps_its_column_on_every_row(string fixture)
    {
        var screen = A330McduScreen.From(Fixture(fixture))!;
        var lines = A330McduLines.From(screen);
        var listing = CduLineText.Listing(lines);
        for (int row = 0; row < A330McduGeometry.Rows; row++)
        {
            int last = -1;
            for (int c = 0; c < A330McduGeometry.Columns; c++) if (screen[row, c] != null) last = c;
            Assert.Equal(last + 1, lines[row].Text.Length);
            Assert.Equal(screen.Text(row).Length, lines[row].Text.Length);
        }
        // the data rows in the listing carry the row's text after "n: ", column for column
        foreach (var line in listing.Where(l => l.Length > 2 && char.IsDigit(l[0]) && l[1] == ':' && l.Length > 3))
        {
            int k = line[0] - '0';
            Assert.Equal(lines[2 * k].Text, line[3..]);
        }
    }

    [Fact]
    public void Each_special_character_takes_one_cell()
    {
        var row = Row((0, "oda", null), (3, "bef", null), (6, "c\"", null));
        Assert.Equal("-\U000000B0\U00002190\U00002192\U00002191\U00002193\U00000394\"", row.Text);
        Assert.Equal(8, row.Text.Length);
    }

    [Fact]
    public void The_unavailable_marker_moves_no_column()
    {
        var plain = Row((0, "<TO DATA", null), (18, "PHASE>", null));
        var grey = Row((0, "<TO DATA", 0.3f), (18, "PHASE>", null));
        Assert.Equal(plain.Text.Length, CduLineText.ForWindow(grey).Length);
        Assert.Equal(plain.Text.IndexOf("PHASE>"), CduLineText.ForWindow(grey).IndexOf("PHASE>"));
        var marked = Row((0, "<POINT", 0.3f), (15, "FUNCTION>", null));
        Assert.Equal("<POINT (inop)  FUNCTION>", CduLineText.ForWindow(marked));
        Assert.Equal(15, CduLineText.ForWindow(marked).IndexOf("FUNCTION>"));
    }

    [Fact]
    public void The_measured_greyed_prompt_cases_read_as_the_cockpit_marks_them()
    {
        Assert.Equal("<FM1           NAV B/UP>(unavailable)",
            CduLineText.ForWindow(Row((0, "<FM1", null), (15, "NAV B/UP>", 0.3f))));
        Assert.Equal("<AIDS (unavailable)", CduLineText.ForWindow(Row((0, "<AIDS", 0.3f))));
        Assert.Equal("                 SELECT (unavailable)",
            CduLineText.ForWindow(Row((17, "SELECT", 0.3f))));
        Assert.Equal("<TO DATA (inop)   PHASE>",
            CduLineText.ForWindow(Row((0, "<TO DATA", 0.3f), (18, "PHASE>", null))));
        Assert.Equal("<POINT (inop)  FUNCTION>",
            CduLineText.ForWindow(Row((0, "<POINT", 0.3f), (15, "FUNCTION>", null))));
        Assert.Equal("<WX REQUEST      D-ATIS>(unavailable)",
            CduLineText.ForWindow(Row((0, "<WX REQUEST", 0.3f), (17, "D-ATIS>", 0.3f))));
        // a coloured (not grey) prompt and a pale grey are not marked
        Assert.Equal("<AIDS", CduLineText.ForWindow(Row((0, "<AIDS", null))));
        Assert.Equal("<AIDS", CduLineText.ForWindow(Row((0, "<AIDS", 0.6f))));
    }

    [Fact]
    public void No_row_with_its_prefix_passes_40_cells()
    {
        var all = new List<string>();
        foreach (var name in Fixtures)
        {
            var listing = CduLineText.Listing(Lines(name));
            Assert.StartsWith("Title: ", listing[0]);   // the title line with its prefix counts too
            all.AddRange(listing.Where(l => l.Length > 3 && !l.StartsWith("Scratchpad")));
        }
        foreach (var line in new[]
        {
            Row((0, "<FM1", null), (15, "NAV B/UP>", 0.3f)),
            Row((0, "<AIDS", 0.3f)), Row((17, "SELECT", 0.3f)),
            Row((0, "<TO DATA", 0.3f), (18, "PHASE>", null)),
            Row((0, "<POINT", 0.3f), (15, "FUNCTION>", null)),
            Row((0, "<WX REQUEST", 0.3f), (17, "D-ATIS>", 0.3f)),
        })
        {
            string w = CduLineText.ForWindow(line);
            Assert.True(w.Length <= 37, w);
            all.Add("1: " + w);
        }
        foreach (var l in all) Assert.True(l.Length <= 40, l);
    }

    [Fact]
    public void Without_colours_no_prompt_is_marked_unavailable()
    {
        var frame = Fixture("p03_menu.json");
        var plain = frame with { Texts = frame.Texts.Select(t => t with { Colour = null }).ToList() };
        var listing = CduLineText.Listing(A330McduLines.From(A330McduScreen.From(plain)!));
        Assert.DoesNotContain(listing, l => l.Contains("(unavailable)") || l.Contains("(inop)"));
        Assert.Contains("3: <AIDS", listing);
    }

    /// <summary>An in-memory page titled <paramref name="title"/> (at column 1, as the F-PLN draws " FROM").</summary>
    private static IReadOnlyList<A330McduLine> PageTitled(string title) => A330McduLines.From(A330McduScreen.From(new A330Frame(
        A330Gauge.McduCaptain, new[]
        {
            T(1, 1, 0, title, null),
            T(2, 8, 2, "0005  250/ 10000", null),     // line 1: no name, its time, speed and altitude
            T(3, 0, 4, "RIGGZ", null), T(4, 8, 4, "0004", null),   // line 2: named
            T(5, 7, 6, "X", null),                    // line 3: a character at column 7 only
            T(6, 8, 3, "BRG292", null),               // a label row
            T(7, 8, 12, "394", null),                 // line 6, the destination row
        }, true, 0, 0))!);

    [Fact]
    public void A_flight_plan_row_with_no_name_reads_unnamed()
    {
        var fpln = PageTitled("FROM");
        Assert.Equal("unnamed 0005  250/ 10000", fpln[2].Text);
        Assert.Equal(8, fpln[2].Text.IndexOf("0005", StringComparison.Ordinal));   // no column moves
        Assert.Contains("1: unnamed 0005  250/ 10000", CduLineText.Listing(fpln));
        Assert.Equal("RIGGZ   0004", fpln[4].Text);
        Assert.Equal("       X", fpln[6].Text);       // nothing from column 8 on: not a waypoint row
        Assert.Equal("", fpln[8].Text);               // an empty row stays empty
        Assert.Equal("        BRG292", fpln[3].Text); // a label row is never a waypoint
        Assert.Equal("        394", fpln[12].Text);   // the destination row is not one of the five
        Assert.Equal("unnamed 0005  250/ 10000", PageTitled("FROM TMPY")[2].Text);

        // Another page's row with an empty start is the page's own.
        Assert.Equal("        0005  250/ 10000", PageTitled("INIT")[2].Text);

        // A captured F-PLN with its first waypoint's name taken away.
        var frame = Fixture("p03_fpln1.json");
        var nameless = frame with { Texts = frame.Texts.Where(t => t.Text != "KATL26L").ToList() };
        var lines = A330McduLines.From(A330McduScreen.From(nameless)!);
        Assert.Equal("unnamed ----  ---/ -----", lines[2].Text);
        Assert.Equal(A330McduLines.From(A330McduScreen.From(frame)!)[2].Text.Length, lines[2].Text.Length);
    }
}
