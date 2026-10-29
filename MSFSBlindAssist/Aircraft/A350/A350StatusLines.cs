using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>A status line on the panel of the row <see cref="Beside"/>: a variable read as the A380 reads it.</summary>
/// <param name="Unit">A stock sim variable's unit; null for an L:var.</param>
public sealed record A350StatusLine(string Key, string Beside, string Var, string Name, Func<double, string> Format, string? Unit = null);

/// <summary>
/// What a nudge or a lever did, as the A380 reads it, shown beside the control on its panel and never spoken (a sighted
/// pilot reads these off the SD, which tells nobody anything on its own). The rudder trim is
/// <c>RUDDER_TRIM_ACTUAL_ANGLE</c>, negative nose left (live: a left nudge read -0.25); the speed brake handle
/// <c>INI_SPOILERS_HANDLE_POSITION</c>, 0 to 1; the pitch trim the stock <c>ELEVATOR TRIM POSITION</c>, which follows
/// the A350's own (<c>ELEV_TRIM_RATIO</c> 0.4 read <c>ELEVATOR TRIM PCT</c> 40, live), positive nose up. Pure.
/// </summary>
public static class A350StatusLines
{
    public static readonly IReadOnlyList<A350StatusLine> All = new[]
    {
        new A350StatusLine("A350_STAT_RUDDER_TRIM", "A350_RUDDER_TRIM_RESET", "RUDDER_TRIM_ACTUAL_ANGLE", "Rudder trim", RudderTrim),
        new A350StatusLine("A350_STAT_SPEED_BRAKE", "A350_FCTL_SPD_BRK_LEVER", "INI_SPOILERS_HANDLE_POSITION", "Speed brake handle", Percent),
        new A350StatusLine("A350_STAT_PITCH_TRIM", "A350_PITCH_TRIM_UP", "ELEVATOR TRIM POSITION", "Pitch trim", PitchTrim, "degrees"),
        // The parking brake panel's accumulator gauges: each needle sweeps its INI_ACCU_*_PRESSURE_PERCENT, 0 to 1 of the
        // dial (the model's animation reads it times 100).
        new A350StatusLine("A350_STAT_ACCU_GREEN", "A350_PARK_BRK_ACCU_REINFLATE", "INI_ACCU_GREEN_PRESSURE_PERCENT",
            "Green brake accumulator pressure", Percent),
        new A350StatusLine("A350_STAT_ACCU_YELLOW", "A350_PARK_BRK_ACCU_REINFLATE", "INI_ACCU_YELLOW_PRESSURE_PERCENT",
            "Yellow brake accumulator pressure", Percent),
        // Each IR's alignment beside its pushbutton, as the A380 shows its ADIRUs' (parity task 8; the call-out is
        // A350IrsAlignment's). The time left waits for the sim: INI_IRSn_TIME_REMAIN's unit is unmeasured.
        new A350StatusLine("A350_STAT_IR1_ALIGNED", "A350_IRS_IR_1", "INI_IRS1_ALIGNED", "IR 1 alignment", Alignment),
        new A350StatusLine("A350_STAT_IR2_ALIGNED", "A350_IRS_IR_2", "INI_IRS2_ALIGNED", "IR 2 alignment", Alignment),
        new A350StatusLine("A350_STAT_IR3_ALIGNED", "A350_IRS_IR_3", "INI_IRS3_ALIGNED", "IR 3 alignment", Alignment),
    };

    /// <summary>An IR's alignment flag: "Aligned" or "Not aligned".</summary>
    public static string Alignment(double flag) => flag >= 0.5 ? "Aligned" : "Not aligned";

    /// <summary>A 0 to 1 travel as a whole percent: "37 percent".</summary>
    public static string Percent(double fraction) =>
        Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " percent";

    /// <summary>"5.5 degrees up" or "1.2 degrees down" (the A380's "Pitch trim (THS)" words).</summary>
    public static string PitchTrim(double degrees) =>
        Math.Abs(degrees).ToString("0.0", CultureInfo.InvariantCulture) + (degrees >= 0 ? " degrees up" : " degrees down");

    /// <summary>"Neutral", or "0.4 degrees left" (the A380's words).</summary>
    public static string RudderTrim(double degrees) =>
        Math.Abs(degrees) < 0.05
            ? "Neutral"
            : Math.Abs(degrees).ToString("0.0", CultureInfo.InvariantCulture) + (degrees < 0 ? " degrees left" : " degrees right");
}
