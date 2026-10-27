using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>A status line on the panel of the row <see cref="Beside"/>: a variable read as the A380 reads it.</summary>
public sealed record A350StatusLine(string Key, string Beside, string Var, string Name, Func<double, string> Format);

/// <summary>
/// What a nudge or a lever did, as the A380 reads it, shown beside the control on its panel and never spoken (a sighted
/// pilot reads these off the SD, which tells nobody anything on its own). The rudder trim is
/// <c>RUDDER_TRIM_ACTUAL_ANGLE</c>, negative nose left (live: a left nudge read -0.25); the speed brake handle
/// <c>INI_SPOILERS_HANDLE_POSITION</c>, 0 to 1. Pure.
/// </summary>
public static class A350StatusLines
{
    public static readonly IReadOnlyList<A350StatusLine> All = new[]
    {
        new A350StatusLine("A350_STAT_RUDDER_TRIM", "A350_RUDDER_TRIM_RESET", "RUDDER_TRIM_ACTUAL_ANGLE", "Rudder trim", RudderTrim),
        new A350StatusLine("A350_STAT_SPEED_BRAKE", "A350_FCTL_SPD_BRK_LEVER", "INI_SPOILERS_HANDLE_POSITION", "Speed brake handle",
            v => Math.Round(v * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " percent"),
    };

    /// <summary>"Neutral", or "0.4 degrees left" (the A380's words).</summary>
    public static string RudderTrim(double degrees) =>
        Math.Abs(degrees) < 0.05
            ? "Neutral"
            : Math.Abs(degrees).ToString("0.0", CultureInfo.InvariantCulture) + (degrees < 0 ? " degrees left" : " degrees right");
}
