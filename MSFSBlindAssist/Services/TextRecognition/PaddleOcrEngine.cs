using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RapidOcrNet;
using SkiaSharp;

namespace MSFSBlindAssist.Services.TextRecognition;

/// <summary>One recognized word: its text, where it sits in the picture's pixels and how sure the model is of
/// it (0 to 1).</summary>
public sealed record OcrWord(string Text, Rectangle Box, float Confidence);

/// <summary>
/// One recognized line, trimmed, in the picture's pixels: its text, the rectangle around it, its words in
/// reading order and how sure the model is of the line (the mean of its characters' scores; 1 when it gave none).
/// </summary>
public sealed record OcrLine(string Text, Rectangle Box, IReadOnlyList<OcrWord> Words, float Confidence);

/// <summary>
/// PaddleOCR (PP-OCRv6 small) run through RapidOcrNet and ONNX Runtime: loads the models from the folder the text
/// recognition download fills, and reads one picture at a time into <see cref="OcrLine"/>s.
///
/// <para>It stays free of the rest of the app so that the 757 probe (tools/Bbs757EfbProbe) can compile it next to
/// <c>TextRecognitionModels.cs</c>: it uses only the BCL, RapidOcrNet, SkiaSharp, System.Drawing and
/// <see cref="TextRecognitionModels"/>, and it logs nothing (a caller that cannot <see cref="Load"/> reports why).</para>
/// </summary>
public sealed class PaddleOcrEngine : IDisposable
{
    // Windows' MAX_PATH: from this many characters on, the file calls under ONNX Runtime need a path in extended form.
    private const int MaxPathLength = 260;
    private const string ExtendedPrefix = @"\\?\";
    private const string ExtendedNetworkPrefix = @"\\?\UNC\";

    private readonly Func<SKBitmap, CancellationToken, IReadOnlyList<TextBlock>> _recognize;
    private readonly IDisposable? _owned;
    private readonly SemaphoreSlim _oneRead = new(1, 1);

    /// <param name="recognize">Reads a picture into text blocks: <see cref="Load"/> gives it RapidOcrNet's detect
    /// call, a test its own.</param>
    /// <param name="owned">What the engine disposes with itself (<see cref="Load"/> passes the RapidOcrNet instance).</param>
    internal PaddleOcrEngine(Func<SKBitmap, CancellationToken, IReadOnlyList<TextBlock>> recognize, IDisposable? owned = null)
    {
        _recognize = recognize;
        _owned = owned;
    }

