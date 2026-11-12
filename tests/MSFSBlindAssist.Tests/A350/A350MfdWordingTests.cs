using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The MFD said as the A380's MFD window says its own (plan 25; the owner, 2026-11-12: "the colours would have to be removed
/// completely from the MFD"): a colour becomes the owner's word where it means something to do (amber on a field "required",
/// yellow "temporary", magenta "constraint") and nothing elsewhere. The examples are the reader's own lines from the 222
/// captured MFD pages.
/// </summary>
public class A350MfdWordingTests
{
    [Theory]
    // Amber on a field is an entry the FMS needs: "required", once.
    [InlineData("field", "CI: required, amber", "CI: required")]
    [InlineData("field", "ETA: none, amber", "ETA: none, required")]
    // Amber elsewhere (a key to act on, a message) says nothing more.
    [InlineData("button", "ACTIVATE * APPR button, amber", "ACTIVATE * APPR button")]
    [InlineData("scratchpad", "Scratchpad: CHECK DATABASE CYCLE, amber", "Scratchpad: CHECK DATABASE CYCLE")]
    [InlineData("text", "CHECK FMS MESSAGE, amber", "CHECK FMS MESSAGE")]
    // Yellow is the temporary flight plan, said once: never beside the word TMPY.
    [InlineData("field", "VIA: P570, yellow", "VIA: P570, temporary")]
    [InlineData("checkbox", "DIRECT check box, checked, yellow", "DIRECT check box, checked, temporary")]
    [InlineData("text", "FROM: VCBI, yellow", "FROM: VCBI, temporary")]
    [InlineData("title", "ACTIVE/F-PLN/AIRWAYS; TMPY, yellow", "ACTIVE/F-PLN/AIRWAYS; TMPY")]
    [InlineData("button", "TMPY F-PLN button, yellow", "TMPY F-PLN button")]
    [InlineData("waypoint", "VCBI22                             00:00, yellow", "VCBI22                             00:00, temporary")]
    // Magenta is a constraint.
    [InlineData("text", "UTC none, EFOB none T, magenta", "UTC none, EFOB none T, constraint")]
    [InlineData("waypoint", "IKONA         038° 15       2000, cyan, overfly, ALT magenta", "IKONA         038° 15       2000, overfly, ALT constraint")]
    // Every other colour, and a note naming one, says nothing; a constraint's own words stay.
    [InlineData("field", "FUEL PENALTY: +000.0 %, cyan", "FUEL PENALTY: +000.0 %")]
    [InlineData("field", "IDLE: +0.0, green", "IDLE: +0.0")]
    [InlineData("field", "FLT NBR: MSFSBA1, white", "FLT NBR: MSFSBA1")]
    [InlineData("field", "Notepad: HELLO WORLD, cyan, menu open", "Notepad: HELLO WORLD, menu open")]
    [InlineData("button", "DELETE ALL * button, grey", "DELETE ALL * button")]
    [InlineData("menu", "FMS 1 menu, cyan", "FMS 1 menu")]
    [InlineData("option", "ADS-C: OFF, selected, green", "ADS-C: OFF, selected")]
    [InlineData("text", "A350-900P / TRENT XWB-84, green", "A350-900P / TRENT XWB-84")]
    [InlineData("text", "LOCAL TIME none, LOCAL ETA AT DEST (white), green", "LOCAL TIME none, LOCAL ETA AT DEST")]
    [InlineData("waypoint", "DB612   C119° 116° 1   154  *2000  01:55 FPA -3.0, green, ALT constraint met, FPA white",
        "DB612   C119° 116° 1   154  *2000  01:55 FPA -3.0, ALT constraint met")]
    [InlineData("waypoint", "DER22   BASU1D 034° 1, yellow, overfly, ident white", "DER22   BASU1D 034° 1, temporary, overfly")]
    [InlineData("waypoint", "BI611   BASU1D 279° 6   210  3290   00:02, green, right turn", "BI611   BASU1D 279° 6   210  3290   00:02, right turn")]
    // A constraint's star in a colour other than magenta (met) or amber (missed): the constraint stays, its colour goes (final review).
    [InlineData("waypoint", "BI613   BASU1D 295° 4        *5000  00:05, green, ALT constraint cyan", "BI613   BASU1D 295° 4        *5000  00:05, ALT constraint")]
    [InlineData("waypoint", "BI613   BASU1D 295° 4   *210  5000  00:05, green, SPD constraint white", "BI613   BASU1D 295° 4   *210  5000  00:05, SPD constraint")]
    public void A_colour_becomes_the_owners_word_or_nothing(string kind, string spoken, string said) =>
        Assert.Equal(said, A350MfdWording.Speak(kind, spoken));

