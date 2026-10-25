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
/// and destination name the plan when the result is spoken, and the aircraft type
/// (<c>aircraft/icaocode</c>) must be the 757-200's; any of them may be empty.
/// </summary>
public sealed record Bbs757SimBriefPlan(string Flight, string Origin, string Destination, string AircraftIcao,
    double BlockFuelLbs, int Passengers, double PassengerWeightLbs, double PayloadLbs);

/// <summary>What goes in each wing tank, the center tank, the cabin and each hold, in whole pounds.</summary>
public sealed record Bbs757Loads(double LeftLbs, double RightLbs, double CenterLbs, double PassengerLbs,
    double ForwardLbs, double AftLbs, int PlannedPassengers, int SeatedPassengers)
{
    /// <summary>The plan has more passengers than the cabin seats.</summary>
    public bool PassengersCapped => PlannedPassengers > SeatedPassengers;
}

/// <summary>
/// The tanks (gallons), the sim's fuel weight per gallon and the four payload stations (pounds),
/// as read before or after the write; a value that could not be read is null.
/// </summary>
public sealed record Bbs757LoadReadBack(double? LeftGallons, double? RightGallons, double? CenterGallons,
    double? PoundsPerGallon, double? FirstClassLbs, double? EconomyLbs, double? ForwardHoldLbs, double? AftHoldLbs);

/// <summary>One stock variable the read-back needs: the key it is registered and read under.</summary>
public sealed record Bbs757ReadBackVariable(string Key, string SimVar, string Units, string Label);

/// <summary>Whether the read-back shows BlueBird took the load (<see cref="Bbs757Loadsheet.Judge"/>).</summary>
public enum Bbs757LoadVerdict
{
    /// <summary>Every tank, the cabin and each hold that could be read hold what was written.</summary>
    Loaded,
    /// <summary>Some part does not, and something moved (or nothing could be read before the write).</summary>
    PartlyTaken,
    /// <summary>Some part does not, and nothing moved from before the write: BlueBird ignored the setters.</summary>
    NotTaken,
    /// <summary>Nothing could be read back.</summary>
    NotRead,
    /// <summary>
    /// Every group read holds what was written, but a group (the fuel, the cabin or the cargo) could
    /// not be read back, so the load is not known to be whole: never said as "Loaded".
    /// </summary>
    Unchecked,
}

/// <summary>
/// The 757's fuel and payload from a SimBrief plan, spread the way BlueBird's own EFB spreads them
/// (measured 2026-10-25, docs/b757/evidence/live-findings.md, session 5): the block evenly over
/// both wings and the rest in the center, every passenger the cabin seats at the plan's standard
/// weight, and the rest of the payload as cargo, half to each hold with the forward one capped.
/// BlueBird's setters (<c>L:BBS_EFBX_SET_*</c>) take one write each and set their tank or station
/// at once, where its own EFB defuels and boards for half an hour. Every target is whole pounds:
/// the setters were measured with whole values only.
///
/// Pure: parse the plan, compute the loads, build the one calculator string, judge the read-back
/// against what was written and phrase it. The window fetches, writes and reads.
/// </summary>
public static class Bbs757Loadsheet
{
    /// <summary>The 757-200's ICAO type, the only one a plan may be for.</summary>
    public const string AircraftType = "B752";

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

    /// <summary>
    /// A tank holds its target within the larger of this share of it and <see cref="FuelToleranceLbs"/>.
    /// BlueBird converts at about 6.72 lb/gal and the sim reads 6.70: 10,000 written read 9,973,
    /// 20,000 read 19,919, 12,775 about 12,745 (all within 0.5%).
    /// </summary>
    public const double FuelToleranceShare = 0.01;
    public const double FuelToleranceLbs = 100;

    /// <summary>The cabin and each hold hold their target within the larger of this share and <see cref="PayloadToleranceLbs"/> (set exactly, measured).</summary>
    public const double PayloadToleranceShare = 0.005;
    public const double PayloadToleranceLbs = 20;

    public const string FuelLeftSetter = "BBS_EFBX_SET_FUEL_LEFT_LBS";
    public const string FuelRightSetter = "BBS_EFBX_SET_FUEL_RIGHT_LBS";
    public const string FuelCenterSetter = "BBS_EFBX_SET_FUEL_CENTER_LBS";
    /// <summary>The whole cabin's weight; BlueBird splits it between First and Economy itself (1 : 10).</summary>
    public const string PassengerSetter = "BBS_EFBX_SET_PAX_WEIGHT_LBS";
    public const string CargoForwardSetter = "BBS_EFBX_SET_CARGO_FWD_LBS";
    public const string CargoAftSetter = "BBS_EFBX_SET_CARGO_AFT_LBS";

