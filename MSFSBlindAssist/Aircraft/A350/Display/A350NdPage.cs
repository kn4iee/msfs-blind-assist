namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// One side's ND as lines a pilot moves through (pure), as the MFD's are (<see cref="MfdLine"/>; click points in the ND's own
/// coordinates, <see cref="EfisX"/> takes one to the EFIS's): a menu open on the map first, then the function panel (the
/// double-arrow button's MAP DATA, LANDING, ARPT SEL and STATUS tabs), then what the ND shows (<see cref="A350GroundDisplays.ReadNd"/>).
/// The ND's controls are the MFD's classes, and each control of every panel is in each draw, hidden or not, some over another
/// panel's texts: a control's own texts are those drawn between it and the next control (<see cref="DrawnElement.TextStart"/>),
/// and a hidden one draws none. Live at VCBI 2026-10-30.
/// </summary>
public static class A350NdPage
{
    private const float HalfWidth = 836;

    /// <summary>The double-arrow button (BaseTexturedButton) at the bottom right of the ND: it has no text of its own.</summary>
    public const string FunctionButton = "Functions button";

    /// <summary>The aircraft on a range map: a click there says P.POS and DIR TO.</summary>
    public const string AircraftSymbol = "Aircraft symbol button";

    /// <summary>Where a range map (no ANF) has the aircraft, how far out its range lies in the ND's pixels, and where a name is
    /// drawn from its point (live 2026-10-30, VCBI to OMDB at 320 NM against the F-PLN page: BASUR, TVM, DOTAT, POMAN at 0.7525 px a
    /// NM on ROSE and PLAN, 1.506 on ARC): ARC at 404,629 with the range 482 px out; ROSE and PLAN at 404,381, 241 px out.</summary>
    private const float MapCentreX = 404, ArcCentreY = 629, RoseCentreY = 381, ArcRangePixels = 482, RoseRangePixels = 241, NameOffsetX = 11, NameOffsetY = -1;

    /// <summary>The EFIS range knob's positions from 10 NM on, in NM (below them, the zoom).</summary>
    private static readonly int[] RangeNm = { 10, 20, 40, 80, 160, 320, 640 };

    /// <summary>The EFIS's x for a point on a side's ND: the captain's ND is the right half.</summary>
    public static float EfisX(int side, float ndX) => side == 0 ? ndX + HalfWidth : ndX;

    private sealed record Control(string Class, float X, float Y, float W, float H)
    {
        public bool Holds(DrawnText t) => t.X >= X && t.X <= X + W && t.Y >= Y && t.Y <= Y + H;
        public float Cx => X + W / 2;
        public float Cy => Y + H / 2;
        public List<DrawnText> Own { get; } = new();
        public int Index { get; init; }
    }

    /// <summary>The map's marks by their sprites in ND's atlas (image number differs per session), as MapMarkerLayer draws them.</summary>
    private static readonly (string Name, float AtlasX, float AtlasY, float W, float H)[] Marks =
    {
        ("Flag", 115, 9, 18, 26), ("Cross", 149, 11, 23, 23),
    };

