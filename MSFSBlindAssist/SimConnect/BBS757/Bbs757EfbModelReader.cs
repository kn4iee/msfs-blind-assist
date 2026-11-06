using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Turns the EFB memory walk (<see cref="Bbs757EfbMemory"/>) into the window's model (<see cref="Bbs757EfbScreenModel"/>),
/// the reader of the tablet (<see cref="Bbs757EfbScreen"/> keeps its shared key helpers). The front content app's page becomes the model:
/// its side menu (<c>GENERAL</c>, <c>FUEL</c>, …) as <see cref="Bbs757EfbKind.Menu"/> entries, the home screen's five app
/// icons as menu entries that navigate, and the page's own controls as items. The keys it gives match the schemes the
/// press runner, the elements builder and the settings matcher already speak (<c>app:</c>, <c>menu:</c>,
/// <c>btn:…|words@x,y</c>), so the press runner reads them as they are; a control's name in BlueBird's tree is
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
            return home is null ? null : HomeModel(home) with { Keyboard = KeyboardOf(apps) };
        }

        string section = SectionOf(content, content.Class);
        var menu = new List<Bbs757EfbItem>();
        var items = new List<Bbs757EfbItem>();
        var grammar = new Grammar(content);
        EmitChildren(content.Controls, content, grammar, section, menu, items, Bbs757EfbScreen.ContentArea);
        var scroller = PageScrollbox(content);
        return new Bbs757EfbScreenModel(content.Class, section, menu, Bbs757EfbPageRules.Reorder(content.Class, section, items))
        {
            Scrollable = scroller is not null,
            Viewport = scroller is null ? Bbs757EfbScreen.ContentArea : Within(Box(scroller), Bbs757EfbScreen.ContentArea),
            Keyboard = KeyboardOf(apps),
        };
    }

    /// <summary>BlueBird's keyboard from the shell app's <c>kb_grid</c>: its box and its keys in tree order. A <c>char_btn_</c> key
    /// (and the comma and period keys, whose words are <c>,|,|!</c> and <c>.|.|?</c>) carries three layers split on <c>|</c>; a
    /// <c>btn_*</c> command key keeps its name and empty layers. Null without a <c>kb_grid</c>. The grid reads visible whether the
    /// keyboard shows or not, so this says where the keys are, never that the keyboard is up.</summary>
    private static Bbs757EfbKeyboard? KeyboardOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        var grid = apps.Where(a => a.Class == "app").SelectMany(a => Flatten(a.Controls)).FirstOrDefault(n => n.Name == "kb_grid");
        if (grid is null) return null;
        var keys = new List<Bbs757EfbKey>();
        foreach (var k in Descendants(grid).Where(n => n.Kind == "button"))
        {
            string[] layers = k.Name.StartsWith("char_btn_", StringComparison.Ordinal) ? k.Name["char_btn_".Length..].Split('|') : k.Text.Split('|');
            keys.Add(layers.Length == 3
                ? new Bbs757EfbKey(k.Name, layers[0], layers[1], layers[2], Hit(k))
                : new Bbs757EfbKey(k.Name, "", "", "", Hit(k)));
        }
        return new Bbs757EfbKeyboard(Box(grid), keys);
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

    /// <summary>One node and what is under it, as items; each item the node itself makes is clipped to <paramref name="clip"/>, the
    /// drawn rectangle of the scrollbox it sits in (the content area in none), and a scrollbox's own descendants to that scrollbox.</summary>
    private static void Emit(Bbs757EfbMemory.Node n, Bbs757EfbMemory.App content, Grammar g, string section, List<Bbs757EfbItem> menu,
        List<Bbs757EfbItem> items, Rectangle clip)
    {
        int first = items.Count;
        EmitNode(n, content, g, section, menu, items, IsScrollbox(n) ? Within(Box(n), clip) : clip);
        Clip(items, first, clip);
    }

    private static void EmitNode(Bbs757EfbMemory.Node n, Bbs757EfbMemory.App content, Grammar g, string section, List<Bbs757EfbItem> menu,
        List<Bbs757EfbItem> items, Rectangle inner)
    {
        if (IsChrome(n)) return; // the home button and the page title: the window adds its own Home and shows the page name

        // A settings row: a grid that directly holds a toggle or slider, with its label and value beside it.
        if (n.Kind == "grid" && n.Children.FirstOrDefault(c => c.Kind is "toggle" or "slider") is { } control)
        {
            items.Add(Row(n, control));
            return;
        }

        // A side-menu entry: a control in the left column (an app's section list, the checklist's sections).
        if (IsMenu(n, content.Class) && n.Kind is "button" or "toggle")
        {
            EmitMenu(n, content.Class, section, menu, items);
            return;
        }

        switch (n.Kind)
        {
            case "button":
                if (g.FieldButtons.Contains(n)) { items.Add(g.FieldItem(n)); return; } // a value box: its number is the field's value
                if (g.ButtonItem(n) is { } button) items.Add(button);
                return;
            case "toggle":
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Toggle, n.Text, Box(n), Hit(n), true, n.Name, On: StateOf(n.Text), Value: n.Text));
                return;
            case "slider":
                items.Add(g.SliderItem(n));
                return;
            case "label":
            case "other":
                if (n.Text.Length > 0 && n.Children.Count == 0) EmitText(n, g, items);
                EmitChildren(n.Children, content, g, section, menu, items, inner);
                return;
            default: // grid, colorbox, canvas, gradientbox, stack, image, pan_indicator, scrollbox: containers
                EmitChildren(n.Children, content, g, section, menu, items, inner);
                return;
        }
    }

    /// <summary>The items from <paramref name="first"/> on that have no clip yet get <paramref name="clip"/>.</summary>
    private static void Clip(List<Bbs757EfbItem> items, int first, Rectangle clip)
    {
        for (int i = first; i < items.Count; i++)
            if (items[i].Clip == default) items[i] = items[i] with { Clip = clip };
    }

    /// <summary>A scrollbox's drawn rectangle kept within the scrollbox around it (top and bottom only: pages scroll up and down), so a
    /// control in a scrollbox scrolled out of another is never shown, nor tapped, outside either.</summary>
    private static Rectangle Within(Rectangle box, Rectangle outer)
    {
        int top = Math.Max(box.Top, outer.Top), bottom = Math.Min(box.Bottom, outer.Bottom);
        return new Rectangle(box.X, top, box.Width, Math.Max(0, bottom - top));
    }

    /// <summary>A scrollbox: BlueBird's scrollbox class, or a control named <c>*_scrollbox</c> (the committed trees predate the class).</summary>
    private static bool IsScrollbox(Bbs757EfbMemory.Node n) =>
        n.Kind == "scrollbox" || n.Name.EndsWith("_scrollbox", StringComparison.Ordinal);

    /// <summary>The children of a container in order; the buttons of a tab row (two or more siblings named as
    /// <see cref="Bbs757EfbPageRules.IsTabName"/> says) come out as <see cref="Bbs757EfbKind.Tab"/> items keyed
    /// <c>tab:&lt;parent's name&gt;|&lt;words&gt;</c>, the current one selected. Every item is clipped to <paramref name="clip"/>, the
    /// scrollbox the children sit in.</summary>
    private static void EmitChildren(IReadOnlyList<Bbs757EfbMemory.Node> kids, Bbs757EfbMemory.App content, Grammar g, string section,
        List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items, Rectangle clip)
    {
        var tabs = kids.Where(k => k.Kind == "button" && k.Text.Length > 0 && Bbs757EfbPageRules.IsTabName(k.Name)).ToList();
        if (tabs.Count < 2)
        {
            foreach (var k in kids) Emit(k, content, g, section, menu, items, clip);
            return;
        }
        var current = CurrentTab(tabs, content);
        foreach (var k in kids)
        {
            if (!tabs.Contains(k)) { Emit(k, content, g, section, menu, items, clip); continue; }
            string words = string.Join(' ', k.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Tab, Bbs757EfbVocabulary.Pretty(words), Box(k), Hit(k), true,
                "tab:" + ParentNameOf(content, k) + "|" + words, Selected: ReferenceEquals(k, current), Name: k.Name, Clip: clip));
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

    /// <summary>A page button or tile: keyed <c>btn:|words@x,y</c> so the press runner reads
    /// its words (<see cref="Bbs757EfbScreen.KeyWords"/>) and a ground-service tile is found (<see cref="Bbs757GroundServices.ForTile"/>).
    /// The words are the control's text without its trailing state word ("GPU REMOVED" → GPU).</summary>
    private static Bbs757EfbItem Tile(Bbs757EfbMemory.Node n, string? pictureState, string? text = null, string label = "")
    {
        text ??= n.Text;
        var hit = Hit(n);
        // The key comes from the button's own words, so a state the tile's picture shows (CHOCKS, then "IN PLACE" or "REMOVED") never changes it.
        string key = "btn:|" + StripState(text) + "@" + (hit.X / 20).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "," + (hit.Y / 20).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string words = pictureState is null ? text : text + " " + pictureState;
        return new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(words), Box(n), hit, true, key, Label: label, Name: n.Name);
    }

    /// <summary>A label's words as the page grammar reads them (<see cref="Grammar"/>): a line with its value (and unit), a heading,
    /// or plain text; nothing for a path, a separator or a label another line took.</summary>
    private static void EmitText(Bbs757EfbMemory.Node n, Grammar g, List<Bbs757EfbItem> items)
    {
        if (IsNoise(n) || g.Taken.Contains(n)) return;
        if (g.RowValues.TryGetValue(n, out var rowValues)) // an index row's name: one line per other label of the row
        {
            foreach (var v in rowValues)
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Row, n.Text, Box(n), Hit(n), false, rowValues.Count == 1 ? KeyOf(n) : KeyOf(v),
                    Value: v.Text, Name: n.Name));
            return;
        }
        if (g.InputOf.TryGetValue(n, out var input)) // a title naming an input the reader cannot read: the input, named by the title
        {
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Field, n.Text, Box(input), Hit(input), true, input.Name, Name: input.Name));
            return;
        }
        if (g.ValueOf.TryGetValue(n, out var value))
        {
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Row, n.Text, Box(n), Hit(n), false, KeyOf(n),
                Value: g.WithUnit(value), Name: n.Name));
            return;
        }
        if (!n.Text.Any(char.IsLetterOrDigit)) return; // a separator ("|"), or marks with no word in them
        if (g.LevelOf.TryGetValue(n, out int level))
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Heading, n.Text, Box(n), Hit(n), false, KeyOf(n), Level: level, Name: n.Name));
        else if (n.H > LongTextHeight || n.Text.Contains('\n'))
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.LongText, n.Text, Box(n), Hit(n), false, KeyOf(n), Name: n.Name));
        else
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Text, g.WithUnit(n), Box(n), Hit(n), false, KeyOf(n), Name: n.Name));
    }

    /// <summary>A text taller than this (or holding a line break) is a long text, read whole (the route, the VNAV note).</summary>
    private const int LongTextHeight = 60;

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
        return new Bbs757EfbItem(kind, label, Box(control), Hit(control), true, control.Name, On: StateOf(value), Value: value,
            Slider: kind == Bbs757EfbKind.Slider ? SliderOf(control, value) : null);
    }

    /// <summary>A slider's track from its rectangle, left to right, or bottom to top for a box taller than wide (the hydraulic reservoirs,
    /// Systems' zoom; the direction is provisional until measured), and its knob: at the part of the track its value reads as (a percent,
    /// "70%", or a count of its most, "16/16"), else at the start (the choices of a settings slider are told by the catalog, not by the
    /// knob; a slider whose value shows nowhere keeps R8's start).</summary>
    private static Bbs757EfbSlider SliderOf(Bbs757EfbMemory.Node slider, string value)
    {
        bool vertical = slider.H > slider.W;
        int start, end;
        if (vertical)
        {
            int top = (int)slider.Y;
            (start, end) = (top + (int)slider.H, top);
        }
        else
        {
            int left = (int)slider.X;
            (start, end) = (left, left + (int)slider.W);
        }
        var track = new Bbs757EfbSlider(start, end, start) { Vertical = vertical, AcrossX = vertical ? (int)(slider.X + slider.W / 2) : 0 };
        // Only a value that is a percent or a count places the knob: a bare number on a settings slider is one of its choices.
        double? shown = value.EndsWith('%') || value.Contains('/') ? Bbs757EfbSlider.PercentShown(value) : null;
        return shown is double percent ? track with { KnobX = track.At(percent) } : track;
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

    /// <summary>The one scrollbox of the content that scrolls: the first that wraps a taller <c>*_stackbox</c> (its content does not
    /// fit it; Settings' pages). Null when none does: an inner scrollbox holding a text (the ACARS route, Weather's report, the
    /// checklist's lines) does not make the page scroll, and the controls around it are clipped to the content area, not to it.</summary>
    private static Bbs757EfbMemory.Node? PageScrollbox(Bbs757EfbMemory.App content) =>
        Flatten(content.Controls).FirstOrDefault(n => IsScrollbox(n)
            && Descendants(n).Any(d => d.Name.EndsWith("_stackbox", StringComparison.Ordinal) && d.H > n.H + 4));

    // ---- helpers -------------------------------------------------------------

    /// <summary>The page chrome the model never lists: the home button (the window adds its own) and the page title
    /// (the window shows the page name).</summary>
    private static bool IsChrome(Bbs757EfbMemory.Node n) =>
        n.Name.EndsWith("home_btn", StringComparison.Ordinal) || IsPageTitle(n);

    /// <summary>The app's own page title: <c>page_title_label</c>, and the Checklist's <c>checklist_title_label</c> in the same place.
    /// Every other <c>*_title_label</c> is a title of the page's own.</summary>
    private static bool IsPageTitle(Bbs757EfbMemory.Node n) => n.Name is "page_title_label" or "checklist_title_label";

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

    /// <summary>A control's text without its trailing state word ("FUEL TRUCK OFF" → FUEL TRUCK), so <see cref="Bbs757EfbScreen.KeyWords"/> gives the same words.</summary>
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
    /// only a leading path marker or an image file extension counts as a path, never a bare slash. A label of marks alone
    /// ("|") is a separator, dropped where it stands alone (<see cref="EmitText"/>); a value of marks ("---") still reads in its line.</summary>
    private static bool IsNoise(Bbs757EfbMemory.Node n) =>
        IsPageTitle(n)
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
    /// <summary>A side-menu entry: a control in the left column of an app that has a side menu (the apps with sections, and the
    /// checklist's section list). In any other app (Systems' zoom toolbar at x 16) a control left of the content is content.</summary>
    private static bool IsMenu(Bbs757EfbMemory.Node n, string app) =>
        (Bbs757EfbPageRules.HasSections(app) || app == "CHECKLIST") && n.X is >= 0 and < MenuRight && n.W <= MenuRight;
    private static string KeyOf(Bbs757EfbMemory.Node n) => n.Name.Length > 0 ? n.Name : "text|" + n.Text;
    private static Rectangle Box(Bbs757EfbMemory.Node n) => new((int)n.X, (int)n.Y, (int)n.W, (int)n.H);
    private static Point Hit(Bbs757EfbMemory.Node n) => new((int)(n.X + n.W / 2), (int)(n.Y + n.H / 2));

    // ---- the page grammar: lines, headings and pictures from BlueBird's names ---

    /// <summary>
    /// What BlueBird's control names say about one page (plan 20, R3, R4 and R10), worked out once before the page is emitted.
    /// A label and a value whose names are alike once their role words are taken out (<see cref="Bbs757EfbPageRules.ReadName"/>)
    /// are one line: the label emits as a <see cref="Bbs757EfbKind.Row"/> and the value is <see cref="Taken"/>. A unit beside a value
    /// (or beside a bare number whose name is the unit's without <c>unit</c>) joins its words. A title with no value is a heading, level 2
    /// or, inside a card already titled, level 3. A picture that shows a state (<see cref="Bbs757EfbPageRules.PictureState"/>) gives it to the
    /// button in its box. Only <c>label</c> controls pair: a long text (<c>other</c>, such as the route) stays a text of its own.
    /// </summary>
    private sealed class Grammar
    {
        private static readonly HashSet<string> GenericParts = new(StringComparer.Ordinal)
            { "btn", "button", "toggle", "status", "image", "img", "service", "box", "label", "bg", "icon", "state" };

        private readonly Dictionary<Bbs757EfbMemory.Node, string> _unitOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, string> _pictureOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, string> _labelOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> _rowNameOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, (string Text, string Value)> _sliderOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> _fieldLabel = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Bbs757EfbMemory.Node> _fieldLabels = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Bbs757EfbMemory.Node> _rowNames = new(ReferenceEqualityComparer.Instance);

        /// <summary>For a label that pairs, its value.</summary>
        public Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> ValueOf { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The labels another item already holds: the values that paired, the units that joined, the labels that name a control.</summary>
        public HashSet<Bbs757EfbMemory.Node> Taken { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For a title that is a heading, its level (2 or 3).</summary>
        public Dictionary<Bbs757EfbMemory.Node, int> LevelOf { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For the name label of an index row (R6) with other labels, those labels: the row's state words.</summary>
        public Dictionary<Bbs757EfbMemory.Node, List<Bbs757EfbMemory.Node>> RowValues { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For a title that names an input the reader cannot read, that input.</summary>
        public Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> InputOf { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The word-less buttons holding exactly one label: value boxes, read as fields (R7).</summary>
        public HashSet<Bbs757EfbMemory.Node> FieldButtons { get; } = new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<Bbs757EfbMemory.Node, Bbs757EfbPageRules.NameInfo> _info = new(ReferenceEqualityComparer.Instance);

        private readonly string _app;

        public Grammar(Bbs757EfbMemory.App content)
        {
            _app = content.Class;
            var all = Flatten(content.Controls).ToList();
            var parentOf = new Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
            foreach (var n in all) foreach (var c in n.Children) parentOf[c] = n;

            foreach (var b in all)
                if (b.Kind == "button" && b.Text.Length == 0 && !IsChrome(b) && Descendants(b).Count(IsReadable) == 1)
                {
                    FieldButtons.Add(b);
                    var inner = Descendants(b).First(IsReadable);
                    _fieldLabel[b] = inner;
                    _fieldLabels.Add(inner);
                }

            // Only a label the page emits can pair or take a unit: the reader does not open a button with words, a switch, a slider or a
            // settings row. A value box (a word-less button holding one label) is opened for its number and its unit.
            var labels = Reachable(content.Controls).Where(IsReadable).ToList();
            foreach (var l in labels) _info[l] = Bbs757EfbPageRules.ReadName(l.Name);
            var free = labels.Where(l => !_fieldLabels.Contains(l)).ToList();

            _needsLabel.AddRange(Reachable(content.Controls).Where(n => n.Kind == "slider"
                || (n.Kind == "button" && !IsMenu(n, content.Class) && (FieldButtons.Contains(n) || Bbs757EfbPageRules.IsStateOrAction(n.Text)))));
            Rows(all, free);
            Sliders(content.Controls, free);
            Pair(free);
            JoinUnits(labels);
            LabelControls(content.Controls, free);
            ColonLabels(free, parentOf);
            Inputs(all, free, parentOf);
            Title(content.Controls, titledAbove: false);
            Pictures(all);
        }

        private static bool IsReadable(Bbs757EfbMemory.Node n) =>
            n.Kind == "label" && n.Text.Length > 0 && n.Children.Count == 0 && !IsNoise(n);

        /// <summary>The nodes <see cref="Emit"/> walks into: not the inside of a button with words, a switch, a slider or a settings row.</summary>
        private static IEnumerable<Bbs757EfbMemory.Node> Reachable(IReadOnlyList<Bbs757EfbMemory.Node> kids)
        {
            foreach (var n in kids)
            {
                if (IsChrome(n)) continue;
                yield return n;
                if (n.Kind is "toggle" or "slider" || (n.Kind == "button" && n.Text.Length > 0)) continue;
                if (n.Kind == "grid" && n.Children.Any(c => c.Kind is "toggle" or "slider")) continue;
                foreach (var d in Reachable(n.Children)) yield return d;
            }
        }

        /// <summary>A line's value with its unit ("1539 Gal"), or the label's own words when it has no unit.</summary>
        public string WithUnit(Bbs757EfbMemory.Node n) => _unitOf.TryGetValue(n, out var unit) ? n.Text + " " + unit : n.Text;

        public string? PictureStateOf(Bbs757EfbMemory.Node button) => _pictureOf.GetValueOrDefault(button);

        // ---- R6: index rows -----------------------------------------------------

        /// <summary>Siblings whose names end in the same <c>_N</c> and share the stem before their last part (<c>failure_name_0</c>,
        /// <c>failure_state_0</c>, <c>failure_action_0</c>) are one row when they hold a control and a label: the <c>*_name_N</c> label
        /// (else the first label) names every control, and is the line of a row whose value is each other label.</summary>
        private void Rows(List<Bbs757EfbMemory.Node> all, List<Bbs757EfbMemory.Node> labels)
        {
            var labelSet = new HashSet<Bbs757EfbMemory.Node>(labels, ReferenceEqualityComparer.Instance);
            foreach (var parent in all)
            {
                var groups = parent.Children
                    .Select(k => (Node: k, Parts: Bbs757EfbPageRules.NameParts(k.Name)))
                    .Where(x => x.Parts.Length >= 3 && x.Parts[^1].All(char.IsAsciiDigit))
                    .GroupBy(x => string.Join('_', x.Parts[..^2]) + "#" + x.Parts[^1]);
                foreach (var group in groups)
                {
                    var controls = group.Select(x => x.Node).Where(k => k.Kind is "button" or "slider" or "toggle").ToList();
                    var rowLabels = group.Select(x => x.Node).Where(labelSet.Contains).ToList();
                    if (controls.Count == 0 || rowLabels.Count == 0) continue;
                    var name = rowLabels.FirstOrDefault(l => Bbs757EfbPageRules.NameParts(l.Name).Contains("name")) ?? rowLabels[0];
                    _rowNames.Add(name);
                    foreach (var c in controls) _rowNameOf[c] = name;
                    var others = rowLabels.Where(l => !ReferenceEquals(l, name)).ToList();
                    if (others.Count == 0) continue;
                    RowValues[name] = others;
                    foreach (var o in others) Taken.Add(o);
                }
            }
        }

        // ---- R5: the label that names a control -----------------------------------

        /// <summary>The label that names a control whose own words are none, a state or an action, in order: the one label whose name,
        /// once role words and control words are taken out, equals the control's; the name of the control's index row; the one label
        /// touching the control's rectangle. Null when none, or when two touch.</summary>
        private Bbs757EfbMemory.Node? NamingLabel(Bbs757EfbMemory.Node control, IEnumerable<Bbs757EfbMemory.Node> pool)
        {
            var candidates = pool.Where(l => !Taken.Contains(l) || RowValues.ContainsKey(l)).ToList();
            string key = Bbs757EfbPageRules.ControlKey(control.Name);
            if (key.Length > 0)
            {
                var same = candidates.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Label && !ValueOf.ContainsKey(l)
                    && Bbs757EfbPageRules.ControlKey(_info[l]) == key).ToList();
                if (same.Count == 1) return same[0];
            }
            if (_rowNameOf.TryGetValue(control, out var row)) return row;
            var touching = candidates.Where(l => _info[l].Role is Bbs757EfbPageRules.NameRole.None or Bbs757EfbPageRules.NameRole.Label
                && !_info[l].IsTitle && !ValueOf.ContainsKey(l) && TouchGap(control, l) is { } own
                && !_needsLabel.Any(o => !ReferenceEquals(o, control) && TouchGap(o, l) is { } other && other < own)).ToList();
            return touching.Count == 1 ? touching[0] : null;
        }

        /// <summary>The controls that wait for a name: a label touching two of them belongs to the nearer one.</summary>
        private readonly List<Bbs757EfbMemory.Node> _needsLabel = new();

        /// <summary>How far a label lies below, above or left of a control (at most 12 px, overlapping it in the other axis); null when it
        /// does not touch it.</summary>
        private static float? TouchGap(Bbs757EfbMemory.Node c, Bbs757EfbMemory.Node l)
        {
            const float gap = 12, slack = -0.5f;
            bool overlapX = l.X < c.X + c.W && l.X + l.W > c.X;
            bool overlapY = l.Y < c.Y + c.H && l.Y + l.H > c.Y;
            float below = l.Y - (c.Y + c.H), above = c.Y - (l.Y + l.H), left = c.X - (l.X + l.W);
            float? best = null;
            void Take(bool fits, float d) { if (fits && d >= slack && d <= gap && (best is null || d < best)) best = d; }
            Take(overlapX, below);
            Take(overlapX, above);
            Take(overlapY, left);
            return best;
        }

        /// <summary>A label that names a control is not a line of its own (unless it is a title, which stays a heading, or an index row's name).</summary>
        private void Use(Bbs757EfbMemory.Node label)
        {
            if (!_info[label].IsTitle && !RowValues.ContainsKey(label)) Taken.Add(label);
        }

        private void LabelControls(IReadOnlyList<Bbs757EfbMemory.Node> roots, List<Bbs757EfbMemory.Node> free)
        {
            var pool = free.Where(l => !Taken.Contains(l) && !ValueOf.ContainsKey(l) && _info[l].Role != Bbs757EfbPageRules.NameRole.Unit).ToList();
            foreach (var b in Reachable(roots).Where(n => n.Kind == "button" && !IsMenu(n, _app)))
            {
                if (!FieldButtons.Contains(b) && !Bbs757EfbPageRules.IsStateOrAction(b.Text)) continue;
                if (NamingLabel(b, pool) is not { } label) continue;
                _labelOf[b] = label.Text;
                Use(label);
            }
        }

        /// <summary>A button as an item: its own words, with the label that names it when its words are a state or an action; a button
        /// with no words is named by its label, else by its control name (R11), else left out (null).</summary>
        public Bbs757EfbItem? ButtonItem(Bbs757EfbMemory.Node n)
        {
            _labelOf.TryGetValue(n, out var label);
            // A button whose text is only marks ("+", "-") has no words either.
            bool wordless = !n.Text.Any(char.IsLetterOrDigit);
            if (wordless)
            {
                // A background box of the page (chocks_service_box) is no control the pilot presses.
                if (n.Text.Length == 0 && Bbs757EfbPageRules.NameParts(n.Name).Contains("box")) return null;
                string? name = label ?? Bbs757EfbPageRules.NameFromParts(n.Name);
                if (name is not null) return Tile(n, null, name);
                return n.Text.Length == 0 ? null : Tile(n, PictureStateOf(n), label: label ?? "");
            }
            return Tile(n, PictureStateOf(n), label: label ?? "");
        }

        /// <summary>A value box as a field: named by the page rules' table, else its label, else its control name; its value is the
        /// number it holds with its unit.</summary>
        public Bbs757EfbItem FieldItem(Bbs757EfbMemory.Node box)
        {
            var inner = _fieldLabel[box];
            _labelOf.TryGetValue(box, out var label);
            string name = Bbs757EfbPageRules.FieldName(box.Name) ?? label ?? Bbs757EfbPageRules.NameFromParts(box.Name) ?? box.Name;
            return new Bbs757EfbItem(Bbs757EfbKind.Field, name, Box(box), Hit(box), true, box.Name, Value: WithUnit(inner), Name: box.Name);
        }

        // ---- R8: sliders ----------------------------------------------------------

        private void Sliders(IReadOnlyList<Bbs757EfbMemory.Node> roots, List<Bbs757EfbMemory.Node> free)
        {
            foreach (var s in Reachable(roots).Where(n => n.Kind == "slider"))
            {
                var label = NamingLabel(s, free);
                string key = Bbs757EfbPageRules.ControlKey(s.Name);
                var value = key.Length == 0 ? null : free.FirstOrDefault(l => !Taken.Contains(l) && _info[l].Role == Bbs757EfbPageRules.NameRole.Value
                    && _info[l].CoreKey == key);
                if (value is null && Bbs757EfbPageRules.SliderValueName(s.Name) is { } valueName)
                    value = free.FirstOrDefault(l => l.Name == valueName && !Taken.Contains(l));
                if (value is not null) Taken.Add(value);
                if (label is not null)
                {
                    Use(label);
                    // A title that names a slider is the slider's name, not a heading too: the slider says it (Weight & balance).
                    if (_info[label].IsTitle) Taken.Add(label);
                }
                string own = string.Join(' ', Bbs757EfbPageRules.NameParts(s.Name).Where(p => !Bbs757EfbPageRules.ControlWords.Contains(p))).ToUpperInvariant();
                string text = label?.Text ?? (own.Length > 0 ? own : s.Name), shown = value?.Text ?? "";
                // A title that also shows the slider's count ("BUSINESS PAX - 16/16") gives the slider its name and its value: the
                // count is where its knob is.
                if (shown.Length == 0 && label is not null && TitleCount.Match(label.Text) is { Success: true } count)
                    (text, shown) = (count.Groups[1].Value, count.Groups[2].Value);
                _sliderOf[s] = (text, shown);
            }
        }

        /// <summary>A title and the count it shows after a dash: "BUSINESS PAX - 16/16" is BUSINESS PAX, 16/16.</summary>
        private static readonly System.Text.RegularExpressions.Regex TitleCount =
            new(@"^(.*\S)\s+-\s+(\d+\s*/\s*\d+)\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>A slider outside a settings row as an item: named by R5, valued by its partner, its track from its rectangle.</summary>
        public Bbs757EfbItem SliderItem(Bbs757EfbMemory.Node s)
        {
            var (text, value) = _sliderOf.GetValueOrDefault(s, (s.Name, ""));
            return new Bbs757EfbItem(Bbs757EfbKind.Slider, text, Box(s), Hit(s), true, s.Name, Value: value, Slider: SliderOf(s, value), Name: s.Name);
        }

        // ---- R4: lines and units ----------------------------------------------------

        private void Pair(List<Bbs757EfbMemory.Node> labels)
        {
            var open = labels.Where(l => !Taken.Contains(l) && !_rowNames.Contains(l)).ToList();
            var waiting = new Dictionary<string, Queue<Bbs757EfbMemory.Node>>(StringComparer.Ordinal);
            foreach (var v in open.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Value && _info[l].Core.Count > 0))
            {
                if (!waiting.TryGetValue(_info[v].CoreKey, out var q)) waiting[_info[v].CoreKey] = q = new Queue<Bbs757EfbMemory.Node>();
                q.Enqueue(v);
            }
            foreach (var l in open.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Label && _info[l].Core.Count > 0))
            {
                if (!waiting.TryGetValue(_info[l].CoreKey, out var q) || q.Count == 0) continue;
                var v = q.Dequeue();
                ValueOf[l] = v;
                Taken.Add(v);
            }
        }

        /// <summary>A unit joins the value (or the bare number) whose core is its own, with or without <c>qty</c>; a unit named for a
        /// group alone (<c>wb_pax_business_unit</c>) joins the number of that group (<c>wb_pax_business_weight</c>).</summary>
        private void JoinUnits(List<Bbs757EfbMemory.Node> labels)
        {
            foreach (var unit in labels.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Unit))
            {
                var want = new[] { _info[unit].CoreKey, string.Join(' ', _info[unit].Core.Where(p => p != "qty").OrderBy(p => p, StringComparer.Ordinal)) }
                    .Where(k => k.Length > 0).Distinct().ToList();
                bool plain = !_info[unit].Core.Contains("qty") && !_info[unit].Core.Contains("weight");
                string Bare(Bbs757EfbMemory.Node l) =>
                    string.Join(' ', _info[l].Core.Where(p => p is not ("qty" or "weight")).OrderBy(p => p, StringComparer.Ordinal));
                var partner = labels.FirstOrDefault(l => !ReferenceEquals(l, unit) && !_unitOf.ContainsKey(l)
                    && _info[l].Role is Bbs757EfbPageRules.NameRole.Value or Bbs757EfbPageRules.NameRole.None
                    && (want.Contains(_info[l].CoreKey) || (plain && want.Contains(Bare(l)))));
                if (partner is null) continue;
                _unitOf[partner] = unit.Text;
                Taken.Add(unit);
            }
        }

        // ---- a title ending in a colon ----------------------------------------------------

        /// <summary>A title whose text ends in ":" is a label, never a heading: it names the one value or control beside it. Beside
        /// means a sibling in its parent; when a sibling box (<c>*_box</c>) holds the title, only the siblings inside that box. One
        /// other readable label there is its value (the pair is a line); one button with words there takes the title as its label.
        /// With none or several the title stays what it was.</summary>
        private void ColonLabels(List<Bbs757EfbMemory.Node> free, Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> parentOf)
        {
            foreach (var title in free.Where(l => _info[l].IsTitle && l.Text.TrimEnd().EndsWith(':') && !Taken.Contains(l)
                && !ValueOf.ContainsKey(l) && !RowValues.ContainsKey(l)).ToList())
            {
                if (!parentOf.TryGetValue(title, out var parent)) continue;
                var siblings = parent.Children.Where(s => !ReferenceEquals(s, title)).ToList();
                var box = siblings.FirstOrDefault(s => s.Text.Length == 0 && s.Children.Count == 0
                    && Bbs757EfbPageRules.NameParts(s.Name).Contains("box") && Inside(title, s));
                var beside = box is null ? siblings : siblings.Where(s => !ReferenceEquals(s, box) && Inside(s, box)).ToList();
                var values = beside.Where(s => IsReadable(s) && !Taken.Contains(s) && !ValueOf.ContainsKey(s)).ToList();
                var controls = beside.Where(s => s.Kind == "button" && s.Text.Any(char.IsLetterOrDigit)).ToList();
                if (controls.Count == 1 && values.Count == 0 && !_labelOf.ContainsKey(controls[0]))
                {
                    _labelOf[controls[0]] = title.Text;
                    Taken.Add(title);
                }
                else if (values.Count == 1 && controls.Count == 0)
                {
                    ValueOf[title] = values[0];
                    Taken.Add(values[0]);
                }
            }
        }

        /// <summary>Whether the middle of <paramref name="n"/> lies inside <paramref name="box"/>.</summary>
        private static bool Inside(Bbs757EfbMemory.Node n, Bbs757EfbMemory.Node box)
        {
            float cx = n.X + n.W / 2, cy = n.Y + n.H / 2;
            return cx >= box.X && cx <= box.X + box.W && cy >= box.Y && cy <= box.Y + box.H;
        }

        // ---- inputs the reader cannot read ----------------------------------------------

        /// <summary>A title that names an input (<c>planning_perf_calc_flap_field_title</c> and <c>..._flap_dropdown</c>, else the one
        /// unread input beside it in its container) is that input's name: it is no heading, and the input is a field with no value
        /// read. Plan 21 reads the value.</summary>
        private void Inputs(List<Bbs757EfbMemory.Node> all, List<Bbs757EfbMemory.Node> free, Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> parentOf)
        {
            var inputs = all.Where(n => n.Kind == "other" && n.Text.Length == 0 && Bbs757EfbPageRules.IsUnreadInputName(n.Name)).ToList();
            if (inputs.Count == 0) return;
            var used = new HashSet<Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
            var titles = free.Where(l => _info[l].IsTitle && !Taken.Contains(l) && !ValueOf.ContainsKey(l) && l.Text.Any(char.IsLetterOrDigit)).ToList();
            foreach (var t in titles)
            {
                string key = Bbs757EfbPageRules.ControlKey(_info[t]);
                var hit = key.Length == 0 ? null : inputs.FirstOrDefault(i => !used.Contains(i) && Bbs757EfbPageRules.ControlKey(i.Name) == key);
                if (hit is null) continue;
                InputOf[t] = hit;
                used.Add(hit);
            }
            foreach (var t in titles.Where(t => !InputOf.ContainsKey(t)))
            {
                if (!parentOf.TryGetValue(t, out var parent)) continue;
                var beside = parent.Children.Where(i => inputs.Contains(i) && !used.Contains(i)).ToList();
                if (beside.Count != 1) continue;
                InputOf[t] = beside[0];
                used.Add(beside[0]);
            }
        }

        private bool IsHeadingTitle(Bbs757EfbMemory.Node n) =>
            _info.TryGetValue(n, out var info) && (info.IsTitle || Bbs757EfbPageRules.IsHeadingName(_app, n.Name)) && !ValueOf.ContainsKey(n)
            && !Taken.Contains(n) && !InputOf.ContainsKey(n) && n.Text.Any(char.IsLetterOrDigit);

        /// <summary>Gives each heading title its level: 2, or 3 when a container above it already holds a title of its own.</summary>
        private void Title(IReadOnlyList<Bbs757EfbMemory.Node> kids, bool titledAbove)
        {
            bool holds = kids.Any(IsHeadingTitle);
            foreach (var k in kids.Where(IsHeadingTitle)) LevelOf[k] = titledAbove ? 3 : 2;
            foreach (var k in kids) Title(k.Children, titledAbove || holds);
        }

        private void Pictures(List<Bbs757EfbMemory.Node> all)
        {
            var parentOf = new Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
            foreach (var n in all) foreach (var c in n.Children) parentOf[c] = n;
            foreach (var picture in all.Where(n => n.Kind != "button" && n.Text.Length > 0))
            {
                if (Bbs757EfbPageRules.PictureState(picture.Text) is not { } state || !parentOf.TryGetValue(picture, out var box)) continue;
                var buttons = Descendants(box).Where(b => b.Kind == "button" && b.Text.Length > 0).ToList();
                var own = Bbs757EfbPageRules.NameParts(picture.Name).Where(p => !GenericParts.Contains(p)).ToHashSet(StringComparer.Ordinal);
                var named = buttons.Where(b => Bbs757EfbPageRules.NameParts(b.Name).Any(own.Contains)).ToList();
                var direct = box.Children.Where(b => b.Kind == "button" && b.Text.Length > 0).ToList();
                var tile = named.Count == 1 ? named[0] : named.Count == 0 && direct.Count == 1 ? direct[0] : null;
                if (tile is not null) _pictureOf[tile] = state;
            }
        }
    }
}
