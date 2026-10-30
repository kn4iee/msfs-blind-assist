using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

// Every page of the RMP, live 2026-10-30 (captain's RMP): what it reads and which key each line presses. The side keys' names cross:
// AIRLINER_RMP_LSKn is the module's right-hand key, AIRLINER_RMP_ADKn its left-hand one.
public class A350RmpPageTests
{
    private static IReadOnlyList<RmpLine> Read(string fixture) => A350RmpPage.Read(A350Fixtures.Capture(fixture + ".json"));
    private static List<string> Said(string fixture) => Read(fixture).Select(l => l.Spoken).ToList();
    private static RmpLine Line(string fixture, string spoken) => Read(fixture).Single(l => l.Spoken == spoken);

    private const string Squawk = "SQUAWK : 2000 - STBY, white";

    // Review 3 (M9): a page's rule reads what it knows; a line it does not know was lost on most pages, where the radio page reads the rest.
    [Theory]
    [InlineData("rmp_nav_page.json")]
    [InlineData("rmp_menu_page.json")]
    [InlineData("rmp_router_page.json")]
    [InlineData("rmp_com_status.json")]
    [InlineData("rmp_datalink_init.json")]
    [InlineData("rmp_vhf_provider.json")]
    [InlineData("rmp_ls_page.json")]
    [InlineData("rmp_vor_page.json")]
    [InlineData("rmp_sqwk_page.json")]
    [InlineData("rmp_vhf_page.json")]
    public void What_a_page_does_not_name_still_reads(string fixture)
    {
        var capture = A350Fixtures.Capture(fixture);
        var extra = capture.Texts.First(t => !string.IsNullOrWhiteSpace(t.Text)) with { Text = "XYZZY", X = 500, Y = 300 };
        var lines = A350RmpPage.Read(capture with { Texts = capture.Texts.Append(extra).ToList() });
        Assert.Single(lines, l => l.Spoken.Contains("XYZZY", StringComparison.Ordinal));
    }

    // ...and what the rule has read is not said a second time.
    [Theory]
    [InlineData("rmp_nav_page.json")]
    [InlineData("rmp_menu_page.json")]
    [InlineData("rmp_router_page.json")]
    [InlineData("rmp_com_status.json")]
    [InlineData("rmp_datalink_init.json")]
    [InlineData("rmp_vhf_provider.json")]
    [InlineData("rmp_ls_page.json")]
    [InlineData("rmp_vor_page.json")]
    [InlineData("rmp_sqwk_page.json")]
    [InlineData("rmp_vhf_page.json")]
    public void Nothing_a_page_read_comes_twice(string fixture)
    {
        var lines = A350RmpPage.Read(A350Fixtures.Capture(fixture)).Select(l => l.Spoken).ToList();
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Fact]
    public void The_vhf_page_keeps_its_radios_and_presses_through_the_windows_own_keys()
    {
        Assert.Equal(new[]
        {
            "VHF1: active 122.800, white; standby 123.450, cyan",
            "VHF2: active 121.500, white; standby 124.850, white",
            "VHF3: active DATA, white; standby 121.500, white",
            Squawk,
        }, Said("rmp_vhf_page"));
        Assert.All(Read("rmp_vhf_page"), l => Assert.Null(l.Key));
    }

    [Fact]
    public void The_hf_page_reads_its_radios_and_the_am_key()
    {
        var said = Said("rmp_hf_page");
        Assert.Contains("HF1: active 6.200, white; standby 8.864, cyan", said);
        Assert.Contains("HF2: active DATA, white; standby 8.864, white", said);
        Assert.Equal("LSK3", Line("rmp_hf_page", "HF1 AM SELECT ON*, cyan").Key);
    }

    [Fact]
    public void The_squawk_page_says_the_code_under_its_label_and_the_ident_key()
    {
        Assert.Equal(new[] { "STBY, white", "SQWK: 2000, cyan", "*IDENT, cyan" }, Said("rmp_sqwk_page"));
        Assert.Equal("ADK3", Line("rmp_sqwk_page", "*IDENT, cyan").Key);
    }

