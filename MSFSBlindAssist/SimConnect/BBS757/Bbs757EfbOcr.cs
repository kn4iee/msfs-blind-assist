using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Services.TextRecognition;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One recognised word, in tablet pixels (the agent's click coordinates).</summary>
public sealed record Bbs757EfbWord(string Text, Rectangle Box);

/// <summary>One recognised line, in tablet pixels, and which pass read it (P: PaddleOCR; K: text a page template
/// knows, put in by the parser).</summary>
public sealed record Bbs757EfbTextLine(string Text, Rectangle Box, IReadOnlyList<Bbs757EfbWord> Words, char Pass)
{
    public Point Centre => new(Box.X + Box.Width / 2, Box.Y + Box.Height / 2);
}

/// <summary>
/// Reads the tablet picture with PaddleOCR (PP-OCRv6 small, through <see cref="PaddleOcrEngine"/>): ONE read of
/// the whole picture at the tablet's own size, with one preprocessing step, the brightening (<see cref="Brighten"/>),
/// and no other (no gray filter, no upscaling, no second pass) and no close-up re-reads; Windows OCR needed them,
/// PaddleOCR does not. Measured on the 11 test pages, dim and bright, through the parser (2026-10-28), on the raw
/// pictures and so before the brightening: 98.4% of page elements right, against 91.8% for Windows OCR with its two
/// passes and re-reads. The brightening is plan 18's: at 0 percent tablet brightness (white text at 80 of 255) the
/// small model missed faint text on the raw picture, the Maintenance page's pink gear-door OPEN buttons, and found it
/// with the white scaled to 255.
/// </summary>
public static class Bbs757EfbOcr
{
    /// <summary>The first row that can set the picture's white. The top bar above it (<see cref="Bbs757EfbScreen.StatusBottom"/>
    /// and a margin: the clock, the departure countdown, the boarding box) never does.</summary>
    private const int WhiteTop = Bbs757EfbScreen.StatusBottom + 4;

    /// <summary>The least white, as a luminance, that the picture is scaled from (<see cref="Bbs757EfbPixels"/>
    /// floors its white the same way): a picture that shows next to nothing is not blown up into noise.</summary>
    private const int WhiteFloor = 30;

    /// <summary>Reads a tablet picture (any size; boxes come back in its pixels) after brightening a copy of it
    /// (<see cref="Brighten"/>), which is released when the read is over. The engine reads one picture at a time and
    /// runs the recognition on a thread-pool thread; <paramref name="tablet"/> is only looked at while the copy is
    /// made.</summary>
    public static async Task<IReadOnlyList<Bbs757EfbTextLine>> ReadAsync(PaddleOcrEngine engine, Bitmap tablet, CancellationToken ct = default)
    {
        using Bitmap bright = Brighten(tablet);
        return FromEngine(await engine.ReadAsync(bright, ct));
    }

    /// <summary>
    /// A copy of the tablet picture, 24 bits per pixel, with every channel scaled so that the picture's white is
    /// 255: each channel times 255 over the brightest luminance (0.299 R + 0.587 G + 0.114 B, as
    /// <see cref="Bbs757EfbPixels"/> weighs it) from row <see cref="WhiteTop"/> down, at least
    /// <see cref="WhiteFloor"/>. Every pixel of those rows is looked at, so a lone bright one sets the scale. The
    /// rows above (the top bar) are scaled like the rest but never set the scale. A picture of
    /// <see cref="WhiteTop"/> rows or fewer has no such row and keeps the floor. The size is the tablet's, so a
    /// box read from the copy is in the tablet's pixels. The caller disposes the copy and still owns
    /// <paramref name="tablet"/>.
    /// </summary>
    internal static Bitmap Brighten(Bitmap tablet)
    {
        ArgumentNullException.ThrowIfNull(tablet);
        int width = tablet.Width, height = tablet.Height;
        // GDI+ converts any source format to 24 bits as it locks; a row is B, G, R per pixel and is padded to its stride.
        BitmapData source = tablet.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            long white = WhiteOf(source, width, height);
            var bright = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            try
            {
                ScaleInto(source, bright, white);
                return bright;
            }
            catch
            {
                bright.Dispose();
                throw;
            }
        }
        finally
        {
            tablet.UnlockBits(source);
        }
    }

    /// <summary>The picture's white, as a luminance in thousandths (whole numbers, so that the scale is exact: 40 of
    /// 80 is 127.5, and rounds up to 128): the brightest pixel from <see cref="WhiteTop"/> down, at least
    /// <see cref="WhiteFloor"/>.</summary>
    private static long WhiteOf(BitmapData source, int width, int height)
    {
        int rowBytes = width * 3;
        var row = new byte[rowBytes];
        long white = WhiteFloor * 1000L;
        for (int y = Math.Min(WhiteTop, height); y < height; y++)
        {
            Marshal.Copy(source.Scan0 + y * source.Stride, row, 0, rowBytes);
            for (int i = 0; i < rowBytes; i += 3)
            {
                long luminance = 114 * row[i] + 587 * row[i + 1] + 299 * row[i + 2];
                if (luminance > white) white = luminance;
            }
        }
        return white;
    }

    /// <summary>Every byte of <paramref name="source"/> times 255 over <paramref name="white"/> (a luminance in
    /// thousandths), rounded and held at 255, into <paramref name="target"/>, a 24-bit bitmap of the same size.</summary>
    private static void ScaleInto(BitmapData source, Bitmap target, long white)
    {
        var scaled = new byte[256];
        for (int value = 0; value < scaled.Length; value++)
            scaled[value] = (byte)Math.Min(255L, (value * 255_000L + white / 2) / white);

        int rowBytes = target.Width * 3;
        var row = new byte[rowBytes];
        BitmapData bits = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int y = 0; y < target.Height; y++)
            {
                Marshal.Copy(source.Scan0 + y * source.Stride, row, 0, rowBytes);
                for (int i = 0; i < rowBytes; i++) row[i] = scaled[row[i]];
                Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, rowBytes);
            }
        }
        finally
        {
            target.UnlockBits(bits);
        }
    }

    /// <summary>The engine's lines as the reader's: each line with its box and its words, pass P, in reading
    /// order (<see cref="Bbs757EfbReadingOrder"/>). Pure; tested.</summary>
    internal static IReadOnlyList<Bbs757EfbTextLine> FromEngine(IReadOnlyList<OcrLine> lines) =>
        Bbs757EfbReadingOrder.Lines(lines.Select(line => new Bbs757EfbTextLine(line.Text, line.Box,
            line.Words.Select(word => new Bbs757EfbWord(word.Text, word.Box)).ToList(), 'P')));
}
