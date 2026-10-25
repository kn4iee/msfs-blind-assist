using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's fuel and payload load from SimBrief, which spreads the plan the way BlueBird's own EFB
/// does (measured 2026-10-25, docs/b757/evidence/live-findings.md, session 5): the plan's fields,
/// the loads per tank, cabin and hold, the one calculator string, and what is said from the
/// read-back. Whether BlueBird takes the setters is checked in the sim.
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
        string? airline = "QQX", string? flight = "1234", string? origin = "KSEA", string? destination = "KDEN")
    {
        static string El(string name, string? value) => value == null ? "" : $"<{name}>{value}</{name}>";
        return "<OFP>" +
               $"<params>{El("units", units)}</params>" +
               $"<general>{El("icao_airline", airline)}{El("flight_number", flight)}</general>" +
               $"<origin>{El("icao_code", origin)}</origin><destination>{El("icao_code", destination)}</destination>" +
               $"<fuel>{El("plan_ramp", ramp)}</fuel>" +
               $"<weights>{El("pax_count", pax)}{El("pax_count_actual", paxActual)}{El("pax_weight", paxWeight)}" +
               $"{El("cargo", cargo)}{El("payload", payload)}</weights>" +
               "</OFP>";
    }

    private static Bbs757SimBriefPlan Plan(double block = 25550, int pax = 224, double paxWeight = 175, double payload = 51459)
        => new("QQX 1234", "KSEA", "KDEN", block, pax, paxWeight, payload);

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

    // ---- the plan ---------------------------------------------------------------------------

    [Fact]
    public void The_fixture_reads_as_the_measured_plan()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Fixture());

        Assert.NotNull(plan);
        Assert.Equal("QQX 1234", plan!.Flight);
        Assert.Equal("KSEA", plan.Origin);
        Assert.Equal("KDEN", plan.Destination);
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
    public void Numbers_read_the_same_under_a_comma_decimal_culture()
    {
        InCulture("de-DE", () =>
        {
            var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589.5"));
            Assert.Equal(11589.5 * 2.20462, plan!.BlockFuelLbs, 6);
        });
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
    public void The_write_is_invariant_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            string code = Bbs757Loadsheet.WriteCode(Loads(Plan(block: 25551, payload: 51460)));

            Assert.StartsWith("12775.5 (>L:BBS_EFBX_SET_FUEL_LEFT_LBS) 12775.5 (>L:BBS_EFBX_SET_FUEL_RIGHT_LBS)", code);
            Assert.Contains("10360 (>L:BBS_EFBX_SET_CARGO_AFT_LBS)", code);
            Assert.DoesNotContain(",", code);
        });
    }

    [Fact]
    public void The_write_fits_one_calculator_call()
    {
        // The largest numbers each setter can be given, with six decimals from a kilogram plan.
        var loads = new Bbs757Loads(14578.123456, 14578.123456, 46389.123456, 99999.123456, 10299.123456, 14699.123456, 176, 176);

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

    // ---- what is said -----------------------------------------------------------------------

    private const string MeasuredPounds =
        "Loaded QQX 1234, KSEA to KDEN. Fuel 25550 pounds: left 12780, right 12780, center 0. " +
        "Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360. " +
        "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds. " +
        "For the ACARS weight and balance, request WGTBAL on the CDU.";

    [Fact]
    public void The_measured_load_is_said_in_pounds_rounded_to_ten()
    {
        var plan = Plan();
        var loads = Loads(plan);

        Assert.Equal(MeasuredPounds, Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
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
            Assert.Equal(MeasuredPounds, Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
        });
    }

    [Fact]
    public void Unread_aircraft_units_are_said_in_pounds()
    {
        var plan = Plan();
        var loads = Loads(plan);

        Assert.Equal(MeasuredPounds, Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), null));
    }

    [Fact]
    public void A_kilogram_aircraft_hears_kilograms()
    {
        var plan = Plan();
        var loads = Loads(plan);

        string said = Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), Bbs757WeightUnits.Kilograms);

        Assert.Equal(
            "Loaded QQX 1234, KSEA to KDEN. Fuel 11590 kilograms: left 5790, right 5790, center 0. " +
            "Passengers 13970 kilograms. Cargo 9370 kilograms: forward 4670, aft 4700. " +
            "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds. " +
            "For the ACARS weight and balance, request WGTBAL on the CDU.", said);
    }

    [Fact]
    public void Every_passenger_seated_says_no_cap()
    {
        var plan = Plan(pax: 150, payload: 40000);
        var loads = Loads(plan);

        string said = Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), Bbs757WeightUnits.Pounds);

        Assert.DoesNotContain("capped", said);
        Assert.Contains("Passengers 26250 pounds.", said);
        Assert.EndsWith("Cargo 13750 pounds: forward 6880, aft 6880. For the ACARS weight and balance, request WGTBAL on the CDU.", said);
    }

    [Fact]
    public void The_phrase_speaks_what_was_read_back_not_what_was_planned()
    {
        // BlueBird converts at about 6.72 lb/gal and the sim reads 6.70: 12775 written reads back lower.
        var plan = Plan();
        var loads = Loads(plan);
        var read = ReadBackOf(loads) with { LeftGallons = 12775 / 6.72, RightGallons = 12775 / 6.72 };

        string said = Bbs757Loadsheet.Phrase(plan, loads, read, Bbs757WeightUnits.Pounds);

        Assert.Contains("Fuel 25470 pounds: left 12740, right 12740, center 0.", said);
    }

    [Fact]
    public void A_tank_read_a_hair_below_zero_says_zero()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var read = ReadBackOf(loads) with { CenterGallons = -0.0001 };

        Assert.Contains("center 0.", Bbs757Loadsheet.Phrase(plan, loads, read, Bbs757WeightUnits.Pounds));
        Assert.DoesNotContain("-0", Bbs757Loadsheet.Phrase(plan, loads, read, Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void A_part_that_could_not_be_read_back_says_so()
    {
        var plan = Plan();
        var loads = Loads(plan);
        var read = ReadBackOf(loads) with { PoundsPerGallon = null, AftHoldLbs = null };

        string said = Bbs757Loadsheet.Phrase(plan, loads, read, Bbs757WeightUnits.Pounds);

        Assert.StartsWith("Loaded QQX 1234, KSEA to KDEN. The fuel could not be read back. Passengers 30800 pounds. " +
                          "The cargo could not be read back.", said);
        Assert.EndsWith("For the ACARS weight and balance, request WGTBAL on the CDU.", said);
    }

    [Fact]
    public void Nothing_read_back_says_the_load_was_sent_but_not_confirmed()
    {
        var plan = Plan();
        var loads = Loads(plan);

        string said = Bbs757Loadsheet.Phrase(plan, loads, new Bbs757LoadReadBack(null, null, null, null, null, null, null, null),
            Bbs757WeightUnits.Pounds);

        Assert.Equal("Fuel and payload sent for QQX 1234, KSEA to KDEN, but the tanks and holds could not be read back.", said);
    }

    [Fact]
    public void A_plan_with_no_flight_or_route_is_named_the_SimBrief_plan()
    {
        var plan = Plan() with { Flight = "", Origin = "", Destination = "" };
        var loads = Loads(plan);

        Assert.StartsWith("Loaded the SimBrief plan. Fuel 25550 pounds",
            Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void A_route_with_one_end_missing_is_left_out()
    {
        var plan = Plan() with { Destination = "" };
        var loads = Loads(plan);

        Assert.StartsWith("Loaded QQX 1234. Fuel",
            Bbs757Loadsheet.Phrase(plan, loads, ReadBackOf(loads), Bbs757WeightUnits.Pounds));
    }
}
