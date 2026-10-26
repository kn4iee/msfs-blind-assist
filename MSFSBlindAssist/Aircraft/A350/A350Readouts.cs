using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The value an encoder sets, shown as a status line on its panel: an encoder row only steps it, so without the line
/// the pilot could not tell where it is. Live 2026-10-26: the cabin altitude target steps 100 ft and the cabin V/S
/// target 100 ft/min per click (in manual mode). Pure.
/// </summary>
public static class A350Readouts
{
    /// <summary>The unit an encoder's value is in, by map control id; an encoder not listed reads as a bare number.</summary>
    private static readonly Dictionary<string, string> Units = new(StringComparer.Ordinal)
    {
        ["AIRLINER_CABIN_PRESS_ALTITUDE_TARGET"] = "feet",
        ["AIRLINER_CABIN_PRESS_VS_TARGET"] = "feet per minute",
    };

    /// <summary>The value line's MSFSBA key: <c>A350_VAL_</c> plus the control's id after <c>AIRLINER_</c>.</summary>
    public static string KeyFor(A350Control control) =>
        "A350_VAL_" + (control.Id.StartsWith("AIRLINER_", StringComparison.Ordinal) ? control.Id.Substring(9) : control.Id);

    /// <summary>The value as the line reads it: whole, invariant digits, its unit ("2200 feet").</summary>
    public static string Format(A350Control control, double value)
    {
        string number = Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        return Units.TryGetValue(control.Id, out var unit) ? $"{number} {unit}" : number;
    }
}
