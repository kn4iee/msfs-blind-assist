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
        // The printer's paper: out of the printer until torn off (INI_PRINTER_PAPER_OUT_PERCENT 50 after a print, 0 once torn), then
        // beside the seat until put away (INI_PRINTER_SIDE_PAPER_VIS 1 to 4 picks where, 0 none; live 2026-10-30).
        new A350StatusLine("A350_STAT_PRINTER_PAPER", "A350_PRINTER_PAPER_PED", "INI_PRINTER_PAPER_OUT_PERCENT", "Paper at the printer", PrinterPaper),
        new A350StatusLine("A350_STAT_TORN_PAPER", "A350_PRINTER_PAPER_SIDE_1_CS", "INI_PRINTER_SIDE_PAPER_VIS", "Torn paper", TornPaper),
        // Each seat's place along its travel, 0 to 100 (0 fully back, 100 fully forward; 0 fully down, 100 fully up).
        new A350StatusLine("A350_STAT_SEAT_CPT_FORE_AFT", "A350_SEAT_CPT_BACK", "INI_SEAT_CPT_BKFWD_ANIMATION", "Captain seat forward by", WholePercent),
        new A350StatusLine("A350_STAT_SEAT_CPT_HEIGHT", "A350_SEAT_CPT_DOWN", "INI_SEAT_CPT_UPDOWN_ANIMATION", "Captain seat height", WholePercent),
        new A350StatusLine("A350_STAT_SEAT_FO_FORE_AFT", "A350_SEAT_FO_BACK", "INI_SEAT_FO_BKFWD_ANIMATION", "First officer seat forward by", WholePercent),
        new A350StatusLine("A350_STAT_SEAT_FO_HEIGHT", "A350_SEAT_FO_DOWN", "INI_SEAT_FO_UPDOWN_ANIMATION", "First officer seat height", WholePercent),
        // The sight gauge under each cowling (the model's ENG1_OIL_INDICATOR animation reads it).
        new A350StatusLine("A350_STAT_ENG1_OIL", "A350_EXT_COWL_LEFT", "INI_ENG1_OIL_QUANTITY", "Left engine oil level", Quarts),
        new A350StatusLine("A350_STAT_ENG2_OIL", "A350_EXT_COWL_RIGHT", "INI_ENG2_OIL_QUANTITY", "Right engine oil level", Quarts),
    };

    /// <summary>A travel already in percent: "68 percent".</summary>
    public static string WholePercent(double percent) =>
        Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " percent";

    /// <summary>"16.3 quarts", as the SD's oil quantity.</summary>
    public static string Quarts(double quantity) => quantity.ToString("0.0", CultureInfo.InvariantCulture) + " quarts";

    /// <summary>The paper out of the printer: "Hanging out" or "None".</summary>
    public static string PrinterPaper(double percent) => percent > 0 ? "Hanging out" : "None";

    /// <summary>The torn paper: "Beside the seat" or "None".</summary>
    public static string TornPaper(double position) => position > 0 ? "Beside the seat" : "None";

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
