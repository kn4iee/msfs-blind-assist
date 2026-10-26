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
/// <c>wasm_gauge=ils_cont</c> in one of the aircraft's VCockpit pages, drawn as an
/// <c>&lt;img src="WasmGaugeLiveView_N"&gt;</c> (N changes with the load). A live view resolves in any Coherent
/// view, so a read lays a copy of it over the simulator's 2D UI page (<c>mainUi</c>, drawn over the whole window
/// whatever the camera does): a fixed black box at CSS (16,16), 668 × 93, on top of everything, never taking the
/// mouse, the image filtered to white segments on black. Then it captures the window
/// (<see cref="ScreenshotService"/>: <c>canvas.toDataURL</c> of a live view is empty and Coherent has no
/// screenshot call, so the window capture is the only way to the pixels), crops the box
/// (<see cref="Bbs757IlsOcr.OverlayCrop"/>, from the page's size against the capture's) and decodes it
/// (<see cref="Bbs757IlsOcr.Decode"/>), then removes the box.
///
/// A sighted viewer sees that black box with white digits in the top-left corner for about a tenth of a
/// second per read (round 5's raw captures: about 0.13 s; the app's capture also encodes a PNG, which may hold
/// it a little longer, unmeasured). It never shows on a cockpit display.
///
/// The read: the box goes up, ~150 ms (round 5: it was in a capture ending 134-153 ms after the inject), then
/// up to <see cref="MaxCaptures"/> captures about 50 ms apart until one decodes; at most
/// <see cref="ReadCapMs"/> in all; the box is removed in a <c>finally</c>, on a token of its own, whatever
/// happened (failure, timeout, the caller's cancellation), and the next inject removes any stale box of the same
/// id first. Any failure is a null read and a <c>Log.Debug</c>. One read at a time.
///
/// Coherent ([DBG-9]: one inspector socket per view). The <c>mainUi</c> socket stays open while the reader
/// lives (the definition disposes it on a switch away, with the radio windows), so a read costs about 0.17 s
/// rather than 0.24 s; the view is claimed through <see cref="CoherentViewOwnership"/> from the first connect to
/// <see cref="Dispose"/>; a socket that fails is aborted and disposed before one new connection is tried
/// ([A380C-2]). The gauge is found once per load by a one-shot socket on each VCockpit page in turn, by id from
/// <c>/pagelist.json</c> (never hardcoded; <see cref="CoherentEvalClient"/> resolves by a title substring, and
/// "VCockpit1" is a substring of "VCockpit13 - BBSTablet"), never on a page the 757's persistent clients hold
/// (the tablet's BBSTablet page and the EFB's BBSNavigraphMap page: <see cref="IsProbeable"/>), each entered as a
/// one-shot (<see cref="CoherentViewOwnership.TryEnterOneShot"/>). Every Coherent call is async; the capture runs
/// on its own thread (<see cref="ScreenshotService.CaptureAsync"/>) and the decode on the pool.
/// </summary>
public sealed class Bbs757IlsDisplay : IBbs757IlsDisplay
{
    private const string DebuggerBase = "http://127.0.0.1:19999";

    /// <summary>The simulator's 2D UI page's title (<c>coui://html_UI/Global/mainUI.html</c>).</summary>
    internal const string ViewNeedle = "mainUi";
    private const string ViewUrl = "html_UI/Global/mainUI.html";

    /// <summary>The overlay element's id in <c>mainUi</c>.</summary>
    internal const string OverlayId = "msfsba_ils_ocr";

    /// <summary>White segments on black, the unlit ghosts gone (round 5: 99.4 % of the box pure black or white).</summary>
    internal const string Filter = "grayscale(1) brightness(2.6) contrast(40)";

    private const int SettleMs = 150;
    private const int CaptureGapMs = 50;
    internal const int MaxCaptures = 4;
    internal const int ReadCapMs = 1000;
    private const int FindCapMs = 1500;
    private const int RemoveTimeoutMs = 1000;
    private const int ConnectTimeoutMs = 1000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly Regex LiveViewName = new(@"^WasmGaugeLiveView_\d+$", RegexOptions.CultureInvariant);

