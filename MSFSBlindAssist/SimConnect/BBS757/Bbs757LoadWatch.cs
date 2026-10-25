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
/// of the plan's block and every tank moved less than <see cref="FuelSettledLbs"/> since the last read
/// (the APU burns fuel, so the tanks never read unchanged; and BlueBird can move fuel between tanks
/// while the total holds still);</item>
/// <item>"Boarding complete." with the passengers, the cargo and where the ACARS weight and balance
/// comes from, when the cabin and the cargo's TOTAL are each within the larger of 0.5% and 20 lb of the
/// plan and moved less than <see cref="PayloadSettledLbs"/> since the last read (BlueBird's split
/// between the holds is not judged, ruling R43);</item>
/// <item>at <see cref="NotStartedMs"/>, for each part not done whose tanks or stations have not moved
/// since the first read, that BlueBird has not started it, and that part is no longer watched: fueling
/// and boarding start at once (measured), and with BBS Nest closed BlueBird ignores every tap;</item>
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

    /// <summary>The fuel counts as settled when every tank moved less than this since the last read.</summary>
    public const double FuelSettledLbs = 20;

    /// <summary>When the watch judges whether BlueBird started each part (fueling and boarding start at once, measured).</summary>
    public const long NotStartedMs = 3 * 60_000;

    /// <summary>A tank counts as moved since the first read once it changed by at least this (the APU burns far less in three minutes).</summary>
    public const double FuelMovedLbs = 100;

    /// <summary>The cabin and the cargo count as settled when they moved less than this since the last read.</summary>
    public const double PayloadSettledLbs = 1;

    public const string StartedText =
        "Fueling and boarding started. BlueBird loads at its own pace; boarding can take half an hour.";

    public const string AlreadyRunningText = "The load is already running.";

    /// <summary>
    /// What the window says once the load's presses are made: <paramref name="confirmed"/> when the EFB
    /// reported every press made, <paramref name="watching"/> when the load watch started. A run that may
    /// have pressed something is watched, never retried: what a second START BOARDING press does during a
    /// boarding is unmeasured (the final review of task 11), and the watch says within
    /// <see cref="NotStartedMs"/> if nothing started.
    /// </summary>
    public static string StartedPhrase(bool confirmed, bool watching) => (confirmed, watching) switch
    {
        (true, true) => StartedText,
        (false, true) => "The EFB did not confirm every press. BA is watching the fuel and payload, and says within three " +
                         "minutes if BlueBird has not started.",
        (true, false) => "Fueling and boarding started. BlueBird loads at its own pace; check the fuel with F and the weight with W.",
        _ => "The EFB did not confirm every press. Check the fuel with F and the weight with W.",
    };

    private readonly Bbs757WeightUnits? _units;
    private (double Left, double Right, double Center)? _firstTanks, _lastTanks;
    private double? _firstCabin, _firstCargo, _lastPassengers, _lastCargo;
    private bool _startJudged;

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

    /// <summary>Fueling had not started at <see cref="NotStartedMs"/> and is no longer watched.</summary>
    public bool FuelGaveUp { get; private set; }

    /// <summary>Boarding had not started at <see cref="NotStartedMs"/> and is no longer watched.</summary>
    public bool BoardingGaveUp { get; private set; }

    /// <summary>Nothing more to say: each part done or given up, or the hour is up.</summary>
    public bool Finished => ((FuelDone || FuelGaveUp) && (BoardingDone || BoardingGaveUp)) || TimedOut;

    /// <summary>
    /// One read, <paramref name="elapsedMs"/> after the watch started: what to say now (the fuel's
    /// sentence before the boarding's when both finish at one read), or null.
    /// </summary>
    public string? Observe(Bbs757LoadReadBack read, long elapsedMs)
    {
        if (Finished) return null;
        var said = new List<string>();

        if (Tanks(read) is { } tanks)
        {
            double fuel = tanks.Left + tanks.Right + tanks.Center;
            double target = Loads.LeftLbs + Loads.RightLbs + Loads.CenterLbs;
            if (!FuelDone && !FuelGaveUp && _lastTanks is { } last && MovedLess(tanks, last, FuelSettledLbs)
                && Math.Abs(fuel - target) <= Bbs757Loadsheet.FuelTolerance(target))
            {
                FuelDone = true;
                said.Add($"Fueling complete. {Bbs757Loadsheet.FuelSentence(read, _units)}");
            }
            _firstTanks ??= tanks;
            _lastTanks = tanks;
        }

        if (read.PassengerLbs is double cabin && read.CargoLbs is double cargo)
        {
            double cargoTarget = Loads.ForwardLbs + Loads.AftLbs;
            if (!BoardingDone && !BoardingGaveUp && _lastPassengers is double lastCabin && _lastCargo is double lastCargo
                && Math.Abs(cabin - lastCabin) < PayloadSettledLbs && Math.Abs(cargo - lastCargo) < PayloadSettledLbs
                && Math.Abs(cabin - Loads.PassengerLbs) <= Bbs757Loadsheet.PayloadTolerance(Loads.PassengerLbs)
                && Math.Abs(cargo - cargoTarget) <= Bbs757Loadsheet.PayloadTolerance(cargoTarget))
            {
                BoardingDone = true;
                said.Add(BoardedPhrase(read));
            }
            _firstCabin ??= cabin;
            _firstCargo ??= cargo;
            _lastPassengers = cabin;
            _lastCargo = cargo;
        }

        if (!_startJudged && elapsedMs >= NotStartedMs)
        {
            _startJudged = true;
            bool fuelStalled = !FuelDone && _firstTanks is { } firstTanks && _lastTanks is { } lastTanks
                && MovedLess(lastTanks, firstTanks, FuelMovedLbs);
            bool boardingStalled = !BoardingDone && _firstCabin is double firstCabin && _lastPassengers is double lastCabin
                && _firstCargo is double firstCargo && _lastCargo is double lastCargo
                && Math.Abs(lastCabin - firstCabin) < PayloadSettledLbs && Math.Abs(lastCargo - firstCargo) < PayloadSettledLbs;
            if (NotStartedPhrase(fuelStalled, boardingStalled) is { } notStarted) said.Add(notStarted);
            FuelGaveUp |= fuelStalled;
            BoardingGaveUp |= boardingStalled;
        }

        if (!Finished && elapsedMs >= TimeoutMs)
        {
            TimedOut = true;
            said.Add(UnfinishedPhrase(fuelPending: !FuelDone && !FuelGaveUp, boardingPending: !BoardingDone && !BoardingGaveUp));
        }
        return said.Count > 0 ? string.Join(" ", said) : null;
    }

    /// <summary>Each tank in pounds at the sim's weight per gallon, or null when any of the four is unread.</summary>
    private static (double Left, double Right, double Center)? Tanks(Bbs757LoadReadBack read) =>
        read.LeftGallons is double l && read.RightGallons is double r && read.CenterGallons is double c
            && read.PoundsPerGallon is double perGallon
            ? (l * perGallon, r * perGallon, c * perGallon)
            : null;

    /// <summary>Whether every tank moved less than <paramref name="lbs"/> from <paramref name="from"/>.</summary>
    private static bool MovedLess((double Left, double Right, double Center) now, (double Left, double Right, double Center) from,
        double lbs) =>
        Math.Abs(now.Left - from.Left) < lbs && Math.Abs(now.Right - from.Right) < lbs && Math.Abs(now.Center - from.Center) < lbs;

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
    private static string UnfinishedPhrase(bool fuelPending, bool boardingPending) => (fuelPending, boardingPending) switch
    {
        (true, true) => "Fueling and boarding have not finished after an hour. Check the fuel with F and the weight with W.",
        (true, false) => "Fueling has not finished after an hour. Check the fuel with F.",
        _ => "Boarding has not finished after an hour. Check the weight with W.",
    };

    /// <summary>
    /// What has not started at <see cref="NotStartedMs"/>, or null. Neither moving is the BBS Nest case
    /// (BlueBird ignores every tap without it), so only that one names it.
    /// </summary>
    private static string? NotStartedPhrase(bool fuelStalled, bool boardingStalled) => (fuelStalled, boardingStalled) switch
    {
        (true, true) => "BlueBird has not started loading. Is BBS Nest running? Check the fuel with F and the weight with W.",
        (true, false) => "BlueBird has not started fueling. Check the fuel with F.",
        (false, true) => "BlueBird has not started boarding. Check the weight with W.",
        _ => null,
    };
}
