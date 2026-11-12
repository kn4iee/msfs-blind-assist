using System.Globalization;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 09 Task 3 (question 13, Robin 2026-10-31): the F-PLN reads as the A380 window's fixed 40-cell grid, so a braille
/// display shows a whole row and the columns line up: ident 8, airway 6, track 5, distance 4, speed 5, altitude 7, time 5, each
/// padded with spaces and a value that fills its cell followed by one space (never cut), literal screen values (a ditto, a
/// constraint's star), the descent FPA as a suffix, one column-header line above the first waypoint; then the row's colour, and
/// after it what the grid cannot hold: the ident's symbol, and a cell drawn in another colour than its row.</summary>
public class A350MfdFplnGridTests
{
    private static IReadOnlyList<MfdLine> Lines(string fixture) => A350MfdPage.Read(A350Fixtures.Capture(fixture + ".json"), 0);

    private static List<string> Rows(string fixture) => Lines(fixture).Where(l => l.Kind == "waypoint").Select(l => l.Spoken).ToList();

    [Fact]
    public void The_flight_plan_reads_as_the_a380s_grid()
    {
        Assert.Equal(new[]
        {
            "VCBI                               00:00, green",
            "BASUR, green",
            "TVM     P570  295° 80, green",
            "DOTAT   P570  306° 198, green",
            "POMAN   P570  306° 158, green",
            "OLNIK   P570  307° 55, green",
            "OTABI   P570  306° 57, green",
            "NIVUD   P570  306° 105, green",
        }, Rows("mfd_fpln_route"));
        Assert.Equal(new[]
        {
            "VCBI22                 142  30     00:00, green",
            "DER22   BASU1D 238° 0   143  140    00:00, white, overfly",
            "500     C220° 221° 0   146  500    00:00, green",
            "BI611   BASU1D 279° 6   210  3290   00:02, green, right turn",
            "BI453   BASU1D 309° 6   250  5000   00:04, green",
            "BI613   BASU1D 295° 4   \"    *5000  00:05, green, ALT constraint met",
            "(SPDLIM)            10  250  10000  00:07, green",
            "BI614   BASU1D 295° 32  301  FL188  00:13, green",
            "BASUR   BASU1D 295° 52  \"    FL270  00:20, green",
        }, Rows("mfd_fpln_basu1d"));
    }

    // The TMPY plan (live, BASU1D picked): the rows are yellow; the TO waypoint's ident is white and the 500 ft constraint's
    // altitude magenta, each said after the row's colour.
    [Fact]
    public void The_grid_row_ends_with_its_color()
    {
        var rows = Rows("mfd_fpln_tmpy_sid");
        Assert.Contains("DER22   BASU1D 218° 1, yellow, overfly, ident white", rows);
        Assert.Contains("500     C220° 221° 0        500, yellow, ALT magenta", rows);
        Assert.All(rows, row => Assert.Matches(@", yellow(, |$)", row));
    }

    [Fact]
    public void The_header_line_stands_above_the_first_waypoint()
    {
        var lines = Lines("mfd_fpln_basu1d").ToList();
        int first = lines.FindIndex(l => l.Kind == "waypoint");
        Assert.Equal("FROM          TRK  NM  SPD  ALT    TIME, white", lines[first - 1].Spoken);
        Assert.Equal("text", lines[first - 1].Kind);
        Assert.Single(lines, l => l.Spoken.Contains("TRK  NM", StringComparison.Ordinal));
        // Scrolled down the A350 draws no FROM over the first column: the header's ident cell is blank.
        var scrolled = Lines("mfd_fpln_ils12l_final").ToList();
        Assert.Equal("              TRK  NM  SPD  ALT    TIME, white", scrolled[scrolled.FindIndex(l => l.Kind == "waypoint") - 1].Spoken);
    }

    [Fact]
    public void A_constraint_star_and_a_ditto_read_as_drawn()
    {
        Assert.Contains("BI613   BASU1D 295° 4   \"    *5000  00:05, green, ALT constraint met", Rows("mfd_fpln_basu1d"));
        var final = Rows("mfd_fpln_ils12l_final");
        Assert.Contains("REREK   REREK 118° 3   *185 *3370  01:51, green, SPD constraint met, ALT constraint met", final);
        Assert.Contains("DB612   C119° 116° 1   154  *2000  01:55 FPA -3.0, green, ALT constraint met, FPA white", final);
    }

    [Fact]
    public void A_discontinuity_reads_as_one_short_line()
    {
        var lines = Lines("mfd_fpln_route");
        var marker = Assert.Single(lines, l => l.Spoken.Contains("DISCONTINUITY", StringComparison.Ordinal));
        Assert.Equal("DISCONTINUITY, white", marker.Spoken);
    }

    [Theory]
    [InlineData("mfd_fpln_basu1d")]
    [InlineData("mfd_fpln_ils12l_final")]
    [InlineData("mfd_fpln_tmpy_sid")]
    public void The_grid_reads_the_same_under_de_DE(string fixture)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var english = Lines(fixture).Select(l => l.Spoken).ToList();
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(english, Lines(fixture).Select(l => l.Spoken).ToList());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // Review focus: a copy read across a frame tears a text (a control character, A350MfdPage.Unreadable); its row reads
    // unreadable after its ident, never a grid with the word in one column and the rest pushed out of line.
    [Fact]
    public void A_torn_grid_line_reads_unreadable()
    {
        var capture = A350Fixtures.Capture("mfd_fpln_basu1d.json");
        A350DrawCapture Tear(string text) => capture with
        {
            Texts = capture.Texts.Select(t => t.Text == text ? t with { Text = text[..2] + "\u0001" + text[2..] } : t).ToList(),
        };
        Assert.Contains("BI613   unreadable, green", A350MfdPage.Read(Tear("00:05"), 0).Select(l => l.Spoken));
        Assert.Contains("unreadable, green", A350MfdPage.Read(Tear("BI613"), 0).Select(l => l.Spoken));
    }

    // The A380 window's RoleWord: an F-PLN row the MFD makes clickable (Enter opens its revision menu) reads "button".
    [Fact]
    public void A_waypoint_row_reads_button_in_the_window()
    {
        var shown = A350MfdWindow.PageLines(A350Fixtures.Capture("mfd_fpln_basu1d.json"), 0, dark: false).Shown;
        Assert.Contains("BI453   BASU1D 309° 6   250  5000   00:04, button", shown);
        Assert.Contains("FROM          TRK  NM  SPD  ALT    TIME", shown);
    }
}
