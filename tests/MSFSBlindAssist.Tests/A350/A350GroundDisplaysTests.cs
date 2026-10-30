using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The other displays read by name, from live draws on the stand at VCBI (2026-10-27): engines and APU running,
// IRs aligned, no flight plan; heading 219, 12.8 ft indicated.
public class A350GroundDisplaysTests
{
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
}