    /// <summary>
    /// How the library reads: its PP-OCRv6 preset (no white border and no long-side cap, so a picture the tablet's
    /// size is read at its native size; a short side under 736 pixels is raised to it, as the v6 models were
    /// exported; the v5 defaults starve the v6 detector), with word boxes on (the reader clicks on words) and the
    /// orientation classifier off (the tablet is upright).
    /// </summary>
    internal static RapidOcrOptions Options { get; } = RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true, DoAngle = false };

    /// <summary>
    /// The threads to give ONNX Runtime, so that reading the tablet leaves the simulator its cores: one per eight
    /// logical processors, at least one, at most four (a page took 548 ms on four threads against 466 ms on all of
    /// them, measured on the owner's PC).
    /// </summary>
    public static int ThreadsFor(int processorCount) => Math.Clamp(processorCount / 8, 1, 4);

    /// <summary>
    /// Loads the four models from <paramref name="folder"/> with <paramref name="threads"/> ONNX Runtime threads.
    /// It reads 32 megabytes and builds the sessions, so call it off the UI thread. Throws when the models cannot
    /// be loaded (missing, cut short or not the models they should be); the caller reports it.
    /// </summary>
    public static PaddleOcrEngine Load(string folder, int threads)
    {
        var ocr = new RapidOcr();
        try
        {
            ocr.InitModels(ModelSet(folder), threads);
            return new PaddleOcrEngine((picture, ct) => ocr.Detect(picture, Options, ct).TextBlocks, ocr);
        }
        catch
        {
            ocr.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The PP-OCRv6 small model set with all four paths pointing into <paramref name="folder"/>, absolute (the
    /// library's own are relative to the app) and, from 260 characters on, in the extended form that file calls
    /// accept past MAX_PATH. The detector's normalization stays the preset's.
    /// </summary>
    internal static RapidOcrModelSet ModelSet(string folder)
    {
        // TextRecognitionModels.Files lists the detector, the recognizer, the dictionary and the classifier, in that
        // order; the classifier is in the list because the library loads it whether or not angles are detected.
        IReadOnlyList<TextRecognitionFile> files = TextRecognitionModels.Files;
        string detector = AbsolutePath(folder, files[0].Name);
        string recognizer = AbsolutePath(folder, files[1].Name);
        string dictionary = AbsolutePath(folder, files[2].Name);
        string classifier = AbsolutePath(folder, files[3].Name);
        return RapidOcrModelSet.PPOCRv6Small with
        {
            DetModelPath = detector,
            ClsModelPath = classifier,
            RecModelPath = recognizer,
            KeysPath = dictionary,
        };
    }

    /// <summary>
    /// Reads <paramref name="picture"/> into lines, with boxes in its pixels. One read runs at a time: a second call
    /// waits its turn outside the engine and can be cancelled there. The picture is copied on the caller's side, so
    /// it must stay valid until the task completes; the recognition (about half a second) runs on a thread-pool
    /// thread, which leaves the caller's thread free.
    /// </summary>
    public async Task<IReadOnlyList<OcrLine>> ReadAsync(Bitmap picture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(picture);
        await _oneRead.WaitAsync(ct);
        try
        {
            using SKBitmap pixels = ToSkBitmap(picture);
            IReadOnlyList<TextBlock> blocks = await Task.Run(() => _recognize(pixels, ct), ct);
            return ToLines(blocks);
        }
        finally
        {
            _oneRead.Release();
        }
    }

    /// <summary>
    /// RapidOcrNet's text blocks as lines: blank blocks dropped, the text trimmed, the box the rectangle around the
    /// detected outline (tilted or not) and the confidence the mean of the character scores (1 when it gave none).
    /// The words are the library's own word boxes, in order, when it gave any; otherwise the line's words share its
    /// box in proportion to their characters (blanks counted), each at least a pixel wide, with the line's confidence.
    /// </summary>
    internal static IReadOnlyList<OcrLine> ToLines(IReadOnlyList<TextBlock> blocks)
    {
        var lines = new List<OcrLine>(blocks.Count);
        foreach (TextBlock block in blocks)
        {
            string text = block.Text.Trim();
            if (text.Length == 0) continue;

            Rectangle box = Bounds(block.BoxPoints);
            float confidence = block.CharScores is { Length: > 0 } scores ? scores.Average() : 1f;
            lines.Add(new OcrLine(text, box, WordsOf(block, text, box, confidence), confidence));
        }
        return lines;
    }

    /// <summary>
    /// Copies the picture's pixels into a BGRA bitmap for the library, with no PNG encode and decode between. GDI+
    /// converts any source format (24-bit, indexed, ...) to 32-bit ARGB as it locks the bits, and the rows are
    /// copied one at a time, so the picture's stride (rows padded past their pixels) and the copy's need not
    /// agree. The alpha is kept as it is, not premultiplied. The caller disposes the result and still owns
    /// <paramref name="picture"/>.
    /// </summary>
    internal static SKBitmap ToSkBitmap(Bitmap picture)
    {
        int width = picture.Width;
        int height = picture.Height;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        try
        {
            BitmapData source = picture.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[width * 4];
                IntPtr target = bitmap.GetPixels();
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(source.Scan0 + y * source.Stride, row, 0, row.Length);
                    Marshal.Copy(row, 0, target + y * bitmap.RowBytes, row.Length);
                }
            }
            finally
            {
                picture.UnlockBits(source);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Releases the models. Not safe during a read: the caller (the 757 bridge) makes sure none runs.</summary>
    public void Dispose()
    {
        _owned?.Dispose();
        _oneRead.Dispose();
    }

    /// <summary>The full path of <paramref name="name"/> in <paramref name="folder"/>; from 260 characters on the
    /// extended form: <c>\\?\C:\...</c>, or <c>\\?\UNC\server\...</c> for a network path. One that already is
    /// extended is left alone.</summary>
    private static string AbsolutePath(string folder, string name)
    {
        string path = Path.GetFullPath(Path.Combine(folder, name));
        if (path.Length < MaxPathLength || path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? ExtendedNetworkPrefix + path[2..] : ExtendedPrefix + path;
    }

    /// <summary>The rectangle around every corner of an outline, as <see cref="Rectangle.FromLTRB"/> reads it.</summary>
    private static Rectangle Bounds(SKPointI[] outline)
        => Rectangle.FromLTRB(outline.Min(point => point.X), outline.Min(point => point.Y), outline.Max(point => point.X), outline.Max(point => point.Y));

    private static List<OcrWord> WordsOf(TextBlock block, string text, Rectangle box, float confidence)
    {
        if (block.WordResults is { Length: > 0 } wordBoxes)
            return wordBoxes.Select(word => new OcrWord(word.Text, Bounds(word.BoxPoints), word.Score)).ToList();

        // No word boxes: each word takes the share of the line's box that its characters take.
        var words = new List<OcrWord>();
        int start = 0;
        while (start < text.Length)
        {
            if (char.IsWhiteSpace(text[start]))
            {
                start++;
                continue;
            }

            int end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            int left = box.X + box.Width * start / text.Length;
            int right = box.X + box.Width * end / text.Length;
            words.Add(new OcrWord(text[start..end], new Rectangle(left, box.Y, Math.Max(1, right - left), box.Height), confidence));
            start = end;
        }
        return words;
    }
}
