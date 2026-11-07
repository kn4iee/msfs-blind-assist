using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 tablet window presses what the pilot presses, reading the tablet from the module's memory (the owner's ruling of
/// 2026-10-29: the window refuses nothing; only the press runner's precision checks may stop a tap). The press runner
/// (<see cref="Bbs757EfbPressRunner"/>) runs over a fake tablet: a page, BlueBird's settings file and variables, and a clock its
/// waits move; a tap and a scripted page change. Everything spoken is checked under en-US, de-DE and sv-SE (CI runs under en-US).
/// Pure element-building is in <see cref="Bbs757EfbElementsTests"/>.
/// </summary>
public class Bbs757EfbBridgeTests
{
    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

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

    /// <summary>A ground-service tile (keyed so <see cref="Bbs757EfbScreen.KeyWords"/> gives the service's word).</summary>
    private static Bbs757EfbItem FuelTruck(string state) =>
        Tile("Fuel truck " + state, "btn:|FUEL TRUCK@61,29", new Rectangle(1132, 577, 196, 42),
            state == "on" ? Bbs757EfbTint.Green : Bbs757EfbTint.None);
    private static Bbs757EfbItem Gpu(string state) => Tile("GPU " + state, "btn:|GPU@16,16", new Rectangle(300, 300, 196, 42));

    private static string Tap(Bbs757EfbItem item) => $"tap {item.Hit.X},{item.Hit.Y}";

    private static Bbs757EfbItem Countdown(string text) =>
        new(Bbs757EfbKind.Text, text, new Rectangle(1029, 0, 106, 28), new Point(1082, 14), false, Bbs757EfbTopBar.DepartureKey);

    [Fact]
    public void An_alert_is_the_first_element_and_assertive()
    {
        var b = Bbs757EfbElements.Build(null, "The tablet could not be read.", NoExtras);
        Assert.Equal("BlueBird tablet", b.Data["page"]);
        Assert.Equal("alert", b.Data["items.0.kind"]);
        Assert.Equal("assertive", b.Data["items.0.live"]);
        Assert.Equal("alert", b.Data["items.0.key"]);
    }

    /// <summary>
    /// A tablet behind the press runner: the page it shows now (a memory read returns it), BlueBird's settings file, variables and
    /// installed nav data cycle, a clock the runner's waits move, and what a tap does. What the runner did is kept in order (reads,
    /// cycle reads, taps, alerts, echoes, hooks); <see cref="Flow"/> is that order without the reads, which a press makes many of.
    /// </summary>
    private sealed class FakeTablet
    {
        public FakeTablet(Bbs757EfbScreenModel page) => Page = page;

        public Bbs757EfbScreenModel Page { get; private set; }
        public long Now { get; private set; }
        public bool Reachable { get; set; } = true;
        public int? Cycle { get; set; }
        public Action<Point>? OnTap { get; set; }
        public Action<int, int, int>? OnDrag { get; set; }
        public bool DragReachable { get; set; } = true;
        public List<(int X, int Y0, int Y1)> Drags { get; } = new();
        public Dictionary<string, Bbs757Value> File { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);
        public List<string> Events { get; } = new();
        public List<Point> Taps { get; } = new();
        public List<string> Alerts { get; } = new();
        public List<string> SettingsRead { get; } = new();

        /// <summary>The tablet shows another page.</summary>
        public void Show(Bbs757EfbScreenModel page) => Page = page;

        /// <summary>A press on <paramref name="item"/> of the page shown now.</summary>
        public Bbs757EfbPressed Press(Bbs757EfbItem item) => new(item, Page);

        /// <summary>The order the runner acted in, without the reads.</summary>
        public List<string> Flow => Events.Where(e => e != "read").ToList();

