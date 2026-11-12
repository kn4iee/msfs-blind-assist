using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 25 Task 3: the MFD window's list as the A380's MFD window keeps its own (`FBWA380MCDUForm.ApplyScreenMeta`,
/// `RefreshDisplay`): the page's title is said once as the page changes, never a row of the list; every other line is a row,
/// shown in the A380 window's words with no colour.</summary>
public class A350MfdWindowListTests
{
    private static readonly Regex ColourWord = new(@"\b(white|green|cyan|amber|magenta|yellow|grey|black|red|blue)\b", RegexOptions.Compiled);

    [Fact]
    public void The_title_is_no_row_and_every_other_line_is_one()
    {
        // (The TMPY F-PLN: its title bar holds TMPY on yellow. ACTIVE's open menu, the brief's page, covers its title bar.)
        var (lines, listed, shown) = A350MfdWindow.PageLines(A350Fixtures.Capture("mfd_fpln_tmpy_basu1d.json"), 0, dark: false);
        Assert.Contains(lines, l => l.Kind == "title");
        Assert.DoesNotContain(listed, l => l.Kind == "title");
        Assert.Equal(lines.Count - 1, listed.Count);
        Assert.Equal(listed.Count, shown.Count);
        Assert.Equal(listed.Select(A350MfdPage.Shown), shown);
        Assert.DoesNotContain(shown, l => ColourWord.IsMatch(l));
    }

    // Every captured MFD page, both sides, with a title or without one (Review Focus 4): one row per listed line, no title row, no colour.
    [Fact]
    public void Every_captured_page_lists_its_lines_without_the_title()
    {
        int untitled = 0, pages = 0;
        foreach (var file in Directory.GetFiles(A350Fixtures.Dir, "*.json").Where(f => Path.GetFileName(f).Contains("mfd", StringComparison.OrdinalIgnoreCase)))
            foreach (int side in new[] { 0, 1 })
            {
                var (lines, listed, shown) = A350MfdWindow.PageLines(A350Fixtures.Capture(Path.GetFileName(file)), side, dark: false);
                if (lines.Count == 0) continue;
                pages++;
                if (!lines.Any(l => l.Kind == "title")) untitled++;
                Assert.DoesNotContain(listed, l => l.Kind == "title");
                Assert.Equal(lines.Count(l => l.Kind != "title"), listed.Count);
                Assert.Equal(listed.Count, shown.Count);
                Assert.DoesNotContain(shown, l => ColourWord.IsMatch(l));
            }
        Assert.True(pages >= 100, $"only {pages} pages read");
        Assert.True(untitled > 0, "no page without a title: the no-title case is untested");
    }

    private static readonly IReadOnlyList<MfdLine> Titled = new[]
    {
        new MfdLine("text", "Flight number: MSFSBA1, white", 0, 0),
        new MfdLine("title", "ACTIVE/F-PLN", 0, 0),
        new MfdLine("field", "CI: required, amber", 0, 0),
        new MfdLine("button", "INSERT * TMPY button, amber", 0, 0),
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void A_line_after_the_title_is_one_row_up(int line, int row) => Assert.Equal(row, A350MfdWindow.ListedIndex(Titled, line));

    [Fact]
    public void The_title_has_no_row() => Assert.Null(A350MfdWindow.ListedIndex(Titled, 1));

    [Fact]
    public void Without_a_title_a_line_keeps_its_place() =>
        Assert.Equal(2, A350MfdWindow.ListedIndex(Titled.Where(l => l.Kind != "title").ToList(), 2));

    [Fact]
    public void No_line_has_no_row() => Assert.Null(A350MfdWindow.ListedIndex(Titled, null));

    // Final review: a half-drawn page with its title alone said "The MFD is off: no power." under a title just announced.
    [Fact]
    public void A_page_with_only_its_title_is_no_unpowered_mfd()
    {
        var cap = A350Fixtures.Capture("mfd_init_empty.json");
        var titleOnly = cap with
        {
            Elements = Array.Empty<DrawnElement>(),
            Texts = cap.Texts.Where(t => t.X < 836 && t.Y >= 108 && t.Y < 150 && t.ColourName == "black").ToList(),
        };
        var (lines, listed, shown) = A350MfdWindow.PageLines(titleOnly, 0, dark: false);
        Assert.Equal("title", Assert.Single(lines).Kind);
        Assert.Empty(listed);
        Assert.Equal(new[] { A350MfdWindow.NoLines }, shown);
        Assert.Equal(new[] { "The MFD is off: no power." }, A350MfdWindow.PageLines(cap with { Texts = Array.Empty<DrawnText>(), Elements = Array.Empty<DrawnElement>() }, 0, dark: false).Shown);
    }

    [Fact]
    public void A_dark_or_absent_screen_lists_nothing_to_press()
    {
        var dark = A350MfdWindow.PageLines(A350Fixtures.Capture("mfd_init_empty.json"), 0, dark: true);
        Assert.Empty(dark.Listed);
        var none = A350MfdWindow.PageLines(null, 0, dark: false);
        Assert.Empty(none.Listed);
    }
}
