using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The KCCU's keys: the L:var each side's key presses (one write of 1; the aircraft resets it), and
/// which keys type a text.</summary>
public static class A350Kccu
{
    /// <summary>The page and function keys besides letters and digits (from the module's own names).</summary>
    public static readonly IReadOnlyList<string> FunctionKeys = new[]
    {
        "INIT", "FPLAN", "PERF", "FUEL", "DIR", "DIR_TO", "DEST", "SEC", "RADNAV", "SURV", "ATC", "DATA", "MAILBOX",
        "PROG", "REF", "AIRPORT", "CL_MENU", "MENU", "NOTEPAD", "OIS", "ND", "MFD", "ESC", "CLR", "BACKSPACE", "ENTER",
        "UP", "DOWN", "LEFT", "RIGHT", "SCROLL_UP", "SCROLL_DOWN",
    };

    public static string LVar(int side, string key) => $"INI_MCDU{side + 1}_{key}";

    /// <summary>The keys that type <paramref name="text"/>, or null when a character has no key.</summary>
    public static IReadOnlyList<string>? KeysFor(string text)
    {
        var keys = new List<string>();
        foreach (char ch in text.ToUpperInvariant())
        {
            string? key = ch switch
            {
                >= 'A' and <= 'Z' or >= '0' and <= '9' => ch.ToString(),
                '/' => "SLASH", '.' => "DOT", ' ' => "SPACE", '-' => "MINUS", '+' => "PLUS",
                _ => null,
            };
            if (key == null) return null;
            keys.Add(key);
        }
        return keys;
    }
}

/// <summary>
/// Operates the MFD: clicks through the Coherent debugger's mouse route into the view whose wasm-instrument names
/// the A350's MFD gauge (found by its url, since the view's number and the gauge's guid change per session), and
/// presses KCCU keys. The MFD takes mouse input only while the side's KCCU cursor is on it, so a click first
/// presses that side's MFD key [A350-4]. Never writes the sim's memory [A350-1].
/// </summary>
public sealed class A350MfdInput
{
    private const string Find = "(function(){var e=document.querySelector('wasm-instrument');return e?e.getAttribute('guid')+'|'+e.getAttribute('url'):''})()";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private readonly Func<string, double, bool> _setLVar;
    private (string Title, int Guid)? _view;

    /// <param name="setLVar">Writes an L:var once (the definition's SetLVar).</param>
    public A350MfdInput(Func<string, double, bool> setLVar) => _setLVar = setLVar;

    /// <summary>The guid of a view's wasm-instrument ("guid|url") when it is the A350's MFD, else null.</summary>
    internal static int? GuidIf(string guidAndUrl)
    {
        var parts = guidAndUrl.Split('|', 2);
        return parts.Length == 2 && parts[1].Contains("wasm_module=inibuilds-A350", StringComparison.OrdinalIgnoreCase)
            && parts[1].Contains("wasm_gauge=MFD", StringComparison.Ordinal) && int.TryParse(parts[0], out var g) ? g : null;
    }

    /// <summary>The JavaScript that moves, presses and releases the mouse at a side's MFD point (the first
    /// officer's MFD is drawn 836 to the right).</summary>
    internal static string ClickJs(int guid, int side, float x, float y)
    {
        string X = (x + side * 836).ToString(CultureInfo.InvariantCulture), Y = y.ToString(CultureInfo.InvariantCulture);
        string at = $"{guid},{X},{Y},0";
        return $"(function(){{Coherent.call('WASM_MOUSE_MOVE',{at});Coherent.call('WASM_MOUSE_DOWN',{at});" +
               $"setTimeout(function(){{Coherent.call('WASM_MOUSE_UP',{at});Coherent.call('WASM_CLICK',{at});}},120);return 'ok'}})()";
    }

    private async Task<(string Title, int Guid)?> ViewAsync(CancellationToken ct)
    {
        if (_view is { } known) return known;
        string list;
        try { list = await Http.GetStringAsync("http://127.0.0.1:19999/pagelist.json", ct).ConfigureAwait(false); }
        catch { return null; }
        using var doc = JsonDocument.Parse(list);
        foreach (var page in doc.RootElement.EnumerateArray())
        {
            var title = page.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (!title.Contains("WasmInstrument", StringComparison.Ordinal)) continue;
            if (GuidIf(await CoherentEvalClient.EvalAsync(title, Find, ct).ConfigureAwait(false)) is int guid)
                return _view = (title, guid);
        }
        return null;
    }

    /// <summary>Presses a side's KCCU key once.</summary>
    public bool Press(int side, string key) => _setLVar(A350Kccu.LVar(side, key), 1);

    /// <summary>Clicks a point on a side's MFD; false when the MFD view cannot be reached.</summary>
    public async Task<bool> ClickAsync(int side, float x, float y, CancellationToken ct = default)
    {
        Press(side, "MFD");
        await Task.Delay(200, ct).ConfigureAwait(false);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (await ViewAsync(ct).ConfigureAwait(false) is not { } view) return false;
            if (await CoherentEvalClient.EvalAsync(view.Title, ClickJs(view.Guid, side, x, y), ct).ConfigureAwait(false) == "ok") return true;
            _view = null;   // the view moved (a new session): find it again
        }
        return false;
    }

    /// <summary>Types a text on a side's KCCU, key by key; false when a character has no key.</summary>
    public async Task<bool> TypeAsync(int side, string text, CancellationToken ct = default)
    {
        if (A350Kccu.KeysFor(text) is not { } keys) return false;
        foreach (var key in keys)
        {
            Press(side, key);
            await Task.Delay(150, ct).ConfigureAwait(false);
        }
        return true;
    }
}
