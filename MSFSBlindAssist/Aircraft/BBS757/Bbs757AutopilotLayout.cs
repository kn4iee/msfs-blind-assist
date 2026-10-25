namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The "757 Autopilot" window's buttons, in order: the three CMD buttons, the autothrottle arm and
/// flight director switches, then the mode buttons in the MCP's own left-to-right order. Each names
/// its map node; a light button's state is its MCP light (<see cref="Bbs757McpLights"/>), a switch's
/// is its map position. Pure: the window takes the labels, the states and the presses from the
/// definition (BBS757Definition.Autopilot.cs).
/// </summary>
public static class Bbs757AutopilotLayout
{
    /// <summary>A/T ARM: a toggle on <c>L:AT_ARM_SWITCH</c> (0 On, 10 Off, the map's positions).</summary>
    public const string AutothrottleArmNode = "B757_MCP_SW_AP_throttle";
    public const string CaptainFdNode = "B757_MCP_SW_FD_Cap";
    public const string FirstOfficerFdNode = "B757_MCP_SW_FD_FO";

    /// <summary>The window's buttons by node, in order.</summary>
    public static IReadOnlyList<string> Nodes { get; } = new[]
    {
        "B757_MCP_CMD_1_BUTTON", "B757_MCP_CMD_2_BUTTON", "B757_MCP_CMD_3_BUTTON",
        AutothrottleArmNode, CaptainFdNode, FirstOfficerFdNode,
        "B757_MCP_THR_BUTTON", "B757_MCP_SPD_BUTTON",
        "B757_MCP_LNAV_BUTTON", "B757_MCP_VNAV_BUTTON", "B757_MCP_FLCH_BUTTON",
        "B757_MCP_HOLD_1_BUTTON", "B757_MCP_HOLD_2_BUTTON", "B757_MCP_VS_BUTTON",
        "B757_MCP_LOC_BUTTON", "B757_MCP_APP_BUTTON",
    };

    /// <summary>The switches among them: their state is not a light, so the window asks the sim for it while shown.</summary>
    public static IReadOnlyList<string> SwitchNodes { get; } = new[] { AutothrottleArmNode, CaptainFdNode, FirstOfficerFdNode };

    /// <summary>
    /// A switch's state from its read value: the name of the map position it is at, or "unavailable"
    /// when it was not read or is at none of them.
    /// </summary>
    public static string SwitchState(Bbs757Control control, double? value)
    {
        if (value is not double v || !double.IsFinite(v)) return "unavailable";
        int i = Bbs757Rpn.PositionIndex(control, v);
        return i >= 0 ? control.Positions[i].Label : "unavailable";
    }

    /// <summary>
    /// The position a press of a two-position switch moves it to: the other one; null when the value
    /// is not one of its two positions (a toggle's press flips it, so nothing is pressed then).
    /// </summary>
    public static double? FlipTarget(Bbs757Control control, double? value)
    {
        if (control.Positions.Count != 2 || value is not double v || !double.IsFinite(v)) return null;
        int i = Bbs757Rpn.PositionIndex(control, v);
        return i < 0 ? null : control.Positions[1 - i].Value;
    }
}
