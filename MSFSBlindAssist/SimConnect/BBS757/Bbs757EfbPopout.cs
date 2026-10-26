using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The pop-out window that lets the app SEE BlueBird's tablet.
///
/// The tablet (WASM gauge <c>efb_l</c>) is drawn natively: Coherent cannot read a canvas's pixels
/// (getImageData throws NotSupportedError, toDataURL returns "", no WebGL; measured 2026-10-26), and
/// no camera view shows the tablet. What works: the EFB agent's mirror lays the tablet's live image
/// (<c>WasmGaugeLiveView_N</c>) over the captain's ND gauge (<c>nd_l</c>) in the same VCockpit page,
/// and a pop-out window of that ND then shows the tablet at FULL brightness, whatever the aircraft's
/// power (the 3D screen is dark when unpowered; the pop-out is not). PrintWindow captures the
/// pop-out even parked off the desktop.
///
/// The pop-out is made once per session the way the A300/A330 CDU pop-outs are: the camera goes to
/// instrument view 0 (ADI/EHSI: the captain's PFD and ND), the sim is brought to the front, and a
/// Right-Alt + left click lands on the ND. The new window (title "WASMINSTRUMENT", client aspect
/// 704:859) is then parked off the desktop at exactly the tablet's width, so a capture needs no
/// scaling, and the camera is put back.
///
/// Measured in the probe on a 1278 x 1360 sim window: the ND's centre in instrument view 0 is at
/// (w/2 + 0.0154 h, 0.86 h). Vertical position scales with the window height (MSFS keeps the
/// vertical field of view); horizontal offset from the centre scales with the height too.
/// </summary>
public sealed class Bbs757EfbPopout
{
    /// <summary>The tablet's own size (efb_l), the coordinate space of every agent click.</summary>
    public const int TabletWidth = 1397, TabletHeight = 1048;
    /// <summary>The ND gauge (nd_l) the tablet is mirrored onto.</summary>
    public const int NdWidth = 704, NdHeight = 859;
    /// <summary>Instrument view 0 = ADI/EHSI (cameras.cfg; index is 0-based).</summary>
    public const int InstrumentViewType = 2, NdViewIndex = 0;

    private const double ClientAspect = (double)NdWidth / NdHeight;
    private const double AspectTolerance = 0.03;
    // The ND centre in instrument view 0, relative to the sim window's client height.
    private const double NdCentreXFromMid = 0.0154, NdCentreY = 0.86;

    private readonly Action<string> _log;
    private IntPtr _hwnd;

    public Bbs757EfbPopout(Action<string>? log = null) => _log = log ?? (_ => { });

    /// <summary>Last attempt's click points, for the log and the status line.</summary>
    public string LastDiagnostic { get; private set; } = "";

    public bool HasWindow => _hwnd != IntPtr.Zero && IsWindow(_hwnd);

    /// <summary>The ND pop-out (the one this instance made, else any ND-shaped pop-out of the sim).</summary>
    public IntPtr Find()
    {
        if (HasWindow) return _hwnd;
        _hwnd = FindCandidates().FirstOrDefault();
        return _hwnd;
    }

