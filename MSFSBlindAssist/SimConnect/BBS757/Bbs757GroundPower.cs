namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What the 757 EFB window's connect and disconnect ground power buttons decide and say, and what the
/// definition's ground power row says when it changes. Pure. BlueBird's Ground Services page has one GPU tile
/// (<see cref="Bbs757EfbLayout.GpuTile"/>) and it TOGGLES <see cref="ConnectedVar"/> (measured 2026-10-25,
/// session 8, and again 2026-10-26), so a button taps it only when <see cref="Refusal"/> returns null
/// ([B757-2]'s pattern) and says what came of the tap from <see cref="Result"/>. Connecting only makes the
/// ground power available: the EXT PWR button on the Electrical panel puts it on line, and a disconnect is
/// refused while it is. Every value reads as <see cref="Bbs757EfbLayout.ChocksInPlace"/> reads the chocks:
/// finite and above 0.5 is 1, finite and at most 0.5 is 0, anything else is unread.
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
    /// From just before the window's taps, how long a change of the ground power row counts as the window's
    /// own run: longer than any run can take, so the run's end shortens it.
    /// </summary>
    public const int WindowRunMaxMs = 60_000;

    /// <summary>After the window's run is over, how long a delivery still in flight counts as its echo.</summary>
    public const int WindowRunEchoMs = 8000;

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

    /// <summary>A value as a flag: true for 1, false for 0, null when it was not read or is not finite.</summary>
    private static bool? Flag(double? value) => value is double v && double.IsFinite(v) ? v > 0.5 : null;
}
