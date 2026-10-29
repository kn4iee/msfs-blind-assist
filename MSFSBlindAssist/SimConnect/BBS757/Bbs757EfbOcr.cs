using System.Drawing;
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
/// the whole picture at the tablet's own size, with no preprocessing (no gray filter, no upscaling, no second
/// pass) and no close-up re-reads; Windows OCR needed them, PaddleOCR does not. Measured on the 11 test pages,
/// dim and bright, through the parser (2026-10-28): 98.4% of page elements right, against 91.8% for Windows OCR
/// with its two passes and re-reads.
/// </summary>
public static class Bbs757EfbOcr
{
    /// <summary>Reads a tablet picture (any size; boxes come back in its pixels). The engine reads one picture at
    /// a time and runs the recognition on a thread-pool thread; <paramref name="tablet"/> must stay valid until
    /// the task completes.</summary>
    public static async Task<IReadOnlyList<Bbs757EfbTextLine>> ReadAsync(PaddleOcrEngine engine, Bitmap tablet, CancellationToken ct = default)
        => FromEngine(await engine.ReadAsync(tablet, ct));

    /// <summary>The engine's lines as the reader's: each line with its box and its words, pass P, in reading
    /// order (<see cref="Bbs757EfbReadingOrder"/>). Pure; tested.</summary>
    internal static IReadOnlyList<Bbs757EfbTextLine> FromEngine(IReadOnlyList<OcrLine> lines) =>
        Bbs757EfbReadingOrder.Lines(lines.Select(line => new Bbs757EfbTextLine(line.Text, line.Box,
            line.Words.Select(word => new Bbs757EfbWord(word.Text, word.Box)).ToList(), 'P')));
}
