using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The test-side file format of the tablet's live memory trees (<c>Fixtures/bbs757-efb-memory/&lt;name&gt;.tree.json</c>).
/// Each node keeps kind, name, text, rectangle and children, and, only when the walk read them (optional: absent reads as
/// none), a scrollbox's offset, a slider's knob, a button's selected state, and a drop-down's value, entries, open state, rows
/// and open list; each app keeps
/// key, class and controls. The file's root is the array of apps, or, for a capture with BlueBird's notifications, an object
/// <c>{ "apps": [...], "notices": [...] }</c>; a root array reads as no notices. The node's address is not written and reads
/// back as 0. Numbers are invariant and the output is indented so a diff reads.
/// </summary>
internal static class Bbs757EfbTreeJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The apps as the file's root array; with <paramref name="notices"/> (even none), the object holding both.</summary>
    internal static string Write(IReadOnlyList<Bbs757EfbMemory.App> apps, IReadOnlyList<Bbs757EfbNoticeRecord>? notices = null)
    {
        var array = new JsonArray();
        foreach (var app in apps)
            array.Add(new JsonObject
            {
                ["key"] = app.Key,
                ["class"] = app.Class,
                ["controls"] = new JsonArray(app.Controls.Select(n => (JsonNode?)NodeToJson(n)).ToArray()),
            });
        if (notices is null) return array.ToJsonString(Indented);
        return new JsonObject
        {
            ["apps"] = array,
            ["notices"] = new JsonArray(notices.Select(n => (JsonNode?)new JsonObject
            {
                ["id"] = n.Id,
                ["title"] = n.Title,
                ["text"] = n.Text,
                ["time"] = n.Time,
                ["unread"] = n.Unread,
            }).ToArray()),
        }.ToJsonString(Indented);
    }

    internal static IReadOnlyList<Bbs757EfbMemory.App> Read(string json)
    {
        var root = JsonNode.Parse(json)!;
        var array = root is JsonObject o ? o["apps"]!.AsArray() : root.AsArray();
        return array.Select(a => new Bbs757EfbMemory.App(
            a!["key"]!.GetValue<int>(), a["class"]!.GetValue<string>(),
            a["controls"]!.AsArray().Select(n => NodeFromJson(n!)).ToList())).ToList();
    }

    /// <summary>The notifications a capture holds; none for a root array (every tree captured before plan 21).</summary>
    internal static IReadOnlyList<Bbs757EfbNoticeRecord> ReadNotices(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject { } root || root["notices"] is not JsonArray notices) return Array.Empty<Bbs757EfbNoticeRecord>();
        return notices.Select(n => new Bbs757EfbNoticeRecord(n!["id"]!.GetValue<int>(), n["title"]!.GetValue<string>(),
            n["text"]!.GetValue<string>(), n["time"]!.GetValue<string>(), n["unread"]!.GetValue<bool>())).ToList();
    }

    /// <summary>Reads <c>Fixtures/bbs757-efb-memory/&lt;name&gt;.tree.json</c> from the source tree.</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> Load(string name) => Read(File.ReadAllText(PathOf(name)));

    /// <summary>The notifications in <c>Fixtures/bbs757-efb-memory/&lt;name&gt;.tree.json</c>.</summary>
    internal static IReadOnlyList<Bbs757EfbNoticeRecord> LoadNotices(string name) => ReadNotices(File.ReadAllText(PathOf(name)));

    private static string PathOf(string name) => Path.Combine(FixtureDir(), name + ".tree.json");

    /// <summary>
    /// The trees as the memory walk reads them with the scrollbox named <paramref name="scrollbox"/> scrolled by
    /// <paramref name="offset"/>: everything inside it drawn that much higher, the box itself where it is with its offset
    /// (<see cref="Bbs757EfbMemory"/> subtracts only the ancestors' offsets). Stands in for a capture of a scrolled page.
    /// </summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> Scrolled(IReadOnlyList<Bbs757EfbMemory.App> apps, string scrollbox, float offset)
    {
        static Bbs757EfbMemory.Node Up(Bbs757EfbMemory.Node n, float by) => n with
        {
            Y = n.Y - by, Children = n.Children.Select(c => Up(c, by)).ToList(), Rows = n.Rows?.Select(r => Up(r, by)).ToList(),
            OpenList = n.OpenList is { } list ? Up(list, by) : null,
        };
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) => n.Name == scrollbox
            ? n with { Scroll = offset, Children = n.Children.Select(c => Up(c, offset)).ToList() }
            : n with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>The trees with the slider named <paramref name="name"/> holding its knob at <paramref name="place"/> (0 to 1), the knob
    /// <paramref name="span"/> px along its track: the memory walk of a build whose layout has the slider knob, for a knob where a test
    /// needs one (the slider pages recaptured on 2026-11-07 carry BlueBird's own; 11-aircraft-fuel is kept from before, with none).</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithKnob(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, float place, float span)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) =>
            (n.Name == name ? n with { Knob = place, KnobSpan = span } : n) with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>The trees with no slider's knob: the memory walk of a layout without the slider knob, so a test pins the fallback a
    /// slider with no knob read takes (its rectangle's track, a knob placed from its value, the page's judgment) on a real page.</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithoutKnobs(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        static Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) =>
            n with { Knob = null, KnobSpan = 0f, Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>The trees with the control named <paramref name="name"/> showing <paramref name="text"/>: the tablet after a tap that
    /// changed one label.</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithText(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, string text) =>
        Change(apps, name, n => n with { Text = text });

    /// <summary>The trees with the control named <paramref name="name"/> read as BlueBird's class <paramref name="kind"/>
    /// (<c>textbox</c>, <c>multiline</c>, <c>pulldown</c>, <c>image</c>) holding <paramref name="text"/>: the memory walk of a layout that
    /// knows the class, on a tree captured before it did (where the control reads "other", and a text box's or a long text's words read
    /// none).</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithKind(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, string kind, string text) =>
        Change(apps, name, n => n with { Kind = kind, Text = text });

    /// <summary>The trees with the control named <paramref name="name"/> a drop-down whose face shows <paramref name="face"/> and whose
    /// entries are <paramref name="entries"/>, closed: its value the entry the face shows, "" when it shows none of them (Systems'
    /// SELECT), as the memory walk reads it (<see cref="Bbs757EfbMemory"/>).</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> AsDropDown(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, string face,
        params string[] entries) =>
        Change(apps, name, n => n with { Kind = "pulldown", Text = face, Value = entries.Contains(face) ? face : "", Entries = entries });

    /// <summary>The trees with the drop-down named <paramref name="name"/> open as the memory walk reads an open list
    /// (<see cref="Bbs757EfbMemory"/>): its list a scrollbox at the face's x and width from <paramref name="top"/>, <paramref name="height"/>
    /// tall and scrolled by <paramref name="scroll"/>, and one row per entry, 38 px apart from <paramref name="top"/> (as measured on the
    /// runway list, 2026-11-07), drawn <paramref name="scroll"/> higher, each <paramref name="rowWidth"/> wide (the face's width when not
    /// given; the Surface list's rows measured 130 in its 150 px box), or, with <paramref name="rowsAt"/>, every row still where a closed list
    /// leaves it (50x38, measured at [0,0]). Stands in for a capture of a drop-down no capture shows open.</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithOpenList(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, float top, float height,
        float scroll = 0f, float? rowWidth = null, System.Drawing.PointF? rowsAt = null) =>
        Change(apps, name, n => n with
        {
            Open = true,
            OpenList = new Bbs757EfbMemory.Node("scrollbox", "item_scrollbox", "", n.X, top, n.W, height, 0, Array.Empty<Bbs757EfbMemory.Node>(),
                Scroll: scroll),
            Rows = (n.Entries ?? Array.Empty<string>()).Select((entry, i) => new Bbs757EfbMemory.Node("button", "item_btn_" + i.ToString(CultureInfo.InvariantCulture),
                entry, rowsAt?.X ?? n.X, rowsAt?.Y ?? top + 38 * i - scroll, rowsAt is null ? rowWidth ?? n.W : 50, 38, 0,
                Array.Empty<Bbs757EfbMemory.Node>())).ToList(),
        });

    /// <summary>The trees as the memory walk reads BlueBird's selected state: each button named in <paramref name="selected"/> the
    /// current one of its row (Selected true), the other buttons with words beside it in its parent not (false), every other control
    /// none (null), as only a side menu's and a tab row's buttons are selectable (measured 2026-11-07).</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithSelected(IReadOnlyList<Bbs757EfbMemory.App> apps, params string[] selected)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n)
        {
            bool row = n.Children.Any(c => selected.Contains(c.Name));
            return n with
            {
                Children = n.Children.Select(c => Walk(c) with
                {
                    Selected = selected.Contains(c.Name) ? true : row && c.Kind == "button" && c.Text.Length > 0 ? false : c.Selected,
                }).ToList(),
            };
        }
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>The trees with <paramref name="change"/> made to every control named <paramref name="name"/>.</summary>
    private static IReadOnlyList<Bbs757EfbMemory.App> Change(IReadOnlyList<Bbs757EfbMemory.App> apps, string name,
        Func<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> change)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) =>
            (n.Name == name ? change(n) : n) with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures", "bbs757-efb-memory");
    }

    /// <summary>A node as JSON; a scrollbox's offset when not 0, a slider's knob (its place and its size along the track), a
    /// button's selected state and a drop-down's value, entries, open state, rows and open list only when the walk read them, so a
    /// node without them, and every tree committed before they were read, reads the same.</summary>
    private static JsonObject NodeToJson(Bbs757EfbMemory.Node n)
    {
        var o = new JsonObject
        {
            ["kind"] = n.Kind,
            ["name"] = n.Name,
            ["text"] = n.Text,
            ["x"] = Number(n.X),
            ["y"] = Number(n.Y),
            ["w"] = Number(n.W),
            ["h"] = Number(n.H),
        };
        if (n.Scroll != 0) o["scroll"] = Number(n.Scroll);
        if (n.Knob is float knob)
        {
            o["knob"] = Number(knob);
            o["knobSpan"] = Number(n.KnobSpan);
        }
        if (n.Selected is bool selected) o["selected"] = selected;
        if (n.Value.Length > 0) o["value"] = n.Value;
        if (n.Entries is { } entries) o["entries"] = new JsonArray(entries.Select(e => (JsonNode?)e).ToArray());
        if (n.Open) o["open"] = true;
        if (n.Rows is { } rows) o["rows"] = new JsonArray(rows.Select(r => (JsonNode?)NodeToJson(r)).ToArray());
        if (n.OpenList is { } list) o["list"] = NodeToJson(list);
        o["children"] = new JsonArray(n.Children.Select(c => (JsonNode?)NodeToJson(c)).ToArray());
        return o;
    }

    /// <summary>A node from JSON; the scroll, knob, selected, value, entries, open, rows and list fields are optional (absent:
    /// none).</summary>
    private static Bbs757EfbMemory.Node NodeFromJson(JsonNode j) => new(
        j["kind"]!.GetValue<string>(), j["name"]!.GetValue<string>(), j["text"]!.GetValue<string>(),
        j["x"]!.GetValue<float>(), j["y"]!.GetValue<float>(), j["w"]!.GetValue<float>(), j["h"]!.GetValue<float>(),
        0, j["children"]!.AsArray().Select(c => NodeFromJson(c!)).ToList(),
        Scroll: j["scroll"]?.GetValue<float>() ?? 0f,
        Knob: j["knob"]?.GetValue<float>(), KnobSpan: j["knobSpan"]?.GetValue<float>() ?? 0f,
        Selected: j["selected"]?.GetValue<bool>(), Value: j["value"]?.GetValue<string>() ?? "",
        Entries: j["entries"]?.AsArray().Select(e => e!.GetValue<string>()).ToList(), Open: j["open"]?.GetValue<bool>() ?? false,
        Rows: j["rows"]?.AsArray().Select(r => NodeFromJson(r!)).ToList(),
        OpenList: j["list"] is { } list ? NodeFromJson(list) : null);

    // A float widened to a double would print noise digits (12.300000190734863); write its shortest round-trip text.
    private static JsonNode Number(float v) =>
        JsonNode.Parse(v.ToString("R", CultureInfo.InvariantCulture))!;
}
