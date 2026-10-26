using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What the 757 EFB window's connect and disconnect ground power buttons decide and say, and what the
/// definition's ground power row says when it changes. Pure. BlueBird's Ground Services page has one GPU tile
/// (<see cref="Bbs757EfbLayout.GpuTile"/>) and it TOGGLES <see cref="ConnectedVar"/> (measured 2026-10-25,
/// session 8, and again 2026-10-26), so a button taps it only when <see cref="Refusal"/> returns null
/// ([B757-2]'s pattern) and says what came of the tap from <see cref="Result"/>. Connecting only makes the
/// ground power available: the EXT PWR button on the Electrical panel puts it on line, and a disconnect is
/// refused while it is. <see cref="SwitchAsync"/> is the buttons' whole flow, over delegates
/// (<see cref="Bbs757GroundPowerIo"/>) so a test can stand a fake tablet and a fake clock behind it: the window
/// only wires it and speaks its outcome. Every value reads as <see cref="Bbs757EfbLayout.ChocksInPlace"/> reads
/// the chocks: finite and above 0.5 is 1, finite and at most 0.5 is 0, anything else is unread.
/// </summary>
public static class Bbs757GroundPower
{
    /// <summary>BlueBird's ground power connection (0 not connected, 1 connected, measured): the GPU tile toggles it.</summary>
    public const string ConnectedVar = "BBS_757_GPU_AC_PWR_CONNECTED";

    /// <summary>The overhead EXT PWR button's ON light (measured): 1 when external power is on line.</summary>
    public const string ExtPwrOnVar = "B757_OVHD_EXT_PWR_BUTTON_ON";

    /// <summary>
    /// The window's own key for <see cref="ExtPwrOnVar"/>, read on request just before a disconnect. It is
    /// not the key of the announced EXT PWR row on the same L:var: two streaming keys on one name break
    /// the batch ([VAR-7]), so this one never streams.
    /// </summary>
    public const string ExtPwrOnKey = "BBS757_EFB_EXT_PWR_ON";

    /// <summary>
    /// From just before the window's taps, the longest a change of the ground power row counts as the window's
    /// own run: longer than any run can take, so it matters only for a run that never settles. A run that ends
    /// ends the echo at once (the definition's <c>SettleGroundPowerEcho</c>).
    /// </summary>
    public const int WindowRunMaxMs = 60_000;

    /// <summary>
    /// Null when the GPU tile may be tapped to <paramref name="connect"/> or disconnect the ground power;
    /// otherwise what the window says instead, with nothing tapped. <paramref name="connected"/> is
    /// <see cref="ConnectedVar"/> and <paramref name="extPwrOn"/> is <see cref="ExtPwrOnVar"/>, each read just
    /// before the press (null when it was not read). The tile toggles, so the first of these that applies
    /// wins: the connection unread; a connect while connected; a disconnect while disconnected; a disconnect
    /// while external power is on line, told to take it off first with the APU or the engines powering the
    /// aircraft; a disconnect with external power unread. A connect never looks at <paramref name="extPwrOn"/>.
    /// </summary>
    public static string? Refusal(bool connect, double? connected, double? extPwrOn)
    {
        if (Flag(connected) is not bool now) return "The ground power cannot be read, so nothing was tapped.";
        if (now == connect) return connect ? "Ground power is already connected." : "Ground power is already disconnected.";
        if (connect) return null;
        return Flag(extPwrOn) switch
        {
            true => "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or " +
                    "the engines powering the aircraft, then try again.",
            null => "External power cannot be read, so nothing was tapped.",
            false => null,
        };
    }

