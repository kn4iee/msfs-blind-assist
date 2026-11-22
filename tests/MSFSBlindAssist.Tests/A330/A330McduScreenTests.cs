using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330McduScreenTests
{
    private static string FixturesDir() => Path.Combine(
        ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A330", "Fixtures");

    private static A330Frame Fixture(string name) =>
        A330ReaderProtocol.ParseFrame(File.ReadAllText(Path.Combine(FixturesDir(), name)), 0)!;

    private static A330Text T(int order, float x, float baseline, string text, string font = "FMGSfont_A") =>
        new(order, x, baseline, text, font, 29.5f, new A330Rgb(1, 1, 1), new float[] { x });

    private static A330Frame Frame(params A330Text[] texts) =>
        new(A330Gauge.McduCaptain, texts, true, 0, 0);

    private static float X(int col) => A330McduGeometry.Left + col * A330McduGeometry.ColumnPitch;
    private static float Y(int row) => A330McduGeometry.Top + row * A330McduGeometry.RowPitch;

    [Fact]
    public void The_INIT_fixture_fills_the_grid_as_the_cockpit_shows_it()
    {
        var screen = A330McduScreen.From(Fixture("p03_init.json"));
        Assert.NotNull(screen);
        string[] expected =
        {
            "          INIT        ab",
            " CO RTE          FROM/TO",
            "oooooooooo     oooo/oooo",
            "ALTN/CO RTE",
            "----/----------",
            "FLT NBR",
            "oooooooo       IRS INIT>",
            "",
            "",
            "COST INDEX",
            "20                 WIND>",
            "CRZ FL/TEMP        TROPO",
            "---- /---d         36090",
            "NAV ACCUR DOWNGRADED",
        };
        for (int r = 0; r < expected.Length; r++) Assert.Equal(expected[r], screen!.Text(r));
        Assert.True(screen!.HiddenRowsLeftOut);
    }

    [Fact]
    public void Labels_are_small_and_data_large()
    {
        var screen = A330McduScreen.From(Fixture("p03_init.json"))!;
        Assert.True(screen[1, 1]!.Value.Small);   // "CO RTE" label
        Assert.False(screen[10, 0]!.Value.Small); // "20" cost index
        Assert.Null(screen[7, 0]);
    }

    [Fact]
    public void A_later_text_wins_a_shared_cell()
    {
        var a = T(1, X(2), Y(4), "ABCD");
        var b = T(5, X(4), Y(4), "xy");
        foreach (var texts in new[] { new[] { a, b }, new[] { b, a } })
        {
            var screen = A330McduScreen.From(Frame(texts))!;
            Assert.Equal("  ABxy", screen.Text(4));
        }
    }

    [Fact]
    public void A_space_never_clears_a_cell_an_earlier_text_filled()
    {
        var screen = A330McduScreen.From(Frame(T(1, X(0), Y(0), "ABC"), T(2, X(0), Y(0), "x z")))!;
        Assert.Equal("xBz", screen.Text(0));
    }

    [Fact]
    public void A_centred_label_between_two_columns_takes_the_nearer_one()
    {
        var screen = A330McduScreen.From(Fixture("p03_departures.json"))!;
        Assert.Equal("   AVAILABLE RUNWAYS", screen.Text(3));
    }

    /// <summary>A text whose baseline sits <paramref name="row"/> rows down (a fraction: half a row off and the like).</summary>
    private static A330Text At(int order, int col, double row, string text) =>
        T(order, X(col), (float)(A330McduGeometry.Top + row * A330McduGeometry.RowPitch), text);

    [Fact]
    public void A_page_counter_half_a_row_under_the_title_goes_on_the_title_row()
    {
        // P/N XLOAD draws its "1/7" 0.54 of a row under the title: on the title line, at its own column, as the other
        // MCDU windows show a page counter.
        var screen = A330McduScreen.From(Fixture("p03_xload.json"))!;
        Assert.Equal("       P/N XLOAD     1/7", screen.Text(0));
        Assert.Equal("", screen.Text(1));

        // Every other text keeps the nearest row: a word half a row off, a counter 0.76 of a row down, a counter whose
        // cells on the title row are taken.
        var title = At(1, 7, 0, "P/N XLOAD");
        Assert.Equal("                     ABC", A330McduScreen.From(Frame(title, At(2, 21, 0.54, "ABC")))!.Text(1));
        Assert.Equal("                     1/7", A330McduScreen.From(Frame(title, At(2, 21, 0.76, "1/7")))!.Text(1));
        var wide = At(1, 0, 0, "      P/N XLOAD STATUS X");
        var taken = A330McduScreen.From(Frame(wide, At(2, 21, 0.54, "1/7")))!;
        Assert.Equal("      P/N XLOAD STATUS X", taken.Text(0));
        Assert.Equal("                     1/7", taken.Text(1));
        Assert.Equal("       P/N XLOAD   12/14", A330McduScreen.From(Frame(title, At(2, 19, 0.7, "12/14")))!.Text(0));
        Assert.Equal(0, A330McduGeometry.Nearest(0.5));
        Assert.Equal(1, A330McduGeometry.Nearest(0.51));
    }

    [Fact]
    public void A_text_outside_the_screen_refuses_it()
    {
        Assert.Null(A330McduScreen.From(Frame(T(1, X(24), Y(0), "A"))));
        Assert.Null(A330McduScreen.From(Frame(T(1, X(0), Y(14), "A"))));
        Assert.Null(A330McduScreen.From(Frame(T(1, X(0) - 20, Y(0), "A"))));
        Assert.Null(A330McduScreen.From(Frame(T(1, X(0), Y(0) - 20, "A"))));
        // Characters past column 23 are dropped, an empty text places nothing.
        var screen = A330McduScreen.From(Frame(T(1, X(22), Y(0), "ABCD"), T(2, 9999, 9999, "")))!;
        Assert.Equal("                      AB", screen.Text(0));
    }

    [Fact]
    public void No_fixture_page_has_two_texts_claiming_one_cell_with_different_characters()
    {
        int checkedPages = 0;
        foreach (var path in Directory.GetFiles(FixturesDir(), "p03_*.json"))
        {
            var frame = A330ReaderProtocol.ParseFrame(File.ReadAllText(path), 0)!;
            if (frame.Gauge != A330Gauge.McduCaptain && frame.Gauge != A330Gauge.McduFirstOfficer) continue;
            checkedPages++;
            var claimed = new Dictionary<(int, int), char>();
            foreach (var t in frame.Texts)
            {
                int row = A330McduGeometry.RowOf(t.Baseline), col = A330McduGeometry.ColumnOf(t.X);
                for (int k = 0; k < t.Text.Length; k++)
                {
                    if (t.Text[k] == ' ') continue;
                    var key = (row, col + k);
                    if (claimed.TryGetValue(key, out char prev))
                        Assert.True(prev == t.Text[k], $"{Path.GetFileName(path)}: cell {key} claimed as '{prev}' and '{t.Text[k]}'");
                    claimed[key] = t.Text[k];
                }
            }
        }
        Assert.True(checkedPages >= 5);
    }

    [Fact]
    public void An_incomplete_frame_gives_no_screen()
    {
        var init = Fixture("p03_init.json");
        Assert.Null(A330McduScreen.From(init with { Complete = false, Missing = 3 }));
        Assert.Null(A330McduScreen.From(init with { Drawn = false }));
        Assert.Null(A330McduScreen.From(init with { Gauge = A330Gauge.Ewd }));
    }

    [Fact]
    public void The_first_officers_frame_uses_the_same_geometry()
    {
        var init = Fixture("p03_init.json");
        var captain = A330McduScreen.From(init)!;
        var fo = A330McduScreen.From(init with { Gauge = A330Gauge.McduFirstOfficer })!;
        for (int r = 0; r < A330McduGeometry.Rows; r++) Assert.Equal(captain.Text(r), fo.Text(r));
    }

    [Fact]
    public void The_screen_copies_the_frames_hidden_rows_flag()
    {
        Assert.True(A330McduScreen.From(Frame(T(1, X(0), Y(0), "A")) with { HiddenRowsLeftOut = true })!.HiddenRowsLeftOut);
        Assert.False(A330McduScreen.From(Frame(T(1, X(0), Y(0), "A")))!.HiddenRowsLeftOut);
    }
}
