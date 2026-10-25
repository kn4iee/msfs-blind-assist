using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's load watch (task 11e): after the EFB window taps IMPORT FROM SIMBRIEF and START BOARDING,
/// BlueBird fuels in about two minutes and boards at its realistic pace (under 40 minutes, measured
/// 2026-10-25), and the watch, reading every 15 s, says once each when the fuel and the boarding are
/// done; after an hour it says what has not finished. The judgement is pure
/// (<see cref="Bbs757LoadWatch"/>); the definition owns the timing loop.
/// </summary>
public class Bbs757LoadWatchTests
{
    // The evening's plan (live-findings.md, task 11e step 0): a 16,400 lb block, 114 passengers at
    // 185 lb, 3,990 lb of cargo, all forward.
    private static readonly Bbs757Loads Plan = new(8200, 8200, 0, 21090, 3990, 0, 114, 114);

    private const string Fueled = "Fueling complete. Fuel 16400 pounds: left 8200, right 8200, center 0.";

    private const string Boarded =
        "Boarding complete. Passengers 21090 pounds. Cargo 3990 pounds: forward 3990, aft 0. " +
        "For the ACARS weight and balance, request WGTBAL on the CDU.";

    /// <summary>A read in pounds (one pound per "gallon"), each wing holding <paramref name="wing"/>.</summary>
    private static Bbs757LoadReadBack Read(double wing, double center = 0, double first = 0, double economy = 0,
        double forward = 0, double aft = 0) => new(wing, wing, center, 1, first, economy, forward, aft);

    /// <summary>The plan's fuel, passengers and cargo, as BlueBird left them once done.</summary>
    private static Bbs757LoadReadBack Loaded => Read(8200, first: 1850, economy: 19240, forward: 3990);

    /// <summary>The plan's fuel aboard and the cabin and holds as <paramref name="first"/> to <paramref name="aft"/>.</summary>
    private static Bbs757LoadReadBack Boarding(double first, double economy, double forward = 0, double aft = 0)
        => Read(8200, first: first, economy: economy, forward: forward, aft: aft);

    /// <summary>A watch whose fuel is said already, so a test hears the boarding alone.</summary>
    private static Bbs757LoadWatch FueledWatch(Bbs757Loads? plan = null, Bbs757WeightUnits? units = Bbs757WeightUnits.Pounds)
    {
        var watch = new Bbs757LoadWatch(plan ?? Plan, units);
        var full = Read(((plan ?? Plan).LeftLbs + (plan ?? Plan).RightLbs) / 2);
        watch.Observe(full, 0);
        Assert.StartsWith("Fueling complete.", watch.Observe(full, 15_000));
        return watch;
    }

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

    // ---- the fuel ---------------------------------------------------------------------------

