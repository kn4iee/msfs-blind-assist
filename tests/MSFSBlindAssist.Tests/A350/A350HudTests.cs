using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The HUD (the option on, the combiner deployed; live 2026-10-30, on the stand and in the climb): all green, its FMA in the PFD's columns,
// its scales' numbers cut off at their ends by the clip.
public class A350HudTests
{
    private static IReadOnlyList<string> Read(string fixture) => A350GroundDisplays.ReadHud(A350Fixtures.Capture(fixture + ".json"));

    [Fact]
    public void The_fma_reads_by_column()
    {
        var lines = Read("hud_climb");
        Assert.Contains("FMA autothrust: THR DCLB, green", lines);
        Assert.Contains("FMA vertical: OP CLB; ALT, green", lines);
        Assert.Contains("FMA lateral: NAV, green", lines);
        Assert.Contains("FMA engagement: ap1; 1 FD 2; A/THR, green", lines);
    }

    [Fact]
    public void Its_values_are_named()
    {
        var lines = Read("hud_climb");
        Assert.True(lines.Contains("Selected altitude: 10000, green"), string.Join(" | ", lines));
        Assert.Contains("QNH: 1013, green", lines);
        Assert.Contains("Wind: 303 / 8, green", lines);
        Assert.Contains("Speed scale: 160, 180, 200, 220, green", lines);
        Assert.Contains("Altitude scale: 030, 035, 040, green", lines);
        Assert.Contains("Heading scale: 28, 29, 30, green", lines);
    }

    [Fact]
    public void On_the_stand_it_says_the_ground_speed_and_only_what_the_clip_leaves()
    {
        var lines = Read("hud_ground");
        Assert.Contains("Ground speed: 0, green", lines);
        Assert.Contains("FMA engagement: 1 FD 2, green", lines);
        Assert.Contains("Heading scale: 21, 22, 23, green", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Heading scale", StringComparison.Ordinal) && l.Contains("19", StringComparison.Ordinal));
    }

    // Review 3 (I4): what it does not name reads as drawn, as every other display's does: a flag or a word it does not know was lost.
    // Plan 24 (owner, 2026-11-03: "keeping everything if possible"): the lone N, drawn at one place in every flight frame and never on
    // the stand, is no compass letter (none turns with the heading) and reads as drawn; the 18 beside the altitude is the V/S.
    [Fact]
    public void What_it_does_not_name_reads_as_drawn()
    {
        var capture = A350Fixtures.Capture("hud_climb.json");
        var flag = capture.Texts.First(t => t.Text.Trim() == "NAV") with { Text = "BIRD OFF", X = 700, Y = 600 };
        var lines = A350GroundDisplays.ReadHud(capture with { Texts = capture.Texts.Append(flag).ToList() });
        Assert.Contains("BIRD OFF, green", lines);
        Assert.Contains("N, green", lines);
        Assert.Contains("Vertical speed: 1800 feet per minute up, green", lines);
    }

    // Plan 24, the second flight of 2026-11-03 (the combiner deployed; level at 10,000 ft, 220 kt, heading 271 by the PFD): what the
    // HUD shows on its scales and drum read off as the PFD's and the SFD's are. The speed tape 5.4 px a knot, its window at y 648;
    // the altitude tape 0.388 px a foot, the drum's tens 1.275 px a foot, its window at y 645; the horizon's heading labels 53.1 px
    // a degree, the boresight at x 800 (each fitted on the flight's level rounds against the PFD).
    [Fact]
    public void It_reads_speed_altitude_and_heading_off_its_scales()
    {
        var lines = Read("hud_level_10000");
        Assert.Contains("Speed: 220 knots, green", lines);
        Assert.Contains("Altitude: 10002 feet, green", lines);
        Assert.Contains("Heading: 271, green", lines);
        Assert.Contains("Selected altitude: 10000, green", lines);   // drawn on the tape at its height
        Assert.Contains("Speed scale: 200, 220, 240, 260, green", lines);   // the 26 at x 197 is the horizon's
    }

    [Fact]
    public void It_reads_the_mach_and_the_ls_block()
    {
        var lines = Read("hud_mach");
        Assert.Contains("Mach: 0.603, green", lines);
        Assert.Contains("LS: ILS, 109.90, 0.0 NM, green", lines);
        Assert.Contains("Heading: 098, green", lines);
    }

    [Fact]
    public void It_reads_the_vertical_speed_in_hundreds()
    {
        Assert.Contains("Vertical speed: 2800 feet per minute up, green", Read("hud_climb_vs"));
    }

    // The same frame in a right turn (the DIRECT TO back east): the horizon's labels turned with the bank, unturned in their local
    // place (one row, 531 px apart). The heading reads off that row, the bank is its turn, the pitch its height (45 px a degree,
    // zero at y 459: the stand's -0.5, cruise's 2.35 and 0.3 by the PFD). Before, the reader missed the tilted labels and said
    // them as bare numbers.
    [Fact]
    public void In_a_turn_it_reads_heading_bank_and_pitch_off_the_turned_horizon()
    {
        var lines = Read("hud_climb_vs");
        Assert.Contains("Heading: 002, green", lines);
        Assert.Contains("Bank: 28 degrees right", lines);
        Assert.Contains("Pitch: 6.7 degrees up", lines);
        Assert.Contains("Heading scale: 35, 00, 01, 02, green", lines);
        Assert.DoesNotContain("00, green", lines);
    }

    [Fact]
    public void Level_it_reads_wings_level_and_its_pitch()
    {
        var lines = Read("hud_level_10000");
        Assert.Contains("Bank: wings level", lines);
        Assert.Contains("Pitch: 2.3 degrees up", lines);
    }

    // On the stand: the tape on its 30 kt floor, V1 beside it, the speed target on its filled box (the number black), the LS block's
    // ident line showing the frequency again (said once).
    [Fact]
    public void On_the_stand_it_reads_v1_the_speed_target_and_the_ls_block()
    {
        var lines = Read("hud_ground_ls");
        Assert.Contains("Speed: 30 knots or less, green", lines);
        Assert.Contains("V1: 141, green", lines);
        Assert.Contains("Speed target: 100, green", lines);
        Assert.Contains("LS: ILS, 109.90, 0.1 NM, green", lines);
        Assert.Contains("Altitude: 46 feet, green", lines);   // the PFD 45: the drum's window is fitted to a foot or two
        Assert.Contains("Heading: 039, green", lines);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Its_numbers_read_the_same_in_every_culture(string culture)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var lines = Read("hud_mach");
            Assert.Contains("Mach: 0.603, green", lines);
            Assert.Contains("LS: ILS, 109.90, 0.0 NM, green", lines);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Stowed_it_is_blank() =>
        Assert.Equal(new[] { "HUD: blank" }, A350GroundDisplays.ReadHud(new A350DrawCapture("HUD", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 0)));
}
