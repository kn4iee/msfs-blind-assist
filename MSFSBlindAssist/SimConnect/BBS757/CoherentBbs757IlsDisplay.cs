using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>The ILS control panel's display as the 757's definition reads it (a fake in the tests).</summary>
public interface IBbs757IlsDisplay : IDisposable
{
    /// <summary>
    /// What the display shows now; null when it could not be read, the read was canceled or the reader is
    /// disposed. Never throws.
    /// </summary>
    Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct);

    /// <summary>Forgets where the gauge was found: a flight load may number its live view anew.</summary>
    void ForgetGauge();
}

/// <summary>
/// Reads the BlueBird 757's ILS control panel display, which no variable mirrors and no instrument view
/// shows (docs/b757/evidence/flight-04.md, "Round 5: reading the ILS display"). The panel is the WASM gauge
/// <c>wasm_gauge=ils_control_lcd</c> in one of the aircraft's VCockpit pages, drawn as an
/// <c>&lt;img src="WasmGaugeLiveView_N"&gt;</c> (N changes with the load). A live view resolves in any Coherent
/// view, so a read lays a copy of it over the simulator's 2D UI page (<c>mainUi</c>, drawn over the whole window
/// whatever the camera does): a fixed black box at CSS (16,16), 668 × 93, on top of everything, never taking the
/// mouse, the image as the gauge draws it. Then it captures the window (<see cref="CaptureFrameAsync"/>:
/// <c>canvas.toDataURL</c> of a live view is empty and Coherent has no screenshot call, so the window capture is
/// the only way to the pixels), crops the box (<see cref="Bbs757IlsOcr.OverlayCrop"/>, from the page's size
/// against the capture's) and decodes it (<see cref="Bbs757IlsOcr.Decode"/>), then removes the box.
///
/// No CSS filter, because the panel dims at night (flight-04.md, "Round 6", 2026-10-27, a 3840 × 2160 sim at
/// dusk): its lit segments peaked at grey 63, round 5's daylight at 64-95. Round 5's filter
/// (<c>grayscale(1) brightness(2.6) contrast(40)</c>) turned every pixel under about 48 black, which shrank the
/// decimal point inside the decoder's core: every read that night was refused (clarity 0.39). The decoder's
/// own Otsu threshold follows the panel's brightness (45 in round 5's daylight, 29 that night) and read the
/// same display unfiltered at clarity 1.00.
///
/// A sighted viewer sees that box, the panel's grey digits on its dark ground, in the top-left corner for
/// about a tenth of a second per read (round 5's raw captures: about 0.13 s). It never shows on a cockpit
/// display.
///
/// The read: the box goes up, ~150 ms (round 5: it was in a capture ending 134-153 ms after the inject), then
/// up to <see cref="MaxCaptures"/> captures about 50 ms apart until one decodes; at most
/// <see cref="ReadCapMs"/> in all; the box is removed in a <c>finally</c>, on a token of its own, whatever
/// happened (failure, timeout, the caller's cancellation), and the next inject removes any stale box of the same
/// id first. Any failure is a null read and a <c>Log.Debug</c>; a timeout of the reader's own (the read's cap, the
/// gauge search's, the removal's, an HTTP timeout) says where it was: "ILS display: timed out (gauge search)",
/// "(inject)", "(capture/decode)", "(removal)". The caller's own cancellation is silent. One read at a time.
///
/// Coherent ([DBG-9]: one inspector socket per view). The <c>mainUi</c> socket stays open while the reader
/// lives (the definition disposes it on a switch away, with the radio windows), so a read costs about 0.17 s
/// rather than 0.24 s; the view is claimed through <see cref="CoherentViewOwnership"/> from the first connect to
/// <see cref="Dispose"/>; a socket that fails is aborted and disposed before one new connection is tried
/// ([A380C-2]). The gauge is found once per load by a one-shot socket on each VCockpit page in turn, by id from
/// <c>/pagelist.json</c> (never hardcoded; <see cref="CoherentEvalClient"/> resolves by a title substring, and
/// "VCockpit1" is a substring of "VCockpit13 - BBSTablet"), never on a page the 757's persistent clients hold
/// (the tablet's BBSTablet page and the EFB's BBSNavigraphMap page: <see cref="IsProbeable"/>), each entered as a
/// one-shot (<see cref="CoherentViewOwnership.TryEnterOneShot"/>). Every Coherent call is async; each capture runs
/// on its own thread (<see cref="CaptureFrameAsync"/>) and the decode on the pool.
/// </summary>
public sealed class CoherentBbs757IlsDisplay : IBbs757IlsDisplay
{
    /// <summary>The Coherent GT debugger the simulator opens.</summary>
    private const string Debugger = "127.0.0.1:19999";