    /// <summary>
    /// What the window says once its taps are done, from the connection read after them
    /// (<paramref name="after"/>; <paramref name="before"/>, the read before them, does not change the
    /// verdict): the wish met ("Ground power connected." with the reminder that EXT PWR puts it on line, or
    /// "Ground power disconnected."), "BlueBird did not connect the ground power." / "BlueBird did not
    /// disconnect the ground power." when it reads the other way, "The ground power cannot be read now."
    /// when it was not read.
    /// </summary>
    public static string Result(bool connect, double? before, double? after) => Flag(after) switch
    {
        null => "The ground power cannot be read now.",
        bool now when now == connect => connect
            ? "Ground power connected. Press EXT PWR on the Electrical panel to put it on line."
            : "Ground power disconnected.",
        _ => connect ? "BlueBird did not connect the ground power." : "BlueBird did not disconnect the ground power.",
    };

    /// <summary>
    /// What the ground power row says when <see cref="ConnectedVar"/> changes to <paramref name="v"/>:
    /// "Ground power connected" or "Ground power disconnected"; null when it is not finite (NaN or infinity).
    /// </summary>
    public static string? ChangePhrase(double v) => Flag(v) switch
    {
        true => "Ground power connected",
        false => "Ground power disconnected",
        null => null,
    };

    /// <summary>
    /// After the taps, how long the connection is read for the wish, counted from the first read-back: the
    /// read-back ends at the first read of the wish, or when this much time has passed.
    /// </summary>
    public const int ReadBackMs = 6000;

    /// <summary>The wait between one read-back of the connection and the next.</summary>
    public const int ReadBackPollMs = 500;

    /// <summary>
    /// Connect ground power / Disconnect ground power (<paramref name="connect"/>): the window's whole flow, run on
    /// the UI thread. The GPU tile TOGGLES <see cref="ConnectedVar"/>, so ([B757-2]'s pattern) the connection is
    /// read fresh first (<see cref="Bbs757SharedKeys.GpuConnected"/>), and for a disconnect that reads connected,
    /// external power too (<see cref="ExtPwrOnKey"/>; a connect never reads it). Nothing is tapped, and no echo
    /// opened, when <see cref="Refusal"/> has something to say: the outcome is
    /// <see cref="Bbs757GroundPowerOutcomeKind.Already"/> when the connection already reads the wish, otherwise
    /// <see cref="Bbs757GroundPowerOutcomeKind.Refused"/> (unread, or a disconnect while external power is on line
    /// or unread). Otherwise the definition's echo opens, the tile is tapped once
    /// (<see cref="Bbs757EfbLayout.ToggleGroundPower"/>), and the connection is read right away, then every
    /// <see cref="ReadBackPollMs"/>, until it reads the wish or <see cref="ReadBackMs"/> have passed since the first
    /// read-back. The outcome is <see cref="Result"/> of the last read. A run that tapped nothing (it did not report
    /// finishing, and may not have clicked) is a <see cref="Bbs757GroundPowerOutcomeKind.RunProblem"/>, with no
    /// read-back: trying again is safe. A run that started and did not report finishing is judged from the
    /// variable all the same and is never run again. The echo ends in every case a run was started, even when
    /// it throws (a cancellation propagates), with the last connection read after the run (null when none).
    /// Never <c>ConfigureAwait(false)</c>: the definition's tracker that <see cref="Bbs757GroundPowerIo.ArmEcho"/>
    /// and <see cref="Bbs757GroundPowerIo.SettleEcho"/> reach is touched only on the UI thread.
    /// </summary>
    public static async Task<Bbs757GroundPowerOutcome> SwitchAsync(bool connect, Bbs757GroundPowerIo io, CancellationToken ct)
    {
        double? connected = await io.Read(Bbs757SharedKeys.GpuConnected, ct);
        double? extPwrOn = !connect && Flag(connected) == true ? await io.Read(ExtPwrOnKey, ct) : null;

        if (Refusal(connect, connected, extPwrOn) is { } refusal)
        {
            var kind = Flag(connected) == connect ? Bbs757GroundPowerOutcomeKind.Already : Bbs757GroundPowerOutcomeKind.Refused;
            return new Bbs757GroundPowerOutcome(kind, refusal);
        }

        io.ArmEcho();
        double? last = null;
        try
        {
            var (run, mayHaveClicked) = await io.Run(Bbs757EfbLayout.ToggleGroundPower(), ct);
            if (run != Bbs757EfbRunResult.Done && !mayHaveClicked)
                return new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.RunProblem, null, run);

            // Judged from the variable, whatever the run reported: read now, then every poll, to the deadline.
            long until = io.NowMs() + ReadBackMs;
            while (true)
            {
                last = await io.Read(Bbs757SharedKeys.GpuConnected, ct);
                if (Flag(last) == connect || io.NowMs() >= until) break;
                await io.Delay(ReadBackPollMs, ct);
            }
            return new Bbs757GroundPowerOutcome(Bbs757GroundPowerOutcomeKind.Result, Result(connect, connected, last));
        }
        finally
        {
            io.SettleEcho(last);
        }
    }

    /// <summary>A value as a flag: true for 1, false for 0, null when it was not read or is not finite.</summary>
    private static bool? Flag(double? value) => value is double v && double.IsFinite(v) ? v > 0.5 : null;
}