    // A label or a value holding a colour's name is the page's own text, never a colour the reader added.
    [Theory]
    [InlineData("field", "Green dot: 190, green", "Green dot: 190")]
    [InlineData("waypoint", "GREEN   C119° 116° 1, green", "GREEN   C119° 116° 1")]
    [InlineData("text", "AMBER ALERT", "AMBER ALERT")]
    public void A_colours_name_in_the_pages_own_text_stays(string kind, string spoken, string said) =>
        Assert.Equal(said, A350MfdWording.Speak(kind, spoken));

    [Fact]
    public void A_line_is_spoken_by_its_own_kind() =>
        Assert.Equal("ETA: none, required", A350MfdWording.Speak(new MfdLine("field", "ETA: none, amber", 0, 0)));

    // The A380 window says its footer message alone (FBWA380MCDUForm.ApplyScreenMeta).
    [Theory]
    [InlineData("Scratchpad: CHECK DATABASE CYCLE, amber", "CHECK DATABASE CYCLE")]
    [InlineData("Scratchpad: NAV ACCURACY UPGRADED, white", "NAV ACCURACY UPGRADED")]
    public void A_message_is_said_as_the_a380_says_its_footer(string pad, string said) =>
        Assert.Equal(said, A350MfdWording.Message(pad));

    [Theory]
    [InlineData("button", "DELETE ALL * button, grey", true)]
    [InlineData("menuitem", "DEPARTURE button, grey", true)]
    [InlineData("field", "LEVEL: FL required, grey", true)]
    [InlineData("checkbox", "AUTO check box, not checked, unavailable", true)]
    [InlineData("button", "ACTIVATE * APPR button, amber", false)]
    [InlineData("field", "CI: required, amber", false)]
    public void A_grey_control_is_dimmed(string kind, string spoken, bool dimmed) =>
        Assert.Equal(dimmed, A350MfdWording.IsDimmed(new MfdLine(kind, spoken, 0, 0)));

    // The status display's MFD entries read the page as the window says it, with no colour (plan 25).
    [Theory]
    [InlineData("Captain MFD", 0)]
    [InlineData("First Officer MFD", 1)]
    public void The_status_displays_mfd_read_says_no_colour(string panel, int side)
    {
        var entry = MSFSBlindAssist.Aircraft.IniBuildsA350Definition.DisplayPanels.Single(p => p.Panel == panel);
        var capture = A350Fixtures.Capture("mfd_fpln_tmpy_basu1d.json");
        var read = entry.Read(capture);
        Assert.Equal(A350MfdPage.Read(capture, side).Select(A350MfdWording.Speak), read);
        Assert.NotEmpty(read);
        Assert.DoesNotContain(read, l => ColourWord.IsMatch(l));
    }

    // A grey key, list item, opener or check box does nothing for a sighted pilot either: "Unavailable". A grey field is a colour the
    // reader infers, not a disabled flag: Enter and Delete still act on it and the FMS says if it refuses (final review: never block).
    [Theory]
    [InlineData("button", "DELETE ALL * button, grey", false, true)]
    [InlineData("menuitem", "DEPARTURE button, grey", false, true)]
    [InlineData("button", "TRANS button, grey", true, true)]
    [InlineData("checkbox", "AUTO check box, not checked, unavailable", false, true)]
    [InlineData("field", "LEVEL: FL required, grey", false, false)]
    [InlineData("field", "CI: 30, grey", false, false)]
    [InlineData("button", "ACTIVATE * APPR button, amber", false, false)]
    public void Only_a_dimmed_control_is_unavailable(string kind, string spoken, bool opener, bool unavailable) =>
        Assert.Equal(unavailable, A350MfdWording.IsUnavailable(new MfdLine(kind, spoken, 0, 0, Opener: opener)));

    private static readonly Regex ColourWord = new(@"\b(white|green|cyan|amber|magenta|yellow|grey|black|red|blue)\b", RegexOptions.Compiled);

    // Every captured MFD page, both sides: no line is said with a colour.
    [Fact]
    public void Every_captured_mfd_line_is_said_without_a_colour()
    {
        var files = Directory.GetFiles(A350Fixtures.Dir, "*.json")
            .Where(f => Path.GetFileName(f).Contains("mfd", StringComparison.OrdinalIgnoreCase)).ToList();
        int read = 0;
        var coloured = new List<string>();
        foreach (var file in files)
        {
            var capture = A350Fixtures.Capture(Path.GetFileName(file));
            foreach (int side in new[] { 0, 1 })
                foreach (var line in A350MfdPage.Read(capture, side))
                {
                    read++;
                    string said = A350MfdWording.Speak(line);
                    if (ColourWord.IsMatch(said)) coloured.Add($"{Path.GetFileName(file)} {side}: {said}");
                }
        }
        Assert.True(read >= 1000, $"only {read} lines read: the sweep proves nothing");
        Assert.Empty(coloured);
    }
}
