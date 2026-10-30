using System.Drawing;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The tablet's top bar, above <see cref="Bbs757EfbScreen.StatusBottom"/>: the controls BlueBird puts at its right end, read
/// as the page's <see cref="Bbs757EfbScreenModel.TopBar"/>. Measured on plan 18's captures (2026-10-29, tablet pixels): the
/// departure countdown in red ("DEP -00:46", which OCR reads "DEP-00:46"); the boarding control, a green box at x 1142-1321
/// holding "» START BOARDING" before boarding starts (tapped live at 1231,24) and "» BOARDING… 10 MIN" while it runs; the
/// notification count, a red pill at x 1326-1359; and the arrow at (1373,15), which opens BlueBird's notification list and
/// closes it (the pill goes while the list is open). The clock, the date and the outside air temperature at the bar's left
/// are left out. The keys stay the same whatever the words, so the window keeps the screen reader's focus on an item while
/// it changes. Pure: tested against the captures (tests/.../Fixtures/bbs757-efb).
/// </summary>
public static class Bbs757EfbTopBar
{
    public const string DepartureKey = "top:dep", BoardingKey = "top:boarding", NotificationsKey = "top:notifications";

    /// <summary>The arrow at the bar's right end: a tap opens BlueBird's notification list, or closes it while it is open.</summary>
    public static readonly Point Arrow = new(1373, 15);

    /// <summary>Where the arrow is drawn (white; on a light square while the list is open).</summary>
    private static readonly Rectangle ArrowBox = new(1364, 6, 19, 19);

    /// <summary>The columns the count pill can take: right of the boarding box, left of the arrow (the countdown's red
    /// text is far to the left).</summary>
    private const int PillLeft = 1300, PillRight = 1366;

    public static IReadOnlyList<Bbs757EfbItem> Parse(IReadOnlyList<Bbs757EfbTextLine> statusLines, Bbs757EfbPixels px)
    {
        var o = new List<Bbs757EfbItem>();
        foreach (var l in statusLines)
            if (DepartureText(l.Text) is string departure)
            {
                o.Add(new(Bbs757EfbKind.Text, departure, l.Box, Centre(l.Box), false, DepartureKey));
                break;
            }

        if (Run(px, 0, px.Width - 1, c => Bbs757EfbScreen.TintOf(c) == Bbs757EfbTint.Green, minRows: 10, minWidth: 60) is Rectangle box
            && BoardingText(string.Join(" ", statusLines.Where(l => box.Contains(l.Centre)).Select(l => l.Text))) is (bool isButton, string text))
            o.Add(new(isButton ? Bbs757EfbKind.Button : Bbs757EfbKind.Text, text, box, Centre(box), isButton, BoardingKey, Bbs757EfbTint.Green));

        if (ArrowShown(px))
        {
            var pill = Run(px, PillLeft, PillRight, c => Bbs757EfbScreen.TintOf(c) == Bbs757EfbTint.Red, minRows: 4, minWidth: 16);
            string? count = pill is Rectangle p
                ? statusLines.Where(l => p.Contains(l.Centre)).Select(l => l.Text.Trim()).FirstOrDefault(t => t.Length > 0 && t.All(char.IsAsciiDigit))
                : null;
            // The list open: BlueBird's panel shows and the pill does not (it shows over the popup of one new notification).
            string label = pill == null && Bbs757EfbNotices.PanelShown(px) ? "Close notifications"
                : count != null ? "Notifications: " + count
                : "Notifications";
            o.Add(new(Bbs757EfbKind.Button, label, pill is Rectangle pr ? Rectangle.Union(pr, ArrowBox) : ArrowBox, Arrow, true,
                NotificationsKey, pill != null ? Bbs757EfbTint.Red : Bbs757EfbTint.None));
        }
        return o;
    }

    /// <summary>A minus read as the minus sign (U+2212) or an en dash (U+2013), and the tablet's ellipsis (U+2026).</summary>
    private const char MinusSign = (char)0x2212, EnDash = (char)0x2013, Ellipsis = (char)0x2026;

