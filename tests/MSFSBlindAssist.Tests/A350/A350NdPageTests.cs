using MSFSBlindAssist.Aircraft.A350.Display;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

// The ND's own controls, live at VCBI 2026-10-30: the map's click menu and the function button's four tabs, as the iniBuilds
// ANF and BTV guides name them. Each line carries where a click lands, in the ND's own coordinates.
public class A350NdPageTests
{
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
