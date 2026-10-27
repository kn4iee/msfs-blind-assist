using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One recognised word, in tablet pixels (the agent's click coordinates).</summary>
public sealed record Bbs757EfbWord(string Text, Rectangle Box);

/// <summary>One recognised line, in tablet pixels, and which pass read it (A or B).</summary>
public sealed record Bbs757EfbTextLine(string Text, Rectangle Box, IReadOnlyList<Bbs757EfbWord> Words, char Pass)
{
    public Point Centre => new(Box.X + Box.Width / 2, Box.Y + Box.Height / 2);
}

/// <summary>
/// Reads the tablet picture with Windows' built-in OCR (Windows.Media.Ocr: offline, nothing to
/// install; needs the project's target framework to name a Windows 10 SDK, net10.0-windows10.0.19041.0).
///
/// Two passes over the same picture, merged (measured 2026-10-26 on every tablet page):
/// <list type="bullet">
/// <item>A: greyscale by LUMINANCE, stretched to the full range. Greyscale by the brightest channel
/// was tried first and lost every red and green tile: a red tile's red channel is as bright as its
/// white text.</item>
/// <item>B: the same, then a square-root gamma: rescues dim text (the dim green "OFF" of a settings
/// switch) but loses some grey tiles, so it only ADDS a line where pass A found nothing.</item>
/// </list>
/// Both passes upscale x2 first: the tablet's small text (10-12 px) reads badly at 1x.
/// </summary>
public static class Bbs757EfbOcr
{
    /// <summary>How a picture is turned grey for the engine.</summary>
    public enum Filter { Luminance, DarkestChannel }

    public const double Scale = 2.0;

    private static OcrEngine? _engine;

    /// <summary>True when Windows has an OCR engine for one of the user's languages.</summary>
    public static bool IsAvailable => Engine != null;

    private static OcrEngine? Engine =>
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

    /// <summary>Reads a tablet picture (any size; boxes come back in its pixels). Thread-safe enough
    /// for one caller at a time; call from a worker thread.</summary>
    public static async Task<IReadOnlyList<Bbs757EfbTextLine>> ReadAsync(Bitmap tablet, CancellationToken ct = default, double scale = Scale,
        Filter filter = Filter.Luminance)
    {
        var engine = Engine ?? throw new InvalidOperationException("No Windows OCR language is installed.");
        var a = await PassAsync(engine, tablet, gamma: false, 'A', scale, filter, ct);
        var b = await PassAsync(engine, tablet, gamma: true, 'B', scale, filter, ct);
        return Merge(a, b);
    }

    /// <summary>
    /// Reads one region only (a value cell, a button) in both passes, merged; boxes in the full
    /// picture's pixels. A small region reads in tens of milliseconds, so a control's read-back
    /// after a tap need not read the whole page.
    /// </summary>
    public static async Task<IReadOnlyList<Bbs757EfbTextLine>> ReadRegionAsync(Bitmap tablet, Rectangle region, CancellationToken ct = default, double scale = 3.0,
        Filter filter = Filter.Luminance)
    {
        region.Intersect(new Rectangle(0, 0, tablet.Width, tablet.Height));
        if (region.Width < 4 || region.Height < 4) return Array.Empty<Bbs757EfbTextLine>();
        // Padding: the engine misses text that touches the picture's edge.
        const int Pad = 12;
        using var crop = new Bitmap(region.Width + 2 * Pad, region.Height + 2 * Pad, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(crop))
        {
            g.Clear(Color.Black);
            g.DrawImage(tablet, new Rectangle(Pad, Pad, region.Width, region.Height), region, GraphicsUnit.Pixel);
        }
        var lines = await ReadAsync(crop, ct, scale, filter);
        return lines.Select(l => l with
        {
            Box = new Rectangle(l.Box.X - Pad + region.X, l.Box.Y - Pad + region.Y, l.Box.Width, l.Box.Height),
            Words = l.Words.Select(w => w with { Box = new Rectangle(w.Box.X - Pad + region.X, w.Box.Y - Pad + region.Y, w.Box.Width, w.Box.Height) }).ToList(),
        }).ToList();
    }

    /// <summary>Pass A's lines, plus every pass B line that overlaps none of them, in reading order
    /// (<see cref="Bbs757EfbReadingOrder"/>).</summary>
    public static IReadOnlyList<Bbs757EfbTextLine> Merge(IReadOnlyList<Bbs757EfbTextLine> a, IReadOnlyList<Bbs757EfbTextLine> b)
    {
        var all = new List<Bbs757EfbTextLine>(a);
        foreach (var l in b)
            if (!a.Any(m => m.Box.IntersectsWith(l.Box))) all.Add(l);
        return Bbs757EfbReadingOrder.Lines(all);
    }

    private static async Task<List<Bbs757EfbTextLine>> PassAsync(OcrEngine engine, Bitmap src, bool gamma, char pass, double scale, Filter filter, CancellationToken ct)
    {
        using var soft = Preprocess(src, gamma, scale, filter);
        var result = await engine.RecognizeAsync(soft).AsTask(ct);
        var lines = new List<Bbs757EfbTextLine>();
        foreach (var line in result.Lines)
        {
            var words = new List<Bbs757EfbWord>();
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
            foreach (var w in line.Words)
            {
                var r = w.BoundingRect;
                var box = Rectangle.FromLTRB((int)(r.X / scale), (int)(r.Y / scale),
                    (int)Math.Ceiling((r.X + r.Width) / scale), (int)Math.Ceiling((r.Y + r.Height) / scale));
                words.Add(new Bbs757EfbWord(w.Text, box));
                x0 = Math.Min(x0, box.Left); y0 = Math.Min(y0, box.Top); x1 = Math.Max(x1, box.Right); y1 = Math.Max(y1, box.Bottom);
            }
            if (words.Count > 0) lines.Add(new Bbs757EfbTextLine(line.Text, Rectangle.FromLTRB(x0, y0, x1, y1), words, pass));
        }
        return lines;
    }

    /// <summary>Upscales x2, greyscales by luminance (BT.601), stretches to the full range, optional
    /// square-root gamma, as a Bgra8 SoftwareBitmap for the OCR engine.</summary>
    private static SoftwareBitmap Preprocess(Bitmap src, bool gamma, double scale, Filter filter)
    {
        int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
        using var big = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        var data = big.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * h];
        try { Marshal.Copy(data.Scan0, px, 0, px.Length); }
        finally { big.UnlockBits(data); }
        if (filter == Filter.DarkestChannel) DarkestChannel(px, gamma); else Luminance(px, gamma);
        var soft = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        soft.CopyFromBuffer(px.AsBuffer());
        return soft;
    }

    /// <summary>In place, BGRA bytes: grey = luminance stretched so the brightest pixel is 255,
    /// optionally square-rooted. Pure; tested.</summary>
    public static bool InvertForTest;

    /// <summary>In place, BGRA bytes: grey = the darkest of the three channels, stretched to full
    /// range, optionally square-rooted. White text stays white; any coloured background goes dark.</summary>
    public static void DarkestChannel(byte[] bgra, bool gamma)
    {
        int max = 1;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int v = Math.Min(bgra[i], Math.Min(bgra[i + 1], bgra[i + 2]));
            if (v > max) max = v;
        }
        double k = 1.0 / max;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            double v = Math.Min(bgra[i], Math.Min(bgra[i + 1], bgra[i + 2])) * k;
            if (v > 1) v = 1;
            if (gamma) v = Math.Sqrt(v);
            byte o = (byte)(v * 255);
            bgra[i] = bgra[i + 1] = bgra[i + 2] = o;
            bgra[i + 3] = 255;
        }
    }

    public static void Luminance(byte[] bgra, bool gamma)
    {
        int max = 1;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int v = (bgra[i] * 29 + bgra[i + 1] * 150 + bgra[i + 2] * 77) >> 8;
            if (v > max) max = v;
        }
        double k = 1.0 / max;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            double v = ((bgra[i] * 29 + bgra[i + 1] * 150 + bgra[i + 2] * 77) >> 8) * k;
            if (v > 1) v = 1;
            if (gamma) v = Math.Sqrt(v);
            if (InvertForTest) v = 1 - v;
            byte o = (byte)(v * 255);
            bgra[i] = bgra[i + 1] = bgra[i + 2] = o;
            bgra[i + 3] = 255;
        }
    }
}
