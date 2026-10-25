using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// What the MCP set loops need from the sim, as delegates so the loops stay pure and a test can
/// stand a simulated MCP behind them: a fresh read of a variable (null when it cannot be read), a
/// calculator write, and an awaitable wait.
/// </summary>
public sealed class McpIo
{
    public required Func<string, CancellationToken, Task<double?>> Read { get; init; }
    public required Action<string> Write { get; init; }
    public required Func<int, CancellationToken, Task> Delay { get; init; }
}

/// <summary>
/// The BlueBird 757's MCP (autopilot panel) values: how each is set and read, the typed-input
/// rules and the words spoken. Pure; <see cref="BBS757Definition"/> wires it to the sim.
///
/// What was measured (docs/b757/evidence/live-findings.md, "MCP"):
///   • Heading <c>L:BBS_AP_TARGET_HEADING</c> and altitude <c>L:BBS_AP_TARGET_ALTITUDE</c> adopt a
///     direct write, so an exact value is one write.
///   • The speed target <c>L:BBS757_ORANGE_AIRSPEED_BUGS</c> is READ-ONLY (a direct write is
///     overwritten). It moves 1 kt per <c>K:AP_SPD_VAR_INC/DEC</c>, so a speed is set by stepping
///     the difference, and the loop reads it back and corrects (a burst of 10-11 events in one call
///     all counted; a burst of 25 is unmeasured).
///   • <c>L:BBS_AP_TARGET_VS</c> is unmeasured: written directly first, stepped by
///     <c>K:AP_VS_VAR_INC/DEC</c> when the window did not take the write.
///   • The stock autopilot SimVars sit at unrelated offsets and are never read here.
///
/// Every spoken value is the one READ back from the MCP's own variable, never the request: a set the
/// aircraft did not take is heard as it is. Numbers are formatted with the invariant culture, since
/// a comma decimal makes the RPN parser reject the string and a locale's minus sign (U+2212 in
/// sv-SE) is not a word a screen reader reads as "minus".
/// </summary>
public static class Bbs757Mcp
{
    public const string HeadingVar = "BBS_AP_TARGET_HEADING";
    public const string AltitudeVar = "BBS_AP_TARGET_ALTITUDE";
    public const string SpeedVar = "BBS757_ORANGE_AIRSPEED_BUGS";
    public const string VsVar = "BBS_AP_TARGET_VS";
    public const string MachModeVar = "BBS_AIRSPEED_IN_MACH";

    /// <summary>The wait before a set is read back (the window takes a write within a frame or two; this is generous).</summary>
    public const int ReadBackMs = 1000;

    /// <summary>The wait between a burst of K-events and the first read that judges it.</summary>
    public const int StepSettleMs = 600;

    /// <summary>
    /// After a speed burst the target is read every this many ms until two reads in a row agree: a
    /// burst can land in more than one part, and judging a half-landed one would re-send the part
    /// still to come and overshoot.
    /// </summary>
    public const int SettlePollMs = 150;

    /// <summary>The settle read gives up after this long and judges the last read it has.</summary>
    public const int SettleGiveUpMs = 1500;

    /// <summary>Most times a speed set re-sends its remainder before it speaks what it reads.</summary>
    public const int MaxSpeedCorrections = 3;

    /// <summary>
    /// Most K-events one round of a speed set sends: the whole 100 to 399 span fits (299), so a read
    /// that is merely far off still sets in one round, while a wild one cannot flood the calculator.
    /// </summary>
    public const int MaxSpeedStepsPerRound = 300;

    /// <summary>Most K-events one round of a V/S set sends: the whole -8000 to 6000 span in hundreds fits (140).</summary>
    public const int MaxVsStepsPerRound = 140;

    /// <summary>The speed target reads inside 0 to this many knots; anything else is an unreadable value, not a speed.</summary>
    public const double SpeedReadMax = 500;

    /// <summary>The V/S target reads inside these limits (feet per minute); anything else is unreadable.</summary>
    public const double VsReadMin = -8000, VsReadMax = 6000;

    public const string MachModeSetRefusal = "MCP is in Mach mode. Set Mach from the Glareshield panel.";
    public const string MachEntryRefusal = "Mach entry is not supported yet. Enter a speed from 100 to 399 knots.";
    public const string MachModeReadNote = "MCP is in Mach mode. Mach is not read yet.";