    private static readonly Regex Departure = new(@"^\s*DEP\s*([+\-" + MinusSign + EnDash + @"]?)\s*(\d{1,2}:\d{2})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The departure countdown as the tablet writes it, "DEP -00:46", from OCR's read of it ("DEP-00:46"; a minus
    /// read as a dash or U+2212); null when the read is no countdown.</summary>
    internal static string? DepartureText(string read)
    {
        var m = Departure.Match(read ?? "");
        if (!m.Success) return null;
        string sign = m.Groups[1].Value switch { "" => "", "+" => "+", _ => "-" };
        return "DEP " + sign + m.Groups[2].Value;
    }

    private static readonly Regex Minutes = new(@"^(\d+)\s*MIN$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// What the boarding box says, from OCR's read of it: a button while it offers to start ("» START BOARDING" is "Start
    /// boarding"), text while boarding runs ("» BOARDING… 10 MIN" is "Boarding… 10 min"), any other words as read, as text.
    /// The box's icon, read as a glyph or two ("3)", "30"), is no word of its own, and a misread of START or BOARDING of
    /// their own length ("B0ARDIHG", measured) is the word. Null when no words are left.
    /// </summary>
    internal static (bool IsButton, string Text)? BoardingText(string read)
    {
        var words = (read ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Length <= 2 && (words.Count > 1 || !words[0].Any(char.IsLetter))) words.RemoveAt(0);
        if (words.Count == 0) return null;
        for (int i = 0; i < words.Count; i++)
        {
            string bare = words[i].TrimEnd('.', Ellipsis);
            string up = bare.ToUpperInvariant();
            if (bare.Length == 8 && Bbs757EfbVocabulary.Distance(up, "BOARDING") <= 2) words[i] = "BOARDING" + words[i][bare.Length..];
            else if (bare.Length == 5 && Bbs757EfbVocabulary.Distance(up, "START") <= 1) words[i] = "START" + words[i][bare.Length..];
        }
        if (words[0] == "START") return (true, Bbs757EfbVocabulary.Pretty(string.Join(" ", words).ToUpperInvariant()));
        var minutes = Minutes.Match(string.Join(" ", words.Skip(1)));
        if (words[0].TrimEnd('.', Ellipsis) == "BOARDING" && minutes.Success)
            return (false, "Boarding" + Ellipsis + " " + minutes.Groups[1].Value + " min");
        return (false, Bbs757EfbVocabulary.Pretty(string.Join(" ", words)));
    }

    /// <summary>The arrow is drawn: a white pixel where it sits.</summary>
    private static bool ArrowShown(Bbs757EfbPixels px)
    {
        for (int y = ArrowBox.Top; y < ArrowBox.Bottom; y++)
            for (int x = ArrowBox.Left; x < ArrowBox.Right; x++)
                if (px.Lum(x, y) >= 150) return true;
        return false;
    }

    /// <summary>
    /// A box of one color in the bar between <paramref name="left"/> and <paramref name="right"/>: the widest run of columns
    /// with at least <paramref name="minRows"/> pixels of that color (a column through the box's words still has its color
    /// above and below them; a word drawn in that color does not), at least <paramref name="minWidth"/> wide; its rows are
    /// where those columns show the color. Null when there is none.
    /// </summary>
    private static Rectangle? Run(Bbs757EfbPixels px, int left, int right, Func<(int R, int G, int B), bool> color, int minRows, int minWidth)
    {
        int Count(int x)
        {
            int n = 0;
            for (int y = 0; y < Bbs757EfbScreen.StatusBottom; y++) if (color(px.At(x, y))) n++;
            return n;
        }
        int bestL = -1, bestR = -2, runL = -1, gap = 0;
        for (int x = left; x <= right; x++)
        {
            if (Count(x) >= minRows)
            {
                if (runL < 0) runL = x;
                gap = 0;
                if (x - runL > bestR - bestL) { bestL = runL; bestR = x; }
            }
            else if (runL >= 0 && ++gap > 2) { runL = -1; gap = 0; }
        }
        if (bestL < 0 || bestR - bestL + 1 < minWidth) return null;
        int top = int.MaxValue, bottom = -1;
        for (int x = bestL; x <= bestR; x++)
            for (int y = 0; y < Bbs757EfbScreen.StatusBottom; y++)
                if (color(px.At(x, y))) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return new Rectangle(bestL, top, bestR - bestL + 1, bottom - top + 1);
    }

    private static Point Centre(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
}
