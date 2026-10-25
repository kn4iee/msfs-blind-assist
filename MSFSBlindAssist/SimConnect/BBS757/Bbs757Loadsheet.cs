using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What the 757's load takes from a SimBrief OFP, in pounds whatever the plan's units:
/// the block fuel (<c>fuel/plan_ramp</c>), the passengers (<c>weights/pax_count_actual</c>, else
/// <c>pax_count</c>) and their standard weight (<c>pax_weight</c>), and the payload. Flight, origin
/// and destination name the plan when the result is spoken; any of them may be empty.
/// </summary>
public sealed record Bbs757SimBriefPlan(string Flight, string Origin, string Destination,
    double BlockFuelLbs, int Passengers, double PassengerWeightLbs, double PayloadLbs);

/// <summary>What goes in each wing tank, the center tank, the cabin and each hold, in pounds.</summary>
public sealed record Bbs757Loads(double LeftLbs, double RightLbs, double CenterLbs, double PassengerLbs,
    double ForwardLbs, double AftLbs, int PlannedPassengers, int SeatedPassengers)
{
    /// <summary>The plan has more passengers than the cabin seats.</summary>
    public bool PassengersCapped => PlannedPassengers > SeatedPassengers;
}

/// <summary>
/// The tanks (gallons), the sim's fuel weight per gallon and the four payload stations (pounds),
/// as read back after the write; a value that could not be read is null.
/// </summary>
public sealed record Bbs757LoadReadBack(double? LeftGallons, double? RightGallons, double? CenterGallons,
    double? PoundsPerGallon, double? FirstClassLbs, double? EconomyLbs, double? ForwardHoldLbs, double? AftHoldLbs);

/// <summary>One stock variable the read-back needs: the key it is registered and read under.</summary>
public sealed record Bbs757ReadBackVariable(string Key, string SimVar, string Units, string Label);

/// <summary>
/// The 757's fuel and payload from a SimBrief plan, spread the way BlueBird's own EFB spreads them
/// (measured 2026-10-25, docs/b757/evidence/live-findings.md, session 5): the block evenly over
/// both wings and the rest in the center, every passenger the cabin seats at the plan's standard
/// weight, and the rest of the payload as cargo, half to each hold with the forward one capped.
/// BlueBird's setters (<c>L:BBS_EFBX_SET_*</c>) take one write each and set their tank or station
/// at once, where its own EFB defuels and boards for half an hour.
///
/// Pure: parse the plan, compute the loads, build the one calculator string, phrase the
/// read-back. The window fetches, writes and reads.
/// </summary>
public static class Bbs757Loadsheet
{
    /// <summary>One wing tank: 2,176 gallons (<c>flight_model.cfg</c>) at 6.7 pounds.</summary>
    public const double WingTankLbs = 14579;

    /// <summary>The center tank: 6,924 gallons at 6.7 pounds.</summary>
    public const double CenterTankLbs = 46390;

    /// <summary>The cabin: 16 First and 160 Economy seats (BlueBird's EFB showed 15/16 and 151/160).</summary>
    public const int Seats = 176;

    /// <summary>The forward hold, where BlueBird's cargo setter stops filling it (measured).</summary>
    public const double ForwardHoldLbs = 10300;

    /// <summary>The aft hold, as measured with the same setter.</summary>
    public const double AftHoldLbs = 14700;

    /// <summary>Pounds per kilogram, for a plan in kilograms and an aircraft that shows them.</summary>
    public const double PoundsPerKilogram = 2.20462;

    public const string FuelLeftSetter = "BBS_EFBX_SET_FUEL_LEFT_LBS";
    public const string FuelRightSetter = "BBS_EFBX_SET_FUEL_RIGHT_LBS";
    public const string FuelCenterSetter = "BBS_EFBX_SET_FUEL_CENTER_LBS";
    /// <summary>The whole cabin's weight; BlueBird splits it between First and Economy itself (1 : 10).</summary>
    public const string PassengerSetter = "BBS_EFBX_SET_PAX_WEIGHT_LBS";
    public const string CargoForwardSetter = "BBS_EFBX_SET_CARGO_FWD_LBS";
    public const string CargoAftSetter = "BBS_EFBX_SET_CARGO_AFT_LBS";

