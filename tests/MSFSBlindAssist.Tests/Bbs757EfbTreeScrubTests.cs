using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757EfbTreeScrubTests
{
    private static Bbs757EfbMemory.Node N(string name, string text, params Bbs757EfbMemory.Node[] kids) =>
        new("label", name, text, 1.5f, 2.25f, 30, 40, 0x1234, kids);

    private static IReadOnlyList<Bbs757EfbMemory.App> App(params Bbs757EfbMemory.Node[] roots) =>
        new[] { new Bbs757EfbMemory.App(3, "SETTINGS", roots) };

    [Fact]
    public void The_saved_simbrief_id_becomes_the_made_up_one()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("simbrief_id_saved_label", "987654")));
        Assert.Equal("123456", scrubbed[0].Controls[0].Text);
    }

    [Fact]
    public void The_acars_codes_become_made_up_ones()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(
            N("hoppie_logon_code_value_label", "abc"), N("sayintentions_acars_key_value_label", "xyz"),
            N("hoppie_textbox", "secret"), N("hoppie_logon_code_value_label", "------"), N("other_textbox", "------")));
        Assert.Equal(new[] { "MADEUP", "MADEUP", "MADEUP", "------", "------" },
            scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void The_flight_plan_becomes_the_made_up_flight()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(
            N("planning_acars_flight_info_value_0", "ABC123"), N("planning_acars_flight_info_value_1", "ABC"),
            N("planning_acars_flight_info_value_2", "EGLL / LHR"), N("planning_acars_flight_info_value_3", "LFPG / CDG"),
            N("planning_acars_flight_info_value_9", "42"), N("planning_acars_route_value", "EGLL DCT LFPG"),
            N("planning_acars_date_value_4", "05 Mar 27"), N("planning_acars_time_value_5", "08:30 UTC"),
            N("planning_operating_fp_value", "1234")));
        Assert.Equal(new[] { "MSF1", "MSF1", "KSEA / SEA", "KPDX / PDX", "0", "KSEA/16L DCT BTG DCT KPDX/28R",
            "01 Jan 26", "12:00 UTC", "0" }, scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void Every_other_text_is_kept()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("title", "Settings", N("child_label", "Keep me"))));
        Assert.Equal("Settings", scrubbed[0].Controls[0].Text);
        Assert.Equal("Keep me", scrubbed[0].Controls[0].Children[0].Text);
    }

    [Fact]
    public void A_nested_credential_is_scrubbed()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("box", "", N("simbrief_id_saved_label", "999"))));
        Assert.Equal("123456", scrubbed[0].Controls[0].Children[0].Text);
    }

    [Fact]
    public void The_json_round_trips_without_the_address()
    {
        var apps = App(N("a", "x", N("b", "y")));
        var back = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(apps));
        Assert.Equal(3, back[0].Key);
        Assert.Equal("SETTINGS", back[0].Class);
        var a = back[0].Controls[0];
        Assert.Equal(("label", "a", "x", 1.5f, 2.25f, 30f, 40f, 0u), (a.Kind, a.Name, a.Text, a.X, a.Y, a.W, a.H, a.Address));
        Assert.Equal("y", a.Children[0].Text);
    }

    [Fact]
    public void The_json_uses_invariant_numbers_under_a_comma_decimal_culture()
    {
        foreach (var culture in new[] { "de-DE", "sv-SE" })
        {
            string json = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Write(App(N("a", "x"))));
            Assert.Contains("\"x\": 1.5", json);
            var back = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Read(json));
            Assert.Equal(2.25f, back[0].Controls[0].Y);
        }
    }
}
