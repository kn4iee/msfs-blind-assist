using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The mode buttons of the four MCP value windows (Ctrl+H, S, A and V), as the PMDG 737's windows
/// carry theirs: each window the buttons that go with its value, by map node, and their access keys.
/// Pure; the definition takes each button's label from the map, its state from the MCP light and its
/// press from the panel's path (BBS757Definition.Mcp.cs).
/// </summary>
public static class Bbs757McpWindowLayout
{
    private static readonly string[] Heading = { "B757_MCP_AP_HDG_SEL_SW", "B757_MCP_HOLD_1_BUTTON", "B757_MCP_LNAV_BUTTON" };
    private static readonly string[] Speed = { "B757_MCP_SPD_BUTTON", "B757_MCP_THR_BUTTON", "B757_MCP_FLCH_BUTTON", "B757_MCP_AP_SPD_MACH_SW" };
    private static readonly string[] Altitude = { "B757_MCP_HOLD_2_BUTTON", "B757_MCP_VNAV_BUTTON", "B757_MCP_FLCH_BUTTON" };
    private static readonly string[] VerticalSpeed = { "B757_MCP_VS_BUTTON" };

    /// <summary>
    /// A window's buttons by node, in order: heading HDG SEL, HDG HOLD, LNAV; speed SPD, THR, FLCH,
    /// IAS/MACH; altitude ALT HOLD, VNAV, FLCH; vertical speed V/S. None for any other action.
    /// </summary>
    public static IReadOnlyList<string> NodesFor(HotkeyAction action) => action switch
    {
        HotkeyAction.FCUSetHeading => Heading,
        HotkeyAction.FCUSetSpeed => Speed,
        HotkeyAction.FCUSetAltitude => Altitude,
        HotkeyAction.FCUSetVS => VerticalSpeed,
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// A window's button texts for these labels, in order. Each takes an access key (Alt and a letter)
    /// on the first letter of its last word: HDG SEL is Alt+S, HDG HOLD Alt+H, IAS/MACH Alt+I. Two
    /// buttons of one window that would take the same letter get none. An ampersand in a label is
    /// doubled, so it shows as itself rather than marking a key.
    /// </summary>
    public static IReadOnlyList<string> Captions(IReadOnlyList<string> labels)
    {
        var keys = labels.Select(AccessKey).ToList();
        var captions = new string[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            string label = labels[i];
            if (keys[i] is not char key || keys.Count(k => k == key) > 1)
            {
                captions[i] = Escape(label);
                continue;
            }
            int start = LastWordStart(label);
            captions[i] = $"{Escape(label[..start])}&{Escape(label[start..])}";
        }
        return captions;
    }

    /// <summary>The upper-case first letter of the label's last word, or null when it does not start with a letter.</summary>
    private static char? AccessKey(string label)
    {
        int start = LastWordStart(label);
        return start < label.Length && char.IsLetter(label[start]) ? char.ToUpperInvariant(label[start]) : null;
    }

    private static int LastWordStart(string label) => label.TrimEnd().LastIndexOf(' ') + 1;

    private static string Escape(string text) => text.Replace("&", "&&");
}
