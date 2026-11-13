using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26's re-review: the SD was read by the page the A350's variable says (INI_ECAM_ACTIVE_PAGE), which reaches MSFSBA a
/// moment after the draw shows a new page, so a new page could be read by the old page's fields. A draw names its own page: the page
/// whose fields its values fill (as <see cref="A350SdPage.StatusShowing"/> finds the page STATUS shows), STATUS by its own word. Every
/// captured SD page, on the stand and live.</summary>
public class A350SdPageOfTests
{
    [Theory]
    [InlineData("ctr_sd_page_1.json", 1)]
    [InlineData("ctr_sd_page_2.json", 2)]
    [InlineData("ctr_sd_page_3.json", 3)]
    [InlineData("ctr_sd_live_3.json", 3)]
    [InlineData("ctr_sd_elec_dc_stand.json", 3)]
    [InlineData("ctr_sd_page_4.json", 4)]
    [InlineData("ctr_sd_live_4.json", 4)]
    [InlineData("ctr_sd_page_5.json", 5)]
    [InlineData("ctr_sd_page_6.json", 6)]
    [InlineData("ctr_sd_page_7.json", 7)]
    [InlineData("ctr_sd_page_8.json", 8)]
    [InlineData("ctr_sd_fuel_stand.json", 8)]
    [InlineData("ctr_sd_page_9.json", 9)]
    [InlineData("ctr_sd_page_10.json", 10)]
    [InlineData("ctr_sd_page_11.json", 11)]
    [InlineData("ctr_sd_live_11.json", 11)]
    [InlineData("ctr_sd_page_12.json", 12)]
    [InlineData("ctr_sd_cruise_13.json", 13)]
    [InlineData("p04_ctr_sd_cruise_air.json", 13)]
    [InlineData("ctr_sd_page_14.json", 14)]
    [InlineData("ctr_sd_status_inop_14.json", 14)]
    // STATUS chosen in the air with nothing to say draws the CRUISE page; the variable's 14 then names it "Status page, showing the
    // Cruise page" (A350SdText.Shown).
    [InlineData("p04_ctr_sd_status_air.json", 13)]
    public void A_draw_names_its_own_sd_page(string fixture, int page) =>
        Assert.Equal(page, A350SdPage.PageOf(A350Ewd.Parse(A350Fixtures.Capture(fixture)).SystemDisplay));

    // A draw that fits no page well enough names none: the reader then falls back to the variable.
    [Fact]
    public void An_empty_sd_names_no_page() =>
        Assert.Null(A350SdPage.PageOf(Array.Empty<IReadOnlyList<DrawnCell>>()));
}
