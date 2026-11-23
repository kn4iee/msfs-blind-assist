using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.A330.Display;

/// One line from the display reader: exactly one of HelloVersion, State, Frame and Scratchpad is set.
internal sealed record A330ReaderLine(int? HelloVersion, string? ReaderVersion, A330ReaderState? State, A330Frame? Frame, (A330Gauge Gauge, string Text)? Scratchpad);

/// The app's side of the display reader's line protocol: one JSON object a line, UTF-8, on the reader's
/// standard input (requests) and output (replies). Numbers are invariant; nothing here throws on bad input.
internal static class A330ReaderProtocol
{
    public const int Version = 1;

    public static string Hello() => "{\"hello\":" + Version + "}";

    public static string Want(A330Gauge gauge, int refreshMs) =>
        "{\"want\":\"" + gauge + "\",\"refreshMs\":" + refreshMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

    public static string Release(A330Gauge gauge) => "{\"release\":\"" + gauge + "\"}";

    public static string Reset() => "{\"reset\":true}";

    public static string Quit() => "{\"quit\":true}";

    /// One line from the reader, or null for a line that is not one of the four forms.
    public static A330ReaderLine? Parse(string line, long nowTicks)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("hello", out var hello))
            {
                if (hello.ValueKind != JsonValueKind.Number || !hello.TryGetInt32(out int version)) return null;
                string? reader = null;
                if (root.TryGetProperty("reader", out var r) && r.ValueKind == JsonValueKind.String) reader = r.GetString();
                return new A330ReaderLine(version, reader, null, null, null);
            }
            if (root.TryGetProperty("state", out var state))
            {
                if (state.ValueKind != JsonValueKind.String) return null;
                var s = ParseState(state.GetString());
                return s == null ? null : new A330ReaderLine(null, null, s, null, null);
            }
            if (root.TryGetProperty("frame", out var frame))
            {
                var f = ReadFrame(frame, nowTicks);
                return f == null ? null : new A330ReaderLine(null, null, null, f, null);
            }
            if (root.TryGetProperty("scratchpad", out var pad))
            {
                if (pad.ValueKind != JsonValueKind.Object) return null;
                var gauge = ReadGauge(pad);
                if (gauge == null) return null;
                if (!pad.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) return null;
                return new A330ReaderLine(null, null, null, null, (gauge.Value, text.GetString() ?? ""));
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// A frame object as it appears inside {"frame": ...} (a fixture file's content), or null when it is malformed.
    public static A330Frame? ParseFrame(string json, long nowTicks)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadFrame(doc.RootElement, nowTicks);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static A330ReaderState? ParseState(string? name) => name switch
    {
        "Starting" => A330ReaderState.Starting,
        "Running" => A330ReaderState.Running,
        "SimulatorNotFound" => A330ReaderState.SimulatorNotFound,
        "NoAccess" => A330ReaderState.NoAccess,
        "AircraftNotFound" => A330ReaderState.AircraftNotFound,
        "NotRecognised" => A330ReaderState.NotRecognised,
        "Stopped" => A330ReaderState.Stopped,
        _ => null,
    };

    private static A330Gauge? ReadGauge(JsonElement obj)
    {
        if (!obj.TryGetProperty("gauge", out var g) || g.ValueKind != JsonValueKind.String) return null;
        return g.GetString() switch
        {
            "McduCaptain" => A330Gauge.McduCaptain,
            "McduFirstOfficer" => A330Gauge.McduFirstOfficer,
            "Ewd" => A330Gauge.Ewd,
            "Sd" => A330Gauge.Sd,
            "PfdCaptain" => A330Gauge.PfdCaptain,
            "PfdFirstOfficer" => A330Gauge.PfdFirstOfficer,
            "NdCaptain" => A330Gauge.NdCaptain,
            "NdFirstOfficer" => A330Gauge.NdFirstOfficer,
            "DcduCaptain" => A330Gauge.DcduCaptain,
            "DcduFirstOfficer" => A330Gauge.DcduFirstOfficer,
            "Isis" => A330Gauge.Isis,
            _ => null,
        };
    }

    private static A330Frame? ReadFrame(JsonElement f, long nowTicks)
    {
        if (f.ValueKind != JsonValueKind.Object) return null;
        var gauge = ReadGauge(f);
        if (gauge == null) return null;
        if (!f.TryGetProperty("complete", out var complete) || complete.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        if (!f.TryGetProperty("missing", out var missing) || missing.ValueKind != JsonValueKind.Number || !missing.TryGetInt32(out int miss)) return null;
        if (!f.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Array) return null;

        bool drawn = true;
        if (f.TryGetProperty("drawn", out var d))
        {
            if (d.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            drawn = d.GetBoolean();
        }
        if (!Flag(f, "hiddenRowsLeftOut", out bool hidden)
            || !Flag(f, "list", out bool list) || !Flag(f, "listAbove", out bool above) || !Flag(f, "listBelow", out bool below))
            return null;

        // A display that draws nothing: no texts and not complete, whatever the line's complete and texts say (its texts
        // are not read).
        if (!drawn) return new A330Frame(gauge.Value, [], Complete: false, miss, nowTicks, Drawn: false, hidden, list, above, below);

        var read = new List<A330Text>();
        foreach (var te in texts.EnumerateArray())
        {
            var t = ReadText(te);
            if (t == null) return null;
            read.Add(t);
        }
        return new A330Frame(gauge.Value, read, complete.GetBoolean(), miss, nowTicks, drawn, hidden, list, above, below);
    }

    /// An optional true/false of the frame object, absent meaning false; false when it is there and not a true or false
    /// (the frame is then malformed).
    private static bool Flag(JsonElement f, string key, out bool value)
    {
        value = false;
        if (!f.TryGetProperty(key, out var e)) return true;
        if (e.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = e.GetBoolean();
        return true;
    }

    private static A330Text? ReadText(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (!t.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Number || !order.TryGetInt32(out int o)) return null;
        if (!Num(t, "x", out float x) || !Num(t, "baseline", out float baseline) || !Num(t, "size", out float size)) return null;
        if (!t.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) return null;
        if (!t.TryGetProperty("font", out var font) || font.ValueKind != JsonValueKind.String) return null;
        if (!t.TryGetProperty("charX", out var cx) || cx.ValueKind != JsonValueKind.Array) return null;
        var charX = new List<float>();
        foreach (var c in cx.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Number || !c.TryGetSingle(out float v) || !float.IsFinite(v)) return null;
            charX.Add(v);
        }

        A330Rgb? colour = null;
        if (t.TryGetProperty("colour", out var col) && col.ValueKind != JsonValueKind.Null)
        {
            if (col.ValueKind != JsonValueKind.Array || col.GetArrayLength() != 3) return null;
            var rgb = new float[3];
            int i = 0;
            foreach (var c in col.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Number || !c.TryGetSingle(out rgb[i]) || !float.IsFinite(rgb[i])) return null;
                i++;
            }
            colour = new A330Rgb(rgb[0], rgb[1], rgb[2]);
        }
        return new A330Text(o, x, baseline, text.GetString() ?? "", font.GetString() ?? "", size, colour, charX);
    }

    private static bool Num(JsonElement obj, string key, out float value)
    {
        value = 0;
        return obj.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out value) && float.IsFinite(value);
    }
}
