using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 11 Task 4: the read page as the shared EFB window's elements, and what each press does. (Plan 18, Task 7: every
/// control presses and the window refuses nothing; the new element tests are in <see cref="Bbs757EfbElementsTests"/>.)</summary>
public class Bbs757EfbBridgeTests
{
    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

    private static Dictionary<string, string> ItemsByText(Bbs757EfbElements.Built b)
    {
        var o = new Dictionary<string, string>();
        for (int i = 0; i < b.Count; i++) o[b.Data[$"items.{i}.text"]] = i.ToString();
        return o;
    }

    private static string Field(Bbs757EfbElements.Built b, string text, string field)
    {
        var map = ItemsByText(b);
        Assert.True(map.ContainsKey(text), "no element " + text + ": " + string.Join(" | ", map.Keys));
        return b.Data.TryGetValue($"items.{map[text]}.{field}", out var v) ? v : "";
    }

    private static Bbs757EfbElements.Action ActionFor(Bbs757EfbElements.Built b, string text) =>
        b.Actions[int.Parse(Field(b, text, "aidx"))];

    [Fact]
    public void Ground_services_become_buttons_whose_state_change_is_spoken()
    {
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services"), null, NoExtras);
        Assert.Equal("Aircraft, Ground services", b.Data["page"]);
        Assert.Equal("true", Field(b, "Fuel truck off", "clickable"));
        Assert.Equal("true", Field(b, "Fuel truck off", "announceChange"));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Fuel truck off").Kind);
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Home").Kind);
        Assert.Equal("Ground services (current page)", ItemsByText(b).Keys.First(k => k.StartsWith("Ground services", StringComparison.Ordinal)));
    }

    [Fact]
    public void Settings_switches_are_check_boxes_and_known_sliders_are_choice_lists()
    {
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft"), null, NoExtras);
        Assert.Equal("checkbox", Field(b, "Nosewheel steering: Realistic", "type"));
        Assert.Equal("true", Field(b, "Nosewheel steering: Realistic", "value"));
        Assert.Equal("select", Field(b, "IRS alignment time", "type"));
        // The choices are BlueBird's own words, in the order of its slider (the settings catalog's).
        Assert.Equal("Full (7 min+)", Field(b, "IRS alignment time", "value"));
        Assert.Contains("2 min", Field(b, "IRS alignment time", "options").Split((char)0x1f));
        // A switch's key does not carry its state, so the window keeps focus on it when it flips.
        Assert.Equal("tgl:NOSEWHEEL STEERING", Field(b, "Nosewheel steering: Realistic", "key"));
    }

    [Fact]
    public void An_alert_is_the_first_element_and_assertive()
    {
        // The page name is the window's own title line (the "page" key), never a list item.
        var b = Bbs757EfbElements.Build(null, "The tablet reader could not open.", NoExtras);
        Assert.Equal("BlueBird tablet", b.Data["page"]);
        Assert.Equal("alert", b.Data["items.0.kind"]);
        Assert.Equal("assertive", b.Data["items.0.live"]);
        Assert.Equal("alert", b.Data["items.0.key"]);
    }

