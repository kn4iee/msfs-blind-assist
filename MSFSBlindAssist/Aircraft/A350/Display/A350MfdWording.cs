using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The MFD as the pilot hears it, as the A380's MFD window says its own (plan 25; owner, 2026-11-12: "the colours would have
/// to be removed completely from the MFD"). <see cref="A350MfdPage.Read"/> keeps its colours for its own logic (a grey key, a
/// message read without its box); here a colour becomes the owner's word where it asks something of the pilot, and nothing
/// elsewhere: amber on a field "required" (an entry the FMS needs; once, the box's own "required" being enough), yellow
/// "temporary" (never beside the word TMPY), magenta "constraint". The reader writes its colours in lowercase after a comma,
/// in brackets after a value, or as a note after an F-PLN row ("ALT magenta", "ident white"); the page's own texts are
/// capitals ("Green dot" is a label of the reader's), so nothing of the page's is ever taken for one.
/// </summary>
public static class A350MfdWording
{
    private const string Colour = "white|green|cyan|amber|magenta|yellow|grey|black|red|blue";

    /// <summary>An F-PLN row's note: a cell's name and the colour it is drawn in ("ALT magenta", "ident white").</summary>
    private static readonly Regex Note = new($@", (?<name>[A-Za-z][A-Za-z0-9/]*(?: [A-Za-z0-9/]+)*) (?<c>{Colour})(?=,|;|$)", RegexOptions.Compiled);

    /// <summary>A value's colour in brackets ("LOCAL ETA AT DEST (white)").</summary>
    private static readonly Regex Bracketed = new($@" \((?<c>{Colour})\)", RegexOptions.Compiled);

    /// <summary>A colour of its own after a comma, at the end or before the next part.</summary>
    private static readonly Regex Segment = new($@", (?<c>{Colour})(?=,|;|$)", RegexOptions.Compiled);

    private static readonly Regex Required = new(@"\brequired\b", RegexOptions.Compiled);

    /// <summary>What the pilot hears of a line, without the window's role word (<see cref="A350MfdPage.Shown"/> adds it).</summary>
    public static string Speak(MfdLine line) => Speak(line.Kind, line.Spoken);

    /// <summary>What the pilot hears of a line of <paramref name="kind"/> the reader spoke as <paramref name="spoken"/>.</summary>
    public static string Speak(string kind, string spoken)
    {
        string s = Note.Replace(spoken, m => m.Groups["c"].Value == "magenta" ? $", {m.Groups["name"].Value} constraint" : "");
        s = Bracketed.Replace(s, "");
        s = Segment.Replace(s, m => Word(kind, spoken, m.Groups["c"].Value) is { } word ? ", " + word : "");
        return s.Trim();
    }

    /// <summary>The owner's word for a colour, or null for none.</summary>
    private static string? Word(string kind, string spoken, string colour) => colour switch
    {
        "amber" when kind == "field" && !Required.IsMatch(spoken) => "required",
        "yellow" when !spoken.Contains("TMPY", StringComparison.Ordinal) => "temporary",
        "magenta" => "constraint",
        _ => null,
    };

    /// <summary>A grey control, or one the reader read unavailable: the A380 window says "dimmed".</summary>
    public static bool IsDimmed(MfdLine line) =>
        Regex.IsMatch(line.Spoken, @", grey(?=,|;|$)") || line.Spoken.EndsWith(", unavailable", StringComparison.Ordinal);

    private const string ScratchpadPrefix = "Scratchpad: ";

    /// <summary>An FMS message as the A380 window says its footer: the message alone.</summary>
    public static string Message(string pad) =>
        Speak("message", pad.StartsWith(ScratchpadPrefix, StringComparison.Ordinal) ? pad[ScratchpadPrefix.Length..] : pad);
}
