using System.Drawing;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One entry of BlueBird's notification panel: its words as the tablet writes them, made readable ("GPU
/// connected."), and its time ("14:17Z"), null when the time was not read.</summary>
public sealed record Bbs757EfbNotice(string Text, string? Time);

/// <summary>A box the tablet shows over its page with words and one or two answers (YES, NO, OK, CANCEL): the words, then
/// the answers as buttons, left to right.</summary>
public sealed record Bbs757EfbQuestion(string Text, IReadOnlyList<Bbs757EfbItem> Buttons);

/// <summary>
/// What the tablet shows over its page, taken out of the page before it is read, so neither is ever offered as one of the
/// page's controls. Measured on plan 18's captures (2026-10-29, tablet pixels):
/// <list type="bullet">
/// <item>BlueBird's notification panel at x 1037-1390 from y 38: one new notification for about 10 seconds (to y 157),
/// or the list the top bar's arrow opens (to y 518), each entry a time ("14:17Z") over its words; the title
/// "Notifications" and "Clear all" above them. Its title band is blue (31,68,107) where the page's title bar is
/// (17,28,41). Read into <see cref="Bbs757EfbScreenModel.Notices"/>, and each entry spoken once per appearance
/// (<see cref="ToSpeak"/>).</item>
/// <item>A question: a box over the page (dimmed or not) with its words and its answers (the defuel question, the units
/// question, "FUEL ADJUSTMENT IS COMPLETE!" with OK). Found from its answers' words: the box is the color just above
/// an answer, flooded. Read into <see cref="Bbs757EfbScreenModel.Question"/>.</item>
/// </list>
/// Both are taken from OCR's own lines, not the page's snapped ones: snapped to the page's words, "GPU DISCONNECTED."
/// became GPU CONNECTED. Pure.
/// </summary>
public static class Bbs757EfbNotices
{
    /// <summary>How long after the pilot's own press no notification is spoken: the press's own outcome says enough.</summary>
    public const int QuietAfterPressMs = 4000;

    /// <summary>
    /// What to say for the panel's entries: the texts in <paramref name="now"/> not in <paramref name="before"/> (the same
    /// words at the same time), joined with spaces, top first; null when there are none or the pilot's own press was
    /// less than <see cref="QuietAfterPressMs"/> ago. The words match with case, spaces and punctuation ignored, and a
    /// time not read matches any (small text wobbles between reads); the same words at another time are a new
    /// appearance ("Chocks attached." at 12:06Z and again at 12:09Z).
    /// </summary>
    public static string? ToSpeak(IReadOnlyList<Bbs757EfbNotice> before, IReadOnlyList<Bbs757EfbNotice> now, long msSinceOwnPress)
    {
        if (msSinceOwnPress < QuietAfterPressMs) return null;
        var fresh = now.Where(n => !before.Any(b => Same(b, n))).Select(n => n.Text).ToList();
        return fresh.Count == 0 ? null : string.Join(" ", fresh);
    }

    private static bool Same(Bbs757EfbNotice a, Bbs757EfbNotice b) =>
        Bare(a.Text) == Bare(b.Text) && (a.Time == null || b.Time == null || a.Time == b.Time);

