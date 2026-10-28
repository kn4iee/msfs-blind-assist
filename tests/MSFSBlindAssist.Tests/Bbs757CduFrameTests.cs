using System.Text;
using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757 CDU screen arrives as MobiFlight WinCtrl JSON on the Comm Bus
/// (docs/b757/evidence/cdu.md): <c>{"Target":"Display","Data":[336 cells]}</c>, 14 rows of 24
/// cells, a cell <c>[]</c> or <c>[char, color, size, inverse]</c>. Whatever arrives, the window
/// gets exactly 14 lines of 24 characters and never an exception.
/// </summary>
public class Bbs757CduFrameTests
{
    /// <summary>The IDENT page at power-up (cdu.md), with its blank row 5 restored and the scratchpad message on row 13.</summary>
    private static readonly string[] IdentLines =
    {
        "          IDENT",
        " MODEL           ENGINES",
        "757-200           PW2037",
        " NAV DATA         ACTIVE",
        "B572503001 MAR20APR16/25",
        "",
        "           FEB20MAR19/25",
        " OP PROGRAM      CO DATA",
        "4618BBSR02C07     BB1001",
        " OPC             DRAG/FF",
        "8912RA422WL4   +0.0/+2.0",
        "------------------------",
        "<INDEX         POS INIT>",
        "NAV DATA OUT OF DATE",
    };

    /// <summary>Builds the wire format from text lines: a space is <c>[]</c>, anything else <c>[c,"g",size,0]</c>.</summary>
    private static string FrameJson(IReadOnlyList<string> lines, int cellCount = 336)
    {
        var cells = new List<object[]>();
        for (int row = 0; row < 14; row++)
        {
            string line = row < lines.Count ? lines[row].PadRight(24) : new string(' ', 24);
            int size = row % 2 == 1 ? 1 : 0;   // label rows small, as the aircraft draws them
            foreach (char c in line)
                cells.Add(c == ' ' ? Array.Empty<object>() : new object[] { c.ToString(), "g", size, 0 });
        }
        return JsonSerializer.Serialize(new { Target = "Display", Data = cells.Take(cellCount).ToList() });
    }

    private static void AssertGrid(Bbs757CduScreen screen)
    {
        Assert.Equal(14, screen.Lines.Count);
        Assert.All(screen.Lines, line => Assert.Equal(24, line.Length));
    }

    private static void AssertBlank(Bbs757CduScreen screen)
    {
        AssertGrid(screen);
        Assert.All(screen.Lines, line => Assert.Equal(new string(' ', 24), line));
    }

    [Fact]
    public void Ident_frame_reads_as_its_14_lines()
    {
        var screen = Bbs757CduFrame.Parse(FrameJson(IdentLines));

        AssertGrid(screen);
        Assert.Equal("          IDENT         ", screen.Lines[0]);
        Assert.Equal("757-200           PW2037", screen.Lines[2]);
        Assert.Equal("<INDEX         POS INIT>", screen.Lines[12]);
        Assert.StartsWith("NAV DATA OUT OF DATE", screen.Lines[13]);
        Assert.Equal("IDENT", screen.Title);
        Assert.Equal("NAV DATA OUT OF DATE", screen.Scratchpad);
    }

    [Fact]
    public void A_100_cell_frame_pads_to_14_by_24()
    {
        var screen = Bbs757CduFrame.Parse(FrameJson(IdentLines, cellCount: 100));

        AssertGrid(screen);
        Assert.Equal("          IDENT         ", screen.Lines[0]);
        Assert.Equal(" NAV DATA         ACTIVE", screen.Lines[3]);
        Assert.Equal("B572" + new string(' ', 20), screen.Lines[4]);   // cells 96-99, then nothing
        for (int row = 5; row < 14; row++)
            Assert.Equal(new string(' ', 24), screen.Lines[row]);
    }