    /// <summary>The simulator's 2D UI page's title (<c>coui://html_UI/Global/mainUI.html</c>).</summary>
    internal const string ViewNeedle = "mainUi";
    private const string ViewUrl = "html_UI/Global/mainUI.html";

    /// <summary>The overlay element's id in <c>mainUi</c>.</summary>
    internal const string OverlayId = "msfsba_ils_ocr";

    private const int SettleMs = 150;
    private const int CaptureGapMs = 50;
    internal const int MaxCaptures = 4;
    internal const int ReadCapMs = 1000;
    private const int FindCapMs = 1500;
    private const int RemoveTimeoutMs = 1000;
    private const int ConnectTimeoutMs = 1000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly Regex LiveViewName = new(@"^WasmGaugeLiveView_\d+$", RegexOptions.CultureInvariant);

    private readonly Func<Task<Bitmap?>> _capture;
    private readonly Func<Rectangle?> _clientRegion;
    private readonly string _debugger;
    private readonly Action<string> _debug;
    private readonly SemaphoreSlim _reads = new(1, 1);
    private readonly object _gate = new();
    private Inspector? _mainUi;
    private IDisposable? _claim;
    // Written by the read (one at a time) and cleared by ForgetGauge from the definition's thread.
    private volatile string? _liveView;
    private string _lastPageSize = "";
    private bool _disposed;

    /// <summary>Where the read is, for the log line of a read that times out ("gauge search", "inject", "capture/decode").</summary>
    private string _phase = "";

    public CoherentBbs757IlsDisplay() : this(CaptureFrameAsync, SimClientRegion)
    {
    }

    /// <param name="capture">The window capture (the whole window rectangle); the reader disposes each frame.</param>
    /// <param name="clientRegion">Where the client area lies in that capture; null: the whole capture.</param>
    /// <param name="debugger">The Coherent debugger's host and port (tests: a local listener, never the sim's).</param>
    /// <param name="debug">Where this reader's own debug lines go (tests); null: debug.log.</param>
    internal CoherentBbs757IlsDisplay(Func<Task<Bitmap?>> capture, Func<Rectangle?> clientRegion,
        string debugger = Debugger, Action<string>? debug = null)
    {
        _capture = capture;
        _clientRegion = clientRegion;
        _debugger = debugger;
        _debug = debug ?? (message => Log.Debug("BBS757", message));
    }

    private bool IsDisposed
    {
        get { lock (_gate) return _disposed; }
    }

    public void ForgetGauge() => _liveView = null;