    [Fact]
    public void A_still_tablet_reads_as_unchanged()
    {
        using var a = new Bitmap(1397, 1048);
        using var c = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(c)) g.FillRectangle(Brushes.Red, 300, 300, 300, 200);
        var sa = Bbs757EfbBridge.Signature(a);
        Assert.False(Bbs757EfbBridge.Differs(sa, Bbs757EfbBridge.Signature(a)));
        Assert.True(Bbs757EfbBridge.Differs(sa, Bbs757EfbBridge.Signature(c)));
        // The status bar (its clock) is not part of the picture compared.
        using var d = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(d)) g.FillRectangle(Brushes.White, 0, 0, 400, 30);
        Assert.False(Bbs757EfbBridge.Differs(sa, Bbs757EfbBridge.Signature(d)));
    }

    // =====================================================================================================================
    // Plan 18, Task 8: the tablet window presses what the pilot presses. The press runner (Bbs757EfbPressRunner) is run over a
    // fake tablet: a page, a picture that changes with it, BlueBird's settings file and variables, and a clock its waits move.
    // Everything spoken is checked under en-US, de-DE and sv-SE (CI runs under en-US).
    // =====================================================================================================================

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private const string Ellipsis = "…";
    private const string Egpws = "options.egpws_callouts.";

    private static Point Centre(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static Bbs757EfbItem Tile(string text, string key, Rectangle box, Bbs757EfbTint tint = Bbs757EfbTint.None) =>
        new(Bbs757EfbKind.Button, text, box, Centre(box), true, key, tint);

    private static Bbs757EfbItem Switch(string text, string key, bool? on, string value = "", int y = 333) =>
        new(Bbs757EfbKind.Toggle, text, new Rectangle(273, y, 237, 33), new Point(1170, y + 16), true, key, On: on, Value: value);

    private static Bbs757EfbScreenModel PageOf(string app, string section, params Bbs757EfbItem[] items) =>
        new(app, section, Array.Empty<Bbs757EfbItem>(), items);

    private static Bbs757EfbScreenModel Ground(params Bbs757EfbItem[] items) => PageOf("AIRCRAFT", "GROUND SERVICES", items);

    private static Bbs757EfbItem ReqFuelTruck() => Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1132, 577, 196, 42));

    private static Bbs757EfbItem ReqCatering() => Tile("Req catering", "btn:|REQ CATERING@45,36", new Rectangle(912, 723, 196, 42));

    private static Bbs757EfbItem ReqTug(int top = 577) => Tile("Req PB tug", "btn:|REQ PB TUG@61," + (top + 21) / 20, new Rectangle(1132, top, 196, 42));

    private static string Tap(Bbs757EfbItem item) => $"tap {item.Hit.X},{item.Hit.Y}";

    /// <summary>
    /// A tablet behind the press runner: the page it shows, its picture (each capture a 1 x 1 bitmap tagged with the numbers of the
    /// page's picture and of the top bar's; two captures show the same page when their page numbers are, as the real compare leaves
    /// the top bar out, and the same top bar when their bar numbers are), BlueBird's settings file, variables and installed nav data
    /// cycle, a clock the runner's waits move, and what a tap does. What the runner did is kept in order: reads, cycle reads, taps,
    /// alerts, echoes, hooks.
    /// </summary>
    private sealed class FakeTablet
    {
        public FakeTablet(Bbs757EfbScreenModel page) => Page = page;

        public Bbs757EfbScreenModel Page { get; private set; }
        public int Picture { get; private set; }
        public int Bar { get; private set; }
        public long Now { get; private set; }
        public bool Reachable { get; set; } = true;
        public int? Cycle { get; set; }
        public Action<Point>? OnTap { get; set; }
        public Action? OnRead { get; set; }
        public Dictionary<string, Bbs757Value> File { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);
        public List<string> Events { get; } = new();
        public List<Point> Taps { get; } = new();
        public List<string> Alerts { get; } = new();
        public List<string> SettingsRead { get; } = new();

        /// <summary>The tablet shows another page: a new picture.</summary>
        public void Show(Bbs757EfbScreenModel page)
        {
            Page = page;
            Picture++;
        }

        /// <summary>Only the tablet's top bar changed: the page's picture compares the same, the bar's does not.</summary>
        public void ShowTopBar(Bbs757EfbScreenModel page)
        {
            Page = page;
            Bar++;
        }

        /// <summary>A press on <paramref name="item"/> of the page shown now, with the frame that page was read from.</summary>
        public Bbs757EfbPressed Press(Bbs757EfbItem item) => new(item, Page, Capture());

        private Bitmap Capture() => new(1, 1) { Tag = (Picture, Bar) };

        private static string Said(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "unread";

        public Bbs757EfbPressRunner Runner() => new(new Bbs757EfbPressIo
        {
            Capture = Capture,
            Same = (a, b) => (((int, int))a.Tag!).Item1 == (((int, int))b.Tag!).Item1,
            SameTopBar = (a, b) => (((int, int))a.Tag!).Item2 == (((int, int))b.Tag!).Item2,
            ReadInstalledCycle = () =>
            {
                Events.Add("cycle");
                return Cycle;
            },
            Read = _ =>
            {
                OnRead?.Invoke();
                Events.Add("read");
                return Task.FromResult<Bbs757EfbScreenModel?>(Page);
            },
            Tap = (p, _) =>
            {
                Events.Add($"tap {p.X},{p.Y}");
                if (!Reachable) return Task.FromResult(false);
                Taps.Add(p);
                OnTap?.Invoke(p);
                return Task.FromResult(true);
            },
            Wheel = (delta, _) =>
            {
                Events.Add("wheel " + delta.ToString(CultureInfo.InvariantCulture));
                return Task.CompletedTask;
            },
            ReadVar = (name, _) => Task.FromResult<double?>(Vars.TryGetValue(name, out var v) ? v : null),
            ReadRegistered = (key, _) => Task.FromResult<double?>(Vars.TryGetValue(key, out var v) ? v : null),
            ReadSetting = (s, _) =>
            {
                SettingsRead.Add(s.Path);
                return Task.FromResult<Bbs757Value?>(File.TryGetValue(s.Path, out var v) ? v : null);
            },
            Alert = text =>
            {
                Alerts.Add(text);
                Events.Add("alert " + text);
            },
            Delay = (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Now += ms;
                return Task.CompletedTask;
            },
            NowMs = () => Now,
            ArmServiceEcho = s => Events.Add("arm " + s.Key),
            SettleServiceEcho = (s, v) => Events.Add("settle " + s.Key + " " + Said(v)),
            ArmGroundPowerEcho = () => Events.Add("arm GPU"),
            SettleGroundPowerEcho = v => Events.Add("settle GPU " + Said(v)),
            Hooked = (hook, cycle) => Events.Add("hook " + hook + (cycle is int c ? " " + c.ToString(CultureInfo.InvariantCulture) : "")),
        });
    }

    // ---- where a press taps -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_press_on_an_unchanged_frame_taps_the_read_point()
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        // The tile asks for the truck: its words change in place.
        tablet.OnTap = _ => tablet.Show(Ground(Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@56,28",
            new Rectangle(1131, 576, 197, 43)), ReqCatering()));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        // Tapped where the page was read, before anything was read again; nothing said (the window says the tile's new words).
        Assert.Equal(new[] { "tap 1230,598", "read" }, tablet.Events);
        Assert.Equal(new[] { tile.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_press_on_a_changed_frame_reads_again_and_taps_the_new_point()
    {
        // A heading appeared above the tiles after the page was read: the tile is 25 px lower, under another key.
        var tile = ReqTug();
        var tablet = new FakeTablet(Ground(tile));
        var press = tablet.Press(tile);
        var moved = ReqTug(top: 602);
        tablet.Show(Ground(new Bbs757EfbItem(Bbs757EfbKind.Heading, "Services", new Rectangle(273, 540, 120, 30), new Point(333, 555), false, "h:SERVICES"), moved));
        tablet.OnTap = _ => tablet.Show(Ground(Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@61,31", moved.Box)));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEqual(tile.Key, moved.Key);
        Assert.Equal(new[] { "read", Tap(moved), "read" }, tablet.Events);
        Assert.Equal(new[] { new Point(1230, 623) }, tablet.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_press_on_a_vanished_control_taps_nothing_and_says_so(string culture)
    {
        // BlueBird's notification dropped over the tile after the page was read: the reader leaves the covered tile out, and the
        // panel is no control, so nothing may take the tile's place.
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        tablet.Show(Ground(ReqCatering()) with { Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:17Z") } });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "read", "alert Req fuel truck is no longer on the tablet." }, tablet.Events);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_that_turned_into_text_is_gone(string culture)
    {
        // START BOARDING became "Boarding… 10 min" after the page was read (BlueBird started boarding on its own): text is no control.
        // Only the top bar changed, which the page's compare leaves out: the top bar's own compare sees it.
        var start = new Bbs757EfbItem(Bbs757EfbKind.Button, "Start boarding", new Rectangle(1142, 3, 180, 28), new Point(1231, 17), true,
            Bbs757EfbTopBar.BoardingKey, Bbs757EfbTint.Green);
        var tablet = new FakeTablet(Ground(ReqFuelTruck()) with { TopBar = new[] { start } });
        var press = tablet.Press(start);
        tablet.ShowTopBar(Ground(ReqFuelTruck()) with
        {
            TopBar = new[] { new Bbs757EfbItem(Bbs757EfbKind.Text, "Boarding" + Ellipsis + " 10 min", start.Box, new Point(1232, 17), false, Bbs757EfbTopBar.BoardingKey) },
        });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.DoesNotContain(tablet.Events, e => e.StartsWith("hook", StringComparison.Ordinal));
        Assert.Equal(new[] { "read", "alert Start boarding is no longer on the tablet." }, tablet.Events);
    }

    [Fact]
    public async Task A_page_control_is_tapped_whatever_the_top_bar_did()
    {
        // The countdown ticked since the page was read: the page's own compare is still the same, so its tile is tapped as read.
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile) with { TopBar = new[] { Countdown("DEP -00:42") } });
        var press = tablet.Press(tile);
        tablet.ShowTopBar(Ground(tile) with { TopBar = new[] { Countdown("DEP -00:41") } });

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(Tap(tile), tablet.Events[0]);
    }

    [Fact]
    public async Task A_top_bar_control_whose_bar_did_not_change_is_tapped_as_read()
    {
        var start = new Bbs757EfbItem(Bbs757EfbKind.Button, "Start boarding", new Rectangle(1142, 3, 180, 28), new Point(1231, 17), true,
            Bbs757EfbTopBar.BoardingKey, Bbs757EfbTint.Green);
        var tablet = new FakeTablet(Ground(ReqFuelTruck()) with { TopBar = new[] { start } });
        var press = tablet.Press(start);
        tablet.OnTap = _ => tablet.ShowTopBar(Ground(ReqFuelTruck()) with
        {
            TopBar = new[] { new Bbs757EfbItem(Bbs757EfbKind.Text, "Boarding" + Ellipsis + " 10 min", start.Box, new Point(1232, 17), false, Bbs757EfbTopBar.BoardingKey) },
        });

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { Tap(start), "hook StartBoarding" }, tablet.Events.Take(2));
    }

    [Fact]
    public async Task A_control_a_new_question_covers_is_not_tapped()
    {
        // The defuel question came up over the page after it was read: the tablet takes no tap under it. The read that found the
        // question says it, so the press says nothing over it.
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "FUEL", import));
        var press = tablet.Press(import);
        var yes = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);
        tablet.Show(PageOf("AIRCRAFT", "FUEL", import) with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { yes }) });

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    private const string DefuelQuestion = "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?";
    private const string UnitsQuestion = "Mismatch between flight plan and aircraft weight units! Do you want to switch the aircraft unit system?";

    private static Bbs757EfbScreenModel FuelAsked(string question, Bbs757EfbItem answer) =>
        PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45)))
            with { Question = new Bbs757EfbQuestion(question, new[] { answer }) };

    [Fact]
    public async Task A_yes_pressed_on_one_question_is_never_tapped_on_another()
    {
        // The defuel question's YES was pressed, but the tablet now asks about the units, its YES in the same place under the same
        // key: the press was not for this question.
        var yes = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, yes));
        var press = tablet.Press(yes);
        tablet.Show(FuelAsked(UnitsQuestion, yes));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "read", "alert Yes is no longer on the tablet." }, tablet.Events);
    }

    [Fact]
    public async Task A_yes_is_tapped_while_its_question_reads_the_same()
    {
        // The same question read again, its words wobbling a little and its YES a few pixels off: still the YES pressed.
        var yes = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, yes));
        var press = tablet.Press(yes);
        var moved = Tile("Yes", "q:YES", new Rectangle(338, 590, 177, 57), Bbs757EfbTint.Green);
        tablet.Show(FuelAsked("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel ?", moved));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { moved.Hit }, tablet.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tap_that_cannot_run_says_so_and_starts_no_hook(string culture)
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch)) { Reachable = false };
        var press = tablet.Press(fetch);

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { Tap(fetch), "alert The tablet cannot be reached, so nothing was tapped." }, tablet.Events);
    }

    // ---- what a button or tile press says ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tile_that_changed_nothing_says_so(string culture)
    {
        var tug = ReqTug();
        var tablet = new FakeTablet(Ground(tug, ReqCatering()));
        var press = tablet.Press(tug);

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "Req PB tug: no change on the tablet." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_menu_press_whose_page_never_comes_says_so(string culture)
    {
        var fuel = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(130, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, new[] { ReqFuelTruck() }));
        var press = tablet.Press(fuel);

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "The tablet did not show Fuel. Try again, or use Read the tablet again." }, tablet.Alerts);
        Assert.True(tablet.Now >= Bbs757EfbPressRunner.NavigateMaxMs, "read again for four seconds");
    }

    [Fact]
    public async Task A_menu_press_whose_page_comes_late_says_nothing()
    {
        // Measured 2026-10-27: the first change after a tap is often the icon's highlight; the page follows up to 2.4 s later.
        var fuel = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(130, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, new[] { ReqFuelTruck() }));
        var press = tablet.Press(fuel);
        int reads = 0;
        tablet.OnRead = () =>
        {
            if (++reads == 3) tablet.Show(new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { fuel with { Selected = true } }, Array.Empty<Bbs757EfbItem>()));
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Alerts);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task A_tab_press_is_judged_as_a_button_not_as_a_page()
    {
        // A tab changes the list under it, never the page's name: it is not "the tablet did not show Landing gear".
        var tab = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Landing gear", new Rectangle(500, 100, 160, 34), new Point(580, 117), true, "tab:|LANDING GEAR@29,5");
        var other = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Brakes (selected)", new Rectangle(670, 100, 160, 34), new Point(750, 117), true, "tab:|BRAKES@37,5", Selected: true);
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "MAINTENANCE", other, tab));
        var press = tablet.Press(tab);
        tablet.OnTap = _ => tablet.Show(PageOf("AIRCRAFT", "MAINTENANCE", other with { Text = "Brakes", Selected = false },
            tab with { Text = "Landing gear (selected)", Selected = true }));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { Tap(tab), "read" }, tablet.Events);
    }

    [Fact]
    public async Task A_scroll_wheels_the_page_and_reads_it()
    {
        var tablet = new FakeTablet(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true)));

        await tablet.Runner().ScrollAsync(down: true, CancellationToken.None);
        await tablet.Runner().ScrollAsync(down: false, CancellationToken.None);

        Assert.Equal(new[] { "wheel 300", "read", "wheel -300", "read" }, tablet.Events);
    }

    // ---- echoes and hooks ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_fuel_truck_tile_arms_its_echo()
    {
        // The definition's Fuel truck row would say the change the window's tile says itself (CORE-7): its echo is open from right
        // before the tap until the state read after it.
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var tile = page.Items.First(i => i.Text == "Fuel truck off");
        var tablet = new FakeTablet(page);
        tablet.Vars["BBS757_vis_fueltruck"] = 0;
        var press = tablet.Press(tile);
        tablet.OnTap = _ =>
        {
            tablet.Vars["BBS757_vis_fueltruck"] = 1;
            tablet.Show(page with { Items = page.Items.Select(i => ReferenceEquals(i, tile) ? tile with { Text = "Fuel truck on", Tint = Bbs757EfbTint.Green } : i).ToList() });
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "arm FUEL TRUCK", Tap(tile), "read", "settle FUEL TRUCK 1" }, tablet.Events);
    }

    [Fact]
    public async Task The_GPU_tile_arms_and_settles_the_ground_power_echo()
    {
        // Plan 07's echo, as the classic window's run: the Ground Power row is the window's own while its tile is pressed.
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var gpu = page.Items.First(i => i.Text == "GPU removed");
        var tablet = new FakeTablet(page);
        tablet.Vars[Bbs757SharedKeys.GpuConnected] = 0;
        var press = tablet.Press(gpu);
        tablet.OnTap = _ =>
        {
            tablet.Vars[Bbs757SharedKeys.GpuConnected] = 1;
            tablet.Show(page with { Items = page.Items.Select(i => ReferenceEquals(i, gpu) ? gpu with { Text = "GPU connected" } : i).ToList() });
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "arm GPU", Tap(gpu), "read", "settle GPU 1" }, tablet.Events);
    }

    [Fact]
    public async Task An_echo_is_settled_with_nothing_read_when_the_tap_could_not_run()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var tile = page.Items.First(i => i.Text == "Fuel truck off");
        var tablet = new FakeTablet(page) { Reachable = false };
        tablet.Vars["BBS757_vis_fueltruck"] = 0;

        await tablet.Runner().ClickAsync(tablet.Press(tile), CancellationToken.None);

        Assert.Equal(new[] { "arm FUEL TRUCK", Tap(tile), "alert The tablet cannot be reached, so nothing was tapped.", "settle FUEL TRUCK unread" }, tablet.Events);
    }

    [Fact]
    public async Task A_fetch_press_calls_the_hook_after_the_tap()
    {
        // The load watch only listens: it starts once the tap went out, never before it (and never for a tap that could not run).
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch));
        var press = tablet.Press(fetch);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "ACARS DASHBOARD", Tile("Refresh SIMBRIEF ofp", "btn:|REFRESH SIMBRIEF OFP@42,40", fetch.Box)));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { Tap(fetch), "hook Fetch", "read" }, tablet.Events);
    }

    [Fact]
    public async Task The_nav_data_buttons_hook_the_nav_data_watch()
    {
        // The cycle installed is read before the tap (BlueBird's REMOVE may delete the file at once) and handed over with the hook,
        // which goes out only once the tap did.
        foreach (var (words, hook) in new[] { ("Update", Bbs757EfbHook.NavDataUpdate), ("Remove", Bbs757EfbHook.NavDataRemove) })
        {
            var button = Tile(words, "btn:|" + words.ToUpperInvariant() + "@64,16", new Rectangle(1221, 307, 140, 40));
            var tablet = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", button)) { Cycle = 2609 };
            tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRAC DATA", button with { Text = "Updating" + Ellipsis }));

            await tablet.Runner().ClickAsync(tablet.Press(button), CancellationToken.None);

            Assert.Equal(new[] { "cycle", Tap(button), "hook " + hook + " 2609", "read" }, tablet.Events);
        }
    }

    [Fact]
    public async Task A_removal_hands_the_watch_the_cycle_installed_before_the_tap()
    {
        // BlueBird's REMOVE deletes cycle.json as it is tapped: the watch still learns that 2610 was installed.
        var remove = Tile("Remove", "btn:|REMOVE@64,16", new Rectangle(1221, 307, 140, 40));
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", remove)) { Cycle = 2610 };
        tablet.OnTap = _ =>
        {
            tablet.Cycle = null;
            tablet.Show(PageOf("SETTINGS", "AIRAC DATA", remove with { Text = "Update" }));
        };

        await tablet.Runner().ClickAsync(tablet.Press(remove), CancellationToken.None);

        Assert.Equal(new[] { "cycle", Tap(remove), "hook NavDataRemove 2610", "read" }, tablet.Events);
    }

    [Fact]
    public async Task No_cycle_is_read_for_a_press_that_is_no_nav_data_press_or_whose_tap_cannot_run()
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var acars = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch)) { Cycle = 2609 };
        await acars.Runner().ClickAsync(acars.Press(fetch), CancellationToken.None);
        Assert.DoesNotContain("cycle", acars.Events);

        // A nav data press whose tap cannot run reads the cycle and hooks nothing.
        var update = Tile("Update", "btn:|UPDATE@64,16", new Rectangle(1221, 307, 140, 40));
        var airac = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", update)) { Cycle = 2609, Reachable = false };
        await airac.Runner().ClickAsync(airac.Press(update), CancellationToken.None);
        Assert.DoesNotContain(airac.Events, e => e.StartsWith("hook", StringComparison.Ordinal));
    }

    // ---- switches: the wish, and BlueBird's file -----------------------------------------------------------------------

    [Fact]
    public async Task A_switch_already_as_wished_is_not_tapped_and_nothing_is_said()
    {
        // It changed since the page was read (BlueBird, or the pilot's other hand): a tap would undo the wish.
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        var press = tablet.Press(sync);
        tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Equal(new[] { "read" }, tablet.Events);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_settings_switch_not_followed_by_the_file_says_did_not_change(string culture)
    {
        // The pill moved on the page, but BlueBird's file never took the change: the file decides.
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        tablet.File["baro_sync"] = Bbs757Value.Of(false);
        var press = tablet.Press(sync);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "Sync barometers did not change." }, tablet.Alerts);
        Assert.All(tablet.SettingsRead, path => Assert.Equal("baro_sync", path));
        Assert.True(tablet.Now >= Bbs757EfbPressRunner.FileMaxMs, "the file is read for two seconds");
    }

    [Fact]
    public async Task A_settings_switch_the_file_follows_says_nothing()
    {
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        tablet.File["baro_sync"] = Bbs757Value.Of(false);
        var press = tablet.Press(sync);
        tablet.OnTap = _ =>
        {
            tablet.File["baro_sync"] = Bbs757Value.Of(true);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_press_that_switches_a_callout_off_names_it(string culture)
    {
        // Radio altimeter (2500) switched on: BlueBird switches the 2500 call-out off (measured 2026-10-29). The screen reader read
        // the switch the pilot pressed; the one BlueBird switched off only the app can say.
        var height = Switch("2500", "tgl:2500", on: true, y: 400);
        var radio = Switch("Radio altimeter (2500)", "tgl:RADIO ALTIMETER (2500)", on: false, y: 450);
        var tablet = new FakeTablet(PageOf("SETTINGS", "CALLOUTS", height, radio));
        tablet.File[Egpws + "2500"] = Bbs757Value.Of(true);
        tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(false);
        var press = tablet.Press(radio);
        tablet.OnTap = _ =>
        {
            tablet.File[Egpws + "2500"] = Bbs757Value.Of(false);
            tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(true);
            tablet.Show(PageOf("SETTINGS", "CALLOUTS", height with { On = false }, radio with { On = true }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "2500 turned off" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_callout_that_was_already_off_is_not_named()
    {
        var height = Switch("2500", "tgl:2500", on: false, y: 400);
        var radio = Switch("Radio altimeter (2500)", "tgl:RADIO ALTIMETER (2500)", on: false, y: 450);
        var tablet = new FakeTablet(PageOf("SETTINGS", "CALLOUTS", height, radio));
        tablet.File[Egpws + "2500"] = Bbs757Value.Of(false);
        tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(false);
        var press = tablet.Press(radio);
        tablet.OnTap = _ =>
        {
            tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(true);
            tablet.Show(PageOf("SETTINGS", "CALLOUTS", height, radio with { On = true }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [InlineData(1L, false)]   // SayIntentions stored: the other choice, as wished
    [InlineData(0L, true)]    // still Hoppie: the tablet did not change it
    public async Task A_two_choice_setting_is_judged_by_the_choice_that_was_not_set(long stored, bool notChanged)
    {
        // CPDLC network reads Hoppie with its pill off, while EHSI up reference reads HDG with its pill on (measured 2026-10-29): the
        // pill's side names no word, so a tap is wished to store the choice the row did not read before it.
        var cpdlc = Switch("CPDLC network", "tgl:CPDLC NETWORK", on: false, value: "Hoppie");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", cpdlc));
        tablet.File["cpdlc_network"] = Bbs757Value.Of(0L);
        var press = tablet.Press(cpdlc);
        tablet.OnTap = _ =>
        {
            tablet.File["cpdlc_network"] = Bbs757Value.Of(stored);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", cpdlc with { On = true, Value = "SayIntentions" }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Equal(notChanged ? new[] { "CPDLC network did not change." } : Array.Empty<string>(), tablet.Alerts);
    }

    [Fact]
    public async Task A_two_choice_setting_switched_off_is_judged_by_its_other_choice_too()
    {
        // EHSI up reference: HDG reads on, so switching it off is wished to store TRK, in the registration's file.
        var ehsi = Switch("EHSI up reference", "tgl:EHSI UP REFERENCE", on: true, value: "HDG");
        var tablet = new FakeTablet(PageOf("SETTINGS", "LIVERY OPTIONS", ehsi));
        tablet.File["options.ehsi.up_reference"] = Bbs757Value.Of("hdg");
        var press = tablet.Press(ehsi);
        tablet.OnTap = _ =>
        {
            tablet.File["options.ehsi.up_reference"] = Bbs757Value.Of("trk");
            tablet.Show(PageOf("SETTINGS", "LIVERY OPTIONS", ehsi with { On = false, Value = "TRK" }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: false, CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
        Assert.Contains("options.ehsi.up_reference", tablet.SettingsRead);
    }

    [Theory]
    [InlineData(-1L, false)]   // the empty box: off, as wished
    [InlineData(40L, true)]    // the distance kept
    public async Task The_TOD_pause_distance_switched_off_is_judged_by_its_empty_box(long stored, bool notChanged)
    {
        var tod = Switch("TOD pause distance (NM)", "tgl:TOD PAUSE DISTANCE (NM)", on: true, value: "40");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", tod));
        tablet.File["tod_pause_distance"] = Bbs757Value.Of(40L);
        var press = tablet.Press(tod);
        tablet.OnTap = _ =>
        {
            tablet.File["tod_pause_distance"] = Bbs757Value.Of(stored);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", tod with { On = false, Value = "" }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: false, CancellationToken.None);

        Assert.Equal(notChanged ? new[] { "TOD pause distance (NM) did not change." } : Array.Empty<string>(), tablet.Alerts);
    }

    [Fact]
    public async Task A_misread_settings_label_is_judged_by_the_page_and_never_by_a_file_key()
    {
        // "Sync barometrs" matches no setting: BlueBird's file is never read for it, whichever key it might have been.
        var misread = Switch("Sync barometrs", "tgl:SYNC BAROMETRS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", misread));
        var press = tablet.Press(misread);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", misread with { On = true }));

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.SettingsRead);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_switch_the_page_never_follows_says_did_not_change(string culture)
    {
        // A checklist line (no setting): judged by the page, read again until four seconds after the tap.
        var line = new Bbs757EfbItem(Bbs757EfbKind.Checkbox, "Parking brake: SET", new Rectangle(286, 300, 400, 30), new Point(298, 315), true,
            "chk:PARKING BRAKE", On: false);
        var tablet = new FakeTablet(PageOf("CHECKLIST", "BEFORE START", line));
        var press = tablet.Press(line);

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "Parking brake: SET did not change." }, tablet.Alerts);
        Assert.Empty(tablet.SettingsRead);
        Assert.True(tablet.Now >= Bbs757EfbPressRunner.FollowMaxMs);
    }

    [Fact]
    public async Task A_settings_file_that_cannot_be_read_leaves_the_judgement_to_the_page()
    {
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));   // no file
        var press = tablet.Press(sync);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Alerts);
        Assert.NotEmpty(tablet.SettingsRead);
    }

    // ---- sliders --------------------------------------------------------------------------------------------------------

    // EFB BACKGROUND, measured (efb-settings.md): a tap at x 804 is DEFAULT, 826 1, 875 2, 900 3, ... 1025 8, 1050 and right 9.
    private static readonly int[] BackgroundPlaces = { 804, 826, 875, 900, 925, 950, 975, 1000, 1025, 1050 };
    private static readonly string[] BackgroundWords = { "Default", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    private static Bbs757EfbItem Background(int choice) =>
        new(Bbs757EfbKind.Slider, "EFB background", new Rectangle(273, 461, 190, 30), new Point(BackgroundPlaces[choice], 476), true,
            "sld:EFB BACKGROUND", Value: BackgroundWords[choice], Slider: new Bbs757EfbSlider(804, 1050, BackgroundPlaces[choice]));

    [Fact]
    public void The_reader_gives_the_background_slider_its_measured_track()
    {
        var general = Bbs757EfbScreenTests.Load("dim", "50-settings-general");
        var background = general.Items.Single(i => i.Text == "EFB background");
        Assert.Equal((804, 1050), (background.Slider!.TrackLeft, background.Slider.TrackRight));
        // The other settings sliders keep the track measured for them (895 to 1190, widened to the knob).
        var timer = general.Items.Single(i => i.Text == "Notifications fade-out timer");
        Assert.True(timer.Slider!.TrackLeft <= 895 && timer.Slider.TrackRight >= 1190, $"{timer.Slider.TrackLeft}-{timer.Slider.TrackRight}");
    }

    [Fact]
    public async Task The_background_slider_reaches_every_choice_within_four_taps()
    {
        // A tablet built from the measured places: a tap at x sets the largest choice whose place is at or left of x (a tap left of
        // the track changes nothing). Its choices sit unevenly at the left; the closed loop corrects what the even spread misses.
        for (int start = 0; start < 10; start++)
            for (int target = 0; target < 10; target++)
            {
                int value = start;
                var tablet = new FakeTablet(PageOf("SETTINGS", "GENERAL", Background(value)));
                tablet.File["background_num"] = Bbs757Value.Of((long)value);
                var press = tablet.Press(Background(value));
                tablet.OnTap = p =>
                {
                    int at = Array.FindLastIndex(BackgroundPlaces, place => p.X >= place);
                    if (at < 0 || at == value) return;
                    value = at;
                    tablet.File["background_num"] = Bbs757Value.Of((long)value);
                    tablet.Show(PageOf("SETTINGS", "GENERAL", Background(value)));
                };

                await tablet.Runner().SliderAsync(press, BackgroundWords[target], CancellationToken.None);

                Assert.True(value == target, $"from {start} to {target}: the tablet ended at {value}");
                Assert.True(tablet.Taps.Count <= Bbs757EfbPress.MaxSliderTaps, $"from {start} to {target}: {tablet.Taps.Count} taps");
                Assert.True(tablet.Alerts.Count == 0, $"from {start} to {target}: " + string.Join(" | ", tablet.Alerts));
                if (start == target) Assert.Empty(tablet.Taps);
            }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_the_tablet_does_not_follow_could_not_be_set(string culture)
    {
        var irs = new Bbs757EfbItem(Bbs757EfbKind.Slider, "IRS alignment time", new Rectangle(273, 513, 255, 33), new Point(1190, 529), true,
            "sld:IRS ALIGNMENT TIME", Value: "Full (7 min+)", Slider: new Bbs757EfbSlider(895, 1190, 1190));
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", irs));
        tablet.File["irs_align_time"] = Bbs757Value.Of(9L);
        var press = tablet.Press(irs);

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "2 min", CancellationToken.None));

        Assert.Equal(Bbs757EfbPress.MaxSliderTaps, tablet.Taps.Count);
        Assert.Equal(new[] { "IRS alignment time could not be set to 2 min." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_number_slider_is_tapped_at_its_place_and_judged_by_the_knob(string culture)
    {
        // The cabin lights have no choices: a percent along the track, read back from where the knob is.
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Slider: new Bbs757EfbSlider(500, 900, 500));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        var press = tablet.Press(ceiling);
        tablet.OnTap = p => tablet.Show(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling with { Slider = new Bbs757EfbSlider(500, 900, p.X) }));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "30", CancellationToken.None));
        Assert.Equal(new[] { new Point(620, 315) }, tablet.Taps);
        Assert.Empty(tablet.Alerts);

        // A tablet that leaves the knob where it was.
        var stuck = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        await TestCulture.Run(culture, () => stuck.Runner().SliderAsync(stuck.Press(ceiling), "70", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 70 percent." }, stuck.Alerts);
    }

    // ---- the pushes: a press is the push's it was made on ---------------------------------------------------------------

    [Fact]
    public void A_press_from_an_older_push_finds_that_push_and_its_page()
    {
        using var pushes = new Bbs757EfbPushes();
        var first = pushes.Show(Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services"), new Bitmap(1, 1));
        var built1 = pushes.Push(first, null, NoExtras);
        var second = pushes.Show(Bbs757EfbScreenTests.Load("bright", "53-settings-callouts"), new Bitmap(1, 1));
        var built2 = pushes.Push(second, null, NoExtras);

        // The window still showed the first push when the pilot pressed Fuel truck off: that press is the first page's.
        int aidx = int.Parse(Field(built1, "Fuel truck off", "aidx"));
        Assert.True(pushes.TryFind(aidx, out var action, out var page));
        Assert.Equal("Fuel truck off", action!.Item!.Text);
        Assert.Same(first, page);
        // Another page's elements are other elements, the window's own Home and Read the tablet again too: other numbers.
        Assert.Empty(Numbers(built1).Intersect(Numbers(built2)));
        // A number no push gave out finds nothing.
        Assert.False(pushes.TryFind(0, out _, out _));
        Assert.False(pushes.TryFind(Numbers(built1).Concat(Numbers(built2)).Max() + 1, out _, out _));
    }

    private static List<int> Numbers(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => int.Parse(b.Data[$"items.{i}.aidx"])).ToList();

    [Fact]
    public void An_element_keeps_its_number_while_it_stays_on_the_page()
    {
        // The window's list fallback keeps a control's number from when it built the control, while the page's elements stay; the
        // browser patches it. Either way a press names the element it was made on.
        using var pushes = new Bbs757EfbPushes();
        var ground = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var page = pushes.Show(ground, new Bitmap(1, 1));
        var before = pushes.Push(page, null, NoExtras);
        var withAlert = pushes.Push(page, "Req PB tug: no change on the tablet.", NoExtras);
        var reread = pushes.Push(pushes.Show(ground with { }, new Bitmap(1, 1)), null, NoExtras);

        Assert.Equal(Numbers(before), Numbers(withAlert).Skip(1));
        Assert.Equal(Numbers(before), Numbers(reread));
        Assert.DoesNotContain(Numbers(withAlert)[0], Numbers(before));   // the alert is an element of its own

        // An element gone from the page and back is a new element: a number never given out before.
        var away = pushes.Push(pushes.Show(Bbs757EfbScreenTests.Load("bright", "53-settings-callouts"), new Bitmap(1, 1)), null, NoExtras);
        var back = pushes.Push(pushes.Show(ground, new Bitmap(1, 1)), null, NoExtras);
        Assert.Empty(Numbers(back).Intersect(Numbers(before).Concat(Numbers(withAlert)).Concat(Numbers(away))));
    }

    [Fact]
    public void Each_question_numbers_its_answers_apart()
    {
        // Every question's answers are keyed q:YES, q:NO: numbered by key alone, the units question's YES would take the defuel
        // question's number, and a YES pressed on the one would find the other.
        using var pushes = new Bbs757EfbPushes();
        var yes = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);
        var defuel = pushes.Show(FuelAsked(DefuelQuestion, yes), null);
        var a = pushes.Push(defuel, null, NoExtras);
        var b = pushes.Push(pushes.Show(FuelAsked(UnitsQuestion, yes), null), null, NoExtras);

        int yesA = int.Parse(Field(a, "Yes", "aidx")), yesB = int.Parse(Field(b, "Yes", "aidx"));
        Assert.NotEqual(yesA, yesB);
        Assert.True(pushes.TryFind(yesA, out _, out var page));
        Assert.Same(defuel, page);

        // The same question read again, its words wobbling, keeps its answer's number.
        var again = pushes.Push(pushes.Show(FuelAsked(UnitsQuestion.Replace("system?", "system ?"), yes), null), null, NoExtras);
        Assert.Equal(yesB, int.Parse(Field(again, "Yes", "aidx")));
    }

    [Fact]
    public void Only_the_newest_pages_keep_their_frames()
    {
        using var pushes = new Bbs757EfbPushes();
        var pages = Enumerable.Range(0, Bbs757EfbPushes.KeptFrames + 2).Select(_ => pushes.Show(Ground(ReqFuelTruck()), new Bitmap(1, 1))).ToList();

        Assert.All(pages.Take(2), p => Assert.Null(p.Frame));
        Assert.All(pages.Skip(2), p => Assert.NotNull(p.Frame));
    }

    [Fact]
    public void Old_pushes_are_let_go()
    {
        using var pushes = new Bbs757EfbPushes();
        var builds = new List<Bbs757EfbElements.Built>();
        for (int i = 0; i < Bbs757EfbPushes.KeptPushes + 1; i++)
            builds.Add(pushes.Push(pushes.Show(PageOf("AIRCRAFT", "SECTION " + i, ReqFuelTruck()), null), null, NoExtras));

        Assert.False(pushes.TryFind(builds[0].Actions.Keys.First(), out _, out _));
        Assert.True(pushes.TryFind(builds[1].Actions.Keys.First(), out _, out _));
    }

    // ---- what a read says by itself: a new question, BlueBird's new notifications ----------------------------------------

    private static readonly Bbs757EfbItem YesAnswer = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);

    private static Bbs757EfbScreenModel Asked(string text) =>
        Ground(ReqFuelTruck()) with { Question = new Bbs757EfbQuestion(text, new[] { YesAnswer }) };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_new_question_is_said_once(string culture)
    {
        var news = new Bbs757EfbReadNews();
        var quiet = Ground(ReqFuelTruck());
        var asked = Asked("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?");

        var said = TestCulture.Run(culture, () => new[]
        {
            news.After(quiet, long.MaxValue),
            news.After(asked, long.MaxValue),
            news.After(asked with { }, long.MaxValue),                 // still up: said already
            news.After(Asked("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel ?"), long.MaxValue),   // read again
            news.After(quiet, long.MaxValue),                          // answered
            news.After(asked, 100),                                    // asked again, right after the pilot's own press: said
            news.After(Asked("Fuel adjustment is complete!"), long.MaxValue),   // another question at once
        });

        Assert.Equal(new[]
        {
            null,
            "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?",
            null,
            null,
            null,
            "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?",
            "Fuel adjustment is complete!",
        }, said);
    }

    [Fact]
    public void The_first_read_takes_the_notifications_as_they_are()
    {
        // The window did not see them appear: the list already open, say. A later entry is new.
        var news = new Bbs757EfbReadNews();
        var listed = Ground(ReqFuelTruck()) with
        {
            Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:17Z"), new Bbs757EfbNotice("R3 door closed.", "12:44Z") },
        };
        var more = listed with { Notices = new[] { new Bbs757EfbNotice("Chocks attached.", "14:20Z") }.Concat(listed.Notices).ToList() };

        Assert.Null(news.After(listed, long.MaxValue));
        Assert.Equal("Chocks attached.", news.After(more, long.MaxValue));
        Assert.Null(news.After(more, long.MaxValue));
    }

    [Fact]
    public void A_notification_right_after_the_pilots_own_press_is_not_said()
    {
        var news = new Bbs757EfbReadNews();
        var quiet = Ground(ReqFuelTruck());
        var toast = quiet with { Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:17Z") } };

        Assert.Null(news.After(quiet, long.MaxValue));
        Assert.Null(news.After(toast, Bbs757EfbNotices.QuietAfterPressMs - 1));   // the press's own outcome says enough
        Assert.Null(news.After(toast, long.MaxValue));                            // and it is not new any more
    }

    [Fact]
    public void A_question_and_a_notification_together_are_one_text_the_question_first()
    {
        // REFRESH SIMBRIEF OFP with the aircraft in kilograms: the units question and "SIMBRIEF OFP RECEIVED." at once. One alert at
        // a time, so one text.
        var news = new Bbs757EfbReadNews();
        var page = Bbs757EfbScreenTests.Load("bright", "23-units-question");
        Assert.NotNull(page.Question);
        Assert.NotEmpty(page.Notices);

        Assert.Null(news.After(page with { Question = null, Notices = Array.Empty<Bbs757EfbNotice>() }, long.MaxValue));
        Assert.Equal(page.Question!.Text + " " + string.Join(" ", page.Notices.Select(n => n.Text)), news.After(page, long.MaxValue));
    }

    // ---- the top bar's own signature, and reads that agree ----------------------------------------------------------------

    [Fact]
    public void The_top_bar_right_part_has_a_signature_of_its_own()
    {
        using var still = new Bitmap(1397, 1048);
        using var countdown = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(countdown)) g.FillRectangle(Brushes.White, 1029, 4, 100, 20);   // "DEP -00:42"
        using var clock = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(clock)) g.FillRectangle(Brushes.White, 40, 4, 120, 20);         // the clock, at the left
        using var page = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(page)) g.FillRectangle(Brushes.Red, 300, 300, 300, 200);

        var bar = Bbs757EfbBridge.TopBarSignature(still);
        Assert.Equal(32 * 4, bar.Length);
        Assert.True(Bbs757EfbBridge.Differs(bar, Bbs757EfbBridge.TopBarSignature(countdown)));
        Assert.False(Bbs757EfbBridge.Differs(bar, Bbs757EfbBridge.TopBarSignature(clock)));
        Assert.False(Bbs757EfbBridge.Differs(bar, Bbs757EfbBridge.TopBarSignature(page)));
        // ...which the page's signature leaves out.
        Assert.False(Bbs757EfbBridge.Differs(Bbs757EfbBridge.Signature(still), Bbs757EfbBridge.Signature(countdown)));
    }

    private static Bbs757EfbItem Countdown(string text) =>
        new(Bbs757EfbKind.Text, text, new Rectangle(1029, 0, 106, 28), new Point(1082, 14), false, Bbs757EfbTopBar.DepartureKey);

    [Fact]
    public async Task Reads_that_differ_only_in_the_top_bar_and_the_notifications_agree()
    {
        // The countdown ticked and a notification passed between two reads of a page that never holds still: the page itself read
        // the same, so no third read and no warning; the later read is taken.
        var first = Ground(ReqFuelTruck()) with { TopBar = new[] { Countdown("DEP -00:42") } };
        var second = Ground(ReqFuelTruck()) with
        {
            TopBar = new[] { Countdown("DEP -00:41") },
            Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:17Z") },
        };
        int again = 0;

        var (taken, reads, unclear) = await Bbs757EfbBridge.AgreeAsync(first, () => { again++; return Task.FromResult<Bbs757EfbScreenModel?>(second); }, m => m);

        Assert.Same(second, taken);
        Assert.Equal(2, reads);
        Assert.False(unclear);
        Assert.Equal(1, again);
    }

    [Fact]
    public async Task Reads_that_differ_on_the_page_are_decided_by_a_third()
    {
        var off = Ground(Tile("Fuel truck off", "btn:|FUEL TRUCK@61,29", new Rectangle(1132, 577, 196, 42)));
        var misread = Ground(Tile("Fuel truck of", "btn:|FUEL TRUCK OF@61,29", new Rectangle(1132, 577, 196, 42)));
        var thirdOff = off with { };
        var reads = new Queue<Bbs757EfbScreenModel>(new[] { misread, thirdOff });

        var (taken, count, unclear) = await Bbs757EfbBridge.AgreeAsync(off, () => Task.FromResult<Bbs757EfbScreenModel?>(reads.Dequeue()), m => m);

        Assert.Same(thirdOff, taken);
        Assert.Equal(3, count);
        Assert.False(unclear);
    }

    [Fact]
    public async Task Three_reads_that_all_differ_take_the_last_with_a_warning()
    {
        var a = Ground(ReqFuelTruck());
        var b = Ground(ReqCatering());
        var c = Ground(ReqTug());
        var reads = new Queue<Bbs757EfbScreenModel>(new[] { b, c });

        var (taken, count, unclear) = await Bbs757EfbBridge.AgreeAsync(a, () => Task.FromResult<Bbs757EfbScreenModel?>(reads.Dequeue()), m => m);

        Assert.Same(c, taken);
        Assert.Equal(3, count);
        Assert.True(unclear);
    }

    [Fact]
    public async Task A_second_read_that_cannot_be_made_takes_the_first()
    {
        var a = Ground(ReqFuelTruck());

        var (taken, count, unclear) = await Bbs757EfbBridge.AgreeAsync(a, () => Task.FromResult<Bbs757EfbScreenModel?>(null), m => m);

        Assert.Same(a, taken);
        Assert.Equal(1, count);
        Assert.False(unclear);
    }

    // ---- the bridge itself: what a read pushes, and the AIRAC page's extras ------------------------------------------------

    /// <summary>A bridge whose loop never started, made with no synchronization context (its pushes raised inline), each push
    /// kept; the sign-in state and flow are the test's.</summary>
    private sealed class BridgeHarness : IDisposable
    {
        private readonly CoherentBbs757EfbClient _client = new(() => "");

        public BridgeHarness()
        {
            SynchronizationContext? saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                Bridge = new Bbs757EfbBridge(_client, new Bbs757EfbBridgeIo
                {
                    ReadCamera = () => Task.FromResult<(int type, int index)?>(null),
                    SetCamera = (_, _) => { },
                    ReadRegistered = (_, _) => Task.FromResult<double?>(null),
                    ReadSetting = (_, _) => Task.FromResult<Bbs757Value?>(null),
                    SignedIn = _ =>
                    {
                        SignedInReads++;
                        return Task.FromResult(SignedInAnswer);
                    },
                    SignIn = ct => SignInFlow(ct),
                    Hooked = (hook, _) => Hooks.Add(hook),
                    ArmServiceEcho = _ => { },
                    SettleServiceEcho = (_, _) => { },
                    ArmGroundPowerEcho = () => { },
                    SettleGroundPowerEcho = _ => { },
                    LoadOcr = () => throw new FileNotFoundException("no models in this test"),
                    OcrFailed = _ => { },
                });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
            Bridge.StateUpdated += (_, e) => Pushes.Add(e.Data);
        }

        public Bbs757EfbBridge Bridge { get; }
        public bool? SignedInAnswer { get; set; }
        public int SignedInReads { get; private set; }
        public Func<CancellationToken, Task<string>> SignInFlow { get; set; } = _ => Task.FromResult("");
        public List<Bbs757EfbHook> Hooks { get; } = new();
        public List<Dictionary<string, string>> Pushes { get; } = new();

        public void Dispose()
        {
            Bridge.Dispose();
            _client.Dispose();
        }
    }

    private static List<string> Texts(Dictionary<string, string> push) =>
        Enumerable.Range(0, push.Keys.Count(k => k.EndsWith(".text", StringComparison.Ordinal))).Select(i => push[$"items.{i}.text"]).ToList();

    private static int IndexOf(Dictionary<string, string> push, string text)
    {
        int index = Texts(push).IndexOf(text);
        Assert.True(index >= 0, $"No element '{text}': " + string.Join(" | ", Texts(push)));
        return index;
    }

    private static Bbs757EfbScreenModel AiracPage() => PageOf("SETTINGS", "AIRAC DATA",
        new Bbs757EfbItem(Bbs757EfbKind.Row, "SimBrief ID number", new Rectangle(273, 200, 250, 30), new Point(398, 215), false, "row:SIMBRIEF ID NUMBER", Value: "set"));

    [Fact]
    public void A_read_with_a_new_question_is_pushed_with_it_as_the_one_alert()
    {
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding"), new Bitmap(1, 1), unclear: false);
        var asked = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");

        h.Bridge.TakeRead(asked, new Bitmap(1, 1), unclear: false);

        var push = h.Pushes[^1];
        Assert.Equal("alert", push["items.0.kind"]);
        Assert.Equal(asked.Question!.Text, push["items.0.text"]);
        // The question's text stays on the page, first, as text; the alert is the only one.
        Assert.Equal(asked.Question.Text, push["items.1.text"]);
        Assert.Equal("", push["items.1.kind"]);
        Assert.Single(push, kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");

        // Read again with the question still up and nothing changed: nothing new to show or say.
        int pushes = h.Pushes.Count;
        h.Bridge.TakeRead(asked with { }, new Bitmap(1, 1), unclear: false);
        Assert.Equal(pushes, h.Pushes.Count);
    }

    [Fact]
    public void Another_page_gets_other_numbers()
    {
        // A press made on the first page while the second was being pushed still names the first page's element.
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services"), new Bitmap(1, 1), unclear: false);
        h.Bridge.TakeRead(Bbs757EfbScreenTests.Load("bright", "53-settings-callouts"), new Bitmap(1, 1), unclear: false);

        var first = h.Pushes[^2].Where(kv => kv.Key.EndsWith(".aidx", StringComparison.Ordinal)).Select(kv => kv.Value);
        var second = h.Pushes[^1].Where(kv => kv.Key.EndsWith(".aidx", StringComparison.Ordinal)).Select(kv => kv.Value);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public async Task The_AIRAC_page_offers_the_sign_in_while_signed_out_and_shows_its_code_while_it_is_spoken()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(AiracPage(), new Bitmap(1, 1), unclear: false);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        var push = h.Pushes[^1];
        int signIn = IndexOf(push, Bbs757EfbExtras.SignInText);
        Assert.Equal("x:navigraph", push[$"items.{signIn}.key"]);
        Assert.Equal("button", push[$"items.{signIn}.kind"]);

        List<string>? whileSpoken = null;
        h.SignInFlow = _ =>
        {
            // The flow's early line, before the code is spoken (Bbs757NavigraphIo.Show).
            h.Bridge.ShowSignInLine("Navigraph code: ABCD-1234");
            whileSpoken = Texts(h.Pushes[^1]);
            return Task.FromResult("Navigraph code: ABCD-1234");
        };
        await h.Bridge.HandleAsync("click_display_element", new Dictionary<string, string> { ["index"] = push[$"items.{signIn}.aidx"] }, CancellationToken.None);

        Assert.Contains("Navigraph code: ABCD-1234", whileSpoken!);
        var after = h.Pushes[^1];
        int code = IndexOf(after, "Navigraph code: ABCD-1234");
        Assert.Equal("x:navigraph-code", after[$"items.{code}.key"]);
        Assert.Equal("", after[$"items.{code}.kind"]);
    }

    [Fact]
    public async Task A_press_queued_when_the_window_hid_never_runs()
    {
        // Pressed, then the window hid before the loop came to the press, and was shown again later: the press is dropped, as the
        // tablet the pilot saw then is not the one shown now.
        using var h = new BridgeHarness { SignedInAnswer = false };
        int signIns = 0;
        h.SignInFlow = _ =>
        {
            signIns++;
            return Task.FromResult("Navigraph code: ABCD-1234");
        };
        h.Bridge.TakeRead(AiracPage(), new Bitmap(1, 1), unclear: false);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        var push = h.Pushes[^1];
        var press = new Dictionary<string, string> { ["index"] = push[$"items.{IndexOf(push, Bbs757EfbExtras.SignInText)}.aidx"] };

        h.Bridge.EnqueueCommand("click_display_element", press);
        h.Bridge.SetActive(false);
        h.Bridge.SetActive(true);

        Assert.False(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.Equal(0, signIns);

        // A press made while the window shows runs.
        h.Bridge.EnqueueCommand("click_display_element", press);
        Assert.True(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.Equal(1, signIns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task No_sign_in_is_offered_signed_in_or_unread(bool? signedIn)
    {
        using var h = new BridgeHarness { SignedInAnswer = signedIn };
        h.Bridge.TakeRead(AiracPage(), new Bitmap(1, 1), unclear: false);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        Assert.Equal(1, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }

    [Fact]
    public async Task The_sign_in_is_read_only_on_the_AIRAC_page()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services"), new Bitmap(1, 1), unclear: false);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        Assert.Equal(0, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }
}