    [Fact]
    public void Cells_past_336_are_ignored()
    {
        var cells = Enumerable.Repeat(new object[] { "A", "g", 0, 0 }, 400).ToList();
        var screen = Bbs757CduFrame.Parse(JsonSerializer.Serialize(new { Target = "Display", Data = cells }));

        AssertGrid(screen);
        Assert.All(screen.Lines, line => Assert.Equal(new string('A', 24), line));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"Target\":\"Display\"}")]
    [InlineData("{\"Target\":\"Display\",\"Data\":5}")]
    [InlineData("{\"Target\":\"Display\",\"Data\":[]}")]
    [InlineData("{\"Target\":\"Display\",\"Data\":[[\"A\",\"g\",0,0]")]   // truncated
    public void Missing_or_bad_json_gives_14_blank_lines(string? json)
    {
        AssertBlank(Bbs757CduFrame.Parse(json));
    }

    [Fact]
    public void A_payload_for_another_target_is_not_a_screen()
    {
        string json = FrameJson(IdentLines).Replace("\"Display\"", "\"Font\"");

        AssertBlank(Bbs757CduFrame.Parse(json));
    }

    [Fact]
    public void The_box_glyph_reads_as_an_underscore()
    {
        var lines = new[] { "", "", "\u2610\u2610\u2610\u2610" };

        var screen = Bbs757CduFrame.Parse(FrameJson(lines));

        Assert.Equal("____" + new string(' ', 20), screen.Lines[2]);
    }

    [Fact]
    public void Odd_cells_read_as_blanks_and_never_throw()
    {
        // A number, null, an empty string, a control character and a two-character string.
        string json = "{\"Target\":\"Display\",\"Data\":[[5,\"g\",0,0],[null],[\"\"],[\"\\u0007\"],[\"AB\",\"g\",0,0],\"x\",null,{}]}";

        var screen = Bbs757CduFrame.Parse(json);

        AssertGrid(screen);
        Assert.Equal("    A" + new string(' ', 19), screen.Lines[0]);
    }

    [Fact]
    public void The_live_legs_frame_reads_with_its_degree_signs()
    {
        // Captured from CDU 1 on 2026-10-25 (ACT RTE 1 LEGS, KSEA to KDEN).
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-cdu-legs.json"), Encoding.UTF8);

        var screen = Bbs757CduFrame.Parse(json);

        AssertGrid(screen);
        Assert.Equal("  ACT RTE 1 LEGS     1/2", screen.Lines[0]);
        Assert.Equal(" 307°                   ", screen.Lines[1]);
        Assert.Equal("SEA           ---/------", screen.Lines[2]);
        Assert.Equal("<RTE 2 LEGS    RTE DATA>", screen.Lines[12]);
        Assert.Equal("FLT NUMBER UPLINK", screen.Scratchpad);
    }

    [Fact]
    public void Display_lines_follow_the_cdu_window_layout()
    {
        var lines = Bbs757CduFrame.ToDisplayLines(Bbs757CduFrame.Parse(FrameJson(IdentLines)));

        Assert.Equal(new[]
        {
            "Title: IDENT",
            "    MODEL           ENGINES",
            "1: 757-200           PW2037",
            "    NAV DATA         ACTIVE",
            "2: B572503001 MAR20APR16/25",
            "3:            FEB20MAR19/25",   // a blank label row adds no line
            "    OP PROGRAM      CO DATA",
            "4: 4618BBSR02C07     BB1001",
            "    OPC             DRAG/FF",
            "5: 8912RA422WL4   +0.0/+2.0",
            // the 24 dashes above line 6 are left out (R71)
            "6: <INDEX         POS INIT>",
            "Scratchpad: NAV DATA OUT OF DATE",
        }, lines);
    }

    [Fact]
    public void A_blank_screen_says_which_cdu_it_is()
    {
        var screen = Bbs757CduFrame.Parse(null);

        Assert.Equal(new[] { "Captain CDU screen blank" }, Bbs757CduFrame.ToDisplayLines(screen));
        Assert.Equal(new[] { "First officer CDU screen blank" }, Bbs757CduFrame.ToDisplayLines(screen, 2));
    }
}
