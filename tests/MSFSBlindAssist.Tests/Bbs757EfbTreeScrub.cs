using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The only way a tablet tree reaches a fixture file: the SimBrief ID, the Hoppie logon code, the SayIntentions
/// ACARS key and the flight plan data are replaced by made-up values. Works by node NAME only, never by text.
/// </summary>
internal static class Bbs757EfbTreeScrub
{
    internal const string MadeUpSimbriefId = "123456";
    internal const string MadeUpCode = "MADEUP";
    internal const string EmptyCode = "------";

    private const string Zero = "0";

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
    };

    internal static IReadOnlyList<Bbs757EfbMemory.App> Scrub(IReadOnlyList<Bbs757EfbMemory.App> apps) =>
        apps.Select(a => a with { Controls = a.Controls.Select(ScrubNode).ToList() }).ToList();

    /// <summary>The made-up flight's value for a flight plan value node: the mapped one for a name in the table,
    /// "0" for every other flight plan value. An empty one is kept empty (<see cref="Scrub"/>).</summary>
    internal static string MadeUpFlight(string name) =>
        MadeUpFlightValues.TryGetValue(name, out string? v) ? v : Zero;

    private static Bbs757EfbMemory.Node ScrubNode(Bbs757EfbMemory.Node n) =>
        n with { Text = ScrubText(n.Name, n.Text), Children = n.Children.Select(ScrubNode).ToList() };

    private static string ScrubText(string name, string text)
    {
        if (name == "simbrief_id_saved_label") return MadeUpSimbriefId;
        if (name == "hoppie_logon_code_value_label" || name == "sayintentions_acars_key_value_label"
            || name.EndsWith("_textbox", StringComparison.Ordinal))
            return text.Length == 0 || text == EmptyCode ? text : MadeUpCode;
        // An empty value stays empty, as an empty code does: "0" there would read as a value the tablet never showed.
        if (IsFlightPlanValue(name)) return text.Length == 0 ? text : MadeUpFlight(name);
        return text;
    }

    private static bool IsFlightPlanValue(string name) =>
        name == "planning_acars_route_value" || name == "planning_operating_fp_value"
        || (name.StartsWith("planning_acars_", StringComparison.Ordinal) && name.Contains("_value_", StringComparison.Ordinal));
}
