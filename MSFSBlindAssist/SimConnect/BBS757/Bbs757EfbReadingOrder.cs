using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Top-to-bottom, left-to-right order for things read off the tablet, stable from one read to the next.
///
/// The parser used to order by fixed bands (<c>Top / 12</c>, <c>/ 10</c>, <c>/ 14</c>, <c>/ 20</c>). OCR boxes
/// wobble by a pixel or two between reads of the same page, so two things printed on one line could fall
/// either side of a band edge and swap places on a refresh: the "page came out differently each time"
/// feedback on plan 11. Here a row is every box whose vertical centre is within <c>tolerance</c> of the
/// row's FIRST box (anchored, not chained, so a column of lines a few pixels apart never merges into one
/// row), and a row is ordered by its left edge.
/// </summary>
public static class Bbs757EfbReadingOrder
{
    public static List<T> Order<T>(IEnumerable<T> items, Func<T, Rectangle> box, int tolerance)
    {
        var sorted = items.Select(i => (Item: i, Box: box(i)))
            .OrderBy(p => p.Box.Top + p.Box.Height / 2.0).ThenBy(p => p.Box.Left).ToList();
        var o = new List<T>(sorted.Count);
        int at = 0;
        while (at < sorted.Count)
        {
            double anchor = sorted[at].Box.Top + sorted[at].Box.Height / 2.0;
            int end = at;
            while (end < sorted.Count && sorted[end].Box.Top + sorted[end].Box.Height / 2.0 - anchor <= tolerance) end++;
            o.AddRange(sorted.Skip(at).Take(end - at).OrderBy(p => p.Box.Left).ThenBy(p => p.Box.Top).Select(p => p.Item));
            at = end;
        }
        return o;
    }

    public static List<Bbs757EfbTextLine> Lines(IEnumerable<Bbs757EfbTextLine> lines, int tolerance = 6) =>
        Order(lines, l => l.Box, tolerance);
}
