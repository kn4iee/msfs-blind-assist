using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The parts of the 757 EFB window that need no simulator: the AIRAC cycle for a date, when the
/// nav data update may click (BlueBird's REMOVE button sits where UPDATE was once the installed
/// cycle is current), the click sequences, BlueBird's work folder and the two values read from it,
/// the Navigraph sign-in helpers, and the ground power buttons' flow (<c>Bbs757GroundPower.SwitchAsync</c>)
/// over a fake tablet, a fake variable and a fake clock.
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

    [Theory]
    [InlineData(2501, "2025-01-23")]
    [InlineData(2513, "2025-12-25")]
    [InlineData(2601, "2026-01-22")]
    [InlineData(2610, "2026-10-01")]
    [InlineData(2611, "2026-10-29")]
    [InlineData(2014, "2020-12-31")]
    [InlineData(2101, "2021-01-28")]
    public void AiracCycle_effective_date(int cycle, string date)
    {
        Assert.Equal(D(date), AiracCycle.EffectiveDate(cycle));
    }

    [Theory]
    [InlineData(2514)]   // 2025 had thirteen cycles
    [InlineData(2600)]
    [InlineData(2615)]
    [InlineData(999)]
    [InlineData(10001)]
    public void AiracCycle_effective_date_refuses_a_cycle_that_never_was(int cycle)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiracCycle.EffectiveDate(cycle));
    }

    [Fact]
    public void Every_date_falls_in_the_cycle_whose_effective_date_it_follows()
    {
        for (var d = D("2020-01-01"); d < D("2031-01-01"); d = d.AddDays(1))
        {
            int cycle = AiracCycle.ForDate(d);
            var effective = AiracCycle.EffectiveDate(cycle);
            Assert.True(effective <= d && d < effective.AddDays(28), $"{d:yyyy-MM-dd}: {cycle} effective {effective:yyyy-MM-dd}");
        }
    }

    // ---- the nav data update gate, bound to the page ---------------------------

    private static DateTime U(string iso) => DateTime.SpecifyKind(
        DateTime.ParseExact(iso, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Utc);

    [Theory]
    // Installed cycle current: nothing to do, whatever else holds.
    [InlineData(2610, "2026-10-25 12:00:00", "2026-10-25 08:00:00", true, false, Bbs757UpdateGate.Current)]
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-25 08:00:00", true, false, Bbs757UpdateGate.Allowed)]
    [InlineData(null, "2026-10-25 12:00:00", "2026-10-25 08:00:00", true, false, Bbs757UpdateGate.Allowed)]
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-25 08:00:00", false, false, Bbs757UpdateGate.NotSignedIn)]
    // Signed in after this load: BlueBird's load-time check ran signed out.
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-25 08:00:00", true, true, Bbs757UpdateGate.ReloadFirst)]
    // The page must have loaded on or after the cycle's first full day (2610: 2026-10-02 00:00 UTC).
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-01 23:59:59", true, false, Bbs757UpdateGate.ReloadFirst)]
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-02 00:00:00", true, false, Bbs757UpdateGate.Allowed)]
    // On a new cycle's first day no page can qualify yet: reloading would not help.
    [InlineData(2610, "2026-10-29 10:00:00", "2026-10-29 09:00:00", true, false, Bbs757UpdateGate.NewToday)]
    [InlineData(2610, "2026-10-30 10:00:00", "2026-10-29 23:00:00", true, false, Bbs757UpdateGate.ReloadFirst)]
    [InlineData(2610, "2026-10-30 10:00:00", "2026-10-30 00:00:00", true, false, Bbs757UpdateGate.Allowed)]
    // BlueBird's check needs a minute after the load.
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-25 11:59:01", true, false, Bbs757UpdateGate.StillChecking)]
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-25 11:59:00", true, false, Bbs757UpdateGate.Allowed)]
    [InlineData(2603, "2026-10-25 12:00:00", "2026-10-25 12:00:30", true, false, Bbs757UpdateGate.StillChecking)]
    public void MayClickUpdate_needs_an_old_cycle_a_signed_in_load_after_the_cycle_s_first_day_and_a_minute(
        int? installed, string now, string pageLoaded, bool signedIn, bool sawSignedOut, Bbs757UpdateGate expected)
    {
        Assert.Equal(expected, Bbs757EfbLayout.MayClickUpdate(installed, U(now), U(pageLoaded), signedIn, sawSignedOut));
    }

    [Fact]
    public void The_update_gate_never_allows_what_ShouldUpdate_refuses()
    {
        var now = U("2026-10-25 12:00:00");
        foreach (int installed in new[] { 2610, 2611, 2701 })
        {
            Assert.False(Bbs757EfbLayout.ShouldUpdate(installed, DateOnly.FromDateTime(now)));
            Assert.Equal(Bbs757UpdateGate.Current, Bbs757EfbLayout.MayClickUpdate(installed, now, U("2026-10-25 08:00:00"), true, false));
        }
    }

    [Fact]
    public void The_earliest_page_load_is_the_day_after_the_cycle_took_effect()
    {
        Assert.Equal(U("2026-10-02 00:00:00"), Bbs757EfbLayout.EarliestPageLoadUtc(DateOnly.FromDateTime(U("2026-10-25 12:00:00"))));
        Assert.Equal(U("2026-10-30 00:00:00"), Bbs757EfbLayout.EarliestPageLoadUtc(DateOnly.FromDateTime(U("2026-10-30 00:00:00"))));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("2026-10-25 07:59:59", false)]   // read in the flight before this one
    [InlineData("2026-10-25 08:00:00", true)]
    [InlineData("2026-10-25 09:30:00", true)]
    public void A_signed_out_reading_counts_for_the_page_it_was_taken_on(string? signedOutAt, bool expected)
    {
        DateTime? at = signedOutAt == null ? null : U(signedOutAt);
        Assert.Equal(expected, Bbs757EfbLayout.SignedOutDuringLoad(at, U("2026-10-25 08:00:00")));
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
        yield return Bbs757EfbLayout.SetSteeringToRudder();
        yield return Bbs757EfbLayout.ToggleChocks();
        yield return Bbs757EfbLayout.ToggleGroundPower();
        yield return Bbs757EfbLayout.LoadThroughEfb();
    }

    [Fact]
    public void The_load_fetches_the_plan_then_imports_and_boards_answering_the_defuel_question_after_each()
    {
        // Measured 2026-10-25 (live-findings.md, "BlueBird's own loading through its EFB"): IMPORT FROM
        // SIMBRIEF and START BOARDING each ask to defuel when the fuel is above the plan, and the
        // question's YES is empty background on the Fuel page when no question is up.
        var seq = Bbs757EfbLayout.LoadThroughEfb();

        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.NavigationIcon, Bbs757EfbLayout.AcarsDashboard, Bbs757EfbLayout.FetchOfpButton,
            Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, Bbs757EfbLayout.AircraftMenuFuel, Bbs757EfbLayout.ImportFromSimBrief,
            Bbs757EfbLayout.DefuelYes, Bbs757EfbLayout.StartBoarding, Bbs757EfbLayout.DefuelYes, Bbs757EfbLayout.Home,
        }, seq.Select(s => s.Point));
        Assert.All(seq, s => Assert.Equal(Bbs757EfbLayout.NavigateDelayMs, s.DelayAfterMs));
        Assert.Equal(new EfbPoint(67, 237), Bbs757EfbLayout.AircraftMenuFuel);
        Assert.Equal(new EfbPoint(508, 962), Bbs757EfbLayout.ImportFromSimBrief);
        Assert.Equal(new EfbPoint(1231, 24), Bbs757EfbLayout.StartBoarding);
        Assert.Equal(new EfbPoint(400, 648), Bbs757EfbLayout.DefuelYes);
    }

    [Fact]
    public void No_load_tap_comes_near_the_update_and_remove_spot()
    {
        // The agent refuses any click this close to UPDATE/REMOVE without the update's guards.
        Assert.All(Bbs757EfbLayout.LoadThroughEfb(), s => Assert.False(
            Math.Abs(s.Point.X - Bbs757EfbLayout.UpdateButton.X) <= Bbs757EfbLayout.UpdateSpotHalfWidth &&
            Math.Abs(s.Point.Y - Bbs757EfbLayout.UpdateButton.Y) <= Bbs757EfbLayout.UpdateSpotHalfHeight, s.Point.ToString()));
    }

    [Fact]
    public void EnterSimBriefId_clicks_home_settings_airac_field_ten_backspaces_digits_ok_in_order()
    {
        var seq = Bbs757EfbLayout.EnterSimBriefId("189");

        var expected = new List<EfbPoint>
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.SettingsIcon, Bbs757EfbLayout.AiracData, Bbs757EfbLayout.SimBriefField,
        };
        expected.AddRange(Enumerable.Repeat(Bbs757EfbLayout.KeyBackspace, 10));
        expected.AddRange(new[] { Bbs757EfbLayout.Key1, Bbs757EfbLayout.Key8, Bbs757EfbLayout.Key9, Bbs757EfbLayout.KeyOk });
        Assert.Equal(expected, seq.Select(s => s.Point));

        Assert.Equal(new EfbPoint(823, 565), seq[4].Point);    // BKSP
        Assert.Equal(new EfbPoint(823, 413), seq[14].Point);   // 1
        Assert.Equal(new EfbPoint(936, 255), seq[15].Point);   // 8
        Assert.Equal(new EfbPoint(1052, 255), seq[16].Point);  // 9
        Assert.Equal(new EfbPoint(1052, 565), seq[17].Point);  // OK
        // Every keypad key, backspaces and digits, goes in 500 ms after the one before.
        Assert.All(seq.Skip(4).Take(13), s => Assert.Equal(500, s.DelayAfterMs));
    }

    [Fact]
    public void The_simbrief_entry_never_presses_the_keypad_s_delete_key()
    {
        Assert.DoesNotContain(Bbs757EfbLayout.EnterSimBriefId("1234567890"), s => s.Point == Bbs757EfbLayout.KeyDelete);
    }

    [Fact]
    public void Every_keypad_digit_has_its_measured_key()
    {
        var seq = Bbs757EfbLayout.EnterSimBriefId("1234567890");
        var digits = seq.Skip(14).Take(10).Select(s => s.Point).ToList();

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
        others.Add(Bbs757EfbLayout.SetSteeringToRudder());
        others.Add(Bbs757EfbLayout.ToggleChocks());
        others.Add(Bbs757EfbLayout.ToggleGroundPower());
        others.Add(Bbs757EfbLayout.LoadThroughEfb());
        foreach (var seq in others)
            Assert.DoesNotContain(seq, s => s.Point == spot);
    }

    // ---- Chocks ----------------------------------------------------------------------------------

    [Fact]
    public void The_chocks_are_home_aircraft_ground_services_then_the_chocks_tile()
    {
        // Measured 2026-10-25 (live-findings.md, EFB Ground Services): the tile toggles L:BBS757_vis_chocks.
        var seq = Bbs757EfbLayout.ToggleChocks();

        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, Bbs757EfbLayout.AircraftMenuGroundServices,
            Bbs757EfbLayout.GroundServicesChocks,
        }, seq.Select(s => s.Point));
        Assert.Equal(new EfbPoint(320, 190), Bbs757EfbLayout.AircraftIcon);
        Assert.Equal(new EfbPoint(128, 368), Bbs757EfbLayout.AircraftMenuGroundServices);
        Assert.Equal(new EfbPoint(404, 556), Bbs757EfbLayout.GroundServicesChocks);
        Assert.Single(seq, s => s.Point == Bbs757EfbLayout.GroundServicesChocks);   // one tap: it toggles
        Assert.All(seq, s => Assert.Equal(Bbs757EfbLayout.NavigateDelayMs, s.DelayAfterMs));
        Assert.Equal("BBS757_vis_chocks", Bbs757EfbLayout.ChocksVar);
    }

    [Theory]
    [InlineData(1.0, false, true)]    // in place, removing: tap
    [InlineData(0.0, true, true)]     // removed, setting: tap
    [InlineData(0.0, false, false)]   // already removed: a tap would put them back
    [InlineData(1.0, true, false)]
    [InlineData(null, false, false)]  // unreadable: never guess on a toggle
    [InlineData(double.NaN, true, false)]
    public void The_chocks_tile_is_tapped_only_when_the_chocks_differ_from_what_is_wanted(double? chocks, bool wantInPlace, bool tap)
    {
        Assert.Equal(tap, Bbs757EfbLayout.MayClickChocks(chocks, wantInPlace));
    }

    [Theory]
    [InlineData(false, 0.0, "Chocks removed")]
    [InlineData(true, 1.0, "Chocks in place")]
    [InlineData(false, 1.0, "BlueBird did not move the chocks")]
    [InlineData(true, 0.0, "BlueBird did not move the chocks")]
    [InlineData(false, null, "The chocks could not be read back.")]
    public void The_chocks_result_is_said_from_the_variable_read_after(bool wantInPlace, double? after, string phrase)
    {
        Assert.Equal(phrase, Bbs757EfbLayout.ChocksResultPhrase(wantInPlace, after));
    }

    [Fact]
    public void Chocks_already_where_wanted_say_so()
    {
        Assert.Equal("Chocks are already removed", Bbs757EfbLayout.ChocksAlreadyText(inPlace: false));
        Assert.Equal("Chocks are already in place", Bbs757EfbLayout.ChocksAlreadyText(inPlace: true));
    }

    // ---- Ground power ----------------------------------------------------------------------------

    private const string GpuKey = Bbs757SharedKeys.GpuConnected;

    // What the window says: the words are pinned here, as Bbs757GroundPowerTests pins them for the pure half.
    private const string GpuUnreadSaid = "The ground power cannot be read, so nothing was tapped.";
    private const string GpuAlreadyConnectedSaid = "Ground power is already connected.";
    private const string GpuAlreadyDisconnectedSaid = "Ground power is already disconnected.";
    private const string GpuOnLineSaid =
        "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or the engines " +
        "powering the aircraft, then try again.";
    private const string GpuExtPwrUnreadSaid = "External power cannot be read, so nothing was tapped.";
    private const string GpuConnectedSaid = "Ground power connected. Press EXT PWR on the Electrical panel to put it on line.";
    private const string GpuDisconnectedSaid = "Ground power disconnected.";
    private const string GpuNotConnectedSaid = "BlueBird did not connect the ground power.";
    private const string GpuNotDisconnectedSaid = "BlueBird did not disconnect the ground power.";
    private const string GpuUnreadNowSaid = "The ground power cannot be read now.";

    /// <summary>
    /// What stands behind <see cref="Bbs757GroundPowerIo"/> in these tests: the connection's reads come off a queue (the
    /// last one repeats once it runs dry), EXT PWR is one value, the clock is virtual and moves only when the flow
    /// waits, and everything the flow did to the echo and the tablet is logged in order: "arm", "run", then
    /// "settle 1" ("settle null" when the last read was nothing).
    /// </summary>
    private sealed class GroundPowerFake
    {
        public const long StartMs = 5_000_000;   // not zero: a deadline is a time on a clock, not a length

        private readonly Queue<double?> _reads;
        private double? _connection;

        public GroundPowerFake(params double?[] connectionReads) => _reads = new Queue<double?>(connectionReads);

        public double? ExtPwr;                   // what EXT PWR reads
        public (Bbs757EfbRunResult Result, bool MayHaveClicked) Answer = (Bbs757EfbRunResult.Done, true);   // the click sequence's answer
        public Exception? RunThrows;             // the run throws this instead of answering
        public bool CancelOnDelay;               // the first wait throws, as a window going away cancels it
        public long Now = StartMs;               // virtual ms
        public readonly List<string> Log = new();
        public readonly List<string> Reads = new();   // the keys read, in order
        public readonly List<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>> Runs = new();
        public readonly List<int> Delays = new();

        public Bbs757GroundPowerIo Io => new()
        {
            Read = (key, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Reads.Add(key);
                if (key == Bbs757GroundPower.ExtPwrOnKey) return Task.FromResult(ExtPwr);
                if (key != GpuKey) throw new InvalidOperationException($"The flow read {key}.");
                if (_reads.Count > 0) _connection = _reads.Dequeue();
                return Task.FromResult(_connection);
            },
            Run = (seq, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Log.Add("run");
                Runs.Add(seq);
                return RunThrows is { } ex
                    ? Task.FromException<(Bbs757EfbRunResult Result, bool MayHaveClicked)>(ex)
                    : Task.FromResult(Answer);
            },
            Delay = (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (CancelOnDelay) throw new OperationCanceledException();
                Delays.Add(ms);
                Now += ms;
                return Task.CompletedTask;
            },
            NowMs = () => Now,
            ArmEcho = () => Log.Add("arm"),
            SettleEcho = after => Log.Add("settle " + (after is double v ? v.ToString(CultureInfo.InvariantCulture) : "null")),
        };
    }

    [Fact]
    public async Task Connect_taps_once_and_reports()
    {
        var fake = new GroundPowerFake(0.0, 1.0);   // not connected; then the read-back finds it connected

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, GpuConnectedSaid), outcome);
        // The echo opens right before the one run and ends with the connection the read-back found.
        Assert.Equal(new[] { "arm", "run", "settle 1" }, fake.Log);
        Assert.Equal(Bbs757EfbLayout.ToggleGroundPower(), Assert.Single(fake.Runs));
        // The connection before the tap and again after it; a connect never looks at EXT PWR.
        Assert.Equal(new[] { GpuKey, GpuKey }, fake.Reads);
        Assert.Empty(fake.Delays);   // the read-back found the wish at once
    }

    [Fact]
    public async Task Connect_when_connected_taps_nothing()
    {
        var fake = new GroundPowerFake(1.0);   // connected already: the tile would disconnect it

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Already, GpuAlreadyConnectedSaid), outcome);
        Assert.Empty(fake.Log);    // no echo, no run, no settle
        Assert.Empty(fake.Runs);
        Assert.Equal(new[] { GpuKey }, fake.Reads);
    }

    [Fact]
    public async Task Disconnect_when_disconnected_taps_nothing()
    {
        var fake = new GroundPowerFake(0.0) { ExtPwr = 1.0 };   // disconnected already: the tile would connect it

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: false, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Already, GpuAlreadyDisconnectedSaid), outcome);
        Assert.Empty(fake.Log);
        Assert.Empty(fake.Runs);
        Assert.Equal(new[] { GpuKey }, fake.Reads);   // not connected, so EXT PWR is not asked
    }

    [Fact]
    public async Task Disconnect_refused_while_on_line()
    {
        var fake = new GroundPowerFake(1.0) { ExtPwr = 1.0 };

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: false, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Refused, GpuOnLineSaid), outcome);
        Assert.Empty(fake.Log);    // refused before any tap, and no echo opened
        Assert.Empty(fake.Runs);
        Assert.Equal(new[] { GpuKey, Bbs757GroundPower.ExtPwrOnKey }, fake.Reads);
    }

    [Fact]
    public async Task Disconnect_taps_once_and_reports()
    {
        var fake = new GroundPowerFake(1.0, 0.0) { ExtPwr = 0.0 };   // connected, EXT PWR off; then the read-back finds it gone

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: false, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, GpuDisconnectedSaid), outcome);
        Assert.Equal(new[] { "arm", "run", "settle 0" }, fake.Log);
        Assert.Equal(Bbs757EfbLayout.ToggleGroundPower(), Assert.Single(fake.Runs));
        // The connection, then EXT PWR (it is connected), both before the tap; then the read-back.
        Assert.Equal(new[] { GpuKey, Bbs757GroundPower.ExtPwrOnKey, GpuKey }, fake.Reads);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, double.NaN)]
    [InlineData(false, null)]
    [InlineData(false, double.NaN)]
    public async Task A_ground_power_that_cannot_be_read_taps_nothing_and_says_so(bool connect, double? reading)
    {
        var fake = new GroundPowerFake(reading) { ExtPwr = 1.0 };

        var outcome = await Bbs757GroundPower.SwitchAsync(connect, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Refused, GpuUnreadSaid), outcome);
        Assert.Empty(fake.Log);
        Assert.Empty(fake.Runs);
        Assert.Equal(new[] { GpuKey }, fake.Reads);   // nothing says which way a tap would go, so EXT PWR is not asked
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public async Task A_disconnect_with_external_power_unread_taps_nothing(double? extPwr)
    {
        var fake = new GroundPowerFake(1.0) { ExtPwr = extPwr };

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: false, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Refused, GpuExtPwrUnreadSaid), outcome);
        Assert.Empty(fake.Log);
        Assert.Empty(fake.Runs);
        Assert.Equal(new[] { GpuKey, Bbs757GroundPower.ExtPwrOnKey }, fake.Reads);
    }

    [Theory]
    [InlineData(1.0, "settle 1", GpuConnectedSaid)]        // the tile took, though the run never reported finishing
    [InlineData(0.0, "settle 0", GpuNotConnectedSaid)]     // it did not: said so after the whole read-back
    public async Task A_run_that_did_not_report_finishing_is_judged_from_the_variable_and_never_run_again(
        double after, string settle, string said)
    {
        var fake = new GroundPowerFake(0.0, after) { Answer = (Bbs757EfbRunResult.NotFinished, true) };   // it may have clicked

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, said), outcome);
        Assert.Single(fake.Runs);   // exactly one run: a second tap could undo the first
        Assert.Equal(new[] { "arm", "run", settle }, fake.Log);
    }

    [Theory]
    [InlineData(Bbs757EfbRunResult.Unreachable)]
    [InlineData(Bbs757EfbRunResult.LayoutUnknown)]
    [InlineData(Bbs757EfbRunResult.NotFinished)]
    public async Task A_run_that_tapped_nothing_is_a_run_problem_and_is_not_read_back(Bbs757EfbRunResult result)
    {
        var fake = new GroundPowerFake(0.0) { Answer = (result, false) };

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.RunProblem, null, result), outcome);
        Assert.Equal(new[] { "arm", "run", "settle null" }, fake.Log);   // the echo ends all the same
        Assert.Equal(new[] { GpuKey }, fake.Reads);                      // nothing was tapped: nothing to read back
        Assert.Empty(fake.Delays);
    }

    [Theory]
    [InlineData(true, 0.0, "settle 0", GpuNotConnectedSaid)]
    [InlineData(false, 1.0, "settle 1", GpuNotDisconnectedSaid)]
    public async Task BlueBird_never_switching_is_said_after_the_whole_read_back(
        bool connect, double unchanged, string settle, string said)
    {
        var fake = new GroundPowerFake(unchanged) { ExtPwr = 0.0 };

        var outcome = await Bbs757GroundPower.SwitchAsync(connect, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, said), outcome);
        // Read every 500 ms for 6 s: the flow waited out the deadline, not less and not more.
        Assert.Equal(Bbs757GroundPower.ReadBackMs, fake.Now - GroundPowerFake.StartMs);
        Assert.Equal(
            Enumerable.Repeat(Bbs757GroundPower.ReadBackPollMs, Bbs757GroundPower.ReadBackMs / Bbs757GroundPower.ReadBackPollMs),
            fake.Delays);
        Assert.Equal(new[] { "arm", "run", settle }, fake.Log);
    }

    [Fact]
    public async Task The_read_back_stops_at_the_first_read_of_the_wish()
    {
        var fake = new GroundPowerFake(0.0, 0.0, 0.0, 1.0);   // before the tap; then at once, 500 ms later, 1000 ms later

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, GpuConnectedSaid), outcome);
        Assert.Equal(new[] { 500, 500 }, fake.Delays);
        Assert.Equal(new[] { GpuKey, GpuKey, GpuKey, GpuKey }, fake.Reads);
        Assert.Equal(new[] { "arm", "run", "settle 1" }, fake.Log);
    }

    [Fact]
    public async Task A_read_back_that_could_not_read_yet_keeps_waiting()
    {
        var fake = new GroundPowerFake(0.0, null, double.NaN, 1.0);

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, GpuConnectedSaid), outcome);
        Assert.Equal(new[] { 500, 500 }, fake.Delays);
        Assert.Equal(new[] { "arm", "run", "settle 1" }, fake.Log);
    }

    [Theory]
    [InlineData(null, "settle null")]
    [InlineData(double.NaN, "settle NaN")]
    public async Task A_read_back_that_stays_unread_says_so_and_settles_with_what_it_read(double? unread, string settle)
    {
        var fake = new GroundPowerFake(0.0, unread);   // readable before the tap, unreadable after it

        var outcome = await Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default);

        Assert.Equal(new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, GpuUnreadNowSaid), outcome);
        Assert.Equal(new[] { "arm", "run", settle }, fake.Log);
        Assert.Equal(Bbs757GroundPower.ReadBackMs, fake.Now - GroundPowerFake.StartMs);   // waited on like a wrong reading
    }

    [Fact]
    public async Task A_canceled_run_propagates_and_the_echo_still_ends()
    {
        var fake = new GroundPowerFake(0.0) { RunThrows = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default));

        Assert.Equal(new[] { "arm", "run", "settle null" }, fake.Log);   // the echo never outlives the run
    }

    [Fact]
    public async Task A_cancel_during_the_read_back_ends_the_echo_with_the_last_connection_read()
    {
        var fake = new GroundPowerFake(0.0, 0.0) { CancelOnDelay = true };   // the window goes away while the flow waits

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Bbs757GroundPower.SwitchAsync(connect: true, fake.Io, default));

        Assert.Equal(new[] { "arm", "run", "settle 0" }, fake.Log);
    }

    [Fact]
    public void The_read_back_reads_every_500_ms_for_6_seconds_inside_the_echo_window()
    {
        Assert.Equal(6000, Bbs757GroundPower.ReadBackMs);
        Assert.Equal(500, Bbs757GroundPower.ReadBackPollMs);
        // The taps and the whole read-back fit the echo window the definition opens for the run: the row must not
        // start saying the window's own change before the window has.
        Assert.True(Bbs757EfbLayout.TotalMs(Bbs757EfbLayout.ToggleGroundPower()) + Bbs757GroundPower.ReadBackMs
            < Bbs757GroundPower.WindowRunMaxMs);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_ground_power_outcomes_do_not_follow_the_pcs_culture(string culture)
    {
        // Readings with a fraction (they read as 0 and 1) beside the plain ones: no decision and no word may change
        // with a decimal comma.
        static async Task<List<Bbs757GroundPowerOutcome>> All() => new()
        {
            await Bbs757GroundPower.SwitchAsync(true, new GroundPowerFake(0.3, 0.7).Io, default),                      // connected
            await Bbs757GroundPower.SwitchAsync(false, new GroundPowerFake(0.7, 0.3) { ExtPwr = 0.2 }.Io, default),   // disconnected
            await Bbs757GroundPower.SwitchAsync(true, new GroundPowerFake(0.3).Io, default),                           // never connected
            await Bbs757GroundPower.SwitchAsync(false, new GroundPowerFake(0.7) { ExtPwr = 0.7 }.Io, default),         // on line
        };

        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var expected = await All();
            Assert.Equal(new[] { GpuConnectedSaid, GpuDisconnectedSaid, GpuNotConnectedSaid, GpuOnLineSaid },
                expected.Select(o => o.Text));

            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(expected, await All());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- Nosewheel steering ----------------------------------------------------------------

    [Fact]
    public void Steering_to_rudder_is_home_settings_aircraft_then_the_nosewheel_steering_switch()
    {
        // Measured 2026-10-25 (live-findings.md, session 6): one click at (1154,249) on Settings >
        // AIRCRAFT switched NOSEWHEEL STEERING from REALISTIC (tiller) to RUDDER.
        var seq = Bbs757EfbLayout.SetSteeringToRudder();

        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.SettingsIcon, Bbs757EfbLayout.SettingsAircraft, Bbs757EfbLayout.NosewheelSteeringSwitch
        }, seq.Select(s => s.Point));
        Assert.Equal(new EfbPoint(98, 244), Bbs757EfbLayout.SettingsAircraft);
        Assert.Equal(new EfbPoint(1154, 249), Bbs757EfbLayout.NosewheelSteeringSwitch);
        Assert.Equal(new EfbPoint(1073, 190), Bbs757EfbLayout.SettingsIcon);
        // The switch toggles: one click, never two.
        Assert.Single(seq, s => s.Point == Bbs757EfbLayout.NosewheelSteeringSwitch);
        Assert.All(seq.Take(3), s => Assert.True(s.DelayAfterMs >= Bbs757EfbLayout.NavigateDelayMs));
    }

    [Theory]
    [InlineData("{\"tiller_realistic\":true}", true)]
    [InlineData("{\"tiller_realistic\":false}", false)]
    [InlineData("{\"hoppie_logon_code\":\"NOT-A-REAL-CODE\",\"tiller_realistic\":true,\"simbrief_id\":123456}", true)]
    public void ReadTillerRealistic_reads_only_the_steering_key(string json, bool expected)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Equal(expected, Bbs757WorkFolder.ReadTillerRealistic(tmp.Path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tiller_realistic\":1}")]
    [InlineData("{\"tiller_realistic\":\"true\"}")]
    [InlineData("{\"tiller_realistic\":null}")]
    [InlineData("[true]")]
    [InlineData("not json")]
    public void ReadTillerRealistic_is_null_for_anything_but_a_bool(string json)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Null(Bbs757WorkFolder.ReadTillerRealistic(tmp.Path));
    }

    [Fact]
    public void ReadTillerRealistic_with_no_settings_file_is_null()
    {
        using var tmp = new TempDir();

        Assert.Null(Bbs757WorkFolder.ReadTillerRealistic(tmp.Path));
    }

    [Fact]
    public void The_fixture_settings_file_reads_tiller_realistic_too()
    {
        using var tmp = new TempDir();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-efb-settings.json"), Path.Combine(tmp.Path, "efb_settings.json"));

        // The fixture is BlueBird's shape after the owner's switch to rudder (2026-10-25).
        Assert.False(Bbs757WorkFolder.ReadTillerRealistic(tmp.Path));
    }

    [Theory]
    [InlineData(true, true)]     // realistic (tiller): the one case the switch is clicked
    [InlineData(false, false)]   // already rudder: clicking would switch it BACK to the tiller
    [InlineData(null, false)]    // unreadable: never guess on a toggle
    public void The_steering_switch_is_clicked_only_while_it_reads_tiller(bool? tillerRealistic, bool click)
    {
        Assert.Equal(click, Bbs757EfbLayout.MayClickSteering(tillerRealistic));
    }

    [Theory]
    [InlineData(false, "Steering set to rudder")]
    [InlineData(true, "BlueBird did not change the steering. It is still the tiller.")]
    [InlineData(null, "The steering setting could not be read back.")]
    public void The_steering_result_is_said_from_the_setting_read_after(bool? after, string phrase)
    {
        Assert.Equal(phrase, Bbs757EfbLayout.SteeringResultPhrase(after));
    }

    [Fact]
    public void A_steering_already_on_rudder_says_so()
    {
        Assert.Equal("Steering is already rudder", Bbs757EfbLayout.SteeringAlreadyRudderText);
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
    [InlineData("Ready for taxi", 433, 369)]      // measured 2026-10-25
    [InlineData("Ready for takeoff", 433, 421)]   // measured 2026-10-25
    [InlineData("After takeoff", 818, 214)]
    [InlineData("Cruise", 818, 266)]
    [InlineData("Approach", 818, 319)]
    [InlineData("Final approach", 818, 372)]
    public void A_preset_is_home_aircraft_cockpit_presets_then_the_preset(string name, int x, int y)
    {
        var seq = Bbs757EfbLayout.LoadPreset(name);

        Assert.Equal(new[] { Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, Bbs757EfbLayout.AircraftMenuCockpitPresets, new EfbPoint(x, y) },
            seq.Select(s => s.Point));
        Assert.Contains(name, Bbs757EfbLayout.PresetNames);
    }

    [Fact]
    public void Cockpit_presets_is_the_aircraft_apps_first_menu_item()
    {
        // Measured 2026-10-25 (live-findings.md, "EFB Aircraft app keeps its last page"): the Aircraft
        // app reopens on the page it was left on, so a preset or quick start taps this item first.
        Assert.Equal(new EfbPoint(127, 178), Bbs757EfbLayout.AircraftMenuCockpitPresets);
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
    [InlineData("Engine quick start", 822, 524)]   // measured 2026-10-25
    [InlineData("IRS rapid align", 1209, 519)]     // measured 2026-10-25
    public void A_quick_start_is_home_aircraft_cockpit_presets_then_the_tile(string name, int x, int y)
    {
        Assert.Equal(new[] { Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, Bbs757EfbLayout.AircraftMenuCockpitPresets, new EfbPoint(x, y) },
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

    [Theory]
    [InlineData(Bbs757EfbRunKind.FetchOfp, true, true)]
    [InlineData(Bbs757EfbRunKind.FetchOfp, false, false)]   // the fetch did not finish: no dialog to answer
    [InlineData(Bbs757EfbRunKind.Units, true, false)]       // already answered
    [InlineData(Bbs757EfbRunKind.Preset, true, false)]      // another run since the fetch
    [InlineData(Bbs757EfbRunKind.SimBriefId, true, false)]
    [InlineData(null, false, false)]                        // nothing run yet
    public void The_units_question_is_answered_only_right_after_a_finished_fetch(Bbs757EfbRunKind? last, bool done, bool expected)
    {
        Assert.Equal(expected, Bbs757EfbLayout.MayAnswerUnits(last, done));
    }

    [Theory]
    [InlineData(null, 189L, Bbs757SimBriefEntry.Saved)]
    [InlineData(555L, 189L, Bbs757SimBriefEntry.Saved)]
    [InlineData(189L, 189L, Bbs757SimBriefEntry.Saved)]           // the same ID again
    [InlineData(null, null, Bbs757SimBriefEntry.NotSaved)]
    [InlineData(555L, 555L, Bbs757SimBriefEntry.NotSaved)]
    [InlineData(555L, 555189L, Bbs757SimBriefEntry.Different)]    // the keypad appended
    [InlineData(555L, null, Bbs757SimBriefEntry.Different)]       // cleared, not set
    [InlineData(null, 42L, Bbs757SimBriefEntry.Different)]
    public void The_simbrief_entry_is_judged_from_the_stored_id_before_and_after(long? before, long? after, Bbs757SimBriefEntry expected)
    {
        Assert.Equal(expected, Bbs757EfbLayout.JudgeSimBriefEntry(before, after, typed: 189));
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

    private static void WriteSettingsAt(string work, DateTime writtenUtc)
    {
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, "efb_settings.json");
        File.WriteAllText(path, "{\"simbrief_id\":-1}");
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    [Fact]
    public void Find_picks_the_root_whose_settings_were_written_last()
    {
        using var tmp = new TempDir();
        string older = tmp.Sub("older"), newer = tmp.Sub("newer"), bare = tmp.Sub("bare"), missing = tmp.Sub("missing");
        WriteSettingsAt(older, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        WriteSettingsAt(newer, new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));
        Directory.CreateDirectory(bare);   // a folder without BlueBird's settings file

        Assert.Equal(newer, Bbs757WorkFolder.Find(new[] { older, newer, bare }));
        Assert.Equal(newer, Bbs757WorkFolder.Find(new[] { newer, older }));
        Assert.Equal(newer, Bbs757WorkFolder.Find(new[] { missing, bare, older, newer }));
        Assert.Equal(older, Bbs757WorkFolder.Find(new[] { bare, older }));
    }

    [Fact]
    public void With_no_settings_file_anywhere_Find_takes_the_first_root_that_exists()
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
        Assert.False(client.IsLayoutUnknown);
        Assert.Equal(Bbs757EfbRunResult.Unreachable, await client.RunAsync(Bbs757EfbLayout.FetchOfp(), CancellationToken.None));
        Assert.Equal(Bbs757EfbRunResult.Unreachable, await client.RunAsync(Array.Empty<(EfbPoint, int)>(), CancellationToken.None));
        Assert.Null(await client.ReadPageAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("no-canvas: size 1200x900", Bbs757EfbRunResult.LayoutUnknown)]
    [InlineData("no-canvas: canvas", Bbs757EfbRunResult.Unreachable)]
    [InlineData("no-canvas: handlers", Bbs757EfbRunResult.Unreachable)]
    [InlineData("page-too-early", Bbs757EfbRunResult.PageTooEarly)]
    [InlineData("page-too-young", Bbs757EfbRunResult.PageTooYoung)]
    [InlineData("already-ran", Bbs757EfbRunResult.AlreadyRan)]
    [InlineData("other-page", Bbs757EfbRunResult.OtherPage)]
    [InlineData("busy", Bbs757EfbRunResult.NotFinished)]
    [InlineData("bad-seq", Bbs757EfbRunResult.NotFinished)]
    [InlineData("no-page-time", Bbs757EfbRunResult.NotFinished)]
    public void An_agent_refusal_maps_to_what_the_window_says(string error, Bbs757EfbRunResult expected)
    {
        Assert.Equal(expected, CoherentBbs757EfbClient.ResultForRefusal(error));
    }

    [Theory]
    [InlineData("MSFSBA_BBS757_EFB_NOT_READY:size 1200x900", true)]
    [InlineData("MSFSBA_BBS757_EFB_NOT_READY:canvas", false)]
    [InlineData("MSFSBA_BBS757_EFB_NOT_READY:Coherent", false)]
    [InlineData("no-canvas: size 1397x900", true)]
    public void Only_a_canvas_of_another_size_means_an_unknown_layout(string answer, bool expected)
    {
        Assert.Equal(expected, CoherentBbs757EfbClient.IsSizeProblem(answer));
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
        // The page-bound guards the nav data update relies on, and the page clock they use.
        Assert.Contains("notBeforeLoadMs", code);
        Assert.Contains("minAgeMs", code);
        Assert.Contains("onceKey", code);
        Assert.Contains("Date.now() - performance.now()", code);
        Assert.DoesNotContain("cancelled", agent);   // US spelling
    }

    [Fact]
    public void The_nav_data_update_runs_once_per_page_under_the_page_guards_on_the_page_it_judged()
    {
        long judged = new DateTimeOffset(U("2026-10-30 08:00:00")).ToUnixTimeMilliseconds();
        var guard = Bbs757EfbLayout.UpdateGuard(DateOnly.FromDateTime(U("2026-10-30 10:00:00")), judged);

        Assert.Equal("airac-update", guard.OnceKey);
        Assert.Equal(60000, guard.MinPageAgeMs);
        Assert.Equal(new DateTimeOffset(U("2026-10-30 00:00:00")).ToUnixTimeMilliseconds(), guard.NotBeforeLoadMs);
        // The click lands only on the page whose load time the window judged: a reload in between
        // (a new page) is refused by the agent as "other-page".
        Assert.Equal(judged, guard.SamePageAsMs);
    }

    [Fact]
    public void The_efb_agent_refuses_an_unguarded_click_near_the_update_spot()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");

        // The client asks for this agent version, the first that has the guard.
        Assert.Contains("var VERSION = " + CoherentBbs757EfbClient.AgentVersion.ToString(CultureInfo.InvariantCulture) + ";", code);
        Assert.Contains("'unguarded-update'", code);
        Assert.Contains("UPDATE_X = " + Bbs757EfbLayout.UpdateButton.X.ToString(CultureInfo.InvariantCulture), code);
        Assert.Contains("UPDATE_Y = " + Bbs757EfbLayout.UpdateButton.Y.ToString(CultureInfo.InvariantCulture), code);
        Assert.Contains("UPDATE_HALF_W = " + Bbs757EfbLayout.UpdateSpotHalfWidth.ToString(CultureInfo.InvariantCulture), code);
        Assert.Contains("UPDATE_HALF_H = " + Bbs757EfbLayout.UpdateSpotHalfHeight.ToString(CultureInfo.InvariantCulture), code);
        // All three page guards must be there for such a click.
        Assert.Matches(@"onceKey\s*!=\s*null\s*&&\s*opts\.notBeforeLoadMs\s*!=\s*null\s*&&\s*opts\.minAgeMs\s*!=\s*null", code);
        Assert.Equal(Bbs757EfbRunResult.NotFinished, CoherentBbs757EfbClient.ResultForRefusal("unguarded-update"));
    }

    [Fact]
    public void No_other_sequence_clicks_anywhere_near_the_update_spot()
    {
        var spot = Bbs757EfbLayout.UpdateButton;
        bool Near(EfbPoint p) => Math.Abs(p.X - spot.X) <= Bbs757EfbLayout.UpdateSpotHalfWidth
                                 && Math.Abs(p.Y - spot.Y) <= Bbs757EfbLayout.UpdateSpotHalfHeight;

        var update = Bbs757EfbLayout.UpdateNavData();
        Assert.Single(update, s => Near(s.Point));
        var others = EveryHomeSequence().Where(seq => !seq.Any(s => s.Point == spot)).ToList();
        others.Add(Bbs757EfbLayout.AnswerUnits(accept: true));
        others.Add(Bbs757EfbLayout.AnswerUnits(accept: false));
        Assert.True(others.Count >= 15);
        foreach (var seq in others)
            Assert.DoesNotContain(seq, s => Near(s.Point));
    }

    [Fact]
    public void The_new_cycle_s_first_day_refusal_says_after_midnight_utc()
    {
        string text = BBS757EfbForm.NewTodayText(2611);

        Assert.Contains("2611", text);
        Assert.Contains("after midnight UTC", text);
        Assert.DoesNotContain("tomorrow", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- weight units -------------------------------------------------------

    [Fact]
    public void ReadUnits_reads_the_units_field_from_a_settings_file_of_blueBirds_shape()
    {
        using var tmp = new TempDir();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-efb-settings.json"), Path.Combine(tmp.Path, "efb_settings.json"));

        Assert.Equal(Bbs757WeightUnits.Pounds, Bbs757WorkFolder.ReadUnits(tmp.Path));
        Assert.Equal(123456L, Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    [Theory]
    [InlineData("{\"units\":0}", Bbs757WeightUnits.Kilograms)]   // METRIC
    [InlineData("{\"units\":1}", Bbs757WeightUnits.Pounds)]      // IMPERIAL
    [InlineData("{\"hoppie_logon_code\":\"NOT-A-REAL-CODE\",\"units\":0,\"sayintentions_acars_key\":\"NOT-A-REAL-KEY\"}", Bbs757WeightUnits.Kilograms)]
    public void ReadUnits_maps_metric_to_kilograms_and_imperial_to_pounds(string json, Bbs757WeightUnits expected)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Equal(expected, Bbs757WorkFolder.ReadUnits(tmp.Path));
    }

    [Theory]
    [InlineData("{\"units\":2}")]
    [InlineData("{\"units\":-1}")]
    [InlineData("{\"units\":\"1\"}")]
    [InlineData("{\"units\":true}")]
    [InlineData("{\"units\":1.5}")]
    [InlineData("{\"units\":null}")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void ReadUnits_returns_null_for_anything_else(string json)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Null(Bbs757WorkFolder.ReadUnits(tmp.Path));
    }

    [Fact]
    public void ReadUnits_with_no_settings_file_is_null()
    {
        using var tmp = new TempDir();

        Assert.Null(Bbs757WorkFolder.ReadUnits(tmp.Path));
    }

    [Theory]
    [InlineData(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Pounds, "Weight units now pounds")]
    [InlineData(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Kilograms, "Weight units now kilograms")]
    [InlineData(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Pounds, "Weight units unchanged, pounds")]
    [InlineData(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Kilograms, "Weight units unchanged, kilograms")]
    [InlineData(null, Bbs757WeightUnits.Pounds, "Weight units are pounds")]
    [InlineData(Bbs757WeightUnits.Pounds, null, "The weight units could not be read")]
    [InlineData(null, null, "The weight units could not be read")]
    public void The_units_answer_says_which_units_are_set(Bbs757WeightUnits? before, Bbs757WeightUnits? after, string expected)
    {
        Assert.Equal(expected, Bbs757EfbLayout.UnitsAnswerPhrase(before, after));
    }

    [Fact]
    public void After_a_fetch_the_window_does_not_assume_the_pilot_saw_the_units_question()
    {
        string text = BBS757EfbForm.FetchedText(Bbs757WeightUnits.Pounds);

        Assert.DoesNotContain("If BlueBird asks", text);
        Assert.Contains("If the plan's weight units differ from the aircraft's", text);
        Assert.Contains("Accept units switches", text);
        Assert.Contains("Keep units keeps them", text);
        Assert.Contains("the app says which units are set", text);
        Assert.Contains("The aircraft uses pounds.", text);
        Assert.Contains("ACARS INIT FLT", text);
        Assert.DoesNotContain("The aircraft uses", BBS757EfbForm.FetchedText(null));
    }

    [Fact]
    public void After_a_fetch_in_other_units_the_window_says_the_question_is_up()
    {
        Assert.Equal("SimBrief flight requested. The plan is in kilograms and the aircraft in pounds, so BlueBird is asking " +
                     "whether to switch: Accept units switches, Keep units keeps them, and the app says which units are set. " +
                     "Then run ACARS INIT FLT in the CDU window.",
            BBS757EfbForm.FetchedText(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Kilograms));
    }

    [Fact]
    public void After_a_fetch_in_the_aircrafts_units_the_window_says_no_question_comes()
    {
        Assert.Equal("SimBrief flight requested. The plan and the aircraft both use pounds. Then run ACARS INIT FLT in the CDU window.",
            BBS757EfbForm.FetchedText(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Pounds));
    }

    // ---- the units question left open (the final review's important 3) ------------------

    [Fact]
    public void A_fetch_in_other_units_leaves_the_question_open_on_its_page_only()
    {
        var question = new Bbs757UnitsQuestion();
        question.AfterFetch(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Pounds, pageMs: 1000);

        Assert.True(question.MayBeOpen(1000));
        Assert.True(question.MayBeOpen(null));    // the page unread: it may still be up
        Assert.False(question.MayBeOpen(2000));   // the flight reloaded: a new page, without it
    }

    [Fact]
    public void Accept_or_keep_units_closes_the_question()
    {
        var question = new Bbs757UnitsQuestion();
        question.AfterFetch(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Pounds, pageMs: 1000);

        question.Answered();

        Assert.False(question.MayBeOpen(1000));
    }

    [Fact]
    public void A_fetch_in_matching_or_unread_units_raises_no_question()
    {
        var question = new Bbs757UnitsQuestion();
        question.AfterFetch(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Pounds, pageMs: 1000);
        Assert.False(question.MayBeOpen(1000));
        question.AfterFetch(null, Bbs757WeightUnits.Pounds, pageMs: 1000);
        Assert.False(question.MayBeOpen(1000));
        question.AfterFetch(Bbs757WeightUnits.Kilograms, null, pageMs: 1000);
        Assert.False(question.MayBeOpen(1000));
    }

    [Fact]
    public void A_later_fetch_in_matching_units_on_the_same_page_leaves_an_open_question_open()
    {
        // If the question is modal, the later fetch's presses may never have reached the tablet.
        var question = new Bbs757UnitsQuestion();
        question.AfterFetch(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Pounds, pageMs: 1000);

        question.AfterFetch(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Pounds, pageMs: 1000);

        Assert.True(question.MayBeOpen(1000));
    }

    [Fact]
    public void A_fetch_on_a_new_page_forgets_the_old_pages_question()
    {
        var question = new Bbs757UnitsQuestion();
        question.AfterFetch(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Pounds, pageMs: 1000);

        question.AfterFetch(Bbs757WeightUnits.Pounds, Bbs757WeightUnits.Pounds, pageMs: 2000);

        Assert.False(question.MayBeOpen(2000));
    }

    [Fact]
    public void A_question_raised_on_an_unread_page_blocks_every_page_until_answered()
    {
        var question = new Bbs757UnitsQuestion();
        question.AfterFetch(Bbs757WeightUnits.Kilograms, Bbs757WeightUnits.Pounds, pageMs: null);

        Assert.True(question.MayBeOpen(1000));
        question.Answered();
        Assert.False(question.MayBeOpen(1000));
    }

    // ---- cockpit presets: ground or flight -------------------------------------

    [Theory]
    [InlineData("Cold and dark")]
    [InlineData("Turnaround")]
    [InlineData("Before start")]
    [InlineData("Ready for taxi")]
    [InlineData("Ready for takeoff")]
    public void A_ground_preset_loads_only_on_the_ground(string preset)
    {
        Assert.True(Bbs757EfbLayout.IsGroundPreset(preset));
        Assert.Null(Bbs757EfbLayout.PresetRefusal(preset, onGround: true));
        Assert.Equal("That preset is for the ground.", Bbs757EfbLayout.PresetRefusal(preset, onGround: false));
    }

    [Theory]
    [InlineData("After takeoff")]
    [InlineData("Cruise")]
    [InlineData("Approach")]
    [InlineData("Final approach")]
    public void A_flight_preset_loads_only_airborne(string preset)
    {
        Assert.False(Bbs757EfbLayout.IsGroundPreset(preset));
        Assert.Null(Bbs757EfbLayout.PresetRefusal(preset, onGround: false));
        Assert.Equal("That preset is for flight.", Bbs757EfbLayout.PresetRefusal(preset, onGround: true));
    }

    [Fact]
    public void Every_preset_is_a_ground_or_a_flight_one()
    {
        Assert.Equal(5, Bbs757EfbLayout.PresetNames.Count(Bbs757EfbLayout.IsGroundPreset));
        Assert.Equal(4, Bbs757EfbLayout.PresetNames.Count(p => !Bbs757EfbLayout.IsGroundPreset(p)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbLayout.IsGroundPreset("Taxi"));
    }

    // ---- more than one work folder ------------------------------------------------

    [Fact]
    public void Work_folders_disagree_when_another_one_holds_the_current_cycle_or_newer()
    {
        using var tmp = new TempDir();
        string chosen = tmp.Sub("chosen"), other = tmp.Sub("other"), missing = tmp.Sub("missing");
        Directory.CreateDirectory(chosen);
        Directory.CreateDirectory(other);
        WriteCycle(chosen, "{\"cycle\":\"2603\"}");

        WriteCycle(other, "{\"cycle\":\"2610\"}");
        Assert.True(Bbs757WorkFolder.OthersDisagree(new[] { chosen, other, missing }, chosen, currentCycle: 2610));
        WriteCycle(other, "{\"cycle\":\"2611\"}");
        Assert.True(Bbs757WorkFolder.OthersDisagree(new[] { other, chosen }, chosen, currentCycle: 2610));
    }

    [Fact]
    public void Work_folders_disagree_when_another_one_holds_a_cycle_file_that_cannot_be_read()
    {
        using var tmp = new TempDir();
        string chosen = tmp.Sub("chosen"), other = tmp.Sub("other");
        Directory.CreateDirectory(chosen);
        WriteCycle(other, "not json");

        Assert.True(Bbs757WorkFolder.OthersDisagree(new[] { chosen, other }, chosen, currentCycle: 2610));
    }

    [Fact]
    public void Work_folders_agree_when_the_others_hold_an_older_cycle_or_none()
    {
        using var tmp = new TempDir();
        string chosen = tmp.Sub("chosen"), older = tmp.Sub("older"), bare = tmp.Sub("bare"), missing = tmp.Sub("missing");
        WriteCycle(chosen, "{\"cycle\":\"2603\"}");
        WriteCycle(older, "{\"cycle\":\"2609\"}");
        Directory.CreateDirectory(bare);

        Assert.False(Bbs757WorkFolder.OthersDisagree(new[] { chosen, older, bare, missing }, chosen, currentCycle: 2610));
        Assert.False(Bbs757WorkFolder.OthersDisagree(new[] { chosen }, chosen, currentCycle: 2610));
        Assert.False(Bbs757WorkFolder.OthersDisagree(Array.Empty<string>(), chosen, currentCycle: 2610));
    }

    [Fact]
    public void The_chosen_folder_itself_never_counts_as_another()
    {
        using var tmp = new TempDir();
        string chosen = tmp.Sub("chosen");
        WriteCycle(chosen, "{\"cycle\":\"2610\"}");

        // Listed twice, and with another spelling of the same path.
        Assert.False(Bbs757WorkFolder.OthersDisagree(new[] { chosen, chosen + Path.DirectorySeparatorChar, chosen.ToUpperInvariant() }, chosen, currentCycle: 2610));
    }

    // ---- the Navigraph code is spoken before the browser opens -----------------

    [Theory]
    [InlineData("A", 2000)]                           // never shorter than two seconds
    [InlineData("ABCD", 2900)]                        // 1.5 s, then 350 ms a spoken character
    [InlineData("ABCD-1234", 4650)]                   // the dash is a word too
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 6000)]
    public void The_browser_waits_for_the_code_to_be_spoken(string code, int expectedMs)
    {
        Assert.Equal(expectedMs, BBS757EfbForm.CodeSpeechMs(code));
    }
}
