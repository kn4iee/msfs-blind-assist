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
                // The point is DECIMAL: the module's DOT key types nothing (live, the MFD's ZFW field).
                '/' => "SLASH", '.' => "DECIMAL", ' ' => "SPACE", '-' => "MINUS", '+' => "PLUS",
                _ => null,
            };
            if (key == null) return null;
            keys.Add(key);
        }
        return keys;
    }
}

/// <summary>What acting on an MFD line came to.</summary>
public enum MfdAction { Done, Busy, Unreachable, NoKey }

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

    /// <summary>The MFD key's hold [A350-3], the wait before the click once it is let go, the wait after a click before
    /// typing (ClickJs sends the release and the click 120 ms after it returns), and the wait after each typed key.</summary>
    private const int MfdKeyHoldMs = 180, AfterCursorMs = 50, ClickLandsMs = 250, KeyMs = 150;

    private readonly Action<string> _send;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<int, float, float, CancellationToken, Task<bool>> _click;
    private (string Title, int Guid)? _view;
    private int _acting;

    /// <param name="send">Runs one calculator string, never coalesced with an identical one before it (the definition's
    /// Send): a repeated key is the same text, which the MobiFlight channel would otherwise drop [VAR-4].</param>
    public A350MfdInput(Action<string> send) : this(send, Task.Delay, null) { }

    /// <param name="click">Clicks a side's MFD point through the Coherent mouse route; false when it cannot be reached
    /// (tests replace it, and the waits).</param>
    internal A350MfdInput(Action<string> send, Func<int, CancellationToken, Task> delay,
        Func<int, float, float, CancellationToken, Task<bool>>? click)
    {
        _send = send;
        _delay = delay;
        _click = click ?? ClickViewAsync;
    }

    /// <summary>The guid of a view's wasm-instrument ("guid|url") when it is the A350's MFD, else null.</summary>
    internal static int? GuidIf(string guidAndUrl)
    {
        var parts = guidAndUrl.Split('|', 2);
        return parts.Length == 2 && parts[1].Contains("wasm_module=inibuilds-A350", StringComparison.OrdinalIgnoreCase)
            && parts[1].Contains("wasm_gauge=MFD", StringComparison.Ordinal) && int.TryParse(parts[0], out var g) ? g : null;
    }

    /// <summary>The JavaScript that moves, presses and releases the mouse at a side's MFD point (the first
    /// officer's MFD is drawn 836 to the right). It first checks the page still holds the MFD it was found with: after a
    /// reload the view can keep its title while the gauge's guid changes, and a click on the old guid lands nowhere, so
    /// it answers 'moved' and clicks nothing, and the view is found again (review 2026-10-29).</summary>
    internal static string ClickJs(int guid, int side, float x, float y)
    {
        string X = (x + side * 836).ToString(CultureInfo.InvariantCulture), Y = y.ToString(CultureInfo.InvariantCulture);
        string at = $"{guid},{X},{Y},0";
        return $"(function(){{var e=document.querySelector('wasm-instrument');if(!e||e.getAttribute('guid')!=='{guid}')return 'moved';" +
               $"Coherent.call('WASM_MOUSE_MOVE',{at});Coherent.call('WASM_MOUSE_DOWN',{at});" +
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

    /// <summary>Presses a side's KCCU key once: a page or function key the aircraft resets itself. The cursor keys (ND,
    /// MFD) are not reset by it: <see cref="MoveCursorAsync"/> moves the cursor.</summary>
    public void Press(int side, string key) => _send($"1 (>L:{A350Kccu.LVar(side, key)})");

    /// <summary>Whether a line's action is running: a key pressed meanwhile would land among its keys.</summary>
    public bool Busy => Volatile.Read(ref _acting) == 1;

    /// <summary>Acts on a line: clicks it and, given a text, types it on the KCCU and presses ENTER. A text with a
    /// character no key types is refused before anything is clicked; a second action while one runs is Busy and sends
    /// nothing, so two Enters never interleave their keys (review 2026-10-29: "VVCCBBII").</summary>
    public async Task<MfdAction> ActAsync(int side, float x, float y, string? typed, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _acting, 1) == 1) return MfdAction.Busy;
        try
        {
            IReadOnlyList<string>? keys = null;
            if (!string.IsNullOrEmpty(typed) && (keys = A350Kccu.KeysFor(typed)) == null) return MfdAction.NoKey;
            if (!await ClickAsync(side, x, y, ct).ConfigureAwait(false)) return MfdAction.Unreachable;
            if (keys != null)
            {
                await PressAllAsync(side, keys, ct).ConfigureAwait(false);
                Press(side, "ENTER");
            }
            return MfdAction.Done;
        }
        finally { Volatile.Write(ref _acting, 0); }
    }

    /// <summary>Moves a side's KCCU cursor to its ND or MFD ([A350-4]: each takes the mouse only with the cursor on it).
    /// The cursor keys are ones the aircraft does not reset: held about 180 ms, then let go [A350-3] (the MFD key was only
    /// ever written 1; the probe's ND key likewise, tools review 2026-10-30), so the next press has its rising edge.</summary>
    public async Task MoveCursorAsync(int side, string screen, CancellationToken ct = default)
    {
        if (screen is not ("ND" or "MFD"))
            throw new ArgumentException("The KCCU cursor keys are ND and MFD.", nameof(screen));
        string key = A350Kccu.LVar(side, screen);
        _send($"1 (>L:{key})");
        await _delay(MfdKeyHoldMs, ct).ConfigureAwait(false);
        _send($"0 (>L:{key})");
        await _delay(AfterCursorMs, ct).ConfigureAwait(false);
    }

    private Task CursorToMfdAsync(int side, CancellationToken ct) => MoveCursorAsync(side, "MFD", ct);

    /// <summary>Clicks a point on a side's MFD once its cursor is there, and returns once the click has landed; false
    /// when the MFD view cannot be reached.</summary>
    public async Task<bool> ClickAsync(int side, float x, float y, CancellationToken ct = default)
    {
        await CursorToMfdAsync(side, ct).ConfigureAwait(false);
        if (!await _click(side, x, y, ct).ConfigureAwait(false)) return false;
        // ClickJs sends the release and the click 120 ms after it returns: a key typed before then reaches the field
        // focused before (review 2026-10-29).
        await _delay(ClickLandsMs, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ClickViewAsync(int side, float x, float y, CancellationToken ct)
    {
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
        await PressAllAsync(side, keys, ct).ConfigureAwait(false);
        return true;
    }

    private async Task PressAllAsync(int side, IReadOnlyList<string> keys, CancellationToken ct)
    {
        foreach (var key in keys)
        {
            Press(side, key);
            await _delay(KeyMs, ct).ConfigureAwait(false);
        }
    }
}
