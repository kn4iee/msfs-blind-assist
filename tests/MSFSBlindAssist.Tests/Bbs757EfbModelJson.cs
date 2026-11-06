using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Reads and writes a <see cref="Bbs757EfbScreenModel"/> as JSON, so the tablet-reader fixtures are stored as the
/// page model the pilot is given rather than a screenshot run through OCR. The models were serialised once from the
/// old OCR parse (the <c>.lines.json</c> + <c>.png</c> captures); the fixture loader now deserialises them, which is
/// why the tests no longer need PaddleOCR or the OCR parser. The shape is deliberately plain — rectangles and points
/// as arrays, enums as their names — so a committed <c>.model.json</c> is easy to read and diff.
/// </summary>
internal static class Bbs757EfbModelJson
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Write(Bbs757EfbScreenModel m)
    {
        var o = new JsonObject
        {
            ["app"] = m.App,
            ["section"] = m.Section,
            ["scrollable"] = m.Scrollable,
            ["menu"] = Items(m.Menu),
            ["items"] = Items(m.Items),
            ["topBar"] = Items(m.TopBar),
            ["notices"] = new JsonArray(m.Notices.Select(n => (JsonNode)new JsonObject
            {
                ["text"] = n.Text,
                ["time"] = n.Time,
            }).ToArray()),
            ["question"] = m.Question is null ? null : new JsonObject
            {
                ["text"] = m.Question.Text,
                ["buttons"] = Items(m.Question.Buttons),
            },
        };
        return o.ToJsonString(Pretty);
    }

    public static Bbs757EfbScreenModel Read(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        var question = o["question"] is JsonObject q
            ? new Bbs757EfbQuestion(Str(q, "text"), ReadItems(q["buttons"]))
            : null;
        return new Bbs757EfbScreenModel(Str(o, "app"), Str(o, "section"), ReadItems(o["menu"]), ReadItems(o["items"]))
        {
            Scrollable = o["scrollable"]?.GetValue<bool>() ?? false,
            TopBar = ReadItems(o["topBar"]),
            Notices = (o["notices"]?.AsArray() ?? new JsonArray())
                .Select(n => new Bbs757EfbNotice(Str(n!.AsObject(), "text"), n!.AsObject()["time"]?.GetValue<string>())).ToList(),
            Question = question,
        };
    }

    // ---- items ---------------------------------------------------------------

    private static JsonArray Items(IReadOnlyList<Bbs757EfbItem> items) =>
        new(items.Select(Item).ToArray());

    private static JsonNode Item(Bbs757EfbItem i)
    {
        var o = new JsonObject
        {
            ["kind"] = i.Kind.ToString(),
            ["text"] = i.Text,
            ["box"] = Rect(i.Box),
            ["hit"] = Pt(i.Hit),
            ["clickable"] = i.Clickable,
            ["key"] = i.Key,
            ["tint"] = i.Tint.ToString(),
            ["on"] = i.On,
            ["selected"] = i.Selected,
            ["value"] = i.Value,
            ["slider"] = i.Slider is null ? null
                : new JsonArray(i.Slider.TrackLeft, i.Slider.TrackRight, i.Slider.KnobX),
        };
        return o;
    }

    private static IReadOnlyList<Bbs757EfbItem> ReadItems(JsonNode? array)
    {
        if (array is not JsonArray a) return Array.Empty<Bbs757EfbItem>();
        return a.Select(n =>
        {
            var o = n!.AsObject();
            var slider = o["slider"] is JsonArray s ? new Bbs757EfbSlider(s[0]!.GetValue<int>(), s[1]!.GetValue<int>(), s[2]!.GetValue<int>()) : null;
            return new Bbs757EfbItem(
                Enum.Parse<Bbs757EfbKind>(Str(o, "kind")),
                Str(o, "text"),
                ReadRect(o["box"]!.AsArray()),
                ReadPt(o["hit"]!.AsArray()),
                o["clickable"]!.GetValue<bool>(),
                Str(o, "key"),
                Enum.Parse<Bbs757EfbTint>(Str(o, "tint")),
                o["on"]?.GetValue<bool>(),
                o["selected"]!.GetValue<bool>(),
                o["value"]?.GetValue<string>() ?? "",
                slider);
        }).ToList();
    }

    // ---- primitives ----------------------------------------------------------

    private static JsonArray Rect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    private static JsonArray Pt(Point p) => new(p.X, p.Y);
    private static Rectangle ReadRect(JsonArray a) => new(a[0]!.GetValue<int>(), a[1]!.GetValue<int>(), a[2]!.GetValue<int>(), a[3]!.GetValue<int>());
    private static Point ReadPt(JsonArray a) => new(a[0]!.GetValue<int>(), a[1]!.GetValue<int>());
    private static string Str(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";
}