    private static string Bare(string t) => string.Concat(t.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    // ------------------------------------------------------------------ the notification panel

    /// <summary>Where the panel's title band is looked for: row 42, at three places clear of its words, and left of the
    /// panel, where the page's title bar must not look like it (the panel's own left edge).</summary>
    private static readonly int[] PanelProbeX = { 1060, 1200, 1300 };
    private const int PanelProbeY = 42, LeftOfPanel = 1020;

    /// <summary>The panel's left edge (measured at 1037), the column its bottom is found down (inside its left border),
    /// how far down the list can reach (to y 518, measured), and the bottom of the title's row (title center y 54, first
    /// time 83).</summary>
    private const int PanelLeft = 1036, PanelColumn = 1044, PanelLowest = 600, TitleRowBottom = 70;

    /// <summary>Whether BlueBird's notification panel shows (<see cref="Panel"/>).</summary>
    internal static bool PanelShown(Bbs757EfbPixels px) => Panel(px) != null;

    private static bool TitleBlue((int R, int G, int B) c) => c.B >= 80 && c.G >= 45 && c.B > 2 * c.R;

    /// <summary>The panel's body, which darkens down to (9,23,45) at its bottom, and its lighter bottom border; the page
    /// under it, (22,29,37), is not.</summary>
    private static bool PanelBody((int R, int G, int B) c) => c.B >= 40 && c.B - c.R >= 20;

    /// <summary>The panel's place: its blue title band under the top bar, ending at its left edge, down to where its body
    /// ends. Null when it does not show, or shows no bottom (which no panel measured lacks).</summary>
    private static Rectangle? Panel(Bbs757EfbPixels px)
    {
        if (!PanelProbeX.All(x => TitleBlue(px.At(x, PanelProbeY))) || TitleBlue(px.At(LeftOfPanel, PanelProbeY))) return null;
        int bottom = PanelProbeY;
        while (bottom < PanelLowest && PanelBody(px.At(PanelColumn, bottom + 1))) bottom++;
        return bottom < PanelLowest ? Rectangle.FromLTRB(PanelLeft, Bbs757EfbScreen.StatusBottom, px.Width, bottom + 1) : null;
    }

    private static readonly Regex TimeRead = new(@"^(\d{1,2})[:.](\d{2})[Z7z2]?(?:\s+(.+))?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The notification panel's entries, top first, with the panel's lines taken out of <paramref name="lines"/> (its title,
    /// Clear all, the times, the words), and the page's words it cuts: read cut, they snap to other words ("RIGHT AIF" became
    /// RIGHT MID, "REQ CARGO" FWD CARGO, measured with the list open), so a control the panel covers in part is not
    /// offered under a name it does not have. An entry is a time with the words under it ("12:437" is 12:43Z: the Z read as
    /// a 7); words with no time over them start an entry of their own; an entry with no words (the one cut off at the
    /// list's bottom) is none. Empty when the panel does not show.
    /// </summary>
    internal static IReadOnlyList<Bbs757EfbNotice> TakePanel(List<Bbs757EfbTextLine> lines, Bbs757EfbPixels px)
    {
        if (Panel(px) is not Rectangle panel) return Array.Empty<Bbs757EfbNotice>();
        var inside = Bbs757EfbReadingOrder.Lines(lines.Where(l => panel.Contains(l.Centre)), 5);
        lines.RemoveAll(l => l.Box.IntersectsWith(panel));

        var entries = new List<Entry>();
        foreach (var l in inside)
        {
            string t = l.Text.Trim();
            var time = TimeRead.Match(t);
            if (time.Success)
            {
                entries.Add(new Entry(time.Groups[1].Value + ":" + time.Groups[2].Value + "Z", l.Box.Bottom));
                if (time.Groups[3].Success) entries[^1].Words.Add(Bbs757EfbVocabulary.Pretty(time.Groups[3].Value.Trim()));
                continue;
            }
            if (l.Centre.Y < TitleRowBottom || Bbs757EfbScreen.IsNoise(t)) continue;
            if (entries.Count == 0 || l.Box.Top - entries[^1].Bottom > 20) entries.Add(new Entry(null, l.Box.Bottom));
            entries[^1].Words.Add(Bbs757EfbVocabulary.Pretty(t));
            entries[^1].Bottom = l.Box.Bottom;
        }
        return entries.Where(e => e.Words.Count > 0).Select(e => new Bbs757EfbNotice(string.Join(" ", e.Words), e.Time)).ToList();
    }

    private sealed class Entry(string? time, int bottom)
    {
        public string? Time { get; } = time;
        public List<string> Words { get; } = new();
        public int Bottom { get; set; } = bottom;
    }

    // ------------------------------------------------------------------ questions

    private static readonly string[] Answers = { "YES", "NO", "OK", "CANCEL" };

    /// <summary>The seed of a question's box: this far above an answer's words, clear of its button (YES's green starts
    /// about 10 px above the word) and of the question's words.</summary>
    private const int AboveAnswer = 20;

    /// <summary>A question box's size: the three measured are 580 x 133, 760 x 251 and 820 x 272.</summary>
    private const int MinWidth = 300, MaxWidth = 1000, MinHeight = 80, MaxHeight = 450;

    private static string? Answer(string read)
    {
        string t = read.Trim().TrimEnd('.', '!').ToUpperInvariant();
        return Array.IndexOf(Answers, t) >= 0 ? t : null;
    }

    /// <summary>
    /// A question over the page, with its lines taken out of <paramref name="lines"/>: the box round an answer's words
    /// (<see cref="QuestionBox"/>) holding words besides its answers. Its text is those words in reading order; each
    /// answer is a button at the box the pixels show round it, or, where they show none (NO is dark blue on the dark box),
    /// at its word, as tapping either does. Null when there is none.
    /// </summary>
    internal static Bbs757EfbQuestion? TakeQuestion(List<Bbs757EfbTextLine> lines, Bbs757EfbPixels px)
    {
        foreach (var answer in lines.Where(l => Answer(l.Text) != null).ToList())
        {
            if (QuestionBox(px, new Point(answer.Centre.X, answer.Box.Top - AboveAnswer)) is not Rectangle box) continue;
            var inside = lines.Where(l => box.Contains(l.Centre)).ToList();
            var words = Bbs757EfbReadingOrder.Lines(inside.Where(l => Answer(l.Text) == null && !Bbs757EfbScreen.IsNoise(l.Text.Trim())), 5);
            if (words.Count == 0) continue;
            lines.RemoveAll(l => box.Contains(l.Centre));
            var found = Bbs757EfbScreen.FindButtonBoxes(px, box);
            var buttons = inside.Where(l => Answer(l.Text) != null).OrderBy(l => l.Box.Left).Select(a =>
            {
                string word = Answer(a.Text)!;
                var around = found.FirstOrDefault(b => b.Box.Contains(a.Centre));
                var at = around?.Box ?? a.Box;
                return new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(word), at, Centre(at), true, "q:" + word,
                    around?.Tint ?? Bbs757EfbTint.None);
            }).ToList();
            return new Bbs757EfbQuestion(string.Join(" ", words.Select(l => Bbs757EfbVocabulary.Pretty(l.Text.Trim()))), buttons);
        }
        return null;
    }

    /// <summary>
    /// The box round <paramref name="seed"/>: every pixel within 12 of the seed's color that joins it, below the top bar
    /// (the box's words and answers, and the page where the box lets it through, are holes in it). Null when it runs wider
    /// or taller than a question box (the seed was on the page itself) or is smaller than one.
    /// </summary>
    private static Rectangle? QuestionBox(Bbs757EfbPixels px, Point seed)
    {
        if (seed.Y <= Bbs757EfbScreen.StatusBottom || seed.Y >= px.Height || seed.X < 0 || seed.X >= px.Width) return null;
        var c0 = px.At(seed.X, seed.Y);
        bool Like((int R, int G, int B) c) => Math.Abs(c.R - c0.R) <= 12 && Math.Abs(c.G - c0.G) <= 12 && Math.Abs(c.B - c0.B) <= 12;
        var seen = new bool[px.Width * px.Height];
        var queue = new Queue<int>();
        queue.Enqueue(seed.Y * px.Width + seed.X);
        seen[seed.Y * px.Width + seed.X] = true;
        int minX = seed.X, maxX = seed.X, minY = seed.Y, maxY = seed.Y;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % px.Width, y = i / px.Width;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            if (maxX - minX >= MaxWidth || maxY - minY >= MaxHeight) return null;
            Visit(x + 1, y); Visit(x - 1, y); Visit(x, y + 1); Visit(x, y - 1);
        }
        int w = maxX - minX + 1, h = maxY - minY + 1;
        return w >= MinWidth && h >= MinHeight ? new Rectangle(minX, minY, w, h) : null;

        void Visit(int x, int y)
        {
            if (x < 0 || x >= px.Width || y <= Bbs757EfbScreen.StatusBottom || y >= px.Height) return;
            int i = y * px.Width + x;
            if (seen[i]) return;
            seen[i] = true;
            if (Like(px.At(x, y))) queue.Enqueue(i);
        }
    }

    private static Point Centre(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
}
