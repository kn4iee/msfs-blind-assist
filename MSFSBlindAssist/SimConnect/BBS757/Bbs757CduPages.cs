using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What <see cref="Bbs757CduPages"/> needs of the link to the 757's CDUs:
/// <see cref="CoherentBbs757TabletClient"/> in the app, a fake CDU in the tests.
/// </summary>
public interface IBbs757CduLink
{
    /// <summary>Whether keys and reads can reach the tablet view now.</summary>
    bool IsReadable { get; }

    /// <summary>Keys queued or still in their cycle, the pilot's own from the CDU window included.</summary>
    int PendingKeyCount { get; }

    /// <summary>Queues one key for a CDU, held <paramref name="holdMs"/>.</summary>
    void PressKey(int cdu, string key, int holdMs);

    /// <summary>One CDU's screen now; null when it cannot be read.</summary>
    Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct);
}

/// <summary>
/// A CDU as a page service, for the features that read pages and make entries in the background.
/// They use the first officer's CDU only (<see cref="FirstOfficer"/>, ruling R52), so the captain's,
/// the one the pilot works, never changes under him.
///
/// One call at a time: every call takes one gate, waits at most <see cref="GateWaitMs"/> for it, and
/// otherwise answers null (false for the wipe). The keys share the tablet client's one queue with
/// the CDU window, so a call that would press keys refuses (null) while any key is still queued, the
/// pilot's own included, rather than interleave with them; it refuses too while the tablet view
/// cannot be reached, and before pressing anything when a key or a character has no CDU key.
/// Reading is always allowed.
///
/// An entry is typed only into a scratchpad the wipe has cleared, and its line key is pressed only
/// when the scratchpad shows exactly the typed text: a CDU message ("INVALID ENTRY") or a lost key
/// gets the scratchpad wiped again and null, never a wrong entry. Only the wipe holds a key longer
/// than <see cref="CoherentBbs757TabletClient.KeyHoldMs"/>: CLR, 1300 ms.
///
/// Never throws: an exception from the link or the caller's predicate, or a canceled token, is
/// logged and answered null (false). Every wait goes through the delay given to the constructor
/// (<see cref="Task.Delay(int, CancellationToken)"/> in the app), so the tests run at once.
/// </summary>
public sealed class Bbs757CduPages
{
    /// <summary>The first officer's CDU, the only one background work uses.</summary>
    public const int FirstOfficer = 2;

    // How long a call waits for the one before it.
    private const int GateWaitMs = 5000;
    // A page key's new page: read every PagePollMs for up to PageWaitMs.
    private const int PagePollMs = 250;
    private const int PageWaitMs = 3000;
    // The wipe: CLR held WipeHoldMs (the whole scratchpad), read WipeSettleMs later; if anything is
    // left, one ordinary CLR, read ShortClrSettleMs later.
    private const int WipeHoldMs = 1300;
    private const int WipeSettleMs = 1800;
    private const int ShortClrSettleMs = 900;
    // Typed keys: the queue is checked every DrainPollMs, for at most a key cycle per key plus DrainSlackMs.
    private const int DrainPollMs = 100;
    private const int DrainSlackMs = 2000;
    // After the last key (a line key, a page change), before the fresh read.
    private const int KeySettleMs = 1200;

    private readonly IBbs757CduLink _link;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="link">The link to the CDUs.</param>
    /// <param name="delay">Waits the given milliseconds (<see cref="Task.Delay(int, CancellationToken)"/> by default).</param>
    public Bbs757CduPages(IBbs757CduLink link, Func<int, CancellationToken, Task>? delay = null)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    /// <summary>The page service over the 757's tablet client.</summary>
    public static Bbs757CduPages For(CoherentBbs757TabletClient client) => new(client);

    /// <summary>The CDU's screen now; null when it cannot be read.</summary>
    public Task<Bbs757CduScreen?> ReadAsync(int cdu, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} read", null, ct, () => ReadCore(cdu, ct));

