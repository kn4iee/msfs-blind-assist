using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>An encoder whose value the cockpit shows, typed instead of stepped.</summary>
/// <param name="Key">Its MSFSBA variable key; "_SET" makes MainForm render it as a typed field.</param>
/// <param name="Name">The field's name, as the released aircraft name theirs.</param>
/// <param name="ControlId">The encoder it replaces (its increase and decrease rows).</param>
/// <param name="Side">1 captain, 2 first officer, for a baro; 0 otherwise.</param>
internal sealed record A350TypedField(string Key, string Name, string ControlId, int Side = 0);

/// <summary>
/// A value only the knob's own clicks can move, read between them: the baro preset on STD (the WASM keeps it in its own
/// memory; <see cref="ValueVar"/> is only its output). After the plan's first click, the definition reads whether it
/// shows, then clicks toward <see cref="Target"/> one click per <see cref="GapMs"/>, reads again, up to
/// <see cref="MaxRounds"/> times.
/// </summary>
internal sealed record A350TypedStepper(string ValueVar, string ShowVar, double Target, string ControlId, string NotShown)
{
    /// <summary>Between clicks: each click is a command the WASM consumes once a frame (300 ms stepped the preset reliably
    /// in flight; clicks one MCP round trip apart let its timer lapse).</summary>
    public const int GapMs = 300;

    /// <summary>For a click to land before a read.</summary>
    public const int SettleMs = 400;

    public const int MaxRounds = 3;

    public A350CalcStep Click(int dir) => new($"{dir} (>B:{ControlId}_Set)");
}

/// <summary>What a typed value does: the steps that land it, and how to check and correct it afterwards.</summary>
/// <param name="Refusal">Said instead of doing anything (an invalid value, a window in the other unit).</param>
/// <param name="ReadBackVar">The variable the cockpit shows the value in (an A350 L:var or the sim's baro).</param>
/// <param name="Target">The value it should read, in that variable's unit.</param>
/// <param name="Correction">A direct write of the target, sent when the click landed elsewhere.</param>
internal sealed class A350TypedPlan
{
    private readonly Func<double, Func<string, double?>, string>? _confirm;

    public A350TypedPlan(IReadOnlyList<A350Step> steps, string readBackVar, double target, double tolerance,
        A350CalcStep correction, Func<double, Func<string, double?>, string> confirm, A350TypedStepper? stepper = null)
    {
        Stepper = stepper;
        Steps = steps;
        ReadBackVar = readBackVar;
        Target = target;
        Tolerance = tolerance;
        Correction = correction;
        _confirm = confirm;
    }

    private A350TypedPlan(string refusal)
    {
        Refusal = refusal;
        Steps = Array.Empty<A350Step>();
        ReadBackVar = string.Empty;
        Correction = new A350CalcStep(string.Empty);
    }

    public static A350TypedPlan Refused(string why) => new(why);

    public string? Refusal { get; }
    public IReadOnlyList<A350Step> Steps { get; }
    public string ReadBackVar { get; } = string.Empty;
    public double Target { get; }
    public double Tolerance { get; }
    public A350CalcStep Correction { get; }

    /// <summary>Set when the value is stepped to by clicks read between them, not written.</summary>
    public A350TypedStepper? Stepper { get; }

    /// <summary>The confirmation spoken once the cockpit shows <paramref name="shown"/>.</summary>
    public string Confirm(double shown, Func<string, double?> read) => _confirm?.Invoke(shown, read) ?? string.Empty;

    /// <summary>Whether a read-back is the target.</summary>
    public bool Reached(double shown) => Math.Abs(shown - Target) <= Tolerance;
}

