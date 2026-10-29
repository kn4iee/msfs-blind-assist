using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One FCU window as a status line: the A350 variables it is made of and how they read together.</summary>
public sealed class A350FcuWindow
{
    private readonly Func<Func<string, double>, string> _compose;

    internal A350FcuWindow(string name, IReadOnlyList<string> inputs, Func<Func<string, double>, string> compose,
        string panel = "FCU")
    {
        Name = name;
        Panel = panel;
        Inputs = inputs;
        _compose = compose;
    }

    public string Name { get; }

    /// <summary>The panel whose status display shows it.</summary>
    public string Panel { get; }

    /// <summary>Its MSFSBA variable key: <c>A350_FCU_</c> plus its name, upper case.</summary>
    public string Key => "A350_FCU_" + Name.ToUpperInvariant().Replace(' ', '_').Replace('/', '_');

    /// <summary>The variables it reads, A350 L:vars or sim variables (<see cref="A350FcuWindows.StockUnits"/>); the first
    /// streams under <see cref="Key"/>.</summary>
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
    private const string RollMode = "INI_ROLL_MODE";

    private const double NavRollMode = A350TypedEntry.NavRollMode;

    /// <summary>The sim variables a window or a typed field reads, with the unit each is read in; every other input is an A350 L:var.</summary>
    public static readonly IReadOnlyDictionary<string, string> StockUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["KOHLSMAN SETTING MB:1"] = "millibars",
        ["KOHLSMAN SETTING MB:2"] = "millibars",
        ["KOHLSMAN SETTING MB:3"] = "millibars",   // the ISIS's, read by its typed QNH
    };

    public static readonly IReadOnlyList<A350FcuWindow> All = new[]
    {
        // A turn while the speed is managed shows the preset for 45 s (FCU::Draw reads INI_Preset_Dial while its timer
        // runs); the WASM drops the dashes exactly then, so managed and not dashed is the preset (live in flight).
        new A350FcuWindow("Speed window", new[]
            {
                "INI_AIRSPEED_DIAL", "INI_FCU_SPD_DASHED", "INI_FCU_SPD_DOT", "INI_MANAGED_SPEED", "INI_PRESET_DIAL",
                "INI_PRESET_DIAL_IS_MACH",
            },
            v => v("INI_FCU_SPD_DASHED") < 0.5 && v("INI_MANAGED_SPEED") >= 0.5
                ? Speed(v("INI_PRESET_DIAL"), v("INI_PRESET_DIAL_IS_MACH") >= 0.5) + ", preset"
                : Window(v("INI_FCU_SPD_DASHED"), v("INI_FCU_SPD_DOT"), Speed(v("INI_AIRSPEED_DIAL"), v("INI_AIRSPEED_DIAL") < 5))),
        // In NAV (INI_ROLL_MODE 1; HDG is 2, TRK 11) a turn shows the preset heading for 45 s.
        new A350FcuWindow("Heading window", new[] { "INI_HEADING_DIAL", "INI_TRK_DIAL", TrkFpa, "INI_FCU_HDG_DASHED", "INI_FCU_HDG_DOT", RollMode },
            v => Window(v("INI_FCU_HDG_DASHED"), v("INI_FCU_HDG_DOT"),
                (v(TrkFpa) >= 0.5 ? "Track " + Degrees(v("INI_TRK_DIAL")) : "Heading " + Degrees(v("INI_HEADING_DIAL")))
                + (v("INI_FCU_HDG_DASHED") < 0.5 && Math.Abs(v(RollMode) - NavRollMode) < 0.5 ? ", preset" : ""))),
        new A350FcuWindow("Altitude window", new[] { "INI_ALTITUDE_DIAL", "INI_FCU_ALT_DOT" },
            v => Window(0, v("INI_FCU_ALT_DOT"), Whole(v("INI_ALTITUDE_DIAL")) + " feet")),
        new A350FcuWindow("V/S window", new[] { "INI_VVI_DIAL", "INI_FPA_DIAL", TrkFpa, "INI_FCU_VS_DASHED" },
            v => Window(v("INI_FCU_VS_DASHED"), 0,
                v(TrkFpa) >= 0.5 ? "FPA " + v("INI_FPA_DIAL").ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " degrees"
                    : "V/S " + Math.Round(v("INI_VVI_DIAL")).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " feet per minute")),
        Baro("Captain baro", 1, "EFIS Captain"),
        Baro("First officer baro", 2, "EFIS First Officer"),
        // The ISIS keeps its unit and STD to itself (its gauge's own state): its line is the setting the sim holds.
        new A350FcuWindow("ISIS QNH setting", new[] { "KOHLSMAN SETTING MB:3" },
            v => Whole(v("KOHLSMAN SETTING MB:3")) + " hectopascals, "
                + Inches(v("KOHLSMAN SETTING MB:3") / AltimeterPhrase.HpaPerInHg), "ISIS"),
    };

    /// <summary>
    /// A side's baro as its FCU window shows it (live 2026-10-26): STD while <c>XMLVAR_BaroN_Mode</c> is 3 (the knob
    /// pushed), else QNH, the sim's own altimeter setting in the unit <c>XMLVAR_Baro_Selector_HPA_N</c> picks (1 hPa,
    /// 0 inHg). The setting is the sim's <c>KOHLSMAN SETTING</c>: the A350 keeps no copy of its own.
    /// </summary>
    private static A350FcuWindow Baro(string name, int side, string panel)
    {
        string setting = $"KOHLSMAN SETTING MB:{side}", mode = $"XMLVAR_Baro{side}_Mode", unit = $"XMLVAR_Baro_Selector_HPA_{side}";
        string preset = A350TypedEntry.PresetVar(side), shown = preset + "_SHOW";
        return new A350FcuWindow(name, new[] { setting, mode, unit, preset, shown }, v =>
            Math.Abs(v(mode) - 3) < 0.5
                ? v(shown) >= 0.5
                    ? "STD, preset " + (v(unit) >= 0.5 ? Whole(v(preset)) + " hectopascals"
                        : (v(preset) / 100).ToString("0.00", CultureInfo.InvariantCulture) + " inches")
                    : "STD"
            : v(unit) >= 0.5 ? "QNH " + Hectopascals(v(setting)) + " hectopascals"
            : "QNH " + Inches(v(setting) / AltimeterPhrase.HpaPerInHg), panel);
    }

    private static string Window(double dashed, double dot, string value) =>
        (dashed >= 0.5 ? "Dashes" : value) + (dot >= 0.5 ? ", managed" : "");

    /// <summary>A side's QNH in hectopascals as the cockpit reads it: floored (the baro knob's tooltip, flr '%dhPa'), so
    /// 1015.81 reads 1015.</summary>
    public static string Hectopascals(double mb) => Math.Floor(mb + 1e-6).ToString("0", CultureInfo.InvariantCulture);

    private static string Speed(double v, bool mach) =>
        mach ? "Mach " + v.ToString("0.00", CultureInfo.InvariantCulture) : Whole(v) + " knots";

    internal static string Whole(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    internal static string Inches(double inches) => inches.ToString("0.00", CultureInfo.InvariantCulture) + " inches";

    /// <summary>The FCU truncates its heading: 218.9 shows 218.</summary>
    private static string Degrees(double v) => (((int)Math.Floor(v) % 360 + 360) % 360).ToString("000", CultureInfo.InvariantCulture);
}
