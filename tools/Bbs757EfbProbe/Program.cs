// Live probe for the BlueBird 757 tablet reader. See README.md next to this file.
//
//   Bbs757EfbProbe popout                      make (or find) the ND pop-out and park it
//                                              (set the camera to instrument view 0 first:
//                                               CAMERA VIEW TYPE AND INDEX:0 = 2, :1 = 0)
//   Bbs757EfbProbe read <dir> <name> [x,y ...] tap the points (tablet pixels, 1.2 s apart), then
//                                              mirror on, capture, mirror off, OCR; writes
//                                              <name>.png, <name>.ocr.json, <name>.txt; prints timings
//   Bbs757EfbProbe latency                     how long after mirror-on the pop-out shows the tablet
//   Bbs757EfbProbe mirror on|off
//   Bbs757EfbProbe close                       close the pop-out
//   Bbs757EfbProbe stability [n]               read the page shown n times, 1 s apart: how often it reads differently
//   Bbs757EfbProbe noise                       what changes between two captures of an unchanged page
//   Bbs757EfbProbe tour [n]                    Home, every app and every section, each read n times as the app reads
//                                              it (navigation taps only); flight-11.md section 8
//
// Every read is PaddleOCR, as the app reads: one engine per run, loaded on first use from the models the app
// downloads (%APPDATA%\MSFSBlindAssist\text-recognition), with the app's thread cap; the load prints its time
// and the process working set before and after.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.SimConnect.BBS757;

internal static class Program
{
    private static readonly Bbs757EfbPopout Popout = new(s => Console.WriteLine("  [popout] " + s));

