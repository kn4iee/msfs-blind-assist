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
            Wheel = (delta, _) => { Events.Add("wheel " + delta.ToString(CultureInfo.InvariantCulture)); return Task.CompletedTask; },
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
        Assert.Equal(new[] { "The tablet did not show Fuel. Try again, or use Read the tablet again." }, tablet.Alerts);
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

        Assert.Equal(new[] { "The tablet did not show Home. Try again, or use Read the tablet again." }, tablet.Alerts);
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
        // A tab (a Menu keyed tab:, not menu:/app:) changes the list under it, not the page name: judged by the page like a button.
        var tab = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Metar", new Rectangle(300, 100, 120, 40), new Point(360, 120), true, "tab:METAR");
        var tablet = new FakeTablet(PageOf("NAVIGATION", "WEATHER & NOTAM", tab));
        var press = tablet.Press(tab);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "WEATHER & NOTAM", tab with { Selected = true, Text = "Metar (selected)" }));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tab.Hit }, tablet.Taps);
        // No "did not show" navigation message: a tab is not a navigation.
        Assert.DoesNotContain(tablet.Alerts, a => a.Contains("did not show", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_scroll_wheels_the_page_and_reads_it()
    {
        var tablet = new FakeTablet(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true)) with { Scrollable = true });

        await tablet.Runner().ScrollAsync(down: true, CancellationToken.None);
        await tablet.Runner().ScrollAsync(down: false, CancellationToken.None);

        Assert.Equal(new[] { "wheel 300", "read", "wheel -300", "read" }, tablet.Events);
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
    public async Task A_two_choice_setting_is_judged_by_the_choice_that_was_not_set_and_says_it(string culture, long stored, bool notChanged)
    {
        var cpdlc = Switch("CPDLC network", "tgl:CPDLC NETWORK", on: false, value: "Hoppie");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", cpdlc));
        tablet.File["cpdlc_network"] = Bbs757Value.Of(0L);
        var press = tablet.Press(cpdlc);
        tablet.OnTap = _ =>
        {
            tablet.File["cpdlc_network"] = Bbs757Value.Of(stored);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", cpdlc with { On = true, Value = "SayIntentions" }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { notChanged ? "CPDLC network did not change." : "CPDLC network: SayIntentions" }, tablet.Alerts);
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
        tablet.OnTap = p => tablet.Show(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling with { Slider = new Bbs757EfbSlider(500, 900, p.X) }));

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
        tablet.OnTap = p => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", irs with { Slider = new Bbs757EfbSlider(895, 1190, p.X) }));

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
