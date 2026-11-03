using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The other displays read by name, from live draws on the stand at VCBI (2026-10-27): engines and APU running,
// IRs aligned, no flight plan; heading 219, 12.8 ft indicated.
public class A350GroundDisplaysTests
{
    // Live 2026-10-30, on the arrival: the vertical display's waypoints read by rows of names, with nothing of where they lie. Each stands
    // beside its symbol on the two scales; the MSA is a line at its height with no figure.
    [Fact]
    public void The_vertical_displays_waypoints_say_where_they_lie_on_its_scales()
    {
        var lines = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_vd_arrival.json"), 0);
        Assert.Contains("Vertical display: DB423, white: 6.9 NM, 8000 feet", lines);
        Assert.Contains("Vertical display: DB407, green: 19 NM, 6000 feet", lines);
        Assert.Contains("Vertical display: SOLIL, green: 39 NM, 4500 feet", lines);
        Assert.DoesNotContain("DB408 DB403, green", lines);
        Assert.Contains("MSA: about 3800 feet, magenta", lines);
    }

    // Review 2026-10-31 (C1): a range map's name drawn under the map, behind the vertical display, read as one of its waypoints with a
    // made-up altitude: MCT, the Muscat VOR, at 24600 feet in the cruise at FL300; BASUR at 32200 beside the display's own TVM at 30000;
    // MIN at 13200 on the arrival. The map's names are drawn before the display's range labels, its own after them.
    [Theory]
    [InlineData("efis0_cruise_wxt", "MCT", "Vertical display: VELOD, white: 7.9 NM, 30000 feet")]
    [InlineData("efis0_time_marker", "BASUR", "Vertical display: TVM, white: 37 NM, 30000 feet")]
    [InlineData("efis0_arc40_omdb_label", "MIN", "Vertical display: DB403, white: 6.2 NM, 6400 feet")]
    public void A_map_name_behind_the_vd_gets_no_vd_place(string fixture, string mapName, string own)
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture(fixture + ".json"), 0);
        Assert.DoesNotContain(nd, l => l.StartsWith($"Vertical display: {mapName},", StringComparison.Ordinal));
        Assert.Contains(own, nd);
    }

    // Review 2026-10-31 (C1): the display's runway label, the airport's name over the runway's ident (VCBI over 22, on the stand), read as
    // a waypoint's name beside its symbol, 1000 ft over a field near sea level. It stands over the left end of the runway's two white lines.
    [Fact]
    public void The_runway_label_is_not_placed_as_a_waypoint()
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_takeoff_ready.json"), 0);
        Assert.DoesNotContain("Vertical display: VCBI, white: 1.2 NM, 1000 feet", nd);
        Assert.Contains("Vertical display: VCBI 22, white: 1.1 NM, 0 feet", nd);
        Assert.Contains("Vertical display: DER22, white: 2.9 NM, 100 feet", nd);
        // OMDB over 12L on the arrival, clipped past 40 NM, as it reads 100 px nearer with its lines: a lettered ident is no waypoint either.
        var arrival = A350Fixtures.Capture("efis0_arc40_omdb_label.json");
        var nearer = arrival with
        {
            Texts = arrival.Texts.Select(t => t.Text.Trim() is "OMDB" or "12L" && t.Y > 900 ? t with { X = t.X - 100, Clipped = false } : t).ToList(),
            Shapes = arrival.Shapes!.Select(s => s.Kind == "stroke" && s.X0 > 836 + 720 && s.Y0 > 980 ? s with { X0 = s.X0 - 100, X1 = s.X1 - 100 } : s).ToList(),
        };
        var lines = A350GroundDisplays.ReadNd(nearer, 0);
        Assert.Contains("Vertical display: OMDB 12L, white: 34 NM, 100 feet", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Vertical display: 12L", StringComparison.Ordinal));
    }

    // Task 2A review (Minor 3): the runway's place took one of its two white lines as both, its middle then 5 px off the field's height:
    // VCBI read 100 feet with only the upper line, -100 with only the lower (about 400 ft off at 40 NM). A draw strokes some lines twice
    // (the path, in arc40 and on ROSE LS), so one line drawn twice is still one. Both lines, or no place.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void The_runways_place_needs_both_its_lines(bool keepUpper, bool drawnTwice)
    {
        var capture = A350Fixtures.Capture("efis0_takeoff_ready.json");
        var both = capture.Shapes!.Where(s => s.Kind == "stroke" && A350Rgba.NameOf(s.Colour) == "white" && s.Y1 - s.Y0 <= 4 && Math.Abs(s.X0 - 836 - 232.1f) < 1)
            .OrderBy(s => s.Y0).ToList();
        Assert.Equal(2, both.Count);
        var (kept, gone) = keepUpper ? (both[0], both[1]) : (both[1], both[0]);
        var shapes = capture.Shapes!.Where(s => s != gone).ToList();
        if (drawnTwice) shapes.Add(kept);
        var nd = A350GroundDisplays.ReadNd(capture with { Shapes = shapes }, 0);
        Assert.Contains("Vertical display: VCBI 22, white", nd);
    }

    // Review 2026-10-31 (M1): a name the clip hides, its symbol past the last range label, read with a place nobody sees: ODLAL at 42 NM on
    // the 40 NM display (the arrival), SOLIL past 10 NM on ROSE LS, OMDB and 12L at 42 NM. A capture from before the clip was tracked reads
    // each text unclipped: ODLAL still lies past 40 NM. And a name the clip hides inside the range, as one under the display's window.
    // Task 2A review (Minor 1): what the clip hides is not spoken at all, as on the PFD; it read as a row. ODLAL is said as often as the
    // map draws it, once (y 136); SOLIL, drawn only past the clip, never; OMDB and 12L once each, the range map's plan runway label.
    [Fact]
    public void A_clipped_or_out_of_range_vd_name_is_not_read()
    {
        static bool Vd(string line, string name) => line.StartsWith($"Vertical display: {name},", StringComparison.Ordinal);
        var arrival = A350Fixtures.Capture("efis0_vd_arrival.json");
        var nd = A350GroundDisplays.ReadNd(arrival, 0);
        Assert.DoesNotContain(nd, l => Vd(l, "ODLAL"));
        Assert.Equal("ODLAL, green", Assert.Single(nd, l => l.Contains("ODLAL", StringComparison.Ordinal)));
        var unclipped = arrival with { Texts = arrival.Texts.Select(t => t with { Clipped = false }).ToList() };
        Assert.DoesNotContain(A350GroundDisplays.ReadNd(unclipped, 0), l => Vd(l, "ODLAL"));
        var hidden = arrival with { Texts = arrival.Texts.Select(t => t.Text.Trim() == "DB408" && t.Y > 800 ? t with { Clipped = true } : t).ToList() };
        Assert.DoesNotContain(A350GroundDisplays.ReadNd(hidden, 0), l => Vd(l, "DB408"));
        Assert.DoesNotContain(A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_ls_rose_ils2.json"), 0), l => l.Contains("SOLIL", StringComparison.Ordinal));
        var arc40 = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_arc40_omdb_label.json"), 0);
        Assert.DoesNotContain(arc40, l => Vd(l, "OMDB") || Vd(l, "12L"));
        Assert.Single(arc40, l => l == "OMDB, white");
        Assert.Single(arc40, l => l == "12L, white");
    }

    // Review 2026-10-31 (M1): on the arrival a magenta D stands in a circle of its own on the path (likely the decel point), and read as a
    // name beside a symbol, 600 ft over its circle: "Vertical display: D, magenta: 32 NM, 3400 feet". A lone letter is a mark, no name, as on
    // the range map (A350NdPage); red and amber are the ND's flags.
    [Fact]
    public void A_mark_or_a_flag_in_the_vd_is_no_waypoint()
    {
        Assert.DoesNotContain(A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_arc40_omdb_label.json"), 0),
            l => l.StartsWith("Vertical display: D,", StringComparison.Ordinal));
        var arrival = A350Fixtures.Capture("efis0_vd_arrival.json");
        var amber = arrival with { Texts = arrival.Texts.Select(t => t.Text.Trim() == "DB408" && t.Y > 800 ? t with { Colour = A350Rgba.Of("amber") } : t).ToList() };
        var nd = A350GroundDisplays.ReadNd(amber, 0);
        Assert.DoesNotContain(nd, l => l.StartsWith("Vertical display: DB408,", StringComparison.Ordinal));
        Assert.Contains("Vertical display: DB403, green: 34 NM, 4900 feet", nd);
    }

    // Review 2026-10-31 (I2): the vertical display's figures keep their point in a comma-decimal culture: a distance with a tenth ("6.9 NM";
    // "5.9 NM" on the 10 NM range, labelled 0 2.5 5 7.5 10). CI runs under en-US, so nothing else would catch a culture leak.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_vd_reads_the_same_under_de_DE_and_sv_SE(string culture)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var lines = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_vd_arrival.json"), 0);
            Assert.Contains("Vertical display: DB423, white: 6.9 NM, 8000 feet", lines);
            Assert.Contains("Vertical display: DB407, green: 19 NM, 6000 feet", lines);
            Assert.Contains("Vertical display: SOLIL, green: 39 NM, 4500 feet", lines);
            Assert.Contains("MSA: about 3800 feet, magenta", lines);
            Assert.Contains("Vertical display: DB403, white: 5.9 NM, 6200 feet", A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_ls_rose_ils2.json"), 0));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    // Final review (recommendation 2): the runway's line takes its place from its two white lines, not from a name beside a symbol, and its
    // distance on the stand at VCBI has a tenth too.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_vds_runway_line_reads_the_same_under_de_DE_and_sv_SE(string culture)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            Assert.Contains("Vertical display: VCBI 22, white: 1.1 NM, 0 feet", A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_takeoff_ready.json"), 0));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    // Task 2A review (Minor 2): the scale reads only its end labels, none of them a decimal in any capture, so nothing pinned how a label
    // parses (another program's number, CORE-17). With the 10 NM range's "10" gone, "7.5" is the last label: parsed in the PC's culture it
    // reads 75 under de-DE and nothing under sv-SE, and the scale is lost.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_decimal_last_range_label_reads_the_same_under_de_DE_and_sv_SE(string culture)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var capture = A350Fixtures.Capture("efis0_ls_rose_ils2.json");
            var nd = A350GroundDisplays.ReadNd(capture with { Texts = capture.Texts.Where(t => !(t.Text.Trim() == "10" && Math.Abs(t.Y - 789) < 1)).ToList() }, 0);
            Assert.Contains("Vertical display range: 0 2.5 5 7.5, cyan", nd);
            Assert.Contains("Vertical display: DB403, white: 5.9 NM, 6200 feet", nd);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    // Final review: the ND's status display got none of the traffic line: no way it moves, and with FLT ID on, orphan lines.
    [Fact]
    public void The_nds_status_display_says_what_a_target_is_doing_and_who_it_is()
    {
        var lines = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_traffic_flight_id_above.json"), 0);
        Assert.Contains("Traffic IVAO, 1200 feet above, descending, speed 337, wake M, white", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("IVAO", StringComparison.Ordinal) || l.StartsWith("337", StringComparison.Ordinal));
        Assert.Contains("Traffic 900 feet above, climbing, white", A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_traffic_climbing.json"), 0));
    }

    private static A350DrawCapture Fixture(string name) =>
        A350Fixtures.Capture("ground_" + name + ".json");

    [Fact]
    public void The_nd_reads_its_corners_messages_and_vertical_display()
    {
        var nd = A350GroundDisplays.ReadNd(Fixture("EFIS_0"), 0);
        Assert.Contains("Ground speed: 0, green", nd);
        Assert.Contains("True airspeed: ---, green", nd);
        Assert.Contains("Wind: --- ---, green", nd);
        Assert.Contains("To waypoint: PPOS, white", nd);
        Assert.Contains("TCAS STBY, white", nd);
        Assert.Contains("NAV PRIMARY, white", nd);
        Assert.Contains("Vertical display range: 0 1.25 2.5 3.75 5, cyan", nd);
        Assert.Contains("Vertical display altitudes: 1500 1000 500 0, white", nd);
        Assert.DoesNotContain(nd, l => l.Contains("MTL"));   // drawn off the screen
    }

    // On the stand with the departure set (live 2026-10-30): ND::DrawApproachInfo draws the procedure at the top (x 270, y
    // 35: the SID, BASU1D) and ND::DrawChrono the chronometer at the bottom left (x 57 and 114, y 660: minutes and seconds,
    // hours and minutes past the hour, through convertTimeClock); both read as left-over "BASU1D, green" and "28 23, green".
    [Fact]
    public void The_nd_names_its_procedure_and_its_chrono()
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_metric_alt.json"), 0);
        Assert.Contains("Procedure: BASU1D, green", nd);
        Assert.Contains("Chrono: 28 23, green", nd);
        Assert.DoesNotContain("BASU1D, green", nd);
        Assert.DoesNotContain("28 23, green", nd);
    }

    // Live in the climb on BASU1D (2026-10-28): the ND's TO waypoint block, top right, is the name (white), the track
    // to it (green, x 759), its distance a line under ("6." and a smaller "2", then a cyan NM) and the ETA under that.
    // The distance read as a line of its own split at its point ("6., green; 2, green; NM, cyan"), the ETA as another.
    [Fact]
    public void The_nd_reads_its_to_waypoint_block_as_one_line()
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_climb_bi614.json"), 0);
        Assert.Contains("To waypoint: BI614, white; track 296°, green; 6.2 NM, green; ETA 07:20, green", nd);
        Assert.DoesNotContain(nd, l => l.StartsWith("6.", StringComparison.Ordinal) || l.StartsWith("07:20", StringComparison.Ordinal));
    }

    // Live on the arrival to OMDB (2026-10-30, ROSE LS): the same corner says which receiver feeds the ND ("ILS2", white), its
    // frequency ("110", ".", "10"), the course under it ("CRS", "119") and the ident under that. It read as a TO waypoint.
    [Fact]
    public void The_nd_reads_its_ls_block_by_name()
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_ls_rose_ils2.json"), 0);
        Assert.Contains("Receiver: ILS2, 110.10, course 119, IDBL, magenta", nd);
        Assert.DoesNotContain(nd, l => l.StartsWith("To waypoint", StringComparison.Ordinal) || l == "CRS, white");
    }

    // Live on the arrival to OMDB at 40 NM ARC (2026-10-30): the plan's runway on a range map is white at local 0,0 ("OMDB" over
    // "12L"), the airport map's own transform. It read as "Taxiways: OMDB, 12L, white", and the TO waypoint's name as the airport.
    [Fact]
    public void A_range_map_is_no_airport_map()
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_arc40_omdb_label.json"), 0);
        Assert.DoesNotContain(nd, l => l.StartsWith("Taxiways:", StringComparison.Ordinal) || l.StartsWith("Airport:", StringComparison.Ordinal));
        Assert.Contains(nd, l => l.StartsWith("To waypoint: DB403, white; track", StringComparison.Ordinal));
    }

    // Live in the cruise at FL300 (2026-10-30): the radar's mode, top right of the map (x 781, y 622, "WX+T"), read as a name on the map,
    // and the vertical display's top label (y 773) as a line of its own, "40000, white".
    [Fact]
    public void The_nd_reads_its_radar_mode_and_the_vertical_displays_top_label()
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture("efis0_cruise_wxt.json"), 0);
        Assert.Contains("Radar mode: WX+T, green", nd);
        Assert.Contains("Vertical display altitudes: 40000 35000 30000 25000, white", nd);
        Assert.DoesNotContain("40000, white", nd);
    }

    // The vertical display (live 2026-10-28): the selected altitude beyond its altitude range is a cyan value at its top
    // left (x 105, y 786; "FL 400" in STD) or bottom left (y 1010). It read inside the range line ("40000 0 2.5 5 7.5 10")
    // or as the lowest altitude label, or bare; the scale's "FL" (white, x 58) read as a line of its own, and the safe
    // altitude (MSA or MORA, white, its value above it) as two.
    [Theory]
    [InlineData("efis0_takeoff_ready", "Vertical display selected altitude: 40000, cyan", "Vertical display range: 0 2.5 5 7.5 10, cyan", "Vertical display altitudes: 3000 2000 1000 0, white")]
    [InlineData("efis0_climb_bi614", "Vertical display selected altitude: FL 400, cyan", "Vertical display range: 0 2.5 5 7.5 10, cyan", "Vertical display altitudes: FL 190 180 170 160, white")]
    [InlineData("efis0_ls_downwind_omdb", "Vertical display selected altitude: 4000, cyan", "Vertical display range: 0 1.25 2.5 3.75 5, cyan", "Vertical display altitudes: 6000 5500 5000 4500, white")]
    public void The_vertical_display_reads_its_selected_altitude_apart(string fixture, string selected, string range, string altitudes)
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture(fixture + ".json"), 0);
        Assert.Contains(selected, nd);
        Assert.Contains(range, nd);
        Assert.Contains(altitudes, nd);
        Assert.DoesNotContain(nd, l => l is "FL, white" or "FL 400, cyan" or "4000, cyan");
    }

    // Live at OMDB on the ground (2026-10-30, ROSE LS): the name stood above its value and they read apart, "MSA, magenta", "3 8, magenta".
    [Fact]
    public void The_safe_altitude_reads_with_its_name_above_its_value()
    {
        var capture = A350Fixtures.Capture("efis0_ls_rose_ils2.json");
        var nd = A350GroundDisplays.ReadNd(capture with { Texts = capture.Texts.Select(t => t.Text.Trim() == "MSA" ? t with { Y = 962 } : t).ToList() }, 0);
        Assert.Contains("MSA: 38, magenta", nd);
    }

    // Review 2026-10-31 (I1): the MSA's name stands about 8.5 px over its line, and was taken for the MSA only low in the display (y 900
    // on). Higher up it read as a waypoint: on the ground at OMDB (the line at y 882 on a 0 to 6000 ft scale, the name at 874) "Vertical
    // display: MSA, magenta: 0 NM, 4800 feet"; on the stand at VCBI (the line at the display's top, the name at 792) "MSA, magenta".
    [Theory]
    [InlineData("efis0_omdb_landed_clipped", "MSA: about 3800 feet, magenta")]
    [InlineData("efis0_metric_alt", "MSA: about 3500 feet, magenta")]
    public void The_msa_is_read_from_its_line_anywhere_in_the_vd(string fixture, string msa)
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture(fixture + ".json"), 0);
        Assert.Contains(msa, nd);
        Assert.DoesNotContain(nd, l => l.StartsWith("Vertical display: MSA", StringComparison.Ordinal) || l == "MSA, magenta");
    }

    // Task 2A review (Important 1): the MSA's figure was any all-digit text within 40 px over or under its name and 50 px beside it, in
    // any colour, taken before its line and before the runway's pairing. On the stand at VCBI at 40 NM the MSA's name stands about 37 px
    // over the runway's ident "22", 6.6 px left of it (its line starts with the runway: metric_alt's from x 232.7, the name at 236.0), so
    // it read "MSA: 22, white", and VCBI, its ident gone, a waypoint's place 1000 ft up. Here on takeoff_ready's own 10 NM scale, the
    // name drawn among the display's own texts (after the ident, before the PFD's); `line` is a magenta stroke placed from the name, and
    // `figure` adds a figure in the name's colour where the corner's stands over its name ("3" and "8", 28 px up, 12 and 28 px right).
    private static A350DrawCapture StandWithMsaOverTheIdent(Func<DrawnText, DrawnShape>? line, bool shapesRecorded = true, bool figure = false)
    {
        var capture = A350Fixtures.Capture("efis0_takeoff_ready.json");
        var ident = capture.Texts.First(t => t.Text.Trim() == "22" && t.Y > 900);
        var name = ident with { Text = "MSA", X = ident.X - 6.6f, Y = ident.Y - 37, LocalX = ident.LocalX - 6.6f, LocalY = ident.LocalY - 37, Colour = A350Rgba.Of("magenta") };
        var texts = capture.Texts.ToList();
        var added = new List<DrawnText> { name };
        if (figure)
            added.AddRange(new[] { ("3", 12f), ("8", 28f) }.Select(d => name with { Text = d.Item1, X = name.X + d.Item2, Y = name.Y - 28, LocalX = name.LocalX + d.Item2, LocalY = name.LocalY - 28 }));
        texts.InsertRange(texts.IndexOf(ident) + 1, added);
        var shapes = line == null ? capture.Shapes! : capture.Shapes!.Append(line(name)).ToList();
        return capture with { Texts = texts, Shapes = shapesRecorded ? shapes : null };
    }

    // Its line, level, 8 px under its name (its middle 908 ft on this scale), wins over a figure by it, in its colour or not.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_msa_takes_its_line_never_a_runway_ident(bool figureInItsColour)
    {
        var nd = A350GroundDisplays.ReadNd(StandWithMsaOverTheIdent(n => new DrawnShape("stroke", n.X - 3.3f, n.Y + 6, n.X + 400, n.Y + 10, 0, A350Rgba.Of("magenta")),
            figure: figureInItsColour), 0);
        Assert.Contains("MSA: about 900 feet, magenta", nd);
        Assert.Contains("Vertical display: VCBI 22, white: 1.1 NM, 0 feet", nd);
    }

    [Fact]
    public void With_no_line_the_msa_takes_no_figure_in_another_colour()
    {
        var nd = A350GroundDisplays.ReadNd(StandWithMsaOverTheIdent(null, shapesRecorded: false), 0);
        Assert.Contains("MSA, magenta", nd);
        Assert.DoesNotContain(nd, l => l.StartsWith("MSA: ", StringComparison.Ordinal));
        Assert.Contains("Vertical display: VCBI 22, white", nd);
    }

    // Task 2A review (Minor 4): the MSA's line was any stroke whose box passed, so a magenta step drawn as one path (two levels and the
    // climb between them, 40 px tall) read as a line at its middle. Every MSA line drawn in the captures is a level stroke 4 px tall.
    [Fact]
    public void A_tall_magenta_stroke_by_the_msa_is_no_line_of_its()
    {
        var nd = A350GroundDisplays.ReadNd(StandWithMsaOverTheIdent(n => new DrawnShape("stroke", n.X - 3.3f, n.Y - 30, n.X + 400, n.Y + 10, 0, A350Rgba.Of("magenta"))), 0);
        Assert.Contains("MSA, magenta", nd);
        Assert.DoesNotContain(nd, l => l.StartsWith("MSA: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("efis0_ls_downwind_omdb", "MSA: 38, magenta")]
    [InlineData("pfd_climb_std_fl400", "MORA: 16, magenta")]
    public void The_vertical_displays_safe_altitude_reads_with_its_name(string fixture, string safe)
    {
        var nd = A350GroundDisplays.ReadNd(A350Fixtures.Capture(fixture + ".json"), 0);
        Assert.Contains(safe, nd);
        Assert.DoesNotContain(nd, l => l is "MSA, white" or "MORA, white" or "3 8, white" or "1 6, white");
    }

    [Fact]
    public void The_fcu_reads_its_windows_and_both_baros()
    {
        var fcu = A350GroundDisplays.ReadFcu(Fixture("FCU"));
        Assert.Equal(new[]
        {
            "Speed: 100 KT, white", "Heading: 218, white", "Altitude: 100, white", "Vertical speed: ----, white",
            "Captain baro: QNH 1013, white", "First officer baro: QNH 1013, white",
        }, fcu);
    }

    // The FCU's altitude window draws five digits ("00100") and the V/S window its hundreds' zeros small, as "oo" (live
    // 2026-10-28: "+00oo"): they read as the numbers they show, as the FCU windows say them ("V/S +1200", "0").
    [Theory]
    [InlineData("+12oo", "Vertical speed: +1200, white")]
    [InlineData("-07oo", "Vertical speed: -700, white")]
    [InlineData("+00oo", "Vertical speed: 0, white")]
    public void The_fcu_reads_its_vertical_speed_as_a_number(string drawn, string read)
    {
        var cap = Fixture("FCU");
        var fcu = A350GroundDisplays.ReadFcu(cap with { Texts = cap.Texts.Select(t => t.Text == " ----" ? t with { Text = drawn } : t).ToList() });
        Assert.Contains(read, fcu);
        Assert.Contains("Altitude: 100, white", fcu);
    }

    // sv-SE's negative sign is U+2212: the V/S reads with a plain minus, as the FCU windows say it.
    [Fact]
    public void The_fcu_vertical_speed_is_invariant_under_a_swedish_culture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var cap = Fixture("FCU");
            Assert.Contains("Vertical speed: -700, white",
                A350GroundDisplays.ReadFcu(cap with { Texts = cap.Texts.Select(t => t.Text == " ----" ? t with { Text = "-07oo" } : t).ToList() }));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void An_rmp_reads_each_radio_active_and_standby()
    {
        var rmp = A350GroundDisplays.ReadRmp(Fixture("RMP_0"));
        Assert.Contains("VHF1: active 122.800, white; standby 123.450, cyan", rmp);
        Assert.Contains("VHF2: active 121.500, white; standby 124.850, white", rmp);
        Assert.Contains("VHF3: active DATA, white; standby 121.500, white", rmp);
        Assert.Contains("SQUAWK : 2000 - STBY, white", rmp);
    }

    // The overhead BAT display: seven-segment digits with the decimal point drawn apart (live: BAT 1 selected).
    // Review 2026-10-29 (readers minor 14): no baro reads 0; drawn 0 it is a sim value MSFSBA had not answered yet.
    [Fact]
    public void A_baro_of_zero_on_the_fcu_or_the_standby_display_is_not_available()
    {
        var fcu = Fixture("FCU");
        var zero = A350GroundDisplays.ReadFcu(fcu with { Texts = fcu.Texts.Select(t => t.Text.Trim() == "1013" ? t with { Text = "0" } : t).ToList() });
        Assert.Contains("Captain baro: not available", zero);
        Assert.Contains("First officer baro: not available", zero);
        var sfd = Fixture("SFD");
        var standby = A350GroundDisplays.ReadSfd(sfd with { Texts = sfd.Texts.Select(t => t.Text.Trim() == "1013" ? t with { Text = "0" } : t).ToList() });
        Assert.Contains("Baro: not available", standby);
        Assert.DoesNotContain("Baro: 0, cyan", standby);
    }

    [Fact]
    public void The_bat_display_reads_the_selected_battery_voltage()
    {
        Assert.Equal(new[] { "Battery voltage: 28.2 V, grey" }, A350GroundDisplays.ReadBat(Fixture("BAT")));
        Assert.Equal(new[] { "Battery voltage display: blank" },
            A350GroundDisplays.ReadBat(new A350DrawCapture("BAT", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1)));
    }

    [Fact]
    public void The_standby_displays_read_baro_heading_and_position()
    {
        var sfd = A350GroundDisplays.ReadSfd(Fixture("SFD"));
        Assert.Contains("Baro: 1013, cyan", sfd);
        Assert.Contains("Heading: 220", sfd);
        Assert.Contains("Position: N07 10.6 E079 53.1, green", sfd);
        Assert.Contains("Position source: GPIR3, white", sfd);
        var snd = A350GroundDisplays.ReadSnd(Fixture("SND"));
        Assert.Contains("Ground speed: 0, green", snd);
        Assert.Contains("Position: N07 10.6 E079 53.1, green", snd);
        Assert.Contains("Heading source: IR3, white", snd);
    }

    // Review 2026-10-29: the SFD dropped its speed and altitude, so Alt+I with a failed PFD gave baro, heading and position
    // only. They are its tapes, read as the PFD's: the labels nearest the window's text line (y 132.5); speed labels every
    // 20 kt 44 px apart (x 42), the altitude tape's hundreds 70 px per 500 ft (x 243), the drum's tens 26 px per 20 ft
    // (x 259, green). On the stand the speed tape rests on its 30 kt floor and the drum shows 70 ft (the PFD's 71 from the
    // same copy).
    [Fact]
    public void The_standby_flight_display_reads_its_speed_and_altitude()
    {
        var sfd = A350GroundDisplays.ReadSfd(Fixture("SFD"));
        Assert.Equal("Speed: 30 knots or less", sfd[0]);
        Assert.Equal("Altitude: 70 feet", sfd[1]);
    }

    // The same draw with its tapes relabelled as at 230 kt and about 5,070 ft: a label above the window is the faster or
    // higher one.
    [Fact]
    public void In_flight_the_standby_tapes_read_from_their_labels()
    {
        var cap = Fixture("SFD");
        var relabel = new Dictionary<(string, float), string>
        {
            [("40", 110f)] = "240", [("60", 66f)] = "260", [("80", 22f)] = "280",
            [("005", 212.8f)] = "045", [("000", 142.8f)] = "050", [("005", 72.8f)] = "055",
        };
        var texts = cap.Texts.Select(t => relabel.TryGetValue((t.Text.Trim(), MathF.Round(t.Y, 1)), out var n) ? t with { Text = n } : t).ToList();
        var sfd = A350GroundDisplays.ReadSfd(cap with { Texts = texts });
        Assert.Contains("Speed: 230 knots", sfd);
        Assert.Contains("Altitude: 5070 feet", sfd);
    }

    // Plan 08's flight (2026-11-03, VCBI circuit): the SFD's attitude is its pitch ladder, labels every 10 degrees 49 px apart, each 8 px
    // under its line, the ladder turned about the aircraft symbol by the bank. The sim's own attitude, logged beside each capture: climbing
    // 8.29 up, 2.32 right; level at FL200 1.20 up, wings level; turning 4.67 up, 28.35 left; turning 1.34 up, 24.73 right.
    [Theory]
    [InlineData("p08_sfd_climb", "Pitch: 8.3 degrees up", "Bank: 2 degrees right")]
    [InlineData("p08_sfd_level", "Pitch: 1.2 degrees up", "Bank: wings level")]
    [InlineData("p08_sfd_turn_left", "Pitch: 4.7 degrees up", "Bank: 28 degrees left")]
    [InlineData("p08_sfd_turn_right", "Pitch: 1.3 degrees up", "Bank: 25 degrees right")]
    public void The_standby_flight_display_reads_pitch_and_bank_from_its_ladder(string fixture, string pitch, string bank)
    {
        var sfd = A350GroundDisplays.ReadSfd(A350Fixtures.Capture(fixture + ".json"));
        Assert.Equal(new[] { pitch, bank }, sfd.Skip(2).Take(2));
    }

    // In the right turn the ladder's lower 20 crossed the heading scale's row (165.7, 239.0; the scale's 32 at 127.2 still nearer the
    // index at 145, live heading 328). The same draw with that 20 at x 150, as at a little less bank: it is no heading label.
    [Fact]
    public void A_ladder_label_on_the_heading_row_is_no_heading_label()
    {
        var cap = A350Fixtures.Capture("p08_sfd_turn_right.json");
        Assert.Contains("Heading: 328", A350GroundDisplays.ReadSfd(cap));
        var nearer = cap with { Texts = cap.Texts.Select(t => t.Text == "20" && MathF.Abs(t.X - 165.7f) < 0.1f ? t with { X = 150 } : t).ToList() };
        Assert.Contains("Heading: 328", A350GroundDisplays.ReadSfd(nearer));
    }

    // On the stand (sim 0.29 down): the attitude said after the tapes, before the baro.
    [Fact]
    public void On_the_stand_the_standby_attitude_is_level()
    {
        var sfd = A350GroundDisplays.ReadSfd(Fixture("SFD"));
        Assert.Equal(new[] { "Pitch: 0.4 degrees down", "Bank: wings level", "Baro: 1013, cyan" }, sfd.Skip(2).Take(3));
    }
}
