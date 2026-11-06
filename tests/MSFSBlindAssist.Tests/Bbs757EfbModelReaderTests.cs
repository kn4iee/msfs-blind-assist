using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;
using Node = MSFSBlindAssist.SimConnect.BBS757.Bbs757EfbMemory.Node;
using App = MSFSBlindAssist.SimConnect.BBS757.Bbs757EfbMemory.App;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The EFB model reader turns the memory walk into the window's model. These build synthetic app/control trees shaped
/// like the ones BlueBird's tablet draws (measured live 2026-11-06: the home screen, the Settings sections, a ground
/// service tile) and check the model the reader makes: its menu keys navigate, its section names the page, its rows
/// carry the switch state, and the page chrome is left out.
/// </summary>
public class Bbs757EfbModelReaderTests
{
    private static uint _addr = 0x1000;
    private static Node N(string kind, string name, string text, int x, int y, int w, int h, params Node[] children) =>
        new(kind, name, text, x, y, w, h, _addr += 0x40, children);

    // ---- the model's grammar fields ----

    [Fact]
    public void A_model_with_every_new_field_reads_back_equal()
    {
        var box = new System.Drawing.Rectangle(300, 200, 100, 30);
        var hit = new System.Drawing.Point(350, 215);
        var items = new[]
        {
            new Bbs757EfbItem(Bbs757EfbKind.Heading, "Doors", box, hit, false, "h:Doors", Level: 3, Name: "doors_title"),
            new Bbs757EfbItem(Bbs757EfbKind.Button, "OPEN", box, hit, true, "btn:OPEN", Label: "NOSE DOOR", Name: "nose_btn"),
            new Bbs757EfbItem(Bbs757EfbKind.Field, "SimBrief ID", box, hit, true, "f:id", Value: "12345"),
            new Bbs757EfbItem(Bbs757EfbKind.Tab, "Fuel", box, hit, true, "tab:Fuel", Selected: true),
            new Bbs757EfbItem(Bbs757EfbKind.LongText, "one\ntwo", box, hit, false, "lt:1"),
        };
        var model = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", Array.Empty<Bbs757EfbItem>(), items)
        {
            Viewport = new System.Drawing.Rectangle(1, 2, 3, 4),
            Keyboard = new Bbs757EfbKeyboard(new System.Drawing.Rectangle(0, 648, 1397, 400), new[]
            {
                new Bbs757EfbKey("char_btn_q|Q|1", "q", "Q", "1", new System.Drawing.Point(40, 700)),
                new Bbs757EfbKey("btn_enter", "", "", "", new System.Drawing.Point(900, 800)),
            }),
        };

        var back = Bbs757EfbModelJson.Read(Bbs757EfbModelJson.Write(model));

        Assert.Equal(model.Items, back.Items);
        Assert.Equal(model.Viewport, back.Viewport);
        Assert.Equal(model.Keyboard!.Box, back.Keyboard!.Box);
        Assert.Equal(model.Keyboard.Keys, back.Keyboard.Keys);
    }

    // ---- the home screen ----

    [Fact]
    public void Home_offers_the_five_apps_as_navigating_menu_entries()
    {
        var home = new App(6, "HOME", new[]
        {
            N("other", "main_canvas", "", 0, 32, 1397, 1016,
                N("grid", "main_grid_7x3", "", 50, 82, 1297, 600,
                    N("button", "aircraft_btn", "Aircraft", 273, 127, 110, 110),
                    N("button", "navigation_btn", "Navigation", 458, 127, 110, 110),
                    N("button", "systems_btn", "Systems", 644, 127, 110, 110),
                    N("button", "checklist_btn", "Checklist", 829, 127, 110, 110),
                    N("button", "settings_btn", "Settings", 1014, 127, 110, 110))),
        });
        var shell = new App(2, "app", new[] { N("grid", "kb_grid", "", 0, 648, 1397, 400) });

        var model = Bbs757EfbModelReader.Read(new[] { home, shell })!;

        Assert.Equal("HOME", model.App);
        Assert.Equal("Home", model.PageName);
        var keys = model.Items.Select(i => i.Key).ToArray();
        Assert.Equal(new[] { "app:AIRCRAFT", "app:NAVIGATION", "app:SYSTEMS", "app:CHECKLIST", "app:SETTINGS" }, keys);
        Assert.All(model.Items, i => Assert.Equal(Bbs757EfbKind.Menu, i.Kind));
        Assert.Equal("Aircraft", model.Items[0].Text);
    }