    /// <summary>The sim's visible pop-out windows ("WASMINSTRUMENT"). With <paramref name="anyShape"/>
    /// false only ND-shaped ones: the app always parks its pop-out at the ND's aspect, so a later
    /// session finds it by shape. A NEW pop-out opens at the size the sim last used for one (measured:
    /// 1350 x 1138, the gauge letterboxed inside with black bars), so the one a click just made is
    /// found by being new, whatever its shape, and then parked.</summary>
    private static List<IntPtr> FindCandidates(bool anyShape = false)
    {
        var found = new List<IntPtr>();
        var proc = SimProcess();
        if (proc == null) return found;
        uint pid = (uint)proc.Id;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var sb = new StringBuilder(64);
            GetWindowText(h, sb, 64);
            if (sb.ToString() != "WASMINSTRUMENT") return true;
            if (anyShape || (GetClientRect(h, out var rc) && rc.B > 0 && Math.Abs((double)rc.R / rc.B - ClientAspect) < AspectTolerance))
                found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static Process? SimProcess() =>
        Process.GetProcessesByName("FlightSimulator2024").FirstOrDefault()
        ?? Process.GetProcessesByName("FlightSimulator").FirstOrDefault();

    /// <summary>Client size the pop-out is parked at: exactly the tablet's width, ND aspect.</summary>
    public static Size ParkedClientSize => new(TabletWidth, (int)Math.Round(TabletWidth / ClientAspect));

    /// <summary>Sizes the pop-out so its CLIENT area is <see cref="ParkedClientSize"/> and parks it
    /// beyond the left edge of the virtual desktop (it keeps rendering there). Idempotent.</summary>
    public void Park(IntPtr h)
    {
        var want = ParkedClientSize;
        GetWindowRect(h, out var wr);
        GetClientRect(h, out var cr);
        int frameW = (wr.R - wr.L) - cr.R, frameH = (wr.B - wr.T) - cr.B;
        int tw = want.Width + frameW, th = want.Height + frameH;
        int x = SystemInformation.VirtualScreen.Left - tw - 200, y = SystemInformation.VirtualScreen.Top;
        if (cr.R == want.Width && cr.B == want.Height && wr.L == x) return;
        SetWindowPos(h, HWND_BOTTOM, x, y, tw, th, SWP_NOACTIVATE);
    }

    /// <summary>
    /// Makes sure the ND pop-out exists, creating it if needed. Returns null on success, otherwise the
    /// reason to speak. Run on a worker thread; camera reads and writes come through the delegates
    /// (type, index), as for the A300/A330 CDU pop-outs. Moves the mouse and focus for about a second.
    /// </summary>
    public async Task<string?> EnsureAsync(Func<Task<(int type, int index)?>> readCamera,
        Action<int, int> setCamera, CancellationToken ct = default)
    {
        var existing = Find();
        if (existing != IntPtr.Zero) { Park(existing); return null; }
        var proc = SimProcess();
        if (proc == null) return "Flight simulator window not found.";
        IntPtr sim = proc.MainWindowHandle;
        if (sim == IntPtr.Zero) return "Flight simulator window not found.";

        var before = await readCamera();
        IntPtr prevForeground = GetForegroundWindow();
        GetCursorPos(out var prevCursor);
        var oldCandidates = FindCandidates(anyShape: true);
        var tried = new List<string>();
        try
        {
            for (int round = 0; round < 2; round++)
            {
                setCamera(InstrumentViewType, NdViewIndex);
                await Task.Delay(1200, ct);                 // camera move + a rendered frame
                await WaitForStillCameraAsync(sim, ct);
                if (!BringToFront(sim)) return "Could not bring the simulator to the front to open the tablet reader.";
                await Task.Delay(300, ct);

                foreach (var target in ClickTargets(sim))
                {
                    if (GetForegroundWindow() != sim && !BringToFront(sim)) break;
                    _log($"pop-out: round {round + 1}, clicking ND at {target.X},{target.Y}");
                    tried.Add($"{target.X},{target.Y}");
                    ClickWithRightAlt(target.X, target.Y);
                    for (int i = 0; i < 25; i++)
                    {
                        await Task.Delay(120, ct);
                        var now = FindCandidates(anyShape: true).Except(oldCandidates).FirstOrDefault();
                        if (now == IntPtr.Zero) continue;
                        _hwnd = now;
                        await Task.Delay(300, ct);
                        Park(now);
                        LastDiagnostic = "clicked " + string.Join("; ", tried);
                        _log("pop-out: created and parked");
                        return null;
                    }
                }
            }
            LastDiagnostic = "no window after clicks at " + string.Join("; ", tried);
            _log("pop-out: " + LastDiagnostic);
            return "The tablet reader could not open. Make sure the cockpit view is active and try again.";
        }
        finally
        {
            await RestoreCameraAsync(before, readCamera, setCamera);
            SetCursorPos(prevCursor.X, prevCursor.Y);
            if (prevForeground != IntPtr.Zero) BringToFront(prevForeground);
        }
    }

    /// <summary>Points to Right-Alt-click, in screen coordinates: the ND centre, then a little above
    /// and below it (a different window shape moves the ND a few percent).</summary>
    private static IEnumerable<Point> ClickTargets(IntPtr sim)
    {
        GetClientRect(sim, out var cr);
        int w = cr.R, h = cr.B;
        foreach (double fy in new[] { NdCentreY, NdCentreY - 0.04, NdCentreY + 0.03 })
        {
            var p = new POINT { X = (int)(w / 2.0 + NdCentreXFromMid * h), Y = (int)(fy * h) };
            ClientToScreen(sim, ref p);
            yield return new Point(p.X, p.Y);
        }
    }

    /// <summary>
    /// Captures the tablet from the pop-out: the top <see cref="TabletHeight"/> rows of the client
    /// area at <see cref="TabletWidth"/> wide (the mirror keeps the tablet's aspect, so its image
    /// fills the ND's width and the top 528/859 of its height). Null when the window is gone or the
    /// capture failed. The mirror must be ON when this is called. Caller disposes.
    /// </summary>
    public Bitmap? CaptureTablet()
    {
        var h = Find();
        if (h == IntPtr.Zero) return null;
        Park(h);
        GetClientRect(h, out var cr);
        if (cr.R <= 0 || cr.B <= 0) return null;
        using var client = new Bitmap(cr.R, cr.B, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(client))
        {
            var hdc = g.GetHdc();
            bool ok = PrintWindow(h, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) return null;
        }
        var src = TabletSourceRect(cr.R, cr.B);
        var tablet = new Bitmap(TabletWidth, TabletHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(tablet))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(client, new Rectangle(0, 0, TabletWidth, TabletHeight), src, GraphicsUnit.Pixel);
        }
        return tablet;
    }