    public const string FuelTooMuchText = "The plan's fuel is more than the tanks hold.";
    public const string CargoTooMuchText = "The plan's cargo is more than the holds take.";
    public const string NoAircraftTypeText = "Your latest SimBrief plan names no aircraft type. Create a B752 plan, then try again.";
    public const string WeightAndBalanceText = "For the ACARS weight and balance, request WGTBAL on the CDU.";

    /// <summary>The refusal of a plan for another aircraft.</summary>
    public static string WrongAircraftText(string icao) =>
        $"Your latest SimBrief plan is for {icao}, not a 757-200. Create a B752 plan, then try again.";

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
    /// too light. Numbers are read culture-proof (CORE-17). No DTD is processed. The aircraft type
    /// is read as it is (empty when missing) and judged by <see cref="Compute"/>.
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
            Text(root, "destination", "icao_code") ?? "", Text(root, "aircraft", "icaocode") ?? "",
            b * f, n, w * f, p * f);
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
    /// The loads in whole pounds, or a refusal (and no loads): first when the plan is not for a
    /// B752, then when it does not fit. With the block rounded: up to what both wings take
    /// (2 × 14,579), the right wing <c>floor(block / 2)</c>, the left the rest (an odd pound goes
    /// to the left wing, never alone in the center: a 1 lb center tank is unmeasured on BlueBird and
    /// could trip a fuel-configuration message) and the center empty; past it, both wings full and
    /// the center the rest, refused past its 46,390. Passengers: the seated ones (at most 176) at the
    /// plan's standard weight, rounded. Cargo: the payload less the passengers' weight (none when
    /// they weigh more), rounded; forward <c>min(floor(cargo / 2), 10,300)</c>, aft the rest; refused
    /// past 25,000. The measured plan (25,550 block, 224 × 175, payload 51,459) gives wings 12,775,
    /// center 0, passengers 30,800, holds 10,300 and 10,359.
    /// </summary>
    public static (Bbs757Loads? Loads, string? Refusal) Compute(Bbs757SimBriefPlan plan)
    {
        if (plan.AircraftIcao.Length == 0) return (null, NoAircraftTypeText);
        if (!string.Equals(plan.AircraftIcao, AircraftType, StringComparison.OrdinalIgnoreCase))
            return (null, WrongAircraftText(plan.AircraftIcao));

        double block = Whole(plan.BlockFuelLbs);
        double left, right, center;
        if (block <= 2 * WingTankLbs)
        {
            right = Math.Floor(block / 2);
            left = block - right;
            center = 0;
        }
        else
        {
            left = right = WingTankLbs;
            center = block - 2 * WingTankLbs;
        }
        if (center > CenterTankLbs) return (null, FuelTooMuchText);

        int seated = Math.Min(plan.Passengers, Seats);
        double passengers = Whole(seated * plan.PassengerWeightLbs);

        double cargo = Whole(Math.Max(0, plan.PayloadLbs - passengers));
        if (cargo > ForwardHoldLbs + AftHoldLbs) return (null, CargoTooMuchText);
        double forward = Math.Min(Math.Floor(cargo / 2), ForwardHoldLbs);
        double aft = cargo - forward;

        return (new Bbs757Loads(left, right, center, passengers, forward, aft, plan.Passengers, seated), null);
    }

    private static double Whole(double lbs) => Math.Round(lbs, MidpointRounding.AwayFromZero);

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

    private enum Group { Fuel, Cabin, Holds }

    /// <summary>One tank, the cabin, or one hold: its target and what was read before and after, in pounds.</summary>
    private sealed record Part(Group Group, double Target, double? Before, double? After, double Tolerance)
    {
        public bool Matches => After is double a && Math.Abs(a - Target) <= Tolerance;
        public bool Unchanged => After is double a && Before is double b && Math.Abs(a - b) <= Tolerance;
    }

    private static double FuelTolerance(double target) => Math.Max(FuelToleranceShare * target, FuelToleranceLbs);
    private static double PayloadTolerance(double target) => Math.Max(PayloadToleranceShare * target, PayloadToleranceLbs);

    /// <summary>The six parts in order: left, right, center, cabin, forward, aft.</summary>
    private static Part[] PartsOf(Bbs757Loads loads, Bbs757LoadReadBack? before, Bbs757LoadReadBack? after)
    {
        static double? Lbs(double? gallons, double? perGallon) => gallons * perGallon;
        return new[]
        {
            new Part(Group.Fuel, loads.LeftLbs, Lbs(before?.LeftGallons, before?.PoundsPerGallon),
                Lbs(after?.LeftGallons, after?.PoundsPerGallon), FuelTolerance(loads.LeftLbs)),
            new Part(Group.Fuel, loads.RightLbs, Lbs(before?.RightGallons, before?.PoundsPerGallon),
                Lbs(after?.RightGallons, after?.PoundsPerGallon), FuelTolerance(loads.RightLbs)),
            new Part(Group.Fuel, loads.CenterLbs, Lbs(before?.CenterGallons, before?.PoundsPerGallon),
                Lbs(after?.CenterGallons, after?.PoundsPerGallon), FuelTolerance(loads.CenterLbs)),
            new Part(Group.Cabin, loads.PassengerLbs, before?.FirstClassLbs + before?.EconomyLbs,
                after?.FirstClassLbs + after?.EconomyLbs, PayloadTolerance(loads.PassengerLbs)),
            new Part(Group.Holds, loads.ForwardLbs, before?.ForwardHoldLbs, after?.ForwardHoldLbs, PayloadTolerance(loads.ForwardLbs)),
            new Part(Group.Holds, loads.AftLbs, before?.AftHoldLbs, after?.AftHoldLbs, PayloadTolerance(loads.AftLbs)),
        };
    }

    /// <summary>The parts of every group read whole (a group with any part unread is left out).</summary>
    private static List<Part> ReadParts(Part[] parts)
    {
        var read = new List<Part>();
        foreach (var g in parts.GroupBy(p => p.Group))
            if (g.All(p => p.After != null)) read.AddRange(g);
        return read;
    }

    /// <summary>
    /// Whether BlueBird took the load, judged per tank, cabin and hold, never on totals (a short
    /// right tank is not made up by a full left one). A tank holds its target within the larger of
    /// 1% and 100 lb; the cabin (stations 3 and 4 together) and each hold within the larger of 0.5%
    /// and 20 lb. A group with a part unread is judged on nothing. With nothing read at all:
    /// <see cref="Bbs757LoadVerdict.NotRead"/>. A miss where no part read moved from
    /// <paramref name="before"/> (by the same tolerances): <see cref="Bbs757LoadVerdict.NotTaken"/>;
    /// any other miss, or a miss with nothing read before: <see cref="Bbs757LoadVerdict.PartlyTaken"/>
    /// (a miss outranks a group left unread). Every part read matching but a group unread:
    /// <see cref="Bbs757LoadVerdict.Unchecked"/>. Every part of every group matching:
    /// <see cref="Bbs757LoadVerdict.Loaded"/>.
    /// </summary>
    public static Bbs757LoadVerdict Judge(Bbs757Loads loads, Bbs757LoadReadBack? before, Bbs757LoadReadBack after)
    {
        var all = PartsOf(loads, before, after);
        var read = ReadParts(all);
        if (read.Count == 0) return Bbs757LoadVerdict.NotRead;
        if (!read.All(p => p.Matches))
            return read.All(p => p.Unchanged) ? Bbs757LoadVerdict.NotTaken : Bbs757LoadVerdict.PartlyTaken;
        return read.Count < all.Length ? Bbs757LoadVerdict.Unchecked : Bbs757LoadVerdict.Loaded;
    }

    /// <summary>
    /// Whether the window reads the tanks and stations once more, about 3 s later, before it speaks:
    /// for every verdict but <see cref="Bbs757LoadVerdict.Loaded"/>, so a group that did not answer
    /// or a load BlueBird was still taking gets a second look.
    /// </summary>
    public static bool ReadAgain(Bbs757LoadVerdict verdict) => verdict != Bbs757LoadVerdict.Loaded;

    // ---- what is said -----------------------------------------------------------------------

    /// <summary>
    /// What the load says, from the read-back (what the aircraft holds, not what was planned), in
    /// the aircraft's units (pounds when they could not be read), each number rounded to 10 and
    /// each total the sum of its spoken parts:
    /// <list type="bullet">
    /// <item>Loaded: "Loaded QQX 1234, KSEA to KDEN. Fuel 25560 pounds: left 12780, right 12780,
    /// center 0. Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360."</item>
    /// <item>Partly taken: "The aircraft did not take all of the load for …", each matching part as
    /// above, each part off with the plan's figure: "Fuel 3000 pounds, not the plan's 25550: left
    /// 1500, right 1500, center 0."</item>
    /// <item>Not taken: "BlueBird did not take the load for …. Is BBS Nest running?" and nothing more.</item>
    /// <item>Unchecked (every group read matches, one could not be read): "The load for … could not
    /// all be checked.", the groups read as above and "The fuel could not be read back." (or the
    /// passengers, or the cargo).</item>
    /// <item>Nothing read: the load was sent, but the tanks and holds could not be read back.</item>
    /// </list>
    /// A group that could not be read says so, whatever the opening. A plan with more passengers than
    /// seats adds that the rest of the payload went in the holds, and a load whose every group was
    /// read back, taken in whole or part, ends with where the ACARS weight and balance comes from.
    /// </summary>
    public static string Phrase(Bbs757SimBriefPlan plan, Bbs757Loads loads, Bbs757LoadReadBack? before,
        Bbs757LoadReadBack after, Bbs757WeightUnits? units)
    {
        string name = PlanName(plan) ?? "the SimBrief plan";
        switch (Judge(loads, before, after))
        {
            case Bbs757LoadVerdict.NotRead:
                return $"Fuel and payload sent for {name}, but the tanks and holds could not be read back.";
            case Bbs757LoadVerdict.NotTaken:
                return $"BlueBird did not take the load for {name}. Is BBS Nest running?";
            case Bbs757LoadVerdict.PartlyTaken:
                return Said($"The aircraft did not take all of the load for {name}.");
            case Bbs757LoadVerdict.Unchecked:
                return Said($"The load for {name} could not all be checked.");
            default:
                return Said($"Loaded {name}.");
        }

        string Said(string opening)
        {
            var shown = units ?? Bbs757WeightUnits.Pounds;
            string unit = Bbs757EfbLayout.UnitsName(shown);
            long N(double lbs) => Round10(shown == Bbs757WeightUnits.Kilograms ? lbs / PoundsPerKilogram : lbs);
            string S(long value) => value.ToString(CultureInfo.InvariantCulture);

            var parts = PartsOf(loads, before, after);
            Part left = parts[0], right = parts[1], center = parts[2], cabin = parts[3], forward = parts[4], aft = parts[5];
            string Off(params Part[] group) => group.All(p => p.Matches)
                ? "" : $", not the plan's {S(N(group.Sum(p => p.Target)))}";

            var said = new List<string> { opening };
            if (left.After is double l && right.After is double r && center.After is double c)
                said.Add($"Fuel {S(N(l) + N(r) + N(c))} {unit}{Off(left, right, center)}: left {S(N(l))}, right {S(N(r))}, center {S(N(c))}.");
            else
                said.Add("The fuel could not be read back.");
            if (cabin.After is double p)
                said.Add($"Passengers {S(N(p))} {unit}{Off(cabin)}.");
            else
                said.Add("The passengers could not be read back.");
            if (forward.After is double fw && aft.After is double af)
                said.Add($"Cargo {S(N(fw) + N(af))} {unit}{Off(forward, aft)}: forward {S(N(fw))}, aft {S(N(af))}.");
            else
                said.Add("The cargo could not be read back.");
            if (loads.PassengersCapped)
                said.Add($"The plan's {loads.PlannedPassengers.ToString(CultureInfo.InvariantCulture)} passengers are capped at " +
                         $"the 757's {Seats.ToString(CultureInfo.InvariantCulture)} seats; the rest of the payload goes in the holds.");
            // The weight and balance is worth requesting only once every group has been read back.
            if (parts.All(x => x.After != null)) said.Add(WeightAndBalanceText);
            return string.Join(" ", said);
        }
    }

    /// <summary>"QQX 1234, KSEA to KDEN", the flight alone, the route alone, or null when the plan names neither.</summary>
    private static string? PlanName(Bbs757SimBriefPlan plan)
    {
        string route = plan.Origin.Length > 0 && plan.Destination.Length > 0 ? $"{plan.Origin} to {plan.Destination}" : "";
        string name = string.Join(", ", new[] { plan.Flight, route }.Where(s => s.Length > 0));
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// A weight rounded to the nearest 10 (half away from zero), as a whole number, so a read-back
    /// a hair below zero says "0", never "-0", and spoken parts add up to their spoken total.
    /// </summary>
    private static long Round10(double value) => (long)Math.Round(value / 10, MidpointRounding.AwayFromZero) * 10;

    // ---- the log ----------------------------------------------------------------------------

    /// <summary>
    /// Numbers only, whole pounds, for debug.log: the targets, what was read before the write and,
    /// once read back, the verdict and what was read after; "-" for a value not read.
    /// "SimBrief load Loaded: targets 12775/12775/0, 30800, 10300/10359; before 0/0/0, 0, 0/0; after
    /// 12737/12737/0, 30800, 10300/10359". The plan's name is not in it.
    /// </summary>
    public static string LogLine(Bbs757Loads loads, Bbs757LoadReadBack? before, Bbs757LoadReadBack? after)
    {
        var parts = PartsOf(loads, before, after);
        static string W(double? lbs) => lbs is double v ? ((long)Whole(v)).ToString(CultureInfo.InvariantCulture) : "-";
        static string Row(IReadOnlyList<double?> v) => $"{W(v[0])}/{W(v[1])}/{W(v[2])}, {W(v[3])}, {W(v[4])}/{W(v[5])}";

        string targets = Row(parts.Select(p => (double?)p.Target).ToList());
        string was = Row(parts.Select(p => p.Before).ToList());
        if (after == null) return $"SimBrief load written: targets {targets}; before {was}";
        string now = Row(parts.Select(p => p.After).ToList());
        return $"SimBrief load {Judge(loads, before, after)}: targets {targets}; before {was}; after {now}";
    }
}