    public const string FuelTooMuchText = "The plan's fuel is more than the tanks hold.";
    public const string CargoTooMuchText = "The plan's cargo is more than the holds take.";
    public const string WeightAndBalanceText = "For the ACARS weight and balance, request WGTBAL on the CDU.";

    public const string LeftFuelKey = "BBS757_FUEL_LEFT_GALLONS";
    public const string RightFuelKey = "BBS757_FUEL_RIGHT_GALLONS";
    public const string CenterFuelKey = "BBS757_FUEL_CENTER_GALLONS";
    public const string FuelDensityKey = "BBS757_FUEL_POUNDS_PER_GALLON";
    public const string FirstClassKey = "BBS757_PAYLOAD_FIRST_CLASS";
    public const string EconomyKey = "BBS757_PAYLOAD_ECONOMY";
    public const string ForwardHoldKey = "BBS757_PAYLOAD_FORWARD_HOLD";
    public const string AftHoldKey = "BBS757_PAYLOAD_AFT_HOLD";

    /// <summary>
    /// The stock variables the read-back reads; the definition registers each under its key.
    /// Payload stations 1 and 2 are the pilots (200 lb each) and are not read.
    /// </summary>
    public static IReadOnlyList<Bbs757ReadBackVariable> ReadBackVariables { get; } = new Bbs757ReadBackVariable[]
    {
        new(LeftFuelKey, "FUEL TANK LEFT MAIN QUANTITY", "gallons", "Left Wing Tank"),
        new(RightFuelKey, "FUEL TANK RIGHT MAIN QUANTITY", "gallons", "Right Wing Tank"),
        new(CenterFuelKey, "FUEL TANK CENTER QUANTITY", "gallons", "Center Tank"),
        new(FuelDensityKey, "FUEL WEIGHT PER GALLON", "pounds", "Fuel Weight Per Gallon"),
        new(FirstClassKey, "PAYLOAD STATION WEIGHT:3", "pounds", "First Class Payload"),
        new(EconomyKey, "PAYLOAD STATION WEIGHT:4", "pounds", "Economy Payload"),
        new(ForwardHoldKey, "PAYLOAD STATION WEIGHT:5", "pounds", "Forward Hold Payload"),
        new(AftHoldKey, "PAYLOAD STATION WEIGHT:6", "pounds", "Aft Hold Payload"),
    };

    // A passenger count past this is not a 757's (nor any airliner's) and is not read.
    private const int MaxPassengers = 10000;

    // ---- the plan ---------------------------------------------------------------------------

    /// <summary>
    /// The plan from a SimBrief OFP's XML, or null when it is not one, or lacks the block fuel, the
    /// passengers, their weight, the payload (stood in for by passengers × weight + <c>cargo</c>) or
    /// units of <c>lbs</c> or <c>kgs</c>; a negative weight or a passenger count that is not whole
    /// is refused too. Units are never guessed: a kilogram plan read as pounds would be 2.2 times
    /// too light. Numbers are read culture-proof (CORE-17). No DTD is processed.
    /// </summary>
    public static Bbs757SimBriefPlan? ParsePlan(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XElement? root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            root = XDocument.Load(reader).Root;
        }
        catch (XmlException)
        {
            return null;
        }
        if (root == null) return null;

        double? factor = Text(root, "params", "units")?.ToLowerInvariant() switch
        {
            "lbs" => 1.0,
            "kgs" => PoundsPerKilogram,
            _ => null,
        };
        if (factor is not double f) return null;

        // A field stood in for only when it is missing: one that is there but unreadable means the
        // plan is not what this expects, and nothing is loaded from it.
        double? block = Weight(root, "fuel", "plan_ramp");
        double? paxWeight = Weight(root, "weights", "pax_weight");
        int? pax = Text(root, "weights", "pax_count_actual") != null
            ? Count(root, "weights", "pax_count_actual")
            : Count(root, "weights", "pax_count");
        if (block is not double b || paxWeight is not double w || pax is not int n) return null;

