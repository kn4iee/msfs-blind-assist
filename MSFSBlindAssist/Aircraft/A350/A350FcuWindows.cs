using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One FCU window as a status line: the A350 variables it is made of and how they read together.</summary>
public sealed class A350FcuWindow
{
    private readonly Func<Func<string, double>, string> _compose;

    internal A350FcuWindow(string name, IReadOnlyList<string> inputs, Func<Func<string, double>, string> compose)
    {
        Name = name;
        Inputs = inputs;
        _compose = compose;
    }

    public string Name { get; }

    /// <summary>Its MSFSBA variable key: <c>A350_FCU_</c> plus its name, upper case.</summary>
    public string Key => "A350_FCU_" + Name.ToUpperInvariant().Replace(' ', '_').Replace('/', '_');

    /// <summary>The A350 L:vars it reads; the first streams under <see cref="Key"/>.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>The window as the FCU shows it, from values read by L:var name; null while an input is unread.</summary>
    public string? Compose(Func<string, double?> read)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var input in Inputs)
        {
            if (read(input) is not double v)
                return null;
            values[input] = v;
        }
        return _compose(name => values[name]);
    }
}

/// <summary>
/// The FCU's speed, heading, altitude and V/S windows, transcribed from what the FCU draw shows (live 2026-10-26):
/// one variable holds knots or Mach (Mach below 5: the A350 selects no speed under 100 kt); TRK-FPA swaps the heading
/// and vertical windows to <c>INI_TRK_DIAL</c> and <c>INI_FPA_DIAL</c>; the heading truncates and the vertical window
/// carries its sign, as drawn ("218", "+0000"); a dashed window shows dashes, and its dot
/// means managed. Pure; numbers are invariant ([VAR-5]).
/// </summary>
public static class A350FcuWindows
{
    private const string TrkFpa = "INI_TRACK_FPA_STATE";

    public static readonly IReadOnlyList<A350FcuWindow> All = new[]
    {
        new A350FcuWindow("Speed window", new[] { "INI_AIRSPEED_DIAL", "INI_FCU_SPD_DASHED", "INI_FCU_SPD_DOT" },
            v => Window(v("INI_FCU_SPD_DASHED"), v("INI_FCU_SPD_DOT"),
                v("INI_AIRSPEED_DIAL") < 5 ? "Mach " + v("INI_AIRSPEED_DIAL").ToString("0.00", CultureInfo.InvariantCulture)
                    : Whole(v("INI_AIRSPEED_DIAL")) + " knots")),
        new A350FcuWindow("Heading window", new[] { "INI_HEADING_DIAL", "INI_TRK_DIAL", TrkFpa, "INI_FCU_HDG_DASHED", "INI_FCU_HDG_DOT" },
            v => Window(v("INI_FCU_HDG_DASHED"), v("INI_FCU_HDG_DOT"),
                v(TrkFpa) >= 0.5 ? "Track " + Degrees(v("INI_TRK_DIAL")) : "Heading " + Degrees(v("INI_HEADING_DIAL")))),
        new A350FcuWindow("Altitude window", new[] { "INI_ALTITUDE_DIAL", "INI_FCU_ALT_DOT" },
            v => Window(0, v("INI_FCU_ALT_DOT"), Whole(v("INI_ALTITUDE_DIAL")) + " feet")),
        new A350FcuWindow("V/S window", new[] { "INI_VVI_DIAL", "INI_FPA_DIAL", TrkFpa, "INI_FCU_VS_DASHED" },
            v => Window(v("INI_FCU_VS_DASHED"), 0,
                v(TrkFpa) >= 0.5 ? "FPA " + v("INI_FPA_DIAL").ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " degrees"
                    : "V/S " + Math.Round(v("INI_VVI_DIAL")).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " feet per minute")),
    };

    private static string Window(double dashed, double dot, string value) =>
        (dashed >= 0.5 ? "Dashes" : value) + (dot >= 0.5 ? ", managed" : "");

    private static string Whole(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The FCU truncates its heading: 218.9 shows 218.</summary>
    private static string Degrees(double v) => (((int)Math.Floor(v) % 360 + 360) % 360).ToString("000", CultureInfo.InvariantCulture);
}
