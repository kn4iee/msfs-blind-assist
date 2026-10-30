using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Operates a side's ND as the sighted pilot does: the KCCU's ND key brings the side's cursor to it [A350-4], then the Coherent
/// mouse route clicks or drags in the side's EFIS view. The two EFIS views show the same page; their gauges' parameters are
/// not in it, and panel.cfg's order (the captain's first) is the guids' order: the lower guid is the captain's. A click's
/// points are the ND's own (<see cref="A350NdPage"/>), the view's the EFIS's, the captain's ND on its right half. Never writes
/// the sim's memory [A350-1].
/// </summary>
public sealed class A350NdInput
{
    private const string Find = "(function(){var e=document.querySelector('wasm-instrument');return e?e.getAttribute('guid')+'|'+e.getAttribute('url'):''})()";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>The wait after a click before typing (the release and the click come 120 ms after it), and how long a drag takes.</summary>
    private const int ClickLandsMs = 250, DragStepMs = 40, DragSteps = 8;

    private readonly A350MfdInput _kccu;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<int, float, float, CancellationToken, Task<bool>> _click;
    private readonly Func<int, float, float, float, float, CancellationToken, Task<bool>> _drag;
    private IReadOnlyList<(string Title, int Guid)>? _views;
    private int _acting;
    private volatile bool _unreachableLogged;

    public A350NdInput(Action<string> send) : this(send, Task.Delay, null, null) { }

    internal A350NdInput(Action<string> send, Func<int, CancellationToken, Task> delay,
        Func<int, float, float, CancellationToken, Task<bool>>? click, Func<int, float, float, float, float, CancellationToken, Task<bool>>? drag)
    {
        _kccu = new A350MfdInput(send, delay, (_, _, _, _) => Task.FromResult(true));
        _delay = delay;
        _click = click ?? ClickViewAsync;
        _drag = drag ?? DragViewAsync;
    }

    /// <summary>The guid of a view's wasm-instrument ("guid|url") when it is one of the A350's EFIS's, else null.</summary>
    internal static int? EfisGuidIf(string guidAndUrl)
    {
        var parts = guidAndUrl.Split('|', 2);
        return parts.Length == 2 && parts[1].Contains("wasm_module=inibuilds-A350", StringComparison.OrdinalIgnoreCase)
            && parts[1].Contains("wasm_gauge=EFIS", StringComparison.Ordinal) && int.TryParse(parts[0], out var g) ? g : null;
    }

    /// <summary>The JavaScript that drags: down at the first point, moved in steps, up at the second (a click's checks, as
    /// <see cref="A350MfdInput.ClickJs"/>: a page that no longer holds the gauge answers 'moved').</summary>
    internal static string DragJs(int guid, float x0, float y0, float x1, float y1)
    {
        static string N(float v) => v.ToString(CultureInfo.InvariantCulture);
        return $"(function(){{var e=document.querySelector('wasm-instrument');if(!e||e.getAttribute('guid')!=='{guid}')return 'moved';" +
               $"var g={guid},x0={N(x0)},y0={N(y0)},x1={N(x1)},y1={N(y1)},n={DragSteps},i=0;" +
               "Coherent.call('WASM_MOUSE_MOVE',g,x0,y0,0);Coherent.call('WASM_MOUSE_DOWN',g,x0,y0,0);" +
               "function s(){i++;var x=x0+(x1-x0)*i/n,y=y0+(y1-y0)*i/n;Coherent.call('WASM_MOUSE_MOVE',g,x,y,0);" +
               $"if(i<n)setTimeout(s,{DragStepMs});else setTimeout(function(){{Coherent.call('WASM_MOUSE_UP',g,x1,y1,0);}},{DragStepMs});}}" +
               $"setTimeout(s,{DragStepMs});return 'ok'}})()";
    }

