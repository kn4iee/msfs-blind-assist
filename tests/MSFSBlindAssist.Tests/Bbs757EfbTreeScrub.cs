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

    private const string FlightNumber = "MSF1";
    private const string Origin = "KSEA / SEA";
    private const string Destination = "KPDX / PDX";
    private const string Route = "KSEA/16L DCT BTG DCT KPDX/28R";
    private const string Date = "01 Jan 26";
    private const string Time = "12:00 UTC";

    internal static IReadOnlyList<Bbs757EfbMemory.App> Scrub(IReadOnlyList<Bbs757EfbMemory.App> apps) =>
        apps.Select(a => a with { Controls = a.Controls.Select(ScrubNode).ToList() }).ToList();

    /// <summary>The made-up flight's value for a flight plan value node, chosen by the node's name: the route,
    /// a date or time by name, otherwise by the value's index (0 and 1 the flight number and callsign, 2 and 3
    /// the airports); every other value is "0".</summary>
    internal static string MadeUpFlight(string name)
    {
        if (name == "planning_acars_route_value") return Route;
        if (name.Contains("date", StringComparison.Ordinal)) return Date;
        if (name.Contains("time", StringComparison.Ordinal)) return Time;
        if (name.Contains("callsign", StringComparison.Ordinal)) return FlightNumber;
        return ValueIndex(name) switch
        {
            0 => FlightNumber,
            1 => FlightNumber,
            2 => Origin,
            3 => Destination,
            _ => "0",
        };
    }

    private static Bbs757EfbMemory.Node ScrubNode(Bbs757EfbMemory.Node n) =>
        n with { Text = ScrubText(n.Name, n.Text), Children = n.Children.Select(ScrubNode).ToList() };

    private static string ScrubText(string name, string text)
    {
        if (name == "simbrief_id_saved_label") return MadeUpSimbriefId;
        if (name == "hoppie_logon_code_value_label" || name == "sayintentions_acars_key_value_label"
            || name.EndsWith("_textbox", StringComparison.Ordinal))
            return text == EmptyCode ? text : MadeUpCode;
        if (IsFlightPlanValue(name)) return MadeUpFlight(name);
        return text;
    }

    private static bool IsFlightPlanValue(string name) =>
        name == "planning_acars_route_value" || name == "planning_operating_fp_value"
        || (name.StartsWith("planning_acars_", StringComparison.Ordinal) && name.Contains("_value_", StringComparison.Ordinal));

    private static int ValueIndex(string name)
    {
        int at = name.LastIndexOf("_value_", StringComparison.Ordinal);
        return at >= 0 && int.TryParse(name.AsSpan(at + 7), out int i) ? i : -1;
    }
}
