using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The load watch's judgement (task 11e), pure. Once the 757 EFB window has tapped IMPORT FROM
/// SIMBRIEF and START BOARDING, BlueBird loads at its own pace with only a picture to show it: the
/// fuel in about two minutes, the boarding at its REALISTIC rate in under 40 minutes (measured
/// 2026-10-25, live-findings.md). The definition reads the tanks and stations every
/// <see cref="IntervalMs"/> and hands each read to <see cref="Observe"/>, which says once each:
/// <list type="bullet">
/// <item>"Fueling complete." and the fuel, when the tanks' total is within the larger of 1% and 100 lb
/// of the plan's block and moved less than <see cref="FuelSettledLbs"/> since the last read (the APU
/// burns fuel, so the tanks never read unchanged);</item>
/// <item>"Boarding complete." with the passengers, the cargo and where the ACARS weight and balance
/// comes from, when the cabin and the cargo's TOTAL are each within the larger of 0.5% and 20 lb of the
/// plan and moved less than <see cref="PayloadSettledLbs"/> since the last read (BlueBird's split
/// between the holds is not judged, ruling R43);</item>
/// <item>after <see cref="TimeoutMs"/> without both, what has not finished; then the watch ends.</item>
/// </list>
/// The first read only seeds; a group with a value unread is neither judged nor kept as the last read.
/// </summary>
public sealed class Bbs757LoadWatch
{
    /// <summary>How often the tanks and stations are read.</summary>
    public const int IntervalMs = 15_000;

    /// <summary>How long the watch waits for both: BlueBird's realistic boarding took under 40 minutes.</summary>
    public const long TimeoutMs = 60 * 60_000;

    /// <summary>The fuel counts as settled when it moved less than this since the last read.</summary>
    public const double FuelSettledLbs = 20;

    /// <summary>The cabin and the cargo count as settled when they moved less than this since the last read.</summary>
    public const double PayloadSettledLbs = 1;

    public const string StartedText =
        "Fueling and boarding started. BlueBird loads at its own pace; boarding can take half an hour.";

    /// <summary>Said instead when the taps were made but the watch could not start.</summary>
    public const string StartedUnwatchedText =
        "Fueling and boarding started. BlueBird loads at its own pace; check the fuel with F and the weight with W.";

    public const string AlreadyRunningText = "The load is already running.";

    private readonly Bbs757WeightUnits? _units;
    private double? _lastFuel, _lastPassengers, _lastCargo;

    /// <param name="loads">The targets (<see cref="Bbs757Loadsheet.Compute"/>).</param>
    /// <param name="units">The aircraft's weight units, for what is said; pounds when null.</param>
    public Bbs757LoadWatch(Bbs757Loads loads, Bbs757WeightUnits? units)
    {
        Loads = loads;
        _units = units;
    }

    /// <summary>The targets the reads are judged against.</summary>
    public Bbs757Loads Loads { get; }

    public bool FuelDone { get; private set; }
    public bool BoardingDone { get; private set; }
    public bool TimedOut { get; private set; }

    /// <summary>Nothing more to say: both done, or the hour is up.</summary>
    public bool Finished => (FuelDone && BoardingDone) || TimedOut;

    /// <summary>
    /// One read, <paramref name="elapsedMs"/> after the watch started: what to say now (the fuel's
    /// sentence before the boarding's when both finish at one read), or null.
    /// </summary>
    public string? Observe(Bbs757LoadReadBack read, long elapsedMs)
    {
        if (Finished) return null;
        var said = new List<string>();

        if (read.FuelLbs is double fuel)
        {
            double target = Loads.LeftLbs + Loads.RightLbs + Loads.CenterLbs;
            if (!FuelDone && _lastFuel is double last && Math.Abs(fuel - last) < FuelSettledLbs
                && Math.Abs(fuel - target) <= Bbs757Loadsheet.FuelTolerance(target))
            {
                FuelDone = true;
                said.Add($"Fueling complete. {Bbs757Loadsheet.FuelSentence(read, _units)}");
            }
            _lastFuel = fuel;
        }

        if (read.PassengerLbs is double cabin && read.CargoLbs is double cargo)
        {
            double cargoTarget = Loads.ForwardLbs + Loads.AftLbs;
            if (!BoardingDone && _lastPassengers is double lastCabin && _lastCargo is double lastCargo
                && Math.Abs(cabin - lastCabin) < PayloadSettledLbs && Math.Abs(cargo - lastCargo) < PayloadSettledLbs
                && Math.Abs(cabin - Loads.PassengerLbs) <= Bbs757Loadsheet.PayloadTolerance(Loads.PassengerLbs)
                && Math.Abs(cargo - cargoTarget) <= Bbs757Loadsheet.PayloadTolerance(cargoTarget))
            {
                BoardingDone = true;
                said.Add(BoardedPhrase(read));
            }
            _lastPassengers = cabin;
            _lastCargo = cargo;
        }

        if (!Finished && elapsedMs >= TimeoutMs)
        {
            TimedOut = true;
            said.Add(UnfinishedPhrase(FuelDone, BoardingDone));
        }
        return said.Count > 0 ? string.Join(" ", said) : null;
    }

    /// <summary>
    /// "Boarding complete. Passengers 21090 pounds. Cargo 3990 pounds: forward 3990, aft 0. For the ACARS
    /// weight and balance, request WGTBAL on the CDU.", with the cap before the last sentence when the
    /// plan has more passengers than the 757 seats (BlueBird puts the rest of the payload in the holds).
    /// </summary>
    private string BoardedPhrase(Bbs757LoadReadBack read)
    {
        var said = new List<string>
        {
            "Boarding complete.",
            Bbs757Loadsheet.PassengersSentence(read, _units)!,
            Bbs757Loadsheet.CargoSentence(read, _units)!,
        };
        if (Loads.PassengersCapped)
            said.Add($"The plan's {Loads.PlannedPassengers.ToString(CultureInfo.InvariantCulture)} passengers are capped at " +
                     $"the 757's {Bbs757Loadsheet.Seats.ToString(CultureInfo.InvariantCulture)} seats; the rest of the payload goes in the holds.");
        said.Add(Bbs757Loadsheet.WeightAndBalanceText);
        return string.Join(" ", said);
    }

    /// <summary>What has not finished after the hour, and the key that reads it (Output F, Output W).</summary>
    private static string UnfinishedPhrase(bool fuelDone, bool boardingDone) => (fuelDone, boardingDone) switch
    {
        (false, false) => "Fueling and boarding have not finished after an hour. Check the fuel with F and the weight with W.",
        (false, true) => "Fueling has not finished after an hour. Check the fuel with F.",
        _ => "Boarding has not finished after an hour. Check the weight with W.",
    };
}
