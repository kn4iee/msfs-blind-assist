using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The only way a tablet tree reaches a fixture file: the SimBrief ID, the Hoppie logon code, the SayIntentions
/// ACARS key (saved, and in the AIRAC page's text boxes), the flight plan data (the Operating FP's whole text among it) and
/// PERF calc's text boxes are replaced by made-up values. Works by node NAME only (and a node's kind, never its text), on
/// every node, a drop-down's rows included.
/// </summary>
internal static class Bbs757EfbTreeScrub
{
    internal const string MadeUpSimbriefId = "123456";
    internal const string MadeUpCode = "MADEUP";
    internal const string EmptyCode = "------";

    private const string Zero = "0";

    /// <summary>The made-up flight's Operating FP: a few lines in the place of BlueBird's whole OFP text (91,869 characters measured).</summary>
    internal const string MadeUpOperatingFp =
        "MSF1 KSEA-KPDX 01JAN26\n" +
        "KSEA/16L DCT BTG DCT KPDX/28R\n" +
        "WPT     FREQ   TRK  DIST  ETE\n" +
        "BTG     116.60 186   112  0:21\n" +
        "KPDX           103    11  0:04";

    /// <summary>PERF calc's text boxes, by name (measured 2026-11-07), and their made-up values; any other PERF calc text box reads
    /// <see cref="Zero"/>.</summary>
    private static readonly Dictionary<string, string> MadeUpPerfCalcBoxes = new(StringComparer.Ordinal)
    {
        ["planning_perf_calc_airport_input"] = "KSEA",
        ["planning_perf_calc_runway_length_input"] = "11900",
        ["planning_perf_calc_weight"] = "180000",
        ["planning_perf_calc_wind"] = "160/05",
        ["planning_perf_calc_temp"] = "15",
        ["planning_perf_calc_press"] = "30.01",
    };

    // The made-up flight, by the tablet's own value node names (measured on the ACARS dashboard, 2026-11-06).
    private static readonly Dictionary<string, string> MadeUpFlightValues = new(StringComparer.Ordinal)
    {
        ["planning_acars_flight_info_value_0"] = "MSF1",
        ["planning_acars_flight_info_value_1"] = "MSF1",
        ["planning_acars_flight_info_value_2"] = "KSEA / SEA",
        ["planning_acars_flight_info_value_3"] = "KPDX / PDX",
        ["planning_acars_flight_info_value_6"] = "01 Jan 26",
        ["planning_acars_flight_info_value_7"] = "12:00 UTC",
        ["planning_acars_flight_info_value_8"] = "12:00 UTC",
        ["planning_acars_departure_value_0"] = "SEATTLE-TACOMA INTL",
        ["planning_acars_departure_value_1"] = "16L",
        ["planning_acars_departure_value_2"] = "BTG1",
        ["planning_acars_arrival_value_0"] = "PORTLAND INTL",
        ["planning_acars_arrival_value_1"] = "28R",
        ["planning_acars_arrival_value_2"] = "BTG2",
        ["planning_acars_route_value"] = "KSEA/16L DCT BTG DCT KPDX/28R",
        ["planning_operating_fp_value"] = MadeUpOperatingFp,
    };

    internal static IReadOnlyList<Bbs757EfbMemory.App> Scrub(IReadOnlyList<Bbs757EfbMemory.App> apps) =>
        apps.Select(a => a with { Controls = a.Controls.Select(ScrubNode).ToList() }).ToList();

    /// <summary>The made-up flight's value for a flight plan value node: the mapped one for a name in the table,
    /// "0" for every other flight plan value. An empty one is kept empty (<see cref="Scrub"/>).</summary>
    internal static string MadeUpFlight(string name) =>
        MadeUpFlightValues.TryGetValue(name, out string? v) ? v : Zero;

    private static Bbs757EfbMemory.Node ScrubNode(Bbs757EfbMemory.Node n) =>
        n with
        {
            Text = ScrubText(n.Kind, n.Name, n.Text),
            Children = n.Children.Select(ScrubNode).ToList(),
            Rows = n.Rows?.Select(ScrubNode).ToList(),
        };

    private static string ScrubText(string kind, string name, string text)
    {
        if (name == "simbrief_id_saved_label") return MadeUpSimbriefId;
        // From here an empty value stays empty, as an empty code does: "0" there would read as a value the tablet never showed.
        if (name == "simbrief_id_textbox") return text.Length == 0 ? text : MadeUpSimbriefId;
        if (name == "hoppie_logon_code_value_label" || name == "sayintentions_acars_key_value_label"
            || name.EndsWith("_textbox", StringComparison.Ordinal))
            return text.Length == 0 || text == EmptyCode ? text : MadeUpCode;
        if (IsFlightPlanValue(name)) return text.Length == 0 ? text : MadeUpFlight(name);
        if (MadeUpPerfCalcBoxes.TryGetValue(name, out string? box)) return text.Length == 0 ? text : box;
        if (kind == "textbox" && name.StartsWith("planning_perf_calc_", StringComparison.Ordinal)) return text.Length == 0 ? text : Zero;
        return text;
    }

    private static bool IsFlightPlanValue(string name) =>
        name == "planning_acars_route_value" || name == "planning_operating_fp_value"
        || (name.StartsWith("planning_acars_", StringComparison.Ordinal) && name.Contains("_value_", StringComparison.Ordinal));
}
