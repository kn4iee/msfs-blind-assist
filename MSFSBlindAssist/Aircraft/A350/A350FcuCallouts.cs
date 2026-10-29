using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// What the FCU and EFIS windows say when they change (review 2026-10-29, parity task 9): the altitude window and both baro
/// windows speak on their own, as the A300 does for its altitude window ([A300-14]: the autopilot writes the others) and the
/// A380 for its baros, in the A380's words. When each is said (staged to a batch end, a load's settle, the echo of MSFSBA's
/// own typed value) is the shared FCU call-out's (<see cref="FcuValueAnnouncer"/>). Pure; numbers invariant ([VAR-5]).
/// </summary>
public static class A350FcuCallouts
{
    public const string AltitudeWindow = "Altitude window", CaptainBaro = "Captain baro", FirstOfficerBaro = "First officer baro";

    /// <summary>The windows that speak on their own, by <see cref="A350FcuWindow.Name"/>.</summary>
    public static readonly IReadOnlyList<string> Speaking = new[] { AltitudeWindow, CaptainBaro, FirstOfficerBaro };

    /// <summary>How long after the last step of an encoder its value is read back, at the next batch end: the step's click
    /// is in the cockpit and sampled by then, and a run of steps is read back once (panels minor 3).</summary>
    public const int StepSettleMs = 400;

    /// <summary>What a speaking window says, null while an input is unread (never a baseline of nothing).</summary>
    public static string? Phrase(string window, Func<string, double?> read) => window switch
    {
        AltitudeWindow => read("INI_ALTITUDE_DIAL") is double feet ? "Altitude " + Whole(feet) + " feet" : null,
        CaptainBaro => Baro(1, read),
        FirstOfficerBaro => Baro(2, read),
        _ => null,
    };

    /// <summary>A side's baro as the A380 says it (its BaroPhrase): the setting in the side's unit, STD as "standard", and
    /// the preset the knob shows while on STD.</summary>
    private static string? Baro(int side, Func<string, double?> read)
    {
        string who = side == 1 ? "Captain" : "First officer";
        if (read($"XMLVAR_Baro{side}_Mode") is not double mode || read($"XMLVAR_Baro_Selector_HPA_{side}") is not double unit)
            return null;
        bool hectopascals = unit >= 0.5;
        if (Math.Abs(mode - 3) < 0.5)
        {
            string preset = A350TypedEntry.PresetVar(side);
            if (read(preset + "_SHOW") is not double shown || read(preset) is not double value)
                return null;
            return shown < 0.5 ? $"{who} altimeter standard"
                : $"{who} altimeter preset " + (hectopascals ? Whole(value) + " hectopascals" : Inches(value / 100));
        }
        if (read($"KOHLSMAN SETTING MB:{side}") is not double mb)
            return null;
        return $"{who} altimeter " + (hectopascals ? A350FcuWindows.Hectopascals(mb) + " hectopascals" : Inches(mb / 33.8639));
    }

    /// <summary>The baro a unit button switches, by map control id: MSFSBA's own press of it is not said back by the baro
    /// (its label says the new unit, as the A380's unit combo does), a press made elsewhere is.</summary>
    public static string? UnitButtonWindow(string controlId) => controlId switch
    {
        "AIRLINER_EFIS_QNH_UNIT_SELECTOR_LEFT" => CaptainBaro,
        "AIRLINER_EFIS_QNH_UNIT_SELECTOR_RIGHT" => FirstOfficerBaro,
        _ => null,
    };

    /// <summary>A preset going out of view while the baro stays on STD (the knob's preset display times out): nobody
    /// changed anything, so it is recorded, never said.</summary>
    public static bool IsQuiet(string? previous, string next) =>
        previous != null && previous.Contains(" altimeter preset ", StringComparison.Ordinal)
        && next.EndsWith(" altimeter standard", StringComparison.Ordinal);

    private static string Whole(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    private static string Inches(double inches) => inches.ToString("0.00", CultureInfo.InvariantCulture) + " inches";
}