    // ---- a Settings page: side menu, section, scrollable, rows ----

    private static App SettingsApp(string selectedScrollbox, int stackHeight, params Node[] rows)
    {
        var menu = N("grid", "control_grid", "", 0, 82, 250, 966,
            N("other", "btn_stack", "", 0, 142, 250, 716,
                N("button", "btn_general", "GENERAL", 0, 142, 250, 60),
                N("button", "btn_aircraft", "AIRCRAFT", 0, 202, 250, 60),
                N("button", "btn_livery", "LIVERY OPTIONS", 0, 262, 250, 60),
                N("button", "btn_audio", "CALLOUTS", 0, 322, 250, 60),
                N("button", "btn_cabin_audio", "CABIN AUDIO", 0, 382, 250, 60),
                N("button", "btn_airac_data", "AIRAC DATA", 0, 442, 250, 60)));
        var mainGrid = N("grid", "main_grid", "", 0, 32, 1397, 1016,
            N("other", "gradbox_tl", "", 0, 32, 250, 50,
                N("button", "home_btn", "", 0, 32, 50, 50),
                N("label", "page_title_label", "SETTINGS", 60, 42, 145, 36)),
            N("other", "gradbox_br", "", 0, 82, 250, 966, menu));
        var stack = N("other", selectedScrollbox[..^"scrollbox".Length] + "stackbox", "", 250, 94, 1147, stackHeight, rows);
        var pageGrid = N("grid", "page_main_grid", "", 0, 32, 1397, 1016,
            N("grid", "page_control_grid", "", 0, 82, 1397, 966,
                N("other", "page_colorbox", "", 250, 82, 1147, 966,
                    N("other", selectedScrollbox, "", 250, 82, 1147, 966, stack))));
        return new App(12, "SETTINGS", new[] { mainGrid, pageGrid });
    }

    private static Node SettingRow(string name, string kind, string label, string value) =>
        N("grid", name + "_grid", "", 250, 94, 1147, 60,
            N("other", name + "_title_group", "", 250, 94, 647, 60,
                N("other", name + "_label", label, 280, 94, 647, 60)),
            N(kind, name + "_toggle", "", 1218, 107, 60, 35),
            N("label", name + "_value_label", value, 1197, 94, 200, 60));

    [Fact]
    public void Callouts_names_its_section_marks_it_selected_and_scrolls()
    {
        var app = SettingsApp("callouts_scrollbox", 1832,
            SettingRow("pilot_callout_v1", "toggle", "V1", "ON"),
            SettingRow("callout_35", "toggle", "35", "OFF"));

        var model = Bbs757EfbModelReader.Read(new[] { app })!;

        Assert.Equal("SETTINGS", model.App);
        Assert.Equal("CALLOUTS", model.Section);
        Assert.Equal("Settings, Callouts", model.PageName);
        Assert.True(model.Scrollable);

        var menu = model.Menu;
        Assert.Equal(6, menu.Count);
        Assert.All(menu, m => Assert.Equal(Bbs757EfbKind.Menu, m.Kind));
        Assert.Equal(new[] { "menu:GENERAL", "menu:AIRCRAFT", "menu:LIVERY OPTIONS", "menu:CALLOUTS", "menu:CABIN AUDIO", "menu:AIRAC DATA" },
            menu.Select(m => m.Key).ToArray());
        var callouts = Assert.Single(menu, m => m.Key == "menu:CALLOUTS");
        Assert.True(callouts.Selected);
        Assert.DoesNotContain(menu, m => m.Key != "menu:CALLOUTS" && m.Selected);

        var v1 = Assert.Single(model.Items, i => i.Text == "V1");
        Assert.Equal(Bbs757EfbKind.Toggle, v1.Kind);
        Assert.True(v1.On);
        Assert.Equal("ON", v1.Value);
        Assert.Equal("pilot_callout_v1_toggle", v1.Key); // the control name, stable across reads
        var c35 = Assert.Single(model.Items, i => i.Text == "35");
        Assert.False(c35.On);
    }

