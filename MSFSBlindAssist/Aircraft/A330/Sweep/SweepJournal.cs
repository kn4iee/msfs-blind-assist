using System.Globalization;
using MSFSBlindAssist.Utils;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>How a journaled control is put back: by its input event's own value, by pressing it, for a family of
/// keys that share one state by pressing the one key that selects the start, or, for a push-button the aircraft
/// toggles, by its read (pressed once when the read differs).</summary>
internal enum JournalKind { InputEvent, Press, Family, Toggle }

/// <summary>
/// One control's start, written before the sweep's first write. An input-event control (a selector, a guard, a
/// continuous control) keeps its input event's OWN value (<c>B:&lt;Event&gt;</c>) in <see cref="EventValue"/>, which
/// is what the restore writes back: never its read translated through the pairing being tested. A pressed control (a
/// key) keeps its state's reading and, for a multi-press key, how many presses bring it round
/// (<see cref="PressesToCycle"/>). A <see cref="KeyFamily"/> keeps its shared state's start once, keyed by the state.
/// A push-button the aircraft toggles keeps its read's value, which is what its restore compares; its input event's
/// own value is kept for the record only (it rests at 0 whatever the switch shows), and may be missing.
/// <see cref="Read"/> and <see cref="ReadValue"/> are the map's read and its value, which the restore also checks.
/// </summary>
internal sealed record JournalEntry(string Key, JournalKind Kind, string Event, double? EventValue, string? Read, double? ReadValue, int? PressesToCycle = null);

/// <summary>The journal file: tab-separated, one <c>start</c> line a control, <c>#</c> comments. Numbers are
/// written round-trip in the invariant culture (CORE-21), so a restore writes back exactly what was read.</summary>
internal static class SweepJournal
{
    private const string InputEventWord = "input-event", PressWord = "press", FamilyWord = "family", ToggleWord = "toggle";

    public static string Header() =>
        "# A330 panel sweep journal: every planned control's start, written before the first write.\n" +
        "# Put them back after a crash or Ctrl+C with: A330PanelSweep --restore <this file>\n" +
        "# start <TAB> key <TAB> input-event <TAB> event <TAB> its own value <TAB> read <TAB> read value\n" +
        "# start <TAB> key <TAB> press <TAB> event <TAB> presses to cycle <TAB> state <TAB> state value\n" +
        "# start <TAB> state <TAB> family <TAB> <TAB> <TAB> state <TAB> state value (keys that share one state)\n" +
        "# start <TAB> key <TAB> toggle <TAB> event <TAB> its own value (for the record) <TAB> read <TAB> read value (put back by the read)\n";

    public static string Line(JournalEntry e)
    {
        string third = e.Kind switch
        {
            JournalKind.InputEvent or JournalKind.Toggle => Num(e.EventValue),
            JournalKind.Press => e.PressesToCycle?.ToString(CultureInfo.InvariantCulture) ?? "",
            _ => "",
        };
        string word = e.Kind switch
        {
            JournalKind.InputEvent => InputEventWord, JournalKind.Press => PressWord, JournalKind.Toggle => ToggleWord, _ => FamilyWord,
        };
        return string.Join('\t', "start", e.Key, word, e.Event, third, e.Read ?? "", Num(e.ReadValue)) + "\n";
    }

