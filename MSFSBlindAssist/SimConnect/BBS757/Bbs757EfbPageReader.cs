using System.Drawing;
using MSFSBlindAssist.Services.TextRecognition;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One read of the tablet: the page model, the lines it was built from, and how long the read took
/// (the picture's pixels to the parsed page).</summary>
public sealed record Bbs757EfbPageRead(Bbs757EfbScreenModel Model, IReadOnlyList<Bbs757EfbTextLine> Lines, long OcrMs);

/// <summary>
/// Reads a captured tablet picture into its page model: one PaddleOCR read of the whole page
/// (<see cref="Bbs757EfbOcr.ReadAsync"/>), then <see cref="Bbs757EfbScreen.Parse"/>. Nothing is read again closer:
/// those re-reads were for Windows OCR's misses on small text and colored tiles, which PaddleOCR reads in the one
/// pass. Shared by the app (Bbs757EfbBridge) and the probe tool, so what the probe proves is what ships.
/// </summary>
public static class Bbs757EfbPageReader
{
    public static async Task<Bbs757EfbPageRead> ReadAsync(PaddleOcrEngine engine, Bitmap tablet, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var px = Bbs757EfbPixels.FromBitmap(tablet);
        var lines = await Bbs757EfbOcr.ReadAsync(engine, tablet, ct);
        var model = Bbs757EfbScreen.Parse(lines, px);
        return new Bbs757EfbPageRead(model, lines, sw.ElapsedMilliseconds);
    }
}
