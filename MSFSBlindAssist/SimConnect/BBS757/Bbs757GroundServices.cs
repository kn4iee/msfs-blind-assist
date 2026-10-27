namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// One ground service tile on BlueBird's Ground Services page: its key (the tablet's own word, what the
/// page reader names the tile by), the pilot's name, where it is (tablet pixels, measured on the
/// 2026-10-26 captures through the calibrated pop-out, plan 11 Task 1), the BlueBird L:var that holds
/// its state (read through the EFB agent's readVars, no SimConnect definition), and the words for its
/// two states.
/// </summary>
public sealed record Bbs757GroundService(string Key, string Name, EfbPoint Tile, string StateVar, string OnWord, string OffWord);

/// <summary>
/// What the 757 EFB window's ground service tiles decide and say (plan 11 Task 2), and what the
/// definition's service rows say when they change (Task 5). Pure, and shaped like
/// <see cref="Bbs757GroundPower"/> on purpose: every tile TOGGLES its state variable, so ([B757-2]'s
/// pattern) the state is read first and the tile tapped only when it differs from the wish; an
/// unread state taps nothing; the result is judged from the variable read back, so a tile BlueBird
/// moved can only ever read back "BlueBird did not …", never a false success. The GPU tile is plan 07's
/// (<see cref="Bbs757GroundPower"/>) and is not in this list.
/// </summary>
public static class Bbs757GroundServices
{
    /// <summary>
    /// The tiles. Positions are the centres of the tiles' boxes as the page reader finds them
    /// (Bbs757EfbScreen, calibrated: a marker drawn at a tablet point is captured within 2 px of it).
    /// The state variables are the ones the package survey ties to each tile
    /// (docs/b757/evidence/wasm-findings.md section 3, "Doors and ground services"); each must be proven
    /// by a reversible tap in sim (plan 11 Task 1) before the window offers it: see
    /// <see cref="Proven"/>.
    /// </summary>
    public static readonly IReadOnlyList<Bbs757GroundService> All = new[]
    {
        new Bbs757GroundService("FWD STAIR", "Forward stairs", new EfbPoint(620, 275), "BBS757_vis_stairsfwd", "attached", "removed"),
        new Bbs757GroundService("AFT STAIRS", "Aft stairs", new EfbPoint(645, 744), "BBS757_vis_stairsaft", "attached", "removed"),
        new Bbs757GroundService("FUEL TRUCK", "Fuel truck", new EfbPoint(1230, 597), "BBS757_vis_fueltruck", "called", "removed"),
        new Bbs757GroundService("CATERING", "Catering", new EfbPoint(1009, 744), "BBS757_vis_catertruck", "called", "removed"),
        new Bbs757GroundService("GROUND A/C", "Ground air conditioning", new EfbPoint(410, 371), "BBS757_vis_aircon", "connected", "disconnected"),
        new Bbs757GroundService("CARGO SVC", "Cargo service", new EfbPoint(1009, 371), "BBS757_vis_baggage1", "called", "removed"),
        new Bbs757GroundService("CHOCKS", "Chocks", new EfbPoint(410, 535), Bbs757EfbLayout.ChocksVar, "set", "removed"),
    };

    /// <summary>
    /// The tiles whose variable a reversible tap in sim has shown to follow the tile (plan 11 Task 1).
    /// The window offers only these as switches; any other tile it still shows as a button with the
    /// state the tablet draws (its colour and words), tapped only on the pilot's press, and judged by
    /// reading the page again. CHOCKS was measured 2026-10-25 (Bbs757EfbLayout.ChocksVar).
    /// </summary>
    public static readonly HashSet<string> Proven = new(StringComparer.Ordinal) { "CHOCKS", "AFT STAIRS", "FUEL TRUCK", "CATERING", "CARGO SVC" };

    /// <summary>
    /// The services with an announced row of their own on the definition (Task 5): the proven ones but
    /// CHOCKS, whose variable is already the map's Status row (a second key on one L:var breaks the
    /// batch, [VAR-7]; announcing it is the map's, handed to plan 16).
    /// </summary>
    public static IReadOnlyList<Bbs757GroundService> Announced =>
        All.Where(s => Proven.Contains(s.Key) && s.Key != "CHOCKS").ToList();

