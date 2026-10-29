namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The A350's take-off roll call-outs ("V1", "Rotate", "V2"), the A380's (<see cref="A380TakeoffCallouts"/>) on the A350's
/// own speeds: the FMS's take-off speeds as its active flight plan holds them, read from the display reader's memory copy
/// (<see cref="Display.A350TakeoffSpeeds"/>; INI_V1, INI_VR and INI_V2 are never written in 1.2.7), said as they are
/// entered with a Ctrl+M row each, and the indicated airspeed fed per sim frame. The aircraft's own warning system calls
/// "V1" aloud too (<c>INI_V1_CALLOUT</c>, raised by its <c>update_v1()</c> at V1 from the same flight plan); MSFSBA's call
/// arrives alongside it, as on the A380.
/// </summary>
public static class A350TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "A350_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "A350_V1";
    public const string VrKey = "A350_VR";
    public const string V2Key = "A350_V2";

    /// <summary>Each speed's Ctrl+M row and the name it is said by; no sim variable stands behind them.</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> Speeds = new[]
    {
        (V1Key, "V1"), (VrKey, "VR"), (V2Key, "V2"),
    };

    /// <summary>The feed, the speeds and their Ctrl+M rule: each call is muted by its speed's row, which also mutes that
    /// speed's entry ("V1: 142 knots").</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);
}
