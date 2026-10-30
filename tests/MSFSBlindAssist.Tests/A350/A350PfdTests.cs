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
        A350Fixtures.Capture(name + ".json");

    [Fact]
    public void The_captain_pfd_reads_by_area()
    {
        var lines = A350Pfd.Read(Fixture("efis0_fd_stand"), 0);
        foreach (var l in new[]
        {
            "FMA engagement: 1 FD 2, white", "Selected speed: 100, cyan", "Altitude: 43 feet, green",
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

    // Review 2026-10-29 (critical): between the tape's visible window (y 177 to 585, ScalesBackground.png's altitude
    // column) and the held places, the symbol is drawn where the altitude puts it, hidden by the tape's clip: above,
    // inside the FMA strip; below, among the baro and the heading scale's numbers. Moved there it read "FMA approach:
    // 5000, magenta", "FMA approach: 400, cyan" (about 900 ft before every STD level-off), "Selected altitude: 40000
    // 40000", "QNH: 1013 4000" and "Selected heading: 900" (the real one dropped).
    private static A350DrawCapture Moved(A350DrawCapture cap, string text, float fromY, float toY, string? newText = null, A350Rgba? colour = null) =>
        cap with
        {
            Texts = cap.Texts.Select(t => t.Text.Trim() == text && Math.Abs(t.Y - fromY) < 1
                ? t with { Text = newText ?? t.Text, Y = toY, LocalY = t.LocalY + toY - fromY, Colour = colour ?? t.Colour } : t).ToList(),
        };

    [Theory]
    [InlineData(70f)]
    [InlineData(110f)]
    [InlineData(150f)]
    public void A_hidden_constraint_above_the_tape_reads_as_the_constraint(float y)
    {
        var lines = A350Pfd.Read(Moved(Fixture("efis0_takeoff_ready"), "40000", 38, y, "5000", new A350Rgba(1, 0, 1, 1)), 0);
        Assert.Contains("Altitude constraint: 5000, magenta", lines);
        Assert.Contains("Selected altitude: 40000, cyan", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("FMA approach", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100f)]
    [InlineData(150f)]
    [InlineData(170f)]
    public void A_hidden_selected_altitude_above_the_tape_reads_once(float y)
    {
        var lines = A350Pfd.Read(Moved(Fixture("efis0_takeoff_ready"), "40000", 38, y), 0);
        Assert.Equal("Selected altitude: 40000, cyan", Assert.Single(lines, l => l.Contains("40000")));
    }

    // Climbing through FL235 in STD with FL400 selected (live 2026-10-28): the tape's own selected altitude symbol is
    // still drawn out of range, held at the screen's top ("400" at x 646, y 38: the capture as taken) inside the FMA's
    // top row, where it read as the FMA's fourth column ("FMA approach: 400, cyan"); moved, it never does either.
    [Theory]
    [InlineData(38f)]
    [InlineData(60f)]
    [InlineData(90f)]
    [InlineData(120f)]
    [InlineData(160f)]
    public void A_hidden_selected_flight_level_above_the_tape_is_no_fma_column(float y)
    {
        var lines = A350Pfd.Read(Moved(Fixture("pfd_climb_std_fl400"), "400", 38, y), 0);
        Assert.Equal("Selected altitude: FL 400, cyan", Assert.Single(lines, l => l.Contains("400")));
        Assert.DoesNotContain(lines, l => l.StartsWith("FMA approach", StringComparison.Ordinal));
        Assert.Equal("FMA engagement: AP1; 1 FD 2; A/THR, white", lines.Single(l => l.StartsWith("FMA engagement", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(600f)]
    [InlineData(657f)]
    [InlineData(700f)]
    [InlineData(730f)]
    public void A_hidden_selected_altitude_below_the_tape_stays_out_of_the_baro(float y)
    {
        var lines = A350Pfd.Read(Moved(Fixture("efis0_ls_downwind_omdb"), "4000", 742, y), 0);
        Assert.Equal("Selected altitude: 4000, cyan", Assert.Single(lines, l => l.Contains("4000")));
        Assert.Contains("QNH: 1013, cyan", lines);
    }

    // In STD the baro shows no text, so the symbol passing it read as a baro of its own.
    [Theory]
    [InlineData(657f)]
    [InlineData(690f)]
    public void A_hidden_selected_flight_level_below_the_tape_is_no_baro(float y)
    {
        var cap = Moved(Moved(Moved(Fixture("pfd_climb_std_fl400"), "400", 38, y), "FL", 172, 615), "400", 172, 615);
        var lines = A350Pfd.Read(cap, 0);
        Assert.Equal("Selected altitude: FL 400, cyan", Assert.Single(lines, l => l.Contains("400")));
        Assert.DoesNotContain(lines, l => l.StartsWith("Baro", StringComparison.Ordinal));
    }

    // A three-digit symbol in the heading scale's band (a flight level in STD) read as the off-scale selected heading, and
    // the heading bug on the scale (031 at 6000 ft) was dropped for it.
    [Fact]
    public void A_hidden_selected_altitude_never_reads_as_the_selected_heading()
    {
        var lines = A350Pfd.Read(Moved(Fixture("efis0_level6000"), "6000", 389, 690, "900"), 0);
        Assert.Contains("Selected heading: 031, cyan", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Selected heading: 900", StringComparison.Ordinal));
    }

    // Review 2026-10-29: in STD the baro box holds no text but a sprite (PFDA350Elements.png at 954, 545, 66 x 36: "STD"
    // in cyan, boxed in yellow), so the PFD read no baro at all, where QNH reads "QNH: 1013, cyan".
    [Fact]
    public void Std_reads_as_the_baro()
    {
        Assert.Contains("Baro: STD, cyan in a yellow box", A350Pfd.Read(Fixture("efis0_climb_bi614"), 0));
        var qnh = A350Pfd.Read(Fixture("efis0_level6000"), 0);
        Assert.Contains("QNH: 1013, cyan", qnh);
        Assert.DoesNotContain(qnh, l => l.Contains("STD", StringComparison.Ordinal));
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

    // Review 2026-10-29 (readers minor 12): the drum turns amber below the BARO minimum, and only green drum digits were read,
    // so the altitude went unread right there; the line now says the drum's colour, as every PFD line says its own.
    [Fact]
    public void Below_the_minimum_the_altitude_reads_amber()
    {
        var climb = Fixture("efis0_climb");
        var amber = climb with
        {
            // The drum on the captain's PFD, which the capture draws 836 px in (its right half).
            Texts = climb.Texts.Select(t => A350Rgba.NameOf(t.Colour) == "green" && t.X - 836 is >= 605 and < 676 && t.Y is >= 330 and < 460
                ? t with { Colour = A350Rgba.Of("amber") } : t).ToList(),
        };
        Assert.Contains("Altitude: 2941 feet, amber", A350Pfd.Read(amber, 0));
    }

    // Review 2026-10-29 (readers minor 14): a sim value the draw code asked for and MSFSBA had not answered draws as 0, and a
    // baro of 0 was spoken ("QNH: 0, cyan", the ILS fixtures). No baro reads 0: it is not available.
    [Theory]
    [InlineData("efis0_ils_above")]
    [InlineData("efis0_ils_below")]
    [InlineData("efis0_ils_gs_off")]
    public void A_baro_of_zero_is_not_available(string fixture)
    {
        var lines = A350Pfd.Read(Fixture(fixture), 0);
        Assert.Contains("QNH: not available", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("QNH: 0", StringComparison.Ordinal));
    }

    [Fact]
    public void In_the_climb_the_pfd_reads_altitude_heading_and_vertical_speed()
    {
        var lines = A350Pfd.Read(Fixture("efis0_climb"), 0);
        Assert.Contains("Altitude: 2941 feet, green", lines);
        Assert.Contains("Heading: 031", lines);
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

    // The speed tape is SpeedTape.png (75 x 2285): its 10 kt ticks are 48 1/3 px apart, 30 kt at row 2283.59 up to 500 kt
    // at row 11.75, so it moves 145 px every 30 kt. Read at 4.875 px a knot from one live pair (240.2 kt), every speed away
    // from 240 read high: the stand's 30 kt floor as 32, an approach's 140 as 141. (This theory moves the tape by the same
    // 145/30 the reader uses, so it pins the constant; the evidence for it is the texture's ticks, VFE next reading 255 at
    // four speeds, and the speed target triangles below reading whole knots.)
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(140)]
    [InlineData(300)]
    [InlineData(340)]
    public void The_speed_tape_moves_145_px_every_30_knots(int knots)
    {
        var level = Fixture("efis0_level6000");   // 240.2 kt with the tape at -885.21 (live, the sim's airspeed)
        float shift = (float)((240.2 - knots) * 145 / 30);
        var lines = A350Pfd.Read(level with
        {
            Shapes = level.Shapes!.Select(s => Math.Abs(s.PatternH - 2285) < 2 && s.X0 >= 836
                ? s with { Y0 = s.Y0 - shift, Y1 = s.Y1 - shift, PatternY = s.PatternY - shift } : s).ToList(),
        }, 0);
        Assert.Contains(knots == 30 ? "Speed: 30 knots or less" : $"Speed: {knots} knots", lines);
    }

    // Green dot, S, F and VFE next are sprites on the speed tape (PFDA350Elements.png: the green circle at 976,719, F's
    // dash and letter at 887,724, S's under it at 887,757, the amber "=" at 1001,702), each marking its speed at its
    // middle, read off the tape texture itself. One approach's F reads the same at 159 and 185 kt; VFE next reads 255
    // clean at three speeds; one flight's green dot falls as it burns fuel (217.8, 217.6, 217.4).
    [Theory]
    [InlineData("efis0_climb", "Green dot: 218, green")]
    [InlineData("efis0_level6000", "Green dot: 218, green")]
    [InlineData("efis0_turn_left", "Green dot: 217, green")]
    [InlineData("efis0_ls_downwind_omdb", "Green dot: 186, green")]
    [InlineData("efis0_ils_above", "F speed: 137, green")]
    [InlineData("efis0_level6000", "VFE next: 255, amber")]
    [InlineData("efis0_turn_left", "VFE next: 255, amber")]
    [InlineData("efis0_ils_above", "VFE next: 186, amber")]
    [InlineData("efis0_ils_below", "VFE next: 186, amber")]
    public void The_characteristic_speeds_read_off_their_symbols(string fixture, string line)
    {
        Assert.Contains(line, A350Pfd.Read(Fixture(fixture), 0));
    }

    // The tape shows y 180 to 585 (ScalesBackground.png's speed window, about 42 kt either way): a symbol drawn outside it
    // is clipped, or parked at y 139.5 or 623.5 with no value at all (VFE next with flaps FULL reads "101" at 151 kt; there
    // is no next configuration). A sighted pilot sees none of them, so none is read (review 2026-10-29).
    [Theory]
    [InlineData("efis0_ils_below", "F speed")]               // 48 kt below, clipped
    [InlineData("efis0_ls_downwind_omdb", "VFE next")]       // 45 kt above, clipped
    [InlineData("efis0_climb_bi614", "VFE next")]            // 43 kt below, clipped
    [InlineData("efis0_climb_bi614", "Green dot")]           // parked at the foot
    [InlineData("efis0_ils_gs_off", "VFE next")]             // flaps FULL: parked, no value
    [InlineData("efis0_climb", "VFE next")]                  // parked at the top
    public void A_symbol_off_the_visible_tape_is_not_read(string fixture, string symbol)
    {
        Assert.DoesNotContain(A350Pfd.Read(Fixture(fixture), 0), l => l.StartsWith(symbol + ":", StringComparison.Ordinal));
    }

    // The speed target triangle (atlas 957,748) marks the FCU's whole-knot target, so it calibrates the tape apart from the
    // reader's own numbers: every one in the captures reads within 0.15 kt of a whole knot. On the scale it is read as the
    // pilot sees it, cyan selected, magenta managed; off the scale the aircraft writes the target as a number instead.
    [Theory]
    [InlineData("efis0_level6000", 240, "Selected speed: 240, cyan")]
    [InlineData("efis0_turn_left", 240, "Selected speed: 240, cyan")]
    [InlineData("efis0_ils_above", 160, "Selected speed: 160, cyan")]
    [InlineData("efis0_ils_below", 160, "Selected speed: 160, cyan")]
    [InlineData("efis0_ils_gs_off", 140, "Selected speed: 140, cyan")]
    [InlineData("efis0_climb_bi614", 300, "Speed target: 300, magenta")]
    [InlineData("efis0_ls_downwind_omdb", 210, "Speed target: 210, magenta")]
    public void The_speed_target_reads_its_whole_knot_off_the_tape(string fixture, int target, string line)
    {
        var capture = Fixture(fixture);
        var tape = capture.Shapes!.Single(s => Math.Abs(s.PatternH - 2285) < 2 && s.X0 >= 836);
        var triangle = capture.Shapes!.Single(s => s.Image > 1 && Math.Abs(s.X0 - s.PatternX - 957) < 1.5f
            && Math.Abs(s.Y0 - s.PatternY - 748) < 1.5f && s.X0 >= 836);
        Assert.InRange(A350Pfd.TapeKnotsAt(tape, (triangle.Y0 + triangle.Y1) / 2), target - 0.15, target + 0.15);
        var lines = A350Pfd.Read(capture, 0);
        Assert.Contains(line, lines);
        Assert.Single(lines, l => l.StartsWith("Selected speed", StringComparison.Ordinal) || l.StartsWith("Speed target", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tape_symbols_read_the_same_whatever_the_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var lines = A350Pfd.Read(Fixture("efis0_level6000"), 0);
            Assert.Contains("Green dot: 218, green", lines);
            Assert.Contains("VFE next: 255, amber", lines);
            Assert.Contains("Selected speed: 240, cyan", lines);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void S_reads_off_its_own_sprite()
    {
        // Not yet seen drawn (configuration 1): the atlas holds it under F, its dash at the same height in its box.
        var approach = Fixture("efis0_ils_above");
        var lines = A350Pfd.Read(approach with
        {
            Shapes = approach.Shapes!.Select(s => s.Image > 1 && Math.Abs(s.X0 - s.PatternX - 887) < 1.5f
                && Math.Abs(s.Y0 - s.PatternY - 724) < 1.5f ? s with { PatternY = s.PatternY - 33 } : s).ToList(),
        }, 0);
        Assert.Contains("S speed: 137, green", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("F speed", StringComparison.Ordinal));
    }

    [Fact]
    public void On_the_stand_the_tape_shows_none_of_them()
    {
        Assert.DoesNotContain(A350Pfd.Read(Fixture("efis0_takeoff_ready"), 0),
            l => l.StartsWith("Green dot", StringComparison.Ordinal) || l.StartsWith("S speed", StringComparison.Ordinal)
                || l.StartsWith("F speed", StringComparison.Ordinal));
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
        Assert.Contains("Heading: 018", lines);   // mid-turn towards 330
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
        // The magenta course is no speed target; the managed target here is the triangle's, 210.
        Assert.DoesNotContain(lines, l => (l.StartsWith("Speed target") || l.StartsWith("Selected speed")) && l.Contains("119"));
        Assert.Contains("Speed target: 210, magenta", lines);
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

    // Review 2026-10-29 (minors 4 and 5): a magenta text in the LS block's area but on none of its three lines, and a cyan
    // number in the PFD's lower right outside the held selected-altitude symbol's place, were taken and never read.
    [Fact]
    public void No_text_near_the_ls_block_or_the_altitude_tape_is_dropped_silently()
    {
        var cap = Fixture("efis0_ls_downwind_omdb");
        var texts = cap.Texts.ToList();
        texts.Add(new DrawnText("XYZ", 836 + 37, 703, 37, 703, 1, new A350Rgba(1, 0.467f, 0.914f, 1)));
        texts.Add(new DrawnText("25", 836 + 690, 760, 690, 760, 1, new A350Rgba(0, 1, 1, 1)));
        var lines = A350Pfd.Read(cap with { Texts = texts }, 0);
        Assert.Contains(lines, l => l.Contains("XYZ", StringComparison.Ordinal));
        Assert.Contains("25, cyan", lines);
        Assert.Contains("LS: IDBL 110.10, 3.0 NM, magenta", lines);
        Assert.Contains("Selected altitude: 4000, cyan", lines);
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
    [InlineData("efis0_level6000", "Selected heading: 031, cyan", "Track: 034, green")]
    [InlineData("efis0_climb", "Selected heading: 031, cyan", "Track: 033, green")]
    [InlineData("efis0_turn_left", "Selected heading: 330, cyan", "Track: 024, green")]
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
        Assert.Contains("Track: 037, green", lines);
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
    // Final review 2026-10-30: the PFD's altitude was formatted in the PC's culture, so below sea level (EHAM, about -11 ft)
    // sv-SE wrote U+2212. The tape's label 000 at the window and the drum's tens 20 about 31 ft above it read -11 ft.
    [Fact]
    public void An_altitude_below_sea_level_reads_with_a_plain_minus_in_any_culture()
    {
        var cap = Fixture("efis0_takeoff_ready");
        bool Tape(DrawnText t) => t.X - 836 is >= 585 and < 610 or >= 658 and < 676;
        var below = cap with
        {
            Texts = cap.Texts.Where(t => !Tape(t)).Concat(new[]
            {
                new DrawnText("000", 836 + 595, 389, 0, 0, 0, new A350Rgba(1, 1, 1, 1)),
                new DrawnText("20", 836 + 665, 389 - 31 * 21.75f / 20, 0, 0, 0, new A350Rgba(0, 1, 0, 1)),
            }).ToList(),
        };
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            Assert.Contains("Altitude: -11 feet, green", A350Pfd.Read(below, 0));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
    // METRIC ALT on (live 2026-10-30 at VCBI, the field 81 ft): the selected altitude in metres above the altitude tape ("30M",
    // cyan; "1520M" with the FCU at 5000 ft) and the altitude in metres in the box under it ("20", green), read by name, where
    // they read "30M, cyan" and "20, green" as left over.
    [Fact]
    public void Metric_altitude_boxes_are_read_by_name()
    {
        var lines = A350Pfd.Read(Fixture("efis0_metric_alt"), 0);
        Assert.Contains("Selected altitude: 100, cyan", lines);
        Assert.Contains("Selected altitude in metres: 30, cyan", lines);
        Assert.Contains("Altitude: 81 feet, green", lines);
        Assert.Contains("Altitude in metres: 20, green", lines);
        Assert.DoesNotContain(lines, l => l.Contains("30M") || l == "20, green");
        Assert.True(lines.ToList().IndexOf("Selected altitude in metres: 30, cyan") == lines.ToList().IndexOf("Selected altitude: 100, cyan") + 1);
        Assert.True(lines.ToList().IndexOf("Altitude in metres: 20, green") == lines.ToList().IndexOf("Altitude: 81 feet, green") + 1);
    }

    // Landed at OMDB after an autoland (live 2026-10-30): the PFD clips its altitude tape (setClipArea -> nvgScissor), and
    // the tape's labels ("010" at y 50) and the selected altitude held at the screen's top ("3000", white, y 38) are drawn
    // beyond the clip, in the FMA strip over its fourth column. On final the column was re-read four times in 15 s as the
    // labels scrolled ("LAND3; DUAL; 3000", "LAND3 3000; DUAL"...). A text drawn outside the clip is not on the screen.
    [Fact]
    public void Texts_drawn_outside_the_clip_are_not_read()
    {
        var cap = A350Fixtures.Capture("efis0_omdb_landed_clipped.json");
        Assert.Contains(cap.Texts, t => t.Clipped && t.Text == "3000");
        var lines = A350Pfd.Read(cap, 0);
        Assert.Contains("FMA approach: LAND3; DUAL, white", lines);
        // The selected altitude's readout above the tape (x 688, y 172) is on the screen: white here, after the autoland.
        Assert.Contains("Selected altitude: 3000, white", lines);
        Assert.Single(lines, l => l.Contains("3000"));
        Assert.DoesNotContain(lines, l => l.Contains("010"));
    }
}