    /// <param name="mode">The side's ND mode knob (0 to 2 rose, 3 arc, 4 plan), which no text says; null while unread.</param>
    /// <param name="range">The side's ND range knob (0 to 4 the zoom, then 10 NM up); null while unread.</param>
    public static IReadOnlyList<MfdLine> Read(A350DrawCapture capture, int side, int? mode = null, int? range = null)
    {
        float shift = side == 0 ? HalfWidth : 0;
        bool Mine(float x) => x - shift is >= 0 and < HalfWidth;
        var texts = capture.Texts;
        // The map's own names go through its transform, never a control's: untransformed, they would stand where they say.
        bool Plain(DrawnText t) => Math.Abs(t.X - shift - t.LocalX) < 0.5f && Math.Abs(t.Y - t.LocalY) < 0.5f && !string.IsNullOrWhiteSpace(t.Text);

        var controls = new List<Control>();
        for (int e = 0; e < capture.Elements.Count; e++)
        {
            var el = capture.Elements[e];
            if (!Mine(el.X) || el.RectW <= 0 || el.RectH <= 0) continue;
            var c = new Control(el.Class, el.X - shift, el.Y, el.RectW, el.RectH) { Index = e };
            int end = e + 1 < capture.Elements.Count ? capture.Elements[e + 1].TextStart : el.TextStart;
            for (int i = Math.Max(0, el.TextStart); i < end && i < texts.Count; i++)
                if (Plain(texts[i])) c.Own.Add(texts[i] with { X = texts[i].X - shift });
            controls.Add(c);
        }

        var taken = new HashSet<DrawnText>();
        void Take(IEnumerable<DrawnText> shown)
        {
            foreach (var t in shown) taken.Add(t with { X = t.X + shift });
        }
        static string Words(IEnumerable<DrawnText> shown) => string.Join(" ", shown.Select(t => Word(t.Text)));

        var lines = new List<(float Y, float X, MfdLine Line)>();
        var menu = new List<MfdLine>();
        foreach (var c in controls.Where(c => c.Class == "DropdownButtonUI" && c.Own.Any(c.Holds)))
        {
            var own = c.Own.Where(c.Holds).ToList();
            Take(own);
            menu.Add(new MfdLine("menu", own[0].ColourName == "white" ? Words(own) : $"{Words(own)}, {own[0].ColourName}", c.Cx, c.Cy));
        }

        var tabs = controls.Where(c => c.Class == "WidgetSwitcherTab" && c.Own.Any(c.Holds)).ToList();
        foreach (var c in tabs)
        {
            var own = c.Own.Where(c.Holds).ToList();
            Take(own);
            lines.Add((c.Y, c.X, new MfdLine("tab", c == tabs[^1] ? $"{Words(own)} tab, selected" : $"{Words(own)} tab", c.Cx, c.Cy)));
        }
        string tab = tabs.Count > 0 ? Words(tabs[^1].Own.Where(tabs[^1].Holds)) : "";

        foreach (var c in controls.Where(c => c.Class == "BaseUIButton" && c.Own.Any(c.Holds)))
        {
            var own = c.Own.Where(c.Holds).ToList();
            Take(own);
            // ARPT SEL's three at the right hold the flight plan's airports once there is one: ORIGIN, DEST and ALTN, top down.
            string word = Words(own);
            if (tab == "ARPT SEL" && c.X > 640)
                word = (c.Y < 870 ? "Origin" : c.Y < 930 ? "Destination" : "Alternate") is var place && word is "ORIGIN" or "DEST" or "ALTN" ? word : $"{place} {word}";
            lines.Add((c.Y, c.X, new MfdLine("button", own[0].ColourName == "white" ? $"{word} button" : $"{word} button, {own[0].ColourName}", c.Cx, c.Cy)));
        }

        // The option boxes: the name is the box's own text, just right of a small box. Cyan is the one chosen.
        foreach (var c in controls.Where(c => c.Class == "BaseUICheckbox" && c.Own.Count > 0))
        {
            Take(c.Own);
            lines.Add((c.Y, c.X, new MfdLine("option", c.Own[0].ColourName == "cyan" ? $"{Words(c.Own)}, selected" : Words(c.Own), c.Cx, c.Cy)));
        }

        // The title a waypoint's menu has above its items: its name, just above the first, its colour the waypoint's.
        if (menu.Count > 0 && tabs.Count == 0 && controls.FirstOrDefault(c => c.Class == "DropdownButtonUI" && c.Own.Any(c.Holds)) is { } first)
        {
            var title = A350DrawLayout.SplitEfis(texts, side).Nd.Where(t => Plain(t) && !taken.Contains(t))
                .Select(t => (Original: t, At: t with { X = t.X - shift }))
                .FirstOrDefault(p => p.At.X >= first.X - 8 && p.At.X <= first.X + first.W && first.Y - p.At.Y is >= 0 and <= 30);
            if (title.Original != null)
            {
                taken.Add(title.Original);
                menu.Insert(0, new MfdLine("text", $"{Word(title.At.Text)}, {title.At.ColourName}", -1, -1));
            }
        }

        // The fields: what is in the box is the value, a white text left of it the name, a text right of it the rest of the
        // reading ("M FROM THRESHOLD"). A dropdown's box holds a smaller text box, which draws the texts.
        foreach (var c in controls.Where(c => c.Class is "DropdownTextBox" or "BaseUITextBox" && c.Own.Any(t => c.Holds(t))))
        {
            var value = c.Own.Where(c.Holds).OrderBy(t => t.X).ToList();
            var label = c.Own.Where(t => !c.Holds(t) && t.X < c.X).OrderBy(t => t.X).ToList();
            var after = c.Own.Where(t => !c.Holds(t) && t.X >= c.X).ToList();
            Take(c.Own);
            // A unit or a word drawn after the panel's next control can still end the field's row ("FROM THRESHOLD").
            if (label.Count > 0 && value.Count > 0)
            {
                var row = A350DrawLayout.SplitEfis(texts, side).Nd.Where(t => Plain(t) && !taken.Contains(t) && Math.Abs(t.Y - value[0].Y) <= 12)
                    .Select(t => t with { X = t.X - shift }).Where(t => t.X >= c.X + c.W).ToList();
                Take(row);
                after.AddRange(row);
            }
            after = after.OrderBy(t => t.X).ToList();
            string name = label.Count > 0 ? Words(label) : tab switch { "MAP DATA" => "Map item", "ARPT SEL" => "Airport", _ => "Field" };
            lines.Add((c.Y, c.X, new MfdLine("field", $"{name}: {Words(value.Concat(after))}, {value[0].ColourName}", c.Cx, c.Cy)));
            // A dropdown's box is wider than its text box: the part beyond it is the arrow, which opens the list a click in the
            // text box does not (live: it starts typing).
            if (c.Class == "BaseUITextBox" && controls.LastOrDefault(o => o.Class == "DropdownTextBox" && o.Index < c.Index && o.X <= c.X && o.X + o.W > c.X + c.W + 8) is { } box)
                lines.Add((c.Y + 0.5f, c.X, new MfdLine("button", $"{name} list button", (c.X + c.W + box.X + box.W) / 2, box.Cy)));
        }

        // What else the panel draws, by rows, under the tabs.
        if (tabs.Count > 0)
        {
            float top = tabs.Min(c => c.Y);
            var rest = A350DrawLayout.SplitEfis(texts, side).Nd.Where(t => Plain(t) && !taken.Contains(t) && t.Y >= top && Mine(t.X)).ToList();
            foreach (var t in rest) taken.Add(t);
            // A white row of dashes under a figure is a placeholder the panel draws over, not a reading (RWY 04, TORA 3350).
            foreach (var row in A350DrawLayout.Rows(rest.Where(t => !(t.ColourName == "white" && Word(t.Text) == "none")).Select(t => t with { X = t.X - shift, Text = Word(t.Text) })))
                lines.Add((row[0].Y, row[0].X, new MfdLine("text", A350DrawLayout.Speak(row), -1, -1)));
        }

        // A range map's names, as far as a click lands on them: the flight plan's and the navaids, untransformed in the map's
        // area. Red and amber are the ND's flags; a figure is a scale's. Each once, its nearest; the ANF's are its own (below).
        bool anfDrawn = A350NdMap.FrameOf(A350DrawLayout.SplitEfis(texts, side).Nd.Select(t => t with { X = t.X - shift }).ToList()) != null
            || tabs.Count > 0;
        var names = new List<MfdLine>();
        if (!anfDrawn)
        {
            bool roseOrPlan = mode is >= 0 and <= 2 or 4;
            float cy = roseOrPlan ? RoseCentreY : ArcCentreY, reach = roseOrPlan ? RoseRangePixels : ArcRangePixels;
            float? nm = range is int r && r >= 5 && r - 5 < RangeNm.Length ? RangeNm[r - 5] : null;
            var rangeFrame = mode != null && nm != null ? new A350NdMap.NdMapFrame(MapCentreX, cy, mode == 4, reach / nm.Value) : (A350NdMap.NdMapFrame?)null;
            foreach (var t in A350DrawLayout.SplitEfis(texts, side).Nd.Where(t => Plain(t) && !taken.Contains(t)))
            {
                var at = t with { X = t.X - shift };
                if (at.X is < 0 or >= HalfWidth || at.Y is < MapTop or >= MapBottom || at.ColourName is "red" or "amber") continue;
                string name = at.Text.Trim();
                if (name.Length < 2 || name.Contains(' ') || !name.Any(char.IsLetter) || names.Any(n => n.Spoken.StartsWith(name + ",", StringComparison.Ordinal))) continue;
                taken.Add(t);
                string place = rangeFrame is { } f ? A350NdMap.PlaceNm(f, at.X - NameOffsetX, at.Y - NameOffsetY) : "";
                names.Add(new MfdLine("map", $"{name}, {at.ColourName}" + (place.Length > 0 ? $": {place}" : ""), at.X, at.Y));
            }
        }

        var shown = A350GroundDisplays.ReadNd(capture, side, taken.Contains);
        bool anf = shown.Any(l => l.StartsWith("Map range:", StringComparison.Ordinal));
        var button = controls.LastOrDefault(c => c.Class == "BaseTexturedButton");

        var o = new List<MfdLine>(menu);
        // The erase dialog's question, drawn above its two buttons in the middle of the map, before them.
        if (shown.FirstOrDefault(l => l.StartsWith("ERASE ALL ", StringComparison.Ordinal)) is { } question)
        {
            o.Add(new MfdLine("text", question, -1, -1));
            shown = shown.Where(l => l != question).ToList();
        }
        o.AddRange(lines.OrderBy(l => l.Y).ThenBy(l => l.X).Select(l => l.Line));
        if (button != null && (anf || tabs.Count > 0))
            o.Add(new MfdLine("button", FunctionButton, button.Cx, button.Cy));
        o.AddRange(shown.Select(l => new MfdLine("text", l, -1, -1)));
        if (!anfDrawn && mode is >= 0 and <= 3)
            o.Add(new MfdLine("button", AircraftSymbol, MapCentreX, mode == 3 ? ArcCentreY : RoseCentreY));
        o.AddRange(names);

        // The map's names, each where it lies and, on the ND's own screen, where a click lands on it: a menu of what can be
        // done there (a stand, a taxiway on the runway: SET BTV EXIT, a cross, a flag). A name under the function panel or
        // off the screen reads, and needs the map moved first.
        float bottom = tabs.Count > 0 ? tabs.Min(c => c.Y) : 1000;
        var map = A350DrawLayout.Region(A350DrawLayout.SplitEfis(texts, side).Nd, new A350Rect(-100000, -100000, 200000, 200000))
            .Select(t => t with { X = t.X - shift }).ToList();
        // The landing runway, from the BTV block: its idents give the centreline a taxiway's name is measured along.
        string? landing = shown.FirstOrDefault(l => l.StartsWith("Landing runway: ", StringComparison.Ordinal))?["Landing runway: ".Length..].Split(',')[0];
        foreach (var item in A350NdMap.Items(map, landing))
        {
            bool onScreen = item.X is >= 0 and < HalfWidth && item.Y is >= 0 && item.Y < bottom;
            string mark = item.Kind == "Runway" && item.Colour == "cyan" ? " (landing)" : item.IsBtvExit ? " (BTV exit)" : "";
            string said = $"{item.Kind} {item.Name}{mark}" + (item.Where is { } where ? $": {where}" : "");
            if (item.OnRunway is { } onRunway) said += $", {onRunway}";
            said += onScreen ? "" : ", off the screen";
            o.Add(onScreen ? new MfdLine("map", said, item.X, item.Y) : new MfdLine("text", said, -1, -1));
        }
        if (A350NdMap.FrameOf(map) is { } frame)
            foreach (var s in capture.Shapes ?? Array.Empty<DrawnShape>())
                foreach (var (name, atlasX, atlasY, w, h) in Marks)
                    if (s.Image > 1 && Mine(s.X0) && Math.Abs(s.X0 - s.PatternX - atlasX - (side == 0 ? 0 : 0)) < 1.5f && Math.Abs(s.Y0 - s.PatternY - atlasY) < 1.5f
                        && Math.Abs(s.X1 - s.X0 - w) < 1.5f && Math.Abs(s.Y1 - s.Y0 - h) < 1.5f)
                    {
                        float x = (s.X0 + s.X1) / 2 - shift, y = (s.Y0 + s.Y1) / 2;
                        o.Add(new MfdLine("map", A350NdMap.Place(frame, x, y) is { } where ? $"{name}: {where}" : name, x, y));
                    }
        return o;
    }

    /// <summary>The range map's area: under the ND's top lines, above the vertical display.</summary>
    private const float MapTop = 100, MapBottom = 760;

    private static string Word(string text)
    {
        var t = A350MfdPage.Glyphs(text.Trim());
        return t.Length > 0 && t.All(ch => ch is '-' or '.' or ':') ? "none" : t;
    }
}
