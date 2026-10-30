using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// A tablet picture's pixels (RGB), copied once so parsing never touches GDI, and NORMALIZED: every
/// channel scaled so the frame's brightest luminance (its white text) is 255. The tablet dims itself
/// (AUTO brightness at night) and every color on it scales with it (measured 2026-10-26: a red tile
/// (74,0,0) dim and (235,0,0) at 100 %, gray (38,38,38) and (136,136,136), white text 82 and 255), so
/// after normalizing one set of thresholds holds at any brightness. <see cref="Background"/> is the
/// page's own color, measured per frame (near black on a dim tablet, slate (22,29,37) at 100 %).
/// </summary>
public sealed class Bbs757EfbPixels
{
    private readonly byte[] _rgb;
    public int Width { get; }
    public int Height { get; }
    /// <summary>The raw frame's white (its brightest luminance, at least 30); 255 after normalizing.</summary>
    public double RawWhite { get; }
    /// <summary>The page background, normalized: the commonest color of the content area.</summary>
    public (int R, int G, int B) Background { get; }

    public Bbs757EfbPixels(int width, int height, byte[] rgb)
    {
        Width = width; Height = height; _rgb = rgb;
        double max = 30;
        for (int i = 0; i + 2 < rgb.Length; i += 3 * 5)
        {
            double l = 0.299 * rgb[i] + 0.587 * rgb[i + 1] + 0.114 * rgb[i + 2];
            if (l > max) max = l;
        }
        RawWhite = max;
        double k = 255.0 / max;
        for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)Math.Min(255, (int)Math.Round(rgb[i] * k));
        Background = Mode(260, 90, Math.Min(width, 1390), Math.Min(height, 1040));
    }

    private (int R, int G, int B) Mode(int x0, int y0, int x1, int y1)
    {
        var counts = new Dictionary<int, int>();
        for (int y = y0; y < y1; y += 7)
            for (int x = x0; x < x1; x += 7)
            {
                var (r, g, b) = At(x, y);
                int key = (r / 8) << 10 | (g / 8) << 5 | (b / 8);
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        if (counts.Count == 0) return (0, 0, 0);
        int best = counts.MaxBy(kv => kv.Value).Key;
        return (((best >> 10) & 31) * 8 + 4, ((best >> 5) & 31) * 8 + 4, (best & 31) * 8 + 4);
    }

    public static Bbs757EfbPixels FromBitmap(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var rgb = new byte[w * h * 3];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int x = 0; x < w; x++)
                {
                    rgb[(y * w + x) * 3] = row[x * 3 + 2];
                    rgb[(y * w + x) * 3 + 1] = row[x * 3 + 1];
                    rgb[(y * w + x) * 3 + 2] = row[x * 3];
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return new Bbs757EfbPixels(w, h, rgb);
    }

    /// <summary>The normalized color at (x, y); black outside the frame.</summary>
    public (int R, int G, int B) At(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return (0, 0, 0);
        int i = (y * Width + x) * 3;
        return (_rgb[i], _rgb[i + 1], _rgb[i + 2]);
    }

    public double Lum(int x, int y)
    {
        var (r, g, b) = At(x, y);
        return 0.299 * r + 0.587 * g + 0.114 * b;
    }

    /// <summary>How far a color is from the page background (largest channel difference).</summary>
    public int FromBackground((int R, int G, int B) c) =>
        Math.Max(Math.Abs(c.R - Background.R), Math.Max(Math.Abs(c.G - Background.G), Math.Abs(c.B - Background.B)));
}

/// <summary>What an element on the tablet is.</summary>
public enum Bbs757EfbKind
{
    /// <summary>Plain text (a read-out, a caption, a note).</summary>
    Text,
    /// <summary>A section heading inside a page (unboxed short capitals above a group).</summary>
    Heading,
    /// <summary>An entry of the page's side menu, or a home-screen app.</summary>
    Menu,
    /// <summary>A boxed control: a button or a tile.</summary>
    Button,
    /// <summary>A two-state switch (a settings row, the checklist's INTERACTIVE).</summary>
    Toggle,
    /// <summary>A slider (a settings row, a cabin light).</summary>
    Slider,
    /// <summary>A checklist line with its box.</summary>
    Checkbox,
    /// <summary>A settings row with neither (a read-only value or a text field).</summary>
    Row,
}

/// <summary>The color of a box: on BlueBird's tablet the color IS the state of a tile.</summary>
public enum Bbs757EfbTint { None, Blue, Red, Green, Grey, Other }

/// <summary>A slider's geometry: where its track runs and where its knob is, in tablet pixels.</summary>
public sealed record Bbs757EfbSlider(int TrackLeft, int TrackRight, int KnobX);

/// <summary>
/// One element of the page. <see cref="Hit"/> is where to tap it (tablet pixels, the agent's click
/// coordinates); <see cref="Clickable"/> false means it is not a control (a read-out, a credential).
/// <see cref="Key"/> is stable while the element's state changes (its label without
/// the state word), so the window keeps the screen reader's focus on it across a refresh.
/// </summary>
public sealed record Bbs757EfbItem(
    Bbs757EfbKind Kind,
    string Text,
    Rectangle Box,
    Point Hit,
    bool Clickable,
    string Key,
    Bbs757EfbTint Tint = Bbs757EfbTint.None,
    bool? On = null,
    bool Selected = false,
    string Value = "",
    Bbs757EfbSlider? Slider = null);

/// <summary>A parsed tablet page: which app and menu entry, then its elements in reading order.</summary>
public sealed record Bbs757EfbScreenModel(
    string App,
    string Section,
    IReadOnlyList<Bbs757EfbItem> Menu,
    IReadOnlyList<Bbs757EfbItem> Items)
{
    /// <summary>Whether the page scrolls (a scroll bar at its right edge, as Settings, Callouts has):
    /// the window then offers scroll buttons (the agent's wheel).</summary>
    public bool Scrollable { get; init; }

    /// <summary>The tablet's top bar, left to right (<see cref="Bbs757EfbTopBar"/>): the departure countdown, the boarding
    /// control and the notification control, keyed <c>top:dep</c>, <c>top:boarding</c> and <c>top:notifications</c>
    /// whatever their words. Empty when none shows.</summary>
    public IReadOnlyList<Bbs757EfbItem> TopBar { get; init; } = Array.Empty<Bbs757EfbItem>();

    /// <summary>BlueBird's notification panel's entries, top first (<see cref="Bbs757EfbNotices"/>); empty when it does
    /// not show.</summary>
    public IReadOnlyList<Bbs757EfbNotice> Notices { get; init; } = Array.Empty<Bbs757EfbNotice>();

    /// <summary>A question the tablet shows over the page, with its answers (not in <see cref="Items"/>); null when none.</summary>
    public Bbs757EfbQuestion? Question { get; init; }

    /// <summary>"Aircraft, Ground services" / "Home".</summary>
    public string PageName =>
        App.Length == 0 ? "Unknown page"
        : Section.Length == 0 ? Bbs757EfbVocabulary.Pretty(App)
        : Bbs757EfbVocabulary.Pretty(App) + ", " + Bbs757EfbVocabulary.Pretty(Section);
}

/// <summary>
/// Turns one OCR read of the tablet (<see cref="Bbs757EfbOcr"/>) and its pixels into the page's
/// structure. Pure: tested against captured pages, dim and bright (tests/.../Fixtures/bbs757-efb/).
///
/// The layout every BBOS app shares (tablet pixels, 1397 x 1048, measured 2026-10-26): a status bar
/// (date, times, OAT) above y 32, with BlueBird's own controls at its right end (read by
/// <see cref="Bbs757EfbTopBar"/>); the app's title at the top left (y 35-85) after a home button at
/// (19, 62); on the AIRCRAFT, NAVIGATION, SETTINGS and CHECKLIST apps a side menu left of x 252 whose
/// selected entry has a band unlike the others'; the content right of it. The home screen has no
/// title: five app icons with their labels 75 px under them (the label itself is not clickable).
///
/// Controls are found from the PIXELS (filled boxes of button size), so a word OCR missed still
/// leaves its control on the page; text inside a box names it. A settings row is a label at the
/// left and a value at the right (x &gt; 1180), with a switch (a pill at x 1140-1200) or a slider (a
/// round knob, the brightest blue) between; a box in that value column is the row's button instead
/// (the AIRAC data page's UPDATE or REMOVE and its LOGOUTs), named by the row's label, and the row's
/// own words stay text, unless the box's words read as a number, which stays the row's value as a
/// number in a box does on every other page (<see cref="IsNumberField"/>). A checklist line has a box
/// at x 286-311. Labels too small for OCR (a door's name under its tile, a note) come from
/// <see cref="KnownText"/>, by position, on the pages where BlueBird prints them in a fixed place.
/// </summary>
public static class Bbs757EfbScreen
{
    public const int Width = 1397, Height = 1048;
    public const int StatusBottom = 32, TitleTop = 35, TitleBottom = 88, MenuRight = 252;
    /// <summary>The home icon left of every app's title, a point measured on the 2026-10-26 captures: the window's own Home
    /// button taps it (<see cref="Bbs757EfbElements"/>), as the reader offers no control for the icon.</summary>
    public static readonly Point HomeButton = new(19, 62);

    /// <summary>
    /// What one tablet page is known to show where (measured on the 2026-10-26 captures, dim and
    /// bright, in tablet pixels). Applied only when the page's app and menu entry match AND, when given,
    /// the <see cref="Bbs757EfbTemplate.Witness"/> word was read on it (a page with tabs keys each tab by a
    /// word only that tab prints). Known: text printed too small for OCR (10 px), put in place of what
    /// OCR made of it. Slots: a value read in an area, said as "Label: value" (a fuel field means
    /// nothing without its column). Ignore: charts, diagrams and drawings, whose scattered labels would
    /// only be noise. Note: one line telling the pilot what the page holds that the reader leaves out.
    /// </summary>
    public sealed record Bbs757EfbTemplate(
        string? Witness = null,
        (Point At, string Text)[]? Known = null,
        Bbs757EfbSlot[]? Slots = null,
        Rectangle[]? Ignore = null,
        string? Note = null);