/// <summary>
/// Typed values for the A350's encoders, as the released aircraft type theirs: the FBW A320's FCU panel fields
/// (Heading, Speed, Altitude, VS/FPA, validated and worded by <see cref="FcuValueEntry"/>) and the FBW A380's QNH
/// and pressurization fields. Pure.
/// <para>
/// The value is landed by the aircraft's own click: the variable the click steps from is written one step short of
/// the target, then the encoder is clicked once toward it, so the aircraft's own handler sets the target with every
/// side effect a turn has (the speed preset and its timer while managed, linked baros, the altitude knob's mode
/// reversions). Live 2026-10-26: the FCU dials (<c>INI_HEADING_DIAL</c> 100, a click, 101 and the autopilot's
/// heading 101; altitude, speed, track and FPA alike), the cabin targets and the sim's baro setting are each the
/// state a click steps from. Limits are the aircraft's own (WASM 1.2.7: <c>clampSpeed</c>, <c>clampMach</c>,
/// <c>ClampAltitude</c>, the V/S handlers, <c>CabPressSystem</c>'s target handlers).
/// </para>
/// </summary>
internal static class A350TypedEntry
{
    public static readonly A350TypedField Speed = new("A350_FCU_SPEED_SET", "Speed", "AIRLINER_FCU_SPD_KNOB");
    public static readonly A350TypedField Heading = new("A350_FCU_HEADING_SET", "Heading", "AIRLINER_FCU_HDG_KNOB");
    public static readonly A350TypedField Altitude = new("A350_FCU_ALTITUDE_SET", "Altitude", "AIRLINER_FCU_ALT_KNOB");
    public static readonly A350TypedField VerticalSpeed = new("A350_FCU_VS_SET", "VS/FPA", "AIRLINER_FCU_VS_KNOB");
    public static readonly A350TypedField CaptainQnh =
        new("A350_CAPT_QNH_SET", "QNH (in Capt unit)", "AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT", Side: 1);
    public static readonly A350TypedField FirstOfficerQnh =
        new("A350_FO_QNH_SET", "QNH (in F/O unit)", "AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_RIGHT", Side: 2);
    /// <summary>The captain's ISIS: its baro is the sim's third altimeter setting (live 2026-10-26: a knob click moved
    /// KOHLSMAN SETTING MB:3 1009.1 to 1010.1).</summary>
    public static readonly A350TypedField IsisQnh = new("A350_ISIS_QNH_SET", "Captain QNH", "AIRLINER_ISIS_KNOB_1", Side: 3);
    public static readonly A350TypedField CabinAltitudeTarget =
        new("A350_CABIN_ALT_TARGET_SET", "Cabin altitude target (feet)", "AIRLINER_CABIN_PRESS_ALTITUDE_TARGET");
    public static readonly A350TypedField CabinVsTarget =
        new("A350_CABIN_VS_TARGET_SET", "Cabin V/S target (feet per minute)", "AIRLINER_CABIN_PRESS_VS_TARGET");

    public static readonly IReadOnlyList<A350TypedField> All = new[]
    {
        Speed, Heading, Altitude, VerticalSpeed, CaptainQnh, FirstOfficerQnh, IsisQnh, CabinAltitudeTarget, CabinVsTarget,
    };

    private static readonly Dictionary<string, A350TypedField> ByControl =
        All.ToDictionary(f => f.ControlId, StringComparer.OrdinalIgnoreCase);

    /// <summary>The typed field that replaces an encoder's rows, or null.</summary>
    public static A350TypedField? ForControl(string controlId) => ByControl.GetValueOrDefault(controlId);

    /// <summary>How long the short-of-target write is given to land before the click.</summary>
    public const int ClickDelayMs = 100;

    public const string VerticalInvalid = "Invalid value. VS: plus or minus 100 to 6000, FPA: plus or minus 9.9 or less";

    private const double MbPerInch = 33.8639;
    private const double MbPerHundredthInch = 0.338639;