    private readonly Func<Task<byte[]?>> _capture;
    private readonly Func<Rectangle?> _clientRegion;
    private readonly SemaphoreSlim _reads = new(1, 1);
    private readonly object _gate = new();
    private Inspector? _mainUi;
    private IDisposable? _claim;
    // Written by the read (one at a time) and cleared by ForgetGauge from the definition's thread.
    private volatile string? _liveView;
    private string _lastPageSize = "";
    private bool _disposed;

    public Bbs757IlsDisplay() : this(new ScreenshotService().CaptureAsync, SimClientRegion)
    {
    }

    /// <param name="capture">The window capture as PNG bytes (the whole window rectangle).</param>
    /// <param name="clientRegion">Where the client area lies in that capture; null: the whole capture.</param>
    internal Bbs757IlsDisplay(Func<Task<byte[]?>> capture, Func<Rectangle?> clientRegion)
    {
        _capture = capture;
        _clientRegion = clientRegion;
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
            string? liveView = _liveView ?? await FindGaugeAsync(ct).ConfigureAwait(false);
            if (liveView == null)
            {
                Log.Debug("BBS757", "ILS display: the ILS gauge (wasm_gauge=ils_cont) was not found in the cockpit views.");
                return null;
            }
            _liveView = liveView;

            var reading = await ReadGaugeAsync(liveView, ct).ConfigureAwait(false);
            // A load may have numbered the live view anew: it is looked for again next time.
            if (reading == null && !ct.IsCancellationRequested) _liveView = null;
            return reading;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("BBS757", $"ILS display read failed: {ex.GetType().Name}: {ex.Message}");
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
    /// the box removed in the <c>finally</c> on its own token.
    /// </summary>
    private async Task<Bbs757IlsReading?> ReadGaugeAsync(string liveView, CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(ReadCapMs);
        var token = cap.Token;

        // Where the client area lies in the capture, before the box goes up (it does not keep it up longer).
        Rectangle? client = await Task.Run(_clientRegion, token).ConfigureAwait(false);

        bool injected = false;
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
            if (answer == null || !TryPageSize(answer, out double innerWidth, out double innerHeight, out double ratio))
            {
                Log.Debug("BBS757", $"ILS display: the overlay was not laid ({(answer == null ? "no answer from mainUi" : "answer " + Shorten(answer))}).");
                return null;
            }
            LogPageSize(innerWidth, innerHeight, ratio, client);

            await Task.Delay(SettleMs, token).ConfigureAwait(false);
            for (int i = 0; i < MaxCaptures; i++)
            {
                if (i > 0) await Task.Delay(CaptureGapMs, token).ConfigureAwait(false);
                byte[]? png = await _capture().WaitAsync(token).ConfigureAwait(false);
                if (png == null)
                {
                    Log.Debug("BBS757", "ILS display: the simulator window could not be captured.");
                    return null;
                }
                var (cropped, reading) = await Task.Run(() => DecodeCapture(png, client, innerWidth, innerHeight), token).ConfigureAwait(false);
                if (!cropped)
                {
                    Log.Debug("BBS757", "ILS display: the overlay's box does not fit the capture at the page's scale.");
                    return null;
                }
                if (reading != null) return reading;
            }
            Log.Debug("BBS757", $"ILS display: none of {MaxCaptures} captures decoded.");
            return null;
        }
        finally
        {
            if (injected) await RemoveAsync().ConfigureAwait(false);
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
            catch (Exception ex)
            {
                // The page list or the connect failed, or the time ran out: one more try while there is time.
                Log.Debug("BBS757", $"ILS display: the overlay's removal failed: {ex.GetType().Name}");
            }
        }
        Log.Warn("BBS757", "ILS display: the overlay could not be removed now; the next read removes it first.");
    }