    [Fact]
    public void A_short_section_does_not_scroll()
    {
        var app = SettingsApp("general_scrollbox", 420, SettingRow("x", "toggle", "A ROW", "ON"));
        var model = Bbs757EfbModelReader.Read(new[] { app })!;
        Assert.Equal("GENERAL", model.Section);
        Assert.False(model.Scrollable);                          // stackbox 420 fits the 966 scrollbox
    }

    // ---- R1: the section of every page ----

    /// <summary>A page shaped like the real ones: the shell grids and the side menu at the left, the page's controls from x 250.</summary>
    private static App PageApp(string app, string[] menuWords, params Node[] content)
    {
        var buttons = menuWords.Select((w, i) => N("button", "btn_" + i, w, 0, 142 + 60 * i, 250, 60)).ToArray();
        return new App(7, app, new[]
        {
            N("grid", "main_grid", "", 0, 32, 1397, 1016,
                N("grid", "control_grid", "", 0, 82, 250, 966, N("other", "btn_stack", "", 0, 142, 250, 716, buttons))),
            N("grid", "page_main_grid", "", 0, 32, 1397, 1016,
                N("grid", "page_control_grid", "", 0, 82, 1397, 966,
                    N("other", "page_colorbox", "", 250, 82, 1147, 966, content))),
        });
    }

    [Theory]
    [InlineData("presets_label", "COCKPIT PRESETS", "Aircraft, Cockpit presets")]
    [InlineData("fuel_left_tank", "FUEL", "Aircraft, Fuel")]
    [InlineData("wb_pax_business_title", "WEIGHT & BALANCE", "Aircraft, Weight & balance")]
    [InlineData("gnd_service_mode_box", "GROUND SERVICES", "Aircraft, Ground services")]
    [InlineData("cabin_lighting_section", "CABIN & DOORS", "Aircraft, Cabin & doors")]
    [InlineData("maintenance_layout_grid", "MAINTENANCE", "Aircraft, Maintenance")]
    [InlineData("failures_canvas", "FAILURES", "Aircraft, Failures")]
    public void Every_aircraft_section_is_named_by_its_table_entry(string name, string section, string pageName)
    {
        var page = PageApp("AIRCRAFT", Bbs757EfbVocabulary.Menus["AIRCRAFT"],
            N("other", "canvas_aircraft", "", 250, 82, 1147, 966, N("other", name, "", 300, 200, 100, 30)));

        var model = Bbs757EfbModelReader.Read(new[] { page })!;

        Assert.Equal(section, model.Section);
        Assert.Equal(pageName, model.PageName);
        Assert.Equal(section, Bbs757EfbPageRules.SectionFromTable("AIRCRAFT", new[] { "main_grid", name }));
    }

    [Theory]
    [InlineData("planning_acars_dashboard_canvas", "ACARS DASHBOARD")]
    [InlineData("planning_operating_fp_canvas", "OPERATING FP")]
    [InlineData("planning_weather_notam_canvas", "WEATHER & NOTAM")]
    [InlineData("planning_charts_canvas", "CHARTS")]
    [InlineData("map_canvas", "MAP")]
    [InlineData("planning_perf_calc_canvas", "PERF CALC")]
    public void Navigation_sections_are_named_by_their_canvas(string canvas, string section)
    {
        var page = PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            N("other", canvas, "", 250, 82, 1147, 966, N("other", "planning_x_card", "", 260, 90, 100, 30)));

        var model = Bbs757EfbModelReader.Read(new[] { page })!;