    /// <summary>
    /// Brings up a page: the screen when it is already <paramref name="isWanted"/>; else, unless
    /// keys are queued, presses <paramref name="pageKey"/> and reads every 250 ms for up to 3 s until
    /// it is. Null otherwise. A caller wanting page 1 of a set passes a predicate for any page of it,
    /// then uses <see cref="PageCount"/> and <see cref="PressKeysAsync"/> with PREV.
    /// </summary>
    public Task<Bbs757CduScreen?> ShowPageAsync(int cdu, string pageKey, Func<Bbs757CduScreen, bool> isWanted, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} page {pageKey}", null, ct, async () =>
        {
            var screen = await ReadCore(cdu, ct);
            if (screen == null) return null;
            if (isWanted(screen)) return screen;
            if (Refused(cdu, new[] { pageKey })) return null;

            _link.PressKey(cdu, pageKey, CoherentBbs757TabletClient.KeyHoldMs);
            for (int waited = 0; waited < PageWaitMs; waited += PagePollMs)
            {
                await _delay(PagePollMs, ct);
                screen = await ReadCore(cdu, ct);
                if (screen != null && isWanted(screen)) return screen;
            }
            Log.Debug("BBS757", $"CDU {cdu}: {pageKey} did not bring up the page wanted.");
            return null;
        });

    /// <summary>
    /// Clears the scratchpad: CLR held 1300 ms (the whole of it), then, if anything is left, one
    /// ordinary CLR. True when the scratchpad reads blank; false when it does not, cannot be read, or
    /// keys are queued.
    /// </summary>
    public Task<bool> WipeScratchpadAsync(int cdu, CancellationToken ct) =>
        Gated($"CDU {cdu} wipe", false, ct, async () =>
            !Refused(cdu, new[] { Bbs757CduKeys.Clr }) && await WipeCore(cdu, ct));

    /// <summary>
    /// Enters <paramref name="text"/> at <paramref name="lineKey"/>: wipes the scratchpad, types the
    /// text, and presses the line key only when the scratchpad shows exactly that text. The screen
    /// after the line key (its scratchpad may hold the CDU's answer), or null: refused, the wipe
    /// failed, or the typing did not show (the scratchpad is wiped again).
    /// </summary>
    public Task<Bbs757CduScreen?> EnterAsync(int cdu, string text, string lineKey, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} entry at {lineKey}", null, ct, async () =>
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var keys = new List<string>();
            foreach (char c in text)
            {
                var forChar = Bbs757CduKeys.ForChar(c);
                if (forChar.Count == 0)
                {
                    Log.Debug("BBS757", $"CDU {cdu}: no key for '{c}', entry refused.");
                    return null;
                }
                keys.AddRange(forChar);
            }
            if (Refused(cdu, keys.Append(lineKey))) return null;
            if (!await WipeCore(cdu, ct)) return null;

            foreach (var key in keys) _link.PressKey(cdu, key, CoherentBbs757TabletClient.KeyHoldMs);
            await DrainAsync(cdu, keys.Count, ct);
            var typed = await ReadCore(cdu, ct);
            if (typed == null || Scratchpad(typed) != text.Trim().ToUpperInvariant())
            {
                // A CDU message covers the typing, or a key was lost: the line key would enter
                // something else.
                Log.Debug("BBS757", $"CDU {cdu}: the scratchpad does not show the typed entry; wiped, {lineKey} not pressed.");
                await WipeCore(cdu, ct);
                return null;
            }

            _link.PressKey(cdu, lineKey, CoherentBbs757TabletClient.KeyHoldMs);
            await _delay(KeySettleMs, ct);
            return await ReadCore(cdu, ct);
        });

    /// <summary>
    /// Presses <paramref name="keys"/> in order (DEL then a line key, PREV, EXEC …), then reads the
    /// screen once they have gone out and settled. Null when refused or unreadable.
    /// </summary>
    public Task<Bbs757CduScreen?> PressKeysAsync(int cdu, IReadOnlyList<string> keys, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} keys", null, ct, async () =>
        {
            if (Refused(cdu, keys)) return null;
            foreach (var key in keys) _link.PressKey(cdu, key, CoherentBbs757TabletClient.KeyHoldMs);
            await DrainAsync(cdu, keys.Count, ct);
            await _delay(KeySettleMs, ct);
            return await ReadCore(cdu, ct);
        });

    /// <summary>The scratchpad line (13), trimmed: an entry, a message, or "".</summary>
    public static string Scratchpad(Bbs757CduScreen screen) => screen.Lines[Bbs757CduFrame.ScratchpadRow].Trim();

    /// <summary>The page's name: the title without its page counter ("ACT RTE 1 LEGS").</summary>
    public static string Title(Bbs757CduScreen screen) => Bbs757CduTitle.PageName(screen.Lines[0]);

    /// <summary>The title's page counter, (1, 7) for "1/7"; null when it has none.</summary>
    public static (int Page, int Pages)? PageCount(Bbs757CduScreen screen) => Bbs757CduTitle.Counter(screen.Lines[0]);

    // ---- unlocked cores: called only inside the gate -----------------------------------------------

    private Task<Bbs757CduScreen?> ReadCore(int cdu, CancellationToken ct) => _link.ScrapeAsync(cdu, ct);

    private async Task<bool> WipeCore(int cdu, CancellationToken ct)
    {
        _link.PressKey(cdu, Bbs757CduKeys.Clr, WipeHoldMs);
        await _delay(WipeSettleMs, ct);
        var screen = await ReadCore(cdu, ct);
        if (screen != null && Scratchpad(screen).Length == 0) return true;

        _link.PressKey(cdu, Bbs757CduKeys.Clr, CoherentBbs757TabletClient.KeyHoldMs);
        await _delay(ShortClrSettleMs, ct);
        screen = await ReadCore(cdu, ct);
        bool blank = screen != null && Scratchpad(screen).Length == 0;
        if (!blank) Log.Debug("BBS757", $"CDU {cdu}: the scratchpad did not clear.");
        return blank;
    }

    /// <summary>
    /// Waits until no key is queued or in its cycle, at most a key cycle per key pressed plus
    /// <see cref="DrainSlackMs"/>; false when they had not all gone by then.
    /// </summary>
    private async Task<bool> DrainAsync(int cdu, int keys, CancellationToken ct)
    {
        int budget = CoherentBbs757TabletClient.KeyCycleMs * keys + DrainSlackMs;
        for (int waited = 0; _link.PendingKeyCount > 0; waited += DrainPollMs)
        {
            if (waited >= budget)
            {
                Log.Debug("BBS757", $"CDU {cdu}: keys still queued after {budget} ms.");
                return false;
            }
            await _delay(DrainPollMs, ct);
        }
        return true;
    }

    /// <summary>
    /// Whether keys may not be pressed now, logging why: the tablet view cannot be reached, keys are
    /// still queued (the pilot may be typing in the CDU window), or one is not a CDU key.
    /// </summary>
    private bool Refused(int cdu, IEnumerable<string> keys)
    {
        string? why = null;
        if (!_link.IsReadable) why = "the tablet view cannot be reached";
        else if (_link.PendingKeyCount > 0) why = "keys are still queued";
        else
        {
            foreach (var key in keys)
            {
                if (Bbs757CduKeys.IsKey(key)) continue;
                why = $"{key} is not a CDU key";
                break;
            }
        }
        if (why == null) return false;
        Log.Debug("BBS757", $"CDU {cdu}: refused, {why}.");
        return true;
    }

    /// <summary>
    /// Runs <paramref name="body"/> inside the gate, waiting at most <see cref="GateWaitMs"/> for it;
    /// <paramref name="failed"/> when the gate stays taken, <paramref name="ct"/> fires, or anything throws.
    /// </summary>
    private async Task<T> Gated<T>(string what, T failed, CancellationToken ct, Func<Task<T>> body)
    {
        bool entered = false;
        try
        {
            entered = await _gate.WaitAsync(GateWaitMs, ct);
            if (!entered)
            {
                Log.Debug("BBS757", $"{what}: another CDU call is still running, refused.");
                return failed;
            }
            return await body();
        }
        catch (OperationCanceledException)
        {
            Log.Debug("BBS757", $"{what}: canceled.");
            return failed;
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"{what} failed: {ex.GetType().Name}: {ex.Message}");
            return failed;
        }
        finally
        {
            if (entered) _gate.Release();
        }
    }
}
