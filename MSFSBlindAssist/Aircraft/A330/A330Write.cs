using System.Globalization;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>What a panel pick should do to the A330.</summary>
internal enum A330WriteOutcome
{
    /// <summary>The control already reads the picked position: nothing is sent.</summary>
    AtPosition,

    /// <summary>Send <see cref="A330WritePlan.Calls"/>, in order.</summary>
    Writes,

    /// <summary>Nothing can be sent for this pick: the pilot is told "{label} unavailable".</summary>
    Refused,
}

/// <summary>One write of a plan.</summary>
internal abstract record A330Call;

/// <summary>An input event set to a value (<c>SetInputEvent</c>).</summary>
internal sealed record A330InputEventCall(string Event, double Value) : A330Call;

/// <summary>A momentary press: the event's own <c>_Push</c>, then <c>_Release</c> after the hold (<c>PushEvent</c>).</summary>
internal sealed record A330PressCall(string Event, int HoldMs) : A330Call;

/// <summary>A press of a push-button the aircraft toggles: ONE <c>SetInputEvent</c> of its input event to
/// <paramref name="Press"/>, and nothing after it. The model the 2026-11-27 sweep and its 23:05 trial fit: every set to
/// these events is a press (one flip), whatever the value, so a set of 1, a hold and a set of 0 was two presses and left
/// the switch where it was (what was measured on which keys is in tools/a330-gen/README.md).</summary>
internal sealed record A330InputEventPressCall(string Event, double Press) : A330Call;

/// <summary>A calculator line, sent through the sequence-prefixed <c>Write</c>.</summary>
internal sealed record A330CalcCall(string Rpn) : A330Call;

internal sealed record A330WritePlan(A330WriteOutcome Outcome, IReadOnlyList<A330Call> Calls, string? Reason = null)
{
    public static readonly A330WritePlan AtPosition = new(A330WriteOutcome.AtPosition, Array.Empty<A330Call>());
    public static readonly A330WritePlan Refused = new(A330WriteOutcome.Refused, Array.Empty<A330Call>());

    /// <summary>A true toggle whose reading is unknown: a press flips whatever it is, so nothing is sent.</summary>
    public static readonly A330WritePlan Unread = new(A330WriteOutcome.Refused, Array.Empty<A330Call>(), "its position is not read yet");
    public static A330WritePlan Of(params A330Call[] calls) => new(A330WriteOutcome.Writes, calls);
}

/// <summary>
/// The decision every selector pick makes, kept pure so it is pinned without a sim (modelled on the
/// BlueBird 757's write planner). The control's row is a continuous subscription, so its cached value
/// is the read: a pick that the control already reads sends nothing; a control not read yet is
/// written, as before.
/// </summary>
internal static class A330Write
{
    /// <summary>How close a reading is to a position's value to be that position.</summary>
    private const double PositionTolerance = 0.01;

    /// <summary>A toggle's reading is a flag: anything within half a step is the same position.</summary>
    private const double ToggleTolerance = 0.5;

