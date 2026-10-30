using MSFSBlindAssist.Aircraft.A350.Display;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

// The ND's own controls, live at VCBI 2026-10-30: the map's click menu and the function button's four tabs, as the iniBuilds
// ANF and BTV guides name them. Each line carries where a click lands, in the ND's own coordinates.
public class A350NdPageTests
{
    // Traffic on the ND (an AI A320 passing overhead in the cruise, live 2026-10-30): the relative altitude in hundreds of feet beside its
    // symbol, over it when above, under it when below. It says what it is and, in the window, where.
    [Fact]
    public void Traffic_says_how_far_above_and_where()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("nd_traffic.json"), 0, mode: 2, range: 5).Select(l => l.Spoken).ToList();
        Assert.Contains(lines, l => l.StartsWith("Traffic 700 feet above, descending: 6 o'clock, 10", StringComparison.Ordinal) && l.EndsWith(", white", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("+07", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("+07", "700 feet above")]
    [InlineData("-13", "1300 feet below")]
    [InlineData("+00", "at the same level")]
    [InlineData("-00", "at the same level")]
    public void Traffic_altitude_in_words(string drawn, string said) => Assert.Equal(said, A350NdMap.TrafficAltitude(drawn));

    [Fact]
    public void The_status_display_says_traffic_too()
    {
        var lines = A350GroundDisplays.ReadNd(A350Fixtures.Capture("nd_traffic.json"), 0);
        Assert.Contains("Traffic 700 feet above, white", lines);
    }

    private static IReadOnlyList<MfdLine> Lines(string fixture, int side = 0) =>
        A350NdPage.Read(A350Fixtures.Capture(fixture + ".json"), side);

    private static MfdLine Line(IReadOnlyList<MfdLine> lines, string spoken) => lines.Single(l => l.Spoken == spoken);

    [Fact]
    public void The_map_menu_reads_every_item_where_it_is_clicked()
    {
        var lines = Lines("efis0_anf_map_menu");
        var menu = lines.Where(l => l.Kind == "menu").ToList();
        Assert.Equal(new[]
        {
            "SET LDG RWY, grey", "DECLUTTER RWY, grey", "ADD CROSS", "ADD FLAG", "MAP DATA", "ERASE ALL CROSSES, grey", "ERASE ALL FLAGS, grey",
            "CENTER ON ACFT, grey",
        }, menu.Select(l => l.Spoken));
        var add = Line(lines, "ADD CROSS");
        Assert.Equal(501 + 140, add.ClickX);      // its rectangle, 1337 on the EFIS, less the ND's 836
        Assert.Equal(696 + 20, add.ClickY);
    }

    [Fact]
    public void An_open_menu_comes_first()
    {
        Assert.Equal("menu", Lines("efis0_anf_map_menu")[0].Kind);
    }

    [Fact]
    public void The_function_button_is_a_line_wherever_it_sits()
    {
        var closed = Line(Lines("efis0_anf_map_menu"), A350NdPage.FunctionButton);
        Assert.Equal((723 + 25.5f, 972 + 21f), (closed.ClickX, closed.ClickY));
        var open = Line(Lines("efis0_anf_landing"), A350NdPage.FunctionButton);
        Assert.Equal((723 + 25.5f, 765 + 21f), (open.ClickX, open.ClickY));
    }

    [Fact]
    public void There_is_no_function_button_without_the_airport_map()
    {
        Assert.DoesNotContain(Lines("efis0_fd_stand"), l => l.Spoken == A350NdPage.FunctionButton);
    }

    [Fact]
    public void The_tabs_read_in_order_and_say_which_is_up()
    {
        var tabs = Lines("efis0_anf_landing").Where(l => l.Kind == "tab").ToList();
        Assert.Equal(new[] { "MAP DATA tab", "LANDING tab, selected", "ARPT SEL tab", "STATUS tab" }, tabs.Select(l => l.Spoken));
        Assert.InRange(tabs[1].ClickX, 288, 291);   // its rectangle, 1025 on the EFIS, 201 wide
        Assert.InRange(tabs[1].ClickY, 786, 788);
        Assert.Equal("MAP DATA tab, selected", Lines("efis0_anf_map_data").First(l => l.Kind == "tab").Spoken);
        Assert.Contains(Lines("efis0_anf_arpt_sel"), l => l.Spoken == "ARPT SEL tab, selected");
        Assert.Contains(Lines("efis0_anf_status"), l => l.Spoken == "STATUS tab, selected");
    }

    [Fact]
    public void The_landing_tab_reads_its_fields_and_figures()
    {
        var lines = Lines("efis0_anf_landing");
        Assert.Equal("button", Line(lines, "MODIFY RWY button").Kind);
        var code = Line(lines, "LDG PERF CODE: WET-5, cyan");
        Assert.Equal("field", code.Kind);
        Assert.Equal((604.5f, 849f), (code.ClickX, code.ClickY));   // the text box in the dropdown's box
        var exit = Line(lines, "BTV EXIT AT: none M FROM THRESHOLD, grey");
        Assert.Equal("field", exit.Kind);
        Assert.Equal((219 + 71.5f, 961 + 19.5f), (exit.ClickX, exit.ClickY));
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken.StartsWith("RWY", StringComparison.Ordinal) && l.Spoken.Contains("ROW LDG DIST", StringComparison.Ordinal));
    }

    [Fact]
    public void The_airport_tab_reads_its_choices_and_the_airport_found()
    {
        var lines = Lines("efis0_anf_arpt_sel");
        Assert.Equal("option", Line(lines, "ICAO, selected").Kind);
        Assert.Equal("option", Line(lines, "IATA").Kind);
        Assert.Equal("option", Line(lines, "CITY NAME").Kind);
        Assert.Equal("field", Line(lines, "Airport: VCBI, green").Kind);
        Assert.Equal("button", Line(lines, "ORIGIN button, grey").Kind);
        Assert.Equal("button", Line(lines, "DEST button, grey").Kind);
        Assert.Equal("button", Line(lines, "ALTN button, grey").Kind);
        Assert.Equal("button", Line(lines, "DISPLAY AIRPORT button, grey").Kind);
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken == "BANDARANAIKE INTL COLOMBO, green");
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken == "VCBI CMB, green");
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken == "07°10.8N/079°53.1E, green");
    }

    [Fact]
    public void The_map_data_tab_reads_its_filters_and_marks()
    {
        var lines = Lines("efis0_anf_map_data");
        Assert.Equal("option", Line(lines, "RWY, selected").Kind);
        Assert.Equal("option", Line(lines, "TWY").Kind);
        Assert.Equal("option", Line(lines, "STAND").Kind);
        Assert.Equal("option", Line(lines, "OTHER").Kind);
        Assert.Equal("field", Line(lines, "Map item: none, cyan").Kind);
        Assert.Equal("button", Line(lines, "ADD CROSS button, grey").Kind);
        Assert.Equal("button", Line(lines, "ADD FLAG button, grey").Kind);
        Assert.Equal("button", Line(lines, "CENTER MAP button, grey").Kind);
    }

    [Fact]
    public void The_status_tab_reads_its_databases()
    {
        var lines = Lines("efis0_anf_status");
        Assert.Equal("button", Line(lines, "SWAP button").Kind);
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken.Contains("ACTIVE DATABASE", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken.Contains("AIRPORT DATABASE", StringComparison.Ordinal));
    }

    [Fact]
    public void What_the_nd_shows_follows_its_controls_and_is_no_control_again()
    {
        var lines = Lines("efis0_anf_landing");
        var shown = lines.Where(l => l.Kind == "text").Select(l => l.Spoken).ToList();
        Assert.Contains(shown, l => l.StartsWith("Airport: BANDARANAIKE", StringComparison.Ordinal));
        Assert.Contains(shown, l => l == "TCAS, amber");
        Assert.DoesNotContain(shown, l => l.Contains("MODIFY RWY", StringComparison.Ordinal) || l.Contains("ARPT SEL", StringComparison.Ordinal));
        Assert.True(lines.ToList().FindIndex(l => l.Kind == "tab") < lines.ToList().FindIndex(l => l.Spoken.StartsWith("Airport:", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_maps_names_read_where_they_lie_and_can_be_clicked()
    {
        var lines = Lines("efis0_anf_arc_02");
        var a7 = Line(lines, "Stand A7: 2 o'clock, 75 m");
        Assert.Equal("map", a7.Kind);
        Assert.InRange(a7.ClickX, 470, 495);
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken.StartsWith("Runway 22:", StringComparison.Ordinal) && l.Spoken.EndsWith("off the screen", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rose_map_scales_its_half_range_mark()
    {
        // The same stand at the same zoom as the ARC map: the mark says 0.1 where ARC's says 0.2.
        Assert.Contains(Lines("efis0_anf_rose_01"), l => l.Spoken == "Stand A7: 2 o'clock, 75 m");
    }

    [Fact]
    public void A_plan_map_reads_from_its_centre_north_up()
    {
        var lines = Lines("efis0_anf_plan_01");
        Assert.Contains(lines, l => l.Spoken.StartsWith("Map range: 0.1 NM", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "map" && l.Spoken.Contains("from the map's centre", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dropdown_field_also_offers_its_arrow()
    {
        // A click in the box starts typing; the list opens from the arrow beside it (live: OMDB's runways on the RWY filter).
        var lines = Lines("efis0_anf_map_data");
        var arrow = Line(lines, "Map item list button");
        Assert.InRange(arrow.ClickX, 172, 201);   // right of the text box (33 to 172), inside the dropdown's box (30 to 201)
        Assert.InRange(arrow.ClickY, 820, 855);
    }

    [Fact]
    public void An_open_list_comes_first_and_its_items_are_clicked()
    {
        var lines = Lines("efis0_anf_item_list");
        Assert.Equal("menu", lines[0].Kind);
        Assert.Equal(new[] { "12L", "12R", "30L", "30R" }, lines.Where(l => l.Kind == "menu").Select(l => l.Spoken));
    }

    [Fact]
    public void A_dialog_says_what_it_asks_before_its_buttons()
    {
        var lines = Lines("efis0_anf_erase_dialog");
        Assert.Equal("ERASE ALL CROSSES, white", lines[0].Spoken);
        Assert.Equal("CANCEL button", lines[1].Spoken);
        Assert.Equal("CONFIRM button", lines[2].Spoken);
    }

    [Fact]
    public void The_marks_on_the_map_read_where_they_lie_and_are_clicked()
    {
        var lines = Lines("efis0_anf_marks");
        var flag = Assert.Single(lines, l => l.Spoken.StartsWith("Flag:", StringComparison.Ordinal));
        var cross = Assert.Single(lines, l => l.Spoken.StartsWith("Cross:", StringComparison.Ordinal));
        Assert.Equal("map", flag.Kind);
        Assert.Equal("map", cross.Kind);
        Assert.InRange(flag.ClickX, 749, 751);     // sprite 741 to 759, 785 to 811
        Assert.InRange(flag.ClickY, 797, 799);
        Assert.InRange(cross.ClickX, 677, 680);    // sprite 667 to 690, 397 to 420
        Assert.InRange(cross.ClickY, 407, 410);
    }

    [Fact]
    public void Placeholder_dashes_in_a_panels_rows_are_not_read()
    {
        var lines = Lines("efis0_anf_map_data_04");
        Assert.Contains(lines, l => l.Spoken == "RWY, white; 12L, green; TORA, white; 4050, green; M, cyan");
        Assert.Contains(lines, l => l.Spoken == "LDA, white; 0, green; M, cyan");
    }

    [Fact]
    public void A_plan_map_with_the_panel_open_reads_about_the_moved_centre()
    {
        var lines = Lines("efis0_anf_plan_panel");
        Assert.DoesNotContain(lines, l => l.Spoken is "N, white" or "S, white" or "W E, white");
        var frame = A350NdMap.FrameOf(A350Fixtures.Capture("efis0_anf_plan_panel.json").Texts.Where(t => t.X >= 836).Select(t => t with { X = t.X - 836 }).ToList());
        Assert.Equal((404f, 396.5f, true), (frame!.Value.X, frame.Value.Y, frame.Value.NorthUp));
    }

    // Live 2026-10-30, M9 set as OMDB 12L's exit on a wet runway: "RWY 12L - 3900 M - WET-5", "EXIT M9 2087 M", "ROT 63''",
    // "TURNAROUND 152'/169'" (the brakes' cooling after MAX and after IDLE reverse) at the ND's top left.
    [Fact]
    public void The_btv_block_reads_by_name()
    {
        var shown = Lines("efis0_anf_btv_set").Where(l => l.Kind == "text").Select(l => l.Spoken).ToList();
        Assert.Contains("Landing runway: 12L, 3900 M, WET-5, green", shown);
        Assert.Contains("BTV exit: M9, 2087 M, green", shown);
        Assert.Contains("Runway occupancy: 63 seconds, green", shown);
        Assert.Contains("Turnaround: 152 minutes with max reverse, 169 minutes with idle reverse, green", shown);
        Assert.DoesNotContain(shown, l => l.StartsWith("Wind: 12L", StringComparison.Ordinal));   // the wind's place, when it is drawn
    }

    // Live 2026-10-30: the BTV exit's name on the runway turns cyan, the stands' colour, and it read among the buildings.
    [Fact]
    public void The_btv_exit_is_a_taxiway_never_a_building()
    {
        var shown = Lines("efis0_anf_btv_set").Where(l => l.Kind == "text").Select(l => l.Spoken).ToList();
        Assert.DoesNotContain(shown, l => l.StartsWith("Stands and buildings:", StringComparison.Ordinal) && l.Contains("M9", StringComparison.Ordinal));
        Assert.Contains(shown, l => l.StartsWith("Taxiways:", StringComparison.Ordinal) && l.Contains("M9", StringComparison.Ordinal));
    }

    [Fact]
    public void The_radar_mode_is_no_name_on_the_map()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("efis0_cruise_wxt.json"), 0, 3, 7);
        Assert.DoesNotContain(lines, l => l.Kind == "map" && l.Spoken.StartsWith("WX+T", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "text" && l.Spoken == "Radar mode: WX+T, green");
    }

    // Live 2026-10-30, FL300, an AI intruder: an arrow right of the figure, a 9 by 19 sprite, says which way the traffic is moving.
    [Theory]
    [InlineData("efis0_traffic_climbing.json", 7, "Traffic 900 feet above, climbing: 12 o'clock, ")]
    [InlineData("efis0_traffic_descending.json", 7, "Traffic 800 feet above, descending: 12 o'clock, ")]
    public void Traffic_says_which_way_it_is_moving(string fixture, int range, string starts)
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture(fixture), 0, mode: 3, range: range);
        Assert.Contains(lines, l => l.Spoken.StartsWith(starts, StringComparison.Ordinal) && l.Spoken.EndsWith(", white", StringComparison.Ordinal));
    }

    // SURV TRAFFIC's FLT ID DISPLAY ON ND: the target's flight ID and, under it, its speed and wake category, on the other side of the symbol
    // from the figure (live 2026-10-30). It read as a map name of its own and a bare "381 M".
    [Theory]
    [InlineData("efis0_traffic_flight_id_above.json", "Traffic IVAO, 1200 feet above, descending: 12 o'clock, ", ", speed 337, wake M, white")]
    [InlineData("efis0_traffic_flight_id_below.json", "Traffic IVAO, 500 feet below, descending: 12 o'clock, ", ", speed 348, wake M, white")]
    public void Traffic_says_its_flight_id_speed_and_wake(string fixture, string starts, string ends)
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture(fixture), 0, mode: 3, range: 6);
        Assert.Contains(lines, l => l.Spoken.StartsWith(starts, StringComparison.Ordinal) && l.Spoken.EndsWith(ends, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Spoken.StartsWith("IVAO", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l.Spoken, @"^\d+\s+M"));
    }

    // Live 2026-10-30, FL300: POSITION/TIME's marker on the path: "(20:45)" in green. It has no letters, so it read as a bare line with no place.
    [Fact]
    public void A_time_marker_is_on_the_map_where_it_lies()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("efis0_time_marker.json"), 0, mode: 3, range: 8);
        Assert.Contains(lines, l => l.Kind == "map" && l.Spoken.StartsWith("Time marker 20:45, green: 12 o'clock, ", StringComparison.Ordinal));
    }

    // Live 2026-10-30: the aircraft symbol's DIR TO opens a panel on the map whose ERASE and INSERT are DualTextButtons, read only as rows of
    // text ("ERASE INSERT", "* *", "TMPY TMPY"), so nothing in the window could press them.
    [Fact]
    public void The_direct_to_panels_erase_and_insert_are_buttons()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("efis0_nd_dirto_panel.json"), 0, mode: 3, range: 8);
        var erase = Assert.Single(lines, l => l.Kind == "button" && l.Spoken == "ERASE * TMPY button, amber");
        Assert.Equal((265f, 647f), (erase.ClickX, erase.ClickY));
        var insert = Assert.Single(lines, l => l.Kind == "button" && l.Spoken == "INSERT * TMPY button, amber");
        Assert.Equal((542f, 647f), (insert.ClickX, insert.ClickY));
        Assert.DoesNotContain(lines, l => l.Spoken.StartsWith("ERASE INSERT", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Spoken == "DIRECT, selected" || l.Spoken == "DIRECT");
    }

    // Review 3 (M4): a waypoint's menu opens with its title line first, so the selection stayed on an unrelated line; an erase dialog's
    // question has no menu line at all.
    [Fact]
    public void A_menu_or_dialog_just_opened_takes_the_selection()
    {
        MfdLine Line(string kind, string said) => new(kind, said, 1, 1);
        var map = new[] { Line("text", "Ground speed: 0, green"), Line("map", "Taxiway M9: east, 480 m") };
        var menu = new[] { Line("text", "DB407, green"), Line("menu", "FROM P.POS DIR TO"), Line("menu", "DELETE") };
        Assert.Equal(1, A350NdPage.SelectionFor(map, menu));
        Assert.Null(A350NdPage.SelectionFor(menu, menu));   // already open: the pilot's place stands
        Assert.Null(A350NdPage.SelectionFor(menu, map));
        var dialog = new[] { Line("text", "ERASE ALL CROSSES?"), Line("button", "CANCEL button"), Line("button", "ERASE button") };
        Assert.Equal(0, A350NdPage.SelectionFor(map, dialog));
        Assert.Null(A350NdPage.SelectionFor(dialog, dialog));
    }

    // The plan's runway on a range map: it is no taxiway, and it says where it lies, once, as the map's other names do.
    [Fact]
    public void A_range_map_says_where_the_plans_runway_is()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("efis0_arc40_omdb_label.json"), 0, 3, 7);
        Assert.DoesNotContain(lines, l => l.Spoken.StartsWith("Taxiway", StringComparison.Ordinal));
        var runway = Assert.Single(lines, l => l.Kind == "map" && l.Spoken.StartsWith("OMDB 12L, white: ", StringComparison.Ordinal));
        Assert.Contains("o'clock", runway.Spoken);
    }

    [Fact]
    public void An_exit_too_short_says_so()
    {
        Assert.Equal("BTV exit: M6, 1500 M, TOO SHORT, orange", A350GroundDisplays.BtvRow("EXIT", new[] { "EXIT", "M6", "1500", "M", "TOO", "SHORT" }, "orange"));
    }

    [Fact]
    public void A_distance_entered_by_hand_reads_as_the_exit()
    {
        Assert.Equal("BTV exit: 2949 M, green", A350GroundDisplays.BtvRow("EXIT", new[] { "EXIT", "2949", "M" }, "green"));
    }

    // Live 2026-10-30: a taxiway's name is drawn several times along it, and only the one on the runway is the exit's: a click
    // there says SET BTV EXIT, or once set UNSET BTV EXIT, and that one turns cyan. Elsewhere the same click says neither.
    [Fact]
    public void The_exit_on_the_runway_is_the_drawing_a_click_lands_on()
    {
        var m9 = Assert.Single(Lines("efis0_anf_btv_set"), l => l.Kind == "map" && l.Spoken.StartsWith("Taxiway M9", StringComparison.Ordinal));
        Assert.Equal((624.5f, 537.4f), (MathF.Round(m9.ClickX, 1), MathF.Round(m9.ClickY, 1)));
        Assert.StartsWith("Taxiway M9 (BTV exit):", m9.Spoken);
        var m7a = Assert.Single(Lines("efis0_anf_btv_set"), l => l.Kind == "map" && l.Spoken.StartsWith("Taxiway M7A", StringComparison.Ordinal));
        Assert.DoesNotContain("(BTV exit)", m7a.Spoken);
    }

    [Fact]
    public void The_landing_runway_says_so()
    {
        Assert.Contains(Lines("efis0_anf_btv_set"), l => l.Spoken.StartsWith("Runway 12L (landing):", StringComparison.Ordinal));
    }

    [Fact]
    public void A_taxiway_near_the_landing_runway_says_how_far_along_it_is()
    {
        var m7a = Assert.Single(Lines("efis0_anf_btv_set"), l => l.Kind == "map" && l.Spoken.StartsWith("Taxiway M7A:", StringComparison.Ordinal));
        Assert.Contains("on runway 12L, about 1930 m from its threshold", m7a.Spoken);
        var far = Assert.Single(Lines("efis0_anf_btv_set"), l => l.Kind == "map" && l.Spoken.StartsWith("Taxiway K5:", StringComparison.Ordinal));
        Assert.DoesNotContain("on runway", far.Spoken);
    }

    // Live 2026-10-30 at 320 NM with the flight plan: a click on a waypoint's name says its name and FROM P.POS DIR TO *, INSERT
    // NEXT WPT, DELETE *; a click on the aircraft symbol says P.POS and DIR TO.
    [Fact]
    public void On_a_range_the_names_on_the_map_can_be_clicked_and_say_where_they_lie()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("efis0_nd_arc_320.json"), 0, mode: 3, range: 10);
        var tvm = Line(lines, "TVM, green: 3 o'clock, 190 NM");   // 113 + 80 along the route, F-PLN page
        Assert.Equal("map", tvm.Kind);
        Assert.InRange(tvm.ClickX, 690, 710);
        Assert.Contains(lines, l => l.Kind == "map" && l.Spoken.StartsWith("BASUR, green: 3 o'clock, 110 NM", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "map" && l.Spoken.StartsWith("KAT, cyan:", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Kind == "text" && l.Spoken == "KAT, cyan");
    }

    [Fact]
    public void The_aircraft_symbol_is_a_line_on_a_range_map()
    {
        var arc = Line(A350NdPage.Read(A350Fixtures.Capture("efis0_nd_arc_320.json"), 0, mode: 3, range: 10), A350NdPage.AircraftSymbol);
        Assert.Equal((404f, 629f), (arc.ClickX, arc.ClickY));
        var rose = Line(A350NdPage.Read(A350Fixtures.Capture("efis0_nd_rose_320.json"), 0, mode: 2, range: 10), A350NdPage.AircraftSymbol);
        Assert.Equal((404f, 381f), (rose.ClickX, rose.ClickY));
        Assert.DoesNotContain(A350NdPage.Read(A350Fixtures.Capture("efis0_nd_plan_320.json"), 0, mode: 4, range: 10), l => l.Spoken == A350NdPage.AircraftSymbol);
    }

    [Fact]
    public void A_plan_map_says_where_a_name_lies_from_its_centre()
    {
        var lines = A350NdPage.Read(A350Fixtures.Capture("efis0_nd_plan_320.json"), 0, mode: 4, range: 10);
        Assert.Contains(lines, l => l.Kind == "map" && l.Spoken.StartsWith("BASUR, green: north west, 110 NM from the map's centre", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_knobs_a_name_still_reads_and_can_be_clicked()
    {
        var lines = Lines("efis0_nd_arc_320");
        Assert.Contains(lines, l => l.Kind == "map" && l.Spoken == "TVM, green");
    }

    [Fact]
    public void Flags_and_readouts_are_no_names_on_the_map()
    {
        var map = A350NdPage.Read(A350Fixtures.Capture("efis0_nd_arc_320.json"), 0, mode: 3, range: 10).Where(l => l.Kind == "map").Select(l => l.Spoken).ToList();
        Assert.DoesNotContain(map, l => l.StartsWith("WXR", StringComparison.Ordinal) || l.StartsWith("TCAS", StringComparison.Ordinal) || l.StartsWith("NAV ACCUR", StringComparison.Ordinal));
    }

    [Fact]
    public void A_waypoints_menu_reads_its_title_first()
    {
        var lines = Lines("efis0_nd_wpt_menu");
        Assert.Equal("TVM, green", lines[0].Spoken);
        Assert.Equal("FROM P.POS DIR TO *", lines[1].Spoken);
        Assert.Equal("INSERT NEXT WPT", lines[2].Spoken);
        Assert.Equal("DELETE *", lines[3].Spoken);
    }

    [Fact]
    public void The_airport_tabs_fms_airports_say_which_they_are()
    {
        var lines = Lines("efis0_anf_arpt_fms");
        Assert.Contains(lines, l => l.Spoken == "Origin VCBI button");
        Assert.Contains(lines, l => l.Spoken == "Destination OMDB button, grey");
        Assert.Contains(lines, l => l.Spoken == "Alternate OMAA button");
    }

    [Fact]
    public void A_side_clicks_its_own_half()
    {
        Assert.Equal(836 + 100f, A350NdPage.EfisX(0, 100));
        Assert.Equal(100f, A350NdPage.EfisX(1, 100));
    }

    [Fact]
    public void The_first_officers_nd_reads_on_its_own_half()
    {
        var lines = Lines("efis1_stand_omdb_a5", 1);
        Assert.Contains(lines, l => l.Spoken.StartsWith("Airport: DUBAI", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Spoken == A350NdPage.FunctionButton);
    }
}
