using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A value the SD draws at a fixed place: its name (the static label beside it, which is part of the
/// page's background texture, not drawn text), where its text is drawn (Width covers a value drawn as several
/// texts, "26 TO 27"), and the unit the texture prints for it. "{weight}" and "{flow}" stand for the unit the
/// page draws itself (LBS or KG, LBS/H or KG/H), which follows the aircraft's unit setting.</summary>
public sealed record SdField(string Label, float X, float Y, string? Unit = null, float Width = 0);

/// <summary>
/// One SD page read the way a sighted pilot reads it: each drawn value named by the label printed beside it
/// on the page's background (Data/Textures/ECAM/*Static.png, at the draw's own coordinates), with its unit
/// and colour ("BAT 1 voltage: 28 V, green"); everything else read as drawn, with its colour. A unit drawn
/// right of a value is read with it; a unit drawn alone is a caption the fields already carry.
/// </summary>
public static class A350SdPage
{
    private const float Dx = 10, Dy = 8, UnitReach = 50;

    private static readonly Regex Unit = new(@"^(V|A|%|PSI|LBS|KG|LBS/H|KG/H|LBS/MIN|KG/MIN|°C|FT|FT/MIN|QT|HZ)$", RegexOptions.Compiled);

    private static bool IsUnit(DrawnCell c) => c.Colour == "cyan" && Unit.IsMatch(c.Text);

    public static IReadOnlyList<string> Read(IReadOnlyList<IReadOnlyList<DrawnCell>> rows, int page) =>
        Read(rows, Pages.GetValueOrDefault(page, Array.Empty<SdField>()), ColumnSplit.GetValueOrDefault(page, float.MaxValue));

    /// <summary>The SD's bottom line, under every page: TAT, SAT, flight time, UTC, GW, GWCG, FOB.</summary>
    public static IReadOnlyList<string> ReadPermanentData(IReadOnlyList<IReadOnlyList<DrawnCell>> rows) =>
        Read(rows, PermanentData, float.MaxValue);

    private static IReadOnlyList<string> Read(IReadOnlyList<IReadOnlyList<DrawnCell>> rows, SdField[] fields, float split)
    {
        var all = rows.SelectMany(r => r).ToList();
        string? PageUnit(params string[] options) => all.FirstOrDefault(c => IsUnit(c) && options.Contains(c.Text))?.Text;
        var weight = PageUnit("LBS", "KG");
        var flow = PageUnit("LBS/H", "KG/H");

        // A field gathers its texts across rows: UTC's colons are drawn a few pixels below its digits.
        var byField = new Dictionary<SdField, (int Row, List<DrawnCell> Cells)>();
        var units = new Dictionary<SdField, string>();
        var items = new List<(int Row, float X, string Text)>();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r].Select(c => c with { Text = c.Text.TrimStart('.').Trim() }).Where(c => c.Text.Length > 0).ToList();
            var loose = new List<DrawnCell>();
            foreach (var c in row)
            {
                if (IsUnit(c))
                {
                    // A unit reads with the field drawn just left of it; else with a loose value; alone it is a caption.
                    var owner = byField.Where(kv => kv.Value.Row == r && c.X - kv.Value.Cells[^1].X is >= 0 and <= UnitReach)
                        .OrderBy(kv => c.X - kv.Value.Cells[^1].X).Select(kv => kv.Key).FirstOrDefault();
                    if (owner != null) units[owner] = c.Text;
                    else if (loose.Count > 0 && c.X - loose[^1].X is >= 0 and <= UnitReach && !IsUnit(loose[^1])) loose.Add(c);
                    continue;
                }
                var f = fields.Where(f => Math.Abs(c.Y - f.Y) <= Dy && c.X >= f.X - Dx && c.X <= f.X + f.Width + Dx)
                    .OrderBy(f => Math.Abs(c.X - f.X)).FirstOrDefault();
                if (f == null) { loose.Add(c); continue; }
                if (!byField.TryGetValue(f, out var entry)) byField[f] = entry = (r, new List<DrawnCell>());
                entry.Cells.Add(c);
            }
            foreach (var cluster in Clusters(loose))
                items.Add((r, cluster[0].X, A350DrawLayout.Speak(cluster)));
        }
        foreach (var (f, (row, cells)) in byField)
        {
            var ordered = cells.OrderBy(c => c.X).ToList();
            var unit = units.GetValueOrDefault(f) ?? f.Unit?.Replace("{weight}", weight ?? "").Replace("{flow}", flow ?? "");
            var value = TimeColon.Replace(SpeakWithUnit(ordered, string.IsNullOrEmpty(unit) ? null : unit), ":");
            items.Add((row, ordered[0].X, f.Label + ": " + value));
        }
        return items.OrderBy(i => i.X >= split).ThenBy(i => i.Row).ThenBy(i => i.X).Select(i => i.Text).ToList();
    }

    /// <summary>A time drawn as separate texts ("16 : 31 : 57") reads as one ("16:31:57").</summary>
    private static readonly Regex TimeColon = new(@"(?<=\d) : (?=\d)", RegexOptions.Compiled);

    /// <summary>The value's colour runs, the unit after the last run's text ("26, green; TO, white; 27 °C, green").</summary>
    private static string SpeakWithUnit(IReadOnlyList<DrawnCell> cells, string? unit)
    {
        if (unit == null) return A350DrawLayout.Speak(cells);
        var withUnit = cells.Take(cells.Count - 1).Append(cells[^1] with { Text = cells[^1].Text + " " + unit }).ToList();
        return A350DrawLayout.Speak(withUnit);
    }

    /// <summary>Loose texts on one row split where a gap shows they are separate labels ("BAT 1" | "BAT EMER 1").</summary>
    private static IEnumerable<List<DrawnCell>> Clusters(List<DrawnCell> cells)
    {
        const float CharWidth = 14, Gap = 35;
        List<DrawnCell>? cur = null;
        foreach (var c in cells.OrderBy(c => c.X))
        {
            if (cur != null && c.X - (cur[^1].X + cur[^1].Text.Length * CharWidth) <= Gap) { cur.Add(c); continue; }
            if (cur != null) yield return cur;
            cur = new List<DrawnCell> { c };
        }
        if (cur != null) yield return cur;
    }

    private static SdField[] Pair(string label1, string label2, float x1, float x2, float y, string? unit = null) =>
        new[] { new SdField(label1, x1, y, unit), new SdField(label2, x2, y, unit) };

    private static IEnumerable<SdField> Row(float y, string? unit, params (string Label, float X)[] cells) =>
        cells.Select(c => new SdField(c.Label, c.X, y, unit));

    /// <summary>The permanent data line (EcamCommon.png): its labels left of each value.</summary>
    private static readonly SdField[] PermanentData =
    {
        new("TAT", 180, 956, "°C"), new("SAT", 180, 987, "°C"),
        new("Flight time", 423, 958, Width: 9), new("UTC", 377, 998, Width: 70),
        new("Gross weight", 718, 956, "{weight}"), new("Gross weight CG", 718, 986, "%"),
        new("Fuel on board", 723, 1013, "{weight}"),
    };

    /// <summary>Pages split by a rule into two columns, read one after the other: DOOR's oxygen and rain
    /// repellent column right of the aircraft.</summary>
    private static readonly IReadOnlyDictionary<int, float> ColumnSplit = new Dictionary<int, float> { [11] = 590 };

    // Page numbers are INI_ECAM_ACTIVE_PAGE's (A350Levers.SdPages). Positions: the stand capture's draw and,
    // for what only shows powered (EXT power, a battery's OFF), the live one.
    internal static readonly IReadOnlyDictionary<int, SdField[]> Pages = new Dictionary<int, SdField[]>
    {
        [1] = new[]
        {
            Pair("Engine 1 fuel flow", "Engine 2 fuel flow", 223, 585, 333, "{flow}"),
            Pair("Engine 1 N2", "Engine 2 N2", 220, 588, 395, "%"),
            Pair("Engine 1 N3", "Engine 2 N3", 220, 588, 430, "%"),
            Pair("Engine 1 oil quantity", "Engine 2 oil quantity", 215, 593, 521, "QT"),
            Pair("Engine 1 oil temperature", "Engine 2 oil temperature", 212, 596, 556, "°C"),
            Pair("Engine 1 oil pressure", "Engine 2 oil pressure", 212, 596, 627, "PSI"),
            Pair("Engine 1 vibration N1", "Engine 2 vibration N1", 207, 601, 696),
            Pair("Engine 1 vibration N2", "Engine 2 vibration N2", 207, 601, 730),
            Pair("Engine 1 vibration N3", "Engine 2 vibration N3", 207, 601, 765),
        }.SelectMany(p => p).ToArray(),
        [2] = Pair("Green system pressure", "Yellow system pressure", 226, 695, 354, "PSI"),
        [3] = Row(424, null, ("BAT 1 voltage", 132), ("BAT EMER 1 voltage", 302), ("BAT EMER 2 voltage", 534), ("BAT 2 voltage", 710))
            .Concat(Row(439, null, ("BAT EMER 1", 319), ("BAT EMER 2", 551)))   // OFF, in place of its values
            .Concat(Row(457, null, ("BAT 1 current", 132), ("BAT EMER 1 current", 302), ("BAT EMER 2 current", 534), ("BAT 2 current", 710)))
            .Concat(Row(810, null, ("TR 1 voltage", 119), ("TR EMER 1 voltage", 303), ("TR EMER 2 voltage", 534), ("TR 2 voltage", 717)))
            .Concat(Row(843, null, ("TR 1 current", 119), ("TR EMER 1 current", 303), ("TR EMER 2 current", 534), ("TR 2 current", 717)))
            .ToArray(),
        [4] = Row(352, null, ("AC EMER 1 bus", 287), ("AC EMER 2 bus", 487))
            .Concat(Row(412, null, ("AC EMER 1 bus", 287), ("AC EMER 2 bus", 487)))
            .Concat(Row(462, null, ("EXT 1 voltage", 112), ("EXT 2 voltage", 719)))
            .Concat(Row(489, null, ("EXT 1 frequency", 112), ("EXT 2 frequency", 719)))
            .Concat(Row(584, null, ("AC 1A bus", 83), ("AC 1B bus", 236), ("AC 2B bus", 536), ("AC 2A bus", 688)))
            .Concat(Row(646, null, ("AC 1A bus", 83), ("AC 1B bus", 236), ("AC 2B bus", 536), ("AC 2A bus", 688)))
            .Concat(Row(814, null, ("Generator 1A load", 119), ("Generator 1B load", 272), ("Generator 2B load", 573), ("Generator 2A load", 725)))
            .Concat(Row(841, null, ("Generator 1A voltage", 119), ("Generator 1B voltage", 272), ("Generator 2B voltage", 573), ("Generator 2A voltage", 725)))
            .ToArray(),
        [5] = new[]
        {
            // Top to bottom in each pack box, as on the other Airbus BLEED pages: outlet, then compressor outlet.
            Pair("Pack 1 outlet temperature", "Pack 2 outlet temperature", 209, 619, 398, "°C"),
            Pair("Pack 1 compressor outlet temperature", "Pack 2 compressor outlet temperature", 209, 619, 474, "°C"),
            Pair("Pack", "Pack", 297, 498, 408),
            new[] { new SdField("Pack 1 controllers", 284, 469, Width: 27), new SdField("Pack 2 controllers", 483, 469, Width: 27) },
            Pair("Engine 1 bleed pressure", "Engine 2 bleed pressure", 204, 613, 678, "PSI"),
            Pair("Engine 1 bleed temperature", "Engine 2 bleed temperature", 204, 613, 717, "°C"),
        }.SelectMany(p => p).ToArray(),
        [6] = new[]
        {
            new SdField("Cockpit temperature", 77, 445, "°C"),
            new SdField("Cabin temperature", 438, 445, "°C", Width: 84),
            new SdField("Avionics", 61, 546),
            new SdField("Forward cargo temperature", 267, 579, "°C"),
            new SdField("Bulk cargo temperature", 662, 579, "°C"),
            new SdField("Pack", 325, 914), new SdField("Pack", 448, 914),
        },
        [7] = new[]
        {
            new SdField("Landing elevation", 714, 309, "FT"),
            new SdField("Cabin altitude mode", 280, 377), new SdField("Cabin vertical speed mode", 530, 377),
            new SdField("Cabin vertical speed", 730, 532, "FT/MIN"),
            new SdField("Delta P", 234, 587, "PSI"), new SdField("Cabin altitude", 502, 587, "FT"),
            new SdField("Pressurization system", 392, 744, Width: 66),
            new SdField("Pack", 127, 907), new SdField("Pack", 712, 907),
        },
        [8] = new[]
        {
            new SdField("FOB plus fuel used minus block", 722, 301, "{weight}"),
            new SdField("Engine 1 fuel used", 206, 418, "{weight}"), new SdField("Total fuel used", 404, 420, "{weight}"),
            new SdField("Engine 2 fuel used", 713, 418, "{weight}"),
            new SdField("Left tank", 155, 659, "{weight}"), new SdField("Centre tank", 415, 659, "{weight}"),
            new SdField("Right tank", 661, 659, "{weight}"),
            new SdField("Left tank temperature", 54, 691, "°C"), new SdField("Right tank temperature", 761, 691, "°C"),
            new SdField("All engines fuel flow", 82, 912),
        },
        [9] = new[]
        {
            new SdField("APU bleed pressure", 630, 464, "PSI"), new SdField("APU fuel used", 453, 538, "{weight}"),
            new SdField("APU N", 244, 692, "%"), new SdField("APU EGT", 266, 882, "°C"),
        },
        [10] = new[]
        {
            new SdField("Rudder trim", 518, 354), new SdField("Pitch trim", 479, 606, Width: 9),
            new SdField("Left elevator", 246, 546), new SdField("Right elevator", 583, 546),
            new SdField("Left outer aileron", 70, 788), new SdField("Right outer aileron", 764, 788),
            new SdField("Left spoiler 1", 358, 778), new SdField("Left spoiler 2", 330, 778),
            new SdField("Left spoiler 3", 271, 768), new SdField("Left spoiler 4", 242, 768),
            new SdField("Left spoiler 5", 214, 768), new SdField("Left spoiler 6", 184, 768),
            new SdField("Left spoiler 7", 155, 768),
            new SdField("Right spoiler 1", 475, 778), new SdField("Right spoiler 2", 504, 778),
            new SdField("Right spoiler 3", 562, 768), new SdField("Right spoiler 4", 591, 768),
            new SdField("Right spoiler 5", 620, 768), new SdField("Right spoiler 6", 649, 768),
            new SdField("Right spoiler 7", 678, 768),
        },
        [11] = Row(431, null, ("Door 1L slide", 238), ("Door 1L", 263), ("Door 1R", 336), ("Door 1R slide", 367))
            .Concat(Row(533, null, ("Door 2L slide", 238), ("Door 2L", 263), ("Door 2R", 336), ("Door 2R slide", 367)))
            .Concat(Row(675, null, ("Door 3L slide", 242), ("Door 3L", 263), ("Door 3R", 336), ("Door 3R slide", 371)))
            .Concat(Row(846, null, ("Door 4L slide", 242), ("Door 4L", 263), ("Door 4R", 336), ("Door 4R slide", 371)))
            .Concat(new[]
            {
                new SdField("Forward cargo door", 330, 465), new SdField("Aft cargo door", 330, 765),
                new SdField("Bulk cargo door", 264, 812),
                new SdField("Captain oxygen pressure", 737, 374, "PSI"), new SdField("First officer oxygen pressure", 737, 523, "PSI"),
                new SdField("Rain repellent level", 709, 888),
            }).ToArray(),
        [12] = Row(666, "°C", ("Brake 1 temperature", 82), ("Brake 2 temperature", 181), ("Brake 3 temperature", 357), ("Brake 4 temperature", 456))
            .Concat(Row(809, "°C", ("Brake 5 temperature", 82), ("Brake 6 temperature", 181), ("Brake 7 temperature", 357), ("Brake 8 temperature", 456)))
            .ToArray(),
    };
}