    /// <summary>Invariant fixed-point, as the calculator parser needs ([VAR-5]).</summary>
    internal static string Fixed(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool Known(double? v) => v is double d && double.IsFinite(d);

    /// <summary>A push-button the aircraft toggles (measured 2026-11-27): a <c>toggle</c> with <c>press</c>, an input
    /// event and no calculator line. It is pressed by one set of its <c>press</c> value, and its state is its read
    /// alone: its input event's own value says nothing (all 73 started at 0, whatever the switch showed). The app's
    /// pick, the First Officer's route and the panel sweep all take this one definition.</summary>
    public static bool PressedByValue(A330ControlMap.Control c) =>
        c is { Mode: "toggle", Press: not null } && string.IsNullOrWhiteSpace(c.Rpn) && !string.IsNullOrEmpty(c.Event);

    /// <summary>The plan for picking <paramref name="target"/> (a position's read value) on a map
    /// selector, from the cached reading <paramref name="current"/> (null or not finite: unread).</summary>
    public static A330WritePlan ToPosition(A330ControlMap.Control c, double? current, double target)
    {
        if (!double.IsFinite(target)) return A330WritePlan.Refused;
        switch (c.Mode)
        {
            case "toggle":
                if (Known(current) && Math.Abs(current!.Value - target) <= ToggleTolerance) return A330WritePlan.AtPosition;
                // a true toggle (no by-value code) flips whatever it is: unread is unknown, never "differs" ([FO-18])
                if (!Known(current) && c.Rpn?.Contains("{value}", StringComparison.Ordinal) != true) return A330WritePlan.Unread;
                // {value} = the pick (the engine masters pass it to their input event's Set, right whether
                // that Set toggles or sets by value); a press-flag rpn carries none
                if (!string.IsNullOrWhiteSpace(c.Rpn)) return A330WritePlan.Of(new A330CalcCall(c.Rpn.Replace("{value}", Fixed(target))));
                if (string.IsNullOrEmpty(c.Event)) return A330WritePlan.Refused;
                // a push-button the aircraft toggles (measured 2026-11-27): one press, ONE set of its press value through
                // its input event, whichever way it goes; its positions' inputs drive no write
                if (PressedByValue(c))
                    return A330WritePlan.Of(new A330InputEventPressCall(c.Event, c.Press!.Value));
                return A330WritePlan.Of(new A330PressCall(c.Event, c.HoldMs ?? IniA330Definition.DefaultPushMs));
            default:
                if (string.IsNullOrEmpty(c.Event)) return A330WritePlan.Refused;
                // a unit control's pick is one of its steps and its reading is continuous: within half a step is
                // the same step (the nearest one); its input is the step's own value
                double tolerance = c is { Kind: "continuous", Unit: not null } ? (c.Step is > 0 ? c.Step.Value : 1) / 2 : PositionTolerance;
                if (Known(current) && Math.Abs(current!.Value - target) < tolerance) return A330WritePlan.AtPosition;
                return A330WritePlan.Of(new A330InputEventCall(c.Event, A330ControlMap.InputFor(c, target) ?? target));
        }
    }

    /// <summary>A key's press: a <c>push</c> control's own <c>_Push</c> then <c>_Release</c> after its hold, an
    /// <c>rpn</c> control's calculator line (<paramref name="value"/> = the pick, normally 1).</summary>
    public static A330WritePlan Press(A330ControlMap.Control c, double value)
    {
        if (c.Mode == "rpn")
            return string.IsNullOrWhiteSpace(c.Rpn) ? A330WritePlan.Refused
                : A330WritePlan.Of(new A330CalcCall(c.Rpn.Replace("{value}", Fixed(value))));
        if (string.IsNullOrEmpty(c.Event)) return A330WritePlan.Refused;
        return A330WritePlan.Of(new A330PressCall(c.Event, c.HoldMs ?? IniA330Definition.DefaultPushMs));
    }

    /// <summary>What a key with a state says after its press: the state row's description of what it reads,
    /// bare, as MainForm's button state read-back says it for the FlyByWire A32NX and A380
    /// (<c>AnnounceVariableState</c>, MainForm.Announcers.cs), with its number fallback in the invariant culture
    /// (CORE-21).</summary>
    public static string StateSentence(SimVarDefinition stateRow, double read) =>
        stateRow.ValueDescriptions.TryGetValue(stateRow.DescriptionKeyFor(read), out var described)
            ? described
            : FormattableString.Invariant($"{stateRow.DisplayName}: {read}");

    /// <summary>What a key with no readable state says once, as the base class's momentary press and the Fenix's
    /// press buttons with no light say it (<c>PulseMomentaryLVar</c>, <c>ExecuteButtonTransition</c>'s
    /// <c>confirmPress</c>).</summary>
    public static string PressedSentence(string label) => $"{label} pressed";

    /// <summary>The parking brake lever: input 0 sets it, 1 releases it ([INI-1]); <paramref name="current"/>
    /// is the status row (1 = set).</summary>
    public static A330WritePlan ParkingBrake(double? current, bool set)
    {
        if (Known(current) && (current!.Value > 0.5) == set) return A330WritePlan.AtPosition;
        return A330WritePlan.Of(new A330InputEventCall("AIRLINER_PARKINGBRAKE", IniA330Definition.ParkingBrakeInput(set)));
    }

    /// <summary>What is said when a pick did not land: the row's label and the position it reads, the form
    /// the generic announce of an announced row speaks ("{DisplayName}: {description}", MainForm.Announcers.cs)
    /// and its number fallback, invariant so no PC culture changes it (CORE-21).</summary>
    public static string PositionSentence(SimVarDefinition def, double read)
    {
        string position = def.ValueDescriptions.TryGetValue(def.DescriptionKeyFor(read), out var described)
            ? described : Fixed(read);
        return $"{def.DisplayName}: {position}";
    }
}

/// <summary>The sim access of a panel pick, replaced by tests (a <see cref="SimConnectManager"/> cannot write
/// or read fresh in them). The default is <c>IniA330Definition.IoFor</c>.</summary>
internal sealed class A330PanelIo
{
    public required Func<bool> CanCalc { get; init; }
    public required Func<string, double, bool> TrySetInput { get; init; }
    public required Action<string, int> Press { get; init; }
    public required Action<string> Calc { get; init; }
    public required Func<string, double?> Cached { get; init; }
    public required Func<string, CancellationToken, Task<double?>> ReadFresh { get; init; }

    /// <summary>A forced read of the row, delivered to MainForm like any other (its combo follows).</summary>
    public required Action<string> Request { get; init; }

    public required Func<int, Task> Delay { get; init; }
}
