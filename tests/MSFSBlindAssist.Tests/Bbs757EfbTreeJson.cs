using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The test-side file format of the tablet's live memory trees (<c>Fixtures/bbs757-efb-memory/&lt;name&gt;.tree.json</c>).
/// Each node keeps kind, name, text, rectangle and children; each app keeps key, class and controls. The node's
/// address is not written and reads back as 0. Numbers are invariant and the output is indented so a diff reads.
/// </summary>
internal static class Bbs757EfbTreeJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal static string Write(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        var array = new JsonArray();
        foreach (var app in apps)
            array.Add(new JsonObject
            {
                ["key"] = app.Key,
                ["class"] = app.Class,
                ["controls"] = new JsonArray(app.Controls.Select(n => (JsonNode?)NodeToJson(n)).ToArray()),
            });
        return array.ToJsonString(Indented);
    }

    internal static IReadOnlyList<Bbs757EfbMemory.App> Read(string json)
    {
        var array = JsonNode.Parse(json)!.AsArray();
        return array.Select(a => new Bbs757EfbMemory.App(
            a!["key"]!.GetValue<int>(), a["class"]!.GetValue<string>(),
            a["controls"]!.AsArray().Select(n => NodeFromJson(n!)).ToList())).ToList();
    }

    /// <summary>Reads <c>Fixtures/bbs757-efb-memory/&lt;name&gt;.tree.json</c> from the source tree.</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> Load(string name) =>
        Read(File.ReadAllText(Path.Combine(FixtureDir(), name + ".tree.json")));

    /// <summary>
    /// The trees as the memory walk reads them with the scrollbox named <paramref name="scrollbox"/> scrolled by
    /// <paramref name="offset"/>: everything inside it drawn that much higher, the box itself where it is with its offset
    /// (<see cref="Bbs757EfbMemory"/> subtracts only the ancestors' offsets). Stands in for a capture of a scrolled page.
    /// </summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> Scrolled(IReadOnlyList<Bbs757EfbMemory.App> apps, string scrollbox, float offset)
    {
        static Bbs757EfbMemory.Node Up(Bbs757EfbMemory.Node n, float by) =>
            n with { Y = n.Y - by, Children = n.Children.Select(c => Up(c, by)).ToList() };
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) => n.Name == scrollbox
            ? n with { Scroll = offset, Children = n.Children.Select(c => Up(c, offset)).ToList() }
            : n with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>The trees with the control named <paramref name="name"/> showing <paramref name="text"/>: the tablet after a tap that
    /// changed one label.</summary>
    internal static IReadOnlyList<Bbs757EfbMemory.App> WithText(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, string text)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) =>
            (n.Name == name ? n with { Text = text } : n) with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures", "bbs757-efb-memory");
    }

    private static JsonObject NodeToJson(Bbs757EfbMemory.Node n) => new()
    {
        ["kind"] = n.Kind,
        ["name"] = n.Name,
        ["text"] = n.Text,
        ["x"] = Number(n.X),
        ["y"] = Number(n.Y),
        ["w"] = Number(n.W),
        ["h"] = Number(n.H),
        ["children"] = new JsonArray(n.Children.Select(c => (JsonNode?)NodeToJson(c)).ToArray()),
    };

    private static Bbs757EfbMemory.Node NodeFromJson(JsonNode j) => new(
        j["kind"]!.GetValue<string>(), j["name"]!.GetValue<string>(), j["text"]!.GetValue<string>(),
        j["x"]!.GetValue<float>(), j["y"]!.GetValue<float>(), j["w"]!.GetValue<float>(), j["h"]!.GetValue<float>(),
        0, j["children"]!.AsArray().Select(c => NodeFromJson(c!)).ToList());

    // A float widened to a double would print noise digits (12.300000190734863); write its shortest round-trip text.
    private static JsonNode Number(float v) =>
        JsonNode.Parse(v.ToString("R", CultureInfo.InvariantCulture))!;
}