        private static string Said(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "unread";

        public Bbs757EfbPressRunner Runner() => new(new Bbs757EfbPressIo
        {
            ReadInstalledCycle = () => { Events.Add("cycle"); return Cycle; },
            Read = _ => { Events.Add("read"); return Task.FromResult<Bbs757EfbScreenModel?>(Page); },
            Tap = (p, _) =>
            {
                Events.Add($"tap {p.X},{p.Y}");
                if (!Reachable) return Task.FromResult(false);
                Taps.Add(p);
                OnTap?.Invoke(p);
                return Task.FromResult(true);
            },
            Drag = (x, y0, y1, _) =>
            {
                Events.Add($"drag {x},{y0},{y1}");
                if (!DragReachable) return Task.FromResult(false);
                Drags.Add((x, y0, y1));
                OnDrag?.Invoke(x, y0, y1);
                return Task.FromResult(true);
            },
            ReadVar = (name, _) => Task.FromResult<double?>(Vars.TryGetValue(name, out var v) ? v : null),
            ReadRegistered = (key, _) => Task.FromResult<double?>(Vars.TryGetValue(key, out var v) ? v : null),
            ReadSetting = (s, _) => { SettingsRead.Add(s.Path); return Task.FromResult<Bbs757Value?>(File.TryGetValue(s.Path, out var v) ? v : null); },
            Alert = text => { Alerts.Add(text); Events.Add("alert " + text); },
            Delay = (ms, ct) => { ct.ThrowIfCancellationRequested(); Now += ms; return Task.CompletedTask; },
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
    public async Task A_tab_from_the_real_maintenance_page_is_relocated_and_tapped()
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var hydraulic = Assert.Single(page.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Name == "maintenance_hydraulic_tab_button");
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(hydraulic), CancellationToken.None);

        Assert.Equal(new[] { hydraulic.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_press_reads_relocates_and_taps_the_tile_in_place()
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        tablet.OnTap = _ => tablet.Show(Ground(Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@56,28",
            new Rectangle(1131, 576, 197, 43)), ReqCatering()));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);           // tapped where the fresh read placed it
        Assert.Equal(new[] { "Requesting" + Ellipsis }, tablet.Alerts);   // its new words, once
    }

    [Fact]
    public async Task A_press_on_a_moved_control_taps_the_new_point()
    {
        // A heading appeared above the tiles, so the tile is 25 px lower under another key; the fresh read finds it there.
        var tile = ReqTug();
        var tablet = new FakeTablet(Ground(tile));
        var press = tablet.Press(tile);
        var moved = ReqTug(top: 602);
        tablet.Show(Ground(new Bbs757EfbItem(Bbs757EfbKind.Heading, "Services", new Rectangle(273, 540, 120, 30), new Point(333, 555), false, "h:SERVICES"), moved));
        tablet.OnTap = _ => tablet.Show(Ground(Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@61,31", moved.Box)));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEqual(tile.Key, moved.Key);
        Assert.Equal(new[] { moved.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Requesting" + Ellipsis }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_press_on_a_vanished_control_taps_nothing_and_says_so(string culture)
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        tablet.Show(Ground(ReqCatering()) with { Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:17Z") } });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Req fuel truck is no longer on the tablet." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_that_turned_into_text_is_gone(string culture)
    {
        // START BOARDING became "Boarding… 10 min" after the page was read: text is no control.
        var start = new Bbs757EfbItem(Bbs757EfbKind.Button, "Start boarding", new Rectangle(1142, 3, 180, 28), new Point(1231, 17), true,
            Bbs757EfbTopBar.BoardingKey, Bbs757EfbTint.Green);
        var tablet = new FakeTablet(Ground(ReqFuelTruck()) with { TopBar = new[] { start } });
        var press = tablet.Press(start);
        tablet.Show(Ground(ReqFuelTruck()) with
        {
            TopBar = new[] { new Bbs757EfbItem(Bbs757EfbKind.Text, "Boarding" + Ellipsis + " 10 min", start.Box, new Point(1232, 17), false, Bbs757EfbTopBar.BoardingKey) },
        });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.DoesNotContain(tablet.Flow, e => e.StartsWith("hook", StringComparison.Ordinal));
        Assert.Equal(new[] { "Start boarding is no longer on the tablet." }, tablet.Alerts);
    }

    // ---- questions --------------------------------------------------------------------------------------------------

    private const string DefuelQuestion = "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?";
    private const string UnitsQuestion = "Mismatch between flight plan and aircraft weight units! Do you want to switch the aircraft unit system?";

    private static Bbs757EfbScreenModel FuelAsked(string question, Bbs757EfbItem answer) =>
        PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45)))
            with { Question = new Bbs757EfbQuestion(question, new[] { answer }) };

    private static readonly Bbs757EfbItem YesAnswer = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);

    [Fact]
    public async Task A_control_a_new_question_covers_is_not_tapped()
    {
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "FUEL", import));
        var press = tablet.Press(import);
        tablet.Show(PageOf("AIRCRAFT", "FUEL", import) with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { YesAnswer }) });

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_yes_pressed_on_one_question_is_never_tapped_on_another()
    {
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, YesAnswer));
        var press = tablet.Press(YesAnswer);
        tablet.Show(FuelAsked(UnitsQuestion, YesAnswer));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_yes_whose_question_was_answered_meanwhile_is_gone(string culture)
    {
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, YesAnswer));
        var press = tablet.Press(YesAnswer);
        tablet.Show(PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45))));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Yes is no longer on the tablet." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_yes_is_tapped_while_its_question_reads_the_same()
    {
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, YesAnswer));
        var press = tablet.Press(YesAnswer);
        tablet.OnTap = _ => tablet.Show(PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45))));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { YesAnswer.Hit }, tablet.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tap_that_cannot_run_says_so_and_starts_no_hook(string culture)
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch)) { Reachable = false };

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(tablet.Press(fetch), CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "The tablet cannot be reached, so nothing was tapped." }, tablet.Alerts);
        Assert.DoesNotContain(tablet.Flow, e => e.StartsWith("hook", StringComparison.Ordinal));
    }

    // ---- outcomes ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tile_that_changed_nothing_says_so(string culture)
    {
        var tile = ReqTug();
        var tablet = new FakeTablet(Ground(tile));
        var press = tablet.Press(tile);
        // The tap changes nothing on the page.

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Req PB tug: no change on the tablet." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_relabeled_under_a_new_key_is_said_once_in_its_new_words(string culture)
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch));
        var press = tablet.Press(fetch);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "ACARS DASHBOARD", Tile("Refresh SIMBRIEF ofp", "btn:|REFRESH SIMBRIEF OFP@42,40", fetch.Box)));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { "Refresh SIMBRIEF ofp" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_press_whose_control_kept_its_words_but_changed_the_page_says_nothing()
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        // The tile kept its words, but another tile's did change: the press's own control says nothing.
        tablet.OnTap = _ => tablet.Show(Ground(tile, Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@45,36", new Rectangle(912, 723, 196, 42))));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_press_that_brought_up_a_question_says_nothing_over_it()
    {
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "FUEL", import));
        var press = tablet.Press(import);
        tablet.OnTap = _ => tablet.Show(FuelAsked(DefuelQuestion, YesAnswer));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { import.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);   // the read that found the question says it, not the press
    }

    // ---- navigation -------------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Menu(string word) =>
        new(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(word), new Rectangle(0, 142, 250, 60), new Point(125, 172), true, "menu:" + word);

    private static Bbs757EfbItem HomeApp(string app) =>
        new(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(app), new Rectangle(273, 57, 110, 110), new Point(328, 112), true, "app:" + app);

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_menu_press_whose_page_never_comes_says_so(string culture)
    {
        var fuel = Menu("FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(fuel);
        // The tap changes nothing: the page never becomes "Aircraft, Fuel".

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { fuel.Hit }, tablet.Taps);
        Assert.Equal(new[] { "The tablet did not show Fuel. Try again, or press F5 to read the tablet again." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_menu_press_whose_section_is_not_read_says_nothing(string culture)
    {
        var fuel = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(130, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, new[] { ReqFuelTruck() }));
        var press = tablet.Press(fuel);
        tablet.OnTap = _ => tablet.Show(new Bbs757EfbScreenModel("AIRCRAFT", "", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
        Assert.True(tablet.Now >= Bbs757EfbPressRunner.NavigateMaxMs, "read again for four seconds");
    }

    [Fact]
    public async Task A_menu_press_whose_page_reads_unknown_says_nothing()
    {
        var fuel = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(130, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, new[] { ReqFuelTruck() }));
        var press = tablet.Press(fuel);
        tablet.OnTap = _ => tablet.Show(new Bbs757EfbScreenModel("", "", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_home_press_that_lands_in_another_app_says_so()
    {
        var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", new Rectangle(5, 5, 40, 30), new Point(25, 20), true, "btn:HOME");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("NAVIGATION", "", new[] { home }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(home);

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "The tablet did not show Home. Try again, or press F5 to read the tablet again." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_menu_press_whose_page_comes_late_says_nothing()
    {
        var fuel = Menu("FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(fuel);
        int taps = 0;
        tablet.OnTap = _ => { };
        // The page follows two reads after the tap.
        var runner = tablet.Runner();
        tablet.OnTap = _ => taps++;
        // Script the page to change to Fuel after the tap.
        tablet.OnTap = _ => tablet.Show(new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { fuel with { Selected = true } }, Array.Empty<Bbs757EfbItem>()));

        await runner.ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { fuel.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_side_menu_entry_pressed_again_on_its_own_page_taps_nothing_and_says_nothing()
    {
        var fuel = Menu("FUEL");
        // The tablet already shows Aircraft, Fuel: the first press took it there, this one is done.
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));

        await tablet.Runner().ClickAsync(tablet.Press(fuel), CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_home_screen_app_pressed_again_once_it_opened_taps_nothing_and_says_nothing()
    {
        var aircraft = HomeApp("AIRCRAFT");
        var tablet = new FakeTablet(Ground(ReqFuelTruck()));   // already in the AIRCRAFT app

        await tablet.Runner().ClickAsync(tablet.Press(aircraft), CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_side_menu_entry_on_a_page_that_went_elsewhere_is_gone(string culture)
    {
        var fuel = Menu("FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(fuel);
        // The tablet went to another app entirely: the entry is not there.
        tablet.Show(PageOf("SETTINGS", "GENERAL", Switch("Lock keyboard to EFB", "tgl:LOCK KEYBOARD TO EFB", on: true)));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Fuel is no longer on the tablet." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_tab_press_is_judged_as_a_button_not_as_a_page()
    {
        // A tab (Kind.Tab, keyed tab:) changes the list under it, not the page name: no navigation, and the reader only flips Selected.
        var tab = new Bbs757EfbItem(Bbs757EfbKind.Tab, "Metar", new Rectangle(300, 100, 120, 40), new Point(360, 120), true, "tab:METAR");
        var tablet = new FakeTablet(PageOf("NAVIGATION", "WEATHER & NOTAM", tab));
        var press = tablet.Press(tab);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "WEATHER & NOTAM", tab with { Selected = true }));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tab.Hit }, tablet.Taps);
        // No "did not show" navigation message: a tab is not a navigation. It says it is the current one.
        Assert.Equal(new[] { "Metar (current)" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tab_of_the_real_maintenance_page_says_it_is_current_once(string culture)
    {
        // The final review's I2: HYDRAULIC pressed on Landing gear, the tablet then showing the Hydraulic tab (15b). The reader keeps
        // the tab's words and flips Selected; the window patches its label silently, so the press says it.
        var gear = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var hydraulic = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15b-aircraft-maintenance-hydraulic"))!;
        var tab = Assert.Single(gear.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Name == "maintenance_hydraulic_tab_button");
        Assert.False(tab.Selected);
        var tablet = new FakeTablet(gear);
        tablet.OnTap = _ => tablet.Show(hydraulic);

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(tablet.Press(tab), CancellationToken.None));

        Assert.Equal(new[] { tab.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Hydraulic (current)" }, tablet.Alerts);
    }

    [Fact]
    public async Task The_current_tab_pressed_again_changes_nothing_and_says_so()
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var tab = Assert.Single(page.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Selected);
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(tab), CancellationToken.None);

        Assert.Equal(new[] { "Landing gear: no change on the tablet." }, tablet.Alerts);
    }

    // ---- M2: what a press says names the control as the window does ---------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_labelled_door_says_its_label_with_its_new_words(string culture)
    {
        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("14-aircraft-cabin-doors"))!;
        var door = Assert.Single(closed.Items, i => i.Name == "btn_l1_door");
        var tablet = new FakeTablet(closed);
        tablet.OnTap = _ => tablet.Show(closed with { Items = closed.Items.Select(i => i.Name == "btn_l1_door" ? i with { Text = "Open" } : i).ToList() });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(tablet.Press(door), CancellationToken.None));

        Assert.Equal(new[] { "Left FWD: Open" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_settings_row_the_tablet_did_not_follow_is_named_in_sentence_case(string culture)
    {
        // The reader keeps a settings row's words as BlueBird writes them (SYNC BAROMETERS); the window and the press both say
        // "Sync barometers".
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        var sync = Assert.Single(page.Items, i => i.Text == "SYNC BAROMETERS");
        var tablet = new FakeTablet(page);
        tablet.File["baro_sync"] = Bbs757Value.Of(true);

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(tablet.Press(sync), wantOn: false, CancellationToken.None));

        Assert.Equal(new[] { "Sync barometers did not change." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_real_row_that_never_comes_into_view_is_named_in_sentence_case(string culture)
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        var tablet = new FakeTablet(page);   // the drag moves nothing

        await TestCulture.Run(culture, () =>
            tablet.Runner().SwitchAsync(tablet.Press(page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle)), wantOn: true, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Dirty panel states could not be brought into view, not pressed." }, tablet.Alerts);
    }

    // ---- echoes and hooks ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_fuel_truck_tile_arms_and_settles_its_echo()
    {
        var off = FuelTruck("off");
        var tablet = new FakeTablet(Ground(off));
        tablet.Vars["BBS757_vis_fueltruck"] = 0;
        var press = tablet.Press(off);
        tablet.OnTap = _ =>
        {
            tablet.Vars["BBS757_vis_fueltruck"] = 1;
            tablet.Show(Ground(FuelTruck("on")));
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "arm FUEL TRUCK", Tap(off), "alert Fuel truck on", "settle FUEL TRUCK 1" }, tablet.Flow);
    }

    [Fact]
    public async Task The_GPU_tile_arms_and_settles_the_ground_power_echo()
    {
        var gpu = Gpu("removed");
        var tablet = new FakeTablet(Ground(gpu));
        tablet.Vars[Bbs757SharedKeys.GpuConnected] = 0;
        var press = tablet.Press(gpu);
        tablet.OnTap = _ =>
        {
            tablet.Vars[Bbs757SharedKeys.GpuConnected] = 1;
            tablet.Show(Ground(Gpu("connected")));
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "arm GPU", Tap(gpu), "alert GPU connected", "settle GPU 1" }, tablet.Flow);
    }

    [Fact]
    public async Task An_echo_is_settled_with_nothing_read_when_the_tap_could_not_run()
    {
        var off = FuelTruck("off");
        var tablet = new FakeTablet(Ground(off)) { Reachable = false };
        tablet.Vars["BBS757_vis_fueltruck"] = 0;

        await tablet.Runner().ClickAsync(tablet.Press(off), CancellationToken.None);

        Assert.Equal(new[] { "arm FUEL TRUCK", Tap(off), "alert The tablet cannot be reached, so nothing was tapped.", "settle FUEL TRUCK unread" }, tablet.Flow);
    }

    [Fact]
    public async Task A_fetch_press_calls_the_hook_after_the_tap()
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch));
        var press = tablet.Press(fetch);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "ACARS DASHBOARD", Tile("Refresh SIMBRIEF ofp", "btn:|REFRESH SIMBRIEF OFP@42,40", fetch.Box)));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { Tap(fetch), "hook Fetch", "alert Refresh SIMBRIEF ofp" }, tablet.Flow);
    }

    [Fact]
    public async Task The_nav_data_buttons_hook_the_nav_data_watch()
    {
        foreach (var (words, hook) in new[] { ("Update", Bbs757EfbHook.NavDataUpdate), ("Remove", Bbs757EfbHook.NavDataRemove) })
        {
            var button = Tile(words, "btn:|" + words.ToUpperInvariant() + "@64,16", new Rectangle(1221, 307, 140, 40));
            var tablet = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", button)) { Cycle = 2609 };
            tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRAC DATA", button with { Text = "Updating" + Ellipsis }));

            await tablet.Runner().ClickAsync(tablet.Press(button), CancellationToken.None);

            Assert.Equal(new[] { "cycle", Tap(button), "hook " + hook + " 2609", "alert Updating" + Ellipsis }, tablet.Flow);
        }
    }

    [Fact]
    public async Task A_removal_hands_the_watch_the_cycle_installed_before_the_tap()
    {
        var remove = Tile("Remove", "btn:|REMOVE@64,16", new Rectangle(1221, 307, 140, 40));
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", remove)) { Cycle = 2610 };
        tablet.OnTap = _ =>
        {
            tablet.Cycle = null;
            tablet.Show(PageOf("SETTINGS", "AIRAC DATA", remove with { Text = "Update" }));
        };

        await tablet.Runner().ClickAsync(tablet.Press(remove), CancellationToken.None);

        Assert.Equal(new[] { "cycle", Tap(remove), "hook NavDataRemove 2610", "alert Update" }, tablet.Flow);
    }

    [Fact]
    public async Task No_cycle_is_read_for_a_press_that_is_no_nav_data_press_or_whose_tap_cannot_run()
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var acars = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch)) { Cycle = 2609 };
        await acars.Runner().ClickAsync(acars.Press(fetch), CancellationToken.None);
        Assert.DoesNotContain("cycle", acars.Events);

        var update = Tile("Update", "btn:|UPDATE@64,16", new Rectangle(1221, 307, 140, 40));
        var airac = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", update)) { Cycle = 2609, Reachable = false };
        await airac.Runner().ClickAsync(airac.Press(update), CancellationToken.None);
        Assert.DoesNotContain(airac.Events, e => e.StartsWith("hook", StringComparison.Ordinal));
    }

    // ---- switches: the wish, and BlueBird's file -----------------------------------------------------------------------

    [Fact]
    public async Task A_switch_already_as_wished_is_not_tapped_and_nothing_is_said()
    {
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        var press = tablet.Press(sync);
        tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_settings_switch_not_followed_by_the_file_says_did_not_change(string culture)
    {
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

    public static TheoryData<string, long, bool> CulturesByCpdlcFile => new()
    {
        { "en-US", 1L, false }, { "en-US", 0L, true },
        { "de-DE", 1L, false }, { "de-DE", 0L, true },
        { "sv-SE", 1L, false }, { "sv-SE", 0L, true },
    };

    [Theory]
    [MemberData(nameof(CulturesByCpdlcFile))]
    public async Task A_two_choice_setting_is_judged_by_the_choice_that_was_not_set(string culture, long stored, bool notChanged)
    {
        // A choice list in the window (its word was read): judged by the file, and only a failure speaks (item 18).
        var cpdlc = Switch("CPDLC network", "tgl:CPDLC NETWORK", on: null, value: "Hoppie");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", cpdlc));
        tablet.File["cpdlc_network"] = Bbs757Value.Of(0L);
        var press = tablet.Press(cpdlc);
        tablet.OnTap = _ =>
        {
            tablet.File["cpdlc_network"] = Bbs757Value.Of(stored);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", cpdlc with { Value = "SayIntentions" }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().ChoiceSwitchAsync(press, wanted: 1, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(notChanged ? new[] { "CPDLC network did not change." } : Array.Empty<string>(), tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_two_choice_setting_the_window_shows_as_a_check_box_still_says_its_new_word(string culture)
    {
        // Its word was not read, so the window shows a check box, of which the screen reader says only checked or not: the press says
        // the new word, from the catalog, as before item 18.
        var cpdlc = Switch("CPDLC network", "tgl:CPDLC NETWORK", on: false, value: "");
        var page = PageOf("SETTINGS", "AIRCRAFT", cpdlc);
        Assert.False(Bbs757EfbElements.ShowsAsChoiceList(cpdlc, page.PageName));
        var tablet = new FakeTablet(page);
        tablet.File["cpdlc_network"] = Bbs757Value.Of(0L);
        tablet.OnTap = _ => tablet.File["cpdlc_network"] = Bbs757Value.Of(1L);

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(tablet.Press(cpdlc), wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "CPDLC network: SayIntentions" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_two_choice_row_that_is_no_setting_says_the_word_the_page_shows_now(string culture)
    {
        // A label misread so it matches no setting: judged by the page, and the new word it shows is said.
        var row = Switch("CPDLC netwrk", "tgl:CPDLC NETWRK", on: false, value: "Hoppie");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", row));
        var press = tablet.Press(row);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", row with { On = true, Value = "SayIntentions" }));

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "CPDLC netwrk: SayIntentions" }, tablet.Alerts);
    }

    // ---- sliders -----------------------------------------------------------------------------------------------------

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
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Slider: new Bbs757EfbSlider(500, 900, 500));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        var press = tablet.Press(ceiling);
        tablet.OnTap = p => tablet.Show(PageOf("AIRCRAFT", "CABIN & DOORS",
            ceiling with { Slider = new Bbs757EfbSlider(500, 900, p.X), Value = ((p.X - 500) / 4).ToString(CultureInfo.InvariantCulture) + "%" }));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "30", CancellationToken.None));
        Assert.Equal(new[] { new Point(620, 315) }, tablet.Taps);
        Assert.Empty(tablet.Alerts);

        var stuck = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        await TestCulture.Run(culture, () => stuck.Runner().SliderAsync(stuck.Press(ceiling), "70", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 70 percent." }, stuck.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_choice_slider_whose_choice_was_not_read_is_set_as_a_percent(string culture)
    {
        var irs = new Bbs757EfbItem(Bbs757EfbKind.Slider, "IRS alignment time", new Rectangle(273, 513, 255, 33), new Point(1000, 529), true,
            "sld:IRS ALIGNMENT TIME", Value: "", Slider: new Bbs757EfbSlider(895, 1190, 1000));
        var page = PageOf("SETTINGS", "AIRCRAFT", irs);
        Assert.NotNull(Bbs757EfbElements.OptionsFor(irs, page.PageName));
        var tablet = new FakeTablet(page);
        var press = tablet.Press(irs);
        tablet.OnTap = p => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", irs with { Slider = new Bbs757EfbSlider(895, 1190, p.X), Value = "50" }));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "50", CancellationToken.None));
        Assert.Equal(new[] { new Point(1043, 529) }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_value_that_is_neither_a_choice_nor_a_number_is_said_not_set(string culture)
    {
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Slider: new Bbs757EfbSlider(500, 900, 500));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(tablet.Press(ceiling), "bright", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Ceiling could not be set to bright." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_percent_is_judged_by_its_shown_value(string culture)
    {
        // The knob moves, but the value shown is what judges: 30 asked, 90 shown is not set; a value that is no percent but changed is.
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Value: "10%", Slider: new Bbs757EfbSlider(500, 900, 500));
        var wrong = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        wrong.OnTap = p => wrong.Show(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling with { Value = "90%", Slider = new Bbs757EfbSlider(500, 900, p.X) }));
        await TestCulture.Run(culture, () => wrong.Runner().SliderAsync(wrong.Press(ceiling), "30", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 30 percent." }, wrong.Alerts);

        var feet = ceiling with { Value = "100 ft" };
        var changed = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", feet));
        changed.OnTap = _ => changed.Show(PageOf("AIRCRAFT", "CABIN & DOORS", feet with { Value = "300 ft" }));
        await TestCulture.Run(culture, () => changed.Runner().SliderAsync(changed.Press(feet), "30", CancellationToken.None));
        Assert.Empty(changed.Alerts);

        var same = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", feet));
        await TestCulture.Run(culture, () => same.Runner().SliderAsync(same.Press(feet), "30", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 30 percent." }, same.Alerts);
    }

    // ---- I3, I4: sliders whose value shows elsewhere, or nowhere, and vertical ones -----------------------------------------

    private static Bbs757EfbScreenModel WeightBalance(string? businessTitle = null, string? forwardCargo = null)
    {
        var apps = Bbs757EfbTreeJson.Load("12-aircraft-weight-balance");
        if (businessTitle != null) apps = Bbs757EfbTreeJson.WithText(apps, "wb_pax_business_title", businessTitle);
        if (forwardCargo != null) apps = Bbs757EfbTreeJson.WithText(apps, "wb_baggage_fwd_value", forwardCargo);
        return Bbs757EfbModelReader.Read(apps)!;
    }

    [Fact]
    public void A_passenger_slider_is_named_by_its_title_and_placed_by_its_count()
    {
        var page = WeightBalance();
        var pax = Assert.Single(page.Items, i => i.Name == "wb_pax_business_slider");

        Assert.Equal("BUSINESS PAX", pax.Text);
        Assert.Equal("16/16", pax.Value);
        Assert.Equal(pax.Slider!.TrackRight, pax.Slider.KnobX);   // 16 of 16 boarded: the knob at the end
        Assert.Equal(pax.Slider.At(50), WeightBalance("BUSINESS PAX - 8/16").Items.Single(i => i.Name == "wb_pax_business_slider").Slider!.KnobX);
        Assert.DoesNotContain(page.Items, i => i.Kind == Bbs757EfbKind.Heading && i.Text.StartsWith("BUSINESS PAX", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_passenger_slider_is_judged_by_the_count_its_title_shows_after_the_tap(string culture)
    {
        var page = WeightBalance();
        var pax = Assert.Single(page.Items, i => i.Name == "wb_pax_business_slider");
        var set = new FakeTablet(page);
        set.OnTap = _ => set.Show(WeightBalance("BUSINESS PAX - 8/16"));

        await TestCulture.Run(culture, () => set.Runner().SliderAsync(set.Press(pax), "50", CancellationToken.None));

        Assert.Equal(new[] { new Point(pax.Slider!.At(50), pax.Hit.Y) }, set.Taps);
        Assert.Empty(set.Alerts);

        var wrong = new FakeTablet(page);
        wrong.OnTap = _ => wrong.Show(WeightBalance("BUSINESS PAX - 15/16"));
        await TestCulture.Run(culture, () => wrong.Runner().SliderAsync(wrong.Press(pax), "50", CancellationToken.None));
        Assert.Equal(new[] { "Business pax could not be set to 50 percent." }, wrong.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_whose_value_shows_nowhere_is_judged_by_its_page(string culture)
    {
        // The cargo slider shows no value of its own: its weight box beside it is what the tap changes.
        var page = WeightBalance();
        var cargo = Assert.Single(page.Items, i => i.Name == "wb_baggage_fwd_slider");
        Assert.Equal("", cargo.Value);
        var followed = new FakeTablet(page);
        followed.OnTap = _ => followed.Show(WeightBalance(forwardCargo: "3080"));

        await TestCulture.Run(culture, () => followed.Runner().SliderAsync(followed.Press(cargo), "50", CancellationToken.None));

        Assert.Single(followed.Taps);
        Assert.Empty(followed.Alerts);

        var stuck = new FakeTablet(page);
        await TestCulture.Run(culture, () => stuck.Runner().SliderAsync(stuck.Press(cargo), "50", CancellationToken.None));
        Assert.Equal(new[] { "Forward cargo could not be set to 50 percent." }, stuck.Alerts);
    }

    [Theory]
    [InlineData("15b-aircraft-maintenance-hydraulic", "maintenance_hyd_res_left_slider")]
    [InlineData("30-systems", "elec_zoom_slider")]
    public async Task A_vertical_slider_is_set_along_its_height(string fixture, string name)
    {
        // A slider taller than wide runs bottom (0, provisional until measured) to top, and is tapped at its x middle.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;
        var slider = Assert.Single(page.Items, i => i.Name == name);
        Assert.True(slider.Slider!.Vertical);
        var taps = new List<Point>();
        foreach (var percent in new[] { "10", "90" })
        {
            var tablet = new FakeTablet(page);
            await tablet.Runner().SliderAsync(tablet.Press(slider), percent, CancellationToken.None);
            taps.Add(Assert.Single(tablet.Taps));
        }

        Assert.All(taps, t => Assert.Equal(slider.Box.Left + slider.Box.Width / 2, t.X));
        Assert.All(taps, t => Assert.InRange(t.Y, slider.Box.Top, slider.Box.Bottom - 1));
        Assert.True(taps[0].Y > taps[1].Y, $"10 percent at {taps[0]} should be below 90 percent at {taps[1]}");
        Assert.InRange(taps[0].Y, slider.Box.Bottom - slider.Box.Height / 10 - 2, slider.Box.Bottom - slider.Box.Height / 10 + 2);
    }

    // ---- two-choice switches --------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Nosewheel(string value) =>
        new(Bbs757EfbKind.Toggle, "Nosewheel steering", new Rectangle(273, 333, 237, 33), new Point(1170, 349), true, "tgl:NOSEWHEEL STEERING", Value: value);

    private static async Task<List<string>> SetNosewheel(string shown, string wanted)
    {
        var page = PageOf("SETTINGS", "AIRCRAFT", Nosewheel(shown));
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(page, null) };
        h.Bridge.TakeRead(page);
        var push = h.Pushes[^1];
        int row = IndexOf(push, "Nosewheel steering");
        Assert.Equal("select", push[$"items.{row}.type"]);
        await h.Bridge.HandleAsync("set_element_value", new Dictionary<string, string> { ["index"] = push[$"items.{row}.aidx"], ["value"] = wanted }, CancellationToken.None);
        return Texts(h.Pushes[^1]);
    }

    [Fact]
    public async Task A_two_choice_switch_set_to_its_other_word_taps_once()
    {
        // The harness has no tablet to tap, so the tap ends "cannot be reached": that alert is the proof a tap was tried.
        var texts = await SetNosewheel("Realistic", "Rudder");
        Assert.Contains(Bbs757EfbPress.UnreachableText, texts);
    }

    [Fact]
    public async Task A_two_choice_switch_set_to_its_shown_word_taps_nothing()
    {
        var texts = await SetNosewheel("Realistic", "Realistic");
        Assert.DoesNotContain(Bbs757EfbPress.UnreachableText, texts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_two_choice_choice_list_change_says_nothing_and_a_failed_one_says_so(string culture)
    {
        // Item 18 (the owner's ruling, 2026-11-07): the screen reader said "Rudder" as the pilot chose it in the choice list, so a
        // change that landed says nothing; one that did not says "did not change", in sentence case.
        var row = Nosewheel("Realistic");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", row));
        tablet.File["tiller_realistic"] = Bbs757Value.Of(true);
        tablet.OnTap = _ =>
        {
            tablet.File["tiller_realistic"] = Bbs757Value.Of(false);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", row with { Value = "Rudder" }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().ChoiceSwitchAsync(tablet.Press(row), wanted: 1, CancellationToken.None));

        Assert.Equal(new[] { row.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);

        var stuck = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", row with { Text = "NOSEWHEEL STEERING" }));
        stuck.File["tiller_realistic"] = Bbs757Value.Of(true);
        await TestCulture.Run(culture, () => stuck.Runner().ChoiceSwitchAsync(stuck.Press(stuck.Page.Items[0]), wanted: 1, CancellationToken.None));
        Assert.Single(stuck.Taps);
        Assert.Equal(new[] { "Nosewheel steering did not change." }, stuck.Alerts);
    }

    [Fact]
    public async Task A_two_choice_switch_whose_fresh_read_shows_the_wish_is_not_tapped()
    {
        // The final review's I5: pressed on a push showing Realistic, the tablet shows Rudder by the press's own read. Rudder asked
        // for: nothing tapped (a tap would switch it away from the wish). Realistic asked for: one tap.
        var pushed = Nosewheel("Realistic");
        var already = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", pushed));
        var press = already.Press(pushed);
        already.Show(PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Rudder")));

        await already.Runner().ChoiceSwitchAsync(press, wanted: 1, CancellationToken.None);

        Assert.Empty(already.Taps);
        Assert.Empty(already.Alerts);

        var back = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", pushed));
        var again = back.Press(pushed);
        back.Show(PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Rudder")));
        await back.Runner().ChoiceSwitchAsync(again, wanted: 0, CancellationToken.None);
        Assert.Single(back.Taps);
    }

    [Fact]
    public async Task A_two_choice_switch_set_from_an_older_push_to_the_word_the_tablet_shows_now_taps_nothing()
    {
        // Through the bridge: the push showed Realistic, the tablet's read now shows Rudder, the pilot picks Rudder.
        var pushed = PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Realistic"));
        var now = PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Rudder"));
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(now, null) };
        h.Bridge.TakeRead(pushed);
        var push = h.Pushes[^1];
        int row = IndexOf(push, "Nosewheel steering");

        await h.Bridge.HandleAsync("set_element_value", new Dictionary<string, string> { ["index"] = push[$"items.{row}.aidx"], ["value"] = "Rudder" },
            CancellationToken.None);

        Assert.DoesNotContain(Bbs757EfbPress.UnreachableText, Texts(h.Pushes[^1]));   // no tap was tried
    }

    // ---- scrolling into view --------------------------------------------------------------------------------------------

    /// <summary>A scrolled page: rows at content positions inside a viewport of 250,82 1147x966, drawn at content less the offset;
    /// a drag moves the offset by y0 - y1, kept between 0 and <see cref="MaxOffset"/>, as the tablet's does (about 1:1).</summary>
    private sealed class Scrolled
    {
        public const int MaxOffset = 214;
        public double Offset { get; set; }
        public double Ratio { get; set; } = 1.0;
        public FakeTablet Tablet { get; }
        private readonly (string Name, int ContentY)[] _rows;

        public Scrolled(double offset, params (string Name, int ContentY)[] rows)
        {
            _rows = rows;
            Offset = offset;
            Tablet = new FakeTablet(PageAt());
            Tablet.OnDrag = (_, y0, y1) =>
            {
                Offset = Math.Clamp(Offset + (y0 - y1) * Ratio, 0, MaxOffset);
                Tablet.Show(PageAt());
            };
        }

        public Bbs757EfbItem Row(string name, double? offset = null)
        {
            int content = _rows.First(r => r.Name == name).ContentY;
            int y = (int)Math.Round(content - (offset ?? Offset));
            return new Bbs757EfbItem(Bbs757EfbKind.Button, name, new Rectangle(273, y, 600, 60), new Point(573, y + 30), true, "btn:" + name.ToUpperInvariant());
        }

        public Bbs757EfbScreenModel PageAt() =>
            PageOf("SETTINGS", "AIRCRAFT", _rows.Select(r => Row(r.Name)).ToArray()) with { Scrollable = true, Viewport = new Rectangle(250, 82, 1147, 966) };
    }

    private static Scrolled TwoRows(double offset) => new(offset, ("First row", 100), ("Dirty panel states", 1202));

    [Fact]
    public async Task A_control_below_the_viewport_is_scrolled_into_view_before_its_tap()
    {
        var s = TwoRows(0);
        var press = s.Tablet.Press(s.Row("Dirty panel states"));   // drawn at 1202, under the viewport's bottom (1048)

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEmpty(s.Tablet.Drags);
        Assert.All(s.Tablet.Drags, d => { Assert.Equal(500, d.X); Assert.True(d.Y0 > d.Y1); });   // content moves up
        var tap = Assert.Single(s.Tablet.Taps);
        var row = s.Row("Dirty panel states");
        Assert.True(row.Box.Contains(tap) && s.PageAt().Viewport.Contains(tap), $"tapped {tap} where it is drawn {row.Box}");
        Assert.True(s.Tablet.Flow.FindIndex(e => e.StartsWith("drag")) < s.Tablet.Flow.FindIndex(e => e.StartsWith("tap")));
    }

    [Fact]
    public async Task A_control_above_the_viewport_scrolls_up()
    {
        var s = new Scrolled(Scrolled.MaxOffset, ("First row", 100), ("Dirty panel states", 1202));   // First row drawn at -114
        var press = s.Tablet.Press(s.Row("First row"));

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEmpty(s.Tablet.Drags);
        Assert.All(s.Tablet.Drags, d => Assert.True(d.Y0 < d.Y1));   // content moves down
        var tap = Assert.Single(s.Tablet.Taps);
        Assert.True(s.Row("First row").Box.Contains(tap) && s.PageAt().Viewport.Contains(tap), $"tapped {tap}");
    }

    [Fact]
    public async Task A_control_that_never_comes_into_view_is_not_tapped()
    {
        var s = TwoRows(0);
        s.Tablet.OnDrag = null;   // the page does not move
        var press = s.Tablet.Press(s.Row("Dirty panel states"));

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(s.Tablet.Taps);
        Assert.Single(s.Tablet.Drags);   // a drag that left it where it was ends the loop
        Assert.Equal(new[] { "Dirty panel states could not be brought into view, not pressed." }, s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_control_on_a_page_scrolled_since_the_push_is_tapped_where_it_is_drawn()
    {
        var s = TwoRows(0);
        var press = s.Tablet.Press(s.Row("First row"));   // pressed at offset 0, drawn at 100
        s.Offset = 100;
        s.Tablet.Show(s.PageAt());                         // now drawn at 0: above the viewport's top (82)? 0..60 is outside

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        var tap = Assert.Single(s.Tablet.Taps);
        Assert.True(s.Row("First row").Box.Contains(tap) && s.PageAt().Viewport.Contains(tap), $"tapped {tap}");
    }

    [Fact]
    public async Task A_control_left_of_the_viewport_is_tapped_where_it_is()
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("30-systems"))!;
        var zoom = page.Items.First(i => i.Clickable && i.Box.Right <= page.Viewport.Left && i.Name.Contains("elec_zoom_in", StringComparison.Ordinal));
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(zoom), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { zoom.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_page_without_a_scrollbox_is_never_dragged()
    {
        var below = Tile("Below", "btn:BELOW", new Rectangle(300, 1100, 200, 40));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "MAP", below));   // Scrollable is false

        await tablet.Runner().ClickAsync(tablet.Press(below), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Below could not be brought into view, not pressed." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_control_in_view_taps_without_a_drag()
    {
        var s = TwoRows(0);
        var row = s.Row("First row");

        await s.Tablet.Runner().ClickAsync(s.Tablet.Press(row), CancellationToken.None);

        Assert.Empty(s.Tablet.Drags);
        Assert.Equal(new[] { row.Hit }, s.Tablet.Taps);
    }

    /// <summary>Settings, Aircraft read from its committed tree with the page scrolled by <paramref name="offset"/>.</summary>
    private static Bbs757EfbScreenModel SettingsAircraftAt(double offset) =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Scrolled(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "aircraft_scrollbox", (float)offset))!;

    /// <summary>The end of Settings, Aircraft's scroll: its stackbox's 1,180 less its scrollbox's 966, read at +0x134 (B757-26;
    /// measured 2026-11-07). The scrollbar thumb at +0x158 stops at 175.19, short of it.</summary>
    private const double SettingsAircraftEnd = 214;

    /// <summary>A mid-scroll offset of Settings, Aircraft at which a 10.2 px strip of its last switch was measured to take a tap
    /// (2026-11-07).</summary>
    private const double SettingsAircraftMidScroll = 176.7;

    [Fact]
    public async Task A_partly_shown_last_row_is_tapped_in_its_shown_part()
    {
        // The real tree mid-scroll: Dirty panel states' toggle [1137,1214.5 60x35] is drawn from 1037.8 and only its top 10 px or so
        // show above the viewport's bottom (1048). It is tapped there, in the middle of that strip, with no drag.
        var page = SettingsAircraftAt(SettingsAircraftMidScroll);
        var dirty = page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        var tablet = new FakeTablet(page);

        await tablet.Runner().SwitchAsync(tablet.Press(dirty), wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Drags);
        var tap = Assert.Single(tablet.Taps);
        Assert.InRange(tap.Y, dirty.Box.Top, 1047);
        Assert.Equal(dirty.Box.Top + (1048 - dirty.Box.Top) / 2, tap.Y);
        Assert.Equal(dirty.Hit.X, tap.X);
    }

    [Fact]
    public async Task The_last_row_of_the_real_settings_page_is_scrolled_into_view_and_tapped_from_the_top()
    {
        // Review Focus 1 on the real tree: pressed at the top of the page, Dirty panel states is dragged into view (the drag moves the
        // page 1:1 up to its measured end, 214), read again and tapped where it is drawn then, inside the viewport.
        double offset = 0;
        var tablet = new FakeTablet(SettingsAircraftAt(0));
        var press = tablet.Press(tablet.Page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle));
        tablet.OnDrag = (_, y0, y1) =>
        {
            offset = Math.Clamp(offset + (y0 - y1), 0, SettingsAircraftEnd);
            tablet.Show(SettingsAircraftAt(offset));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Single(tablet.Drags);
        var tap = Assert.Single(tablet.Taps);
        var drawn = tablet.Page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        Assert.True(tap.Y >= drawn.Box.Top && tap.Y < 1048, $"tapped {tap}, drawn {drawn.Box}");
        Assert.DoesNotContain(tablet.Alerts, a => a.Contains("could not be brought into view", StringComparison.Ordinal));
    }

    // ---- C1: a page whose only scrollbox is an inner one --------------------------------------------------------------------

    [Theory]
    [InlineData("20-navigation-acars-dashboard", "planning_acars_fetch_button")]
    [InlineData("22-navigation-weather", "planning_weather_primary_tab_button_0")]
    [InlineData("40-checklist", "checklist_reset")]
    public async Task A_control_outside_an_inner_scrollbox_is_tapped_once_with_no_drag(string fixture, string name)
    {
        // REFRESH SIMBRIEF OFP above the ACARS route's box, METAR above Weather's report box, the Checklist's RESET above its lines.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;
        var control = Assert.Single(page.Items, i => i.Name == name);
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(control), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { control.Hit }, tablet.Taps);
        Assert.DoesNotContain(tablet.Alerts, a => a.Contains("could not be brought into view", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_long_way_takes_several_drags()
    {
        var s = new Scrolled(0, ("Far row", 5000));
        s.Tablet.OnDrag = (_, y0, y1) =>
        {
            s.Offset += y0 - y1;   // no end: a long page
            s.Tablet.Show(s.PageAt());
        };

        await s.Tablet.Runner().ClickAsync(s.Tablet.Press(s.Row("Far row")), CancellationToken.None);

        Assert.True(s.Tablet.Drags.Count >= 2);
        Assert.True(s.Tablet.Drags.Count <= Bbs757EfbPressRunner.MaxScrollSteps);
        Assert.All(s.Tablet.Drags, d => Assert.True(d.Y0 - d.Y1 <= 966 - 200));
        Assert.Single(s.Tablet.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_drag_that_cannot_run_says_so_and_taps_nothing(string culture)
    {
        var s = TwoRows(0);
        s.Tablet.DragReachable = false;

        await TestCulture.Run(culture, () => s.Tablet.Runner().ClickAsync(s.Tablet.Press(s.Row("Dirty panel states")), CancellationToken.None));

        Assert.Empty(s.Tablet.Taps);
        Assert.Equal(new[] { Bbs757EfbPress.UnreachableText }, s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_page_that_never_brings_the_control_in_stops_after_the_most_drags()
    {
        // Each drag moves the page, but the control never arrives (a page far longer than the drags can cover): at most MaxScrollSteps
        // drags, then no tap and "could not be brought into view".
        var s = new Scrolled(0, ("Far row", 100_000));
        s.Tablet.OnDrag = (_, y0, y1) =>
        {
            s.Offset += (y0 - y1) / 10.0;   // a tenth of each drag: it moves, but never far enough
            s.Tablet.Show(s.PageAt());
        };

        await s.Tablet.Runner().ClickAsync(s.Tablet.Press(s.Row("Far row")), CancellationToken.None);

        Assert.Equal(Bbs757EfbPressRunner.MaxScrollSteps, s.Tablet.Drags.Count);
        Assert.Empty(s.Tablet.Taps);
        Assert.Equal(new[] { "Far row could not be brought into view, not pressed." }, s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_press_canceled_while_it_scrolls_taps_nothing_and_says_nothing()
    {
        // The window hid (or the bridge stopped) between two drags: the next drag answers false as canceled, and nothing is said.
        var s = new Scrolled(0, ("Far row", 5000));
        using var cts = new CancellationTokenSource();
        var runner = new Bbs757EfbPressRunner(new Bbs757EfbPressIo
        {
            Read = _ => Task.FromResult<Bbs757EfbScreenModel?>(s.Tablet.Page),
            Drag = (_, y0, y1, ct) =>
            {
                if (ct.IsCancellationRequested) return Task.FromResult(false);
                s.Offset += y0 - y1;
                s.Tablet.Show(s.PageAt());
                cts.Cancel();   // canceled after the first drag
                return Task.FromResult(true);
            },
            Tap = (p, _) => { s.Tablet.Taps.Add(p); return Task.FromResult(true); },
            ReadInstalledCycle = () => null,
            ReadVar = (_, _) => Task.FromResult<double?>(null),
            ReadRegistered = (_, _) => Task.FromResult<double?>(null),
            ReadSetting = (_, _) => Task.FromResult<Bbs757Value?>(null),
            Alert = s.Tablet.Alerts.Add,
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            NowMs = () => 0,
            ArmServiceEcho = _ => { },
            SettleServiceEcho = (_, _) => { },
            ArmGroundPowerEcho = () => { },
            SettleGroundPowerEcho = _ => { },
            Hooked = (_, _) => { },
        });

        await runner.ClickAsync(s.Tablet.Press(s.Row("Far row")), cts.Token);

        Assert.Empty(s.Tablet.Taps);
        Assert.Empty(s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_control_in_the_side_menu_taps_where_it_is_with_no_drag()
    {
        var home = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(72, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { home }, new[] { ReqFuelTruck() }));

        await tablet.Runner().ClickAsync(tablet.Press(home), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { home.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_tab_press_says_its_new_words_once()
    {
        var tab = new Bbs757EfbItem(Bbs757EfbKind.Tab, "Hydraulic", new Rectangle(300, 200, 150, 40), new Point(375, 220), true, "tab:HYDRAULIC");
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "MAINTENANCE", tab));
        // The reader keeps a tab's words and flips only Selected; "(current)" is the window's.
        tablet.OnTap = _ => tablet.Show(PageOf("AIRCRAFT", "MAINTENANCE", tab with { Selected = true }));

        await tablet.Runner().ClickAsync(tablet.Press(tab), CancellationToken.None);

        Assert.Equal(new[] { "Hydraulic (current)" }, tablet.Alerts);
    }

    // ---- the pushes: a press is the push's it was made on ---------------------------------------------------------------

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

    private static List<int> Numbers(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => int.Parse(b.Data[$"items.{i}.aidx"])).ToList();

    [Fact]
    public void A_press_from_an_older_push_finds_that_push_and_its_page()
    {
        var pushes = new Bbs757EfbPushes();
        var first = pushes.Show(Ground(FuelTruck("off")));
        var built1 = pushes.Push(first, null, NoExtras);
        var second = pushes.Show(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true)));
        var built2 = pushes.Push(second, null, NoExtras);

        int aidx = int.Parse(Field(built1, "Fuel truck off", "aidx"));
        Assert.True(pushes.TryFind(aidx, out var action, out var page));
        Assert.Equal("Fuel truck off", action!.Item!.Text);
        Assert.Same(first, page);
        Assert.Empty(Numbers(built1).Intersect(Numbers(built2)));
        Assert.False(pushes.TryFind(0, out _, out _));
        Assert.False(pushes.TryFind(Numbers(built1).Concat(Numbers(built2)).Max() + 1, out _, out _));
    }

    [Fact]
    public void An_element_keeps_its_number_while_it_stays_on_the_page()
    {
        var pushes = new Bbs757EfbPushes();
        var ground = Ground(FuelTruck("off"), ReqTug());
        var page = pushes.Show(ground);
        var before = pushes.Push(page, null, NoExtras);
        var withAlert = pushes.Push(page, "Req PB tug: no change on the tablet.", NoExtras);
        var reread = pushes.Push(pushes.Show(ground with { }), null, NoExtras);

        Assert.Equal(Numbers(before), Numbers(withAlert).Skip(1));
        Assert.Equal(Numbers(before), Numbers(reread));
        Assert.DoesNotContain(Numbers(withAlert)[0], Numbers(before));

        var away = pushes.Push(pushes.Show(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true))), null, NoExtras);
        var back = pushes.Push(pushes.Show(ground), null, NoExtras);
        Assert.Empty(Numbers(back).Intersect(Numbers(before).Concat(Numbers(withAlert)).Concat(Numbers(away))));
    }

    [Fact]
    public void Each_question_numbers_its_answers_apart()
    {
        var pushes = new Bbs757EfbPushes();
        var defuel = pushes.Show(FuelAsked(DefuelQuestion, YesAnswer));
        var a = pushes.Push(defuel, null, NoExtras);
        var b = pushes.Push(pushes.Show(FuelAsked(UnitsQuestion, YesAnswer)), null, NoExtras);

        int yesA = int.Parse(Field(a, "Yes", "aidx")), yesB = int.Parse(Field(b, "Yes", "aidx"));
        Assert.NotEqual(yesA, yesB);
        Assert.True(pushes.TryFind(yesA, out _, out var page));
        Assert.Same(defuel, page);

        var again = pushes.Push(pushes.Show(FuelAsked(UnitsQuestion.Replace("system?", "system ?"), YesAnswer)), null, NoExtras);
        Assert.Equal(yesB, int.Parse(Field(again, "Yes", "aidx")));
    }

    [Fact]
    public void Old_pushes_are_let_go()
    {
        var pushes = new Bbs757EfbPushes();
        var builds = new List<Bbs757EfbElements.Built>();
        for (int i = 0; i < Bbs757EfbPushes.KeptPushes + 1; i++)
            builds.Add(pushes.Push(pushes.Show(PageOf("AIRCRAFT", "SECTION " + i, ReqFuelTruck())), null, NoExtras));

        Assert.False(pushes.TryFind(builds[0].Actions.Keys.First(), out _, out _));
        Assert.True(pushes.TryFind(builds[1].Actions.Keys.First(), out _, out _));
    }

    // ---- what a read says by itself: a new question, BlueBird's new notifications ----------------------------------------

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
            news.After(asked with { }, long.MaxValue),
            news.After(Asked("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel ?"), long.MaxValue),
            news.After(quiet, long.MaxValue),
            news.After(asked, 100),
            news.After(Asked("Fuel adjustment is complete!"), long.MaxValue),
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
        Assert.Null(news.After(toast, Bbs757EfbNotices.QuietAfterPressMs - 1));
        Assert.Null(news.After(toast, long.MaxValue));
    }

    [Fact]
    public void A_question_and_a_notification_together_are_one_text_the_question_first()
    {
        var news = new Bbs757EfbReadNews();
        var quiet = Ground(ReqFuelTruck());
        var both = quiet with
        {
            Question = new Bbs757EfbQuestion("Switch the aircraft unit system?", new[] { YesAnswer }),
            Notices = new[] { new Bbs757EfbNotice("SIMBRIEF OFP received.", "14:17Z") },
        };

        Assert.Null(news.After(quiet, long.MaxValue));
        Assert.Equal("Switch the aircraft unit system? SIMBRIEF OFP received.", news.After(both, long.MaxValue));
    }

    // ---- the bridge itself: what a read pushes, and the AIRAC page's extras ------------------------------------------------

    /// <summary>A bridge whose loop never started, made with no synchronization context (its pushes raised inline), each push kept;
    /// the memory read, sign-in state and flow are the test's.</summary>
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
                    ReadModel = _ => Task.FromResult(ModelRead()),
                    ReadRegistered = (_, _) => Task.FromResult<double?>(null),
                    ReadSetting = (_, _) => Task.FromResult<Bbs757Value?>(null),
                    SignedIn = _ => { SignedInReads++; return Task.FromResult(SignedInAnswer); },
                    SignIn = ct => SignInFlow(ct),
                    Hooked = (hook, _) => Hooks.Add(hook),
                    ArmServiceEcho = _ => { },
                    SettleServiceEcho = (_, _) => { },
                    ArmGroundPowerEcho = () => { },
                    SettleGroundPowerEcho = _ => { },
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
        public Func<Bbs757EfbModelRead> ModelRead { get; set; } = () => new Bbs757EfbModelRead(null, "no simulator in this test");
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
        var quiet = PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45)));
        h.Bridge.TakeRead(quiet);
        var asked = quiet with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { YesAnswer }) };

        h.Bridge.TakeRead(asked);

        var push = h.Pushes[^1];
        Assert.Equal("alert", push["items.0.kind"]);
        Assert.Equal(asked.Question!.Text, push["items.0.text"]);
        Assert.Equal(asked.Question.Text, push["items.1.text"]);
        Assert.Equal("", push["items.1.kind"]);
        Assert.Single(push, kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");

        int pushes = h.Pushes.Count;
        h.Bridge.TakeRead(asked with { });
        Assert.Equal(pushes, h.Pushes.Count);
    }

    [Fact]
    public void Another_page_gets_other_numbers()
    {
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Ground(FuelTruck("off")));
        h.Bridge.TakeRead(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true)));

        var first = h.Pushes[^2].Where(kv => kv.Key.EndsWith(".aidx", StringComparison.Ordinal)).Select(kv => kv.Value);
        var second = h.Pushes[^1].Where(kv => kv.Key.EndsWith(".aidx", StringComparison.Ordinal)).Select(kv => kv.Value);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public void A_read_that_could_not_be_made_shows_its_reason_as_a_status()
    {
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(null, "The BlueBird tablet could not be read.") };

        var model = h.Bridge.ReadNowAsync(CancellationToken.None, asked: false).GetAwaiter().GetResult();

        Assert.Null(model);
        Assert.Equal("The BlueBird tablet could not be read.", h.Pushes[^1]["items.0.text"]);
        Assert.Equal("alert", h.Pushes[^1]["items.0.kind"]);
    }

    [Fact]
    public async Task The_AIRAC_page_offers_the_sign_in_while_signed_out_and_shows_its_code_while_it_is_spoken()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        var push = h.Pushes[^1];
        int signIn = IndexOf(push, Bbs757EfbExtras.SignInText);
        Assert.Equal("x:navigraph", push[$"items.{signIn}.key"]);
        Assert.Equal("button", push[$"items.{signIn}.kind"]);

        List<string>? whileSpoken = null;
        h.SignInFlow = _ =>
        {
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
    public async Task The_airac_page_from_memory_offers_the_sign_in()
    {
        // The real tree of Settings > AIRAC data, read by the model reader: its page name is "Settings, AIRAC data".
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("55-settings-airac-data"))!;
        Assert.Equal("Settings, AIRAC data", model.PageName);
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(model);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        var push = h.Pushes[^1];
        int signIn = IndexOf(push, Bbs757EfbExtras.SignInText);
        Assert.Equal("x:navigraph", push[$"items.{signIn}.key"]);
    }

    [Fact]
    public async Task A_press_queued_when_the_window_hid_never_runs()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        int signIns = 0;
        h.SignInFlow = _ => { signIns++; return Task.FromResult("Navigraph code: ABCD-1234"); };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        var push = h.Pushes[^1];
        var press = new Dictionary<string, string> { ["index"] = push[$"items.{IndexOf(push, Bbs757EfbExtras.SignInText)}.aidx"] };

        h.Bridge.EnqueueCommand("click_display_element", press);
        h.Bridge.SetActive(false);
        h.Bridge.SetActive(true);

        Assert.False(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.Equal(0, signIns);

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
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        Assert.Equal(1, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }

    [Fact]
    public async Task The_sign_in_is_read_only_on_the_AIRAC_page()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(Ground(FuelTruck("off")));
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        Assert.Equal(0, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }

    [Fact]
    public async Task A_read_asked_for_on_a_still_AIRAC_page_reads_the_sign_in_again()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        Assert.Equal(1, h.SignedInReads);

        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        Assert.Equal(1, h.SignedInReads);

        h.SignedInAnswer = true;
        h.Bridge.TakeRead(AiracPage(), asked: true);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        Assert.Equal(2, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }
}