    /// <summary>A capture's box decoded: whether the box fits the capture at the page's scale, and the reading (null: not a valid display).</summary>
    private static (bool Cropped, Bbs757IlsReading? Reading) DecodeCapture(byte[] png, Rectangle? client, double innerWidth, double innerHeight)
    {
        using var stream = new MemoryStream(png);
        using var frame = new Bitmap(stream);
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
        Log.Debug("BBS757", $"ILS display: {size}.");
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

        var page = await Inspector.OpenAsync(mainUi.Id, ct).ConfigureAwait(false);
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
    /// (<see cref="IsProbeable"/>) through a one-shot socket; null when none holds it.
    /// </summary>
    private static async Task<string?> FindGaugeAsync(CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(FindCapMs);
        foreach (var (id, title, _) in await PageListAsync(cap.Token).ConfigureAwait(false))
        {
            if (!IsProbeable(title)) continue;
            using var oneShot = CoherentViewOwnership.TryEnterOneShot(title);
            if (oneShot == null) continue;
            using var page = await Inspector.OpenAsync(id, cap.Token).ConfigureAwait(false);
            if (page == null) continue;
            string? found = await page.EvalAsync(FindGaugeScript, cap.Token).ConfigureAwait(false);
            if (found != null && LiveViewName.IsMatch(found))
            {
                Log.Debug("BBS757", $"ILS display: the ILS gauge is {found} in '{title}'.");
                return found;
            }
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

    private static async Task<List<(int Id, string Title, string Url)>> PageListAsync(CancellationToken ct)
    {
        var pages = new List<(int, string, string)>();
        string json = await Http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct).ConfigureAwait(false);
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
            + "pointer-events:none;margin:0;padding:0;border:0;filter:" + Filter + ";';"
            + "host.appendChild(im);(document.body||document.documentElement).appendChild(host);"
            + "return JSON.stringify({iw:window.innerWidth,ih:window.innerHeight,dpr:window.devicePixelRatio});})()";
    }

    /// <summary>Removes every box of <see cref="OverlayId"/>; answers how many it removed.</summary>
    internal const string RemoveScript =
        "(function(){var n=0,e;while((e=document.getElementById('" + OverlayId + "'))){e.parentNode.removeChild(e);n++;}return String(n);})()";

    /// <summary>
    /// In a VCockpit page: the live view name of the <c>&lt;img&gt;</c> inside the <c>wasm-instrument</c> whose
    /// <c>url</c> holds <c>wasm_gauge=ils_cont</c>, or "" when the page has none.
    /// </summary>
    internal const string FindGaugeScript = """
        (function(){var a=document.querySelectorAll('wasm-instrument');
        for(var i=0;i<a.length;i++){var u=a[i].getAttribute('url')||'';
        if(!/[?&]wasm_gauge=ils_cont(&|$)/.test(u))continue;
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
    /// Where the simulator's client area lies in a <see cref="ScreenshotService"/> capture, whose picture is the
    /// whole window rectangle (its frame too, on a windowed simulator); null when Windows will not say, and the
    /// capture is then taken as the client area (<see cref="Bbs757IlsOcr.OverlayCrop"/> checks that it can be).
    /// Per-monitor DPI aware (V2) as the capture is, on the calling thread only and put back before it returns:
    /// run it on a thread of its own, never across an await.
    /// </summary>
    internal static Rectangle? SimClientRegion()
    {
        IntPtr previous = IntPtr.Zero;
        try
        {
            previous = SetThreadDpiAwarenessContext((IntPtr)(-4));   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch (EntryPointNotFoundException)
        {
            // Before Windows 10 1607: the capture is not per-monitor aware either.
        }

        try
        {
            IntPtr hwnd = FindSimWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT window) || !GetClientRect(hwnd, out RECT client)) return null;
            var origin = new POINT();
            if (!ClientToScreen(hwnd, ref origin)) return null;
            int width = client.Right - client.Left, height = client.Bottom - client.Top;
            return width > 0 && height > 0 ? new Rectangle(origin.X - window.Left, origin.Y - window.Top, width, height) : null;
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

        /// <summary>A new socket on the page; null when it cannot be had. Throws only when <paramref name="ct"/> fires.</summary>
        public static async Task<Inspector?> OpenAsync(int pageId, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connect.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.ToString(CultureInfo.InvariantCulture)}"),
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