    [Fact]
    public void Fueling_complete_is_said_once_the_fuel_reaches_the_plan_and_holds_still()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);

        Assert.Null(watch.Observe(Read(6554.5), 0));       // 13,109 lb at the start: seeds the watch
        Assert.Null(watch.Observe(Read(7500), 15_000));     // fueling
        Assert.Null(watch.Observe(Read(8173), 30_000));     // 16,346 lb, within 1% of the plan, but 1,346 lb on
        Assert.Equal("Fueling complete. Fuel 16340 pounds: left 8170, right 8170, center 0.",
            watch.Observe(Read(8172), 45_000));              // 2 lb less: the APU burns, so never "unchanged"
        Assert.True(watch.FuelDone);
        Assert.False(watch.Finished);
        Assert.Null(watch.Observe(Read(8171), 60_000));     // said once
    }

    [Fact]
    public void The_first_read_never_completes_even_at_the_plan()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);

        Assert.Null(watch.Observe(Read(8200), 0));
        Assert.Equal(Fueled, watch.Observe(Read(8200), 15_000));
    }

    [Fact]
    public void Fuel_still_moving_twenty_pounds_a_read_is_not_complete()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        watch.Observe(Read(8175), 0);

        Assert.Null(watch.Observe(Read(8185), 15_000));     // 20 lb more: still fueling
        Assert.StartsWith("Fueling complete.", watch.Observe(Read(8185), 30_000));
    }

    [Fact]
    public void Fuel_that_stops_short_of_the_plan_is_not_complete()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        watch.Observe(Read(8000), 0);

        // 16,000 lb is 400 short: more than the larger of 1% (164) and 100 lb.
        Assert.Null(watch.Observe(Read(8000), 15_000));
        Assert.False(watch.FuelDone);
    }

    [Theory]
    [InlineData(16236, true)]    // the plan less 1% (164 lb)
    [InlineData(16234, false)]
    [InlineData(16564, true)]    // over as well as under
    [InlineData(16566, false)]
    public void The_fuel_counts_within_one_percent_of_the_plan_or_100_pounds(double total, bool complete)
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        watch.Observe(Read(total / 2), 0);

        Assert.Equal(complete, watch.Observe(Read(total / 2), 15_000) != null);
    }

    [Fact]
    public void A_small_block_counts_within_100_pounds()
    {
        var small = new Bbs757Loads(2000, 2000, 0, 0, 0, 0, 0, 0);   // 1% of 4,000 is 40: the 100 lb floor
        var watch = new Bbs757LoadWatch(small, Bbs757WeightUnits.Pounds);
        watch.Observe(Read(1950), 0);

        Assert.StartsWith("Fueling complete.", watch.Observe(Read(1950), 15_000));
    }

    // ---- the boarding -----------------------------------------------------------------------

    [Fact]
    public void Boarding_complete_is_said_once_the_cabin_and_the_cargo_reach_the_plan_and_hold_still()
    {
        var watch = FueledWatch();

        Assert.Null(watch.Observe(Boarding(1850, 9000), 30_000));   // boarding
        Assert.Null(watch.Observe(Loaded, 45_000));                 // all aboard, just now
        Assert.Equal(Boarded, watch.Observe(Loaded, 60_000));
        Assert.True(watch.BoardingDone);
        Assert.True(watch.Finished);
        Assert.Null(watch.Observe(Loaded, 75_000));
    }

    [Fact]
    public void The_cargo_is_judged_as_a_total_whichever_hold_BlueBird_fills()
    {
        var watch = FueledWatch();
        watch.Observe(Boarding(1850, 19240, forward: 2000, aft: 1990), 30_000);

        Assert.Equal("Boarding complete. Passengers 21090 pounds. Cargo 3990 pounds: forward 2000, aft 1990. " +
                     "For the ACARS weight and balance, request WGTBAL on the CDU.",
            watch.Observe(Boarding(1850, 19240, forward: 2000, aft: 1990), 45_000));
    }

    [Fact]
    public void A_cabin_one_passenger_short_is_not_complete()
    {
        // One passenger (185 lb) is more than the cabin's tolerance (0.5% of 21,090: 105 lb).
        var watch = FueledWatch();
        watch.Observe(Boarding(1850, 19055, forward: 3990), 30_000);

        Assert.Null(watch.Observe(Boarding(1850, 19055, forward: 3990), 45_000));
        Assert.False(watch.BoardingDone);
    }

    [Fact]
    public void Cargo_still_loading_is_not_complete()
    {
        var watch = FueledWatch();
        watch.Observe(Boarding(1850, 19240, forward: 3975), 30_000);   // within 20 lb, still moving

        Assert.Null(watch.Observe(Boarding(1850, 19240, forward: 3980), 45_000));
        Assert.Equal(Boarded.Replace("Cargo 3990 pounds: forward 3990", "Cargo 3980 pounds: forward 3980"),
            watch.Observe(Boarding(1850, 19240, forward: 3980), 60_000));
    }

    [Fact]
    public void Both_done_at_one_read_are_said_together_the_fuel_first()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        watch.Observe(Loaded, 0);

        Assert.Equal(Fueled + " " + Boarded, watch.Observe(Loaded, 15_000));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void A_plan_with_more_passengers_than_seats_says_the_cap_when_boarded()
    {
        var capped = new Bbs757Loads(12775, 12775, 0, 30800, 10300, 10359, 224, 176);
        var watch = FueledWatch(capped);
        var aboard = Read(12775, first: 2800, economy: 28000, forward: 10300, aft: 10359);
        watch.Observe(aboard, 30_000);

        Assert.Equal("Boarding complete. Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360. " +
                     "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds. " +
                     "For the ACARS weight and balance, request WGTBAL on the CDU.",
            watch.Observe(aboard, 45_000));
    }

    // ---- what cannot be read, and the hour ---------------------------------------------------

    [Fact]
    public void An_unread_value_neither_completes_nor_counts_as_the_previous_read()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        watch.Observe(Read(8200), 0);

        Assert.Null(watch.Observe(Read(8200) with { PoundsPerGallon = null }, 15_000));
        Assert.Equal(Fueled, watch.Observe(Read(8200), 30_000));   // judged against the read at 0
    }

    [Fact]
    public void Boarding_unfinished_after_an_hour_says_so_once_and_the_watch_ends()
    {
        var watch = FueledWatch();
        var halfway = Boarding(1850, 5000);

        Assert.Null(watch.Observe(halfway, Bbs757LoadWatch.TimeoutMs - 1));
        Assert.Equal("Boarding has not finished after an hour. Check the weight with W.",
            watch.Observe(halfway, Bbs757LoadWatch.TimeoutMs));
        Assert.True(watch.Finished);
        Assert.Null(watch.Observe(Loaded, Bbs757LoadWatch.TimeoutMs + 15_000));
        Assert.Null(watch.Observe(Loaded, Bbs757LoadWatch.TimeoutMs + 30_000));
    }

    [Fact]
    public void Fuel_and_boarding_unfinished_after_an_hour_say_both()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        watch.Observe(Read(5000), 0);

        Assert.Equal("Fueling and boarding have not finished after an hour. Check the fuel with F and the weight with W.",
            watch.Observe(Read(5000), Bbs757LoadWatch.TimeoutMs));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void Fuel_unfinished_after_an_hour_with_the_boarding_done_says_the_fuel()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
        var shortOfFuel = Loaded with { LeftGallons = 5000, RightGallons = 5000 };
        watch.Observe(shortOfFuel, 0);
        Assert.Equal(Boarded, watch.Observe(shortOfFuel, 15_000));

        Assert.Equal("Fueling has not finished after an hour. Check the fuel with F.",
            watch.Observe(shortOfFuel, Bbs757LoadWatch.TimeoutMs));
    }

    [Fact]
    public void The_hour_is_one_hour_and_the_reads_fifteen_seconds_apart()
    {
        Assert.Equal(60 * 60_000, Bbs757LoadWatch.TimeoutMs);
        Assert.Equal(15_000, Bbs757LoadWatch.IntervalMs);
    }

    // ---- units and culture ------------------------------------------------------------------

    [Fact]
    public void A_kilogram_aircraft_hears_kilograms()
    {
        var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Kilograms);
        watch.Observe(Read(8200), 0);

        Assert.Equal("Fueling complete. Fuel 7440 kilograms: left 3720, right 3720, center 0.", watch.Observe(Read(8200), 15_000));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_announcements_are_the_same_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var watch = new Bbs757LoadWatch(Plan, Bbs757WeightUnits.Pounds);
            watch.Observe(Loaded, 0);
            Assert.Equal(Fueled + " " + Boarded, watch.Observe(Loaded, 15_000));
        });
    }

    // ---- the definition's watch -------------------------------------------------------------

    /// <summary>The eight load variables behind the definition's reads, and a clock its waits move on.</summary>
    private sealed class LoadSim
    {
        public long Now;
        public readonly Dictionary<string, double?> Values = new(StringComparer.Ordinal);
        public readonly List<int> Waits = new();
        public bool WaitForever;

        public void Set(Bbs757LoadReadBack r)
        {
            Values[Bbs757Loadsheet.LeftFuelKey] = r.LeftGallons;
            Values[Bbs757Loadsheet.RightFuelKey] = r.RightGallons;
            Values[Bbs757Loadsheet.CenterFuelKey] = r.CenterGallons;
            Values[Bbs757Loadsheet.FuelDensityKey] = r.PoundsPerGallon;
            Values[Bbs757Loadsheet.FirstClassKey] = r.FirstClassLbs;
            Values[Bbs757Loadsheet.EconomyKey] = r.EconomyLbs;
            Values[Bbs757Loadsheet.ForwardHoldKey] = r.ForwardHoldLbs;
            Values[Bbs757Loadsheet.AftHoldKey] = r.AftHoldLbs;
        }

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => true,
            ReadFresh = (key, ct) => Task.FromResult(Values.TryGetValue(key, out double? v) ? v : null),
            Cached = _ => null,
            Write = _ => { },
            RequestRead = _ => { },
            Delay = (ms, ct) =>
            {
                Waits.Add(ms);
                if (WaitForever) return Task.Delay(Timeout.Infinite, ct);
                ct.ThrowIfCancellationRequested();
                Now += ms;
                return Task.CompletedTask;
            },
        };
    }

    private static (BBS757Definition Def, SpeechCapture Speech) Watching(LoadSim sim)
        => (new BBS757Definition { NestRunning = () => true, NowMs = () => sim.Now, WriteIo = _ => sim.Io }, new SpeechCapture());

    [Fact]
    public void The_definition_says_each_completion_once_as_a_background_announcement()
    {
        var sim = new LoadSim();
        sim.Set(Loaded);
        var (def, speech) = Watching(sim);

        Assert.True(def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech));

        Assert.Equal(new[] { Fueled + " " + Boarded }, speech.All);
        Assert.Empty(speech.Interrupts);            // never cuts the pilot off
        Assert.False(def.LoadWatchRunning);         // both said: the watch is over
        Assert.Equal(new[] { Bbs757LoadWatch.IntervalMs }, sim.Waits);
    }

    [Fact]
    public void A_load_that_never_finishes_is_given_up_after_an_hour_of_reads_fifteen_seconds_apart()
    {
        var sim = new LoadSim();
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);

        def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "Fueling and boarding have not finished after an hour. Check the fuel with F and the weight with W." },
            speech.All);
        Assert.Equal(240, sim.Waits.Count);
        Assert.All(sim.Waits, w => Assert.Equal(15_000, w));
        Assert.False(def.LoadWatchRunning);
    }

    [Fact]
    public void A_second_load_while_the_watch_runs_is_refused()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);

        Assert.True(def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech));
        Assert.True(def.LoadWatchRunning);
        Assert.False(def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech));

        def.OnSwitchedAway();
    }

    [Fact]
    public void A_flight_load_or_a_lost_connection_ends_the_watch_silently()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);
        def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech);

        def.OnSimContextReset();

        Assert.False(def.LoadWatchRunning);
        Assert.Empty(speech.All);
        Assert.True(def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech));   // a new load may start
        def.OnSwitchedAway();
    }

    [Fact]
    public void A_profile_switch_ends_the_watch_and_none_starts_after_it()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);
        def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech);

        def.OnSwitchedAway();

        Assert.False(def.LoadWatchRunning);
        Assert.False(def.StartLoadWatch(Plan, Bbs757WeightUnits.Pounds, new SimConnectManager(IntPtr.Zero), speech));
        Assert.Empty(speech.All);
    }
}