        double? payload = Text(root, "weights", "payload") != null
            ? Weight(root, "weights", "payload")
            : Weight(root, "weights", "cargo") is double cargo ? n * w + cargo : null;
        if (payload is not double p) return null;

        string airline = Text(root, "general", "icao_airline") ?? "";
        string number = Text(root, "general", "flight_number") ?? "";
        string flight = string.Join(" ", new[] { airline, number }.Where(s => s.Length > 0));
        return new Bbs757SimBriefPlan(flight, Text(root, "origin", "icao_code") ?? "",
            Text(root, "destination", "icao_code") ?? "", b * f, n, w * f, p * f);
    }

    /// <summary>A child's child's text, trimmed with inner runs of white space made one space; null when missing or empty.</summary>
    private static string? Text(XElement root, string parent, string child)
    {
        string? text = root.Element(parent)?.Element(child)?.Value;
        if (string.IsNullOrWhiteSpace(text)) return null;
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A weight in the plan's units, or null when missing, not a number or negative.</summary>
    private static double? Weight(XElement root, string parent, string child)
        => NumberParsing.TryParseExternal(Text(root, parent, child), out double v) && v >= 0 ? v : null;

    /// <summary>A whole count from 0 to <see cref="MaxPassengers"/>, or null.</summary>
    private static int? Count(XElement root, string parent, string child)
        => Weight(root, parent, child) is double v && v == Math.Floor(v) && v <= MaxPassengers ? (int)v : null;

    // ---- the loads --------------------------------------------------------------------------

    /// <summary>
    /// The loads, or a refusal (and no loads) when the plan does not fit. Fuel: each wing
    /// <c>min(block / 2, 14,579)</c>, the center the rest, refused past its 46,390. Passengers: the
    /// seated ones (at most 176) at the plan's standard weight. Cargo: the payload less the
    /// passengers' weight (none when they weigh more), half to each hold with the forward one capped
    /// at 10,300 and its excess aft, refused past 25,000. The measured plan (25,550 block, 224 × 175,
    /// payload 51,459) gives wings 12,775, center 0, passengers 30,800, holds 10,300 and 10,359.
    /// </summary>
    public static (Bbs757Loads? Loads, string? Refusal) Compute(Bbs757SimBriefPlan plan)
    {
        double wing = Math.Min(plan.BlockFuelLbs / 2, WingTankLbs);
        double center = plan.BlockFuelLbs - 2 * wing;
        if (center > CenterTankLbs) return (null, FuelTooMuchText);

        int seated = Math.Min(plan.Passengers, Seats);
        double passengers = seated * plan.PassengerWeightLbs;

        double cargo = Math.Max(0, plan.PayloadLbs - passengers);
        if (cargo > ForwardHoldLbs + AftHoldLbs) return (null, CargoTooMuchText);
        double forward = Math.Min(cargo / 2, ForwardHoldLbs);
        double aft = cargo - forward;

        return (new Bbs757Loads(wing, wing, center, passengers, forward, aft, plan.Passengers, seated), null);
    }

    /// <summary>
    /// The six setters in one calculator string, in the order fuel left, right, center, passengers,
    /// cargo forward, aft; invariant numbers (VAR-5). About 270 characters, well inside one call.
    /// </summary>
    public static string WriteCode(Bbs757Loads loads) => string.Join(" ",
        Bbs757Rpn.SetLVar(FuelLeftSetter, loads.LeftLbs),
        Bbs757Rpn.SetLVar(FuelRightSetter, loads.RightLbs),
        Bbs757Rpn.SetLVar(FuelCenterSetter, loads.CenterLbs),
        Bbs757Rpn.SetLVar(PassengerSetter, loads.PassengerLbs),
        Bbs757Rpn.SetLVar(CargoForwardSetter, loads.ForwardLbs),
        Bbs757Rpn.SetLVar(CargoAftSetter, loads.AftLbs));

    // ---- the read-back ----------------------------------------------------------------------

    /// <summary>The read-back from the values read under each <see cref="ReadBackVariables"/> key; a missing key is null.</summary>
    public static Bbs757LoadReadBack ReadBack(IReadOnlyDictionary<string, double?> values)
    {
        double? Get(string key) => values.TryGetValue(key, out double? v) && v is double d && double.IsFinite(d) ? d : null;
        return new Bbs757LoadReadBack(Get(LeftFuelKey), Get(RightFuelKey), Get(CenterFuelKey), Get(FuelDensityKey),
            Get(FirstClassKey), Get(EconomyKey), Get(ForwardHoldKey), Get(AftHoldKey));
    }

    // ---- what is said -----------------------------------------------------------------------

    /// <summary>
    /// What the load says, from the read-back (what the aircraft holds, not what was planned), in
    /// the aircraft's units (pounds when they could not be read), each number rounded to 10:
    /// "Loaded QQX 1234, KSEA to KDEN. Fuel 25550 pounds: left 12780, right 12780, center 0.
    /// Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360." A part that could not
    /// be read back says so; when nothing could, the load is said to be sent, not loaded. A plan
    /// with more passengers than seats adds that the rest of the payload went in the holds, and every
    /// result ends with where the ACARS weight and balance comes from.
    /// </summary>
    public static string Phrase(Bbs757SimBriefPlan plan, Bbs757Loads loads, Bbs757LoadReadBack read, Bbs757WeightUnits? units)
    {
        var shown = units ?? Bbs757WeightUnits.Pounds;
        string unit = Bbs757EfbLayout.UnitsName(shown);
        string N(double lbs) => Round10(shown == Bbs757WeightUnits.Kilograms ? lbs / PoundsPerKilogram : lbs);

        string name = PlanName(plan) ?? "the SimBrief plan";
        double? left = read.LeftGallons * read.PoundsPerGallon;
        double? right = read.RightGallons * read.PoundsPerGallon;
        double? center = read.CenterGallons * read.PoundsPerGallon;
        double? first = read.FirstClassLbs, economy = read.EconomyLbs;
        double? forward = read.ForwardHoldLbs, aft = read.AftHoldLbs;

        bool fuelRead = left != null && right != null && center != null;
        bool cabinRead = first != null && economy != null;
        bool holdsRead = forward != null && aft != null;
        if (!fuelRead && !cabinRead && !holdsRead)
            return $"Fuel and payload sent for {name}, but the tanks and holds could not be read back.";

        var parts = new List<string> { $"Loaded {name}." };
        parts.Add(fuelRead
            ? $"Fuel {N(left!.Value + right!.Value + center!.Value)} {unit}: left {N(left.Value)}, right {N(right.Value)}, center {N(center.Value)}."
            : "The fuel could not be read back.");
        parts.Add(cabinRead
            ? $"Passengers {N(first!.Value + economy!.Value)} {unit}."
            : "The passengers could not be read back.");
        parts.Add(holdsRead
            ? $"Cargo {N(forward!.Value + aft!.Value)} {unit}: forward {N(forward.Value)}, aft {N(aft.Value)}."
            : "The cargo could not be read back.");
        if (loads.PassengersCapped)
            parts.Add($"The plan's {Whole(loads.PlannedPassengers)} passengers are capped at the 757's {Whole(Seats)} seats; " +
                      "the rest of the payload goes in the holds.");
        parts.Add(WeightAndBalanceText);
        return string.Join(" ", parts);
    }

    /// <summary>"QQX 1234, KSEA to KDEN", the flight alone, the route alone, or null when the plan names neither.</summary>
    private static string? PlanName(Bbs757SimBriefPlan plan)
    {
        string route = plan.Origin.Length > 0 && plan.Destination.Length > 0 ? $"{plan.Origin} to {plan.Destination}" : "";
        string name = string.Join(", ", new[] { plan.Flight, route }.Where(s => s.Length > 0));
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// A weight rounded to the nearest 10 (half away from zero), invariant digits with no grouping.
    /// Through a long, so a read-back a hair below zero says "0", never "-0".
    /// </summary>
    private static string Round10(double value)
        => ((long)Math.Round(value / 10, MidpointRounding.AwayFromZero) * 10).ToString(CultureInfo.InvariantCulture);

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);
}
