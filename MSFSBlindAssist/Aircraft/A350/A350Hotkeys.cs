using System.Globalization;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The A350's own hotkeys, the A380's set (FBW_A380_Hotkeys.txt): what each press drives and what each read-out says.
/// A press drives the same FCU row the panel offers, so a hotkey acts exactly as that row; a read-out says what the
/// cockpit shows in the words the A350's typed entries use ("FCU altitude 40000, managed"). Pure; numbers invariant
/// ([VAR-5]).
/// </summary>
public static class A350Hotkeys
{
    /// <summary>The panel row each press hotkey drives: the AP, A/THR, LOC and APPR pushbuttons (input Shift+A, Ctrl+O,
    /// Ctrl+J, Ctrl+L, Shift+P) and the four FCU knobs pushed or pulled (input Shift+1 to 4, Ctrl+1 to 4).</summary>
    public static readonly IReadOnlyDictionary<HotkeyAction, string> PressRows = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.ToggleAutopilot1] = "A350_FCU_AP_1",
        [HotkeyAction.ToggleAutopilot2] = "A350_FCU_AP_2",
        [HotkeyAction.ToggleAutothrust] = "A350_FCU_ATHR",
        [HotkeyAction.ToggleLocalizer] = "A350_FCU_LOC",
        [HotkeyAction.ToggleApproachMode] = "A350_FCU_APPR",
        [HotkeyAction.FCUHeadingPush] = "A350_FCU_HDG_KNOB_PUSH",
        [HotkeyAction.FCUHeadingPull] = "A350_FCU_HDG_KNOB_PULL",
        [HotkeyAction.FCUAltitudePush] = "A350_FCU_ALT_KNOB_PUSH",
        [HotkeyAction.FCUAltitudePull] = "A350_FCU_ALT_KNOB_PULL",
        [HotkeyAction.FCUSpeedPush] = "A350_FCU_SPD_KNOB_PUSH",
        [HotkeyAction.FCUSpeedPull] = "A350_FCU_SPD_KNOB_PULL",
        [HotkeyAction.FCUVSPush] = "A350_FCU_VS_KNOB_PUSH",
        [HotkeyAction.FCUVSPull] = "A350_FCU_VS_KNOB_PULL",
    };

    /// <summary>The A350's own characteristic speeds the read-outs say (Shift+4 VLS, Shift+5 the alpha speeds, Shift+6 VFE
    /// next; live in a clean climb: VLS 166, VFE 255, VMAX 340, the alpha speeds 0), streamed for them.</summary>
    public const string Vls = "INI_VLS_SPEED", VfeNext = "INI_VFE_SPEED", Vmax = "INI_VMAX_SPEED",
        AlphaProt = "INI_V_ALPHA_PROT_SPEED", AlphaMax = "INI_V_ALPHA_MAX_SPEED";

    public static readonly IReadOnlyList<string> ReadoutVars = new[]
    {
        Vls, VfeNext, Vmax, AlphaProt, AlphaMax, A350RouteDistances.DestinationVar, A350RouteDistances.TopOfDescentVar,
    };

    /// <summary>An FCU window's line (<see cref="A350FcuWindows"/>) as the Shift+S, H, A and V read-outs say it.</summary>
    public static string FcuReadout(string window, string shown) => window switch
    {
        "Speed window" => "FCU speed " + LowerFirst(shown),
        "Heading window" => shown.StartsWith("Heading ", StringComparison.Ordinal) || shown.StartsWith("Track ", StringComparison.Ordinal)
            ? "FCU " + LowerFirst(shown) : "FCU heading " + LowerFirst(shown),
        "Altitude window" => "FCU altitude " + LowerFirst(shown),
        "V/S window" => shown.StartsWith("FPA ", StringComparison.Ordinal) ? "FCU flight path angle " + shown[4..]
            : shown.StartsWith("V/S ", StringComparison.Ordinal) ? "FCU vertical speed " + shown[4..]
            : "FCU vertical speed " + LowerFirst(shown),
        _ => shown,
    };

    /// <summary>The captain's altimeter as the A380's B read-out says it: standard while the baro knob is pushed
    /// (<c>XMLVAR_Baro1_Mode</c> 3), else the setting in both units, its hectopascals floored as the cockpit shows them.</summary>
    public static string Altimeter(double mode, double millibars) =>
        Math.Abs(mode - 3) < 0.5
            ? "Altimeter standard"
            : "Altimeter: " + A350FcuWindows.Hectopascals(millibars) + ", "
              + (millibars / 33.8639).ToString("0.00", CultureInfo.InvariantCulture);

    private static readonly string[] FlapDetents = { "Up", "1", "2", "3", "Full" };

    /// <summary>The flaps handle (the A350_FLAPS row, 0 up to 4 full) in the A380's words: "Flaps Up", "Flaps 2".</summary>
    public static string Flaps(double handle)
    {
        int i = (int)Math.Round(handle);
        return "Flaps " + (i >= 0 && i < FlapDetents.Length ? FlapDetents[i] : handle.ToString("0", CultureInfo.InvariantCulture));
    }

    /// <summary>The gear lever (the A350_LDG_LEVER row: 0 up, 1 down).</summary>
    public static string Gear(double lever) => lever >= 0.5 ? "Gear down" : "Gear up";

    /// <summary>A status display's lines read aloud as sentences: each line (\r\n or \n) trimmed, joined by ". ", a
    /// line's own full stop not doubled.</summary>
    public static string Sentences(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return string.Join(" ", lines.Select((l, i) => i == lines.Count - 1 || l.EndsWith('.') ? l : l + "."));
    }

    /// <summary>A characteristic speed the A350 publishes (INI_VLS_SPEED and its kin), in whole knots; 0 means the aircraft
    /// computes none now (its alpha speeds are 0 in a clean climb, live 2026-10-28).</summary>
    public static string Speed(string name, double knots) =>
        knots > 1 ? name + " " + Math.Round(knots).ToString("0", CultureInfo.InvariantCulture) + " knots" : name + " not shown";

    /// <summary>The gross weight and its CG as the SD's permanent data line shows them ("Gross weight: 199400 KG, green",
    /// "Gross weight CG: 30.1 %, green", in the aircraft's unit), spoken in the asked unit, as the A380's W and Shift+W.</summary>
    public static string GrossWeight(IEnumerable<string> permanentData, bool pounds)
    {
        var lines = permanentData.ToList();
        var gw = Figure(lines, "Gross weight");
        if (gw is not (double value, string unit) || value <= 0) return "Gross weight not available";
        double kg = unit == "LBS" ? value / PoundsPerKilogram : value;
        string weight = pounds
            ? Math.Round(kg * PoundsPerKilogram / 100) * 100 is var lb ? lb.ToString("0", CultureInfo.InvariantCulture) + " pounds" : ""
            : Math.Round(kg / 100) * 100 is var k ? k.ToString("0", CultureInfo.InvariantCulture) + " kilograms" : "";
        string cg = Figure(lines, "Gross weight CG") is (double c, _) && c > 5 && c < 60
            ? ", center of gravity " + c.ToString("0.0", CultureInfo.InvariantCulture) + " percent MAC" : "";
        return "Gross weight " + weight + cg;
    }

    private const double PoundsPerKilogram = 2.204625;

    /// <summary>A permanent data figure ("Label: 199400 KG, green") as its number and unit, or null when it shows dashes.</summary>
    private static (double Value, string Unit)? Figure(List<string> lines, string label)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(label + ": ", StringComparison.Ordinal));
        if (line == null) return null;
        var parts = line[(label.Length + 2)..].Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? (v, parts.Length >= 2 ? parts[1] : "") : null;
    }

    /// <summary>The squawk and transponder mode as an RMP shows them ("SQUAWK : 2000 - STBY, white"), read by the X key: the
    /// A350 keeps its squawk to itself (the stock TRANSPONDER CODE reads 0).</summary>
    public static string Squawk(IEnumerable<string> rmpLines)
    {
        var line = rmpLines.FirstOrDefault(l => l.StartsWith("SQUAWK", StringComparison.Ordinal));
        if (line == null) return "Squawk not shown";
        string shown = line[(line.IndexOf(':') + 1)..];
        int colour = shown.LastIndexOf(", ", StringComparison.Ordinal);
        if (colour > 0) shown = shown[..colour];
        var parts = shown.Split('-', 2, StringSplitOptions.TrimEntries);
        string mode = parts.Length > 1 ? parts[1] switch { "STBY" => "standby", var m => m.ToLowerInvariant() } : "";
        return "Squawk " + parts[0] + (mode.Length > 0 ? ", " + mode : "");
    }

    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