    public async Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct)
    {
        if (IsDisposed) return null;
        try
        {
            await _reads.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            if (IsDisposed) return null;
            _phase = "gauge search";
            string? liveView = _liveView ?? await FindGaugeAsync(ct).ConfigureAwait(false);
            if (liveView == null)
            {
                _debug("ILS display: the ILS gauge (wasm_gauge=ils_control_lcd) was not found in the cockpit views.");
                return null;
            }
            _liveView = liveView;

            var reading = await ReadGaugeAsync(liveView, ct).ConfigureAwait(false);
            // A load may have numbered the live view anew: it is looked for again next time.
            if (reading == null && !ct.IsCancellationRequested) _liveView = null;
            return reading;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A cap of this reader's own (the read's, the gauge search's) or an HTTP timeout, not the caller.
            _debug($"ILS display: timed out ({_phase}).");
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;   // the caller's own cancellation (a reset, a switch away): silent
        }
        catch (Exception ex)
        {
            _debug($"ILS display read failed ({_phase}): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _reads.Release();
            if (IsDisposed) CloseMainUi();   // a Dispose during the read left the socket for the box's removal
        }
    }

    /// <summary>
    /// One read of the gauge through the overlay, within <see cref="ReadCapMs"/>: the box up, the captures,
    /// the box removed in the <c>finally</c> on its own token, then a failed read's last capture saved
    /// (<see cref="SaveFailedCapture"/>).
    /// </summary>
    private async Task<Bbs757IlsReading?> ReadGaugeAsync(string liveView, CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(ReadCapMs);
        var token = cap.Token;
        _phase = "inject";

        // Where the client area lies in the capture, before the box goes up (it does not keep it up longer).
        Rectangle? client = await Task.Run(_clientRegion, token).ConfigureAwait(false);

        bool injected = false;
        Bitmap? failed = null;   // a failed read's last capture, saved once the box is down
        double pageWidth = 0, pageHeight = 0;
        try
        {
            string inject = InjectScript(liveView);
            string? answer = null;
            for (int attempt = 0; attempt < 2 && answer == null; attempt++)   // one reconnect when the socket fails
            {
                var page = await MainUiAsync(fresh: attempt > 0, token).ConfigureAwait(false);
                if (page == null) break;
                injected = true;   // sent: it may land even if no answer comes
                answer = await page.EvalAsync(inject, token).ConfigureAwait(false);
            }
            if (answer == null) token.ThrowIfCancellationRequested();   // no answer in time: a timeout, said as one
            if (answer == null || !TryPageSize(answer, out double innerWidth, out double innerHeight, out double ratio))
            {
                _debug($"ILS display: the overlay was not laid ({(answer == null ? "no answer from mainUi" : "answer " + Shorten(answer))}).");
                return null;
            }
            LogPageSize(innerWidth, innerHeight, ratio, client);
            (pageWidth, pageHeight) = (innerWidth, innerHeight);

            _phase = "capture/decode";
            Bitmap? last = null;   // the latest capture, disposed when the next one comes or the read ends
            var started = Stopwatch.StartNew();
            try
            {
                await Task.Delay(SettleMs, token).ConfigureAwait(false);
                for (int i = 0; i < MaxCaptures; i++)
                {
                    if (i > 0) await Task.Delay(CaptureGapMs, token).ConfigureAwait(false);
                    long before = started.ElapsedMilliseconds;
                    if (await CaptureWithinAsync(token).ConfigureAwait(false) is not Bitmap frame)
                    {
                        _debug("ILS display: the simulator window could not be captured.");
                        return null;
                    }
                    last?.Dispose();
                    last = frame;
                    var (cropped, reading) = await Task.Run(() => DecodeCapture(frame, client, innerWidth, innerHeight), token).ConfigureAwait(false);
                    _debug(string.Create(CultureInfo.InvariantCulture,
                        $"ILS display: capture {i + 1} at {before} ms took {started.ElapsedMilliseconds - before} ms with its decode: {(reading != null ? "read" : cropped ? "no reading" : "box off the capture")}."));
                    if (!cropped)
                    {
                        _debug("ILS display: the overlay's box does not fit the capture at the page's scale.");
                        (failed, last) = (last, null);
                        return null;
                    }
                    if (reading != null) return reading;
                }
                _debug($"ILS display: none of {MaxCaptures} captures decoded.");
                (failed, last) = (last, null);
                return null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The read's own cap ran out mid-capture: keep what the last capture saw. Said as a timeout above.
                if (last == null) _debug("ILS display: no capture finished before the read gave up; nothing saved.");
                (failed, last) = (last, null);
                throw;
            }
            finally
            {
                last?.Dispose();
            }
        }
        finally
        {
            if (injected) await RemoveAsync().ConfigureAwait(false);
            if (failed != null) SaveFailedCapture(failed, client, pageWidth, pageHeight);
        }
    }

    /// <summary>One capture, waited for within the read's cap; a frame that comes after the cap is disposed, not leaked.</summary>
    private async Task<Bitmap?> CaptureWithinAsync(CancellationToken token)
    {
        var pending = _capture();
        try
        {
            return await pending.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = pending.ContinueWith(t => t.Result?.Dispose(), CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>
    /// Removes the box, on a token of its own (never the caller's: a canceled read still takes it down),
    /// reconnecting once when the socket fails. A box that cannot be removed is logged; the next inject
    /// removes it first.
    /// </summary>
    private async Task RemoveAsync()
    {
        using var timeout = new CancellationTokenSource(RemoveTimeoutMs);
        for (int attempt = 0; attempt < 2 && !timeout.IsCancellationRequested; attempt++)
        {
            try
            {
                var page = await MainUiAsync(fresh: attempt > 0, timeout.Token).ConfigureAwait(false);
                if (page != null && await page.EvalAsync(RemoveScript, timeout.Token).ConfigureAwait(false) != null) return;
            }
            catch (Exception ex) when (!timeout.IsCancellationRequested)
            {
                // The page list or the connect failed: one more try while there is time.
                _debug($"ILS display: the overlay's removal failed: {ex.GetType().Name}");
            }
            catch (Exception)
            {
                // The time ran out: said below.
            }
        }
        if (timeout.IsCancellationRequested) _debug("ILS display: timed out (removal).");
        Log.Warn("BBS757", "ILS display: the overlay could not be removed now; the next read removes it first.");
    }

    /// <summary>
    /// A failed read's last capture, for working out why (2026-10-27: every read timed out on a 1024 × 576 CSS
    /// mainUi in a 1920 × 1080 window): the whole capture as ils-display-fail.png and the overlay's box as
    /// ils-display-fail-crop.png in the logs folder, each overwritten by the next failure. Saved on the pool, after
    /// the box is down (a 3840 × 2160 PNG takes about 0.3 s to encode); takes <paramref name="frame"/> and disposes
    /// it. Never throws.
    /// </summary>
    private void SaveFailedCapture(Bitmap frame, Rectangle? client, double innerWidth, double innerHeight)
    {
        _ = Task.Run(() =>
        {
            try
            {
                string whole = AppLogs.PathFor("ils-display-fail.png");
                frame.Save(whole, ImageFormat.Png);
                string saved = whole;
                if (Bbs757IlsOcr.OverlayCrop(frame.Size, client, innerWidth, innerHeight) is Rectangle box)
                {
                    string crop = AppLogs.PathFor("ils-display-fail-crop.png");
                    using var cut = frame.Clone(box, PixelFormat.Format32bppArgb);
                    cut.Save(crop, ImageFormat.Png);
                    saved += $" and {crop} ({box.Width}x{box.Height} at {box.X},{box.Y})";
                }
                _debug($"ILS display: saved the last capture ({frame.Width}x{frame.Height}) to {saved}.");
            }
            catch (Exception ex)
            {
                _debug($"ILS display: the failed capture could not be saved: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                frame.Dispose();
            }
        });
    }

    /// <summary>A capture's box decoded: whether the box fits the capture at the page's scale, and the reading (null: not a valid display).</summary>
    private static (bool Cropped, Bbs757IlsReading? Reading) DecodeCapture(Bitmap frame, Rectangle? client, double innerWidth, double innerHeight)
    {
        if (Bbs757IlsOcr.OverlayCrop(frame.Size, client, innerWidth, innerHeight) is not Rectangle box) return (false, null);
        using var crop = frame.Clone(box, PixelFormat.Format32bppArgb);
        return (true, Bbs757IlsOcr.Decode(crop));
    }

    /// <summary>The page size the inject answered, logged when it changes (other resolutions, DPI and UI scales are unmeasured).</summary>
    private void LogPageSize(double innerWidth, double innerHeight, double ratio, Rectangle? client)
    {
        string size = string.Create(CultureInfo.InvariantCulture,
            $"mainUi {innerWidth}x{innerHeight} CSS px, devicePixelRatio {ratio}, client {(client is Rectangle c ? $"{c.Width}x{c.Height} at {c.X},{c.Y}" : "unknown")}");
        if (size == _lastPageSize) return;
        _lastPageSize = size;
        _debug($"ILS display: {size}.");
    }

    // ---- Coherent ----------------------------------------------------------------------------------------

    /// <summary>
    /// The open <c>mainUi</c> socket, or a new one (<paramref name="fresh"/>: the old one failed). The old socket
    /// goes before a new one opens; the view is claimed from the first connect to <see cref="Dispose"/>. Null when
    /// the page cannot be had, or a one-shot holds it.
    /// </summary>
    private async Task<Inspector?> MainUiAsync(bool fresh, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!fresh && _mainUi is { IsOpen: true } open) return open;
        }
        DropMainUi();
        lock (_gate) _claim ??= CoherentViewOwnership.Claim(ViewNeedle);
        if (CoherentViewOwnership.OneShotInFlight(ViewNeedle)) return null;

        var pages = await PageListAsync(ct).ConfigureAwait(false);
        var mainUi = pages.FirstOrDefault(IsMainUi);
        if (mainUi.Title == null) return null;

        var page = await Inspector.ConnectAsync(_debugger, mainUi.Id, ct).ConfigureAwait(false);
        if (page == null) return null;
        lock (_gate) _mainUi = page;
        return page;
    }

    private static bool IsMainUi((int Id, string Title, string Url) page)
        => page.Title != null
           && (string.Equals(page.Title, ViewNeedle, StringComparison.OrdinalIgnoreCase)
               || page.Url.Contains(ViewUrl, StringComparison.OrdinalIgnoreCase));

    private void DropMainUi()
    {
        Inspector? old;
        lock (_gate)
        {
            old = _mainUi;
            _mainUi = null;
        }
        old?.Dispose();
    }

    private void CloseMainUi()
    {
        IDisposable? claim;
        lock (_gate)
        {
            claim = _claim;
            _claim = null;
        }
        DropMainUi();
        claim?.Dispose();
    }

    /// <summary>
    /// The gauge's live view name ("WasmGaugeLiveView_12"), looked for in each VCockpit page this client may open
    /// (<see cref="IsProbeable"/>) through a one-shot socket, within <see cref="FindCapMs"/>; null when none holds
    /// it. Throws when the cap or <paramref name="ct"/> fires (an unanswered probe included).
    /// </summary>
    private async Task<string?> FindGaugeAsync(CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(FindCapMs);
        foreach (var (id, title, _) in await PageListAsync(cap.Token).ConfigureAwait(false))
        {
            if (!IsProbeable(title)) continue;
            using var oneShot = CoherentViewOwnership.TryEnterOneShot(title);
            if (oneShot == null) continue;
            using var page = await Inspector.ConnectAsync(_debugger, id, cap.Token).ConfigureAwait(false);
            if (page == null) continue;
            string? found = await page.EvalAsync(FindGaugeScript, cap.Token).ConfigureAwait(false);
            if (found != null && LiveViewName.IsMatch(found))
            {
                _debug($"ILS display: the ILS gauge is {found} in '{title}'.");
                return found;
            }
            cap.Token.ThrowIfCancellationRequested();   // a probe left unanswered by the cap: a timeout, said as one
        }
        return null;
    }

    /// <summary>
    /// A page the gauge search may open a socket on: a VCockpit page, never the BBSTablet or BBSNavigraphMap
    /// page the 757's persistent clients hold ([DBG-9]). Pure.
    /// </summary>
    internal static bool IsProbeable(string title)
        => title.StartsWith("VCockpit", StringComparison.OrdinalIgnoreCase)
           && !title.Contains(CoherentBbs757TabletClient.ViewNeedle, StringComparison.OrdinalIgnoreCase)
           && !title.Contains(CoherentBbs757EfbClient.ViewNeedle, StringComparison.OrdinalIgnoreCase);

    private async Task<List<(int Id, string Title, string Url)>> PageListAsync(CancellationToken ct)
    {
        var pages = new List<(int, string, string)>();
        string json = await Http.GetStringAsync($"http://{_debugger}/pagelist.json", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        foreach (var view in doc.RootElement.EnumerateArray())
        {
            if (!view.TryGetProperty("id", out var idEl)) continue;
            int id;
            if (idEl.ValueKind == JsonValueKind.Number) id = idEl.GetInt32();
            else if (!int.TryParse(idEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
            string title = view.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            string url = view.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            pages.Add((id, title, url));
        }
        return pages;
    }

    private static bool TryPageSize(string answer, out double innerWidth, out double innerHeight, out double ratio)
    {
        innerWidth = innerHeight = ratio = 0;
        try
        {
            using var doc = JsonDocument.Parse(answer);
            var root = doc.RootElement;
            innerWidth = root.GetProperty("iw").GetDouble();
            innerHeight = root.GetProperty("ih").GetDouble();
            ratio = root.TryGetProperty("dpr", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
            return innerWidth > 0 && innerHeight > 0;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120];

    // ---- The scripts (pure) ------------------------------------------------------------------------------

    /// <summary>
    /// Lays the box over <c>mainUi</c> (any stale box of the same id removed first) holding the live view
    /// <paramref name="liveView"/>, and answers the page's size: <c>{"iw":…,"ih":…,"dpr":…}</c>.
    /// </summary>
    internal static string InjectScript(string liveView)
    {
        string left = Bbs757IlsOcr.OverlayLeft.ToString(CultureInfo.InvariantCulture);
        string top = Bbs757IlsOcr.OverlayTop.ToString(CultureInfo.InvariantCulture);
        string width = Bbs757IlsOcr.Width.ToString(CultureInfo.InvariantCulture);
        string height = Bbs757IlsOcr.Height.ToString(CultureInfo.InvariantCulture);
        return "(function(){var id='" + OverlayId + "',e;"
            + "while((e=document.getElementById(id)))e.parentNode.removeChild(e);"
            + "var host=document.createElement('div');host.id=id;"
            + "host.style.cssText='position:fixed;left:" + left + "px;top:" + top + "px;width:" + width + "px;height:" + height + "px;"
            + "background:#000;z-index:2147483647;pointer-events:none;margin:0;padding:0;border:0;display:block;visibility:visible;opacity:1;transform:none;';"
            + "var im=document.createElement('img');im.src=" + JsonSerializer.Serialize(liveView) + ";"
            + "im.style.cssText='position:absolute;left:0px;top:0px;width:" + width + "px;height:" + height + "px;"
            + "pointer-events:none;margin:0;padding:0;border:0;';"
            + "host.appendChild(im);(document.body||document.documentElement).appendChild(host);"
            + "return JSON.stringify({iw:window.innerWidth,ih:window.innerHeight,dpr:window.devicePixelRatio});})()";
    }

    /// <summary>Removes every box of <see cref="OverlayId"/>; answers how many it removed.</summary>
    internal const string RemoveScript =
        "(function(){var n=0,e;while((e=document.getElementById('" + OverlayId + "'))){e.parentNode.removeChild(e);n++;}return String(n);})()";

    /// <summary>
    /// In a VCockpit page: the live view name of the <c>&lt;img&gt;</c> inside the <c>wasm-instrument</c> whose
    /// <c>url</c> holds <c>wasm_gauge=ils_control_lcd</c> (the full name: a probe that cut urls short once
    /// reported "ils_cont", which matches nothing), or "" when the page has none.
    /// </summary>
    internal const string FindGaugeScript = """
        (function(){var a=document.querySelectorAll('wasm-instrument');
        for(var i=0;i<a.length;i++){var u=a[i].getAttribute('url')||'';
        if(!/[?&]wasm_gauge=ils_control_lcd(&|$)/.test(u))continue;
        var im=a[i].querySelector('img[src^="WasmGaugeLiveView_"]');if(im)return im.getAttribute('src');
        var all=a[i].querySelectorAll('img');
        for(var k=0;k<all.length;k++){var m=/WasmGaugeLiveView_\d+/.exec(all[k].getAttribute('src')||'');if(m)return m[0];}}
        return '';})()
        """;

    // ---- The window --------------------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Ask the compositor for the window's full content, as <see cref="ScreenshotService"/> does.</summary>
    private const uint PwRenderFullContent = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    /// <summary>
    /// The simulator's main window, kept while Windows still has it shown: finding it takes two process scans
    /// (<see cref="FindSimWindow"/>), and a read captures up to <see cref="MaxCaptures"/> times.
    /// </summary>
    private static volatile IntPtr _simWindow;

    /// <summary>
    /// Where the simulator's client area lies in a capture (<see cref="CaptureFrameAsync"/>), whose picture is the
    /// whole window rectangle (its frame too, on a windowed simulator); null when Windows will not say, and the
    /// capture is then taken as the client area (<see cref="Bbs757IlsOcr.OverlayCrop"/> checks that it can be).
    /// Per-monitor DPI aware (V2) as the capture is, on the calling thread only and put back before it returns:
    /// run it on a thread of its own, never across an await.
    /// </summary>
    internal static Rectangle? SimClientRegion()
    {
        IntPtr previous = EnterPerMonitorDpiAwareness();
        try
        {
            IntPtr hwnd = SimWindow();
            return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT window) ? ClientRegionIn(hwnd, window) : null;
        }
        catch (Exception ex)
        {
            Log.Debug("BBS757", $"ILS display: the simulator's client area could not be had: {ex.Message}");
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>
    /// The simulator window as a bitmap: the whole window rectangle, as <see cref="ScreenshotService"/> captures it,
    /// without the PNG encoding only the AI's upload needs. Round 6 (flight-04.md, a 3840 × 2160 sim): PrintWindow
    /// took 80-96 ms, the service's encode 310-320 ms more and this reader's decode back 41 ms, so a read had room
    /// for one capture in its <see cref="ReadCapMs"/>; cropped in memory, a capture took 63-85 ms. PrintWindow runs
    /// on a thread of its own (into a simulator that has stopped pumping messages it never returns; the read's cap
    /// abandons the wait), and a frame it fails or hands back blank (<see cref="ScreenshotFrame.LooksBlank"/>) takes
    /// the service's own capture instead, with its screen-copy fallback. `Services/**` is frozen on the 757's
    /// branch: plan 04's handoff asks plan 16 for this capture in the service. Null when neither captures.
    /// </summary>
    internal static async Task<Bitmap?> CaptureFrameAsync()
    {
        if (await OnOwnThread(PrintSimWindow).ConfigureAwait(false) is Bitmap printed) return printed;
        byte[]? png = await new ScreenshotService().CaptureAsync().ConfigureAwait(false);
        if (png == null) return null;
        using var stream = new MemoryStream(png);
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);   // a copy: a bitmap read from a stream needs the stream for its whole life
    }

    /// <summary>The window by PrintWindow, per-monitor DPI aware on this thread only; null when it fails or looks blank.</summary>
    private static Bitmap? PrintSimWindow()
    {
        IntPtr previous = EnterPerMonitorDpiAwareness();
        Bitmap? frame = null;
        try
        {
            IntPtr hwnd = SimWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT window)) return null;
            int width = window.Right - window.Left, height = window.Bottom - window.Top;
            if (width <= 0 || height <= 0) return null;

            frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            bool rendered;
            using (var graphics = Graphics.FromImage(frame))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    rendered = PrintWindow(hwnd, hdc, PwRenderFullContent);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
            if (!rendered || ScreenshotFrame.LooksBlank(frame, ClientRegionIn(hwnd, window))) return null;

            var printed = frame;
            frame = null;
            return printed;
        }
        catch (Exception ex)
        {
            Log.Debug("BBS757", $"ILS display: PrintWindow failed, the screenshot service captures instead: {ex.Message}");
            return null;
        }
        finally
        {
            frame?.Dispose();
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>Runs <paramref name="work"/> on a background thread of its own, never the pool: a hung PrintWindow parks only it.</summary>
    private static Task<T?> OnOwnThread<T>(Func<T?> work) where T : class
    {
        var done = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try { done.TrySetResult(work()); }
            catch (Exception ex) { done.TrySetException(ex); }
        })
        {
            IsBackground = true,
            Name = "MSFSBA ILS display capture",
        }.Start();
        return done.Task;
    }

    /// <summary>The client area inside a picture of <paramref name="window"/>'s rectangle; null when Windows will not say.</summary>
    private static Rectangle? ClientRegionIn(IntPtr hwnd, RECT window)
    {
        if (!GetClientRect(hwnd, out RECT client)) return null;
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return null;
        int width = client.Right - client.Left, height = client.Bottom - client.Top;
        return width > 0 && height > 0 ? new Rectangle(origin.X - window.Left, origin.Y - window.Top, width, height) : null;
    }

    /// <summary>Makes the calling thread per-monitor DPI aware (V2); the context to put back, or zero when nothing changed.</summary>
    private static IntPtr EnterPerMonitorDpiAwareness()
    {
        try
        {
            return SetThreadDpiAwarenessContext((IntPtr)(-4));   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;   // before Windows 10 1607: the capture is not per-monitor aware either
        }
    }

    /// <summary>The simulator's main window: the one kept while Windows still has it shown, else found anew.</summary>
    private static IntPtr SimWindow()
    {
        IntPtr known = _simWindow;
        if (known != IntPtr.Zero && IsWindow(known) && IsWindowVisible(known)) return known;
        return _simWindow = FindSimWindow();
    }

    /// <summary>The simulator's main window, found as <see cref="ScreenshotService"/> finds it.</summary>
    private static IntPtr FindSimWindow()
    {
        string? name = SimulatorDetector.GetProcessName(SimulatorDetector.DetectRunningSimulator());
        if (string.IsNullOrEmpty(name)) return IntPtr.Zero;
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0 ? processes[0].MainWindowHandle : IntPtr.Zero;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    // ---- Lifetime ------------------------------------------------------------------------------------------

    /// <summary>
    /// Closes the <c>mainUi</c> socket and releases the view. A read running now removes its box first and
    /// closes the socket as it ends; reads after this answer null. Never throws.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (!_reads.Wait(0)) return;
        try
        {
            CloseMainUi();
        }
        finally
        {
            _reads.Release();
        }
    }

    /// <summary>
    /// One inspector socket on one page: <c>Runtime.evaluate</c>, one call at a time (the reads are serialized),
    /// its answer read inline. Any failure, a cancellation included (.NET aborts a socket whose receive is
    /// canceled), answers null, and the socket is of no further use.
    /// </summary>
    private sealed class Inspector : IDisposable
    {
        private readonly ClientWebSocket _ws;
        private int _id;

        private Inspector(ClientWebSocket ws) => _ws = ws;

        public bool IsOpen => _ws.State == WebSocketState.Open;

        /// <summary>
        /// A new socket on the page (callers drop any old one on the same view first, [A380C-2]); null when it
        /// cannot be had. Throws only when <paramref name="ct"/> fires.
        /// </summary>
        public static async Task<Inspector?> ConnectAsync(string debugger, int pageId, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connect.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(new Uri($"ws://{debugger}/devtools/inspector/{pageId.ToString(CultureInfo.InvariantCulture)}"),
                    connect.Token).ConfigureAwait(false);
                return new Inspector(ws);
            }
            catch (Exception ex)
            {
                Close(ws);
                ct.ThrowIfCancellationRequested();
                Log.Debug("BBS757", $"ILS display: connect to page {pageId} failed: {ex.GetType().Name}");
                return null;
            }
        }

        /// <summary>The expression's value as a string ("" for an answer with none); null when no answer came.</summary>
        public async Task<string?> EvalAsync(string expression, CancellationToken ct)
        {
            int id = ++_id;
            string message = JsonSerializer.Serialize(new
            {
                id,
                method = "Runtime.evaluate",
                @params = new { expression, returnByValue = true },
            });
            try
            {
                await _ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                var buffer = new byte[16384];
                using var received = new MemoryStream();
                while (true)
                {
                    received.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return null;
                        received.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    // Decoded once the message is whole: a character split across reads would be corrupted.
                    if (AnswerTo(Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length), id) is { } value) return value;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The value of the answer to request <paramref name="id"/>, or null for any other message.</summary>
        private static string? AnswerTo(string text, int id)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out int answered) || answered != id) return null;
                if (root.TryGetProperty("result", out var outer) && outer.TryGetProperty("result", out var inner)
                    && inner.TryGetProperty("value", out var value))
                    return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
                return "";
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public void Dispose() => Close(_ws);

        /// <summary>Abort AND dispose: Abort alone leaks the socket's native handle.</summary>
        private static void Close(ClientWebSocket ws)
        {
            try { ws.Abort(); } catch { /* already closed */ }
            try { ws.Dispose(); } catch { /* already disposed */ }
        }
    }
}
