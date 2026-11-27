using System.Globalization;
using MSFSBlindAssist.Utils;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>How a journaled control is put back: by its input event's own value, or by pressing it.</summary>
internal enum JournalKind { InputEvent, Press }

/// <summary>
/// One control's start, written before the sweep's first write. An input-event control (a selector, a guard, a
/// continuous control) keeps its input event's OWN value (<c>B:&lt;Event&gt;</c>) in <see cref="EventValue"/>, which
/// is what the restore writes back: never its read translated through the pairing being tested. A pressed control (a
/// key) keeps its state's reading and, for a multi-press key, how many presses bring it round
/// (<see cref="PressesToCycle"/>). <see cref="Read"/> and <see cref="ReadValue"/> are the map's read and its value,
/// which the restore also checks.
/// </summary>
internal sealed record JournalEntry(string Key, JournalKind Kind, string Event, double? EventValue, string? Read, double? ReadValue, int? PressesToCycle = null);

/// <summary>The journal file: tab-separated, one <c>start</c> line a control, <c>#</c> comments. Numbers are
/// written round-trip in the invariant culture (CORE-21), so a restore writes back exactly what was read.</summary>
internal static class SweepJournal
{
    private const string InputEventWord = "input-event", PressWord = "press";

    public static string Header() =>
        "# A330 panel sweep journal: every planned control's start, written before the first write.\n" +
        "# Put them back after a crash or Ctrl+C with: A330PanelSweep --restore <this file>\n" +
        "# start <TAB> key <TAB> input-event <TAB> event <TAB> its own value <TAB> read <TAB> read value\n" +
        "# start <TAB> key <TAB> press <TAB> event <TAB> presses to cycle <TAB> state <TAB> state value\n";

    public static string Line(JournalEntry e)
    {
        string third = e.Kind == JournalKind.InputEvent ? Num(e.EventValue) : e.PressesToCycle?.ToString(CultureInfo.InvariantCulture) ?? "";
        return string.Join('\t', "start", e.Key, e.Kind == JournalKind.InputEvent ? InputEventWord : PressWord, e.Event, third, e.Read ?? "", Num(e.ReadValue)) + "\n";
    }

    public static List<JournalEntry> Parse(string text)
    {
        var entries = new List<JournalEntry>();
        int lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            string line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var f = line.Split('\t');
            if (f.Length != 7 || f[0] != "start" || f[1].Length == 0 || (f[2] != InputEventWord && f[2] != PressWord))
                throw new FormatException($"journal line {lineNo}: expected 'start' and six tab-separated fields, got '{line}'");
            var kind = f[2] == InputEventWord ? JournalKind.InputEvent : JournalKind.Press;
            double? eventValue = null;
            int? presses = null;
            if (kind == JournalKind.InputEvent)
            {
                if (f[3].Length == 0 || !NumberParsing.TryParseExternal(f[4], out double ev))
                    throw new FormatException($"journal line {lineNo}: an input-event line needs its event and its own value");
                eventValue = ev;
            }
            else if (f[4].Length > 0)
            {
                if (!int.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out int p))
                    throw new FormatException($"journal line {lineNo}: presses to cycle '{f[4]}' is not a count");
                presses = p;
            }
            double? readValue = f[6].Length == 0 ? null
                : NumberParsing.TryParseExternal(f[6], out double rv) ? rv
                : throw new FormatException($"journal line {lineNo}: read value '{f[6]}' is not a number");
            entries.Add(new JournalEntry(f[1], kind, f[3], eventValue, f[5].Length == 0 ? null : f[5], readValue, presses));
        }
        return entries;
    }

    /// <summary>The journal line for a control at its start, or null when it could not be put back: an input-event
    /// control whose own value was not read, a key whose state was not read, a control with neither.</summary>
    public static JournalEntry? EntryFor(Control c, double? eventValue, double? readValue)
    {
        if (c.Kind == "button")
            return c.State is { Length: > 0 } state && readValue != null
                ? new JournalEntry(c.Key, JournalKind.Press, c.Event ?? "", null, state, readValue, c.PressesToCycle)
                : null;
        if (string.IsNullOrEmpty(c.Event) || eventValue == null) return null;
        return new JournalEntry(c.Key, JournalKind.InputEvent, c.Event, eventValue, c.Read, readValue);
    }

    private static string Num(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "";
}

/// <summary>What the restore does next for one journaled control.</summary>
internal abstract record RestoreStep
{
    /// <summary>It reads its start: put back.</summary>
    public sealed record Held : RestoreStep;

    /// <summary>It cannot be put back (or did not come back): the sweep stops.</summary>
    public sealed record Failed(string Why) : RestoreStep;

    /// <summary>Send this, let it settle, then ask again.</summary>
    public sealed record Write(A330WritePlan Plan) : RestoreStep;
}

/// <summary>
/// Puts a journaled control back, one step at a time against fresh readings. An input-event control is written its
/// input event's own starting value (SetInputEvent), at most <see cref="MaxWrites"/> times, and holds once that value
/// and its read (where one was journaled) are both back. A key is pressed through the app's own press
/// (<see cref="A330Write.Press"/>) until its state reads its start, at most <c>PressesToCycle - 1</c> times; a key with
/// no count is a push-on/push-off key, so one press. Pure.
/// </summary>
internal static class JournalRestore
{
    public const int MaxWrites = 2;

    public static RestoreStep Next(JournalEntry e, Control c, double? eventNow, double? readNow, int actionsSoFar)
    {
        if (e.Kind == JournalKind.InputEvent)
        {
            if (eventNow == null) return new RestoreStep.Failed($"{e.Event}'s own value could not be read");
            bool eventBack = SweepTiming.Same(eventNow, e.EventValue);
            bool readBack = e.ReadValue == null || SweepTiming.Same(readNow, e.ReadValue);
            if (eventBack && readBack) return new RestoreStep.Held();
            if (actionsSoFar >= MaxWrites)
                return new RestoreStep.Failed($"after {actionsSoFar} writes {e.Event} reads {SweepTsv.Num(eventNow)} (start {SweepTsv.Num(e.EventValue)})"
                    + (e.Read != null ? $", {e.Read} reads {SweepTsv.Num(readNow)} (start {SweepTsv.Num(e.ReadValue)})" : ""));
            return new RestoreStep.Write(A330WritePlan.Of(new A330InputEventCall(e.Event, e.EventValue!.Value)));
        }

        if (readNow == null) return new RestoreStep.Failed($"{e.Read} could not be read");
        if (SweepTiming.Same(readNow, e.ReadValue)) return new RestoreStep.Held();
        int allowed = (e.PressesToCycle is > 1 ? e.PressesToCycle.Value : 2) - 1;
        if (actionsSoFar >= allowed)
            return new RestoreStep.Failed($"after {actionsSoFar} presses {e.Read} reads {SweepTsv.Num(readNow)} (start {SweepTsv.Num(e.ReadValue)})");
        var press = A330Write.Press(c, 1);
        return press.Outcome == A330WriteOutcome.Writes ? new RestoreStep.Write(press) : new RestoreStep.Failed("the app has no press for it");
    }

    /// <summary>The journal's entries in the order a full restore puts them back: last first, so a switch is put back
    /// before the guard over it is closed.</summary>
    public static IEnumerable<JournalEntry> RestoreOrder(IReadOnlyList<JournalEntry> entries) => entries.Reverse();
}
