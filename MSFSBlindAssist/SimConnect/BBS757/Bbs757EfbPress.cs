using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The press pipeline's decisions, pure (plan 18, Task 5). The bridge does the capturing, tapping and reading; what it decides
/// from the reads lives here, where it is tested without a tablet: finding the control the pilot pressed again in a fresh read
/// (<see cref="Relocate"/>), whether a switch still needs its tap (<see cref="NeedsTap"/>), where to tap a slider with choices
/// and where to tap next (<see cref="ChoiceX"/>, <see cref="NextChoiceX"/>), whether a press changed the page
/// (<see cref="PageUnchanged"/>, <see cref="ButtonOutcome"/>), and the words said when it did not go through. Nothing here
/// refuses a press (the owner's ruling of 2026-10-29): a decision says where a control is now, whether its tap is still wanted,
/// or what became of the press.
/// </summary>
public static class Bbs757EfbPress
{
    /// <summary>A slider with choices is tapped at most this many times for one wish: its place, then steps toward it.</summary>
    public const int MaxSliderTaps = 4;

    /// <summary>The smallest step toward a choice, in tablet pixels (a slider with very many choices would step less).</summary>
    private const int MinStepPx = 4;

    // ------------------------------------------------------------------ the page as one string

    /// <summary>
    /// What a page says, as one string: two reads that say the same thing give the same one. It covers the page's name, its menu
    /// and controls (kind, key, words, value, state, selection), the question over it, the top bar and the notifications; not where
    /// anything is, so a pixel of wobble changes nothing. The bridge pushes a read only when this moved, so a notification or a
    /// question appearing over a page that did not change counts as a change.
    /// </summary>
    public static string Fingerprint(Bbs757EfbScreenModel m) => Describe(m, withBars: true);

    /// <summary>
    /// Whether the page itself is the same in two reads: <see cref="Fingerprint"/> without the top bar (the departure countdown, the
    /// boarding control, the notification count) and the notifications, which come and go over any page. A question appearing is a
    /// change.
    /// </summary>
    public static bool PageUnchanged(Bbs757EfbScreenModel before, Bbs757EfbScreenModel after) =>
        string.Equals(Describe(before, withBars: false), Describe(after, withBars: false), StringComparison.Ordinal);