    /// <summary>
    /// Where the tablet sits in a pop-out client area of <paramref name="clientW"/> x
    /// <paramref name="clientH"/>. The sim draws the ND gauge in a pop-out at the ND's own aspect,
    /// centred, with black bars filling the rest (measured: a 1350 x 1138 window showed the ND
    /// letterboxed between bars), and the mirror lays the tablet over the top of the ND at the ND's
    /// width. Parked at <see cref="ParkedClientSize"/> there are no bars; any other size is still
    /// mapped right. Pure; tested.
    /// </summary>
    public static Rectangle TabletSourceRect(int clientW, int clientH)
    {
        double ndW = Math.Min(clientW, clientH * ClientAspect);
        double ndH = ndW / ClientAspect;
        double offX = (clientW - ndW) / 2, offY = (clientH - ndH) / 2;
        double tabH = ndW * TabletHeight / TabletWidth;
        return new Rectangle((int)Math.Round(offX), (int)Math.Round(offY), (int)Math.Round(ndW), (int)Math.Round(tabH));
    }

    /// <summary>Closes the pop-out (the ND returns to the cockpit).</summary>
    public void Close()
    {
        if (HasWindow) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _hwnd = IntPtr.Zero;
    }

    // ---------------- camera ----------------

    /// <summary>Puts the camera back: the pilot's own cockpit view if it takes, else the default
    /// pilot seat; views outside the cockpit are left alone. Each write is read back and repeated
    /// (a single type+index write straight after a pop-out was measured not to take on the A330).</summary>
    private async Task RestoreCameraAsync((int type, int index)? before,
        Func<Task<(int type, int index)?>> readCamera, Action<int, int> setCamera)
    {
        if (before is not { } b || (b.type != 1 && b.type != InstrumentViewType)) return;
        try
        {
            if (b.type == 1 && await SetAndConfirmAsync(b.index)) return;
            if (await SetAndConfirmAsync(0)) return;
            _log("pop-out: the camera did not leave the instrument view");
        }
        catch (Exception ex) { _log($"pop-out: camera restore failed: {ex.Message}"); }

        async Task<bool> SetAndConfirmAsync(int index)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                setCamera(1, index);
                await Task.Delay(400);
                if (await readCamera() is { type: 1 } now && now.index == index) return true;
            }
            return false;
        }
    }

    /// <summary>Waits (up to ~3 s) until two captures 300 ms apart nearly match: the camera stopped.</summary>
    private static async Task WaitForStillCameraAsync(IntPtr sim, CancellationToken ct)
    {
        GetClientRect(sim, out var cr);
        byte[]? prev = null;
        for (int i = 0; i < 10; i++)
        {
            using var bmp = CaptureClient(sim, cr.R, cr.B);
            if (bmp == null) return;
            var cur = Thumbnail(bmp);
            if (prev != null)
            {
                double diff = 0;
                for (int k = 0; k < cur.Length; k++) diff += Math.Abs(cur[k] - prev[k]);
                if (diff / cur.Length < 3) return;
            }
            prev = cur;
            await Task.Delay(300, ct);
        }
    }

    private static byte[] Thumbnail(Bitmap bmp)
    {
        using var small = new Bitmap(bmp, 64, 64);
        var t = new byte[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var c = small.GetPixel(x, y);
                t[y * 64 + x] = Math.Max(c.R, Math.Max(c.G, c.B));
            }
        return t;
    }

    private static Bitmap? CaptureClient(IntPtr h, int w, int hh)
    {
        if (w <= 0 || hh <= 0) return null;
        var bmp = new Bitmap(w, hh, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            bool ok = PrintWindow(h, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) { bmp.Dispose(); return null; }
        }
        return bmp;
    }

    // ---------------- focus and input (same chain as the A300/A330 CDU pop-out) ----------------

    private bool BringToFront(IntPtr h)
    {
        if (GetForegroundWindow() == h) return true;
        // 1: a synthetic Alt tap satisfies the foreground lock's "last input" rule.
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        if (WaitForeground(h, 10)) return true;
        // 2: attach to the foreground window's input queue.
        IntPtr fg = GetForegroundWindow();
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try { BringWindowToTop(h); SetForegroundWindow(h); }
        finally { if (attached) AttachThreadInput(me, fgThread, false); }
        if (WaitForeground(h, 10)) return true;
        // 3: the shell's window switch.
        try { SwitchToThisWindow(h, true); } catch { }
        if (WaitForeground(h, 10)) return true;
        // 4: Windows Script Host's AppActivate (the one that brought FS2024 forward from a
        // background process when the others were refused; measured on the A330, 2026-10-16, and on
        // the 757 probe, 2026-10-26, where a plain AppActivate alone failed once).
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type != null && Activator.CreateInstance(type) is object shell)
            {
                type.InvokeMember("AppActivate", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { (int)pid });
                Marshal.FinalReleaseComObject(shell);
            }
            SetForegroundWindow(h);
        }
        catch (Exception ex) { _log($"AppActivate failed: {ex.Message}"); }
        bool ok = WaitForeground(h, 20);
        if (!ok) _log("could not bring the window to the front by any of four ways");
        return ok;
    }

    private static bool WaitForeground(IntPtr h, int tries)
    {
        for (int i = 0; i < tries && GetForegroundWindow() != h; i++) Thread.Sleep(50);
        return GetForegroundWindow() == h;
    }

    private static void ClickWithRightAlt(int x, int y)
    {
        // The sim only treats the cursor as over a gauge after it has moved there.
        SetCursorPos(x - 30, y);
        for (int i = 0; i < 30; i++) { mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, UIntPtr.Zero); Thread.Sleep(15); }
        Thread.Sleep(300);
        keybd_event(VK_RMENU, 0x38, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        Thread.Sleep(250);
        try
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(120);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(250);
        }
        finally
        {
            keybd_event(VK_RMENU, 0x38, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);   // never leave Alt held
        }
    }

    // ---------------- Win32 ----------------
    private const uint PW_CLIENTONLY = 1, PW_RENDERFULLCONTENT = 2, SWP_NOACTIVATE = 0x0010, WM_CLOSE = 0x0010;
    private const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
    private const byte VK_MENU = 0x12, VK_RMENU = 0xA5;
    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}

