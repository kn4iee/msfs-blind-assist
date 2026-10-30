using System.Text.Json;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Presses the mailbox's buttons as the sighted pilot does: the captain's KCCU MAILBOX key brings the cursor to it (INI_CPT_CURSOR_SCREEN
/// 3, [A350-4]'s rule for this screen too), then the Coherent mouse route clicks in the view holding the CTR. Never writes the sim's
/// memory [A350-1].
/// </summary>
public sealed class A350MailboxInput
{
    private const string Find = "(function(){var e=document.querySelector('wasm-instrument');return e?e.getAttribute('guid')+'|'+e.getAttribute('url'):''})()";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private const int CaptainSide = 0, ClickLandsMs = 250;

    private readonly A350MfdInput _kccu;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<float, float, CancellationToken, Task<bool>> _click;
    private (string Title, int Guid)? _view;
    private int _acting;
    private volatile bool _unreachableLogged;

    public A350MailboxInput(Action<string> send) : this(send, Task.Delay, null) { }

    internal A350MailboxInput(Action<string> send, Func<int, CancellationToken, Task> delay, Func<float, float, CancellationToken, Task<bool>>? click)
    {
        _kccu = new A350MfdInput(send, delay, (_, _, _, _) => Task.FromResult(true));
        _delay = delay;
        _click = click ?? ClickViewAsync;
    }

    /// <summary>The guid of a view's wasm-instrument ("guid|url") when it is the A350's CTR, else null.</summary>
    internal static int? CtrGuidIf(string guidAndUrl)
    {
        var parts = guidAndUrl.Split('|', 2);
        return parts.Length == 2 && parts[1].Contains("wasm_module=inibuilds-A350", StringComparison.OrdinalIgnoreCase)
            && parts[1].Contains("wasm_gauge=CTR", StringComparison.Ordinal) && int.TryParse(parts[0], out var g) ? g : null;
    }

    /// <summary>Whether a press is running.</summary>
    public bool Busy => Volatile.Read(ref _acting) == 1;

    /// <summary>Clicks a point of the CTR with the cursor on the mailbox; a second press while one runs is Busy.</summary>
    public async Task<MfdAction> ActAsync(float x, float y, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _acting, 1) == 1) return MfdAction.Busy;
        try
        {
            await _kccu.MoveCursorAsync(CaptainSide, "MAILBOX", ct).ConfigureAwait(false);
            if (!await _click(x, y, ct).ConfigureAwait(false))
            {
                Log.Debug("A350Display", FormattableString.Invariant($"Mailbox: click {x:0},{y:0} did not reach it"));
                return MfdAction.Unreachable;
            }
            await _delay(ClickLandsMs, ct).ConfigureAwait(false);
            Log.Debug("A350Display", FormattableString.Invariant($"Mailbox: click {x:0},{y:0}"));
            return MfdAction.Done;
        }
        finally { Volatile.Write(ref _acting, 0); }
    }

    private async Task<(string Title, int Guid)?> ViewAsync(CancellationToken ct)
    {
        if (_view is { } known) return known;
        string list;
        try { list = await Http.GetStringAsync("http://127.0.0.1:19999/pagelist.json", ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            NoteUnreachable("the Coherent debugger did not answer: " + ex.Message);
            return null;
        }
        using var doc = JsonDocument.Parse(list);
        foreach (var page in doc.RootElement.EnumerateArray())
        {
            var title = page.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (!title.Contains("WasmInstrument", StringComparison.Ordinal)) continue;
            if (CtrGuidIf(await CoherentEvalClient.EvalAsync(title, Find, ct).ConfigureAwait(false)) is int guid)
                return _view = (title, guid);
        }
        NoteUnreachable("no view holds the A350's CTR");
        return null;
    }

    private void NoteUnreachable(string why)
    {
        if (_unreachableLogged) return;
        _unreachableLogged = true;
        Log.Warn("A350Display", "Mailbox input: " + why);
    }

    private async Task<bool> ClickViewAsync(float x, float y, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (await ViewAsync(ct).ConfigureAwait(false) is not { } view) return false;
            if (await CoherentEvalClient.EvalAsync(view.Title, A350MfdInput.ClickJs(view.Guid, 0, x, y), ct).ConfigureAwait(false) == "ok")
            {
                _unreachableLogged = false;
                return true;
            }
            _view = null;   // the view moved (a new session): find it again
        }
        NoteUnreachable("the CTR's view did not take the click");
        return false;
    }
}