    [Fact]
    public void The_nav_page_says_whether_standby_radio_nav_is_on_and_its_keys()
    {
        Assert.Equal(new[] { "STBY RAD NAV: Off", "LS >, grey", "VOR >, grey", Squawk }, Said("rmp_nav_page_off"));
        Assert.Equal(new[] { "STBY RAD NAV: On", "LS >, white", "VOR >, white", Squawk }, Said("rmp_nav_page"));
        Assert.Equal(A350Rmp.StbyRadNavKey, Line("rmp_nav_page", "STBY RAD NAV: On").Key);
        Assert.Equal("LSK1", Line("rmp_nav_page", "LS >, white").Key);
        Assert.Equal("LSK2", Line("rmp_nav_page", "VOR >, white").Key);
    }

    [Fact]
    public void The_ls_page_names_its_frequency_and_course_and_each_takes_its_own_key()
    {
        Assert.Equal(new[] { "LS frequency: 109.50, white", "LS course: 120, white", "ILS ACTIVE, grey", "GLS >, grey", Squawk }, Said("rmp_ls_page"));
        var frequency = Line("rmp_ls_page", "LS frequency: 109.50, white");
        Assert.Equal("ADK1", frequency.Key);
        Assert.True(frequency.Typed);
        var course = Line("rmp_ls_page", "LS course: 120, white");
        Assert.Equal("LSK1", course.Key);
        Assert.True(course.Typed);
        Assert.False(course.IsFrequency);
        Assert.True(frequency.IsFrequency);
    }

    [Fact]
    public void The_vor_page_names_each_receiver_and_an_unset_course()
    {
        Assert.Equal(new[]
        {
            "VOR1 frequency: 113.90, white", "VOR1 course: 270, cyan",
            "VOR2 frequency: 114.10, white", "VOR2 course: not set, amber",
            Squawk,
        }, Said("rmp_vor_page"));
        Assert.Equal("ADK2", Line("rmp_vor_page", "VOR2 frequency: 114.10, white").Key);
        Assert.Equal("LSK2", Line("rmp_vor_page", "VOR2 course: not set, amber").Key);
    }

    [Fact]
    public void The_menu_and_the_router_are_lists_of_keys()
    {
        Assert.Equal(new[] { "MENU, white", "DATALINK ROUTER >, white", "SATCOM INFO >, grey", Squawk }, Said("rmp_menu_page"));
        Assert.Equal("LSK2", Line("rmp_menu_page", "DATALINK ROUTER >, white").Key);
        Assert.Equal(new[] { "DATALINK ROUTER, white", "INIT >, white", "COM STATUS >, white", "VHF PROVIDER >, white", "< RETURN, white", Squawk }, Said("rmp_router_page"));
        Assert.Equal("LSK3", Line("rmp_router_page", "VHF PROVIDER >, white").Key);
        Assert.Equal("ADK3", Line("rmp_router_page", "< RETURN, white").Key);
    }

    [Fact]
    public void The_datalink_init_page_pairs_labels_and_values()
    {
        var said = Said("rmp_datalink_init");
        Assert.Contains("DATALINK INIT, white", said);
        Assert.Contains("A/C REGISTATION FIELD: N/A, grey", said);   // the module's spelling
        Assert.Contains("A/L IDENT: IN / INI, grey", said);
        Assert.Equal("ADK3", Line("rmp_datalink_init", "< RETURN, white").Key);
    }

    [Fact]
    public void The_com_status_page_reads_as_a_table()
    {
        var said = Said("rmp_com_status");
        Assert.Contains("DATALINK COM STATUS, white", said);
        Assert.Contains("VHF 3: EQPT STATUS DATA, DTLNK COM STATUS AVAIL, grey", said);
        Assert.Contains("HF 1/2: EQPT STATUS DATA, DTLNK COM STATUS AVAIL, grey", said);
        Assert.Contains("SAT: EQPT STATUS DATA, DTLNK COM STATUS AVAIL, grey", said);
    }

    [Fact]
    public void The_vhf_provider_page_pairs_labels_and_values()
    {
        var said = Said("rmp_vhf_provider");
        Assert.Contains("VHF DATALINK PROVIDER, white", said);
        Assert.Contains("SCAN MODE: AUTO, grey", said);
        Assert.Contains("ACTIVE PROVIDER: CONNECTED, white", said);
        Assert.Equal("ADK3", Line("rmp_vhf_provider", "MAN SCAN ACTIVATE, cyan").Key);
    }

