using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The PFD read from the EFIS draw. Fixtures: the captain's EFIS on the stand at VCBI, IRs aligned, FDs on, and
// in a climb through 2940 ft (2026-10-27). The altitude window is at y 389 (the tape's labels, 0.35 px a foot,
// and the drum's tens, 21.75 px per 20 ft, agree there, in flight and on the stand); 219 degrees magnetic put
// the heading index at x 359, 7 px a degree.
public class A350PfdTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void The_captain_pfd_reads_by_area()
    {
        var lines = A350Pfd.Read(Fixture("efis0_fd_stand"), 0);
        foreach (var l in new[]
        {
            "FMA engagement: 1 FD 2, white", "Selected speed: 100, cyan", "Altitude: 43 feet",
            "Selected altitude: 100, cyan", "QNH: 1013, cyan", "Radio altitude: 0, amber", "Heading: 219",
            "Speed: 30 knots or less",   // on the stand the tape rests on its 30 kt floor (its offset reads 32)
        })
            Assert.Contains(l, lines);
        Assert.Contains(lines, l => l.StartsWith("T.O THS FOR, white; 28.5"));
        Assert.DoesNotContain(lines, l => l.Contains("010") || l.Contains("80, green"));   // tape and drum labels
    }

    // Ready for takeoff on the stand (live 2026-10-28, PERF T.O V1 142, VR 143, V2 148): above the speed tape, off its
    // scale, the speed target (x 153, y 174: the managed V2 in magenta; a selected speed in cyan) and under it V1 in cyan
    // (x 129, y 207). The message line under the heading scale reads as one line, its red two-line OUT OF / RANGE beside
    // the white T.O THS (the trim not yet set to PERF's THS).
    [Fact]
    public void The_takeoff_speeds_and_the_trim_message_read_as_the_pfd_shows_them()
    {
        var lines = A350Pfd.Read(Fixture("efis0_takeoff_ready"), 0);
        Assert.Contains("Speed target: 148, magenta", lines);
        Assert.Contains("V1: 142, cyan", lines);
        Assert.Contains("T.O THS, white; OUT OF RANGE, red", lines);
        Assert.Contains("Speed: 30 knots or less", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Selected speed", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("OUT OF", StringComparison.Ordinal) || l.StartsWith("RANGE", StringComparison.Ordinal));
    }

    // Out of the tape's range the selected altitude is drawn twice: the readout at the tape's top or bottom edge (x 688; y
    // 172 above, 615 below) and the tape's own symbol held at the screen's top or bottom (x 597; y 38, 742), where the
    // tape's clip hides it. It read "Selected altitude: 40000 40000, cyan" on the stand, and a bare "4000, cyan" on the
    // downwind at OMDB descending to 4000.
    [Theory]
    [InlineData("efis0_takeoff_ready", "Selected altitude: 40000, cyan")]
    [InlineData("efis0_ls_downwind_omdb", "Selected altitude: 4000, cyan")]
    public void Out_of_range_the_selected_altitude_reads_once(string fixture, string selected)
    {
        var lines = A350Pfd.Read(Fixture(fixture), 0);
        Assert.Equal(selected, Assert.Single(lines, l => l.Contains("000, cyan")));
    }

    // At the hold with the SID's 5000 ft constraint ahead (live 2026-10-28: "FMA approach: 5000, magenta"), the tape's
    // symbol is the constraint the vertical mode will level at, magenta, while the readout keeps the FCU's altitude.
    [Fact]
    public void A_magenta_altitude_symbol_reads_as_the_constraint()
    {
        var cap = Fixture("efis0_takeoff_ready");
        var magenta = new A350Rgba(1, 0, 1, 1);
        var texts = cap.Texts.Select(t => t.Text == "40000" && t.LocalY < 60 ? t with { Text = "5000", Colour = magenta } : t).ToList();
        var lines = A350Pfd.Read(cap with { Texts = texts }, 0);
        Assert.Contains("Altitude constraint: 5000, magenta", lines);
        Assert.Contains("Selected altitude: 40000, cyan", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("FMA approach", StringComparison.Ordinal));
    }

    // Under the speed tape in the climb (live 2026-10-28, ".681" green at x 50, y 657 in STD at FL226): the Mach number,
    // which read bare.
    [Fact]
    public void The_mach_number_under_the_speed_tape_reads_by_name()
    {
        var lines = A350Pfd.Read(Fixture("pfd_climb_std_fl400"), 0);
        Assert.Contains("Mach: 0.681, green", lines);
        Assert.DoesNotContain(".681, green", lines);
    }

    // The FMA's fourth column holds the approach capability (APPR1, LAND3 SINGLE) and, on its third row, the minimum the
    // pilot set on PERF APPR: "RADIO" white and its height cyan (the downwind at OMDB, RADIO 50). It read as part of the
    // capability ("FMA approach: APPR1, white; RADIO, white; 50, cyan").
    [Fact]
    public void The_minimum_reads_apart_from_the_approach_capability()
    {
        var lines = A350Pfd.Read(Fixture("efis0_ls_downwind_omdb"), 0);
        Assert.Contains("FMA approach: APPR1, white", lines);
        Assert.Contains("Minimum: RADIO 50, cyan", lines);
    }

    // Live 2026-10-28 the PFD said "Selected speed: 305, cyan" with the speed managed. The selected heading off the
    // scale is a cyan number at the scale's end, here its left end (x 192, y 690: stopped on OMDB 12L heading 121 with
    // 305 selected), inside the speed tape's band, so it was read twice: as a speed and as the heading.
    [Fact]
    public void The_off_scale_selected_heading_is_not_a_speed()
    {
        var lines = A350Pfd.Read(Fixture("efis0_stopped_omdb_12l"), 0);
        Assert.Contains("Selected heading: 305, cyan", lines);
        Assert.Contains("Selected speed: 100, cyan", lines);
        Assert.DoesNotContain("Selected speed: 305, cyan", lines);
    }

    [Fact]
    public void The_first_officer_pfd_is_the_left_half()
    {
        var lines = A350Pfd.Read(Fixture("efis1_fd_stand"), 1);
        Assert.Contains("FMA engagement: 1 FD 2, white", lines);
        Assert.Contains("QNH: 1010, cyan", lines);
    }

    [Fact]
    public void A_flag_reads_with_its_colour()
    {
        var lines = A350Pfd.Read(Fixture("efis0_aligning"), 0);
        Assert.Contains("ATT, red", lines);
    }

    [Theory]
    [InlineData(0, 405f, 0, 436f, 43)]       // the stand
    [InlineData(30, 369f, 40, 390f, 2941)]   // the climb
    [InlineData(30, 369f, 80, 346f, 2940)]   // a tens label away from the window still lands in the right hundred
    public void The_altitude_reads_the_tape_and_the_drum(int tapeHundreds, float tapeY, int tens, float tensY, int feet)
    {
        Assert.Equal(feet, A350Pfd.Altitude(tapeHundreds, tapeY, tens, tensY));
    }

    [Fact]
    public void In_the_climb_the_pfd_reads_altitude_heading_and_vertical_speed()
    {
        var lines = A350Pfd.Read(Fixture("efis0_climb"), 0);
        Assert.Contains("Altitude: 2941 feet", lines);
        Assert.Contains("Heading: 31", lines);
        Assert.Contains("Vertical speed: 1400 feet per minute up, green", lines);
        Assert.Contains("FMA autothrust: SPEED, green", lines);
        Assert.Contains("FMA vertical: ALT CRZ*, green", lines);
        Assert.Contains("FMA lateral: HDG, green", lines);
        Assert.Contains("FMA engagement: 1 FD 2; A/THR, white", lines);
    }

    // The FMA's small capitals are lowercase letters in the PFD's font ("ap1" for AP1; live 2026-10-28 "man flx" for
    // MAN FLX): they read as the capitals the pilot sees.
    [Fact]
    public void The_fmas_small_capitals_read_as_capitals()
    {
        var lines = A350Pfd.Read(Fixture("efis0_level6000"), 0);
        Assert.Contains("FMA engagement: AP1; 1 FD 2; A/THR, white", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("FMA", StringComparison.Ordinal) && l.Contains("ap1"));
    }

    // Speed, pitch and bank are textures: the speed tape's offset (4.875 px a knot) and the horizon under the
    // attitude centre (x 359, y 389; 8.61 px a degree), turned by the bank. Live pairs with the sim: 240.2 kt
    // at -885.2; 3.61 and 6.63 degrees nose up; a left turn read 24.98 degrees against the horizon's 24.72.
    [Fact]
    public void Level_flight_reads_speed_pitch_and_wings_level()
    {
        var lines = A350Pfd.Read(Fixture("efis0_level6000"), 0);
        Assert.Contains("Speed: 240 knots", lines);
        Assert.Contains("Pitch: 3.6 degrees up", lines);
        Assert.Contains("Bank: wings level", lines);
    }

    // The EFIS draws its ND, then its PFD (live, both sides); an ND map label can land on the PFD's half (the
    // display clips it), so each display reads only its own part of the draw.
    [Fact]
    public void An_nd_map_label_never_reads_on_the_pfd()
    {
        var cap = Fixture("efis0_turn_left");
        var nd = new DrawnText("MTL", 1500, 300, 1500, 300, 1, new A350Rgba(0.4f, 0.86f, 1, 1));
        var texts = cap.Texts.ToList();
        texts.Insert(1, nd);
        var lines = A350Pfd.Read(cap with { Texts = texts }, 0);
        Assert.DoesNotContain(lines, l => l.Contains("MTL"));
        Assert.Contains("Heading: 18", lines);   // mid-turn towards 330
    }

    // The ILS 04 at VCBI (live 2026-10-27, LS on, LOC engaged, G/S armed): the deviation diamonds read in dots
    // against their scales (PFDA350Elements.png: dots 70.25 px apart, the LOC scale's centre 146.25 px in, the
    // G/S scale's 146.75 px down), where the aircraft is, as the A380 says it; a diamond off its scale is not
    // drawn. The LS block reads as one line: ident, frequency (drawn "109." and "90"), DME.
    [Theory]
    [InlineData("efis0_ils_above", "Localizer: 0.1 dots left of localizer", "Glideslope: 1.4 dots above glideslope", "LS: IKW 109.90, 7.8 NM, magenta")]
    [InlineData("efis0_ils_gs_off", "Localizer: on the localizer", "Glideslope: off scale", "LS: IKW 109.90, 6.6 NM, magenta")]
    [InlineData("efis0_ils_below", "Localizer: on the localizer", "Glideslope: 0.5 dots below glideslope", "LS: IKW 109.90, 10.0 NM, magenta")]
    public void The_ils_deviation_reads_in_dots(string fixture, string loc, string gs, string ls)
    {
        var lines = A350Pfd.Read(Fixture(fixture), 0);
        Assert.Contains(loc, lines);
        Assert.Contains(gs, lines);
        Assert.Contains(ls, lines);
        Assert.True(lines.ToList().IndexOf(ls) > lines.ToList().IndexOf(gs), "the LS block reads after the deviation");
        Assert.DoesNotContain(lines, l => l.StartsWith("IKW"));
    }

    // The ILS 12L at OMDB from the downwind (live 2026-10-28, LS on, LOC not armed, heading about 300): the LS
    // block reads ident, frequency and DME as at VCBI ("The ILS identifier, frequency, and distance (DME) is
    // indicated on the lower left of the PFD", manual p. 120), and the course pointer, off the heading scale,
    // is a magenta number at the scale's left end. None of it is a speed: the speed tape's loop once took the
    // frequency's "10", the course and the cyan "nm".
    [Fact]
    public void The_ls_block_and_an_off_scale_course_read_as_themselves()
    {
        var lines = A350Pfd.Read(Fixture("efis0_ls_downwind_omdb"), 0);
        Assert.Contains("LS: IDBL 110.10, 3.0 NM, magenta", lines);
        Assert.Contains("LS course: 119, magenta", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Speed target") || l.StartsWith("Selected speed"));
    }

    // Before its ident decodes the block shows the frequency and DME alone: still the LS block.
    [Fact]
    public void The_ls_block_reads_before_its_ident_is_drawn()
    {
        var cap = Fixture("efis0_ls_downwind_omdb");
        var lines = A350Pfd.Read(cap with { Texts = cap.Texts.Where(t => t.Text != "IDBL").ToList() }, 0);
        Assert.Contains("LS: 110.10, 3.0 NM, magenta", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("110."));
    }

    // Review 2026-10-28 (minor 9): the block's rows were taken in order, the ident by being all letters, so an ident with
    // digits (an FLS or GLS one) shifted every row: "LS: R22, 110.10 NM". Each row has its own line: ident at y 689,
    // frequency at 717, DME at 745.
    [Fact]
    public void An_ident_with_digits_keeps_the_ls_blocks_rows_in_place()
    {
        var cap = Fixture("efis0_ls_downwind_omdb");
        var lines = A350Pfd.Read(cap with { Texts = cap.Texts.Select(t => t.Text == "IDBL" ? t with { Text = "R22" } : t).ToList() }, 0);
        Assert.Contains("LS: R22 110.10, 3.0 NM, magenta", lines);
    }

    [Fact]
    public void Without_ls_there_is_no_deviation_line()
    {
        Assert.DoesNotContain(A350Pfd.Read(Fixture("efis0_level6000"), 0), l => l.StartsWith("Localizer") || l.StartsWith("Glideslope"));
    }

    // The heading scale's sprites (PFDA350Elements.png, by atlas place): the selected heading bug (shown in HDG,
    // gone in LOC) and the track diamond; a target off the scale is a cyan number beside it instead (the bug
    // waits at the scale's edge). The flap indication at the bottom: "S" and "F" labels and the configuration,
    // cyan while moving, green once there.
    [Theory]
    [InlineData("efis0_level6000", "Selected heading: 031, cyan", "Track: 034")]
    [InlineData("efis0_climb", "Selected heading: 031, cyan", "Track: 033")]
    [InlineData("efis0_turn_left", "Selected heading: 330, cyan", "Track: 024")]
    public void The_heading_scale_reads_the_selected_heading_and_track(string fixture, string selected, string track)
    {
        var lines = A350Pfd.Read(Fixture(fixture), 0);
        Assert.Contains(selected, lines);
        Assert.Contains(track, lines);
        Assert.DoesNotContain("330, cyan", lines);
    }

    [Fact]
    public void In_loc_there_is_no_selected_heading_and_the_flaps_read_their_configuration()
    {
        var lines = A350Pfd.Read(Fixture("efis0_ils_below"), 0);
        Assert.DoesNotContain(lines, l => l.StartsWith("Selected heading"));
        Assert.Contains("Track: 037", lines);
        Assert.Contains("Flaps: 3, cyan", lines);
        Assert.DoesNotContain(lines, l => l is "S, white" or "F, white" or "3, cyan");
    }

    // Live near 2200 ft the altitude tape's "015" label passed through the baro's area and read as
    // "QNH: 015, white; ...": a tape label is never part of the baro.
    [Fact]
    public void A_tape_label_never_reads_as_the_baro()
    {
        var cap = Fixture("efis0_ils_below");
        var texts = cap.Texts.ToList();
        texts.Add(new DrawnText("015", 836 + 595, 657, 595, 657, 1, new A350Rgba(1, 1, 1, 1)));
        var lines = A350Pfd.Read(cap with { Texts = texts }, 0);
        Assert.Contains(lines, l => l.StartsWith("QNH: "));
        Assert.DoesNotContain(lines, l => l.Contains("015"));
    }

    [Fact]
    public void A_turn_reads_its_bank()
    {
        var lines = A350Pfd.Read(Fixture("efis0_turn_left"), 0);
        Assert.Contains("Bank: 25 degrees left", lines);
        Assert.Contains("Speed: 239 knots", lines);
        Assert.Contains(lines, l => l.StartsWith("Pitch: 4."));
    }
}
