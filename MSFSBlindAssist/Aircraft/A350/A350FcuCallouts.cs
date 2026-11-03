using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// What the FCU and EFIS windows say when they change (review 2026-10-29, parity task 9): the altitude window and both baro
/// windows speak on their own, as the iniBuilds A300 does for its altitude window and the A380 for its baros, in the A380's words;
/// since plan 02 (2026-11-02) the speed, heading and V/S windows too, as the A380 and the A320 say a knob turned outside MSFSBA. A
/// window says nothing while it shows dashes, so what the autopilot manages is never read as a turn (the A300's reason, [A320-22]). When each is said (staged to a batch end, a load's settle, the echo of MSFSBA's
/// own typed value) is the shared FCU call-out's (<see cref="FcuValueAnnouncer"/>). Pure; numbers invariant ([VAR-5]).
/// </summary>
public static class A350FcuCallouts
{
    public const string AltitudeWindow = "Altitude window", CaptainBaro = "Captain baro", FirstOfficerBaro = "First officer baro",
        SpeedWindow = "Speed window", HeadingWindow = "Heading window", VsWindow = "V/S window";

    /// <summary>The windows that speak on their own, by <see cref="A350FcuWindow.Name"/>: the four the A380 and the A320 say when
    /// a knob is turned outside MSFSBA (plan 02, in flight 2026-11-02: speed, heading and V/S said nothing), and both baros.</summary>
    public static readonly IReadOnlyList<string> Speaking = new[] { SpeedWindow, HeadingWindow, AltitudeWindow, VsWindow, CaptainBaro, FirstOfficerBaro };

    /// <summary>How long after the last step of an encoder its value is read back, at the next batch end: the step's click
    /// is in the cockpit and sampled by then, and a run of steps is read back once (panels minor 3).</summary>
    public const int StepSettleMs = 400;

    /// <summary>The emergency descent flying, as the modes read it (live 2026-11-03, 1.2.8): <c>INI_PITCH_MODE</c> 9 from its
    /// engagement to ALT*, <c>INI_ROLL_MODE</c> 3 (its HDG) from the engagement until the pilot picks a lateral mode.
    /// <c>INI_EMER_DESCENT</c> stays 0 throughout.</summary>
    public const double EmerDescentPitchMode = 9, EmerDescentRollMode = 3;

    /// <summary>How long the heading window must stand still, while the aircraft turns it, before its value is said once.</summary>
    public const int AircraftTurnSettleMs = 3000;

    /// <summary>Whether a window's change is the aircraft turning it: the heading window while the emergency descent flies (engaged
    /// from NAV it turned the selected heading a degree a second, 160 to 130, live 2026-11-03). Said once settled, never per degree.</summary>
    public static bool IsAircraftTurning(string window, Func<string, double?> read) =>
        window == HeadingWindow
        && (read("INI_PITCH_MODE") is double pitch && Math.Abs(pitch - EmerDescentPitchMode) < 0.5
            || read("INI_ROLL_MODE") is double roll && Math.Abs(roll - EmerDescentRollMode) < 0.5);

    /// <summary>What a speaking window says, null while an input is unread (never a baseline of nothing).</summary>
    public static string? Phrase(string window, Func<string, double?> read) => window switch
    {
        AltitudeWindow => read("INI_ALTITUDE_DIAL") is double feet ? "Altitude " + Whole(feet) + " feet" : null,
        CaptainBaro => Baro(1, read),
        FirstOfficerBaro => Baro(2, read),
        SpeedWindow => Speed(read),
        HeadingWindow => Heading(read),
        VsWindow => Vertical(read),
        _ => null,
    };

    /// <summary>The speed window as the A380 says it ("Speed 250 knots", "Mach 0.78"); a turn while managed shows the preset (as the
    /// window's line reads it); dashes say nothing.</summary>
    private static string? Speed(Func<string, double?> read)
    {
        if (read("INI_FCU_SPD_DASHED") is not double dashed || read("INI_MANAGED_SPEED") is not double managed)
            return null;
        if (dashed >= 0.5)
            return null;
        if (managed >= 0.5)
            return read("INI_PRESET_DIAL") is double preset && read("INI_PRESET_DIAL_IS_MACH") is double isMach
                ? SpeedWords(preset, isMach >= 0.5) + ", preset" : null;
        return read("INI_AIRSPEED_DIAL") is double dial ? SpeedWords(dial, dial < 5) : null;
    }

    private static string SpeedWords(double value, bool mach) =>
        mach ? "Mach " + value.ToString("0.00", CultureInfo.InvariantCulture) : "Speed " + Whole(value) + " knots";

    /// <summary>The heading or track window as the A380 says it ("Heading 270 degrees"), truncated as the FCU shows it; a turn in NAV
    /// shows the preset; dashes say nothing.</summary>
    private static string? Heading(Func<string, double?> read)
    {
        if (read("INI_FCU_HDG_DASHED") is not double dashed || read("INI_TRACK_FPA_STATE") is not double trkFpa || dashed >= 0.5)
            return null;
        bool track = trkFpa >= 0.5;
        if (read(track ? "INI_TRK_DIAL" : "INI_HEADING_DIAL") is not double dial)
            return null;
        bool preset = read("INI_ROLL_MODE") is double roll && Math.Abs(roll - A350TypedEntry.NavRollMode) < 0.5;
        int degrees = ((int)Math.Floor(dial) % 360 + 360) % 360;
        return (track ? "Track " : "Heading ") + degrees.ToString("000", CultureInfo.InvariantCulture) + " degrees" + (preset ? ", preset" : "");
    }

    /// <summary>The V/S or FPA window as the A380 says it ("Vertical speed -500 feet per minute", "FPA -3.0 degrees"); a turn while
    /// dashed shows a preview, which is said; dashes say nothing.</summary>
    private static string? Vertical(Func<string, double?> read)
    {
        if (read("INI_FCU_VS_DASHED") is not double dashed || read("INI_TRACK_FPA_STATE") is not double trkFpa || dashed >= 0.5)
            return null;
        if (trkFpa >= 0.5)
            return read("INI_FPA_DIAL") is double fpa
                ? "FPA " + (Math.Round(fpa, 1) is var d && d == 0 ? 0 : d).ToString("0.0", CultureInfo.InvariantCulture) + " degrees" : null;
        return read("INI_VVI_DIAL") is double vs
            ? "Vertical speed " + (Math.Round(vs / 100) * 100 is var f && f == 0 ? 0 : f).ToString("0", CultureInfo.InvariantCulture) + " feet per minute"
            : null;
    }

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
                : $"{who} altimeter preset " + (hectopascals ? Whole(value) + " hectopascals" : A350FcuWindows.Inches(value / 100));
        }
        if (read($"KOHLSMAN SETTING MB:{side}") is not double mb)
            return null;
        return $"{who} altimeter " + (hectopascals ? A350FcuWindows.Hectopascals(mb) + " hectopascals" : A350FcuWindows.Inches(mb / AltimeterPhrase.HpaPerInHg));
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

    private static string Whole(double v) => A350FcuWindows.Whole(v);
}
