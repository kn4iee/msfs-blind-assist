using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One read of the tablet: the page model, the lines it was built from, and timings.</summary>
public sealed record Bbs757EfbPageRead(Bbs757EfbScreenModel Model, IReadOnlyList<Bbs757EfbTextLine> Lines, long OcrMs, int Rereads);

/// <summary>
/// Reads a captured tablet picture into its page model: the whole page at 2x
/// (<see cref="Bbs757EfbOcr.ReadAsync"/>), then a closer look (3x) at what the first read missed or
/// read badly (<see cref="Bbs757EfbScreen.RegionsToReread"/>, and every line of small text), then
/// <see cref="Bbs757EfbScreen.Parse"/>. A re-read line replaces the first read's lines it overlaps.
/// Shared by the app (Bbs757EfbBridge) and the probe tool, so what the probe proves is what ships.
/// </summary>
public static class Bbs757EfbPageReader
{
    /// <summary>Text at most this tall is read again at 3x: the tablet's notes and captions are 10-14 px,
    /// where 2x misreads letters ("then 8 pøo€ 8t8te." for "then select a panel state.").</summary>
    public const int SmallTextHeight = 14;

    public static async Task<Bbs757EfbPageRead> ReadAsync(Bitmap tablet, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var px = Bbs757EfbPixels.FromBitmap(tablet);
        var lines = (await Bbs757EfbOcr.ReadAsync(tablet, ct)).ToList();
        // A Systems page is a diagram: nothing on it is worth a closer read (25 re-reads of diagram
        // labels cost two seconds, measured).
        if (Bbs757EfbScreen.Parse(lines, px).App == "SYSTEMS")
            return new Bbs757EfbPageRead(Bbs757EfbScreen.Parse(lines, px), lines, sw.ElapsedMilliseconds, 0);

        // Buttons and slots first (a control's words), then small loose text (notes, captions).
        var controlRegions = Merge(Bbs757EfbScreen.RegionsToReread(lines, px).Concat(Bbs757EfbScreen.EmptySlots(lines, px)).ToList());
        var textRegions = new List<Rectangle>();
        foreach (var l in lines)
            if (l.Box.Height <= SmallTextHeight && l.Box.Top > Bbs757EfbScreen.StatusBottom
                && Bbs757EfbVocabulary.Nearest(l.Text) == null)
            {
                var r = new Rectangle(l.Box.X - 6, l.Box.Y - 6, l.Box.Width + 12, l.Box.Height + 12);
                if (!controlRegions.Any(c => c.IntersectsWith(r))) textRegions.Add(r);
            }
        textRegions = Merge(textRegions);

        foreach (var r in controlRegions)
        {
            // A control's words are few, and Windows' engine is uneven on them: measured, a white OPEN
            // on a pink button read only with the darkest-channel filter at 4x, REPAIR ALTN only partly
            // at any setting. Try the settings in turn, stop at the first that reads a tablet word
            // exactly, else keep the best scoring. A control may take a better word than the first read
            // had (TOW -> FAIL NOW): its words come from the vocabulary.
            var before = lines.Where(l => r.Contains(l.Centre)).ToList();
            IReadOnlyList<Bbs757EfbTextLine> best = before;
            foreach (var (filter, scale) in ControlTries)
            {
                var again = await Bbs757EfbOcr.ReadRegionAsync(tablet, r, ct, scale, filter);
                // Every setting is tried: an exact word can still be a fragment (RESET read as SET at
                // one setting, measured), and the score prefers the longer exact word.
                if (again.Count > 0 && Score(again) > Score(best)) best = again;
            }
            if (ReferenceEquals(best, before)) continue;
            lines.RemoveAll(l => r.Contains(l.Centre));
            lines.AddRange(best);
        }
        foreach (var r in textRegions)
        {
            var again = await Bbs757EfbOcr.ReadRegionAsync(tablet, r, ct);
            if (again.Count == 0) continue;
            var before = lines.Where(l => r.Contains(l.Centre)).ToList();
            if (Score(again) < Score(before)) continue;
            // Loose text read again must still look like the first read: a known word in place of a
            // different one is a guess (measured: "Apu FIRE" read again as FIRES, a menu word).
            string was = string.Join(" ", before.Select(l => l.Text)), now = string.Join(" ", again.Select(l => l.Text));
            if (was.Length > 0 && Bbs757EfbVocabulary.Distance(was.ToUpperInvariant(), now.ToUpperInvariant()) > Math.Max(3, was.Length / 2)) continue;
            lines.RemoveAll(l => r.Contains(l.Centre));
            lines.AddRange(again);
        }
        var regions = controlRegions.Concat(textRegions).ToList();
        lines = Bbs757EfbReadingOrder.Lines(lines);
        return new Bbs757EfbPageRead(Bbs757EfbScreen.Parse(lines, px), lines, sw.ElapsedMilliseconds, regions.Count);
    }

    /// <summary>The settings a control is read again with, in order (measured on the captures).</summary>
    private static readonly (Bbs757EfbOcr.Filter Filter, double Scale)[] ControlTries =
    {
        (Bbs757EfbOcr.Filter.Luminance, 3.0), (Bbs757EfbOcr.Filter.DarkestChannel, 3.0),
        (Bbs757EfbOcr.Filter.DarkestChannel, 4.0), (Bbs757EfbOcr.Filter.Luminance, 4.0),
        (Bbs757EfbOcr.Filter.Luminance, 2.0), (Bbs757EfbOcr.Filter.DarkestChannel, 2.0),
    };

    /// <summary>How well a read matches the tablet's words: 3 per line that is a known word exactly,
    /// 1 per line that snaps to one, plus a little for length (more of the label read).</summary>
    internal static double Score(IReadOnlyList<Bbs757EfbTextLine> lines)
    {
        double s = 0;
        foreach (var l in lines)
        {
            var n = Bbs757EfbVocabulary.Nearest(l.Text);
            bool exact = n != null && string.Equals(n, l.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            s += n == null ? 0 : exact ? 3 : 1;
            s += Math.Min(l.Text.Length, 30) * (exact ? 0.1 : 0.01);
        }
        return s;
    }

    /// <summary>Overlapping regions as one (fewer engine calls).</summary>
    private static List<Rectangle> Merge(List<Rectangle> rs)
    {
        var o = new List<Rectangle>();
        foreach (var r in rs)
        {
            int i = o.FindIndex(x => x.IntersectsWith(r));
            if (i >= 0) o[i] = Rectangle.Union(o[i], r); else o.Add(r);
        }
        return o;
    }
}
