using System.Globalization;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The parts of the 757 EFB window that need no simulator: the AIRAC cycle for a date, when the
/// nav data update may click (BlueBird's REMOVE button sits where UPDATE was once the installed
/// cycle is current), the click sequences, BlueBird's work folder and the two values read from it,
/// and the Navigraph sign-in helpers.
/// </summary>
public class Bbs757EfbTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- AIRAC cycle ------------------------------------------------------

    [Theory]
    [InlineData("2026-10-01", 2610)]
    [InlineData("2026-09-30", 2609)]
    [InlineData("2026-01-22", 2601)]
    [InlineData("2025-12-31", 2513)]
    [InlineData("2025-01-23", 2501)]   // the anchor
    [InlineData("2025-01-22", 2413)]   // the day before: 2024's last (13th) cycle
    [InlineData("2026-10-25", 2610)]
    [InlineData("2026-10-29", 2611)]
    [InlineData("2020-12-31", 2014)]   // a year with 14 cycles
    [InlineData("2021-01-27", 2014)]
    [InlineData("2021-01-28", 2101)]
    public void AiracCycle_for_a_date(string date, int expected)
    {
        Assert.Equal(expected, AiracCycle.ForDate(D(date)));
    }

    [Theory]
    [InlineData(2610, false)]   // current on 2026-10-25: BlueBird shows REMOVE at the UPDATE spot
    [InlineData(2611, false)]   // newer than the date's cycle: nothing to update either
    [InlineData(null, true)]    // nothing installed (no cycle file)
    [InlineData(2603, true)]
    [InlineData(2513, true)]
    public void AiracUpdate_refuses_when_installed_is_current(int? installed, bool expected)
    {
        Assert.Equal(expected, Bbs757EfbLayout.ShouldUpdate(installed, D("2026-10-25")));
    }

    // ---- click sequences --------------------------------------------------

    private static IEnumerable<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>> EveryHomeSequence()
    {
        yield return Bbs757EfbLayout.OpenAiracPage();
        yield return Bbs757EfbLayout.UpdateNavData();
        yield return Bbs757EfbLayout.EnterSimBriefId("189");
        yield return Bbs757EfbLayout.EnterSimBriefId("1234567890");
        yield return Bbs757EfbLayout.FetchOfp();
        foreach (string preset in Bbs757EfbLayout.PresetNames) yield return Bbs757EfbLayout.LoadPreset(preset);
        foreach (string quick in Bbs757EfbLayout.QuickStartNames) yield return Bbs757EfbLayout.QuickStart(quick);
    }

    [Fact]
    public void EnterSimBriefId_clicks_home_settings_airac_field_digits_ok_in_order()
    {
        var seq = Bbs757EfbLayout.EnterSimBriefId("189");

        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.SettingsIcon, Bbs757EfbLayout.AiracData, Bbs757EfbLayout.SimBriefField,
            Bbs757EfbLayout.Key1, Bbs757EfbLayout.Key8, Bbs757EfbLayout.Key9, Bbs757EfbLayout.KeyOk
        }, seq.Select(s => s.Point));
        Assert.Equal(new EfbPoint(823, 413), seq[4].Point);
        Assert.Equal(new EfbPoint(936, 255), seq[5].Point);
        Assert.Equal(new EfbPoint(1052, 255), seq[6].Point);
        Assert.Equal(new EfbPoint(1052, 565), seq[7].Point);
        // The digits go in 500 ms apart.
        Assert.Equal(500, seq[4].DelayAfterMs);
        Assert.Equal(500, seq[5].DelayAfterMs);
        Assert.Equal(500, seq[6].DelayAfterMs);
    }

    [Fact]
    public void Every_keypad_digit_has_its_measured_key()
    {
        var seq = Bbs757EfbLayout.EnterSimBriefId("1234567890");
        var digits = seq.Skip(4).Take(10).Select(s => s.Point).ToList();

        Assert.Equal(new[]
        {
            new EfbPoint(823, 413), new EfbPoint(936, 413), new EfbPoint(1052, 413),
            new EfbPoint(823, 334), new EfbPoint(936, 334), new EfbPoint(1052, 334),
            new EfbPoint(823, 255), new EfbPoint(936, 255), new EfbPoint(1052, 255),
            new EfbPoint(936, 489)
        }, digits);
        Assert.Equal(Bbs757EfbLayout.KeyOk, seq[^1].Point);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12a")]
    [InlineData(" 189")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\u0661\u0662")]   // Arabic-Indic digits: char.IsDigit, but no key on the keypad
    [InlineData("12345678901")]    // longer than BlueBird's int can hold
    public void A_simbrief_id_that_is_not_plain_digits_is_never_typed(string text)
    {
        Assert.False(Bbs757EfbLayout.IsSimBriefId(text));
        Assert.Throws<ArgumentException>(() => Bbs757EfbLayout.EnterSimBriefId(text));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("189")]
    [InlineData("2147483647")]
    public void Plain_digit_simbrief_ids_are_accepted(string text)
    {
        Assert.True(Bbs757EfbLayout.IsSimBriefId(text));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("2147483648")]
    public void A_simbrief_id_blueBird_cannot_store_is_refused(string text)
    {
        Assert.False(Bbs757EfbLayout.IsSimBriefId(text));
    }

    [Fact]
    public void Every_sequence_from_the_home_screen_starts_with_home_and_1500_ms()
    {
        foreach (var seq in EveryHomeSequence())
        {
            Assert.NotEmpty(seq);
            Assert.Equal(Bbs757EfbLayout.Home, seq[0].Point);
            Assert.Equal(1500, seq[0].DelayAfterMs);
        }
    }

    [Fact]
    public void Only_the_nav_data_update_clicks_the_update_and_remove_spot()
    {
        var spot = new EfbPoint(1291, 337);
        Assert.Equal(spot, Bbs757EfbLayout.UpdateButton);

        var update = Bbs757EfbLayout.UpdateNavData();
        Assert.Equal(spot, update[^1].Point);
        Assert.Single(update, s => s.Point == spot);

        var others = new List<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>>
        {
            Bbs757EfbLayout.OpenAiracPage(),
            Bbs757EfbLayout.EnterSimBriefId("189"),
            Bbs757EfbLayout.EnterSimBriefId("1234567890"),
            Bbs757EfbLayout.FetchOfp(),
            Bbs757EfbLayout.AnswerUnits(accept: true),
            Bbs757EfbLayout.AnswerUnits(accept: false),
        };
        others.AddRange(Bbs757EfbLayout.PresetNames.Select(Bbs757EfbLayout.LoadPreset));
        others.AddRange(Bbs757EfbLayout.QuickStartNames.Select(Bbs757EfbLayout.QuickStart));
        foreach (var seq in others)
            Assert.DoesNotContain(seq, s => s.Point == spot);
    }

    [Fact]
    public void The_nav_data_update_opens_the_airac_page_then_waits_4_s_before_clicking()
    {
        var open = Bbs757EfbLayout.OpenAiracPage();
        var update = Bbs757EfbLayout.UpdateNavData();

        Assert.Equal(new[] { Bbs757EfbLayout.Home, Bbs757EfbLayout.SettingsIcon, Bbs757EfbLayout.AiracData },
            open.Select(s => s.Point));
        Assert.Equal(open.Select(s => s.Point).Append(Bbs757EfbLayout.UpdateButton), update.Select(s => s.Point));
        Assert.True(update[^2].DelayAfterMs >= open[^1].DelayAfterMs + 4000);
    }

    [Fact]
    public void Fetch_ofp_goes_through_the_navigation_app_and_the_acars_dashboard()
    {
        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.NavigationIcon, Bbs757EfbLayout.AcarsDashboard, Bbs757EfbLayout.FetchOfpButton
        }, Bbs757EfbLayout.FetchOfp().Select(s => s.Point));
        Assert.Equal(new EfbPoint(503, 190), Bbs757EfbLayout.NavigationIcon);
        Assert.Equal(new EfbPoint(135, 179), Bbs757EfbLayout.AcarsDashboard);
        Assert.Equal(new EfbPoint(441, 120), Bbs757EfbLayout.FetchOfpButton);
    }

    [Theory]
    [InlineData("Cold and dark", 433, 214)]
    [InlineData("Turnaround", 433, 266)]
    [InlineData("Before start", 433, 319)]
    [InlineData("Ready for taxi", 433, 372)]
    [InlineData("Ready for takeoff", 433, 424)]
    [InlineData("After takeoff", 818, 214)]
    [InlineData("Cruise", 818, 266)]
    [InlineData("Approach", 818, 319)]
    [InlineData("Final approach", 818, 372)]
    public void A_preset_is_home_aircraft_then_the_preset(string name, int x, int y)
    {
        var seq = Bbs757EfbLayout.LoadPreset(name);

        Assert.Equal(new[] { Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, new EfbPoint(x, y) }, seq.Select(s => s.Point));
        Assert.Contains(name, Bbs757EfbLayout.PresetNames);
    }

    [Fact]
    public void The_preset_list_is_the_nine_tiles_in_cockpit_order()
    {
        Assert.Equal(new[]
        {
            "Cold and dark", "Turnaround", "Before start", "Ready for taxi", "Ready for takeoff",
            "After takeoff", "Cruise", "Approach", "Final approach"
        }, Bbs757EfbLayout.PresetNames);
    }

    [Theory]
    [InlineData("APU quick start", 433, 524)]
    [InlineData("Engine quick start", 818, 524)]
    [InlineData("IRS rapid align", 1197, 524)]
    public void A_quick_start_is_home_aircraft_then_the_tile(string name, int x, int y)
    {
        Assert.Equal(new[] { Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, new EfbPoint(x, y) },
            Bbs757EfbLayout.QuickStart(name).Select(s => s.Point));
    }

    [Fact]
    public void Unknown_preset_and_quick_start_names_throw()
    {
        Assert.Throws<ArgumentException>(() => Bbs757EfbLayout.LoadPreset("Taxi"));
        Assert.Throws<ArgumentException>(() => Bbs757EfbLayout.QuickStart("APU"));
    }

    [Fact]
    public void The_units_answer_is_one_click_on_the_open_dialog_without_going_home()
    {
        Assert.Equal(new[] { new EfbPoint(390, 658) }, Bbs757EfbLayout.AnswerUnits(accept: true).Select(s => s.Point));
        Assert.Equal(new[] { new EfbPoint(993, 658) }, Bbs757EfbLayout.AnswerUnits(accept: false).Select(s => s.Point));
    }

    [Fact]
    public void Home_and_the_icons_sit_at_their_measured_positions()
    {
        Assert.Equal(new EfbPoint(19, 62), Bbs757EfbLayout.Home);
        Assert.Equal(new EfbPoint(320, 190), Bbs757EfbLayout.AircraftIcon);
        Assert.Equal(new EfbPoint(1073, 190), Bbs757EfbLayout.SettingsIcon);
        Assert.Equal(new EfbPoint(95, 486), Bbs757EfbLayout.AiracData);
        Assert.Equal(new EfbPoint(842, 126), Bbs757EfbLayout.SimBriefField);
    }

    [Fact]
    public void A_sequence_goes_to_the_agent_as_invariant_integer_triples()
    {
        InCulture("de-DE", () =>
        {
            Assert.Equal("[[19,62,1500],[1073,190,1500]]",
                Bbs757EfbLayout.ToJson(new (EfbPoint, int)[] { (new EfbPoint(19, 62), 1500), (new EfbPoint(1073, 190), 1500) }));
        });
        InCulture("sv-SE", () =>
        {
            Assert.Equal("[[1291,337,0]]", Bbs757EfbLayout.ToJson(new (EfbPoint, int)[] { (Bbs757EfbLayout.UpdateButton, 0) }));
        });
    }

    // ---- BlueBird's work folder -------------------------------------------

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msfsba-bbs757-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string Sub(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Find_picks_the_first_root_that_exists()
    {
        using var tmp = new TempDir();
        string missing = tmp.Sub("missing"), second = tmp.Sub("second"), third = tmp.Sub("third");
        Directory.CreateDirectory(second);
        Directory.CreateDirectory(third);

        Assert.Equal(second, Bbs757WorkFolder.Find(new[] { missing, second, third }));
        Assert.Equal(third, Bbs757WorkFolder.Find(new[] { third, second }));
        Assert.Null(Bbs757WorkFolder.Find(new[] { missing }));
        Assert.Null(Bbs757WorkFolder.Find(Array.Empty<string>()));
    }

    [Fact]
    public void The_default_roots_are_the_five_install_locations_in_order()
    {
        var roots = Bbs757WorkFolder.DefaultRoots();

        Assert.Equal(5, roots.Count);
        Assert.EndsWith(@"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work", roots[0]);
        Assert.EndsWith(@"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024\bluebirdsim-aircraft-757-200\work", roots[1]);
        Assert.EndsWith(@"Microsoft Flight Simulator 2024\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work", roots[2]);
        Assert.EndsWith(@"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\bluebirdsim-aircraft-757-200\work", roots[3]);
        Assert.EndsWith(@"Microsoft Flight Simulator\Packages\bluebirdsim-aircraft-757-200\work", roots[4]);
    }

    private static void WriteCycle(string work, string json)
    {
        string nav = Path.Combine(work, "NavigationData");
        Directory.CreateDirectory(nav);
        File.WriteAllText(Path.Combine(nav, "cycle.json"), json);
    }

    [Fact]
    public void ReadInstalledCycle_reads_the_cycle_from_blueBirds_cycle_file()
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-cycle.json")));

        Assert.True(Bbs757WorkFolder.HasCycleFile(tmp.Path));
        Assert.Equal(2610, Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"cycle\":\"abcd\"}")]
    [InlineData("{\"cycle\":\"26100\"}")]
    [InlineData("{\"cycle\":\"2615\"}")]    // no 15th cycle in a year
    [InlineData("{\"cycle\":\"2600\"}")]
    [InlineData("{\"cycle\":true}")]
    [InlineData("{\"cycle\":{\"id\":2610}}")]
    [InlineData("{\"cycle\":26.10}")]
    public void ReadInstalledCycle_returns_null_for_garbage(string json)
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, json);

        Assert.True(Bbs757WorkFolder.HasCycleFile(tmp.Path));
        Assert.Null(Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Fact]
    public void ReadInstalledCycle_takes_a_numeric_cycle_too()
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, "{\"cycle\":2610}");

        Assert.Equal(2610, Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Fact]
    public void With_no_cycle_file_nothing_is_installed()
    {
        using var tmp = new TempDir();

        Assert.False(Bbs757WorkFolder.HasCycleFile(tmp.Path));
        Assert.Null(Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
        Assert.Null(Bbs757WorkFolder.ReadInstalledCycle(tmp.Sub("no-such-folder")));
    }

    private static void WriteSettings(string work, string json) => File.WriteAllText(Path.Combine(work, "efb_settings.json"), json);

    [Theory]
    [InlineData("{\"simbrief_id\":-1}")]
    [InlineData("{\"simbrief_id\":0}")]
    [InlineData("{}")]
    [InlineData("{\"simbrief_id\":null}")]
    [InlineData("{\"simbrief_id\":\"abc\"}")]
    [InlineData("{\"simbrief_id\":12.5}")]
    [InlineData("not json")]
    [InlineData("[123456]")]
    public void ReadSimBriefId_maps_minus_one_and_garbage_to_null(string json)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Null(Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    [Fact]
    public void ReadSimBriefId_reads_only_the_simbrief_id()
    {
        using var tmp = new TempDir();
        // A made-up ID beside made-up credentials: only the ID comes back.
        WriteSettings(tmp.Path,
            "{\"hoppie_logon_code\":\"NOT-A-REAL-CODE\",\"sayintentions_acars_key\":\"NOT-A-REAL-KEY\",\"simbrief_id\":123456,\"tiller_realistic\":true}");

        Assert.Equal(123456L, Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    [Fact]
    public void ReadSimBriefId_with_no_settings_file_is_null()
    {
        using var tmp = new TempDir();

        Assert.Null(Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    // ---- Navigraph sign-in ------------------------------------------------

    [Theory]
    [InlineData("https://navigraph.com/code?user_code=ABCD1234", true)]
    [InlineData("https://identity.api.navigraph.com/code?user_code=ABCD1234", true)]
    [InlineData("https://NAVIGRAPH.COM/code", true)]
    [InlineData("http://navigraph.com/code", false)]                // not https
    [InlineData("https://evilnavigraph.com/code", false)]
    [InlineData("https://navigraph.com.evil.example/code", false)]
    [InlineData("https://evil.example/navigraph.com", false)]
    [InlineData("https://evil.example/?next=https://navigraph.com", false)]
    [InlineData("https://navigraph.com@evil.example/", false)]
    [InlineData("https://sub.navigraph.com/code", false)]
    [InlineData("https://navigraph.com:8443/code", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("navigraph.com/code", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_https_navigraph_addresses_are_opened(string? uri, bool expected)
    {
        Assert.Equal(expected, BBS757EfbForm.IsTrustedNavigraphUri(uri, out _));
    }

    [Theory]
    [InlineData("ABCD1234", "A B C D 1 2 3 4")]
    [InlineData("ab-12", "a b dash 1 2")]
    [InlineData(" XY ", "X Y")]
    public void The_navigraph_code_is_spelled_letter_by_letter(string code, string expected)
    {
        Assert.Equal(expected, BBS757EfbForm.SpellCode(code));
    }

    // ---- client with nothing to reach ---------------------------------------

    [Fact]
    public async Task With_no_agent_the_efb_client_is_not_ready_and_runs_nothing()
    {
        using var client = new CoherentBbs757EfbClient(() => "");
        client.Start();

        Assert.False(client.IsReady);
        Assert.False(await client.RunAsync(Bbs757EfbLayout.FetchOfp(), CancellationToken.None));
        Assert.False(await client.RunAsync(Array.Empty<(EfbPoint, int)>(), CancellationToken.None));
    }

    [Fact]
    public void The_efb_agent_is_es5_and_answers_with_the_clients_markers()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));

        Assert.Contains("'" + CoherentBbs757EfbClient.InstallMarker + "'", agent);
        Assert.Contains("window.__MSFSBA_BBS757EFB = A;", agent);
        Assert.Contains("MSFSBA_BBS757_EFB_NOT_READY:", agent);
        // ES5 only (Coherent GT is an old Chromium): checked on the code with its comments removed.
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        Assert.DoesNotContain("=>", code);
        Assert.DoesNotContain("`", code);
        Assert.DoesNotMatch(@"\b(let|const|class)\s", code);
        Assert.DoesNotContain("Array.from", code);
        Assert.DoesNotContain(".includes(", code);
        Assert.StartsWith("(function () {", code.TrimStart());
        Assert.Contains("} catch (e) {", code);
    }
}