    // The app is SystemAware (Program.cs): without the same here, a scaled display (measured: 4K at
    // 300 %) sizes and captures the pop-out in logical pixels, and a capture holds only the tablet's
    // top-left third, enlarged.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static async Task<int> Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length == 0) { Console.WriteLine("commands: popout | read <dir> <name> [x,y ...] | latency | mirror on|off | close"); return 1; }
        switch (args[0])
        {
            case "popout":
            {
                string? err = await Popout.EnsureAsync(() => Task.FromResult<(int, int)?>(null), (_, _) => { });
                Console.WriteLine(err ?? $"pop-out ready: {Popout.Find()} ({Popout.LastDiagnostic})");
                return err == null ? 0 : 2;
            }
            case "close":
                Popout.Find(); Popout.Close(); Console.WriteLine("closed"); return 0;
            case "mirror":
                Console.WriteLine(await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.mirror({(args[1] == "on" ? "true" : "false")}))"));
                return 0;
            case "latency":
                return await Latency();
            case "calibrate":
                return await Calibrate();
            case "sweep":   // sweep <y> <x0> <x1> <step> [valueLeft]   taps along a row, reads the value after each
                return await Sweep(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]),
                    args.Length > 5 ? int.Parse(args[5]) : 1200);
            case "tapread": // tapread <x> <y> <regionX> <regionY> <regionW> <regionH>   one tap, read a region
                return await TapRead(int.Parse(args[1]), int.Parse(args[2]),
                    new Rectangle(int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])));
            case "parse":   // parse <dir> [name ...]   the page model of saved captures (png + ocr.json)
                foreach (var n in args.Length > 2 ? args.Skip(2) : Directory.GetFiles(args[1], "*.ocr.json").Select(f => Path.GetFileName(f)[..^9]))
                    PrintModel(Path.Combine(args[1], n));
                return 0;
            case "reread":  // reread <dir> [name ...]   the app's reader (one PaddleOCR read, then the parser) on saved pictures
                foreach (var n in args.Length > 2 ? args.Skip(2) : Directory.GetFiles(args[1], "*.png").Where(f => !f.EndsWith(".pre.png")).Select(f => Path.GetFileNameWithoutExtension(f)))
                {
                    var engine = Engine();
                    using var bmp = new Bitmap(Path.Combine(args[1], n + ".png"));
                    var read = await Bbs757EfbPageReader.ReadAsync(engine, bmp);
                    if (Environment.GetEnvironmentVariable("PROBE_SAVE") is { Length: > 0 } saveDir)
                    {
                        // A test fixture: the picture and the reader's final lines (what Parse is given).
                        Directory.CreateDirectory(saveDir);
                        File.Copy(Path.Combine(args[1], n + ".png"), Path.Combine(saveDir, n + ".png"), true);
                        File.WriteAllText(Path.Combine(saveDir, n + ".lines.json"), JsonSerializer.Serialize(read.Lines.Select(l => new
                        {
                            text = l.Text, x = l.Box.X, y = l.Box.Y, w = l.Box.Width, h = l.Box.Height, pass = l.Pass.ToString(),
                            words = l.Words.Select(w => new { text = w.Text, x = w.Box.X, y = w.Box.Y, w = w.Box.Width, h = w.Box.Height }),
                        }), new JsonSerializerOptions { WriteIndented = false }));
                    }
                    if (Environment.GetEnvironmentVariable("PROBE_LINES") == "1")
                        foreach (var l in read.Lines) Console.WriteLine($"    line {l.Box.X},{l.Box.Y} {l.Box.Width}x{l.Box.Height} [{l.Pass}] {l.Text}");
                    PrintModel(n, read.Model, $"{read.OcrMs} ms");
                }
                return 0;
            case "region":  // region <png> x y w h   read one region of a saved picture, as sweep and tapread read theirs
            {
                var engine = Engine();
                using var bmp = new Bitmap(args[1]);
                var r = new Rectangle(int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]));
                var sw = Stopwatch.StartNew();
                var ls = await ReadRegionAsync(engine, bmp, r);
                long ms = sw.ElapsedMilliseconds;
                foreach (var l in ls) Console.WriteLine($"  {l.Box.X},{l.Box.Y} {l.Box.Width}x{l.Box.Height} {l.Text}");
                Console.WriteLine($"  '{string.Join(" | ", ls.Select(l => l.Text))}'  {ms} ms");
                return 0;
            }
            case "boxes":   // boxes <stem> [x0 y0 x1 y1]   button boxes found from the pixels (and why a region has none)
            {
                using var bmp = new Bitmap(args[1] + ".png");
                var px = Bbs757EfbPixels.FromBitmap(bmp);
                Console.WriteLine($"white {px.RawWhite:0}, background {px.Background}");
                foreach (var b in Bbs757EfbScreen.FindButtonBoxes(px, new Rectangle(256, 88, 1141, 960)))
                    Console.WriteLine($"  {b.Box} {b.Tint}");
                if (args.Length > 5)
                    for (int y = int.Parse(args[3]); y <= int.Parse(args[5]); y += 4)
                        Console.WriteLine($"  y{y}: " + string.Join(" ", Enumerable.Range(0, 12).Select(i => { int x = int.Parse(args[2]) + i * (int.Parse(args[4]) - int.Parse(args[2])) / 11; var c = px.At(x, y); return $"{c.R},{c.G},{c.B}"; })));
                return 0;
            }
            case "wheel":   // wheel <x> <y> <deltaY>
                Console.WriteLine(await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.wheel({args[1]},{args[2]},{args[3]}))"));
                return 0;
            case "front":   // front <hwnd>   time bringing a window to the front
            {
                var sw = Stopwatch.StartNew();
                bool ok = Popout.BringToFront(new IntPtr(long.Parse(args[1])));
                Console.WriteLine($"front {(ok ? "ok" : "failed")} in {sw.ElapsedMilliseconds} ms");
                return 0;
            }
            case "vars":    // vars NAME [NAME ...]   BlueBird L:vars read in the tablet's page
                Console.WriteLine(await Agent("JSON.stringify(__MSFSBA_BBS757EFB.readVars(" + JsonSerializer.Serialize(args.Skip(1).ToArray()) + "))"));
                return 0;
            case "tap":     // tap x,y [x,y ...]   (1.2 s apart)
                foreach (var t in args.Skip(1))
                {
                    var xy = t.Split(',');
                    Console.WriteLine($"  tap {t}: {await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({int.Parse(xy[0])},{int.Parse(xy[1])}))")}");
                    await Task.Delay(1200);
                }
                return 0;
            case "look":    // look   capture the pop-out as it is (no debugger: the app may hold the view) and read it
            {
                if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out"); return 2; }
                var engine = Engine();
                using var t = Popout.CaptureTablet();
                if (t == null) { Console.WriteLine("capture failed"); return 3; }
                var read = await Bbs757EfbPageReader.ReadAsync(engine, t);
                PrintModel("live", read.Model, $"{read.OcrMs} ms");
                return 0;
            }
            case "read":
                return await Read(args[1], args[2], args.Skip(3).ToArray());
            case "stability":   // stability [n]   read the page as it is n times, 1 s apart: how often it differs
                return await Stability(args.Length > 1 ? int.Parse(args[1]) : 10);
            case "noise":       // noise   what changes between two captures 250 ms apart
            {
                await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
                await Task.Delay(MirrorSettleMs);
                for (int run = 0; run < 4; run++)
                {
                    using var a = Popout.CaptureTablet();
                    await Task.Delay(250);
                    using var b = Popout.CaptureTablet();
                    if (a == null || b == null) return 3;
                    var pa = Bbs757EfbPixels.FromBitmap(a); var pb = Bbs757EfbPixels.FromBitmap(b);
                    int n = 0, maxD = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
                    var hist = new int[5];
                    for (int y = Bbs757EfbScreen.StatusBottom + 4; y < a.Height; y++)
                        for (int x = 0; x < a.Width; x++)
                        {
                            var ca = pa.At(x, y); var cb = pb.At(x, y);
                            int d = Math.Max(Math.Abs(ca.R - cb.R), Math.Max(Math.Abs(ca.G - cb.G), Math.Abs(ca.B - cb.B)));
                            if (d == 0) continue;
                            n++; maxD = Math.Max(maxD, d);
                            hist[d <= 2 ? 0 : d <= 8 ? 1 : d <= 32 ? 2 : d <= 96 ? 3 : 4]++;
                            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                        }
                    Console.WriteLine($"  run {run + 1}: {n} pixels differ of {a.Width * a.Height}, max {maxD}, box {x0},{y0}-{x1},{y1}, "
                        + $"by size <=2:{hist[0]} <=8:{hist[1]} <=32:{hist[2]} <=96:{hist[3]} more:{hist[4]}");
                }
                return 0;
            }
            case "tour":        // tour [n]   every menu entry of the page shown, each read n times (menu taps only)
                return await Tour(args.Length > 1 ? int.Parse(args[1]) : 3);
        }
        Console.WriteLine("unknown command"); return 1;
    }

    private static async Task<int> Read(string dir, string name, string[] taps)
    {
        Directory.CreateDirectory(dir);
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        var engine = Engine();   // loaded before the taps, so its load time is not in the timings
        foreach (var t in taps)
        {
            var xy = t.Split(',');
            Console.WriteLine($"  tap {t}: {await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({int.Parse(xy[0])},{int.Parse(xy[1])}))")}");
            await Task.Delay(1200);
        }
        var sw = Stopwatch.StartNew();
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        long tMirror = sw.ElapsedMilliseconds;
        await Task.Delay(MirrorSettleMs);
        using var tablet = Popout.CaptureTablet();
        long tCapture = sw.ElapsedMilliseconds;
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(false))");
        long tOff = sw.ElapsedMilliseconds;
        if (tablet == null) { Console.WriteLine("capture failed"); return 3; }
        tablet.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        var ocrWatch = Stopwatch.StartNew();
        var lines = await Bbs757EfbOcr.ReadAsync(engine, tablet);
        long tOcr = ocrWatch.ElapsedMilliseconds;
        File.WriteAllText(Path.Combine(dir, name + ".ocr.json"), JsonSerializer.Serialize(lines.Select(l => new
        {
            text = l.Text, x = l.Box.X, y = l.Box.Y, w = l.Box.Width, h = l.Box.Height, pass = l.Pass.ToString(),
            words = l.Words.Select(w => new { text = w.Text, x = w.Box.X, y = w.Box.Y, w = w.Box.Width, h = w.Box.Height }),
        }), new JsonSerializerOptions { WriteIndented = true }));
        var sb = new StringBuilder();
        foreach (var l in lines)
            sb.AppendLine($"{l.Box.X,5},{l.Box.Y,5} {l.Box.Width,4}x{l.Box.Height,-3} c={l.Centre.X},{l.Centre.Y} [{l.Pass}] {l.Text}");
        File.WriteAllText(Path.Combine(dir, name + ".txt"), sb.ToString());
        Console.Write(sb);
        Console.WriteLine($"  timing: mirror on {tMirror} ms, captured at {tCapture} ms, mirror off at {tOff} ms, OCR {tOcr} ms");
        return 0;
    }

    // ------------------------------------------------------------------ reliability (2026-10-27 review)

    private static string Fp(Bbs757EfbScreenModel m)
    {
        var sb = new StringBuilder(m.PageName).Append('\n');
        foreach (var i in m.Menu.Concat(m.Items))
            sb.Append(i.Kind).Append('|').Append(i.Text).Append('|').Append(i.Value).Append('|').Append(i.On).Append('|').Append(i.Selected).Append('\n');
        return sb.ToString();
    }

    private static ulong Hash(Bitmap bmp)
    {
        int top = Math.Min(bmp.Height, Bbs757EfbScreen.StatusBottom + 4);
        var rect = new Rectangle(0, top, bmp.Width, bmp.Height - top);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = rect.Width * 4; var row = new byte[rowBytes]; ulong h = 14695981039346656037UL;
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                for (int x = 0; x < rowBytes; x++) { h ^= row[x]; h *= 1099511628211UL; }
            }
            return h;
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Captures until two in a row are pixel-identical (the shipped bridge's settle).</summary>
    private static async Task<(Bitmap? Bmp, bool Still, long Ms)> Settled()
    {
        var sw = Stopwatch.StartNew();
        var bmp = Popout.CaptureTablet();
        if (bmp == null) return (null, false, 0);
        while (sw.ElapsedMilliseconds < 2500)
        {
            await Task.Delay(250);
            var next = Popout.CaptureTablet();
            if (next == null) break;
            bool same = Bbs757EfbFrameCompare.Same(bmp, next);
            bmp.Dispose(); bmp = next;
            if (same) return (bmp, true, sw.ElapsedMilliseconds);
        }
        return (bmp, false, sw.ElapsedMilliseconds);
    }

    private static void PrintDiff(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        foreach (var l in la.Except(lb)) if (l.Length > 0) Console.WriteLine("      - " + l);
        foreach (var l in lb.Except(la)) if (l.Length > 0) Console.WriteLine("      + " + l);
    }

    private static async Task<int> Stability(int n)
    {
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        var engine = Engine();
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        await Task.Delay(MirrorSettleMs);
        var hashes = new List<ulong>(); var fps = new List<string>(); var ms = new List<long>();
        Bitmap? prev = null; int sameAsPrev = 0;
        for (int i = 0; i < n; i++)
        {
            using var t = Popout.CaptureTablet();
            if (t == null) { Console.WriteLine("capture failed"); return 3; }
            hashes.Add(Hash(t));
            if (prev != null) { if (Bbs757EfbFrameCompare.Same(prev, t)) sameAsPrev++; prev.Dispose(); }
            prev = new Bitmap(t);
            var r = await Bbs757EfbPageReader.ReadAsync(engine, t);
            fps.Add(Fp(r.Model)); ms.Add(r.OcrMs);
            if (i == 0)
            {
                var again = await Bbs757EfbPageReader.ReadAsync(engine, t);
                Console.WriteLine($"  page: {r.Model.PageName}, {r.Model.Menu.Count} menu + {r.Model.Items.Count} items");
                Console.WriteLine($"  same picture read twice gives the same page: {Fp(again.Model) == fps[0]}");
            }
            await Task.Delay(1000);
        }
        var common = fps.GroupBy(f => f).OrderByDescending(g => g.Count()).First().Key;
        Console.WriteLine($"  {n} captures: {hashes.Distinct().Count()} distinct pictures, {fps.Distinct().Count()} distinct readings, "
            + $"{fps.Count(f => f == common)}/{n} match the usual one; {sameAsPrev}/{n - 1} captures 'the same' as the one before; OCR {ms.Min()}-{ms.Max()} ms");
        for (int i = 0; i < n; i++)
            if (fps[i] != common) { Console.WriteLine($"    read {i + 1} (picture {(hashes[i] == hashes[fps.IndexOf(common)] ? "same" : "different")}):"); PrintDiff(common, fps[i]); }
        return 0;
    }

    private static int _pages, _stablePages, _landed, _unstill;

    private static readonly List<long> _changeMs = new();

    /// <summary>Taps, then waits (up to 4 s) for the picture to change from the one before the tap.</summary>
    private static async Task Tap(Point p)
    {
        using var before = Popout.CaptureTablet();
        await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({p.X},{p.Y}))");
        var sw = Stopwatch.StartNew();
        while (before != null && sw.ElapsedMilliseconds < 4000)
        {
            await Task.Delay(100);
            using var now = Popout.CaptureTablet();
            if (now != null && !Bbs757EfbFrameCompare.Same(before, now)) { _changeMs.Add(sw.ElapsedMilliseconds); return; }
        }
        Console.WriteLine($"    (tap {p.X},{p.Y}: no change seen in 4 s)");
    }

    /// <summary>Reads the page shown n times (settled, as the app does); prints the verdict; returns the first model.</summary>
    private static async Task<Bbs757EfbScreenModel?> ReadPage(string label, string expect, int n)
    {
        var fps = new List<string>(); Bbs757EfbScreenModel? first = null; bool allStill = true; long settle = 0, ocr = 0;
        // As the app: read until the page name is the one tapped (at most 4 s), then the n reads.
        var nav = Stopwatch.StartNew(); int tries = 0;
        while (expect.Length > 0 && nav.ElapsedMilliseconds < 4000)
        {
            var (b0, _, _) = await Settled();
            if (b0 == null) break;
            tries++;
            using (b0) { var r0 = await Bbs757EfbPageReader.ReadAsync(Engine(), b0); if (r0.Model.PageName.Contains(expect, StringComparison.OrdinalIgnoreCase)) break; }
            await Task.Delay(500);
        }
        if (tries > 1) Console.WriteLine($"    ({label.Trim()}: right page after {tries} reads, {nav.ElapsedMilliseconds} ms)");
        for (int i = 0; i < n; i++)
        {
            var (b, still, sms) = await Settled();
            if (b == null) break;
            allStill &= still; settle = Math.Max(settle, sms);
            using (b) { var r = await Bbs757EfbPageReader.ReadAsync(Engine(), b); first ??= r.Model; fps.Add(Fp(r.Model)); ocr = Math.Max(ocr, r.OcrMs); }
            if (i < n - 1) await Task.Delay(700);
        }
        if (first == null) { Console.WriteLine($"  {label}: capture failed"); return null; }
        bool stable = fps.Distinct().Count() == 1;
        bool landed = expect.Length == 0 || first.PageName.Contains(expect, StringComparison.OrdinalIgnoreCase);
        _pages++; if (stable) _stablePages++; if (landed) _landed++; if (!allStill) _unstill++;
        Console.WriteLine($"  {label}: '{first.PageName}', {first.Items.Count} items, {(stable ? "SAME" : "DIFFERENT")} x{fps.Count}, "
            + $"still={allStill}, settle<={settle} ms, OCR<={ocr} ms{(landed ? "" : "  <-- not the page tapped")}");
        if (!stable) for (int i = 1; i < fps.Count; i++) if (fps[i] != fps[0]) { Console.WriteLine($"    read {i + 1} vs read 1:"); PrintDiff(fps[0], fps[i]); }
        return first;
    }

    private static async Task<int> Tour(int n)
    {
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        Engine();   // loaded before the first tap, so no page waits for it
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        await Task.Delay(MirrorSettleMs);
        await Tap(Bbs757EfbScreen.HomeButton);
        var home = await ReadPage("Home", "Home", n);
        if (home == null) return 3;
        var apps = home.Items.Where(i => i.Kind == Bbs757EfbKind.Menu && i.Clickable).ToList();
        foreach (var app in apps)
        {
            await Tap(Bbs757EfbScreen.HomeButton);
            await Tap(app.Hit);
            var page = await ReadPage(app.Text, app.Text, n);
            if (page == null) continue;
            foreach (var m in page.Menu.Where(m => m.Kind == Bbs757EfbKind.Menu && m.Clickable && !m.Selected))
            {
                await Tap(m.Hit);
                await ReadPage("  " + app.Text + " > " + m.Text, m.Text, n);
            }
        }
        await Tap(Bbs757EfbScreen.HomeButton);
        if (_changeMs.Count > 0) Console.WriteLine($"  page change seen {_changeMs.Min()}-{_changeMs.Max()} ms after a tap (median {_changeMs.OrderBy(x => x).ElementAt(_changeMs.Count / 2)})");
        Console.WriteLine($"  SUMMARY: {_pages} pages; {_stablePages} read the same every time; {_landed} were the page tapped; {_unstill} never held still");
        return 0;
    }

    // How long after mirror-on the pop-out reliably shows the tablet (see 'latency').
    private const int MirrorSettleMs = 600;

    private static async Task<int> Latency()
    {
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        for (int run = 0; run < 3; run++)
        {
            await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(false))");
            await Task.Delay(800);
            using var before = Popout.CaptureTablet();
            var sw = Stopwatch.StartNew();
            await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
            long sent = sw.ElapsedMilliseconds;
            long seen = -1;
            while (sw.ElapsedMilliseconds < 3000)
            {
                using var now = Popout.CaptureTablet();
                if (before != null && now != null && Diff(before, now) > 8) { seen = sw.ElapsedMilliseconds; break; }
                await Task.Delay(20);
            }
            Console.WriteLine($"  run {run + 1}: eval returned {sent} ms, tablet visible at {seen} ms");
        }
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(false))");
        return 0;
    }

    /// <summary>Proves the capture's coordinates are the agent's click coordinates: draws magenta
    /// markers over the mirrored tablet at known tablet points, captures, and finds them.</summary>
    private static async Task<int> Calibrate()
    {
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        var points = new[] { (100, 100), (1297, 948), (698, 524), (40, 1000) };
        string pts = string.Join(",", points.Select(p => $"[{p.Item1},{p.Item2}]"));
        Console.WriteLine(await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))"));
        Console.WriteLine(await Agent(
            "(function(){var nd=null,a=document.querySelectorAll('wasm-instrument');for(var i=0;i<a.length;i++){if(/wasm_gauge=nd_l(&|$)/.test(a[i].getAttribute('url')||''))nd=a[i];}" +
            "var r=nd.getBoundingClientRect(),k=r.width/1397,pts=[" + pts + "],o=[];" +
            "for(var j=0;j<pts.length;j++){var d=document.createElement('div');d.className='msfsba_cal';" +
            "d.style.cssText='position:absolute;z-index:100000;background:#ff00ff;width:8px;height:8px;left:'+(r.left+pts[j][0]*k-4)+'px;top:'+(r.top+pts[j][1]*k-4)+'px;';" +
            "document.body.appendChild(d);o.push(pts[j]);}return 'markers '+JSON.stringify(o);})()"));
        await Task.Delay(1000);
        using var tablet = Popout.CaptureTablet();
        await Agent("(function(){var a=document.querySelectorAll('.msfsba_cal');for(var i=0;i<a.length;i++)a[i].parentNode.removeChild(a[i]);return 'removed';})()");
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(false))");
        if (tablet == null) { Console.WriteLine("capture failed"); return 3; }
        tablet.Save(Path.Combine(Path.GetTempPath(), "bbs757-calibrate.png"), ImageFormat.Png);
        foreach (var (tx, ty) in points)
        {
            long sx = 0, sy = 0, n = 0;
            for (int y = Math.Max(0, ty - 30); y < Math.Min(tablet.Height, ty + 30); y++)
                for (int x = Math.Max(0, tx - 30); x < Math.Min(tablet.Width, tx + 30); x++)
                {
                    var c = tablet.GetPixel(x, y);
                    if (c.R > 180 && c.B > 180 && c.G < 90) { sx += x; sy += y; n++; }
                }
            Console.WriteLine(n == 0 ? $"  marker at ({tx},{ty}): NOT FOUND within 30 px"
                                     : $"  marker at ({tx},{ty}): seen at ({sx / (double)n:0.0},{sy / (double)n:0.0}), {n} px");
        }
        return 0;
    }

    /// <summary>Taps along a slider row and reports the value and knob after each tap. The mirror
    /// stays on for the whole sweep. Prints the value before the sweep so it can be restored.</summary>
    private static async Task<int> Sweep(int y, int x0, int x1, int step, int valueLeft)
    {
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        Engine();
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        await Task.Delay(700);
        var valueRect = new Rectangle(valueLeft, y - 20, 1397 - valueLeft, 40);
        Console.WriteLine($"  before: {await ValueAt(valueRect)}  knob {KnobAt(y)}");
        for (int x = x0; x <= x1; x += step)
        {
            await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({x},{y}))");
            await Task.Delay(450);
            Console.WriteLine($"  tap x={x,4}: value '{await ValueAt(valueRect)}'  knob {KnobAt(y)}");
        }
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(false))");
        return 0;
    }

    private static async Task<int> TapRead(int x, int y, Rectangle region)
    {
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        Engine();
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        await Task.Delay(700);
        Console.WriteLine($"  before: '{await ValueAt(region)}'");
        await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({x},{y}))");
        await Task.Delay(700);
        Console.WriteLine($"  after:  '{await ValueAt(region)}'");
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(false))");
        return 0;
    }

    private static async Task<string> ValueAt(Rectangle r)
    {
        using var t = Popout.CaptureTablet();
        if (t == null) return "(no capture)";
        var lines = await ReadRegionAsync(Engine(), t, r);
        return string.Join(" | ", lines.Select(l => l.Text));
    }

    // ---------------- PaddleOCR: the engine and region reads ----------------

    private static PaddleOcrEngine? _engine;

    /// <summary>The run's one engine, loaded on first use as the app loads it (the models folder, ThreadsFor's
    /// threads); prints the load time and the process working set just before and just after the load.</summary>
    private static PaddleOcrEngine Engine()
    {
        if (_engine != null) return _engine;
        string folder = TextRecognitionModels.DefaultFolder();
        int threads = PaddleOcrEngine.ThreadsFor(Environment.ProcessorCount);
        long before = WorkingSetMb();
        var sw = Stopwatch.StartNew();
        _engine = PaddleOcrEngine.Load(folder, threads);
        long loadMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [engine] PaddleOCR loaded in {loadMs} ms ({threads} threads, {folder}); working set {before} MB before, {WorkingSetMb()} MB after");
        return _engine;
    }

    private static long WorkingSetMb()
    {
        using var self = Process.GetCurrentProcess();
        return self.WorkingSet64 / (1024 * 1024);
    }

    /// <summary>One region of the tablet (a value cell, a button) read on its own: cropped with a 12 px black border
    /// (text that touches a picture's edge reads badly), read with PaddleOCR, boxes put back in tablet pixels.</summary>
    private static async Task<IReadOnlyList<Bbs757EfbTextLine>> ReadRegionAsync(PaddleOcrEngine engine, Bitmap tablet, Rectangle region)
    {
        const int Pad = 12;
        region.Intersect(new Rectangle(0, 0, tablet.Width, tablet.Height));
        if (region.Width < 4 || region.Height < 4) return Array.Empty<Bbs757EfbTextLine>();
        using var crop = new Bitmap(region.Width + 2 * Pad, region.Height + 2 * Pad, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(crop))
        {
            g.Clear(Color.Black);
            g.DrawImage(tablet, new Rectangle(Pad, Pad, region.Width, region.Height), region, GraphicsUnit.Pixel);
        }
        var lines = await Bbs757EfbOcr.ReadAsync(engine, crop);
        return lines.Select(l => l with
        {
            Box = ToTablet(l.Box),
            Words = l.Words.Select(w => w with { Box = ToTablet(w.Box) }).ToList(),
        }).ToList();

        Rectangle ToTablet(Rectangle box) => new(box.X - Pad + region.X, box.Y - Pad + region.Y, box.Width, box.Height);
    }

    /// <summary>The slider knob's centre on a row: the widest run of knob-blue pixels between x 600
    /// and 1230 (knob and filled track are the same blue; the knob is the run's right end).</summary>
    private static string KnobAt(int y)
    {
        using var t = Popout.CaptureTablet();
        if (t == null) return "?";
        int bestL = -1, bestR = -1, runL = -1;
        for (int x = 600; x <= 1230; x++)
        {
            var c = t.GetPixel(x, y);
            bool blue = c.B > 30 && c.B > c.R * 2 && c.B > c.G + 10;
            if (blue && runL < 0) runL = x;
            if ((!blue || x == 1230) && runL >= 0)
            {
                int r = blue ? x : x - 1;
                if (r - runL > bestR - bestL) { bestL = runL; bestR = r; }
                runL = -1;
            }
        }
        return bestL < 0 ? "none" : $"{bestL}-{bestR}";
    }

    /// <summary>Loads a saved OCR read (the read command's JSON) back into lines.</summary>
    internal static List<Bbs757EfbTextLine> LoadOcr(string jsonPath)
    {
        var lines = new List<Bbs757EfbTextLine>();
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        foreach (var l in doc.RootElement.EnumerateArray())
        {
            var words = l.GetProperty("words").EnumerateArray().Select(w => new Bbs757EfbWord(w.GetProperty("text").GetString() ?? "",
                new Rectangle(w.GetProperty("x").GetInt32(), w.GetProperty("y").GetInt32(), w.GetProperty("w").GetInt32(), w.GetProperty("h").GetInt32()))).ToList();
            lines.Add(new Bbs757EfbTextLine(l.GetProperty("text").GetString() ?? "",
                new Rectangle(l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32(), l.GetProperty("w").GetInt32(), l.GetProperty("h").GetInt32()),
                words, (l.GetProperty("pass").GetString() ?? "A")[0]));
        }
        return lines;
    }

    private static void PrintModel(string stem)
    {
        using var bmp = new Bitmap(stem + ".png");
        PrintModel(Path.GetFileName(stem), Bbs757EfbScreen.Parse(LoadOcr(stem + ".ocr.json"), Bbs757EfbPixels.FromBitmap(bmp)), "");
    }

    private static void PrintModel(string name, Bbs757EfbScreenModel model, string note)
    {
        Console.WriteLine($"===== {name}: {model.PageName}  {note}");
        foreach (var m in model.Menu)
            Console.WriteLine($"  menu  {(m.Selected ? "*" : " ")} {m.Text}{(m.Kind != Bbs757EfbKind.Menu ? $" [{m.Kind} {m.Value}]" : "")}  @{m.Hit.X},{m.Hit.Y}");
        foreach (var i in model.Items)
        {
            string extra = i.Kind switch
            {
                Bbs757EfbKind.Toggle => $" = {i.Value} ({(i.On == true ? "on" : "off")})",
                Bbs757EfbKind.Slider => $" = {i.Value} (knob {i.Slider!.KnobX}, track {i.Slider.TrackLeft}-{i.Slider.TrackRight})",
                Bbs757EfbKind.Checkbox => $" [{(i.On == true ? "x" : " ")}]",
                Bbs757EfbKind.Row => $" = {i.Value}",
                Bbs757EfbKind.Button => $" ({i.Tint})",
                _ => "",
            };
            Console.WriteLine($"  {i.Kind,-8} {i.Text}{extra}{(i.Clickable ? $"  @{i.Hit.X},{i.Hit.Y}" : "")}");
        }
    }

    private static double Diff(Bitmap a, Bitmap b)
    {
        double d = 0; int n = 0;
        for (int y = 10; y < a.Height; y += 37)
            for (int x = 10; x < a.Width; x += 37)
            {
                var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
                d += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B); n++;
            }
        return d / n;
    }

    // ---------------- Coherent debugger: install the app's agent, evaluate ----------------

    private static string? _agent;

    private static async Task<string> Agent(string expression)
    {
        _agent ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "coherent-bbs757-efb-agent.js"));
        return await Eval(_agent + ";\n" + expression);
    }

    private static async Task<string> Eval(string expression)
    {
        using var http = new HttpClient();
        var pages = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:19999/pagelist.json")).RootElement;
        int id = -1;
        foreach (var p in pages.EnumerateArray())
            if ((p.GetProperty("title").GetString() ?? "").Contains("BBSNavigraphMap")) id = p.GetProperty("id").GetInt32();
        if (id < 0) return "no BBSNavigraphMap view";
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{id}"), CancellationToken.None);
        var msg = JsonSerializer.Serialize(new { id = 1, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } });
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);
        var buf = new byte[1 << 20];
        while (true)
        {
            var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do { r = await ws.ReceiveAsync(buf, CancellationToken.None); ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
            var root = JsonDocument.Parse(ms.ToArray()).RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.GetInt32() == 1)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                var res = root.GetProperty("result").GetProperty("result");
                return res.TryGetProperty("value", out var v) ? v.ToString() : res.ToString();
            }
        }
    }
}
