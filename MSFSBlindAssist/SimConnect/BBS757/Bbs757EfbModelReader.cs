using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Turns the EFB memory walk (<see cref="Bbs757EfbMemory"/>) into the window's model (<see cref="Bbs757EfbScreenModel"/>),
/// the memory replacement for <see cref="Bbs757EfbScreen"/>'s OCR parse. The front content app's page becomes the model:
/// its side menu (<c>GENERAL</c>, <c>FUEL</c>, …) as <see cref="Bbs757EfbKind.Menu"/> entries, the home screen's five app
/// icons as menu entries that navigate, and the page's own controls as items. The keys it gives match the schemes the
/// press runner, the elements builder and the settings matcher already speak (<c>app:</c>, <c>menu:</c>,
/// <c>btn:…|words@x,y</c>), so the memory read drops in where the OCR read was; a control's name in BlueBird's tree is
/// its stable identity, kept as the key of a settings row so a re-read relocates it exactly.
///
/// Settings rows (a grid holding a toggle or slider with its label and value) become one Toggle or Slider item carrying
/// the value the tablet shows and the switch state its value label gives. The current section comes from the content
/// scrollbox's name (<c>callouts_scrollbox</c> → CALLOUTS), which is also the selected side-menu entry; whether the page
/// scrolls comes from the scrollbox holding a taller stackbox. Pure over the walk (<see cref="Read(IReadOnlyList{Bbs757EfbMemory.App})"/>):
/// tested against synthetic trees and live against the running 757.
/// </summary>
public sealed class Bbs757EfbModelReader
{
    private readonly Bbs757EfbMemory _memory;

    public Bbs757EfbModelReader(Bbs757EfbMemory memory) => _memory = memory;

    private const int MenuRight = Bbs757EfbScreen.MenuRight; // controls left of this are the side menu

    /// <summary>The App tokens whose app is a content screen (the reader's class comes from the app's vtable, which
    /// the layout maps to these tokens); "HOME" is the home screen, handled apart.</summary>
    private static readonly HashSet<string> ContentApps = new(StringComparer.Ordinal)
        { "AIRCRAFT", "NAVIGATION", "SYSTEMS", "CHECKLIST", "SETTINGS", "SIGN IN" };

    /// <summary>The current page as the window's model; null when the EFB is not reachable (not located yet).</summary>
    public Bbs757EfbScreenModel? Read() => Read(_memory.ReadFrontApps());

    /// <summary>The page the front apps show, as the window's model; null when none is drawn. Pure, for the tests.</summary>
    public static Bbs757EfbScreenModel? Read(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        if (apps.Count == 0) return null;

        // The content app: the front content app with the most visible controls (the one drawn on top).
        var content = apps
            .Where(a => ContentApps.Contains(a.Class) && Count(a.Controls) > 0)
            .OrderByDescending(a => Count(a.Controls))
            .FirstOrDefault();

        if (content is null)
        {
            var home = apps.FirstOrDefault(a => a.Class == "HOME" && Count(a.Controls) > 0);
            return home is null ? null : HomeModel(home);
        }

        string section = SectionOf(content, content.Class);
        var menu = new List<Bbs757EfbItem>();
        var items = new List<Bbs757EfbItem>();
        EmitChildren(content.Controls, content, section, menu, items);
        return new Bbs757EfbScreenModel(content.Class, section, menu, items) { Scrollable = IsScrollable(content) };
    }

    // ---- the home screen -----------------------------------------------------

