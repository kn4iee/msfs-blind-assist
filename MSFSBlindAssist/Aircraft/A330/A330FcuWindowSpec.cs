using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>A button of an FCU value window: its label (no state, no Alt letter), what a screen reader
/// is told about it, and the press. The window never speaks after a press; the aircraft's own
/// call-outs say what happened.</summary>
public sealed record A330FcuButton(string Label, string Description, Action Press);

/// <summary>What Enter in the value box decided: an error to speak (and Set is null), or a Set to run.</summary>
public sealed record A330FcuCommit(string? Error, Action? Set);

/// <summary>One of the A330's four FCU value windows (Ctrl+H, Ctrl+S, Ctrl+A, Ctrl+V), laid out and
/// worded as the Fenix A320's: built from this spec by IniA330FcuValueWindow, and testable without a form.</summary>
public sealed record A330FcuWindowSpec(
    string Title,
    string FieldLabel,
    string LabelName,
    string ValueName,
    string ValueDescription,
    IReadOnlyList<A330FcuButton> Buttons,
    Func<string, A330FcuCommit> Commit)
{
    /// <summary>The Fenix windows' check, in their order: empty box, not a number, out of range, then a set.
    /// <paramref name="article"/> is "a" or "an" for "Please enter ... value"; <paramref name="rangeError"/>
    /// returns the message for an out-of-range number, or null when it is in range.</summary>
    public static A330FcuCommit Check(string? typed, string article, string noun,
        Func<double, string?> rangeError, Func<double, Action> set)
    {
        string input = (typed ?? "").Trim();
        if (input.Length == 0) return new($"Please enter {article} {noun} value", null);
        if (!NumberParsing.TryParseTyped(input, out double value)) return new("Invalid number format", null);
        if (rangeError(value) is { } message) return new(message, null);
        return new(null, set(value));
    }
}