    private const string SpeedRangeError = "Speed must be between 100 and 399 knots";
    private const string VsRangeError = "Vertical speed must be between minus 8000 and 6000 feet per minute";

    // =================================================================================
    // RPN
    // =================================================================================

    /// <summary>
    /// The calculator calls that move the speed target from <paramref name="current"/> to
    /// <paramref name="target"/>: one <c>K:AP_SPD_VAR_INC</c> or <c>DEC</c> per whole knot between
    /// the two rounded values, in calls of at most <see cref="Bbs757Rpn.MaxKeysPerCall"/>, and at most
    /// <see cref="MaxSpeedStepsPerRound"/> in all (the loop sends the rest in a later round). Empty
    /// when they round to the same knot or either is not a finite number.
    /// </summary>
    public static IReadOnlyList<string> SpeedSteps(double current, double target)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return Array.Empty<string>();
        long diff = Round(target) - Round(current);
        return Steps("AP_SPD_VAR_INC", "AP_SPD_VAR_DEC", diff, MaxSpeedStepsPerRound);
    }

    /// <summary>
    /// The calls that step the V/S target from <paramref name="current"/> to <paramref name="target"/>:
    /// one <c>K:AP_VS_VAR_INC</c> or <c>DEC</c> per hundred feet per minute of the difference
    /// (assumed, unmeasured: the plan's 100 fpm per event), at most <see cref="MaxVsStepsPerRound"/>.
    /// Empty for less than half a step.
    /// </summary>
    public static IReadOnlyList<string> VsSteps(double current, double target)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return Array.Empty<string>();
        long diff = Round((target - current) / 100.0);
        return Steps("AP_VS_VAR_INC", "AP_VS_VAR_DEC", diff, MaxVsStepsPerRound);
    }

    /// <summary>Writes the heading target: 0-359, 360 and above wrap, a negative heading wraps the other way.</summary>
    public static string HeadingWrite(int deg) => Bbs757Rpn.SetLVar(HeadingVar, ((deg % 360) + 360) % 360);

    /// <summary>Writes the altitude target, rounded to the nearest hundred feet (half up).</summary>
    public static string AltitudeWrite(int ft) => Bbs757Rpn.SetLVar(AltitudeVar, RoundToHundred(ft));

    /// <summary>Writes the V/S target in feet per minute, signed.</summary>
    public static string VsWrite(int fpm) => Bbs757Rpn.SetLVar(VsVar, fpm);

    private static IReadOnlyList<string> Steps(string inc, string dec, long diff, int maxSteps)
    {
        if (diff == 0) return Array.Empty<string>();
        return Bbs757Rpn.KeyBurst(diff > 0 ? inc : dec, (int)Math.Min(Math.Abs(diff), maxSteps));
    }

    private static long Round(double value)
        => (long)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), -1e12, 1e12);

    private static int RoundToHundred(double value)
        => (int)(Math.Clamp(Math.Round(value / 100.0, MidpointRounding.AwayFromZero), -1e7, 1e7) * 100);

    // =================================================================================
    // Typed input
    // =================================================================================

    /// <summary>A typed heading, 0-360 (360 is north); <see cref="FcuValueEntry"/>'s ranges and words.</summary>
    public static bool TryHeadingInput(string? input, out int degrees, out string? error)
    {
        degrees = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        return FcuValueEntry.TryHeading(value, out degrees, out error);
    }

    /// <summary>
    /// A typed speed in knots, 100-399. <see cref="FcuValueEntry.TrySpeed"/> also takes Mach
    /// 0.10-0.99, which the prototype does not set: that is refused with its own words so the pilot
    /// is not left guessing why a valid-looking Mach number did nothing.
    /// </summary>
    public static bool TrySpeedInput(string? input, out int knots, out string? error)
    {
        knots = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        if (!FcuValueEntry.TrySpeed(value, out int internalSpeed, out _)) { error = SpeedRangeError; return false; }
        if (value < 1.0) { error = MachEntryRefusal; return false; }
        knots = internalSpeed;
        error = null;
        return true;
    }

    /// <summary>A typed altitude, 100-49000, rounded to the nearest hundred feet.</summary>
    public static bool TryAltitudeInput(string? input, out int feet, out string? error)
    {
        feet = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        if (!FcuValueEntry.TryAltitude(value, out double ft, out error)) return false;
        feet = RoundToHundred(ft);
        return true;
    }

    /// <summary>A typed vertical speed, minus 8000 to 6000 feet per minute, rounded to the nearest hundred.</summary>
    public static bool TryVerticalSpeedInput(string? input, out int fpm, out string? error)
    {
        fpm = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        if (value < -8000 || value > 6000) { error = VsRangeError; return false; }
        fpm = RoundToHundred(value);
        error = null;
        return true;
    }

    // =================================================================================
    // Phrases
    // =================================================================================

    /// <summary>"Heading 090" from the heading target; three digits, 360 reads as 000. Null or not finite: "Heading unavailable".</summary>
    public static string HeadingPhrase(double? value)
    {
        if (!IsReadable(value)) return "Heading unavailable";
        long deg = ((Round(value!.Value) % 360) + 360) % 360;
        return $"Heading {deg.ToString("D3", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"Speed 250" from the IAS target. Outside 0 to <see cref="SpeedReadMax"/> knots is not a speed: "Speed unavailable".</summary>
    public static string SpeedPhrase(double? value)
        => SpeedReadable(value) ? $"Speed {Plain(Round(value!.Value))}" : "Speed unavailable";

    /// <summary>"Altitude 12000" from the altitude target: a plain integer, no thousands separator.</summary>
    public static string AltitudePhrase(double? value)
        => IsReadable(value) ? $"Altitude {Plain(Round(value!.Value))}" : "Altitude unavailable";

    /// <summary>
    /// "Vertical speed minus 1500", "Vertical speed 1500", "Vertical speed 0" (never "minus 0").
    /// Outside <see cref="VsReadMin"/> to <see cref="VsReadMax"/> is not a vertical speed: "unavailable".
    /// </summary>
    public static string VsPhrase(double? value)
    {
        if (!VsReadable(value)) return "Vertical speed unavailable";
        long fpm = Round(value!.Value);
        return fpm < 0 ? $"Vertical speed minus {Plain(-fpm)}" : $"Vertical speed {Plain(fpm)}";
    }

    private static bool IsReadable(double? value) => value is { } v && double.IsFinite(v);

    /// <summary>
    /// A speed target read that can be believed: the step counts of a set rest on it, so a wild one
    /// (an uninitialized variable, a unit mix-up) must send nothing rather than a flood of K-events.
    /// </summary>
    private static bool SpeedReadable(double? value) => value is { } v && double.IsFinite(v) && v >= 0 && v <= SpeedReadMax;

    private static bool VsReadable(double? value) => value is { } v && double.IsFinite(v) && v >= VsReadMin && v <= VsReadMax;

    /// <summary>Two reads of the same target that agree to within half a knot (or half a step): one value, still.</summary>
    private static bool SameReading(double? a, double? b)
        => a is null || b is null ? a is null && b is null : Math.Abs(a.Value - b.Value) < 0.5;

    private static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);

    // =================================================================================
    // Set loops
    // =================================================================================

    /// <summary>Writes the heading target, waits, and speaks what the heading target then reads.</summary>
    public static async Task<string> SetHeadingAsync(McpIo io, int degrees, CancellationToken ct)
    {
        io.Write(HeadingWrite(degrees));
        await io.Delay(ReadBackMs, ct);
        return HeadingPhrase(await io.Read(HeadingVar, ct));
    }

    /// <summary>Writes the altitude target, waits, and speaks what the altitude target then reads.</summary>
    public static async Task<string> SetAltitudeAsync(McpIo io, int feet, CancellationToken ct)
    {
        io.Write(AltitudeWrite(feet));
        await io.Delay(ReadBackMs, ct);
        return AltitudePhrase(await io.Read(AltitudeVar, ct));
    }

    /// <summary>
    /// Sets the IAS target by stepping it, closing the loop on what it reads: read the target fresh,
    /// send the steps (at most <see cref="MaxSpeedStepsPerRound"/>), wait <see cref="StepSettleMs"/>,
    /// read until the value has SETTLED (<see cref="ReadSettledAsync"/>), and judge the round only on
    /// that value. While the remaining difference is non-zero and SMALLER in magnitude than before
    /// send the steps for the remainder, up to <see cref="MaxSpeedCorrections"/> times. A shrinking
    /// remainder counts as progress even when its sign flipped (an overshoot brought nearer); one that
    /// did not shrink is not chased (the aircraft is not taking the steps, the variable is being
    /// overwritten, or the burst overshot by more than it undershot). Speaks the value read last,
    /// never the request. In Mach mode nothing is sent and the pilot is told to set Mach from the
    /// panel; a target outside 0 to <see cref="SpeedReadMax"/> knots is unreadable ("Speed
    /// unavailable") and sends nothing. Throws <see cref="OperationCanceledException"/> when canceled.
    /// </summary>
    public static async Task<string> SetSpeedAsync(McpIo io, int knots, CancellationToken ct)
    {
        double? mach = await io.Read(MachModeVar, ct);
        if (mach is > 0.5) return MachModeSetRefusal;

        double? current = await io.Read(SpeedVar, ct);
        if (!SpeedReadable(current)) return SpeedPhrase(null);

        long remaining = Math.Abs(knots - Round(current!.Value));
        if (remaining == 0) return SpeedPhrase(current);

        foreach (var call in SpeedSteps(current.Value, knots)) io.Write(call);

        double? now = current;
        for (int corrections = 0; ; corrections++)
        {
            await io.Delay(StepSettleMs, ct);
            now = await ReadSettledAsync(io, SpeedVar, ct);
            if (!SpeedReadable(now)) break;

            long left = Math.Abs(knots - Round(now!.Value));
            if (left == 0 || left >= remaining || corrections == MaxSpeedCorrections) break;

            remaining = left;
            foreach (var call in SpeedSteps(now.Value, knots)) io.Write(call);
        }
        return SpeedPhrase(now);
    }

    /// <summary>
    /// Reads <paramref name="key"/> until two reads <see cref="SettlePollMs"/> apart agree, and returns
    /// that value; after <see cref="SettleGiveUpMs"/> without agreement returns the last read. A K-event
    /// burst can land in more than one part (the aircraft counts them over several frames), and a read
    /// between the parts is not a place to judge from.
    /// </summary>
    private static async Task<double?> ReadSettledAsync(McpIo io, string key, CancellationToken ct)
    {
        double? previous = await io.Read(key, ct);
        for (int waited = 0; waited < SettleGiveUpMs; waited += SettlePollMs)
        {
            await io.Delay(SettlePollMs, ct);
            double? next = await io.Read(key, ct);
            if (SameReading(previous, next)) return next;
            previous = next;
        }
        return previous;
    }

    /// <summary>
    /// Writes the V/S target, waits, reads it, and when it is not what was asked steps the
    /// difference in hundreds (the write is unmeasured on this variable; at most
    /// <see cref="MaxVsStepsPerRound"/> events), waits and reads again. Speaks what it reads last. A
    /// read outside <see cref="VsReadMin"/> to <see cref="VsReadMax"/> is unreadable: nothing is
    /// stepped from it and the pilot hears "Vertical speed unavailable".
    /// </summary>
    public static async Task<string> SetVerticalSpeedAsync(McpIo io, int fpm, CancellationToken ct)
    {
        io.Write(VsWrite(fpm));
        await io.Delay(ReadBackMs, ct);
        double? now = await io.Read(VsVar, ct);

        if (VsReadable(now))
        {
            var steps = VsSteps(now!.Value, fpm);
            if (steps.Count > 0)
            {
                foreach (var call in steps) io.Write(call);
                await io.Delay(StepSettleMs, ct);
                now = await io.Read(VsVar, ct);
            }
        }
        return VsPhrase(now);
    }

    // =================================================================================
    // Readouts
    // =================================================================================

    public static async Task<string> ReadHeadingAsync(McpIo io, CancellationToken ct)
        => HeadingPhrase(await io.Read(HeadingVar, ct));

    public static async Task<string> ReadAltitudeAsync(McpIo io, CancellationToken ct)
        => AltitudePhrase(await io.Read(AltitudeVar, ct));

    public static async Task<string> ReadVerticalSpeedAsync(McpIo io, CancellationToken ct)
        => VsPhrase(await io.Read(VsVar, ct));

    /// <summary>
    /// The IAS target, unless the MCP is in Mach mode: the window then shows Mach, and the IAS
    /// target (still held) would be passed off as what the pilot is looking at.
    /// </summary>
    public static async Task<string> ReadSpeedAsync(McpIo io, CancellationToken ct)
    {
        double? mach = await io.Read(MachModeVar, ct);
        if (mach is > 0.5) return MachModeReadNote;
        return SpeedPhrase(await io.Read(SpeedVar, ct));
    }
}
