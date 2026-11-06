using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Turns the EFB memory walk (<see cref="Bbs757EfbMemory"/>) into the window's model (<see cref="Bbs757EfbScreenModel"/>),
/// the memory replacement for <see cref="Bbs757EfbScreen"/>'s OCR parse: the front content app's page becomes the
/// App, its side menu and its items; settings rows (a grid holding a toggle or slider with its label and value) become
/// one Toggle or Slider item carrying the value the tablet shows. Pure over the walk; tested against a synthetic tree
/// and live against the running 757.
/// </summary>
public sealed class Bbs757EfbModelReader
{
    private readonly Bbs757EfbMemory _memory;

    public Bbs757EfbModelReader(Bbs757EfbMemory memory) => _memory = memory;

    private const int MenuRight = 252; // controls left of this are the side menu

    /// <summary>The App tokens whose app is a content screen (the reader's class comes from the app's vtable, which
    /// the layout maps to these tokens); "HOME" is the home screen, handled apart.</summary>
    private static readonly HashSet<string> ContentApps = new(StringComparer.Ordinal)
        { "AIRCRAFT", "NAVIGATION", "SYSTEMS", "CHECKLIST", "SETTINGS", "SIGN IN" };

    /// <summary>The current page as the window's model; null when the EFB is not reachable (not located yet).</summary>
    public Bbs757EfbScreenModel? Read()
    {
        var apps = _memory.ReadFrontApps();
        if (apps.Count == 0) return null;

        // The content app: the front content app with the most visible controls (the one drawn on top).
        var content = apps
            .Where(a => ContentApps.Contains(a.Class) && Count(a.Controls) > 0)
            .OrderByDescending(a => Count(a.Controls))
            .FirstOrDefault();

        if (content is null)
            return new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(), HomeTiles(apps));

        var menu = new List<Bbs757EfbItem>();
        var items = new List<Bbs757EfbItem>();
        foreach (var root in content.Controls) Emit(root, menu, items);
        return new Bbs757EfbScreenModel(content.Class, "", menu, items);
    }

    // ---- flattening ----------------------------------------------------------

    private void Emit(Bbs757EfbMemory.Node n, List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items)
    {
        // A settings row: a grid that directly holds a toggle or slider, with its label and value beside it.
        if (n.Kind == "grid" && n.Children.FirstOrDefault(c => c.Kind is "toggle" or "slider") is { } control)
        {
            items.Add(Row(n, control));
            return;
        }
        switch (n.Kind)
        {
            case "button":
                if (n.Name == "home_btn") return; // the window adds its own Home
                var item = new Bbs757EfbItem(IsMenu(n) ? Bbs757EfbKind.Menu : Bbs757EfbKind.Button,
                    n.Text, Box(n), Hit(n), true, n.Name);
                (IsMenu(n) ? menu : items).Add(item);
                return;
            case "toggle":
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Toggle, n.Text, Box(n), Hit(n), true, n.Name));
                return;
            case "slider":
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Slider, n.Text, Box(n), Hit(n), true, n.Name));
                return;
            case "label":
            case "other":
                if (n.Text.Length > 0 && n.Children.Count == 0 && !IsNoise(n))
                    items.Add(new Bbs757EfbItem(IsHeading(n) ? Bbs757EfbKind.Heading : Bbs757EfbKind.Text,
                        n.Text, Box(n), Hit(n), false, KeyOf(n)));
                foreach (var c in n.Children) Emit(c, menu, items);
                return;
            default: // grid, colorbox, canvas, gradientbox, stack, image, pan_indicator, scrollbox: containers
                foreach (var c in n.Children) Emit(c, menu, items);
                return;
        }
    }

    /// <summary>A settings row as one item: the control's kind and tap point, the label's words, and the value the
    /// row's value label shows (the toggle/slider has no text of its own).</summary>
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
        bool? on = value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? true
            : value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? false : null;
        return new Bbs757EfbItem(kind, label, Box(control), Hit(control), true, control.Name, On: on, Value: value);
    }

    private static bool IsValue(Bbs757EfbMemory.Node n) =>
        n.Kind is "label" or "other" && (n.Name.EndsWith("_value", StringComparison.Ordinal)
            || n.Name.EndsWith("_value_label", StringComparison.Ordinal) || n.X >= 1180);

    /// <summary>Text that is not part of the page's reading: an image or asset path the tablet keeps in a label
    /// field, or the app's own page-title label (the window shows that as the page name, not an item). A real
    /// label may hold a slash ("AUTOMATIC GPU/JETWAY", "B/CRS"), so only a leading path marker or an image file
    /// extension counts as a path, never a bare slash.</summary>
    private static bool IsNoise(Bbs757EfbMemory.Node n) =>
        n.Name == "page_title_label"
        || n.Text.StartsWith("./", StringComparison.Ordinal)
        || n.Text.StartsWith("../", StringComparison.Ordinal)
        || n.Text.StartsWith("/", StringComparison.Ordinal)
        || n.Text.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

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

    private IReadOnlyList<Bbs757EfbItem> HomeTiles(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        var tiles = new List<Bbs757EfbItem>();
        var main = apps.FirstOrDefault(a => a.Class == "HOME");
        if (main != null)
            foreach (var root in main.Controls) Emit(root, new List<Bbs757EfbItem>(), tiles);
        return tiles;
    }

    private static bool IsMenu(Bbs757EfbMemory.Node n) => n.X is >= 0 and < MenuRight && n.W <= MenuRight;
    private static bool IsHeading(Bbs757EfbMemory.Node n) => n.Text == n.Text.ToUpperInvariant() && n.W > 300;
    private static string KeyOf(Bbs757EfbMemory.Node n) => n.Name.Length > 0 ? n.Name : "text|" + n.Text;
    private static Rectangle Box(Bbs757EfbMemory.Node n) => new((int)n.X, (int)n.Y, (int)n.W, (int)n.H);
    private static Point Hit(Bbs757EfbMemory.Node n) => new((int)(n.X + n.W / 2), (int)(n.Y + n.H / 2));
}