        Assert.Equal(section, model.Section);
        Assert.Contains(model.Menu, m => m.Key == "menu:" + section && m.Selected);
    }

    [Fact]
    public void Airac_data_is_named_by_its_grid()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("55-settings-airac-data"))!;
        Assert.Equal("AIRAC DATA", model.Section);
        Assert.Equal("Settings, AIRAC data", model.PageName);
    }

    [Fact]
    public void Livery_options_is_not_read_as_aircraft()
    {
        // The real shape: no scrollbox, the rows in the stackbox Settings > Aircraft shares.
        var app = PageApp("SETTINGS", Bbs757EfbVocabulary.Menus["SETTINGS"],
            N("other", "aircraft_stackbox", "", 250, 94, 1147, 420,
                SettingRow("weight_units_setting", "toggle", "WEIGHT UNITS", "KG")));

        var model = Bbs757EfbModelReader.Read(new[] { app })!;

        Assert.Equal("LIVERY OPTIONS", model.Section);
        Assert.Equal("Settings, Livery options", model.PageName);
        var real = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("52-settings-livery-options"))!;
        Assert.Equal("Settings, Livery options", real.PageName);
        var aircraft = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        Assert.Equal("Settings, Aircraft", aircraft.PageName);
    }

    [Fact]
    public void The_section_marks_its_menu_entry_current()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11-aircraft-fuel"))!;
        var current = Assert.Single(model.Menu, m => m.Selected);
        Assert.Equal("menu:FUEL", current.Key);
    }

    [Theory]
    [InlineData("30-systems", "Systems")]
    [InlineData("40-checklist", "Checklist")]
    public void A_page_matching_no_section_reads_its_app_alone(string fixture, string pageName)
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;
        Assert.Equal("", model.Section);
        Assert.Equal(pageName, model.PageName);
        Assert.DoesNotContain(model.Menu, m => m.Selected);
    }

    [Fact]
    public void The_chrome_grids_never_name_a_section()
    {
        // main_grid must not read as the start of MAINTENANCE.
        var page = PageApp("AIRCRAFT", Bbs757EfbVocabulary.Menus["AIRCRAFT"], N("other", "canvas_aircraft", "", 250, 82, 1147, 966));
        Assert.Equal("", Bbs757EfbModelReader.Read(new[] { page })!.Section);
    }

    // ---- R2: tab rows and the current tab ----

    private static Node Tab(string name, string text) => N("button", name, text, 300, 100, 120, 40);

    [Fact]
    public void A_tab_row_marks_the_tab_its_content_names()
    {
        var landing = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var gearTabs = landing.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();
        Assert.Equal(5, gearTabs.Count);
        Assert.Equal("maintenance_landing_gear_tab_button", gearTabs[0].Name);
        Assert.Equal("tab:maintenance_selector_grid|LANDING GEAR", gearTabs[0].Key);
        Assert.Equal("Landing gear", gearTabs[0].Text);
        Assert.Equal(new[] { "LANDING GEAR" }, gearTabs.Where(t => t.Selected).Select(t => t.Key["tab:maintenance_selector_grid|".Length..]));

        var hydraulic = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15b-aircraft-maintenance-hydraulic"))!;
        Assert.Equal(new[] { "HYDRAULIC" },
            hydraulic.Items.Where(i => i.Kind == Bbs757EfbKind.Tab && i.Selected).Select(t => t.Key["tab:maintenance_selector_grid|".Length..]));
    }

    [Fact]
    public void A_tab_row_with_no_match_marks_none()
    {
        var page = PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            N("other", "planning_perf_calc_canvas", "", 250, 82, 1147, 966,
                N("grid", "tabs_grid", "", 260, 90, 300, 40, Tab("x_tab_button_0", "TAKE OFF"), Tab("x_tab_button_1", "LANDING")),
                N("label", "some_label", "NOTHING HERE", 300, 200, 100, 30)));

        var tabs = Bbs757EfbModelReader.Read(new[] { page })!.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();

        Assert.Equal(2, tabs.Count);
        Assert.DoesNotContain(tabs, t => t.Selected);
    }

    [Fact]
    public void A_tab_row_with_two_matching_panels_marks_none()
    {
        var page = PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            N("other", "planning_perf_calc_canvas", "", 250, 82, 1147, 966,
                N("grid", "tabs_grid", "", 260, 90, 300, 40, Tab("x_tab_button_0", "TAKE OFF"), Tab("x_tab_button_1", "LANDING")),
                N("other", "x_takeoff_panel", "", 300, 200, 100, 30), N("other", "x_landing_panel", "", 300, 240, 100, 30)));

        Assert.DoesNotContain(Bbs757EfbModelReader.Read(new[] { page })!.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Selected);
    }

    [Fact]
    public void A_tab_row_is_not_marked_from_a_text()
    {
        // The Failures shape: the footer button "RANDOM FAILURES: OFF" holds the RANDOM tab's word.
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("16-aircraft-failures"))!;
        var tabs = model.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();
        Assert.Equal(3, tabs.Count);
        Assert.DoesNotContain(tabs, t => t.Selected);
    }

    [Fact]
    public void A_tab_row_is_not_marked_from_a_canvas_name()
    {
        // The Weather shape: planning_weather_notam_canvas holds the NOTAM tab's word.
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("22-navigation-weather"))!;
        var tabs = model.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();
        Assert.Contains(tabs, t => t.Name == "planning_weather_primary_tab_button_3");
        Assert.Contains(tabs, t => t.Name == "planning_weather_primary_selector_button_0");
        Assert.DoesNotContain(tabs, t => t.Selected);
    }

    [Fact]
    public void A_lone_tab_name_and_the_unit_switches_are_plain_buttons()
    {
        var page = PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            N("other", "planning_perf_calc_canvas", "", 250, 82, 1147, 966,
                N("grid", "g1", "", 260, 90, 300, 40, Tab("x_tab_button_0", "TAKE OFF")),
                N("grid", "g2", "", 260, 140, 300, 40, Tab("x_unit_button_0", "Feet"), Tab("x_unit_button_1", "Pounds"))));

        Assert.DoesNotContain(Bbs757EfbModelReader.Read(new[] { page })!.Items, i => i.Kind == Bbs757EfbKind.Tab);
    }

    [Fact]
    public void The_tab_names_BlueBird_draws_are_tab_rows()
    {
        foreach (var name in new[] { "maintenance_apu_tab_button", "planning_weather_primary_tab_button_3", "failure_tab_2",
                     "planning_charts_source_button_0", "planning_charts_type_button_4" })
            Assert.True(Bbs757EfbPageRules.IsTabName(name), name);
        Assert.True(Bbs757EfbPageRules.IsTabName("planning_weather_primary_selector_button_0"));
        foreach (var name in new[] { "planning_perf_calc_unit_button_0", "failure_tabs_divider", "x_tab_button_x" })
            Assert.False(Bbs757EfbPageRules.IsTabName(name), name);
    }

    [Fact]
    public void The_page_title_and_home_button_are_not_items()
    {
        var model = Bbs757EfbModelReader.Read(new[] { SettingsApp("general_scrollbox", 420) })!;
        Assert.DoesNotContain(model.Menu.Concat(model.Items), i => i.Text == "SETTINGS");
        Assert.DoesNotContain(model.Menu.Concat(model.Items), i => i.Key.EndsWith("home_btn"));
        Assert.DoesNotContain(model.Menu.Concat(model.Items), i => i.Key == "btn:HOME");
    }

    // ---- a ground-service tile keeps its state in its words, keyed for the press runner ----

    [Fact]
    public void A_ground_service_tile_is_keyed_so_the_press_runner_finds_its_service()
    {
        var app = new App(7, "AIRCRAFT", new[]
        {
            N("grid", "page_main_grid", "", 0, 32, 1397, 1016,
                N("button", "gpu_btn", "GPU REMOVED", 300, 300, 200, 120),
                N("button", "fuel_truck_btn", "FUEL TRUCK OFF", 520, 300, 200, 120)),
        });

        var model = Bbs757EfbModelReader.Read(new[] { app })!;

        var gpu = Assert.Single(model.Items, i => i.Text.StartsWith("GPU", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("GPU", Bbs757EfbScreen.KeyWords(gpu.Key)); // the press runner's ground-power branch keys off "GPU"
        var fuel = Assert.Single(model.Items, i => i.Text.StartsWith("Fuel truck", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("FUEL TRUCK", Bbs757EfbScreen.KeyWords(fuel.Key));
        Assert.NotNull(Bbs757GroundServices.ForTile(Bbs757EfbScreen.KeyWords(fuel.Key)));
    }

    [Fact]
    public void No_front_app_reads_as_null()
    {
        Assert.Null(Bbs757EfbModelReader.Read(Array.Empty<App>()));
        var shellOnly = new App(2, "app", new[] { N("grid", "kb_grid", "", 0, 648, 1397, 400) });
        Assert.Null(Bbs757EfbModelReader.Read(new[] { shellOnly }));
    }
}
