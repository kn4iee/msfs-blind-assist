namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The ATC mailbox, at the top of the CTR's right half above the WD (SD::DrawMailbox, live 2026-10-30): the message shown, a request
/// transferred from the MFD's ATC COM or an uplink, its time and station, the message and page counts, and its buttons. As on the
/// ND, every control is in every draw: one is on the screen when its fill is drawn, its texts are those drawn between it and the
/// next control, and a control drawn over another hides it (SEND over CLOSE). The body of the message shown is black on a cyan box. Pure.
/// </summary>
public static class A350Mailbox
{
    private const float SameSpotPixels = 3;

    /// <summary>Where the message's column begins and ends: MSG and its count stand left of it, PGE and its count right of it.</summary>
    private const float BodyLeft = 960, BodyRight = 1392;

    /// <summary>The arrows, which hold a picture and no text, by where they are drawn.</summary>
    private static string? ArrowName(DrawnElement e) => ((int)e.X, (int)e.Y) switch
    {
        (864, 9) => "Previous message",
        (865, 125) => "Next message",
        (1393, 50) => "Previous page",
        (1393, 94) => "Next page",
        _ => null,
    };

    /// <summary>What the mailbox shows: its lines top to bottom, then its buttons (click points in the CTR's own coordinates).</summary>
    public static IReadOnlyList<MfdLine> Read(A350DrawCapture capture)
    {
        var band = A350DrawLayout.CtrMailbox;
        var elements = capture.Elements.Where(e => band.Contains(e.X, e.Y) && e.RectW > 0 && e.RectH > 0).ToList();
        var shapes = capture.Shapes ?? Array.Empty<DrawnShape>();
        var ownedTexts = new HashSet<int>();
        var controls = new List<(DrawnElement Element, string Name, string? Colour)>();
        for (int i = 0; i < elements.Count; i++)
        {
            var e = elements[i];
            int end = i + 1 < elements.Count ? elements[i + 1].TextStart : capture.Texts.Count;
            var own = Enumerable.Range(e.TextStart, Math.Max(0, end - e.TextStart))
                .Where(k => k < capture.Texts.Count && Inside(e, capture.Texts[k]) && !string.IsNullOrWhiteSpace(capture.Texts[k].Text)).ToList();
            ownedTexts.UnionWith(own);
            if (!shapes.Any(s => s.Kind == "fill" && Math.Abs(s.X0 - e.X) <= SameSpotPixels && Math.Abs(s.Y0 - e.Y) <= SameSpotPixels
                    && Math.Abs(s.X1 - (e.X + e.RectW)) <= SameSpotPixels))
                continue;
            string name = own.Count > 0 ? string.Join(" ", own.Select(k => capture.Texts[k].Text.Trim())) : ArrowName(e) ?? "";
            if (name.Length == 0)
                continue;
            controls.RemoveAll(c => Math.Abs(c.Element.X - e.X) <= SameSpotPixels && Math.Abs(c.Element.Y - e.Y) <= SameSpotPixels);
            controls.Add((e, name, own.Count > 0 ? capture.Texts[own[0]].ColourName : null));
        }

        var free = capture.Texts.Where((t, k) => !ownedTexts.Contains(k) && band.Contains(t.X, t.Y)).ToList();
        var lines = new List<MfdLine>();
        foreach (var column in new[] { free.Where(t => t.X >= BodyLeft && t.X < BodyRight), free.Where(t => t.X < BodyLeft), free.Where(t => t.X >= BodyRight) })
            lines.AddRange(CountsJoined(A350DrawLayout.Rows(column)).Select(row =>
                new MfdLine("text", A350DrawLayout.Speak(row.Select(c => Painted(c, shapes)).ToList()), -1, -1)));
        lines.AddRange(controls.Select(c => new MfdLine("button", c.Name + (c.Colour is null or "white" ? " button" : $" button, {c.Colour}"), c.Element.X + c.Element.RectW / 2, c.Element.Y + c.Element.RectH / 2)));
        return lines;
    }

    /// <summary>"MSG" over "1/2" is one line, as is "PGE" over its count.</summary>
    private static IEnumerable<IReadOnlyList<DrawnCell>> CountsJoined(IReadOnlyList<IReadOnlyList<DrawnCell>> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count == 1 && rows[i][0].Text is "MSG" or "PGE" && i + 1 < rows.Count && rows[i + 1].Count == 1 && rows[i + 1][0].Text.Contains('/'))
            {
                yield return new[] { rows[i][0], rows[i + 1][0] };
                i++;
                continue;
            }
            yield return rows[i];
        }
    }

    private static bool Inside(DrawnElement e, DrawnText t) =>
        t.X >= e.X && t.X <= e.X + e.RectW && t.Y >= e.Y && t.Y <= e.Y + e.RectH + 4;

    /// <summary>A black text says the box it is drawn on: black alone says nothing a sighted pilot sees.</summary>
    private static DrawnCell Painted(DrawnCell cell, IReadOnlyList<DrawnShape> shapes)
    {
        if (cell.Colour != "black")
            return cell;
        var box = shapes.LastOrDefault(s => s.Kind == "fill" && s.Image == 0 && cell.X >= s.X0 && cell.X <= s.X1 && cell.Y >= s.Y0 && cell.Y <= s.Y1
            && A350Rgba.NameOf(s.Colour) != "black");
        return box == null ? cell : cell with { Colour = "on " + A350Rgba.NameOf(box.Colour) };
    }
}
