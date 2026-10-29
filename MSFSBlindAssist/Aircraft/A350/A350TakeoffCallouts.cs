namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The A350's take-off roll call-outs ("V1", "Rotate", "V2"), the A380's (<see cref="A380TakeoffCallouts"/>) on the A350's
/// own variables: the FMS's V-speeds <c>INI_V1</c>, <c>INI_VR</c>, <c>INI_V2</c> (its var table; knots, 0 before PERF has
/// them, the A350 on the stand 2026-10-26), announced as they are entered with a Ctrl+M row each, and the indicated
/// airspeed fed per sim frame. The aircraft's own warning system can call "V1" aloud too (<c>INI_V1_CALLOUT</c>);
/// MSFSBA's call arrives alongside it, as on the A380. The speeds' values after PERF are still to see in the sim.
/// </summary>
public static class A350TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "A350_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "A350_V1";
    public const string VrKey = "A350_VR";
    public const string V2Key = "A350_V2";

    /// <summary>Each speed's row: its key, the FMS's variable, the name it is announced by.</summary>
    public static readonly IReadOnlyList<(string Key, string Var, string Name)> Speeds = new[]
    {
        (V1Key, "INI_V1", "V1"), (VrKey, "INI_VR", "VR"), (V2Key, "INI_V2", "V2"),
    };

    /// <summary>The feed, the speeds and their Ctrl+M rule: each call is muted by its speed's row, which also mutes that
    /// speed's set-announce ("V1: 142 knots").</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);
}