    /// <summary>The variables a field's plan reads, by name (A350 L:vars or the sim's baro setting).</summary>
    public static IReadOnlyList<string> Inputs(A350TypedField field) => field switch
    {
        _ when field == Speed => new[] { "INI_AIRSPEED_DIAL", "INI_AIRSPEED_IS_MACH", "INI_MANAGED_SPEED", "INI_PRESET_DIAL", "INI_FCU_SPD_DOT" },
        _ when field == Heading => new[] { "INI_HEADING_DIAL", "INI_TRK_DIAL", "INI_TRACK_FPA_STATE", "INI_FCU_HDG_DOT", "INI_FCU_HDG_DASHED", "INI_ROLL_MODE" },
        _ when field == Altitude => new[] { "INI_ALTITUDE_DIAL", "INI_FCU_ALT_DOT" },
        _ when field == VerticalSpeed => new[] { "INI_VVI_DIAL", "INI_FPA_DIAL", "INI_TRACK_FPA_STATE", "INI_FCU_VS_DASHED", "INI_PITCH_MODE" },
        _ when field == CabinAltitudeTarget => new[] { "INI_CABIN_PRESS_ALTITUDE_TARGET" },
        _ when field == CabinVsTarget => new[] { "INI_CABIN_PRESS_VS_TARGET" },
        _ when field == IsisQnh => new[] { "KOHLSMAN SETTING MB:3" },
        _ => new[]
        {
            $"KOHLSMAN SETTING MB:{field.Side}", $"XMLVAR_Baro_Selector_HPA_{field.Side}", $"XMLVAR_Baro{field.Side}_Mode",
            PresetVar(field.Side), PresetVar(field.Side) + "_SHOW",
        },
    };

    public static A350TypedPlan Plan(A350TypedField field, double typed, Func<string, double?> read)
    {
        if (!double.IsFinite(typed))
            return A350TypedPlan.Refused(FcuValueEntry.InvalidNumber);
        return field switch
        {
            _ when field == Speed => PlanSpeed(typed, read),
            _ when field == Heading => PlanHeading(typed, read),
            _ when field == Altitude => PlanAltitude(typed, read),
            _ when field == VerticalSpeed => PlanVertical(typed, read),
            _ when field == CabinAltitudeTarget => PlanCabin(field, typed, read, "INI_CABIN_PRESS_ALTITUDE_TARGET", 100, -2000, 13500,
                "Cabin altitude target", "feet"),
            _ when field == CabinVsTarget => PlanCabin(field, typed, read, "INI_CABIN_PRESS_VS_TARGET", 50, -1500, 2500,
                "Cabin V/S target", "feet per minute"),
            _ when field == IsisQnh => PlanIsis(typed, read),
            _ => PlanBaro(field, typed, read),
        };
    }

    private static A350TypedPlan PlanSpeed(double typed, Func<string, double?> read)
    {
        if (!FcuValueEntry.TrySpeed(typed, out int internalSpeed, out string? error))
            return A350TypedPlan.Refused(error!);
        if (read("INI_AIRSPEED_DIAL") is not double dial || read("INI_AIRSPEED_IS_MACH") is not double isMach
            || read("INI_MANAGED_SPEED") is not double managed)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        bool typedMach = internalSpeed < 100;
        if (typedMach != isMach >= 0.5)
            return A350TypedPlan.Refused(isMach >= 0.5
                ? "The speed window shows Mach. Type 0.10 to 0.99, or press SPD/MACH first."
                : "The speed window shows knots. Type 100 to 399, or press SPD/MACH first.");
        double target = typedMach ? internalSpeed / 100.0 : internalSpeed;
        double step = typedMach ? 0.01 : 1;
        string Confirm(double shown, Func<string, double?> r) =>
            (typedMach ? "FCU speed mach " + shown.ToString("0.00", CultureInfo.InvariantCulture)
                : "FCU speed " + Whole(shown)) + Status(r("INI_FCU_SPD_DOT"));

        // While managed the knob turns a preset seeded from the managed speed (airspeed_dial_up_handler): the click
        // shows it, and the read-back puts the typed value on it.
        if (managed >= 0.5)
            return Landing("INI_PRESET_DIAL", target, step / 2, null, Click(Speed, 1),
                (shown, _) => (typedMach ? "FCU speed mach " + shown.ToString("0.00", CultureInfo.InvariantCulture)
                    : "FCU speed " + Whole(shown)) + Preset);
        return Landing("INI_AIRSPEED_DIAL", target, step / 2, Short("INI_AIRSPEED_DIAL", target, dial, step, out int dir),
            Click(Speed, dir), Confirm);
    }