    private async Task<(string Title, int Guid)?> ViewAsync(int side, CancellationToken ct)
    {
        if (_views is { } known && side < known.Count) return known[side];
        string list;
        try { list = await Http.GetStringAsync("http://127.0.0.1:19999/pagelist.json", ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            NoteUnreachable("the Coherent debugger did not answer: " + ex.Message);
            return null;
        }
        var found = new List<(string, int)>();
        using (var doc = JsonDocument.Parse(list))
            foreach (var page in doc.RootElement.EnumerateArray())
            {
                var title = page.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                if (!title.Contains("WasmInstrument", StringComparison.Ordinal)) continue;
                if (EfisGuidIf(await CoherentEvalClient.EvalAsync(title, Find, ct).ConfigureAwait(false)) is int guid)
                    found.Add((title, guid));
            }
        _views = found.OrderBy(v => v.Item2).ToList();
        if (side < _views.Count) return _views[side];
        NoteUnreachable($"no EFIS view for side {side}: {found.Count} found");
        return null;
    }

    private void NoteUnreachable(string why)
    {
        if (_unreachableLogged) return;
        _unreachableLogged = true;
        Log.Warn("A350Display", "ND input: " + why);
    }

    private async Task<bool> EvalAsync(int side, Func<int, string> js, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (await ViewAsync(side, ct).ConfigureAwait(false) is not { } view) return false;
            if (await CoherentEvalClient.EvalAsync(view.Title, js(view.Guid), ct).ConfigureAwait(false) == "ok")
            {
                _unreachableLogged = false;
                return true;
            }
            _views = null;   // the views moved (a new session): find them again
        }
        NoteUnreachable("the ND's view did not take the mouse");
        return false;
    }

    private Task<bool> ClickViewAsync(int side, float x, float y, CancellationToken ct) =>
        EvalAsync(side, guid => A350MfdInput.ClickJs(guid, 0, A350NdPage.EfisX(side, x), y), ct);

    private Task<bool> DragViewAsync(int side, float x0, float y0, float x1, float y1, CancellationToken ct) =>
        EvalAsync(side, guid => DragJs(guid, A350NdPage.EfisX(side, x0), y0, A350NdPage.EfisX(side, x1), y1), ct);

    /// <summary>Whether an action is running: a key pressed meanwhile would land among its keys.</summary>
    public bool Busy => Volatile.Read(ref _acting) == 1;

    /// <summary>Presses a side's KCCU key.</summary>
    public void Press(int side, string key) => _kccu.Press(side, key);

    /// <summary>Clicks a point of a side's ND and, given a text, types it on the KCCU and presses ENTER; a text with a character no
    /// key types is refused before anything is clicked, and a second action while one runs is Busy.</summary>
    public async Task<MfdAction> ActAsync(int side, float x, float y, string? typed, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _acting, 1) == 1) return MfdAction.Busy;
        try
        {
            IReadOnlyList<string>? keys = null;
            if (!string.IsNullOrEmpty(typed) && (keys = A350Kccu.KeysFor(typed)) == null) return MfdAction.NoKey;
            await _kccu.MoveCursorAsync(side, "ND", ct).ConfigureAwait(false);
            if (!await _click(side, x, y, ct).ConfigureAwait(false))
            {
                Log.Debug("A350Display", FormattableString.Invariant($"ND {side + 1}: click {x:0},{y:0} did not reach it"));
                return MfdAction.Unreachable;
            }
            await _delay(ClickLandsMs, ct).ConfigureAwait(false);
            if (keys != null)
            {
                await _kccu.TypeAsync(side, typed!, ct).ConfigureAwait(false);
                _kccu.Press(side, "ENTER");
            }
            Log.Debug("A350Display", FormattableString.Invariant($"ND {side + 1}: click {x:0},{y:0}") + (keys != null ? ", typed " + typed : ""));
            return MfdAction.Done;
        }
        finally { Volatile.Write(ref _acting, 0); }
    }

    /// <summary>Drags a side's map from one point of its ND to another: in PLAN it stays there, in NAV and ARC it re-centres on the
    /// aircraft as it is let go, as the guide says.</summary>
    public async Task<MfdAction> DragAsync(int side, float x0, float y0, float x1, float y1, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _acting, 1) == 1) return MfdAction.Busy;
        try
        {
            await _kccu.MoveCursorAsync(side, "ND", ct).ConfigureAwait(false);
            if (!await _drag(side, x0, y0, x1, y1, ct).ConfigureAwait(false)) return MfdAction.Unreachable;
            await _delay(DragStepMs * (DragSteps + 2), ct).ConfigureAwait(false);
            Log.Debug("A350Display", FormattableString.Invariant($"ND {side + 1}: drag {x0:0},{y0:0} to {x1:0},{y1:0}"));
            return MfdAction.Done;
        }
        finally { Volatile.Write(ref _acting, 0); }
    }
}