    /// <summary>A family's journal line, or null when it could not be put back: its start was not read, or no key is
    /// known to select it.</summary>
    public static JournalEntry? FamilyEntry(KeyFamily f, double? start) =>
        start is double s && f.KnownKeyFor(s) != null ? new JournalEntry(f.Key, JournalKind.Family, "", null, f.State, s) : null;

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
            if (f.Length != 7 || f[0] != "start" || f[1].Length == 0 || (f[2] != InputEventWord && f[2] != PressWord && f[2] != FamilyWord && f[2] != ToggleWord))
                throw new FormatException($"journal line {lineNo}: expected 'start' and six tab-separated fields, got '{line}'");
            var kind = f[2] switch
            {
                InputEventWord => JournalKind.InputEvent, PressWord => JournalKind.Press, ToggleWord => JournalKind.Toggle, _ => JournalKind.Family,
            };
            double? eventValue = null;
            int? presses = null;
            if (kind == JournalKind.InputEvent)
            {
                if (f[3].Length == 0 || !NumberParsing.TryParseExternal(f[4], out double ev))
                    throw new FormatException($"journal line {lineNo}: an input-event line needs its event and its own value");
                eventValue = ev;
            }
            else if (kind == JournalKind.Toggle)
            {
                if (f[3].Length == 0 || f[5].Length == 0 || f[6].Length == 0)
                    throw new FormatException($"journal line {lineNo}: a toggle line needs its event, its read and the read's value");
                if (f[4].Length > 0)
                {
                    if (!NumberParsing.TryParseExternal(f[4], out double own))
                        throw new FormatException($"journal line {lineNo}: its event's own value '{f[4]}' is not a number");
                    eventValue = own;
                }
            }
            else if (kind == JournalKind.Family)
            {
                if (f[5].Length == 0 || f[6].Length == 0)
                    throw new FormatException($"journal line {lineNo}: a family line needs its state and its start");
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
        // a push-button the aircraft toggles is put back by its read alone: without one it cannot be put back
        if (A330Write.PressedByValue(c))
            return c.Read is { Length: > 0 } read && readValue != null
                ? new JournalEntry(c.Key, JournalKind.Toggle, c.Event!, eventValue, read, readValue)
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
/// no count is a push-on/push-off key, so one press. A push-button the aircraft toggles (<see cref="A330Write.PressedByValue"/>,
/// whatever kind of line the journal gave it, so an older journal's input-event line too) is put back by its READ: held
/// when the read is its start, else pressed once by value through the app's own decision, and failed if that did not
/// hold. Its input event's own value is never compared: it rests at 0 whatever the switch shows. Pure.
/// </summary>
internal static class JournalRestore
{
    public const int MaxWrites = 2;

    /// <summary>A push-button that toggles is pressed at most once to put it back: a second press would undo the first.</summary>
    public const int MaxTogglePresses = 1;

    public static RestoreStep Next(JournalEntry e, Control c, double? eventNow, double? readNow, int actionsSoFar)
    {
        // a family is put back by pressing the one key that selects its start (SweepSession), never key by key
        if (e.Kind == JournalKind.Family) return new RestoreStep.Failed("a family of keys is put back as one unit");
        if (e.Kind is JournalKind.InputEvent or JournalKind.Toggle && A330Write.PressedByValue(c)) return ByRead(e, c, readNow, actionsSoFar);
        if (e.Kind == JournalKind.Toggle) return new RestoreStep.Failed($"the journal has {e.Key} as a push-button that toggles, and the map no longer does: put it back by hand");
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

    /// <summary>A push-button that toggles, put back by its read alone.</summary>
    private static RestoreStep ByRead(JournalEntry e, Control c, double? readNow, int pressesSoFar)
    {
        if (e.Read == null || e.ReadValue is not double start)
            return new RestoreStep.Failed("the journal has no read for it, and its input event's own value says nothing about the switch");
        if (readNow == null) return new RestoreStep.Failed($"{e.Read} could not be read");
        if (SweepTiming.Same(readNow, start)) return new RestoreStep.Held();
        if (pressesSoFar >= MaxTogglePresses)
            return new RestoreStep.Failed($"after {pressesSoFar} press {e.Read} reads {SweepTsv.Num(readNow)} (start {SweepTsv.Num(start)}); not pressed again");
        var press = A330Write.ToPosition(c, readNow, start);
        return press.Outcome switch
        {
            A330WriteOutcome.Writes => new RestoreStep.Write(press),
            A330WriteOutcome.AtPosition => new RestoreStep.Failed(
                $"{e.Read} reads {SweepTsv.Num(readNow)}, which the app takes for its start ({SweepTsv.Num(start)}) and will not press; put it back by hand"),
            _ => new RestoreStep.Failed("the app has no press for it"),
        };
    }

    /// <summary>The journal's entries in the order a full restore puts them back: last first, so a switch is put back
    /// before the guard over it is closed.</summary>
    public static IEnumerable<JournalEntry> RestoreOrder(IReadOnlyList<JournalEntry> entries) => entries.Reverse();
}