    /// <summary>The home screen: the five app icons as menu entries that navigate (<c>app:AIRCRAFT</c>), so a press
    /// takes the tablet into that app.</summary>
    private static Bbs757EfbScreenModel HomeModel(Bbs757EfbMemory.App home)
    {
        var tiles = new List<Bbs757EfbItem>();
        foreach (var n in Flatten(home.Controls))
        {
            if (n.Kind != "button" || IsChrome(n)) continue;
            string app = AppToken(n);
            if (app.Length == 0) continue;
            tiles.Add(new Bbs757EfbItem(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(app), Box(n), Hit(n), true, "app:" + app));
        }
        return new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(), tiles);
    }

    /// <summary>The app a home icon opens, from its control name (<c>aircraft_btn</c> → AIRCRAFT) or its words; "" when
    /// it names none of the apps.</summary>
    private static string AppToken(Bbs757EfbMemory.Node n)
    {
        string fromName = n.Name.EndsWith("_btn", StringComparison.Ordinal)
            ? n.Name[..^4].Replace("_", " ").ToUpperInvariant() : "";
        foreach (var a in Bbs757EfbVocabulary.Apps)
            if (string.Equals(a, fromName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, n.Text, StringComparison.OrdinalIgnoreCase)) return a;
        return "";
    }

    // ---- flattening a content page -------------------------------------------

    private static void Emit(Bbs757EfbMemory.Node n, Bbs757EfbMemory.App content, string section, List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items)
    {
        if (IsChrome(n)) return; // the home button and the page title: the window adds its own Home and shows the page name

        // A settings row: a grid that directly holds a toggle or slider, with its label and value beside it.
        if (n.Kind == "grid" && n.Children.FirstOrDefault(c => c.Kind is "toggle" or "slider") is { } control)
        {
            items.Add(Row(n, control));
            return;
        }

        // A side-menu entry: a control in the left column (an app's section list, the checklist's sections).
        if (IsMenu(n) && n.Kind is "button" or "toggle")
        {
            EmitMenu(n, content.Class, section, menu, items);
            return;
        }

        switch (n.Kind)
        {
            case "button":
                if (n.Text.Length == 0) return; // an icon-only button the reader cannot name
                items.Add(Tile(n));
                return;
            case "toggle":
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Toggle, n.Text, Box(n), Hit(n), true, n.Name, On: StateOf(n.Text), Value: n.Text));
                return;
            case "slider":
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Slider, n.Text, Box(n), Hit(n), true, n.Name, Value: n.Text));
                return;
            case "label":
            case "other":
                if (n.Text.Length > 0 && n.Children.Count == 0 && !IsNoise(n))
                    items.Add(new Bbs757EfbItem(IsHeading(n) ? Bbs757EfbKind.Heading : Bbs757EfbKind.Text,
                        n.Text, Box(n), Hit(n), false, KeyOf(n)));
                EmitChildren(n.Children, content, section, menu, items);
                return;
            default: // grid, colorbox, canvas, gradientbox, stack, image, pan_indicator, scrollbox: containers
                EmitChildren(n.Children, content, section, menu, items);
                return;
        }
    }

    /// <summary>The children of a container in order; the buttons of a tab row (two or more siblings named as
    /// <see cref="Bbs757EfbPageRules.IsTabName"/> says) come out as <see cref="Bbs757EfbKind.Tab"/> items keyed
    /// <c>tab:&lt;parent's name&gt;|&lt;words&gt;</c>, the current one selected.</summary>
    private static void EmitChildren(IReadOnlyList<Bbs757EfbMemory.Node> kids, Bbs757EfbMemory.App content, string section,
        List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items)
    {
        var tabs = kids.Where(k => k.Kind == "button" && k.Text.Length > 0 && Bbs757EfbPageRules.IsTabName(k.Name)).ToList();
        if (tabs.Count < 2)
        {
            foreach (var k in kids) Emit(k, content, section, menu, items);
            return;
        }
        var current = CurrentTab(tabs, content);
        foreach (var k in kids)
        {
            if (!tabs.Contains(k)) { Emit(k, content, section, menu, items); continue; }
            string words = string.Join(' ', k.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Tab, Bbs757EfbVocabulary.Pretty(words), Box(k), Hit(k), true,
                "tab:" + ParentNameOf(content, k) + "|" + words, Selected: ReferenceEquals(k, current), Name: k.Name));
        }
    }

    /// <summary>The control a tab button sits in (the scope of the tab's key), found from the content's tree.</summary>
    private static string ParentNameOf(Bbs757EfbMemory.App content, Bbs757EfbMemory.Node child)
    {
        foreach (var n in Flatten(content.Controls))
            if (n.Children.Any(c => ReferenceEquals(c, child))) return n.Name;
        return "";
    }

    /// <summary>The one tab of a row whose folded words appear in the name of a content panel (<c>*_panel</c>) outside the tab
    /// buttons' own subtrees; null when none, or two or more, match. Until the plan 21 reads BlueBird's selected state.</summary>
    private static Bbs757EfbMemory.Node? CurrentTab(List<Bbs757EfbMemory.Node> tabs, Bbs757EfbMemory.App content)
    {
        var own = new HashSet<Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
        foreach (var t in tabs) { own.Add(t); foreach (var d in Descendants(t)) own.Add(d); }
        // Only a content panel's name counts (maintenance_landing_gear_panel): a text or a canvas/card/grid name can hold a tab's
        // word without being that tab's page ("RANDOM FAILURES: OFF", planning_weather_notam_canvas).
        var panels = Flatten(content.Controls)
            .Where(n => !own.Contains(n) && IsContent(n) && n.Name.EndsWith("_panel", StringComparison.Ordinal))
            .Select(n => n.Name.Split('_', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var hits = tabs.Where(t =>
        {
            var words = t.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => string.Concat(w.Where(char.IsLetterOrDigit))).Where(w => w.Length > 0).ToArray();
            return words.Length > 0 && panels.Any(parts => HasRun(parts, words));
        }).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>Whether the words are a contiguous run of the name's parts, compared case-insensitively ("landing", "gear" in
    /// maintenance_landing_gear_panel); a word never matches inside another part ("apu" is not in "capture").</summary>
    private static bool HasRun(string[] parts, string[] words)
    {
        for (int i = 0; i + words.Length <= parts.Length; i++)
        {
            bool all = true;
            for (int j = 0; j < words.Length && all; j++)
                all = string.Equals(parts[i + j], words[j], StringComparison.OrdinalIgnoreCase);
            if (all) return true;
        }
        return false;
    }

    /// <summary>A left-column control: a side-menu entry (<c>menu:GENERAL</c>, selected when it is the section the page
    /// shows), or the checklist's own RESET ALL button and INTERACTIVE switch, which sit in that column but are not
    /// sections.</summary>
    private static void EmitMenu(Bbs757EfbMemory.Node n, string app, string section, List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items)
    {
        if (n.Kind == "toggle")
        {
            // The checklist's INTERACTIVE switch has no value label; its state is not read from the tree here.
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Toggle, Bbs757EfbVocabulary.Pretty(Clean(n.Text)), Box(n), Hit(n), true,
                "tgl:" + Clean(n.Text)));
            return;
        }
        string word = MenuWord(app, n.Text);
        if (word == "RESET ALL") // the checklist's RESET ALL is a button, not a section
        {
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(word), Box(n), Hit(n), true, "btn:RESET ALL"));
            return;
        }
        bool selected = word.Length > 0 && Fold(word) == Fold(section);
        menu.Add(new Bbs757EfbItem(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(word), Box(n), Hit(n), true, "menu:" + word, Selected: selected));
    }

    /// <summary>A page button or tile: keyed <c>btn:|words@x,y</c> as the OCR reader keyed it, so the press runner reads
    /// its words (<see cref="Bbs757EfbScreen.KeyWords"/>) and a ground-service tile is found (<see cref="Bbs757GroundServices.ForTile"/>).
    /// The words are the control's text without its trailing state word ("GPU REMOVED" → GPU), the same the OCR reader
    /// stripped.</summary>
    private static Bbs757EfbItem Tile(Bbs757EfbMemory.Node n)
    {
        var hit = Hit(n);
        string key = "btn:|" + StripState(n.Text) + "@" + (hit.X / 20).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "," + (hit.Y / 20).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(n.Text), Box(n), hit, true, key);
    }

    /// <summary>A settings row as one item: the control's kind and tap point, the label's words, and the value the
    /// row's value label shows (the toggle/slider has no text of its own). The row's control name is its key, stable
    /// across reads, so a re-read relocates it exactly.</summary>
    private static Bbs757EfbItem Row(Bbs757EfbMemory.Node grid, Bbs757EfbMemory.Node control)
    {
        // The descriptive label and the value label are not always direct children of the row grid (they sit
        // inside a nested layout grid), so search the whole row. The label is the leftmost non-value words.
        var leaves = Descendants(grid)
            .Where(c => c.Kind is "label" or "other" && c.Text.Length > 0 && !IsNoise(c)).ToList();
        string label = leaves
            .Where(c => !IsValue(c) && c.X < 1180)
            .OrderBy(c => c.X).ThenBy(c => c.Y).FirstOrDefault()?.Text ?? grid.Name;
        string value = leaves.FirstOrDefault(IsValue)?.Text ?? "";
        var kind = control.Kind == "toggle" ? Bbs757EfbKind.Toggle : Bbs757EfbKind.Slider;
        return new Bbs757EfbItem(kind, label, Box(control), Hit(control), true, control.Name, On: StateOf(value), Value: value);
    }

    /// <summary>A switch state from the word a value label shows: on for ON, off for OFF, unknown for a word of two
    /// choices (AUTO, a cycle time) that the window reads as the value instead.</summary>
    private static bool? StateOf(string value) =>
        value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? true
        : value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? false : null;

    // ---- the section and whether the page scrolls ----------------------------

    /// <summary>
    /// The section the page shows, as a side-menu word (CALLOUTS, LIVERY OPTIONS): the page rules' table of distinctive names
    /// when it has an entry (<see cref="Bbs757EfbPageRules.SectionFromTable"/>), else the first menu word whose folded letters
    /// equal, or start, the folded name of a content container (<c>planning_perf_calc_canvas</c>, <c>callouts_scrollbox</c>) in
    /// tree order. "" for an app with no sections (Systems, Checklist) or a page that matches none.
    /// </summary>
    private static string SectionOf(Bbs757EfbMemory.App content, string app)
    {
        if (!Bbs757EfbPageRules.HasSections(app) || !Bbs757EfbVocabulary.Menus.TryGetValue(app, out var words)) return "";
        var all = Flatten(content.Controls).ToList();
        if (Bbs757EfbPageRules.SectionFromTable(app, all.Select(n => n.Name)) is { } fromTable) return fromTable;
        foreach (var n in all)
        {
            if (!IsContent(n)) continue; // the shell's and the side menu's own grids (main_grid, control_grid)
            string prefix = Bbs757EfbPageRules.ContainerWords(n.Name);
            if (prefix.Length == 0) continue;
            foreach (var w in words)
            {
                string fw = Fold(w);
                if (fw == prefix || fw.StartsWith(prefix, StringComparison.Ordinal)) return w;
            }
        }
        return "";
    }

    /// <summary>Whether the content scrolls: a <c>*_scrollbox</c> wrapping a taller <c>*_stackbox</c> (its content does
    /// not fit the viewport).</summary>
    private static bool IsScrollable(Bbs757EfbMemory.App content)
    {
        var all = Flatten(content.Controls).ToList();
        var scroll = all.FirstOrDefault(n => n.Name.EndsWith("_scrollbox", StringComparison.Ordinal));
        if (scroll is null) return false;
        var stack = all.FirstOrDefault(n => n.Name.EndsWith("_stackbox", StringComparison.Ordinal));
        return stack is not null && stack.H > scroll.H + 4;
    }

    // ---- helpers -------------------------------------------------------------

    /// <summary>The page chrome the model never lists: the home button (the window adds its own) and the page title
    /// (the window shows the page name).</summary>
    private static bool IsChrome(Bbs757EfbMemory.Node n) =>
        n.Name.EndsWith("home_btn", StringComparison.Ordinal) || n.Name.EndsWith("title_label", StringComparison.Ordinal);

    /// <summary>The app's menu word a side button names: its text cleaned of a status glyph ("__   BEFORE START" →
    /// BEFORE START) and snapped to the menu word whose folded form it matches; the cleaned text when it matches none.</summary>
    private static string MenuWord(string app, string text)
    {
        string clean = Clean(text);
        if (!Bbs757EfbVocabulary.Menus.TryGetValue(app, out var words)) return clean;
        string folded = Fold(clean);
        foreach (var w in words) if (Fold(w) == folded) return w;
        return clean;
    }

    /// <summary>Text without a leading glyph run (the checklist's "__" completion mark) and with runs of spaces as one.</summary>
    private static string Clean(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Any(char.IsLetterOrDigit));
        return string.Join(' ', parts);
    }

    /// <summary>A value's letters and digits, upper-cased invariantly: how a section name and a menu word are compared.</summary>
    private static string Fold(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    /// <summary>A control's text without its trailing state word ("FUEL TRUCK OFF" → FUEL TRUCK), as the OCR reader's
    /// key stripped it, so <see cref="Bbs757EfbScreen.KeyWords"/> gives the same words.</summary>
    private static string StripState(string text)
    {
        foreach (var w in Bbs757EfbScreen.StateWords)
        {
            if (string.Equals(text, w, StringComparison.Ordinal)) return "";
            if (text.EndsWith(" " + w, StringComparison.Ordinal)) return text[..(text.Length - w.Length - 1)];
        }
        return text;
    }

    private static bool IsValue(Bbs757EfbMemory.Node n) =>
        n.Kind is "label" or "other" && (n.Name.EndsWith("_value", StringComparison.Ordinal)
            || n.Name.EndsWith("_value_label", StringComparison.Ordinal) || n.X >= 1180);

    /// <summary>Text that is not part of the page's reading: an image or asset path the tablet keeps in a label
    /// field, or the app's own page-title label. A real label may hold a slash ("AUTOMATIC GPU/JETWAY", "B/CRS"), so
    /// only a leading path marker or an image file extension counts as a path, never a bare slash.</summary>
    private static bool IsNoise(Bbs757EfbMemory.Node n) =>
        n.Name.EndsWith("title_label", StringComparison.Ordinal)
        || n.Text.StartsWith("./", StringComparison.Ordinal)
        || n.Text.StartsWith("../", StringComparison.Ordinal)
        || n.Text.StartsWith("/", StringComparison.Ordinal)
        || n.Text.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every node under the roots, depth first, each root included.</summary>
    private static IEnumerable<Bbs757EfbMemory.Node> Flatten(IReadOnlyList<Bbs757EfbMemory.Node> roots)
    {
        foreach (var r in roots)
        {
            yield return r;
            foreach (var d in Descendants(r)) yield return d;
        }
    }

    /// <summary>Every node under <paramref name="n"/>, depth first; the node itself is not included.</summary>
    private static IEnumerable<Bbs757EfbMemory.Node> Descendants(Bbs757EfbMemory.Node n)
    {
        foreach (var c in n.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static int Count(IReadOnlyList<Bbs757EfbMemory.Node> ns) => ns.Count + ns.Sum(n => Count(n.Children));

    /// <summary>Whether a control is part of the page's content area, which starts at x 250 (the settings scrollbox is drawn
    /// at 250, two pixels left of <see cref="MenuRight"/>): the shell's own grids start at 0.</summary>
    private static bool IsContent(Bbs757EfbMemory.Node n) => n.X >= MenuRight - 10;
    private static bool IsMenu(Bbs757EfbMemory.Node n) => n.X is >= 0 and < MenuRight && n.W <= MenuRight;
    private static bool IsHeading(Bbs757EfbMemory.Node n) => n.Text == n.Text.ToUpperInvariant() && n.W > 300;
    private static string KeyOf(Bbs757EfbMemory.Node n) => n.Name.Length > 0 ? n.Name : "text|" + n.Text;
    private static Rectangle Box(Bbs757EfbMemory.Node n) => new((int)n.X, (int)n.Y, (int)n.W, (int)n.H);
    private static Point Hit(Bbs757EfbMemory.Node n) => new((int)(n.X + n.W / 2), (int)(n.Y + n.H / 2));
}