    private static A350TypedPlan PlanHeading(double typed, Func<string, double?> read)
    {
        if (!FcuValueEntry.TryHeading(typed, out int heading, out string? error))
            return A350TypedPlan.Refused(error!);
        if (read("INI_TRACK_FPA_STATE") is not double trkFpa)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        bool track = trkFpa >= 0.5;
        string dialVar = track ? "INI_TRK_DIAL" : "INI_HEADING_DIAL";
        if (read(dialVar) is not double dial)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        // The short way round: from 350 to 010 is up through 360.
        double delta = ((heading - dial) % 360 + 540) % 360 - 180;
        int dir = delta >= 0 ? 1 : -1;
        double neighbour = ((heading - dir) % 360 + 360) % 360;
        // A dashed window turned shows a preset, and so does a turn while NAV stays engaged over an earlier preset.
        bool preset = read("INI_FCU_HDG_DASHED") is >= 0.5 || read("INI_ROLL_MODE") is double roll && Math.Abs(roll - NavRollMode) < 0.5;
        return Landing(dialVar, heading, 0.5, Write(dialVar, neighbour), Click(Heading, dir),
            (shown, r) => $"FCU {(track ? "track" : "heading")} {(((int)Math.Round(shown)) % 360 + 360) % 360:000}"
                + (preset ? Preset : Status(r("INI_FCU_HDG_DOT"))));
    }

    private static A350TypedPlan PlanAltitude(double typed, Func<string, double?> read)
    {
        if (!FcuValueEntry.TryAltitude(typed, out double feet, out string? error))
            return A350TypedPlan.Refused(error!);
        if (read("INI_ALTITUDE_DIAL") is not double dial)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        double target = Math.Round(feet / 100, MidpointRounding.AwayFromZero) * 100;
        // A click steps 100 ft or 1000 ft (the 100/1000 selector, a value the aircraft keeps to itself): from 100 ft
        // short, a 1000 ft step lands elsewhere and the read-back corrects it.
        return Landing("INI_ALTITUDE_DIAL", target, 0.5, Short("INI_ALTITUDE_DIAL", target, dial, 100, out int dir),
            Click(Altitude, dir), (shown, r) => "FCU altitude " + Whole(shown) + Status(r("INI_FCU_ALT_DOT")));
    }

    private static A350TypedPlan PlanVertical(double typed, Func<string, double?> read)
    {
        bool isVs = typed >= -6000 && typed <= 6000 && Math.Abs(typed) >= 100;
        bool isFpa = typed >= -9.9 && typed <= 9.9;
        if (!isVs && !isFpa)
            return A350TypedPlan.Refused(VerticalInvalid);
        if (read("INI_TRACK_FPA_STATE") is not double trkFpa)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        bool fpaMode = trkFpa >= 0.5;
        if (fpaMode && !isFpa)
            return A350TypedPlan.Refused("The vertical window shows FPA. Type plus or minus 9.9 or less, or press HDG-V/S / TRK-FPA first.");
        if (!fpaMode && !isVs && typed != 0)
            return A350TypedPlan.Refused("The vertical window shows V/S. Type plus or minus 100 to 6000, or press HDG-V/S / TRK-FPA first.");
        string dialVar = fpaMode ? "INI_FPA_DIAL" : "INI_VVI_DIAL";
        if (read(dialVar) is not double dial)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        double step = fpaMode ? 0.1 : 100;
        // V/S or FPA not engaged: the turn is a preview the pilot pulls to use.
        string preset = read("INI_FCU_VS_DASHED") is >= 0.5
            || read("INI_PITCH_MODE") is double pitch && pitch >= 0.5 && Math.Abs(pitch - VsPitchMode) >= 0.5 && Math.Abs(pitch - FpaPitchMode) >= 0.5
            ? Preset : "";
        double target = fpaMode ? Math.Round(typed, 1, MidpointRounding.AwayFromZero)
            : Math.Round(typed / 100, MidpointRounding.AwayFromZero) * 100;
        return Landing(dialVar, target, step / 2, Short(dialVar, target, dial, step, out int dir), Click(VerticalSpeed, dir),
            (shown, _) => (fpaMode
                ? "FCU flight path angle " + shown.ToString("0.0", CultureInfo.InvariantCulture)
                : "FCU vertical speed " + Whole(shown)) + preset);
    }