    /// <summary>A labeled value area. Pattern, when set, keeps only the part of the read that matches
    /// it (the "16/16" out of "BUSINESS PAX - 16/16").</summary>
    public sealed record Bbs757EfbSlot(Rectangle Area, string Label, string? Pattern = null, string? UnitGroup = null);

    public static readonly IReadOnlyDictionary<string, Bbs757EfbTemplate[]> Templates = new Dictionary<string, Bbs757EfbTemplate[]>
    {
        ["AIRCRAFT/CABIN & DOORS"] = new[]
        {
            new Bbs757EfbTemplate(Known: new[]
            {
                (new Point(722, 234), "LEFT FWD"), (new Point(922, 234), "RIGHT FWD"), (new Point(922, 344), "FWD CARGO"),
                (new Point(722, 409), "LEFT MID"), (new Point(922, 409), "RIGHT MID"), (new Point(922, 764), "AFT CARGO"),
                (new Point(722, 884), "LEFT AFT"), (new Point(922, 884), "RIGHT AFT"),
                (new Point(1087, 309), "CEILING"), (new Point(1090, 353), "WINDOW"), (new Point(1083, 397), "ENTRY"),
            }),
        },
        ["AIRCRAFT/COCKPIT PRESETS"] = new[]
        {
            new Bbs757EfbTemplate(Witness: "LOAD USER CONFIG",
                Known: new[] { (new Point(1207, 307), "Choose SAVE or LOAD, then select a panel state.") },
                Ignore: new[] { new Rectangle(330, 530, 960, 150) }),
        },
        ["AIRCRAFT/FUEL"] = new[]
        {
            new Bbs757EfbTemplate(
                Slots: new[]
                {
                    new Bbs757EfbSlot(new Rectangle(430, 405, 140, 40), "Left tank", @"\d+\s*%"),
                    new Bbs757EfbSlot(new Rectangle(755, 325, 140, 40), "Center tank", @"\d+\s*%"),
                    new Bbs757EfbSlot(new Rectangle(1080, 405, 140, 40), "Right tank", @"\d+\s*%"),
                    new Bbs757EfbSlot(new Rectangle(372, 778, 230, 38), "Left tank volume", UnitGroup: "volume"),
                    new Bbs757EfbSlot(new Rectangle(372, 818, 230, 38), "Left tank weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(717, 778, 230, 38), "Center tank volume", UnitGroup: "volume"),
                    new Bbs757EfbSlot(new Rectangle(717, 818, 230, 38), "Center tank weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1062, 778, 230, 38), "Right tank volume", UnitGroup: "volume"),
                    new Bbs757EfbSlot(new Rectangle(1062, 818, 230, 38), "Right tank weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(925, 878, 100, 40), "Total fuel quantity", @"\d+\s*%"),
                    new Bbs757EfbSlot(new Rectangle(717, 912, 230, 38), "Total fuel volume", UnitGroup: "volume"),
                    new Bbs757EfbSlot(new Rectangle(717, 952, 230, 38), "Total fuel weight", UnitGroup: "weight"),
                },
                // The diagram, the columns' LEFT / CENTER / RIGHT and TOTAL FUEL: the page's own words stay, IMPORT FROM
                // SIMBRIEF among them (at 517,951 when a plan is loaded and AUTOMATIC BOARDING is off; measured 2026-10-29).
                Ignore: new[] { new Rectangle(256, 88, 1141, 600), new Rectangle(256, 700, 1141, 60), new Rectangle(500, 865, 215, 55) },
                Note: "Tank figures as the tablet shows them."),
        },
        ["AIRCRAFT/WEIGHT & BALANCE"] = new[]
        {
            new Bbs757EfbTemplate(
                Slots: new[]
                {
                    new Bbs757EfbSlot(new Rectangle(378, 130, 110, 28), "Empty weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(490, 130, 110, 28), "Zero fuel weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(605, 130, 110, 28), "Zero fuel CG"),
                    new Bbs757EfbSlot(new Rectangle(718, 130, 110, 28), "Block fuel", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(830, 130, 110, 28), "Takeoff weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(945, 130, 110, 28), "Landing weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1140, 210, 230, 28), "Business passengers", @"\d+\s*/\s*\d+"),
                    new Bbs757EfbSlot(new Rectangle(1150, 272, 220, 34), "Business passenger weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1140, 318, 230, 28), "Economy passengers", @"\d+\s*/\s*\d+"),
                    new Bbs757EfbSlot(new Rectangle(1150, 380, 220, 34), "Economy passenger weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1150, 488, 220, 34), "Forward cargo", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1150, 596, 220, 34), "Aft cargo", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1150, 776, 215, 36), "Gross weight", UnitGroup: "weight"),
                    new Bbs757EfbSlot(new Rectangle(1150, 840, 215, 36), "Center of gravity"),
                },
                Ignore: new[] { new Rectangle(256, 88, 1141, 960) },
                Note: "The weight and balance chart is not read."),
        },
        ["AIRCRAFT/MAINTENANCE"] = new[]
        {
            new Bbs757EfbTemplate(Witness: "BRAKE TEMPERATURE",
                Known: new[] { (new Point(844, 607), "NOSE DOOR"), (new Point(984, 607), "LEFT DOOR"), (new Point(1124, 607), "RIGHT DOOR") },
                Slots: new[]
                {
                    new Bbs757EfbSlot(new Rectangle(525, 364, 80, 24), "Brake 1"), new Bbs757EfbSlot(new Rectangle(625, 364, 80, 24), "Brake 2"),
                    new Bbs757EfbSlot(new Rectangle(901, 364, 80, 24), "Brake 3"), new Bbs757EfbSlot(new Rectangle(1002, 364, 80, 24), "Brake 4"),
                    new Bbs757EfbSlot(new Rectangle(525, 478, 80, 24), "Brake 5"), new Bbs757EfbSlot(new Rectangle(625, 478, 80, 24), "Brake 6"),
                    new Bbs757EfbSlot(new Rectangle(901, 478, 80, 24), "Brake 7"), new Bbs757EfbSlot(new Rectangle(1002, 478, 80, 24), "Brake 8"),
                },
                Ignore: new[] { new Rectangle(478, 225, 652, 300) }),
        },
    };

    /// <summary>The Systems app draws diagrams whose labels are too small to read: the reader keeps
    /// only the diagram's name and SELECT (which lists the diagrams).</summary>
    public const string SystemsNote = "This diagram's labels are too small to read. Use Select to choose another diagram.";

    public static Bbs757EfbScreenModel Parse(IReadOnlyList<Bbs757EfbTextLine> lines, Bbs757EfbPixels px)
    {
        // The top bar is read apart from the page; what the tablet shows over the page (BlueBird's notification panel, a
        // question) is taken out of it before the page is read, from OCR's own lines (see Bbs757EfbNotices).
        var topBar = Bbs757EfbTopBar.Parse(lines.Where(l => l.Box.Bottom <= StatusBottom).ToList(), px);
        var below = lines.Where(l => l.Box.Bottom > StatusBottom).ToList();
        var notices = Bbs757EfbNotices.TakePanel(below, px);
        var question = Bbs757EfbNotices.TakeQuestion(below, px);
        return ParsePage(Clean(below), px) with { TopBar = topBar, Notices = notices, Question = question };
    }

    /// <summary>The page below the top bar, from its cleaned lines.</summary>
    private static Bbs757EfbScreenModel ParsePage(List<Bbs757EfbTextLine> clean, Bbs757EfbPixels px)
    {
        string app = "";
        string section = "";
        foreach (var l in clean)
        {
            if (l.Box.Top < TitleTop - 8 || l.Box.Bottom > TitleBottom + 6 || l.Box.Left > 300) continue;
            if (Array.IndexOf(Bbs757EfbVocabulary.Apps, l.Text) >= 0) { app = l.Text; break; }
        }
        var body = clean.Where(l => !(l.Box.Top < TitleBottom && l.Box.Left < 300 && l.Text == app)).ToList();

        if (app.Length == 0 && IsHome(body)) return ParseHome(body);

        var items = new List<Bbs757EfbItem>();
        var menu = new List<Bbs757EfbItem>();

        // Systems: a centered title (the diagram's name) and a SELECT button in the title bar.
        if (app == "SYSTEMS")
        {
            foreach (var l in body.Where(l => l.Box.Top < TitleBottom).ToList())
            {
                body.Remove(l);
                if (l.Text == "SELECT") items.Add(new(Bbs757EfbKind.Button, "Select", l.Box, l.Centre, true, "btn:SELECT"));
                else section = l.Text;
            }
        }

        if (app is "AIRCRAFT" or "NAVIGATION" or "SETTINGS" or "CHECKLIST")
        {
            var menuLines = body.Where(l => l.Box.Right <= MenuRight + 4 && l.Box.Top > 125).ToList();
            foreach (var l in menuLines) body.Remove(l);
            menu.AddRange(ParseMenu(menuLines, px, app, out section));
        }

        if (app == "SYSTEMS")
        {
            items.Add(new(Bbs757EfbKind.Text, SystemsNote, Rectangle.Empty, Point.Empty, false, "txt:systems-note"));
            return new Bbs757EfbScreenModel(app, section, menu, items);
        }

        if (Templates.TryGetValue(app + "/" + section, out var templates))
            foreach (var t in templates)
            {
                if (t.Witness != null && !body.Any(l => string.Equals(l.Text, t.Witness, StringComparison.OrdinalIgnoreCase))) continue;
                body = ApplyTemplate(body, t, items);
            }

        items.AddRange(app == "SETTINGS" ? ParseRows(body, px)
                     : app == "CHECKLIST" ? ParseChecklist(body, px)
                     : ParseContent(body, px));
        return new Bbs757EfbScreenModel(app, section, menu, items) { Scrollable = IsScrollable(px) };
    }

    /// <summary>A page template applied to the page's lines: slots become items (added to
    /// <paramref name="items"/>), ignored areas drop their lines, known text replaces OCR's.</summary>
    internal static List<Bbs757EfbTextLine> ApplyTemplate(List<Bbs757EfbTextLine> body, Bbs757EfbTemplate t, List<Bbs757EfbItem> items)
    {
        var o = new List<Bbs757EfbTextLine>(body);
        // Slots take WORDS, not lines: OCR can read two neighboring figures as one line ("20100 Lbs
        // 182672 Lbs" across two columns, measured on Weight & balance).
        var slotted = new List<(Bbs757EfbSlot Slot, string Text)>();
        foreach (var slot in t.Slots ?? Array.Empty<Bbs757EfbSlot>())
        {
            var words = new List<Bbs757EfbWord>();
            foreach (var l in o.ToList())
            {
                var inside = l.Words.Where(w => slot.Area.Contains(w.Box.Left + w.Box.Width / 2, w.Box.Top + w.Box.Height / 2)).ToList();
                if (inside.Count == 0) continue;
                words.AddRange(inside);
                var rest = l.Words.Except(inside).ToList();
                o.Remove(l);
                if (rest.Count > 0)
                    o.Add(l with { Text = string.Join(" ", rest.Select(w => w.Text)), Words = rest, Box = rest.Select(w => w.Box).Aggregate(Rectangle.Union) });
            }
            string text = string.Join(" ", words.OrderBy(w => w.Box.Left).Select(w => w.Text)).Trim();
            if (slot.Pattern != null)
            {
                var m = System.Text.RegularExpressions.Regex.Match(text, slot.Pattern);
                text = m.Success ? m.Value : "";
            }
            // A slot that read no figure has nothing to say (a unit alone is not a value); dashes are
            // the tablet's own "none" (LANDING "---" before a plan is loaded).
            if (!text.Any(char.IsDigit)) text = text.Length > 0 && text.Trim('-', ' ', '.').Length == 0 ? "none" : "";
            slotted.Add((slot, text));
        }
        foreach (var (slot, text0) in slotted)
        {
            string text = NormaliseUnits(text0);
            if (text.Length > 0 && slot.UnitGroup != null && !UnitRegex.IsMatch(text))
            {
                // A unit OCR missed: the one its siblings show (the page uses one unit system).
                var sib = slotted.Where(x => x.Slot.UnitGroup == slot.UnitGroup).Select(x => UnitRegex.Match(NormaliseUnits(x.Text)))
                    .FirstOrDefault(m => m.Success);
                if (sib != null) text += " " + sib.Value;
            }
            items.Add(new(Bbs757EfbKind.Row, slot.Label, slot.Area, Centre(slot.Area), false, "slot:" + slot.Label + "@" + slot.Area.Y,
                Value: text.Length > 0 ? text : "not read"));
        }
        foreach (var r in t.Ignore ?? Array.Empty<Rectangle>())
            o.RemoveAll(l => r.Contains(l.Centre) && l.Pass != 'K');
        if (t.Known != null) o = WithKnownText(o, t.Known);
        if (t.Note != null) items.Add(new(Bbs757EfbKind.Text, t.Note, Rectangle.Empty, Point.Empty, false, "txt:note"));
        return o;
    }

    private static readonly System.Text.RegularExpressions.Regex UnitRegex =
        new(@"\b(lbs|kg|gal|ltr)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string NormaliseUnits(string t) =>
        System.Text.RegularExpressions.Regex.Replace(t, @"\b(Lbs|LBS|Kg|KG|Kgs|Gal|GAL|Ltr|LTR)\b", m => m.Value.ToLowerInvariant().Replace("kgs", "kg"));

    /// <summary>Puts each known text in place of any OCR line within 45 x 9 px of where it is printed.</summary>
    internal static List<Bbs757EfbTextLine> WithKnownText(List<Bbs757EfbTextLine> body, (Point At, string Text)[] known)
    {
        var o = new List<Bbs757EfbTextLine>(body);
        foreach (var (at, text) in known)
        {
            o.RemoveAll(l => Math.Abs(l.Centre.X - at.X) <= 45 && Math.Abs(l.Centre.Y - at.Y) <= 9);
            int w = Math.Min(260, text.Length * 6);
            var box = new Rectangle(at.X - w / 2, at.Y - 6, w, 12);
            o.Add(new Bbs757EfbTextLine(text, box, new[] { new Bbs757EfbWord(text, box) }, 'K'));
        }
        return Bbs757EfbReadingOrder.Lines(o, 5);
    }

    // ------------------------------------------------------------------ cleaning

    /// <summary>Snaps every line to the tablet's words and drops icon glyphs and noise.</summary>
    internal static List<Bbs757EfbTextLine> Clean(IReadOnlyList<Bbs757EfbTextLine> lines)
    {
        var o = new List<Bbs757EfbTextLine>();
        foreach (var l in lines)
        {
            string t = l.Pass == 'K' ? l.Text : Bbs757EfbVocabulary.Snap(l.Text);
            if (IsNoise(t)) continue;
            o.Add(l with { Text = t });
        }
        return Bbs757EfbReadingOrder.Lines(o, 5);
    }

    internal static bool IsNoise(string t)
    {
        if (t.Length == 0) return true;
        if (Bbs757EfbVocabulary.IsKnown(t)) return false;
        int alnum = t.Count(char.IsLetterOrDigit);
        if (alnum == 0) return true;
        // A lone glyph read off an icon ("o", "e", "€9", "(5").
        if (t.Length <= 2 && !t.All(char.IsDigit) && t != "V1" && t != "OK") return true;
        if (alnum * 2 < t.Length) return true;
        return false;
    }

    // ------------------------------------------------------------------ home

    private static bool IsHome(List<Bbs757EfbTextLine> body) =>
        body.Count(l => l.Box.Top > 220 && l.Box.Top < 300 && HomeApp(l.Text) != null) >= 3;

    private static string? HomeApp(string t)
    {
        foreach (var a in Bbs757EfbVocabulary.Apps)
            if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    private static Bbs757EfbScreenModel ParseHome(List<Bbs757EfbTextLine> body)
    {
        var items = new List<Bbs757EfbItem>();
        foreach (var l in Bbs757EfbReadingOrder.Lines(body, 10))
        {
            var app = HomeApp(l.Text);
            if (app != null && l.Box.Top > 220 && l.Box.Top < 300)
                // The icon above the label is the button (measured: the label itself does nothing).
                items.Add(new(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(app), l.Box, new Point(l.Centre.X, l.Box.Top - 70), true, "app:" + app));
            else if (l.Box.Top > 980)
                items.Add(new(Bbs757EfbKind.Text, "Aircraft: " + l.Text.TrimEnd(' ', '|', '1').Trim(), l.Box, l.Centre, false, "txt:variant"));
        }
        return new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(), items);
    }

    // ------------------------------------------------------------------ side menu

    private static List<Bbs757EfbItem> ParseMenu(List<Bbs757EfbTextLine> lines, Bbs757EfbPixels px, string app, out string section)
    {
        section = "";
        var o = new List<Bbs757EfbItem>();
        // An entry can only be one of this app's menu words: snap to the nearest of those, more
        // loosely than the page vocabulary allows (GOCRAL -> GENERAL on a dim tablet, measured).
        if (Bbs757EfbVocabulary.Menus.TryGetValue(app, out var menuWords))
        {
            lines = lines.Select(l =>
            {
                string up = l.Text.ToUpperInvariant();
                var best = menuWords.MinBy(w => Bbs757EfbVocabulary.Distance(up, w));
                return best != null && Bbs757EfbVocabulary.Distance(up, best) <= best.Length / 2 + 1 ? l with { Text = best } : l;
            }).ToList();
            // An entry's icon read as a word (COCKPIT PRESETS' icon as "tlt", measured with PaddleOCR): a line that
            // is none of the menu's words, left of an entry on its row, is no entry.
            var named = lines.Where(l => menuWords.Contains(l.Text)).ToList();
            lines = lines.Where(l => menuWords.Contains(l.Text)
                || !named.Any(e => Math.Abs(e.Centre.Y - l.Centre.Y) <= 10 && l.Centre.X < e.Box.Left)).ToList();
        }
        // The selected entry's band differs from all the others' at the menu's right edge (measured:
        // darker on both a dim and a bright tablet). Judge it against the menu's own median.
        var entries = lines.Where(l => !(app == "CHECKLIST" && (l.Text == "INTERACTIVE" || l.Text == "RESET ALL"))).ToList();
        var bands = entries.Select(l => Enumerable.Range(MenuRight - 6, 5).Min(x => px.Lum(x, l.Centre.Y))).ToList();
        double median = bands.Count == 0 ? 0 : bands.OrderBy(v => v).ElementAt(bands.Count / 2);
        Bbs757EfbTextLine? selected = null;
        double bestGap = 12;
        for (int i = 0; i < entries.Count; i++)
        {
            double gap = Math.Abs(bands[i] - median);
            if (gap > bestGap) { bestGap = gap; selected = entries[i]; }
        }
        foreach (var l in lines)
        {
            if (app == "CHECKLIST" && l.Text == "INTERACTIVE")
            {
                // Its switch is left of the word (measured at x 14-68): the knob right = on.
                bool on = BrightestX(px, 14, 68, l.Centre.Y) > 41;
                o.Add(new(Bbs757EfbKind.Toggle, "Interactive", l.Box, new Point(40, l.Centre.Y), true, "tgl:INTERACTIVE",
                    On: on, Value: on ? "On" : "Off"));
                continue;
            }
            if (app == "CHECKLIST" && l.Text == "RESET ALL")
            {
                o.Add(new(Bbs757EfbKind.Button, "Reset all", l.Box, l.Centre, true, "btn:RESET ALL"));
                continue;
            }
            bool sel = ReferenceEquals(l, selected);
            if (sel) section = l.Text;
            o.Add(new(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(l.Text), l.Box, new Point(130, l.Centre.Y), true, "menu:" + l.Text,
                Selected: sel));
        }
        return o;
    }

    private static int BrightestX(Bbs757EfbPixels px, int left, int right, int cy)
    {
        double best = -1; int bx = left;
        for (int x = left; x <= right; x++)
        {
            double l = px.Lum(x, cy);
            if (l > best) { best = l; bx = x; }
        }
        return bx;
    }

    // ------------------------------------------------------------------ general content

    private static readonly Rectangle ContentArea = new(MenuRight + 4, TitleBottom, Width - MenuRight - 4, Height - TitleBottom);

    private static List<Bbs757EfbItem> ParseContent(List<Bbs757EfbTextLine> lines, Bbs757EfbPixels px)
    {
        var o = new List<Bbs757EfbItem>();
        var boxes = FindButtonBoxes(px, ContentArea);
        var loose = new List<Bbs757EfbTextLine>();
        var claimed = new Dictionary<FoundBox, List<Bbs757EfbTextLine>>();
        // A shaded button can be found only down to the middle of its gradient (measured: the BBS button
        // on a dim tablet), so a box with NO text inside it claims a line whose center is just past its top
        // or bottom edge, and grows to hold it. A box that has its own text never does: the line under it
        // is its caption (a door tile's LEFT FWD).
        var pending = new List<Bbs757EfbTextLine>();
        foreach (var l in lines)
        {
            var host = boxes.FirstOrDefault(b => b.Box.Contains(l.Centre));
            if (host == null) { pending.Add(l); continue; }
            if (!claimed.TryGetValue(host, out var list)) claimed[host] = list = new List<Bbs757EfbTextLine>();
            list.Add(l);
        }
        foreach (var l in pending)
        {
            var host = boxes.FirstOrDefault(b => !claimed.ContainsKey(b) && l.Centre.X > b.Box.Left && l.Centre.X < b.Box.Right
                && l.Centre.Y >= b.Box.Top - 10 && l.Centre.Y <= b.Box.Bottom + 10 && l.Pass != 'K');
            if (host == null) { loose.Add(l); continue; }
            claimed[host] = new List<Bbs757EfbTextLine> { l };
        }
        // Grown boxes: each box with the text it claimed.
        boxes = boxes.Select(b => claimed.TryGetValue(b, out var ls)
            ? b with { Box = Rectangle.Union(b.Box, ls.Select(l => l.Box).Aggregate(Rectangle.Union)) } : b).ToList();
        var inBox = boxes.ToDictionary(b => b, b => lines.Where(l => claimed.Any(kv => kv.Value.Contains(l)
            && Rectangle.Union(kv.Key.Box, kv.Value.Select(x => x.Box).Aggregate(Rectangle.Union)) == b.Box)).ToList());

        // One box can hold several controls: a row of tabs drawn as one band (Maintenance's LANDING
        // GEAR | HYDRAULIC | OXYGEN | FIRE PROTECT | APU, measured): text clusters more than 25 px
        // apart on one line are separate controls, each tapped at its own text.
        var controls = new List<(FoundBox Box, List<Bbs757EfbTextLine> Lines, Rectangle Hit, bool Tab)>();
        foreach (var b in boxes)
        {
            var parts = inBox[b].OrderBy(p => p.Box.Left).ToList();
            if (parts.Count == 0) continue;
            bool oneLine = parts.All(l => Math.Abs(l.Centre.Y - parts[0].Centre.Y) <= 8);
            var clusters = new List<List<Bbs757EfbTextLine>>();
            foreach (var l in parts)
            {
                var last = clusters.Count > 0 ? clusters[^1] : null;
                if (last != null && l.Box.Left - last.Max(m => m.Box.Right) <= 25) last.Add(l);
                else clusters.Add(new List<Bbs757EfbTextLine> { l });
            }
            bool tabs = oneLine && clusters.Count > 1;
            if (tabs)
                foreach (var c in clusters)
                    controls.Add((b, c, c.Select(l => l.Box).Aggregate(Rectangle.Union), true));
            else
                controls.Add((b, parts.OrderBy(p => p.Box.Top).ThenBy(p => p.Box.Left).ToList(), b.Box, false));
        }
        // A tab drawn in its own color beside a band of tabs (Maintenance's selected LANDING GEAR,
        // gray against the blue band, measured) is that band's selected tab.
        var forcedSelected = new HashSet<Rectangle>();
        foreach (var band in controls.Where(c => c.Tab).Select(c => c.Box.Box).Distinct().ToList())
            for (int i = 0; i < controls.Count; i++)
            {
                var c = controls[i];
                if (c.Tab || c.Lines.Count == 0) continue;
                var b = c.Box.Box;
                bool sameLine = Math.Abs(b.Top + b.Height / 2 - (band.Top + band.Height / 2)) <= 8;
                bool touching = Math.Abs(b.Right - band.Left) <= 10 || Math.Abs(band.Right - b.Left) <= 10;
                if (!sameLine || !touching) continue;
                var hitRect = c.Lines.Select(l => l.Box).Aggregate(Rectangle.Union);
                controls[i] = (c.Box, c.Lines, hitRect, true);
                forcedSelected.Add(hitRect);
            }
        // The selected tab: the one whose band color differs from its siblings' (an odd one out).
        var selectedTab = new HashSet<Rectangle>(forcedSelected);
        foreach (var g in controls.Where(c => c.Tab && !forcedSelected.Contains(c.Hit)).GroupBy(c => c.Box))
        {
            if (forcedSelected.Count > 0) break;
            var cols = g.Select(c => px.Lum(c.Hit.Left - 6, c.Hit.Top + c.Hit.Height / 2)).ToList();
            double median = cols.OrderBy(v => v).ElementAt(cols.Count / 2);
            int best = -1; double gap = 14;
            for (int i = 0; i < cols.Count; i++)
                if (Math.Abs(cols[i] - median) > gap) { gap = Math.Abs(cols[i] - median); best = i; }
            if (best >= 0) selectedTab.Add(g.ElementAt(best).Hit);
        }

        // Names and details from the loose text round each control: a short label just ABOVE it names
        // it (NOSE DOOR over OPEN; GND SRVC Mode: over BBS); a short label just BELOW it names it when
        // nothing above does (a door tile's LEFT FWD under CLOSED), or, when it holds a figure, is its
        // detail (0% under OPEN). A loose unit just right of a number field is its unit (1000 Gal).
        var name = new Dictionary<Rectangle, string>();
        var detail = new Dictionary<Rectangle, string>();
        var unit = new Dictionary<Rectangle, string>();
        foreach (var c in controls.Where(c => !c.Tab))
        {
            var box = c.Box.Box;
            // A generic action (FAIL NOW, SET…) on a row with a label of its own is named by its ROW.
            int rowY = box.Top + box.Height / 2;
            bool generic = GenericActions.Contains(StripMark(string.Join(" ", c.Lines.Select(l => l.Text))));
            if (generic && loose.Any(l => Math.Abs(l.Centre.Y - rowY) <= 14 && l.Box.Right < box.Left)) continue;
            var above = loose.Where(l => l.Text.Length <= 24 && !l.Text.Any(char.IsDigit) && l.Box.Bottom <= box.Top + 3
                    && box.Top - l.Box.Bottom < 30 && l.Centre.X > box.Left - 20 && l.Centre.X < box.Right + 20)
                .OrderBy(l => box.Top - l.Box.Bottom).FirstOrDefault();
            // A column header: a label over a box with more boxes further down its column names none of
            // them (ACTIONS over every FAIL NOW; Presets' ON GROUND over COLD & DARK, TURNAROUND...;
            // Failures' title over CATALOG, with FIRES under it). PaddleOCR's line boxes reach a few
            // pixels below the letters, which brings such a header within reach of the box under it.
            if (above != null && controls.Any(o => !o.Tab && o.Box.Box.Top > box.Bottom && Math.Abs(o.Box.Box.Left - box.Left) <= 12))
                above = null;
            if (above != null) { name[box] = above.Text.TrimEnd(':'); loose.Remove(above); }
            var below = loose.Where(l => l.Text.Length <= 24 && l.Box.Height <= 20 && l.Box.Top >= box.Bottom - 3
                    && l.Box.Top - box.Bottom < 22 && l.Centre.X > box.Left && l.Centre.X < box.Right)
                .OrderBy(l => l.Box.Top).FirstOrDefault();
            if (below != null)
            {
                if (below.Text.Any(char.IsDigit)) detail[box] = below.Text;
                else if (!name.ContainsKey(box) && below.Box.Height <= 14) name[box] = below.Text;
                else below = null;
                if (below != null) loose.Remove(below);
            }
            var right = loose.Where(l => l.Text.Length <= 4 && Math.Abs(l.Centre.Y - box.Top - box.Height / 2) <= 12
                    && l.Box.Left >= box.Right - 4 && l.Box.Left - box.Right < 30)
                .FirstOrDefault();
            if (right != null) { unit[box] = right.Text; loose.Remove(right); }
        }

        foreach (var c in controls)
        {
            var box = c.Box.Box;
            string state = string.Join(" ", c.Lines.Select(p => p.Text));
            if (state.Length == 0) continue;
            name.TryGetValue(box, out var cap);
            if (c.Tab) cap = null;
            if (IsNumberField(state))
            {
                // A number in a box is a value field (fuel, weights): said as a value, with its unit.
                string v = state + (unit.TryGetValue(box, out var u) ? " " + u.ToLowerInvariant() : "");
                o.Add(new(Bbs757EfbKind.Row, cap != null ? Bbs757EfbVocabulary.Pretty(cap) : "Value", box, Centre(box), false,
                    "fld:" + (cap ?? "") + "@" + box.X + "," + box.Y, Value: v));
                continue;
            }
            // A generic action (FAIL NOW, SET, RESET, OPEN…) with no name of its own takes its row's
            // first text (the failure it belongs to) so "Fail now" says which.
            if (cap == null && !c.Tab && GenericActions.Contains(StripMark(state)))
            {
                int cy = box.Top + box.Height / 2;
                var rowLabel = loose.Where(l => Math.Abs(l.Centre.Y - cy) <= 14 && l.Box.Right < box.Left)
                    .OrderBy(l => l.Box.Left).FirstOrDefault();
                if (rowLabel != null) cap = rowLabel.Text;
            }
            string text = Bbs757EfbVocabulary.Pretty(state);
            if (cap != null)
                text = GenericActions.Contains(StripMark(state)) && !name.ContainsKey(box)
                    ? text + ", " + Bbs757EfbVocabulary.Pretty(cap)
                    : Bbs757EfbVocabulary.Pretty(cap) + ": " + text;
            // Green is a TILE's "on" (CHOCKS); a green action button (RESET) is just green.
            if (c.Box.Tint == Bbs757EfbTint.Green && !HasStateWord(state) && !GenericActions.Contains(StripMark(state))) text += ", on";
            if (detail.TryGetValue(box, out var d)) text += ", " + d;
            if (c.Tab && selectedTab.Contains(c.Hit)) text += " (selected)";
            o.Add(new(c.Tab ? Bbs757EfbKind.Menu : Bbs757EfbKind.Button, text, c.Hit, Centre(c.Hit), true,
                (c.Tab ? "tab:" : "btn:") + (cap ?? "") + "|" + StripState(state) + "@" + c.Hit.X / 20 + "," + c.Hit.Y / 20, c.Box.Tint,
                Selected: c.Tab && selectedTab.Contains(c.Hit)));
        }

        // Loose text: a label with a slider beside it (the cabin lights), else rows of text joined
        // left to right; a short capitals line alone is a heading.
        var used = new HashSet<Bbs757EfbTextLine>();
        foreach (var l in loose)
        {
            if (used.Contains(l) || l.Text.Split(' ').Length > 4) continue;
            var knob = FindKnob(px, l.Centre.Y, l.Box.Right + 8, Math.Min(Width - 60, l.Box.Right + 300), boxes);
            if (knob is not int k) continue;
            var value = loose.FirstOrDefault(v => v != l && !used.Contains(v) && v.Box.Left > k && Math.Abs(v.Centre.Y - l.Centre.Y) <= 10);
            var (tl, tr) = CabinLights.Contains(l.Text) ? CabinLightTrack : TrackExtent(px, l.Centre.Y, k);
            used.Add(l);
            if (value != null) used.Add(value);
            // A value OCR cannot read (the cabin lights' small gray OFF) is said from the knob.
            string val = value != null ? Bbs757EfbVocabulary.Pretty(value.Text) : SliderPercent(k, tl, tr);
            o.Add(new(Bbs757EfbKind.Slider, Bbs757EfbVocabulary.Pretty(l.Text), l.Box, new Point(k, l.Centre.Y), true, "sld:" + l.Text,
                Value: val, Slider: new Bbs757EfbSlider(tl, tr, k)));
        }
        foreach (var l in loose.Where(l => !used.Contains(l) && FlatButtons.Contains(l.Text)).ToList())
        {
            // A button drawn as plain text on the page (the checklist's RESET). The AIRAC page's LOGOUTs are boxed (bright/55):
            // ParseRows makes them its rows' buttons.
            used.Add(l);
            o.Add(new(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(l.Text), l.Box, l.Centre, true, "btn:" + l.Text));
        }
        foreach (var row in Rows(loose.Where(l => !used.Contains(l)).ToList()))
        {
            var first = row[0];
            string text = string.Join(row.Count > 1 && row.Zip(row.Skip(1)).Any(p => p.Second.Box.Left - p.First.Box.Right >= 40) ? ", " : "  ",
                row.Select(r => r.Text));
            var box = row.Select(r => r.Box).Aggregate(Rectangle.Union);
            bool heading = row.Count == 1 && IsHeadingText(first.Text) && first.Box.Height >= 14;
            o.Add(new(heading ? Bbs757EfbKind.Heading : Bbs757EfbKind.Text, Bbs757EfbVocabulary.Pretty(text), box,
                Centre(box), false, (heading ? "h:" : "txt:") + text));
        }
        return Order(o);
    }

    /// <summary>The cabin lighting sliders (Aircraft, Cabin &amp; doors) and their track, measured on
    /// the captures: the knob rests at 1146 when OFF, the track's line runs to 1268.</summary>
    private static readonly HashSet<string> CabinLights = new() { "CEILING", "WINDOW", "ENTRY" };
    private static readonly (int, int) CabinLightTrack = (1146, 1268);

    /// <summary>"Off" at the track's left end, else the knob's place along it in percent.</summary>
    internal static string SliderPercent(int knob, int left, int right)
    {
        if (right <= left) return "";
        int pct = (int)Math.Round(100.0 * (knob - left) / (right - left));
        pct = Math.Clamp(pct, 0, 100);
        return pct <= 2 ? "Off" : pct + " percent";
    }

    /// <summary>Words that are buttons even drawn as plain text.</summary>
    private static readonly HashSet<string> FlatButtons = new(StringComparer.OrdinalIgnoreCase) { "RESET", "LOGOUT", "SELECT" };

    /// <summary>Buttons whose words do not say what they act on: their row or caption names it. SET UP is
    /// Failures' button beside FAIL NOW (Windows OCR read it "SET w", which snapped to SET).</summary>
    private static readonly HashSet<string> GenericActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "FAIL NOW", "SET", "SET UP", "RESET", "REPAIR", "REPAIR ALTN", "OPEN", "CLOSE", "CLEAR", "ARM", "DISARM",
    };

    /// <summary>The words without a dropdown arrow read as a one-letter last word ("SET w").</summary>
    internal static string StripMark(string t)
    {
        var w = t.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w.Length > 1 && w[^1].Length == 1 ? string.Join(' ', w[..^1]) : t.Trim();
    }

    /// <summary>A number with at most a unit or sign (1000, 20100, 28.8 %, 16/16, 0%): a value field.</summary>
    internal static bool IsNumberField(string t) =>
        System.Text.RegularExpressions.Regex.IsMatch(t.Trim(), @"^[-+]?\d[\d.,/ ]*\s*(%|lbs|kg|gal|ltr|f|c)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool IsHeadingText(string t) =>
        t.Length >= 3 && !t.Any(char.IsLower) && t.Count(char.IsLetter) >= 3 && t.Split(' ').Length <= 3 && !t.Any(char.IsDigit);

    /// <summary>The words a tile's state ends its text with, or that are all of it: a door's box reads OPEN, CLOSED or, gray,
    /// UNAVAIL under its caption (Maintenance's gear doors, measured: tablet-presses.md). The reader leaves them out of the tile's
    /// key, so a press that changes a tile's state keeps its key, and <see cref="Bbs757EfbPress.Relocate"/> leaves them out when it
    /// compares a tile's words with another's.</summary>
    internal static readonly string[] StateWords = { "OFF", "ON", "REMOVED", "CONNECTED", "OPEN", "CLOSED", "ATTACHED", "UNAVAIL" };

    private static bool HasStateWord(string t) => StateWords.Any(w => t.EndsWith(" " + w, StringComparison.Ordinal) || t == w);

    /// <summary>The words a box's key carries, between its caption and its place ("btn:LATEST CYCLE|UPDATE@59,14" is UPDATE,
    /// "btn:|FUEL TRUCK@56,28" is FUEL TRUCK): the box's own words without their state word (<see cref="StripState"/>), never the
    /// caption or the row label the reader may name the box by; "" for a key that is no box's (a switch, the top bar, a menu
    /// entry).</summary>
    internal static string KeyWords(string key) =>
        key.StartsWith("btn:", StringComparison.Ordinal) ? key.Substring(4).Split('|').Last().Split('@')[0] : "";

    /// <summary>A box's words for its key, without the state word <see cref="HasStateWord"/> finds: "FUEL TRUCK OFF" is FUEL TRUCK,
    /// and a door's CLOSED alone is no words (its caption and its place name it, whatever state it shows).</summary>
    private static string StripState(string t)
    {
        foreach (var w in StateWords)
        {
            if (t == w) return "";
            if (t.EndsWith(" " + w, StringComparison.Ordinal)) return t.Substring(0, t.Length - w.Length - 1);
        }
        return t;
    }

    // ------------------------------------------------------------------ settings rows

    /// <summary>Where a Settings row's value column starts: the row's value (its word, a figure) and, on the AIRAC data page, its
    /// button lie right of it.</summary>
    private const int ValueLeft = 1180;

    /// <summary>A box in a Settings page's value column with words in it (<see cref="RowButtons"/>): a button, named by the
    /// label of the row it stands in once one is found (its caption, null until then).</summary>
    private sealed class RowButton(FoundBox box, List<Bbs757EfbTextLine> lines)
    {
        public FoundBox Box { get; } = box;
        public List<Bbs757EfbTextLine> Lines { get; } = lines;
        public string? Caption { get; set; }
    }

    private static List<Bbs757EfbItem> ParseRows(List<Bbs757EfbTextLine> lines, Bbs757EfbPixels px)
    {
        var o = new List<Bbs757EfbItem>();
        var used = new HashSet<Bbs757EfbTextLine>();
        var boxes = FindButtonBoxes(px, ContentArea);
        // A boxed button in the value column is no row's value: its words are its own (the AIRAC data page's UPDATE or REMOVE and
        // its LOGOUTs, read as the rows' values until 2026-10-30, so none could be pressed).
        var buttons = RowButtons(lines, boxes);
        foreach (var button in buttons) used.UnionWith(button.Lines);
        var labels = lines.Where(l => l.Box.Left < 700 && l.Box.Left > MenuRight).OrderBy(l => l.Box.Top).ToList();
        foreach (var label in labels)
        {
            if (used.Contains(label)) continue;
            if (label.Text.Split(' ').Length > 6) continue;   // a note, read as text below
            int cy = label.Centre.Y;
            // A button whose height holds the label is the label's row's, taken by the first label met (UPDATE's box spans the
            // latest and the installed cycle); that row has no value of its own.
            var rowButton = buttons.FirstOrDefault(b => b.Caption == null && cy >= b.Box.Box.Top && cy <= b.Box.Box.Bottom);
            var value = rowButton != null ? null
                : lines.FirstOrDefault(v => !used.Contains(v) && v.Box.Left > ValueLeft && Math.Abs(v.Centre.Y - cy) <= 14);
            string name = label.Text;
            string pretty = Bbs757EfbVocabulary.Pretty(name);
            used.Add(label);
            if (value != null) used.Add(value);
            string val = value?.Text ?? "";

            // A row of one of the pilot's identifiers: its label, or another line of the label column on its row (a label OCR
            // split in two, read in either order), holds the identifier's distinctive word (Bbs757EfbVocabulary.CredentialLabel),
            // whatever OCR made of the rest.
            string? credential = Bbs757EfbVocabulary.CredentialLabel(name)
                ?? labels.Where(l => l != label && Math.Abs(l.Centre.Y - cy) <= 14)
                    .Select(l => Bbs757EfbVocabulary.CredentialLabel(l.Text)).FirstOrDefault(c => c != null);
            if (credential != null)
            {
                // Never show, speak or log a credential: say whether one is set, nothing more. The row is named by the tablet's
                // own label, never by the read, which may have run into the field beside it; its button (the SimBrief ID's
                // LOGOUT) is named by that label too and says only the words in its own box.
                if (rowButton != null) rowButton.Caption = RowCaption(credential);
                foreach (var l in lines.Where(l => Math.Abs(l.Centre.Y - cy) <= 14)) used.Add(l);
                o.Add(new(Bbs757EfbKind.Row, Bbs757EfbVocabulary.Pretty(credential), label.Box, label.Centre, false, "row:" + credential,
                    Value: HasFieldText(px, cy) ? "set" : "not set"));
                continue;
            }

            if (rowButton != null)
            {
                // The row's own words, left of its button and within its height, stay text, a line each as the tablet writes
                // them: "Latest cycle: 2611, Update available!", "Installed cycle: 2610 | exp: 2026-10-28". The first line's
                // first phrase names the button, whichever of that line's parts OCR boxed a pixel higher ("2611" over "LATEST
                // CYCLE:" on bright/55). A line inside a box the page draws (a text field: the identifier rows' at x 897-1196) is
                // left out of the words and of the page, so a row whose label OCR misread past knowing still never says what an
                // identifier's field holds.
                var box = rowButton.Box.Box;
                var beside = lines.Where(l => l != label && !used.Contains(l) && l.Box.Right <= box.Left
                    && l.Centre.Y >= box.Top && l.Centre.Y <= box.Bottom).ToList();
                used.UnionWith(beside);
                var words = beside.Where(l => !boxes.Any(b => b.Box.Contains(l.Centre))).Prepend(label).ToList();
                var rows = Rows(words).ToList();
                rowButton.Caption = RowCaption(Phrases(rows[0])[0]);
                foreach (var row in rows)
                {
                    var rowBox = row.Select(r => r.Box).Aggregate(Rectangle.Union);
                    o.Add(new(Bbs757EfbKind.Text, RowText(row), rowBox, Centre(rowBox), false,
                        "txt:" + string.Join(" ", row.Select(r => r.Text))));
                }
                continue;
            }

            int searchTo = value != null ? value.Box.Left - 4 : 1300;
            var sw = FindSwitch(px, cy, Math.Max(label.Box.Right + 20, 700), searchTo);
            if (sw is { } s)
            {
                // Manual EFB brightness: the switch (AUTO/MANUAL) and the brightness slider share the
                // row; the value then is the brightness (measured: "100%").
                var dual = FindKnob(px, cy, label.Box.Right + 20, s.Hit.X - 40, boxes);
                if (dual is int dk)
                {
                    o.Add(new(Bbs757EfbKind.Toggle, pretty, label.Box, s.Hit, true, "tgl:" + name,
                        On: s.On, Value: s.On ? "Auto" : "Manual"));
                    var (dl, dr) = SliderTrack(name, px, cy, dk);
                    o.Add(new(Bbs757EfbKind.Slider, Bbs757EfbVocabulary.Pretty(name.Replace(" MODE", "")), label.Box, new Point(dk, cy), true,
                        "sld:" + name, Value: val, Slider: new Bbs757EfbSlider(dl, dr, dk)));
                    continue;
                }
                o.Add(new(Bbs757EfbKind.Toggle, pretty, label.Box, s.Hit, true, "tgl:" + name,
                    On: s.On, Value: Bbs757EfbVocabulary.Pretty(val)));
                continue;
            }
            var knob = FindKnob(px, cy, label.Box.Right + 20, Math.Min(searchTo, 1225), boxes);
            if (knob is int k)
            {
                var (tl, tr) = SliderTrack(name, px, cy, k);
                o.Add(new(Bbs757EfbKind.Slider, pretty, label.Box, new Point(k, cy), true, "sld:" + name,
                    Value: val, Slider: new Bbs757EfbSlider(tl, tr, k)));
                continue;
            }
            // A label with no value, switch or slider beside it is a section's title ("PILOT CALLOUTS").
            if (val.Length == 0)
                o.Add(new(Bbs757EfbKind.Heading, pretty, label.Box, label.Centre, false, "h:" + name));
            else
                o.Add(new(Bbs757EfbKind.Row, pretty, label.Box, label.Centre, false, "row:" + name, Value: Bbs757EfbVocabulary.Pretty(val)));
        }
        foreach (var button in buttons)
            o.Add(RowButtonItem(button));
        var rest = lines.Where(l => !used.Contains(l)).ToList();
        o.AddRange(ParseContent(rest, px));
        return Order(o);
    }

    /// <summary>
    /// The buttons of a Settings page's value column: each box <see cref="FindButtonBoxes"/> found there (from x
    /// <see cref="ValueLeft"/>) with the lines whose centers it holds, unless they read as a number, which is a value in a box as
    /// on any other page (<see cref="IsNumberField"/>). Measured on Settings, AIRAC data (2026-10-30, bright/55): UPDATE green and
    /// both LOGOUTs salmon at x 1197-1394, 58 px high; the identifier rows' gray text fields end at x 1196 and are no button.
    /// </summary>
    private static List<RowButton> RowButtons(List<Bbs757EfbTextLine> lines, List<FoundBox> boxes)
    {
        var o = new List<RowButton>();
        foreach (var box in boxes.Where(b => b.Box.Left > ValueLeft))
        {
            var inside = lines.Where(l => box.Box.Contains(l.Centre)).OrderBy(l => l.Box.Top).ThenBy(l => l.Box.Left).ToList();
            if (inside.Count == 0 || IsNumberField(string.Join(" ", inside.Select(l => l.Text)))) continue;
            o.Add(new RowButton(box, inside));
        }
        return o;
    }

    /// <summary>A row's label as a button's caption: the words before its colon ("LATEST CYCLE:" names UPDATE "Latest cycle").</summary>
    private static string RowCaption(string label) => label.Split(':')[0].Trim();

    /// <summary>
    /// A button of the value column as an action beside a row label is named ("Reset, Brake temperature" on Maintenance): its
    /// words, then its row's label ("Update, Latest cycle", "Logout, SIMBRIEF ID number"), or its words alone when no row's label
    /// lay within its height. Its key carries that label and its own words ("btn:LATEST CYCLE|UPDATE@59,14"), so the words a
    /// press is judged by are the box's (<see cref="KeyWords"/>, <see cref="Bbs757EfbExtras.HookFor"/>). Tapped at its box's center.
    /// </summary>
    private static Bbs757EfbItem RowButtonItem(RowButton button)
    {
        string words = string.Join(" ", button.Lines.Select(l => l.Text));
        string? caption = button.Caption is { Length: > 0 } c ? c : null;
        string text = Bbs757EfbVocabulary.Pretty(words) + (caption != null ? ", " + Bbs757EfbVocabulary.Pretty(caption) : "");
        var box = button.Box.Box;
        return new(Bbs757EfbKind.Button, text, box, Centre(box), true,
            "btn:" + (caption ?? "") + "|" + StripState(words) + "@" + box.X / 20 + "," + box.Y / 20, button.Box.Tint);
    }

    /// <summary>One line of a Settings row's words beside its button: its phrases (<see cref="Phrases"/>) left to right, each as
    /// the tablet writes it (<see cref="Bbs757EfbVocabulary.Pretty"/>), parted by commas. "LATEST CYCLE:", "2611" and "UPDATE
    /// AVAILABLE!" are "Latest cycle: 2611, Update available!"; a sentence OCR read in two parts is one sentence again.</summary>
    private static string RowText(IReadOnlyList<Bbs757EfbTextLine> parts) =>
        string.Join(", ", Phrases(parts).Select(Bbs757EfbVocabulary.Pretty));

    /// <summary>
    /// A line's parts, left to right, as phrases, parted at the gap the page's other rows are (<see cref="ParseContent"/> joins a
    /// row with commas once its lines lie 40 px apart): a part runs on into the one before it with a space after a label's colon
    /// ("LATEST CYCLE:" and "2611") or across a gap under 40 px (a sentence OCR read in two), and starts a phrase of its own
    /// across a wider gap. The first phrase names a row's button (<see cref="RowCaption"/>), never cut where OCR cut it.
    /// </summary>
    private static List<string> Phrases(IReadOnlyList<Bbs757EfbTextLine> parts)
    {
        var phrases = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            bool runsOn = i > 0 && (parts[i - 1].Text.EndsWith(':') || parts[i].Box.Left - parts[i - 1].Box.Right < 40);
            if (runsOn) phrases[^1] += " " + parts[i].Text;
            else phrases.Add(parts[i].Text);
        }
        return phrases;
    }

    /// <summary>
    /// A settings slider's track. Measured by tapping along each (2026-10-26): NOTIFICATIONS
    /// FADE-OUT TIMER, BOARDING &amp; LOAD RATE and IRS ALIGNMENT TIME run 895-1190 (taps outside are
    /// ignored); the manual EFB brightness 795-1100; EFB BACKGROUND takes its choices from 804 (DEFAULT)
    /// to 1050 (9, and anything right of it 9 too), placed unevenly at the left (826 is 1, 875 is 2,
    /// then every 25 px: efb-settings.md), so its choices are spread over 804-1050 and the press's
    /// closed loop steps over what the even spread misses. The unfilled part is the page's color, so it
    /// cannot be seen: a slider not measured takes 895-1190, widened to its knob.
    /// </summary>
    internal static (int Left, int Right) SliderTrack(string label, Bbs757EfbPixels px, int cy, int knobX)
    {
        if (label == "EFB BRIGHTNESS MODE") return (795, 1100);
        if (label == "EFB BACKGROUND") return (804, 1050);
        return (Math.Min(895, knobX), Math.Max(1190, knobX));
    }

    /// <summary>
    /// A two-state switch on row <paramref name="cy"/> between <paramref name="from"/> and
    /// <paramref name="to"/>: a near-white ROUND knob (8-30 px wide and tall) inside a pill whose color
    /// shows on at least one side of the knob and differs from the page. On: the knob at the pill's
    /// right (the pill blue); off: at its left (gray). Measured dim and bright: Settings rows put the
    /// pill at x 1140-1200, the Callouts page at 1220-1275. The hit point is the pill's center.
    /// A QR code's white squares fail the round-knob test (measured on the AIRAC page).
    /// </summary>
    internal static (Point Hit, bool On)? FindSwitch(Bbs757EfbPixels px, int cy, int from = 1130, int to = 1210)
    {
        bool White(int x, int y)
        {
            var (r, g, b) = px.At(x, y);
            return 0.299 * r + 0.587 * g + 0.114 * b > 150 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 40;
        }
        int knobL = -1, knobR = -1, runL = -1;
        for (int x = Math.Max(0, from); x <= to; x++)
        {
            bool w = White(x, cy);
            if (w && runL < 0) runL = x;
            if ((!w || x == to) && runL >= 0)
            {
                int rr = w ? x : x - 1;
                if (rr - runL > knobR - knobL) { knobL = runL; knobR = rr; }
                runL = -1;
            }
        }
        int kw = knobR - knobL + 1;
        if (knobL < 0 || kw < 8 || kw > 30) return null;
        int kc = (knobL + knobR) / 2;
        int top = cy, bottom = cy;
        while (top > cy - 20 && White(kc, top - 1)) top--;
        while (bottom < cy + 20 && White(kc, bottom + 1)) bottom++;
        int kh = bottom - top + 1;
        if (kh < 8 || kh > 30 || Math.Abs(kh - kw) > 8) return null;
        // The pill: its color beside the knob, left and right, and not the page.
        var leftC = px.At(knobL - 6, cy);
        var rightC = px.At(knobR + 6, cy);
        bool leftPill = px.FromBackground(leftC) >= 14 && !White(knobL - 6, cy);
        bool rightPill = px.FromBackground(rightC) >= 14 && !White(knobR + 6, cy);
        if (!leftPill && !rightPill) return null;
        // Which side the pill extends to tells where the knob sits.
        int extL = 0, extR = 0;
        while (extL < 60 && px.FromBackground(px.At(knobL - 1 - extL, cy)) >= 14 && !White(knobL - 1 - extL, cy)) extL++;
        while (extR < 60 && px.FromBackground(px.At(knobR + 1 + extR, cy)) >= 14 && !White(knobR + 1 + extR, cy)) extR++;
        if (extL + extR < 12 || extL + extR > 70) return null;
        bool on = extL > extR;
        int pillL = knobL - extL, pillR = knobR + extR;
        return (new Point((pillL + pillR) / 2, cy), on);
    }

    /// <summary>
    /// A slider's knob between <paramref name="from"/> and <paramref name="to"/>: a ROUND blob of the
    /// brightest blue (blue channel at least 150 normalized: the knob and the filled track; buttons
    /// are a darker blue), not inside a button box, at most 26 px tall. Its center is 7 px in from the
    /// right end of the blue run (the knob sits at the end of the filled track).
    /// </summary>
    internal static int? FindKnob(Bbs757EfbPixels px, int cy, int from, int to, IReadOnlyList<FoundBox> boxes)
    {
        bool Blue(int x, int y)
        {
            var (r, g, b) = px.At(x, y);
            return b >= 150 && b > r * 1.8 && b > g * 1.25;
        }
        int bestL = -1, bestR = -1;
        for (int y = cy - 2; y <= cy + 2; y += 2)
        {
            int runL = -1;
            for (int x = Math.Max(0, from); x <= to; x++)
            {
                bool blue = Blue(x, y);
                if (blue && runL < 0) runL = x;
                if ((!blue || x == to) && runL >= 0)
                {
                    int rr = blue ? x : x - 1;
                    if (rr - runL > bestR - bestL) { bestL = runL; bestR = rr; }
                    runL = -1;
                }
            }
        }
        if (bestL < 0 || bestR - bestL < 8) return null;
        int kx = bestR - 7;
        if (boxes.Any(b => b.Box.Contains(kx, cy))) return null;
        int top = cy, bottom = cy;
        while (top > cy - 30 && Blue(kx, top - 1)) top--;
        while (bottom < cy + 30 && Blue(kx, bottom + 1)) bottom++;
        if (bottom - top > 26 || bottom - top < 6) return null;
        return kx;
    }

    /// <summary>A slider's track either side of its knob, as far as the row differs from the page
    /// (a faint line on the cabin lights; the filled part is blue).</summary>
    internal static (int Left, int Right) TrackExtent(Bbs757EfbPixels px, int cy, int knobX)
    {
        bool Line(int x) => px.FromBackground(px.At(x, cy)) > 10 || px.FromBackground(px.At(x, cy - 1)) > 10 || px.FromBackground(px.At(x, cy + 1)) > 10;
        int l = knobX, r = knobX;
        while (l > MenuRight && Line(l - 1) && knobX - l < 400) l--;
        while (r < px.Width - 1 && Line(r + 1) && r - knobX < 400) r++;
        return (l, r);
    }

    /// <summary>Whether a text field on this row holds anything (bright pixels between x 700 and
    /// 1180): used for credential rows, so their text is never read.</summary>
    private static bool HasFieldText(Bbs757EfbPixels px, int cy)
    {
        int bright = 0;
        for (int y = cy - 8; y <= cy + 8; y += 2)
            for (int x = 700; x < 1180; x += 2)
                if (px.Lum(x, y) > 120) bright++;
        return bright > 12;
    }

    // ------------------------------------------------------------------ checklist

    private static List<Bbs757EfbItem> ParseChecklist(List<Bbs757EfbTextLine> lines, Bbs757EfbPixels px)
    {
        var o = new List<Bbs757EfbItem>();
        var used = new HashSet<Bbs757EfbTextLine>();
        // A checklist box read as a letter in front of its item ("D Oxygen", "C] Fuel", measured):
        // the item starts after it.
        lines = lines.Select(l =>
        {
            var sp = l.Text.IndexOf(' ');
            if (l.Box.Left < 300 && l.Box.Left > 270 && sp > 0 && sp <= 2 && l.Words.Count > 1)
            {
                var rest = l.Words.Skip(1).ToList();
                return l with { Text = string.Join(" ", rest.Select(w => w.Text)), Words = rest, Box = rest.Select(w => w.Box).Aggregate(Rectangle.Union) };
            }
            return l;
        }).ToList();
        // The checklist's own name: centered over the list (x 600-1000, above y 180), with a box at
        // x 298 that ticks the whole list.
        foreach (var l in lines.Where(l => l.Box.Left > 600 && l.Box.Left < 1000 && l.Box.Top < 180))
        {
            used.Add(l);
            bool? all = CheckboxAt(px, 298, l.Centre.Y);
            o.Add(new(all != null ? Bbs757EfbKind.Checkbox : Bbs757EfbKind.Heading,
                Bbs757EfbVocabulary.Pretty(l.Text) + (all != null ? ", whole checklist" : ""), l.Box,
                new Point(298, l.Centre.Y), all != null, "chk:" + l.Text, On: all));
        }
        // Each item: its words in the left column (x 300-700), its value in the right (x > 1000),
        // its box at x 298.
        foreach (var l in lines.Where(l => !used.Contains(l) && l.Box.Left >= 300 && l.Box.Left < 700).OrderBy(l => l.Box.Top))
        {
            int cy = l.Centre.Y;
            bool? box = CheckboxAt(px, 298, cy);
            if (box == null) continue;
            var value = lines.FirstOrDefault(v => !used.Contains(v) && v.Box.Left >= 1000 && Math.Abs(v.Centre.Y - cy) <= 12);
            used.Add(l);
            if (value != null) used.Add(value);
            string text = l.Text + (value != null ? ": " + value.Text : "");
            o.Add(new(Bbs757EfbKind.Checkbox, text, l.Box, new Point(298, cy), true, "chk:" + l.Text, On: box));
        }
        o.AddRange(ParseContent(lines.Where(l => !used.Contains(l)).ToList(), px));
        return Order(o);
    }

    /// <summary>
    /// A checklist box at (x, cy): null when there is none, else whether it is ticked (bright pixels inside its border).
    /// The border: lighter than the page 11 px either side. The box's own top and bottom borders are the nearest rows within
    /// 12 px above and below the row that are lit right across it (x - 9 to x + 9, at least halfway from the band round
    /// the box to its side border; a tick's fill leaves a dark gap inside each side border); the inside is between them,
    /// or as far as 12 px where none is found. Measured on the header (2026-10-29): its words sit 5 px above its box's
    /// center, so a window round the words took in the box's bright top border, read as a tick on an empty box.
    /// </summary>
    internal static bool? CheckboxAt(Bbs757EfbPixels px, int x, int cy)
    {
        bool Edge(int ex) => Enumerable.Range(-2, 5).Any(d => px.FromBackground(px.At(ex + d, cy)) > 16);
        if (!Edge(x - 11) || !Edge(x + 11)) return null;
        double side = Enumerable.Range(x - 14, 6).Max(xx => px.Lum(xx, cy));
        double band = Math.Min(px.Lum(x - 16, cy), px.Lum(x + 16, cy));
        bool Border(int y) => Enumerable.Range(x - 9, 19).All(xx => px.Lum(xx, y) >= (side + band) / 2);
        int top = cy - 12, bottom = cy + 12;
        for (int y = cy; y >= cy - 12; y--) if (Border(y)) { top = y; break; }
        for (int y = cy; y <= cy + 12; y++) if (Border(y)) { bottom = y; break; }
        int bright = 0;
        for (int yy = top + 1; yy < bottom; yy++)
            for (int xx = x - 7; xx <= x + 7; xx++)
                if (px.Lum(xx, yy) > 110) bright++;
        return bright > 10;
    }

    // ------------------------------------------------------------------ boxes

    /// <summary>A filled box of button size found from the pixels, with its color.</summary>
    internal sealed record FoundBox(Rectangle Box, Bbs757EfbTint Tint);

    /// <summary>Clearly not the surface it sits on (more than 18 normalized from it).</summary>
    private static bool Filled((int R, int G, int B) c, (int R, int G, int B) bg, int threshold = 18) =>
        Math.Max(Math.Abs(c.R - bg.R), Math.Max(Math.Abs(c.G - bg.G), Math.Abs(c.B - bg.B))) > threshold;

    /// <summary>
    /// Every filled box of button size in <paramref name="area"/>: connected pixels of one color
    /// family that differ from the page, each step a small change (a shaded button is a smooth
    /// gradient; its edge against the page is a jump), 60-520 px wide and 18-70 high, at least half
    /// filled (the text is a hole). A region bigger than that is a PANEL (the ground A/C box, the
    /// GND SRVC Mode box, the cabin lighting box): it is searched again with the panel's own color
    /// as the background, since a button on a panel can shade smoothly into it (measured: the BBS
    /// button's bottom (9,37,69) against its panel (16,50,84), normalized).
    /// </summary>
    internal static List<FoundBox> FindButtonBoxes(Bbs757EfbPixels px, Rectangle area)
    {
        // A button found on the page and again inside its panel is one button.
        var o = new List<FoundBox>();
        foreach (var b in FindButtonBoxes(px, area, px.Background, depth: 0))
            if (!o.Any(k => Near(k.Box, b.Box))) o.Add(b);
        return o;
    }

    private static bool Near(Rectangle a, Rectangle b) =>
        Math.Abs(a.Left - b.Left) <= 6 && Math.Abs(a.Top - b.Top) <= 6 && Math.Abs(a.Right - b.Right) <= 6 && Math.Abs(a.Bottom - b.Bottom) <= 6;

    private static List<FoundBox> FindButtonBoxes(Bbs757EfbPixels px, Rectangle area, (int R, int G, int B) bg, int depth)
    {
        var found = new List<FoundBox>();
        var seen = new bool[px.Width * px.Height];
        const int Step = 14;
        // Inside a panel a button's shading can come within a few levels of the panel's color (the BBS
        // button's lower half, measured): the panel search takes a finer threshold and a shorter box.
        int threshold = depth == 0 ? 18 : 10, minH = depth == 0 ? 18 : 14;
        var queue = new Queue<int>();
        for (int y = area.Top; y < area.Bottom; y += 3)
            for (int x = area.Left; x < area.Right; x += 3)
            {
                int i0 = y * px.Width + x;
                if (seen[i0]) continue;
                var seed = px.At(x, y);
                if (!Filled(seed, bg, threshold)) { seen[i0] = true; continue; }
                var tint = TintOf(seed);
                int minX = x, maxX = x, minY = y, maxY = y, count = 0;
                queue.Clear(); queue.Enqueue(i0); seen[i0] = true;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    int cx = i % px.Width, cy = i / px.Width;
                    count++;
                    if (cx < minX) minX = cx; if (cx > maxX) maxX = cx; if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
                    var c = px.At(cx, cy);
                    TryAdd(cx + 1, cy); TryAdd(cx - 1, cy); TryAdd(cx, cy + 1); TryAdd(cx, cy - 1);

                    void TryAdd(int nx, int ny)
                    {
                        if (nx < area.Left || ny < area.Top || nx >= area.Right || ny >= area.Bottom) return;
                        int ni = ny * px.Width + nx;
                        if (seen[ni]) return;
                        var n = px.At(nx, ny);
                        if (!Filled(n, bg, threshold) || TintOf(n) != tint) return;
                        if (Math.Abs(n.R - c.R) > Step || Math.Abs(n.G - c.G) > Step || Math.Abs(n.B - c.B) > Step) return;
                        seen[ni] = true;
                        queue.Enqueue(ni);
                    }
                }
                int w = maxX - minX + 1, h = maxY - minY + 1;
                var rect = new Rectangle(minX, minY, w, h);
                if (w >= 60 && w <= 520 && h >= minH && h <= 70)
                {
                    if (count >= 0.5 * w * h) found.Add(new FoundBox(rect, tint));
                }
                else if (depth == 0 && w > 60 && h > 40 && count >= 0.3 * w * h)
                {
                    // A panel: look inside it against its own color.
                    var inner = Rectangle.Intersect(rect, area);
                    var panel = ModeOf(px, inner);
                    found.AddRange(FindButtonBoxes(px, inner, panel, depth + 1));
                }
            }
        return found;
    }

    /// <summary>The commonest color in a rectangle (a panel's own color).</summary>
    private static (int R, int G, int B) ModeOf(Bbs757EfbPixels px, Rectangle r)
    {
        var counts = new Dictionary<int, int>();
        for (int y = r.Top; y < r.Bottom; y += 3)
            for (int x = r.Left; x < r.Right; x += 3)
            {
                var (cr, cg, cb) = px.At(x, y);
                int key = (cr / 6) << 12 | (cg / 6) << 6 | (cb / 6);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        int best = counts.MaxBy(kv => kv.Value).Key;
        return (((best >> 12) & 63) * 6 + 3, ((best >> 6) & 63) * 6 + 3, (best & 63) * 6 + 3);
    }

    /// <summary>A box color's meaning by its balance (normalized colors): measured red tile
    /// (235,0,0), chocks green (91,223,61), unavailable gray (136,136,136), button blue (44,86,124).</summary>
    internal static Bbs757EfbTint TintOf((int R, int G, int B) c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        if (max < 12) return Bbs757EfbTint.None;
        if (c.R > 1.8 * c.G && c.R > 1.8 * c.B) return Bbs757EfbTint.Red;
        if (c.G > 1.4 * c.R && c.G > 1.2 * c.B) return Bbs757EfbTint.Green;
        if (c.B > 1.4 * c.R && c.B >= c.G) return Bbs757EfbTint.Blue;
        if (max - min <= max * 0.2) return Bbs757EfbTint.Grey;
        return Bbs757EfbTint.Other;
    }

    /// <summary>
    /// A scroll bar down the page's right edge: column x 1386 lighter than 100 (normalized) for 300 rows
    /// or more between y 90 and 1040. Measured: Settings, Callouts 944 rows (track and thumb), Settings,
    /// Aircraft none (its page fits).
    /// </summary>
    internal static bool IsScrollable(Bbs757EfbPixels px)
    {
        int rows = 0;
        for (int y = 90; y < Math.Min(1040, px.Height); y++)
            if (px.Lum(1386, y) > 100) rows++;
        return rows >= 300;
    }

    /// <summary>One wheel step: where the agent's wheel is given (the content's middle) and how far
    /// (measured: 300 moves the Callouts list about 130 px).</summary>
    public static readonly Point WheelPoint = new(800, 600);
    public const int WheelStep = 300;

    // ------------------------------------------------------------------ helpers

    private static Point Centre(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static IEnumerable<List<Bbs757EfbTextLine>> Rows(List<Bbs757EfbTextLine> lines)
    {
        var rows = new List<List<Bbs757EfbTextLine>>();
        foreach (var l in lines.OrderBy(l => l.Box.Top).ThenBy(l => l.Box.Left))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].Centre.Y - l.Centre.Y) <= 6
                && (r.Any(o => Math.Abs(o.Box.Right - l.Box.Left) < 40 || Math.Abs(l.Box.Right - o.Box.Left) < 40)
                    // a table row: short cells on one line (FAILURE | STATUS: "L ENGINE FIRE", "READY")
                    || (l.Text.Split(' ').Length <= 3 && r.All(o => o.Text.Split(' ').Length <= 3))));
            if (row != null) row.Add(l); else rows.Add(new List<Bbs757EfbTextLine> { l });
        }
        return rows.Select(r => r.OrderBy(l => l.Box.Left).ToList());
    }

    /// <summary>Reading order: rows of items within 7 px of each other, then left to right (<see cref="Bbs757EfbReadingOrder"/>).</summary>
    private static List<Bbs757EfbItem> Order(List<Bbs757EfbItem> items) =>
        Bbs757EfbReadingOrder.Order(items, i => i.Box, 7);
}