    private static string Describe(Bbs757EfbScreenModel m, bool withBars)
    {
        var sb = new StringBuilder(m.PageName).Append('\n');
        foreach (var i in m.Menu.Concat(m.Items)) sb.Append(Line(i));
        if (m.Question is { } q)
        {
            sb.Append("question|").Append(q.Text).Append('\n');
            foreach (var b in q.Buttons) sb.Append(Line(b));
        }
        if (withBars)
        {
            sb.Append("top bar\n");
            foreach (var i in m.TopBar) sb.Append(Line(i));
            sb.Append("notices\n");
            foreach (var n in m.Notices) sb.Append(n.Time).Append('|').Append(n.Text).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>One control as the fingerprint says it: what the pilot would hear change.</summary>
    private static string Line(Bbs757EfbItem i) =>
        new StringBuilder().Append(i.Kind).Append('|').Append(i.Key).Append('|').Append(i.Text).Append('|')
            .Append(i.Value).Append('|').Append(i.On).Append('|').Append(i.Selected).Append('\n').ToString();

    // ------------------------------------------------------------------ finding a pressed control again

    /// <summary>
    /// The control the pilot pressed, found again in <paramref name="now"/>, a fresh read of the tablet taken after the page
    /// <paramref name="before"/> was read and the press was chosen from it; null when it is not there (a notification covers it, or
    /// the page changed under the pilot). Searched in <paramref name="now"/>'s menu, controls, top bar and question buttons, by the
    /// first rule that finds one:
    /// <list type="number">
    /// <item>none, when the two reads are of different pages;</item>
    /// <item>the control with the same key;</item>
    /// <item>the control of the same kind whose box overlaps the pressed one's with intersection over union of at least 0.6: a
    /// tile changing its words in place ("Req fuel truck" becoming "Requesting...") keeps its box, give or take the few pixels the
    /// reader's box varies by from one capture to the next; the best overlap wins;</item>
    /// <item>the control of the same kind that overlaps the pressed box at all and has the same words once the state word that ends
    /// them is left out ("Fuel truck off", "Chocks, on"): the tile moved a little, which changes its key and most of its box;</item>
    /// <item>none.</item>
    /// </list>
    /// A neighbour with other words that covers the place in part is never taken: rule 3 wants it to be the same box, rule 4 the
    /// same words. Tapping it would press a different control than the one asked for.
    /// </summary>
    public static Bbs757EfbItem? Relocate(Bbs757EfbScreenModel before, Bbs757EfbScreenModel now, Bbs757EfbItem pressed)
    {
        if (!SamePage(before, now)) return null;
        var controls = now.Menu.Concat(now.Items).Concat(now.TopBar).Concat(now.Question?.Buttons ?? Array.Empty<Bbs757EfbItem>()).ToList();

        foreach (var c in controls)
            if (string.Equals(c.Key, pressed.Key, StringComparison.Ordinal)) return c;

        var covering = new List<Cover>();
        foreach (var c in controls)
        {
            if (c.Kind != pressed.Kind) continue;
            var (inter, union) = Overlap(pressed.Box, c.Box);
            if (inter > 0) covering.Add(new Cover(c, inter, union));
        }

        // 0.6 as 3/5, so that exactly 0.6 is in and no floating point decides it.
        var inPlace = covering.Where(c => 5 * c.Intersection >= 3 * c.Union).OrderByDescending(c => (double)c.Intersection / c.Union).FirstOrDefault();
        if (inPlace != null) return inPlace.Item;

        var words = StatelessWords(pressed.Text);
        if (words.Length == 0) return null;
        return covering.Where(c => words.SequenceEqual(StatelessWords(c.Item.Text), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Intersection).FirstOrDefault()?.Item;
    }

    private sealed record Cover(Bbs757EfbItem Item, long Intersection, long Union);

    private static bool SamePage(Bbs757EfbScreenModel a, Bbs757EfbScreenModel b) =>
        string.Equals(a.PageName, b.PageName, StringComparison.Ordinal);

    /// <summary>The area two boxes share and the area they cover together. A box with no area (the window's own Home button has
    /// none) shares nothing with any other.</summary>
    private static (long Intersection, long Union) Overlap(Rectangle a, Rectangle b)
    {
        var both = Rectangle.Intersect(a, b);
        long inter = (long)both.Width * both.Height;
        return (inter, (long)a.Width * a.Height + (long)b.Width * b.Height - inter);
    }

    private static readonly Regex WordRun = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    /// <summary>A control's words (letters and digits, in order) without the state word that ends them
    /// (<see cref="Bbs757EfbScreen.StateWords"/>): "Chocks, on" is Chocks, "Left FWD: Closed" is Left FWD. A state word alone is no
    /// words ("Open" stays Open), and "on" ends only a word of its own, never "Common".</summary>
    private static string[] StatelessWords(string text)
    {
        var words = WordRun.Matches(text).Select(m => m.Value).ToList();
        if (words.Count > 1 && Bbs757EfbScreen.StateWords.Contains(words[^1], StringComparer.OrdinalIgnoreCase))
            words.RemoveAt(words.Count - 1);
        return words.ToArray();
    }

    // ------------------------------------------------------------------ a question

    /// <summary>A question's words as two reads compare them: its letters and digits, upper-cased invariantly. Case, spaces and
    /// punctuation wobble from one read to the next ("defuel?", "defuel ?"); the words do not.</summary>
    public static string QuestionWords(string text) => string.Concat(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    /// <summary>Whether two questions are the same question: both there, and their words the same (<see cref="QuestionWords"/>).
    /// Every question's answers are keyed alike (q:YES, q:NO), so an answer is the one pressed only under the same question.</summary>
    public static bool SameQuestion(Bbs757EfbQuestion? a, Bbs757EfbQuestion? b) =>
        a != null && b != null && QuestionWords(a.Text) == QuestionWords(b.Text);

    // ------------------------------------------------------------------ a switch

    /// <summary>Whether a switch or check box needs its tap to be as the pilot wished: false only when the tablet already shows the
    /// wished state (it changed since the page was read, so a tap would undo the wish). A state the reader could not tell is not
    /// known to be as wished, so it is tapped.</summary>
    public static bool NeedsTap(Bbs757EfbItem item, bool wantOn) => item.On != wantOn;

    // ------------------------------------------------------------------ a slider with choices

    /// <summary>
    /// Where on a slider's track choice <paramref name="index"/> of <paramref name="count"/> sits: the choices are spread evenly,
    /// the first at the track's start and the last at its end (measured on IRS ALIGNMENT TIME, BOARDING &amp; LOAD RATE and
    /// NOTIFICATIONS FADE-OUT TIMER, 2026-10-26). A slider with one choice is tapped at its start.
    /// </summary>
    public static int ChoiceX(Bbs757EfbSlider s, int index, int count)
    {
        if (count <= 1) return s.TrackLeft;
        return s.TrackLeft + (int)Math.Round((double)index * (s.TrackRight - s.TrackLeft) / (count - 1));
    }

    /// <summary>
    /// Where to tap a slider next, when the last tap (at <paramref name="lastX"/>, the <paramref name="tapsSoFar"/>th) left it at
    /// choice <paramref name="readIndex"/> instead of <paramref name="targetIndex"/>: a third of a choice's width toward the target
    /// (at least 4 px), kept on the track, since a tap past its end lands beside it, on another control. Null when there is no tap
    /// to make: <see cref="MaxSliderTaps"/> taps are spent, the slider's value was not read (<paramref name="readIndex"/> is
    /// negative), or it already reads the target.
    /// </summary>
    public static int? NextChoiceX(Bbs757EfbSlider s, int lastX, int targetIndex, int readIndex, int count, int tapsSoFar)
    {
        if (tapsSoFar >= MaxSliderTaps || readIndex < 0 || readIndex == targetIndex) return null;
        double choice = (double)(s.TrackRight - s.TrackLeft) / Math.Max(1, count - 1);
        int step = (int)Math.Round(Math.Max(MinStepPx, choice / 3));
        int x = lastX + (targetIndex > readIndex ? step : -step);
        return Math.Min(Math.Max(x, s.TrackLeft), s.TrackRight);
    }

    // ------------------------------------------------------------------ what is said

    /// <summary>
    /// What a button or tile press says about itself, or null when it says nothing here: the window says a new page (nothing) and
    /// a control's new words (once, by <c>announceChange</c>), so this speaks only when the press changed nothing. Null when the page
    /// name changed, and when the pressed control changed or is gone (found again by <see cref="Relocate"/>; the top bar is not part
    /// of the page, so its control is judged on its own); <see cref="NoChangeText"/> when <see cref="PageUnchanged"/>, a new
    /// notification over it included; else null, since something on the page moved even if the pressed control did not.
    /// </summary>
    public static string? ButtonOutcome(Bbs757EfbScreenModel before, Bbs757EfbScreenModel after, Bbs757EfbItem pressed)
    {
        if (!SamePage(before, after)) return null;
        var now = Relocate(before, after, pressed);
        if (now == null || Line(now) != Line(pressed)) return null;
        return PageUnchanged(before, after) ? NoChangeText(pressed.Text) : null;
    }

    // Said as alerts (spoken at once, once): verbatim from the plan's speech rules, the label being the element's text. Plain
    // concatenation of the given words, so no culture can change one.

    /// <summary>A tap that could not run.</summary>
    public const string UnreachableText = "The tablet cannot be reached, so nothing was tapped.";

    /// <summary>The control is not on the tablet any more, so nothing was tapped.</summary>
    public static string GoneText(string label) => label + " is no longer on the tablet.";

    /// <summary>A button or tile press that changed nothing on the page.</summary>
    public static string NoChangeText(string label) => label + ": no change on the tablet.";

    /// <summary>A switch or check box the tablet did not follow in time.</summary>
    public static string NotChangedText(string label) => label + " did not change.";

    /// <summary>A slider that did not reach the wished choice or value.</summary>
    public static string CouldNotSetText(string label, string value) => label + " could not be set to " + value + ".";
}
