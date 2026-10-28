namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The three Radio Management Panels (iniBuilds' RMPS group: 53 input events each, suffixed _1, _2, _3). Each RMP is a
/// pedestal panel of its own (manual p. 23: RMP 1 the captain's, RMP 2 the first officer's, RMP 3 aft), its rows named
/// as the manual names them (p. 74): the keypad, the line select keys (validate the typed frequency as STBY), the
/// activation keys (swap STBY into active), the page keys, and the audio part's microphone keys and reception knobs.
/// </summary>
public static partial class A350PanelLayout
{
    private const string RmpGroup = "RMPS";

    /// <summary>The panel an RMP control is on, by its number suffix (before a reception knob's _BUTTON).</summary>
    private static string RmpPanel(string shortId) => WithoutButton(shortId)[^1] switch
    {
        '1' => "Captain RMP",
        '2' => "First Officer RMP",
        _ => "RMP 3",
    };

    private const string ButtonSuffix = "_BUTTON";

    private static string WithoutButton(string shortId) =>
        shortId.EndsWith(ButtonSuffix, StringComparison.Ordinal) ? shortId[..^ButtonSuffix.Length] : shortId;

    /// <summary>An RMP control's spoken name from its id without the RMP_ prefix and the _n suffix. A reception knob's
    /// push-pull (its _BUTTON, INI_RMPn_m_RECEIVE_SEL) is its radio's reception: pulled out, the radio is heard.</summary>
    private static string RmpName(string shortId, string title)
    {
        bool reception = shortId.EndsWith(ButtonSuffix, StringComparison.Ordinal);
        string id = WithoutButton(shortId);
        string key = id.StartsWith("RMP_", StringComparison.Ordinal) ? id[4..^2] : id;
        if (reception && key.EndsWith("_VOL", StringComparison.Ordinal))
            return (key == "VOICE_VOL" ? "Voice" : key[..^4]) + " reception";
        if (key.Length == 1 && char.IsDigit(key[0])) return "Key " + key;
        if (key.StartsWith("LSK", StringComparison.Ordinal)) return "Line select key " + key[3..];
        if (key.StartsWith("ADK", StringComparison.Ordinal)) return "Activation key " + key[3..];
        if (key.EndsWith("_CALL", StringComparison.Ordinal))
            return (key == "VOICE_CALL" ? "Voice" : key[..^5]) + " microphone";   // iniBuilds names VOICE "VHF1" too
        if (key.EndsWith("_VOL", StringComparison.Ordinal))
            return (key == "VOICE_VOL" ? "Voice" : key[..^4]) + " volume";
        return key switch
        {
            "UP" => "Up arrow",
            "DOWN" => "Down arrow",
            "CLR" => "Clear",
            "PERIOD" => "Point",
            "LOAD" => "Load",
            "MSG_CLR" => "Message clear",
            "VHF" or "HF" or "TEL" or "SQWK" or "MENU" or "NAV" => key + " page",
            "BRT" => "Brightness",
            "RAD_NAV_MODE" => "RAD NAV",
            "INT_RAD" => "INT/RAD",
            _ => SpokenWord(string.IsNullOrWhiteSpace(title) ? key.Replace('_', ' ') : title),
        };
    }

    /// <summary>The RMP selectors' positions, as the manual's words (iniBuilds' tooltip reads "ADF2").</summary>
    private static Dictionary<double, string>? RmpPositions(string shortId) =>
        shortId.StartsWith("RMP_RAD_NAV_MODE_", StringComparison.Ordinal)
            ? new() { [0] = "ADF 1", [1] = "ADF 2", [2] = "LS", [3] = "VOR 1", [4] = "VOR 2", [5] = "MKR" }
            : shortId.StartsWith("RMP_INT_RAD_", StringComparison.Ordinal)
                ? new() { [0] = "Radio", [1] = "Off", [2] = "Interphone" }
                : null;
}