    [Theory]
    [InlineData("109.5", true, new[] { "1", "0", "9", "PERIOD", "5", "0" })]
    [InlineData("113.90", true, new[] { "1", "1", "3", "PERIOD", "9", "0" })]
    [InlineData("11390", true, new[] { "1", "1", "3", "PERIOD", "9", "0" })]
    [InlineData("70", false, new[] { "0", "7", "0" })]
    [InlineData("270", false, new[] { "2", "7", "0" })]
    [InlineData("360", false, new[] { "3", "6", "0" })]
    public void A_typed_entry_becomes_the_keys_the_field_takes(string typed, bool frequency, string[] keys) =>
        Assert.Equal(keys, A350Rmp.TuningKeys(typed, frequency));

    [Theory]
    [InlineData("107.95", true)]
    [InlineData("118.00", true)]
    [InlineData("abc", true)]
    [InlineData("", true)]
    [InlineData("361", false)]
    [InlineData("1000", false)]
    [InlineData("12.5", false)]
    public void A_typed_entry_out_of_range_is_refused(string typed, bool frequency) => Assert.Null(A350Rmp.TuningKeys(typed, frequency));

    // The Surroundings window's Enter tunes VHF 1 through RMP 1's keys, as a pilot does (manual p. 74-75): the VHF page, a new entry's
    // CLR presses and its line's key, the digits, the line's key to load it as standby, the activation key to swap it in.
    [Fact]
    public void Tuning_vhf1_from_outside_presses_the_rmps_own_keys()
    {
        var clears = Enumerable.Repeat("CLR", A350RmpEntry.ClearPresses);
        Assert.Equal(new[] { "VHF" }.Concat(clears).Concat(new[] { "LSK1", "1", "1", "8", "7", "0", "0", "LSK1" }), A350Rmp.ComTuningKeys(118_700_000, active: false));
        Assert.Equal(new[] { "VHF" }.Concat(clears).Concat(new[] { "LSK1", "1", "2", "1", "8", "3", "3", "LSK1", "ADK1" }), A350Rmp.ComTuningKeys(121_833_000, active: true));
    }

    [Fact]
    public void The_screen_says_what_vhf1_holds()
    {
        var lines = Read("rmp_vhf_page");
        Assert.Equal(122_800_000, A350RmpPage.Vhf1Hz(lines, active: true));
        Assert.Equal(123_450_000, A350RmpPage.Vhf1Hz(lines, active: false));
        Assert.Null(A350RmpPage.Vhf1Hz(Read("rmp_hf_page"), active: true));
        // VHF 3's DATA is no frequency: it would never stand on VHF 1, and says nothing if it did.
        Assert.Null(A350RmpPage.Vhf1Hz(new[] { new RmpLine("VHF1: active DATA, white; standby 121.500, white") }, active: true));
    }

    [Fact]
    public void Standby_radio_nav_going_on_says_the_page_it_opened_not_its_own_state_twice()
    {
        Assert.Equal("LS >, white. VOR >, white", A350Rmp.PageReadBack(A350Rmp.StbyRadNavKey, Said("rmp_nav_page_off"), Said("rmp_nav_page")));
    }

    [Fact]
    public void Standby_radio_nav_is_written_for_the_rmps_own_side()
    {
        Assert.Equal("(L:INI_CPT_RADIO_MODE) ! (>L:INI_CPT_RADIO_MODE)", A350Rmp.Press(1, A350Rmp.StbyRadNavKey));
        Assert.Equal("(L:INI_FO_RADIO_MODE) ! (>L:INI_FO_RADIO_MODE)", A350Rmp.Press(2, A350Rmp.StbyRadNavKey));
        Assert.Equal("(L:INI_OVHD_RADIO_MODE) ! (>L:INI_OVHD_RADIO_MODE)", A350Rmp.Press(3, A350Rmp.StbyRadNavKey));
        Assert.Equal("1 (>B:AIRLINER_RMP_NAV_2_Set)", A350Rmp.Press(2, "NAV"));
    }
}
