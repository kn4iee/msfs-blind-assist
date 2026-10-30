using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The VHF and squawk call-outs, as the A380's (its COM_ACTIVE_n, COM_STANDBY_n and XPNDR_CODE): the A350 keeps the stock
/// COM 1 and 2 frequencies in step with its RMPs (live 2026-10-30: VHF1 122.800 / 123.450 and VHF2 121.500 / 124.850 on the
/// RMP, the same in COM ACTIVE/STANDBY FREQUENCY:1 and :2) and TRANSPONDER CODE:1 with its squawk, so a change made anywhere
/// is said, each with its own Ctrl+M row. VHF3 is the datalink radio (DATA on the RMP; the stock COM 3 stayed at its default
/// 124.850, no mirror) and is left out. Pure.
/// </summary>
public static class A350RadioCallouts
{
    public const string SquawkKey = "A350_SQUAWK";

    public static string ActiveKey(int vhf) => $"A350_COM_ACTIVE_{vhf}";
    public static string StandbyKey(int vhf) => $"A350_COM_STANDBY_{vhf}";

    /// <summary>Every row: its key, the sim variable, its Ctrl+M label and the unit it is read in.</summary>
    public static readonly IReadOnlyList<(string Key, string Var, string Label, string Units)> Rows = new[]
    {
        (ActiveKey(1), "COM ACTIVE FREQUENCY:1", "VHF 1 active frequency", "MHz"),
        (StandbyKey(1), "COM STANDBY FREQUENCY:1", "VHF 1 standby frequency", "MHz"),
        (ActiveKey(2), "COM ACTIVE FREQUENCY:2", "VHF 2 active frequency", "MHz"),
        (StandbyKey(2), "COM STANDBY FREQUENCY:2", "VHF 2 standby frequency", "MHz"),
        (SquawkKey, "TRANSPONDER CODE:1", "Squawk code", "BCO16"),
    };

    public static bool IsRadioKey(string key) => Rows.Any(r => r.Key == key);

    /// <summary>A VHF frequency the RMP can hold; outside it (an unpowered 0) nothing is said.</summary>
    public static bool InVhfBand(double mhz) => mhz is >= 118.0 and <= 137.0;

    /// <summary>"VHF 1 active 121.900", "VHF 2 standby 124.850".</summary>
    public static string Frequency(string key, double mhz)
    {
        string label = Rows.Single(r => r.Key == key).Label;   // "VHF 1 active frequency"
        return string.Create(CultureInfo.InvariantCulture, $"{label[..^" frequency".Length]} {mhz:0.000}");
    }

    /// <summary>A BCO16 squawk ("0x4321") as its four digits: "Squawk 4321".</summary>
    public static string Squawk(int bcd) => $"Squawk {(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}";
}