/// <summary>What <see cref="Bbs757GroundPower.SwitchAsync"/> came to, for the window to say.</summary>
public enum Bbs757GroundPowerOutcomeKind
{
    /// <summary>Nothing was tapped, and the outcome's text says why: said at once.</summary>
    Refused,
    /// <summary>The connection already reads the wish: nothing was tapped.</summary>
    Already,
    /// <summary>The run tapped nothing; the outcome's <c>Run</c> says why. Trying again is safe.</summary>
    RunProblem,
    /// <summary>The tile was tapped, or may have been: what the variable read afterward.</summary>
    Result,
}

/// <summary>
/// What <see cref="Bbs757GroundPower.SwitchAsync"/> came to: <paramref name="Text"/> is what the window says, null
/// for a <see cref="Bbs757GroundPowerOutcomeKind.RunProblem"/>, which the window says from <paramref name="Run"/>
/// (<see cref="Bbs757EfbRunResult.Done"/> for every other kind).
/// </summary>
public sealed record Bbs757GroundPowerOutcome(
    Bbs757GroundPowerOutcomeKind Kind, string? Text, Bbs757EfbRunResult Run = Bbs757EfbRunResult.Done);

/// <summary>
/// What <see cref="Bbs757GroundPower.SwitchAsync"/> needs from the app, as delegates, so the flow stays pure and a
/// test can stand a fake tablet, a fake variable and a fake clock behind it (as <c>McpIo</c> does for the MCP set
/// loops). The window builds the real one from its own pieces. Every delegate is called on the UI thread.
/// </summary>
public sealed class Bbs757GroundPowerIo
{
    /// <summary>A registered variable read fresh; null when it cannot be read.</summary>
    public required Func<string, CancellationToken, Task<double?>> Read { get; init; }

    /// <summary>
    /// One click sequence on the tablet, and whether it may have clicked (the client's
    /// <c>LastRunMayHaveClicked</c>): its start went out and was not refused.
    /// </summary>
    public required Func<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>, CancellationToken,
        Task<(Bbs757EfbRunResult Result, bool MayHaveClicked)>> Run { get; init; }

    /// <summary>An awaitable wait.</summary>
    public required Func<int, CancellationToken, Task> Delay { get; init; }

    /// <summary>The clock in milliseconds, for the read-back's deadline.</summary>
    public required Func<long> NowMs { get; init; }

    /// <summary>Opens the definition's echo window, right before the taps (<c>BBS757Definition.ArmGroundPowerEcho</c>).</summary>
    public required Action ArmEcho { get; init; }

    /// <summary>
    /// Ends it when the run is over, with the last connection read after the taps, null when none
    /// (<c>BBS757Definition.SettleGroundPowerEcho</c>).
    /// </summary>
    public required Action<double?> SettleEcho { get; init; }
}