    private static A350TypedPlan PlanCabin(A350TypedField field, double typed, Func<string, double?> read, string var,
        double step, double min, double max, string name, string unit)
    {
        string range = $"{name} must be between {Whole(min)} and {Whole(max)} {unit}";
        double target = Math.Round(typed / step, MidpointRounding.AwayFromZero) * step;
        if (target < min || target > max)
            return A350TypedPlan.Refused(range);
        if (read(var) is not double now)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        return Landing(var, target, step / 2, Short(var, target, now, step, out int dir), Click(field, dir),
            (shown, _) => $"{name} set to {Whole(shown)} {unit}");
    }

    private static A350TypedPlan PlanBaro(A350TypedField field, double typed, Func<string, double?> read)
    {
        int side = field.Side;
        string sideName = side == 1 ? "Captain" : "First officer";
        if (read($"XMLVAR_Baro{side}_Mode") is not double mode || read($"XMLVAR_Baro_Selector_HPA_{side}") is not double hpaUnit
            || read($"KOHLSMAN SETTING MB:{side}") is not double mb)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        bool inches = hpaUnit < 0.5;
        // The A380's range and words, in the side's own unit.
        double targetMb = inches ? Math.Round(typed, 2, MidpointRounding.AwayFromZero) * MbPerInch : Math.Round(typed, MidpointRounding.AwayFromZero);
        if (targetMb < 900 || targetMb > 1100)
            return A350TypedPlan.Refused(inches ? "QNH must be between 26.6 and 32.5 inches." : "QNH must be between 900 and 1100 hectopascals.");
        if (Math.Abs(mode - 3) < 0.5)
            return PlanStdPreset(field, sideName, inches, inches ? Math.Round(typed * 100, MidpointRounding.AwayFromZero) : targetMb);
        double stepMb = inches ? MbPerHundredthInch : 1;
        int dir = targetMb >= mb ? 1 : -1;
        string var = $"KOHLSMAN SETTING MB:{side}";
        return Landing(var, targetMb, 0.1, Kohlsman(targetMb - dir * stepMb, side), Click(field, dir),
            (shown, _) => $"{sideName} altimeter set " + (inches
                ? (shown / MbPerInch).ToString("0.00", CultureInfo.InvariantCulture) + " inches"
                : A350FcuWindows.Hectopascals(shown) + " hectopascals"),
            Kohlsman(targetMb, side));
    }

    /// <summary>
    /// On STD the knob sets a preset QNH, which the pull then sets (live in flight 2026-10-26: 29.85 stepped to 29.89, the
    /// pull set 29.89). Its first click shows the preset, each further click while it shows is a step; the WASM keeps it
    /// in its own memory, so it is stepped to by clicks read between them (<see cref="A350TypedStepper"/>), in the side's
    /// unit: whole hectopascals, or hundredths of an inch. On the ground the knob shows no preset (measured).
    /// </summary>
    private static A350TypedPlan PlanStdPreset(A350TypedField field, string sideName, bool inches, double target)
    {
        var stepper = new A350TypedStepper(PresetVar(field.Side), PresetVar(field.Side) + "_SHOW", target, field.ControlId,
            $"{sideName} baro is on STD and its knob shows no preset here, as on the ground; pull it for QNH first.");
        return new A350TypedPlan(new A350Step[] { stepper.Click(1) }, stepper.ValueVar, target, 0.5,
            new A350CalcStep(string.Empty),
            (shown, _) => $"{sideName} altimeter preset " + (inches
                ? (shown / 100).ToString("0.00", CultureInfo.InvariantCulture) + " inches"
                : Whole(shown) + " hectopascals"),
            stepper);
    }

