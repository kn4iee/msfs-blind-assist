using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The value an encoder sets, shown as a status line on its panel, beside its typed field or its stepping rows.
/// The cabin altitude target steps 100 ft and the cabin V/S target 50 ft/min a click (CabPressSystem's handlers,
/// WASM 1.2.7; measured live 2026-10-26). Pure.
/// </summary>
public static class A350Readouts
{
    /// <summary>The unit an encoder's value is in, by map control id; an encoder not listed reads as a bare number.</summary>
    private static readonly Dictionary<string, string> Units = new(StringComparer.Ordinal)
    {
        ["AIRLINER_CABIN_PRESS_ALTITUDE_TARGET"] = "feet",
        ["AIRLINER_CABIN_PRESS_VS_TARGET"] = "feet per minute",
    };

    /// <summary>Encoders whose map state variable is not their value: the baro knobs' is the hPa/inHg flag, and their
    /// value is the side's baro line (<see cref="A350FcuWindows"/>); DU MASTER BRT's (<c>INI_CKPT_LT_DU_MASTER_BRT</c>, its
    /// tooltip's) is kept by nothing, so it always reads 0 though its steps work.</summary>
    private static readonly HashSet<string> NoValueLine = new(StringComparer.Ordinal)
    {
        "AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT", "AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_RIGHT",
        "AIRLINER_CKPT_LT_DU_MASTER_BRT",
    };

    /// <summary>Whether an encoder gets a value line of its own.</summary>
    public static bool HasValueLine(A350Control control) => control.StateVar is { Length: > 0 } && !NoValueLine.Contains(control.Id);

    /// <summary>The value line's MSFSBA key: <c>A350_VAL_</c> plus the control's id after <c>AIRLINER_</c>.</summary>
    public static string KeyFor(A350Control control) =>
        "A350_VAL_" + A350PanelLayout.Short(control.Id);

    /// <summary>The value as the line reads it: whole, invariant digits, its unit ("2200 feet").</summary>
    public static string Format(A350Control control, double value)
    {
        string number = Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        return Units.TryGetValue(control.Id, out var unit) ? $"{number} {unit}" : number;
    }
}