    /// <summary>The service whose tile the page reader names <paramref name="tileWords"/> ("FUEL TRUCK OFF"
    /// is the FUEL TRUCK tile), or null.</summary>
    public static Bbs757GroundService? ForTile(string tileWords)
    {
        string up = (tileWords ?? "").ToUpperInvariant().Trim();
        return All.FirstOrDefault(s => up == s.Key || up.StartsWith(s.Key + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Null when the tile may be tapped to make the service <paramref name="wantOn"/>; otherwise what the
    /// window says instead, with nothing tapped: the state unread; already as wished.
    /// </summary>
    public static string? Refusal(Bbs757GroundService s, bool wantOn, double? state)
    {
        if (Flag(state) is not bool now) return $"The {s.Name.ToLowerInvariant()} state cannot be read, so nothing was tapped.";
        if (now == wantOn) return $"{s.Name} already {(wantOn ? s.OnWord : s.OffWord)}.";
        return null;
    }

    /// <summary>What the window says after the tap, from the state read back: the wish met ("Fuel truck
    /// called."), "BlueBird did not call the fuel truck." when it reads the other way, or "The fuel truck
    /// state cannot be read now." when it was not read.</summary>
    public static string Result(Bbs757GroundService s, bool wantOn, double? before, double? after) => Flag(after) switch
    {
        null => $"The {s.Name.ToLowerInvariant()} state cannot be read now.",
        bool now when now == wantOn => $"{s.Name} {(wantOn ? s.OnWord : s.OffWord)}.",
        _ => $"BlueBird did not change the {s.Name.ToLowerInvariant()}.",
    };

    /// <summary>What a service row says when its variable changes to <paramref name="v"/> ("Fuel truck
    /// called"); null when it is not finite.</summary>
    public static string? ChangePhrase(Bbs757GroundService s, double v) => Flag(v) switch
    {
        true => $"{s.Name} {s.OnWord}",
        false => $"{s.Name} {s.OffWord}",
        null => null,
    };

    /// <summary>
    /// Null when REMOVE ALL may be tapped; otherwise why not. It removes the GPU with everything else, so
    /// it is refused while ground power is on line (<paramref name="extPwrOn"/>, plan 07's
    /// <see cref="Bbs757GroundPower.ExtPwrOnVar"/>): the aircraft would lose power. An unread external
    /// power refuses too.
    /// </summary>
    public static string? RemoveAllRefusal(double? extPwrOn) => Flag(extPwrOn) switch
    {
        true => "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or the engines " +
                "powering the aircraft, then remove all ground services.",
        null => "External power cannot be read, so nothing was tapped.",
        false => null,
    };

    /// <summary>
    /// A door's state in words from its BlueBird variable: "Door L1 open" at 90 % or more of
    /// <paramref name="max"/> (passenger doors 0-100, cargo doors 0-200, measured by the package survey),
    /// "Door L1 closed" at 5 % or less, null in between (moving) or when not finite.
    /// </summary>
    public static string? DoorPhrase(string door, double position, double max)
    {
        if (!double.IsFinite(position) || max <= 0) return null;
        double f = position / max;
        if (f >= 0.9) return $"Door {door} open";
        if (f <= 0.05) return $"Door {door} closed";
        return null;
    }

    /// <summary>After the tap, how long the state is read for the wish, and the wait between reads.</summary>
    public const int ReadBackMs = 6000, ReadBackPollMs = 500;

    /// <summary>
    /// A tile's whole flow, over delegates (<see cref="Bbs757GroundServiceIo"/>) so a test can stand a fake
    /// tablet, variable and clock behind it; plan 07's <see cref="Bbs757GroundPower.SwitchAsync"/> line for
    /// line. Read first; refused (nothing tapped, no echo) when <see cref="Refusal"/> says so; otherwise the
    /// echo opens, the tile is tapped once, and the state is read back every
    /// <see cref="ReadBackPollMs"/> until it reads the wish or <see cref="ReadBackMs"/> passed. A run that
    /// tapped nothing is a RunProblem (safe to retry); one that may have clicked is judged from the variable.
    /// The echo ends in every case a run was started.
    /// </summary>
    public static async Task<Bbs757GroundPowerOutcome> SwitchAsync(Bbs757GroundService s, bool wantOn, Bbs757GroundServiceIo io, CancellationToken ct)
    {
        double? before = await io.ReadState(s.StateVar, ct);
        if (Refusal(s, wantOn, before) is { } refusal)
        {
            var kind = Flag(before) == wantOn ? Bbs757GroundPowerOutcomeKind.Already : Bbs757GroundPowerOutcomeKind.Refused;
            return new Bbs757GroundPowerOutcome(kind, refusal);
        }

        io.ArmEcho(s);
        double? last = null;
        try
        {
            var (run, mayHaveClicked) = await io.Run(new[] { (s.Tile, 400) }, ct);
            if (run != Bbs757EfbRunResult.Done && !mayHaveClicked)
                return new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.RunProblem, null, run);
            long until = io.NowMs() + ReadBackMs;
            while (true)
            {
                last = await io.ReadState(s.StateVar, ct);
                if (Flag(last) == wantOn || io.NowMs() >= until) break;
                await io.Delay(ReadBackPollMs, ct);
            }
            return new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, Result(s, wantOn, before, last));
        }
        finally
        {
            io.SettleEcho(s, last);
        }
    }

    /// <summary>A value as a flag: true above 0.5, false at or below, null when unread or not finite.</summary>
    internal static bool? Flag(double? value) => value is double v && double.IsFinite(v) ? v > 0.5 : null;
}

/// <summary>What <see cref="Bbs757GroundServices.SwitchAsync"/> needs from the app, as delegates.</summary>
public sealed class Bbs757GroundServiceIo
{
    /// <summary>A BlueBird L:var read now (the EFB agent's readVars); null when it cannot be read.</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadState { get; init; }

    /// <summary>One click sequence on the tablet, and whether it may have clicked.</summary>
    public required Func<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>, CancellationToken,
        Task<(Bbs757EfbRunResult Result, bool MayHaveClicked)>> Run { get; init; }

    public required Func<int, CancellationToken, Task> Delay { get; init; }

    public required Func<long> NowMs { get; init; }

    /// <summary>Opens the definition's echo for the service's row right before the tap (Task 5).</summary>
    public required Action<Bbs757GroundService> ArmEcho { get; init; }

    /// <summary>Ends it with the last state read after the tap, null when none.</summary>
    public required Action<Bbs757GroundService, double?> SettleEcho { get; init; }
}
