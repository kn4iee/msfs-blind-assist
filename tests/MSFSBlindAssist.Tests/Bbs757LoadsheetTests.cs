using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's fuel and payload load from SimBrief, which spreads the plan the way BlueBird's own EFB
/// does (measured 2026-10-25, docs/b757/evidence/live-findings.md, session 5): the plan's fields,
/// the whole-pound loads per tank, cabin and hold, the one calculator string, whether the read-back
/// shows BlueBird took them, and what is said. Whether BlueBird takes the setters is checked in the sim.
/// </summary>
public class Bbs757LoadsheetTests
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

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-simbrief-ofp.xml"));

    /// <summary>An OFP with only the fields the load reads; a null value leaves the element out.</summary>
    private static string Ofp(string? units = "lbs", string? ramp = "25550", string? paxActual = "224",
        string? pax = "224", string? paxWeight = "175", string? payload = "51459", string? cargo = "12259",
        string? airline = "QQX", string? flight = "1234", string? origin = "KSEA", string? destination = "KDEN",
        string? aircraft = "B752")
    {
        static string El(string name, string? value) => value == null ? "" : $"<{name}>{value}</{name}>";
        return "<OFP>" +
               $"<params>{El("units", units)}</params>" +
               $"<general>{El("icao_airline", airline)}{El("flight_number", flight)}</general>" +
               $"<origin>{El("icao_code", origin)}</origin><destination>{El("icao_code", destination)}</destination>" +
               $"<aircraft>{El("icaocode", aircraft)}</aircraft>" +
               $"<fuel>{El("plan_ramp", ramp)}</fuel>" +
               $"<weights>{El("pax_count", pax)}{El("pax_count_actual", paxActual)}{El("pax_weight", paxWeight)}" +
               $"{El("cargo", cargo)}{El("payload", payload)}</weights>" +
               "</OFP>";
    }

    private static Bbs757SimBriefPlan Plan(double block = 25550, int pax = 224, double paxWeight = 175, double payload = 51459,
        string aircraft = "B752")
        => new("QQX 1234", "KSEA", "KDEN", aircraft, block, pax, paxWeight, payload);

    private static Bbs757Loads Loads(Bbs757SimBriefPlan plan)
    {
        var (loads, refusal) = Bbs757Loadsheet.Compute(plan);
        Assert.Null(refusal);
        Assert.NotNull(loads);
        return loads!;
    }

    /// <summary>A read-back that holds exactly what was written, at the sim's 6.7 pounds per gallon.</summary>
    private static Bbs757LoadReadBack ReadBackOf(Bbs757Loads loads, double firstShare = 2800.0 / 30800.0) => new(
        LeftGallons: loads.LeftLbs / 6.7, RightGallons: loads.RightLbs / 6.7, CenterGallons: loads.CenterLbs / 6.7,
        PoundsPerGallon: 6.7,
        FirstClassLbs: loads.PassengerLbs * firstShare, EconomyLbs: loads.PassengerLbs * (1 - firstShare),
        ForwardHoldLbs: loads.ForwardLbs, AftHoldLbs: loads.AftLbs);

    /// <summary>A read-back in pounds (one pound per "gallon"), so tolerance edges are exact.</summary>
    private static Bbs757LoadReadBack Pounds(double left, double right, double center, double cabin, double forward, double aft)
        => new(left, right, center, 1, cabin, 0, forward, aft);

    /// <summary>An empty aircraft: what the tanks and stations held before a load.</summary>
    private static readonly Bbs757LoadReadBack Empty = new(0, 0, 0, 6.7, 0, 0, 0, 0);

    // ---- the plan ---------------------------------------------------------------------------

    [Fact]
    public void The_fixture_reads_as_the_measured_plan()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Fixture());

        Assert.NotNull(plan);
        Assert.Equal("QQX 1234", plan!.Flight);
        Assert.Equal("KSEA", plan.Origin);
        Assert.Equal("KDEN", plan.Destination);
        Assert.Equal("B752", plan.AircraftIcao);
        Assert.Equal(25550, plan.BlockFuelLbs);
        Assert.Equal(224, plan.Passengers);
        Assert.Equal(175, plan.PassengerWeightLbs);
        Assert.Equal(51459, plan.PayloadLbs);
    }

    [Fact]
    public void A_kilogram_plan_is_read_in_pounds()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589", paxWeight: "84", payload: "23341"));

        Assert.NotNull(plan);
        Assert.Equal(11589 * 2.20462, plan!.BlockFuelLbs, 6);
        Assert.Equal(84 * 2.20462, plan.PassengerWeightLbs, 6);
        Assert.Equal(23341 * 2.20462, plan.PayloadLbs, 6);
        Assert.Equal(224, plan.Passengers);   // a count, not a weight
    }

    [Fact]
    public void The_units_are_read_whatever_their_case()
    {
        Assert.Equal(25550, Bbs757Loadsheet.ParsePlan(Ofp(units: " LBS "))!.BlockFuelLbs);
        Assert.Equal(1000 * 2.20462, Bbs757Loadsheet.ParsePlan(Ofp(units: "KGS", ramp: "1000"))!.BlockFuelLbs, 6);
    }

    [Fact]
    public void The_actual_passenger_count_is_preferred_and_the_planned_one_stands_in()
    {
        Assert.Equal(200, Bbs757Loadsheet.ParsePlan(Ofp(paxActual: "200", pax: "224"))!.Passengers);
        Assert.Equal(224, Bbs757Loadsheet.ParsePlan(Ofp(paxActual: null, pax: "224"))!.Passengers);
    }

    [Fact]
    public void A_plan_with_no_payload_takes_the_passengers_and_cargo()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Ofp(payload: null, cargo: "12259"));

        Assert.Equal(224 * 175 + 12259, plan!.PayloadLbs);
    }

    [Theory]
    [InlineData("units")]
    [InlineData("ramp")]
    [InlineData("pax")]
    [InlineData("paxWeight")]
    [InlineData("payloadAndCargo")]
    public void A_plan_missing_a_field_it_needs_reads_as_null(string missing)
    {
        string xml = missing switch
        {
            "units" => Ofp(units: null),
            "ramp" => Ofp(ramp: null),
            "pax" => Ofp(paxActual: null, pax: null),
            "paxWeight" => Ofp(paxWeight: null),
            _ => Ofp(payload: null, cargo: null),
        };

        Assert.Null(Bbs757Loadsheet.ParsePlan(xml));
    }

    [Theory]
    [InlineData("tons", "25550", "224")]    // units neither pounds nor kilograms: never guessed
    [InlineData("lbs", "-1", "224")]        // a negative block
    [InlineData("lbs", "abc", "224")]       // not a number
    [InlineData("lbs", "NaN", "224")]
    [InlineData("lbs", "25550", "22.5")]    // a passenger count that is not whole
    [InlineData("lbs", "25550", "-3")]
    public void A_plan_with_a_value_it_cannot_use_reads_as_null(string units, string ramp, string pax)
    {
        Assert.Null(Bbs757Loadsheet.ParsePlan(Ofp(units: units, ramp: ramp, paxActual: pax)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<html><body>Error</body></html>")]
    [InlineData("<OFP><fetch><status>Error: Unknown UserID</status></fetch></OFP>")]
    public void What_is_not_a_plan_reads_as_null(string xml)
    {
        Assert.Null(Bbs757Loadsheet.ParsePlan(xml));
    }

    [Fact]
    public void Null_reads_as_null()
    {
        Assert.Null(Bbs757Loadsheet.ParsePlan(null));
    }

    [Fact]
    public void The_flight_is_the_airline_and_number_and_either_may_be_missing()
    {
        Assert.Equal("1234", Bbs757Loadsheet.ParsePlan(Ofp(airline: null))!.Flight);
        Assert.Equal("QQX", Bbs757Loadsheet.ParsePlan(Ofp(flight: null))!.Flight);
        Assert.Equal("", Bbs757Loadsheet.ParsePlan(Ofp(airline: null, flight: null))!.Flight);
        Assert.Equal("", Bbs757Loadsheet.ParsePlan(Ofp(origin: null))!.Origin);
    }

    [Fact]
    public void A_plan_with_no_aircraft_type_reads_with_an_empty_one()
    {
        Assert.Equal("", Bbs757Loadsheet.ParsePlan(Ofp(aircraft: null))!.AircraftIcao);
    }

    [Fact]
    public void Numbers_read_the_same_under_a_comma_decimal_culture()
    {
        InCulture("de-DE", () =>
        {
            var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589.5"));
            Assert.Equal(11589.5 * 2.20462, plan!.BlockFuelLbs, 6);
        });
    }

    // ---- the aircraft -----------------------------------------------------------------------

    [Fact]
    public void A_plan_for_another_aircraft_is_refused()
    {
        var (loads, refusal) = Bbs757Loadsheet.Compute(Plan(aircraft: "A320"));

        Assert.Null(loads);
        Assert.Equal("Your latest SimBrief plan is for A320, not a 757-200. Create a B752 plan, then try again.", refusal);
    }

    [Fact]
    public void A_757_300_plan_is_refused_too()
    {
        Assert.Equal("Your latest SimBrief plan is for B753, not a 757-200. Create a B752 plan, then try again.",
            Bbs757Loadsheet.Compute(Plan(aircraft: "B753")).Refusal);
    }

    [Fact]
    public void A_plan_with_no_aircraft_type_is_refused()
    {
        Assert.Equal("Your latest SimBrief plan names no aircraft type. Create a B752 plan, then try again.",
            Bbs757Loadsheet.Compute(Plan(aircraft: "")).Refusal);
    }

    [Fact]
    public void The_aircraft_type_is_matched_whatever_its_case()
    {
        Assert.Null(Bbs757Loadsheet.Compute(Plan(aircraft: "b752")).Refusal);
    }

    [Fact]
    public void A_wrong_aircraft_is_refused_before_the_fuel_is_judged()
    {
        Assert.StartsWith("Your latest SimBrief plan is for A388", Bbs757Loadsheet.Compute(Plan(block: 500000, aircraft: "A388")).Refusal);
    }

    // ---- the loads --------------------------------------------------------------------------

    [Fact]
    public void The_measured_plan_spreads_as_BlueBird_spread_it()
    {
        // BlueBird's EFB on 2026-10-25: both wings evenly with the center empty, 176 seats at 175 lb,
        // cargo = payload - passenger weight, half to each hold with the forward one capped.
        var loads = Loads(Plan());

        Assert.Equal(12775, loads.LeftLbs);
        Assert.Equal(12775, loads.RightLbs);
        Assert.Equal(0, loads.CenterLbs);
        Assert.Equal(30800, loads.PassengerLbs);
        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(10359, loads.AftLbs);
        Assert.Equal(224, loads.PlannedPassengers);
        Assert.Equal(176, loads.SeatedPassengers);
        Assert.True(loads.PassengersCapped);
    }

    [Fact]
    public void A_block_past_the_wings_fills_them_and_puts_the_rest_in_the_center()
    {
        var loads = Loads(Plan(block: 40000));

        Assert.Equal(14579, loads.LeftLbs);
        Assert.Equal(14579, loads.RightLbs);
        Assert.Equal(10842, loads.CenterLbs);
    }

    [Fact]
    public void A_block_that_fills_every_tank_exactly_loads()
    {
        var loads = Loads(Plan(block: 2 * 14579 + 46390));

        Assert.Equal(46390, loads.CenterLbs);
    }

    [Fact]
    public void Fuel_past_the_tanks_is_refused()
    {
        var (loads, refusal) = Bbs757Loadsheet.Compute(Plan(block: 2 * 14579 + 46390 + 1));

        Assert.Null(loads);
        Assert.Equal("The plan's fuel is more than the tanks hold.", refusal);
    }

    [Fact]
    public void Cargo_past_the_holds_is_refused()
    {
        // 176 x 175 = 30800 in the cabin; 25001 left for holds that take 10300 + 14700.
        var (loads, refusal) = Bbs757Loadsheet.Compute(Plan(payload: 30800 + 25001));

        Assert.Null(loads);
        Assert.Equal("The plan's cargo is more than the holds take.", refusal);
    }

    [Fact]
    public void Cargo_that_fills_both_holds_exactly_loads()
    {
        var loads = Loads(Plan(payload: 30800 + 25000));

        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(14700, loads.AftLbs);
    }

    [Fact]
    public void A_payload_no_heavier_than_the_passengers_leaves_the_holds_empty()
    {
        var loads = Loads(Plan(pax: 150, payload: 20000));   // 150 x 175 = 26250, more than the payload

        Assert.Equal(26250, loads.PassengerLbs);
        Assert.Equal(0, loads.ForwardLbs);
        Assert.Equal(0, loads.AftLbs);
    }

    [Fact]
    public void Fewer_passengers_than_seats_all_sit_and_the_cargo_splits_evenly()
    {
        var loads = Loads(Plan(pax: 150, payload: 40000));

        Assert.Equal(150, loads.SeatedPassengers);
        Assert.False(loads.PassengersCapped);
        Assert.Equal(26250, loads.PassengerLbs);
        Assert.Equal(6875, loads.ForwardLbs);
        Assert.Equal(6875, loads.AftLbs);
    }

    [Fact]
    public void An_empty_plan_loads_nothing()
    {
        var loads = Loads(Plan(block: 0, pax: 0, payload: 0));

        Assert.Equal(0, loads.LeftLbs + loads.RightLbs + loads.CenterLbs + loads.PassengerLbs + loads.ForwardLbs + loads.AftLbs);
    }

    private static bool IsWhole(double v) => v == Math.Floor(v);

    [Fact]
    public void An_odd_block_writes_whole_pounds_with_the_odd_one_in_the_center()
    {
        var loads = Loads(Plan(block: 25551, payload: 51460));

        Assert.Equal(12775, loads.LeftLbs);
        Assert.Equal(12775, loads.RightLbs);
        Assert.Equal(1, loads.CenterLbs);
        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(10360, loads.AftLbs);
    }

    [Fact]
    public void A_kilogram_plan_writes_whole_pounds_that_add_up_to_its_rounded_totals()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589", paxActual: "150", paxWeight: "84", payload: "18341"))!;
        var loads = Loads(plan);

        Assert.All(new[] { loads.LeftLbs, loads.RightLbs, loads.CenterLbs, loads.PassengerLbs, loads.ForwardLbs, loads.AftLbs },
            v => Assert.True(IsWhole(v), v.ToString(CultureInfo.InvariantCulture)));
        Assert.Equal(Math.Round(plan.BlockFuelLbs, MidpointRounding.AwayFromZero), loads.LeftLbs + loads.RightLbs + loads.CenterLbs);
        Assert.Equal(Math.Round(150 * plan.PassengerWeightLbs, MidpointRounding.AwayFromZero), loads.PassengerLbs);
        Assert.Equal(Math.Round(plan.PayloadLbs - loads.PassengerLbs, MidpointRounding.AwayFromZero), loads.ForwardLbs + loads.AftLbs);
        Assert.Equal(loads.LeftLbs, loads.RightLbs);
    }

    [Fact]
    public void A_fractional_plan_never_puts_negative_fuel_in_the_center()
    {
        foreach (double block in new[] { 0.4, 0.6, 1.4, 1.6, 2.5, 3.5, 29157.6, 29158.4 })
        {
            var loads = Loads(Plan(block: block));
            Assert.True(loads.CenterLbs >= 0, block.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(Math.Round(block, MidpointRounding.AwayFromZero), loads.LeftLbs + loads.RightLbs + loads.CenterLbs);
        }
    }

    // ---- the write --------------------------------------------------------------------------

    [Fact]
    public void The_write_sets_the_six_setters_in_one_string()
    {
        string code = Bbs757Loadsheet.WriteCode(Loads(Plan()));

        Assert.Equal(
            "12775 (>L:BBS_EFBX_SET_FUEL_LEFT_LBS) 12775 (>L:BBS_EFBX_SET_FUEL_RIGHT_LBS) " +
            "0 (>L:BBS_EFBX_SET_FUEL_CENTER_LBS) 30800 (>L:BBS_EFBX_SET_PAX_WEIGHT_LBS) " +
            "10300 (>L:BBS_EFBX_SET_CARGO_FWD_LBS) 10359 (>L:BBS_EFBX_SET_CARGO_AFT_LBS)", code);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_write_is_whole_numbers_and_invariant_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589", paxWeight: "84", payload: "23341"))!;
            string code = Bbs757Loadsheet.WriteCode(Loads(plan));

            Assert.DoesNotContain(",", code);
            Assert.DoesNotContain(".", code);
            Assert.StartsWith("12774 (>L:BBS_EFBX_SET_FUEL_LEFT_LBS) 12774 (>L:BBS_EFBX_SET_FUEL_RIGHT_LBS) 1 (>L:BBS_EFBX_SET_FUEL_CENTER_LBS)", code);
        });
    }

    [Fact]
    public void The_write_fits_one_calculator_call()
    {
        var loads = new Bbs757Loads(14579, 14579, 46390, 99999, 10300, 14700, 176, 176);

        Assert.True(Bbs757Loadsheet.WriteCode(loads).Length < MSFSBlindAssist.Aircraft.BBS757.Bbs757Rpn.MaxCallChars);
    }

    // ---- the read-back ----------------------------------------------------------------------

    [Fact]
    public void The_read_back_variables_are_the_tanks_the_density_and_the_four_stations()
    {
        var byKey = Bbs757Loadsheet.ReadBackVariables.ToDictionary(v => v.Key);

        Assert.Equal(8, byKey.Count);
        Assert.Equal(("FUEL TANK LEFT MAIN QUANTITY", "gallons"), (byKey[Bbs757Loadsheet.LeftFuelKey].SimVar, byKey[Bbs757Loadsheet.LeftFuelKey].Units));
        Assert.Equal(("FUEL TANK RIGHT MAIN QUANTITY", "gallons"), (byKey[Bbs757Loadsheet.RightFuelKey].SimVar, byKey[Bbs757Loadsheet.RightFuelKey].Units));
        Assert.Equal(("FUEL TANK CENTER QUANTITY", "gallons"), (byKey[Bbs757Loadsheet.CenterFuelKey].SimVar, byKey[Bbs757Loadsheet.CenterFuelKey].Units));
        Assert.Equal(("FUEL WEIGHT PER GALLON", "pounds"), (byKey[Bbs757Loadsheet.FuelDensityKey].SimVar, byKey[Bbs757Loadsheet.FuelDensityKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:3", "pounds"), (byKey[Bbs757Loadsheet.FirstClassKey].SimVar, byKey[Bbs757Loadsheet.FirstClassKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:4", "pounds"), (byKey[Bbs757Loadsheet.EconomyKey].SimVar, byKey[Bbs757Loadsheet.EconomyKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:5", "pounds"), (byKey[Bbs757Loadsheet.ForwardHoldKey].SimVar, byKey[Bbs757Loadsheet.ForwardHoldKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:6", "pounds"), (byKey[Bbs757Loadsheet.AftHoldKey].SimVar, byKey[Bbs757Loadsheet.AftHoldKey].Units));
    }

    [Fact]
    public void The_read_back_variables_are_registered_quiet_on_request_and_on_no_panel()
    {
        // ReadFreshAsync delivers only a registered key; read for the spoken result alone, so no
        // Ctrl+M row ([VAR-9]) and no panel.
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var onPanels = def.GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var v in Bbs757Loadsheet.ReadBackVariables)
        {
            Assert.True(vars.ContainsKey(v.Key), v.Key);
            var d = vars[v.Key];
            Assert.Equal(v.SimVar, d.Name);
            Assert.Equal(v.Units, d.Units);
            Assert.Equal(SimVarType.SimVar, d.Type);
            Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
            Assert.False(d.IsAnnounced);
            Assert.True(d.ExcludeFromMonitorManager);
            Assert.DoesNotContain(v.Key, onPanels);
        }
    }

    [Fact]
    public void The_read_back_is_taken_by_key_and_a_missing_read_stays_missing()
    {
        var values = new Dictionary<string, double?>
        {
            [Bbs757Loadsheet.LeftFuelKey] = 1906.7,
            [Bbs757Loadsheet.RightFuelKey] = 1906.8,
            [Bbs757Loadsheet.CenterFuelKey] = 0,
            [Bbs757Loadsheet.FuelDensityKey] = 6.7,
            [Bbs757Loadsheet.FirstClassKey] = 2800,
            [Bbs757Loadsheet.EconomyKey] = null,
            [Bbs757Loadsheet.ForwardHoldKey] = 10300,
        };

        var read = Bbs757Loadsheet.ReadBack(values);

        Assert.Equal(1906.7, read.LeftGallons);
        Assert.Equal(1906.8, read.RightGallons);
        Assert.Equal(0, read.CenterGallons);
        Assert.Equal(6.7, read.PoundsPerGallon);
        Assert.Equal(2800, read.FirstClassLbs);
        Assert.Null(read.EconomyLbs);
        Assert.Equal(10300, read.ForwardHoldLbs);
        Assert.Null(read.AftHoldLbs);   // not read at all
    }

    // ---- did BlueBird take it ---------------------------------------------------------------

    [Fact]
    public void A_read_back_of_what_was_written_is_loaded()
    {
        var loads = Loads(Plan());

        Assert.Equal(Bbs757LoadVerdict.Loaded, Bbs757Loadsheet.Judge(loads, Empty, ReadBackOf(loads)));
    }

    [Fact]
    public void BlueBirds_conversion_at_6_72_pounds_per_gallon_still_counts_as_loaded()
    {
        // Measured: 10,000 written read 9,973; 20,000 read 19,919; 12,775 about 12,745.
        var loads = new Bbs757Loads(10000, 10000, 20000, 0, 0, 0, 0, 0);
        var read = new Bbs757LoadReadBack(1488.5, 1488.5, 2973, 6.7, 0, 0, 0, 0);

        Assert.Equal(Bbs757LoadVerdict.Loaded, Bbs757Loadsheet.Judge(loads, Empty, read));
    }

    [Fact]
    public void An_unchanged_read_back_is_not_taken()
    {
        var loads = Loads(Plan());
        var before = new Bbs757LoadReadBack(500, 500, 0, 6.7, 0, 0, 0, 0);

        Assert.Equal(Bbs757LoadVerdict.NotTaken, Bbs757Loadsheet.Judge(loads, before, before));
    }

    [Fact]
    public void Only_part_of_the_load_taken_is_partly_taken()
    {
        var loads = Loads(Plan());
        var after = ReadBackOf(loads) with { LeftGallons = 1500 / 6.7, RightGallons = 1500 / 6.7 };
        var before = after with { FirstClassLbs = 0, EconomyLbs = 0, ForwardHoldLbs = 0, AftHoldLbs = 0 };

        Assert.Equal(Bbs757LoadVerdict.PartlyTaken, Bbs757Loadsheet.Judge(loads, before, after));
    }

    [Fact]
    public void With_no_reading_from_before_a_miss_is_partly_taken_never_not_taken()
    {
        var loads = Loads(Plan());

        Assert.Equal(Bbs757LoadVerdict.PartlyTaken, Bbs757Loadsheet.Judge(loads, null, Empty));
    }

    [Fact]
    public void An_aircraft_already_holding_the_plan_is_loaded()
    {
        var loads = Loads(Plan());

        Assert.Equal(Bbs757LoadVerdict.Loaded, Bbs757Loadsheet.Judge(loads, ReadBackOf(loads), ReadBackOf(loads)));
    }

    [Fact]
    public void Nothing_read_back_is_not_read()
    {
        var loads = Loads(Plan());

        Assert.Equal(Bbs757LoadVerdict.NotRead,
            Bbs757Loadsheet.Judge(loads, Empty, new Bbs757LoadReadBack(null, null, null, null, null, null, null, null)));
    }

    [Theory]
    // A tank: within the larger of 1% of its target and 100 lb.
    [InlineData(4000, 3900, true)]        // 1% is 40: the 100 lb floor
    [InlineData(4000, 3899, false)]
    [InlineData(14579, 14434, true)]      // 1% is 145.79
    [InlineData(14579, 14433, false)]
    [InlineData(14579, 14724, true)]      // over as well as under
    [InlineData(14579, 14725, false)]
    public void A_wing_tank_matches_within_one_percent_or_100_pounds(double target, double read, bool matches)
    {
        var loads = new Bbs757Loads(target, target, 0, 0, 0, 0, 0, 0);
        var after = Pounds(read, target, 0, 0, 0, 0);

        Assert.Equal(matches, Bbs757Loadsheet.Judge(loads, null, after) == Bbs757LoadVerdict.Loaded);
    }

    [Theory]
    [InlineData(100, true)]   // an empty center: the 100 lb floor
    [InlineData(101, false)]
    public void The_empty_center_tank_matches_within_100_pounds(double read, bool matches)
    {
        var loads = new Bbs757Loads(1000, 1000, 0, 0, 0, 0, 0, 0);

        Assert.Equal(matches, Bbs757Loadsheet.Judge(loads, null, Pounds(1000, 1000, read, 0, 0, 0)) == Bbs757LoadVerdict.Loaded);
    }

    [Theory]
    // The cabin and each hold: within the larger of 0.5% of the target and 20 lb.
    [InlineData(30800, 30647, true)]   // 0.5% is 154
    [InlineData(30800, 30645, false)]
    [InlineData(1750, 1730, true)]     // 0.5% is 8.75: the 20 lb floor
    [InlineData(1750, 1729, false)]
    public void The_cabin_matches_within_half_a_percent_or_20_pounds(double target, double read, bool matches)
    {
        var loads = new Bbs757Loads(0, 0, 0, target, 0, 0, 10, 10);

        Assert.Equal(matches, Bbs757Loadsheet.Judge(loads, null, Pounds(0, 0, 0, read, 0, 0)) == Bbs757LoadVerdict.Loaded);
    }

    [Theory]
    [InlineData(10300, 10249, true)]   // 0.5% is 51.5
    [InlineData(10300, 10248, false)]
    [InlineData(0, 20, true)]          // an empty hold: the 20 lb floor
    [InlineData(0, 21, false)]
    public void Each_hold_matches_within_half_a_percent_or_20_pounds(double target, double read, bool matches)
    {
        var loads = new Bbs757Loads(0, 0, 0, 0, target, 0, 0, 0);

        Assert.Equal(matches, Bbs757Loadsheet.Judge(loads, null, Pounds(0, 0, 0, 0, read, 0)) == Bbs757LoadVerdict.Loaded);
    }

    [Fact]
    public void Tanks_are_judged_one_by_one_never_as_a_total()
    {
        // The right tank's shortfall made up by the left: the total matches, the tanks do not.
        var loads = new Bbs757Loads(10000, 10000, 0, 0, 0, 0, 0, 0);

        Assert.Equal(Bbs757LoadVerdict.PartlyTaken, Bbs757Loadsheet.Judge(loads, null, Pounds(11000, 9000, 0, 0, 0, 0)));
    }

    // ---- what is said -----------------------------------------------------------------------

    // Each total is the sum of its spoken parts: 12780 + 12780 + 0 = 25560.
    private const string MeasuredPounds =
        "Loaded QQX 1234, KSEA to KDEN. Fuel 25560 pounds: left 12780, right 12780, center 0. " +
        "Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360. " +
        "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds. " +
        "For the ACARS weight and balance, request WGTBAL on the CDU.";

    [Fact]
    public void The_measured_load_is_said_in_pounds_rounded_to_ten()
    {
        var plan = Plan();
        var loads = Loads(plan);

        Assert.Equal(MeasuredPounds, Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_phrase_is_the_same_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var plan = Plan();
            var loads = Loads(plan);
            Assert.Equal(MeasuredPounds, Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
        });
    }

    [Fact]
    public void Unread_aircraft_units_are_said_in_pounds()
    {
        var plan = Plan();
        var loads = Loads(plan);

        Assert.Equal(MeasuredPounds, Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), null));
    }

    [Fact]
    public void A_kilogram_aircraft_hears_kilograms()
    {
        var plan = Plan();
        var loads = Loads(plan);

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), Bbs757WeightUnits.Kilograms);

        Assert.Equal(
            "Loaded QQX 1234, KSEA to KDEN. Fuel 11580 kilograms: left 5790, right 5790, center 0. " +
            "Passengers 13970 kilograms. Cargo 9370 kilograms: forward 4670, aft 4700. " +
            "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds. " +
            "For the ACARS weight and balance, request WGTBAL on the CDU.", said);
    }

    [Fact]
    public void Every_passenger_seated_says_no_cap()
    {
        var plan = Plan(pax: 150, payload: 40000);
        var loads = Loads(plan);

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), Bbs757WeightUnits.Pounds);

        Assert.DoesNotContain("capped", said);
        Assert.Contains("Passengers 26250 pounds.", said);
        Assert.EndsWith("Cargo 13760 pounds: forward 6880, aft 6880. For the ACARS weight and balance, request WGTBAL on the CDU.", said);
    }

    [Fact]
    public void The_phrase_speaks_what_was_read_back_not_what_was_planned()
    {
        // BlueBird converts at about 6.72 lb/gal and the sim reads 6.70: 12775 written reads back lower.
        var plan = Plan();
        var loads = Loads(plan);
        var read = ReadBackOf(loads) with { LeftGallons = 12775 / 6.72, RightGallons = 12775 / 6.72 };

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, read, Bbs757WeightUnits.Pounds);

        Assert.StartsWith("Loaded QQX 1234, KSEA to KDEN. Fuel 25480 pounds: left 12740, right 12740, center 0.", said);
    }

    [Fact]
    public void A_tank_read_a_hair_below_zero_says_zero()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var read = ReadBackOf(loads) with { CenterGallons = -0.0001 };

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, read, Bbs757WeightUnits.Pounds);

        Assert.Contains("center 0.", said);
        Assert.DoesNotContain("-0", said);
    }

    [Fact]
    public void A_load_BlueBird_did_not_take_says_so_and_asks_about_BBS_Nest()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var before = new Bbs757LoadReadBack(500, 500, 0, 6.7, 0, 0, 0, 0);

        Assert.Equal("BlueBird did not take the load for QQX 1234, KSEA to KDEN. Is BBS Nest running?",
            Bbs757Loadsheet.Phrase(plan, loads, before, before, Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void A_load_partly_taken_says_which_parts_are_off_with_the_plans_figure()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var after = ReadBackOf(loads) with { LeftGallons = 1500 / 6.7, RightGallons = 1500 / 6.7 };
        var before = after with { FirstClassLbs = 0, EconomyLbs = 0, ForwardHoldLbs = 0, AftHoldLbs = 0 };

        Assert.Equal(
            "The aircraft did not take all of the load for QQX 1234, KSEA to KDEN. " +
            "Fuel 3000 pounds, not the plan's 25550: left 1500, right 1500, center 0. " +
            "Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360. " +
            "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds. " +
            "For the ACARS weight and balance, request WGTBAL on the CDU.",
            Bbs757Loadsheet.Phrase(plan, loads, before, after, Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void Passengers_and_cargo_off_are_said_with_the_plans_figures()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var after = ReadBackOf(loads) with { FirstClassLbs = 0, EconomyLbs = 0, ForwardHoldLbs = 4000, AftHoldLbs = 6000 };

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, after, Bbs757WeightUnits.Pounds);

        Assert.StartsWith("The aircraft did not take all of the load for QQX 1234, KSEA to KDEN. " +
                          "Fuel 25560 pounds: left 12780, right 12780, center 0. ", said);
        Assert.Contains("Passengers 0 pounds, not the plan's 30800. ", said);
        Assert.Contains("Cargo 10000 pounds, not the plan's 20660: forward 4000, aft 6000. ", said);
    }

    [Fact]
    public void A_partly_taken_load_is_said_the_same_under_a_comma_decimal_culture()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var after = ReadBackOf(loads) with { LeftGallons = 1500 / 6.7, RightGallons = 1500 / 6.7 };
        string invariant = Bbs757Loadsheet.Phrase(plan, loads, null, after, Bbs757WeightUnits.Kilograms);

        InCulture("de-DE", () =>
        {
            string said = Bbs757Loadsheet.Phrase(plan, loads, null, after, Bbs757WeightUnits.Kilograms);
            Assert.Equal(invariant, said);
            Assert.Contains("Fuel 1360 kilograms, not the plan's 11590: left 680, right 680, center 0.", said);
        });
    }

    [Fact]
    public void A_part_that_could_not_be_read_back_says_so()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var read = ReadBackOf(loads) with { PoundsPerGallon = null, AftHoldLbs = null };

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, read, Bbs757WeightUnits.Pounds);

        Assert.StartsWith("Loaded QQX 1234, KSEA to KDEN. The fuel could not be read back. Passengers 30800 pounds. " +
                          "The cargo could not be read back.", said);
        Assert.EndsWith("For the ACARS weight and balance, request WGTBAL on the CDU.", said);
    }

    [Fact]
    public void Nothing_read_back_says_the_load_was_sent_but_not_confirmed()
    {
        var plan = Plan();
        var loads = Loads(plan);

        string said = Bbs757Loadsheet.Phrase(plan, loads, Empty, new Bbs757LoadReadBack(null, null, null, null, null, null, null, null),
            Bbs757WeightUnits.Pounds);

        Assert.Equal("Fuel and payload sent for QQX 1234, KSEA to KDEN, but the tanks and holds could not be read back.", said);
    }

    [Fact]
    public void A_plan_with_no_flight_or_route_is_named_the_SimBrief_plan()
    {
        var plan = Plan() with { Flight = "", Origin = "", Destination = "" };
        var loads = Loads(plan);

        Assert.StartsWith("Loaded the SimBrief plan. Fuel 25560 pounds",
            Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void A_route_with_one_end_missing_is_left_out()
    {
        var plan = Plan() with { Destination = "" };
        var loads = Loads(plan);

        Assert.StartsWith("Loaded QQX 1234. Fuel",
            Bbs757Loadsheet.Phrase(plan, loads, Empty, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
    }

    // ---- the log ----------------------------------------------------------------------------

    [Fact]
    public void The_log_line_is_numbers_and_the_verdict_only()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var after = ReadBackOf(loads) with { LeftGallons = 12775 / 6.72, RightGallons = 12775 / 6.72, AftHoldLbs = null };

        string line = Bbs757Loadsheet.LogLine(loads, Empty, after);

        Assert.Equal("SimBrief load Loaded: targets 12775/12775/0, 30800, 10300/10359; " +
                     "before 0/0/0, 0, 0/0; after 12737/12737/0, 30800, 10300/-", line);
    }

    [Fact]
    public void The_write_is_logged_before_any_read_back()
    {
        var loads = Loads(Plan());

        Assert.Equal("SimBrief load written: targets 12775/12775/0, 30800, 10300/10359; before -/-/-, -, -/-",
            Bbs757Loadsheet.LogLine(loads, null, null));
    }
}
