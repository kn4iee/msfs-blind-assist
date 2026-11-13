using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26's re-review: the SD was read by the page the A350's variable says (INI_ECAM_ACTIVE_PAGE), which reaches MSFSBA a
/// moment after the draw shows a new page, so a new page could be read by the old page's fields. A draw names its own page: the page
/// whose fields its values fill, STATUS by its own word, and the page STATUS shows is found the same way. Every SD capture whose page its
/// name says (the stand's, plan 04's live ones on the ground and in the air, plan 24's symbols', F/CTL's and the wheel's), on that page.</summary>
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
    // STATUS on the ground with an alert (BRAKES PARK BRK ON) draws its WHEEL page.
    [InlineData("p04_ctr_sd_status_alert_ground.json", 12)]
    // Re-review: the live captures too, each page in the air and on the ground (plan 04), the symbols' captures (plan 24), F/CTL's
    // rudder and the mailbox captures, which drew F/CTL: a live F/CTL draws two of its fields as text, the rest as shapes.
    [InlineData("p04_ctr_sd_apu_air.json", 9)]
    [InlineData("p04_ctr_sd_apu_ground.json", 9)]
    [InlineData("p04_ctr_sd_bleed_air.json", 5)]
    [InlineData("p04_ctr_sd_bleed_ground.json", 5)]
    [InlineData("p04_ctr_sd_cond_air.json", 6)]
    [InlineData("p04_ctr_sd_cond_ground.json", 6)]
    [InlineData("p04_ctr_sd_door_air.json", 11)]
    [InlineData("p04_ctr_sd_door_ground.json", 11)]
    [InlineData("p04_ctr_sd_elecac_air.json", 4)]
    [InlineData("p04_ctr_sd_elecac_ground.json", 4)]
    [InlineData("p04_ctr_sd_elecdc_air.json", 3)]
    [InlineData("p04_ctr_sd_elecdc_ground.json", 3)]
    [InlineData("p04_ctr_sd_engine_air.json", 1)]
    [InlineData("p04_ctr_sd_engine_ground.json", 1)]
    [InlineData("p04_ctr_sd_fctl_air.json", 10)]
    [InlineData("p04_ctr_sd_fctl_ground.json", 10)]
    [InlineData("p04_ctr_sd_fuel_air.json", 8)]
    [InlineData("p04_ctr_sd_fuel_ground.json", 8)]
    [InlineData("p04_ctr_sd_hyd_air.json", 2)]
    [InlineData("p04_ctr_sd_hyd_ground.json", 2)]
    [InlineData("p04_ctr_sd_press_air.json", 7)]
    [InlineData("p04_ctr_sd_press_ground.json", 7)]
    [InlineData("p04_ctr_sd_wheel_air.json", 12)]
    [InlineData("p04_ctr_sd_wheel_ground.json", 12)]
    [InlineData("ctr_sdsym_1.json", 1)]
    [InlineData("ctr_sdsym_2.json", 2)]
    [InlineData("ctr_sdsym_3.json", 3)]
    [InlineData("ctr_sdsym_4.json", 4)]
    [InlineData("ctr_sdsym_5.json", 5)]
    [InlineData("ctr_sdsym_6.json", 6)]
    [InlineData("ctr_sdsym_7.json", 7)]
    [InlineData("ctr_sdsym_8.json", 8)]
    [InlineData("ctr_sdsym_9.json", 9)]
    [InlineData("ctr_sdsym_10.json", 10)]
    [InlineData("ctr_sdsym_11.json", 11)]
    [InlineData("ctr_sdsym_12.json", 12)]
    [InlineData("ctr_sdsym_apu_3.json", 3)]
    [InlineData("ctr_sdsym_apu_4.json", 4)]
    [InlineData("ctr_sdsym_apu_5.json", 5)]
    [InlineData("ctr_sdsym_apu_6.json", 6)]
    [InlineData("ctr_sdsym_apu_7.json", 7)]
    [InlineData("ctr_sdsym_apu_9.json", 9)]
    [InlineData("ctr_sdsym_apu_10.json", 10)]
    [InlineData("ctr_sdsym_apu_11.json", 11)]
    [InlineData("ctr_sdsym_apu_12.json", 12)]
    [InlineData("ctr_sdsym_8_pumps_on.json", 8)]
    [InlineData("ctr_sdsym_apu_9_bleed_off.json", 9)]
    [InlineData("ctr_sdsym_door_l1_open.json", 11)]
    [InlineData("ctr_fctl_rudder_halfright.json", 10)]
    [InlineData("ctr_fctl_rudder_left.json", 10)]
    [InlineData("ctr_fctl_rudder_limit.json", 10)]
    [InlineData("ctr_fctl_rudder_neutral.json", 10)]
    [InlineData("p22_ctr_mailbox_clearance.json", 10)]
    [InlineData("p22_ctr_mailbox_status.json", 10)]
    [InlineData("p22_ctr_mailbox_wilco_staged.json", 10)]
    [InlineData("ctr_wheel_gear_down_transit.json", 12)]
    [InlineData("ctr_wheel_gear_up_transit.json", 12)]
    [InlineData("ctr_wheel_hot_brake.json", 12)]
    public void A_draw_names_its_own_sd_page(string fixture, int page) =>
        Assert.Equal(page, A350SdPage.PageOf(A350Ewd.Parse(A350Fixtures.Capture(fixture)).SystemDisplay));

    // A draw that fits no page well enough names none: the reader then falls back to the variable.
    [Fact]
    public void An_empty_sd_names_no_page() =>
        Assert.Null(A350SdPage.PageOf(Array.Empty<IReadOnlyList<DrawnCell>>()));

    // Review m3: a live F/CTL draw with one stray text on another page's field is still F/CTL (it fills two of its fields, the other
    // page one); a draw filling two fields of each of two pages names neither.
    [Fact]
    public void A_stray_text_does_not_hide_a_live_fctl_page()
    {
        var rows = A350Ewd.Parse(A350Fixtures.Capture("p04_ctr_sd_fctl_air.json")).SystemDisplay;
        var eng = A350SdPage.Pages[1];
        IReadOnlyList<IReadOnlyList<DrawnCell>> With(params SdField[] fields) =>
            rows.Append(fields.Select(f => new DrawnCell("XX", "amber", f.X, f.Y)).ToList()).ToList();
        Assert.Equal(10, A350SdPage.PageOf(With(eng[0])));
        Assert.Null(A350SdPage.PageOf(With(eng[0], eng[2])));
    }

    // Review M1: the page STATUS shows is found as any draw's page is, one scorer for both: HYD's two fields and F/CTL's two named it
    // (STATUS's own scorer wanted three, so STATUS showing HYD read as a bare STATUS page); STATUS drawn shows no other page.
    [Theory]
    [InlineData("p04_ctr_sd_hyd_air.json", 2)]
    [InlineData("p04_ctr_sd_fctl_air.json", 10)]
    [InlineData("p04_ctr_sd_status_air.json", 13)]
    [InlineData("ctr_sd_page_14.json", null)]
    public void Status_shows_the_page_its_draw_names(string fixture, int? page) =>
        Assert.Equal(page, A350SdPage.StatusShowing(A350Ewd.Parse(A350Fixtures.Capture(fixture)).SystemDisplay));
}