    /// <summary>The A350's output of a side's STD preset: INI_BARO_PRESET_CPT or _FO.</summary>
    public static string PresetVar(int side) => side == 1 ? "INI_BARO_PRESET_CPT" : "INI_BARO_PRESET_FO";

    /// <summary>
    /// The ISIS keeps its unit and STD to itself (its gauge's own state, no variable), so a typed QNH is taken in either
    /// unit by its size, as the A380's ranges, and landed from one hectopascal short; when the ISIS shows inches its
    /// click steps a hundredth of an inch and the read-back writes the value.
    /// </summary>
    private static A350TypedPlan PlanIsis(double typed, Func<string, double?> read)
    {
        bool inches = typed >= 26.6 && typed <= 32.5;
        double targetMb = inches ? Math.Round(typed, 2, MidpointRounding.AwayFromZero) * MbPerInch : Math.Round(typed, MidpointRounding.AwayFromZero);
        if (!inches && (targetMb < 900 || targetMb > 1100))
            return A350TypedPlan.Refused("QNH must be between 900 and 1100 hectopascals, or 26.6 and 32.5 inches.");
        if (read("KOHLSMAN SETTING MB:3") is not double mb)
            return A350TypedPlan.Refused(A350WritePlan.UnknownPositionRefusal);
        int dir = targetMb >= mb ? 1 : -1;
        return Landing("KOHLSMAN SETTING MB:3", targetMb, 0.1, Kohlsman(targetMb - dir, 3), Click(IsisQnh, dir),
            (shown, _) => "ISIS altimeter set " + (inches
                ? (shown / MbPerInch).ToString("0.00", CultureInfo.InvariantCulture) + " inches"
                : Whole(shown) + " hectopascals"),
            Kohlsman(targetMb, 3));
    }

    /// <summary>The sim's own baro event for one side: millibars ×16, then the side (the cockpit's own order).</summary>
    private static A350CalcStep Kohlsman(double mb, int side) =>
        new($"{A350WritePlan.Format(Math.Round(mb * 16))} {side} r (>K:2:KOHLSMAN_SET)");

    private static A350TypedPlan Landing(string readBack, double target, double tolerance, A350CalcStep? shortWrite,
        A350CalcStep click, Func<double, Func<string, double?>, string> confirm, A350CalcStep? correction = null)
    {
        var steps = new List<A350Step>();
        if (shortWrite != null)
        {
            steps.Add(shortWrite);
            steps.Add(new A350DelayStep(ClickDelayMs));
        }
        steps.Add(click);
        return new A350TypedPlan(steps, readBack, target, tolerance, correction ?? Write(readBack, target), confirm);
    }

    /// <summary>Writes <paramref name="var"/> one step short of the target, on the side the value is coming from.</summary>
    private static A350CalcStep Short(string var, double target, double now, double step, out int dir)
    {
        dir = target >= now ? 1 : -1;
        return Write(var, Math.Round(target - dir * step, 4));
    }

    private static A350CalcStep Write(string var, double value) => new($"{A350WritePlan.Format(value)} (>L:{var})");

    private static A350CalcStep Click(A350TypedField field, int dir) => new($"{dir} (>B:{field.ControlId}_Set)");

    private static string Status(double? dot) => dot is >= 0.5 ? ", managed" : ", selected";

    /// <summary>The lateral and vertical modes, as INI_ROLL_MODE and INI_PITCH_MODE read them (live in flight 2026-10-26:
    /// NAV 1, HDG 2, TRK 11; V/S 8, FPA 11, managed ALT 12). FCU::Draw shows the heading as selected in modes 2 and 11.</summary>
    public const double NavRollMode = 1, VsPitchMode = 8, FpaPitchMode = 11;

    /// <summary>A value turned into a managed or dashed window: shown for 45 s, used once the knob is pulled.</summary>
    private const string Preset = ", preset";

    private static string Whole(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}
