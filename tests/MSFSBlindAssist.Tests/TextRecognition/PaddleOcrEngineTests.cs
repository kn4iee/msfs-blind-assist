using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Services.TextRecognition;
using RapidOcrNet;
using SkiaSharp;

namespace MSFSBlindAssist.Tests.TextRecognition;

/// <summary>
/// The engine host around RapidOcrNet: the settings it runs with, the paths it loads, the conversions between
/// the tablet picture, the library's text blocks and the lines the 757 reader takes, and its one read at a
/// time. No test loads the real models (CI has none): recognition is a fake behind the engine's test seam.
/// </summary>
public sealed class PaddleOcrEngineTests
{
    private const string Classifier = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";   // the longest of the four names
    private const string ExtendedPrefix = @"\\?\";

    // How long a test waits for something that must happen, and how long it watches for something that must not.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    // Six pixels in two rows of three (Palette[y, x]), chosen so that a swapped channel, a transposed row or a
    // shifted column each show up; one pixel is translucent.
    private static readonly Color[,] Palette =
    {
        { Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 0, 0, 255) },
        { Color.FromArgb(255, 10, 20, 30), Color.FromArgb(128, 200, 100, 50), Color.FromArgb(255, 250, 240, 230) },
    };

    // ---- the settings ----

    [Theory]
    [InlineData(0, 1)]     // never none, whatever the count says
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    [InlineData(15, 1)]    // an eighth rounds down
    [InlineData(16, 2)]
    [InlineData(24, 3)]
    [InlineData(31, 3)]
    [InlineData(32, 4)]
    [InlineData(64, 4)]    // and never more than four
    public void ThreadsFor_is_an_eighth_of_the_processors_from_one_to_four(int processors, int threads)
        => Assert.Equal(threads, PaddleOcrEngine.ThreadsFor(processors));

    [Fact]
    public void Options_read_at_native_size_with_word_boxes_and_no_orientation()
    {
        RapidOcrOptions options = PaddleOcrEngine.Options;

        Assert.Equal(0, options.Padding);          // no white border around the tablet
        Assert.Equal(0, options.ImgResize);        // not the long-side cap the v5 model was tuned for
        Assert.Equal(736, options.LimitSideLen);   // short-side adaptive resize, as the v6 model was exported
        Assert.True(options.ReturnWordBox);        // the reader clicks on words
        Assert.False(options.DoAngle);             // the tablet is upright

        // Everything else is the PP-OCRv6 preset as RapidOcrNet ships it.
        Assert.Equal(RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true, DoAngle = false }, options);
    }

    // ---- the model paths ----

    [Fact]
    public void ModelSet_points_into_the_folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "msfsba-textrec", "models");

        RapidOcrModelSet set = PaddleOcrEngine.ModelSet(folder);

        // Each name is pinned to the role it plays, so a reordered list cannot swap the detector and the recognizer.
        Assert.Equal(Path.Combine(folder, "PP-OCRv6_det_small.onnx"), set.DetModelPath);
        Assert.Equal(Path.Combine(folder, "PP-OCRv6_rec_small.onnx"), set.RecModelPath);
        Assert.Equal(Path.Combine(folder, "ppocrv6_dict.txt"), set.KeysPath);
        Assert.Equal(Path.Combine(folder, Classifier), set.ClsModelPath);

        // The engine loads exactly the files the download delivers: no more, no fewer.
        string[] loaded = { set.DetModelPath, set.RecModelPath, set.KeysPath, set.ClsModelPath };
        Assert.Equal(
            TextRecognitionModels.Files.Select(file => file.Name).Order(StringComparer.Ordinal),
            loaded.Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal));

        // The detector's normalization is the preset's.
        Assert.Equal(RapidOcrModelSet.PPOCRv6Small.DetMean, set.DetMean);
        Assert.Equal(RapidOcrModelSet.PPOCRv6Small.DetStd, set.DetStd);
    }

    [Fact]
    public void ModelSet_makes_every_path_absolute()
    {
        string folder = Path.Combine("relative", "models");

        RapidOcrModelSet set = PaddleOcrEngine.ModelSet(folder);

        Assert.All(new[] { set.DetModelPath, set.RecModelPath, set.KeysPath, set.ClsModelPath },
            path => Assert.True(Path.IsPathFullyQualified(path), path));
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "ppocrv6_dict.txt")), set.KeysPath);
    }

    [Fact]
    public void ModelSet_prefixes_a_path_of_260_characters_or_more_and_only_that_path()
    {
        // A folder that makes the classifier's path exactly `length` characters long (C:\ + folder + \ + name).
        static string FolderFor(int length) => @"C:\" + new string('a', length - @"C:\".Length - 1 - Classifier.Length);

        RapidOcrModelSet under = PaddleOcrEngine.ModelSet(FolderFor(259));
        Assert.Equal(259, under.ClsModelPath.Length);
        Assert.Equal(Path.Combine(FolderFor(259), Classifier), under.ClsModelPath);

        RapidOcrModelSet over = PaddleOcrEngine.ModelSet(FolderFor(260));
        Assert.Equal(ExtendedPrefix + Path.Combine(FolderFor(260), Classifier), over.ClsModelPath);
        // The shorter names stay under the limit in the same folder: the prefix goes by each path, not by the folder.
        Assert.Equal(Path.Combine(FolderFor(260), "PP-OCRv6_det_small.onnx"), over.DetModelPath);
        Assert.Equal(Path.Combine(FolderFor(260), "PP-OCRv6_rec_small.onnx"), over.RecModelPath);
        Assert.Equal(Path.Combine(FolderFor(260), "ppocrv6_dict.txt"), over.KeysPath);
    }

    [Fact]
    public void ModelSet_prefixes_a_long_network_path_the_network_way_and_never_twice()
    {
        string unc = @"\\server\share\" + new string('a', 220);
        Assert.Equal(@"\\?\UNC\server\share\" + new string('a', 220) + @"\" + Classifier, PaddleOcrEngine.ModelSet(unc).ClsModelPath);

        string extended = ExtendedPrefix + @"C:\" + new string('a', 220);
        Assert.Equal(extended + @"\" + Classifier, PaddleOcrEngine.ModelSet(extended).ClsModelPath);
    }

    // ---- text blocks to lines ----

    [Fact]
    public void ToLines_takes_the_outline_bounds_and_trims()
    {
        // A tilted outline: the box is the rectangle around all four corners, not just two of them.
        var outline = new[] { new SKPointI(102, 12), new SKPointI(190, 10), new SKPointI(188, 29), new SKPointI(100, 31) };

        OcrLine line = OnlyLine(Block("  TOD PAUSE \t", outline, scores: new[] { 0.5f, 0.75f }));

        Assert.Equal("TOD PAUSE", line.Text);
        Assert.Equal(new Rectangle(100, 10, 90, 21), line.Box);
        Assert.Equal(0.625f, line.Confidence);
    }

    [Theory]
    [InlineData(null, 1f)]                                // no scores: sure
    [InlineData(new float[0], 1f)]                        // ... and an empty list is no scores, not a mean of nothing
    [InlineData(new[] { 0.75f }, 0.75f)]
    [InlineData(new[] { 1f, 0.5f, 0f }, 0.5f)]
    public void ToLines_confidence_is_the_mean_of_the_character_scores_or_one(float[]? scores, float expected)
        => Assert.Equal(expected, OnlyLine(Block("ALT", Corners(0, 0, 30, 10), scores)).Confidence);

    [Fact]
    public void ToLines_keeps_word_boxes_in_order()
    {
        // The words' own outlines and scores, in the order the library gave them; the line's confidence
        // still comes from its characters, not from the words.
        WordBox[] words =
        {
            WordAt("TOD", new[] { new SKPointI(100, 12), new SKPointI(128, 10), new SKPointI(127, 30), new SKPointI(101, 31) }, 0.5f),
            WordAt("PAUSE", Corners(141, 10, 190, 30), 0.25f),
            WordAt("ARMED", Corners(200, 10, 250, 30), 0.75f),
        };

        OcrLine line = OnlyLine(Block("TOD PAUSE ARMED", Corners(100, 10, 250, 31), scores: new[] { 1f, 0f }, words: words));

        Assert.Equal(new[]
        {
            new OcrWord("TOD", new Rectangle(100, 10, 28, 21), 0.5f),
            new OcrWord("PAUSE", new Rectangle(141, 10, 49, 20), 0.25f),
            new OcrWord("ARMED", new Rectangle(200, 10, 50, 20), 0.75f),
        }, line.Words);
        Assert.Equal(0.5f, line.Confidence);
    }

    [Theory]
    [InlineData("en-US", false)]
    [InlineData("de-DE", false)]
    [InlineData("sv-SE", false)]
    [InlineData("en-US", true)]    // an empty list of word boxes is no word boxes
    [InlineData("de-DE", true)]
    [InlineData("sv-SE", true)]
    public void ToLines_splits_evenly_without_word_boxes(string culture, bool emptyWordList)
    {
        // Nine characters across 90 pixels, the space included: ten pixels each.
        TextBlock block = Block("TOD PAUSE", Corners(100, 10, 190, 30), scores: new[] { 0.5f, 0.75f },
            words: emptyWordList ? Array.Empty<WordBox>() : null);

        OcrLine line = TestCulture.Run(culture, () => OnlyLine(block));

        Assert.Equal(new[]
        {
            new OcrWord("TOD", new Rectangle(100, 10, 30, 20), 0.625f),     // characters 0-3: x 100 to 130
            new OcrWord("PAUSE", new Rectangle(140, 10, 50, 20), 0.625f),   // characters 4-9: x 140 to 190
        }, line.Words);
    }

    [Fact]
    public void ToLines_splits_around_runs_of_blanks_and_gives_a_lone_word_the_whole_box()
    {
        // The blanks count as characters: eleven across 110 pixels.
        OcrLine spaced = OnlyLine(Block("TOD   PAUSE", Corners(0, 5, 110, 25)));
        Assert.Equal(new[] { new Rectangle(0, 5, 30, 20), new Rectangle(60, 5, 50, 20) }, spaced.Words.Select(word => word.Box));
        Assert.Equal(new[] { "TOD", "PAUSE" }, spaced.Words.Select(word => word.Text));

        OcrLine lone = OnlyLine(Block("ALTITUDE", Corners(10, 5, 90, 25)));
        Assert.Equal(new OcrWord("ALTITUDE", new Rectangle(10, 5, 80, 20), 1f), Assert.Single(lone.Words));
    }

    [Fact]
    public void ToLines_gives_every_split_word_at_least_one_pixel()
    {
        // Two pixels for five characters: the first word's share rounds to nothing.
        OcrLine line = OnlyLine(Block("AB CD", Corners(10, 0, 12, 8)));

        Assert.Equal(new[] { new Rectangle(10, 0, 1, 8), new Rectangle(11, 0, 1, 8) }, line.Words.Select(word => word.Box));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t ")]
    public void ToLines_drops_blank_blocks(string blank)
    {
        IReadOnlyList<OcrLine> lines = PaddleOcrEngine.ToLines(new[]
        {
            Block("FLAPS", Corners(0, 0, 50, 10)),
            Block(blank, Corners(0, 20, 50, 30)),
            Block("GEAR", Corners(0, 40, 40, 50)),
        });

        Assert.Equal(new[] { "FLAPS", "GEAR" }, lines.Select(line => line.Text));
        Assert.Empty(PaddleOcrEngine.ToLines(Array.Empty<TextBlock>()));
    }

    // ---- the picture to pixels ----

    [Fact]
    public void ToSkBitmap_copies_pixels_in_order()
    {
        using Bitmap picture = Picture(PixelFormat.Format32bppArgb, Palette);

        using SKBitmap converted = PaddleOcrEngine.ToSkBitmap(picture);

        Assert.Equal(SKColorType.Bgra8888, converted.ColorType);
        Assert.Equal(SKAlphaType.Unpremul, converted.AlphaType);   // the translucent pixel is copied as it is, not scaled
        AssertPixels(Palette, converted);
    }

    [Theory]
    [InlineData(PixelFormat.Format24bppRgb)]
    [InlineData(PixelFormat.Format32bppRgb)]
    public void ToSkBitmap_reads_any_source_format_and_a_picture_without_alpha_is_opaque(PixelFormat format)
    {
        using Bitmap picture = Picture(format, Palette);

        using SKBitmap converted = PaddleOcrEngine.ToSkBitmap(picture);

        var opaque = (Color[,])Palette.Clone();
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                opaque[y, x] = Color.FromArgb(255, opaque[y, x]);
        AssertPixels(opaque, converted);
    }

    [Fact]
    public void ToSkBitmap_honors_a_source_stride_wider_than_its_pixels()
    {
        // Three pixels of four bytes, rows sixteen bytes apart: the four bytes between the rows are padding.
        const int Stride = 16;
        var memory = new byte[Stride * 2];
        Array.Fill(memory, (byte)0xEE);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Color color = Palette[y, x];
                int at = y * Stride + x * 4;
                memory[at] = color.B;
                memory[at + 1] = color.G;
                memory[at + 2] = color.R;
                memory[at + 3] = color.A;
            }
        }

        GCHandle pin = GCHandle.Alloc(memory, GCHandleType.Pinned);
        try
        {
            using var picture = new Bitmap(3, 2, Stride, PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());

            using SKBitmap converted = PaddleOcrEngine.ToSkBitmap(picture);

            AssertPixels(Palette, converted);
        }
        finally
        {
            pin.Free();
        }
    }

    // ---- reading ----

    [Fact]
    public async Task ReadAsync_converts_the_picture_recognizes_it_and_returns_the_lines()
    {
        SKBitmap? recognized = null;
        (int Width, int Height, SKColor Corner) seen = default;
        using var engine = new PaddleOcrEngine((bitmap, _) =>
        {
            recognized = bitmap;
            seen = (bitmap.Width, bitmap.Height, bitmap.GetPixel(2, 1));
            return new[] { Block(" TOD PAUSE ", Corners(100, 10, 190, 30), scores: new[] { 0.5f, 0.75f }) };
        });
        using Bitmap picture = Picture(PixelFormat.Format32bppArgb, Palette);

        IReadOnlyList<OcrLine> lines = await engine.ReadAsync(picture, CancellationToken.None);

        OcrLine line = Assert.Single(lines);
        Assert.Equal("TOD PAUSE", line.Text);
        Assert.Equal(new Rectangle(100, 10, 90, 20), line.Box);
        Assert.Equal((3, 2, new SKColor(250, 240, 230)), seen);   // the library saw this picture's pixels
        Assert.NotNull(recognized);
        Assert.Equal(IntPtr.Zero, recognized.Handle);             // ... and its copy is released once the read is over
    }

    [Fact]
    public async Task ReadAsync_runs_recognition_off_the_callers_thread()
    {
        // Recognition waits for a signal that only the caller gives, after ReadAsync has returned. Run on the
        // caller's own thread it would never get it and would time out.
        using var callerHasTheTask = new ManualResetEventSlim();
        bool gotTheSignal = false;
        using var engine = new PaddleOcrEngine((_, _) =>
        {
            gotTheSignal = callerHasTheTask.Wait(Patience);
            return Array.Empty<TextBlock>();
        });
        using var picture = new Bitmap(2, 2);

        Task<IReadOnlyList<OcrLine>> read = engine.ReadAsync(picture, CancellationToken.None);
        callerHasTheTask.Set();
        await read.WaitAsync(Patience);

        Assert.True(gotTheSignal);
    }

    [Fact]
    public async Task ReadAsync_runs_one_read_at_a_time()
    {
        var held = new HeldRecognition();
        using var engine = new PaddleOcrEngine(held.Recognize);
        using var picture = new Bitmap(2, 2);

        Task first = engine.ReadAsync(picture, CancellationToken.None);
        await held.WaitForEntered(1);
        Task second = engine.ReadAsync(picture, CancellationToken.None);
        await Task.Delay(Quiet);

        Assert.Equal(1, held.Entered);   // the second read waits outside, it does not recognize alongside the first

        held.Let(2);
        await Task.WhenAll(first, second).WaitAsync(Patience);
        Assert.Equal(2, held.Entered);
        Assert.Equal(1, held.MostInside);
    }

    [Fact]
    public async Task A_failed_read_releases_its_copy_and_the_next_read_goes_ahead()
    {
        SKBitmap? recognized = null;
        int calls = 0;
        using var engine = new PaddleOcrEngine((bitmap, _) =>
        {
            recognized = bitmap;
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("boom");
            return Array.Empty<TextBlock>();
        });
        using var picture = new Bitmap(2, 2);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.ReadAsync(picture, CancellationToken.None));
        Assert.Equal("boom", failure.Message);
        Assert.Equal(IntPtr.Zero, recognized!.Handle);

        // A read that kept the lock would never get here.
        Assert.Empty(await engine.ReadAsync(picture, CancellationToken.None).WaitAsync(Patience));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_read_cancelled_while_it_waits_never_recognizes_and_leaves_the_lock_whole()
    {
        var held = new HeldRecognition();
        using var engine = new PaddleOcrEngine(held.Recognize);
        using var picture = new Bitmap(2, 2);
        Task first = engine.ReadAsync(picture, CancellationToken.None);
        await held.WaitForEntered(1);

        using var cancel = new CancellationTokenSource();
        Task waiting = engine.ReadAsync(picture, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        held.Let(1);
        await first.WaitAsync(Patience);
        Assert.Equal(1, held.Entered);   // the cancelled read never reached recognition

        // The cancelled wait neither took the lock nor gave one back: reads are still one at a time.
        Task third = engine.ReadAsync(picture, CancellationToken.None);
        await held.WaitForEntered(2);
        Task fourth = engine.ReadAsync(picture, CancellationToken.None);
        await Task.Delay(Quiet);
        Assert.Equal(2, held.Entered);

        held.Let(2);
        await Task.WhenAll(third, fourth).WaitAsync(Patience);
        Assert.Equal(1, held.MostInside);
    }

    [Fact]
    public void Dispose_disposes_what_the_engine_owns()
    {
        var owned = new CountingDisposable();
        var engine = new PaddleOcrEngine((_, _) => Array.Empty<TextBlock>(), owned);

        engine.Dispose();

        Assert.Equal(1, owned.Disposed);
    }

    // ---- loading ----

    [Fact]
    public void Load_throws_when_the_models_are_not_there()
    {
        using var folder = new ScratchFolder();

        // RapidOcrNet looks for each file before it loads one, so this fails before ONNX Runtime is touched.
        Assert.Throws<FileNotFoundException>(() => PaddleOcrEngine.Load(folder.FullName, 1));
    }

    [Fact]
    public void Load_throws_when_a_model_is_not_a_model()
    {
        using var folder = new ScratchFolder();
        foreach (TextRecognitionFile file in TextRecognitionModels.Files)
            File.WriteAllText(Path.Combine(folder.FullName, file.Name), "not a model");

        // ONNX Runtime itself runs here and turns the file down. That also shows its native library is in the
        // test output (a missing one would throw a DllNotFoundException instead) and that the half-built
        // engine's cleanup does not hide the error behind another.
        Assert.Throws<Microsoft.ML.OnnxRuntime.OnnxRuntimeException>(() => PaddleOcrEngine.Load(folder.FullName, 1));
    }

    // ---- builders ----

    /// <summary>The four corners of an upright box, clockwise from the top left, as the detector gives them.</summary>
    private static SKPointI[] Corners(int left, int top, int right, int bottom)
        => new[] { new SKPointI(left, top), new SKPointI(right, top), new SKPointI(right, bottom), new SKPointI(left, bottom) };

    private static TextBlock Block(string text, SKPointI[] outline, float[]? scores = null, WordBox[]? words = null)
        => new() { Text = text, BoxPoints = outline, Chars = null, CharScores = scores, WordResults = words };

    private static WordBox WordAt(string text, SKPointI[] outline, float score)
        => new() { Text = text, BoxPoints = outline, Score = score };

    private static OcrLine OnlyLine(TextBlock block) => Assert.Single(PaddleOcrEngine.ToLines(new[] { block }));

    /// <summary>A bitmap of <paramref name="format"/> holding <paramref name="colors"/> (row, then column).</summary>
    private static Bitmap Picture(PixelFormat format, Color[,] colors)
    {
        var picture = new Bitmap(colors.GetLength(1), colors.GetLength(0), format);
        for (int y = 0; y < colors.GetLength(0); y++)
            for (int x = 0; x < colors.GetLength(1); x++)
                picture.SetPixel(x, y, colors[y, x]);
        return picture;
    }

    private static void AssertPixels(Color[,] expected, SKBitmap actual)
    {
        Assert.Equal(expected.GetLength(1), actual.Width);
        Assert.Equal(expected.GetLength(0), actual.Height);
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                var want = new SKColor(expected[y, x].R, expected[y, x].G, expected[y, x].B, expected[y, x].A);
                SKColor got = actual.GetPixel(x, y);
                Assert.True(want == got, $"pixel {x},{y}: expected {want}, got {got}");
            }
        }
    }

    /// <summary>A recognition that keeps every read it is given until the test lets it go, counting how many
    /// have come in and how many were inside at once.</summary>
    private sealed class HeldRecognition
    {
        private readonly SemaphoreSlim _go = new(0);
        private readonly object _gate = new();
        private int _entered;
        private int _inside;
        private int _mostInside;

        public int Entered { get { lock (_gate) return _entered; } }

        public int MostInside { get { lock (_gate) return _mostInside; } }

        public IReadOnlyList<TextBlock> Recognize(SKBitmap bitmap, CancellationToken cancel)
        {
            lock (_gate)
            {
                _entered++;
                _inside++;
                _mostInside = Math.Max(_mostInside, _inside);
            }
            try
            {
                if (!_go.Wait(Patience)) throw new TimeoutException("The test never let this read go.");
            }
            finally
            {
                lock (_gate) _inside--;
            }
            return Array.Empty<TextBlock>();
        }

        /// <summary>Lets <paramref name="reads"/> of the held reads go.</summary>
        public void Let(int reads) => _go.Release(reads);

        /// <summary>Waits until <paramref name="count"/> reads have come in.</summary>
        public async Task WaitForEntered(int count)
        {
            DateTime until = DateTime.UtcNow + Patience;
            while (Entered < count)
            {
                if (DateTime.UtcNow > until) throw new TimeoutException($"Only {Entered} of {count} reads came in.");
                await Task.Delay(5);
            }
        }
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    /// <summary>An empty folder under the temp folder, removed again on dispose.</summary>
    private sealed class ScratchFolder : IDisposable
    {
        public string FullName { get; } = Path.Combine(Path.GetTempPath(), "msfsba-paddle-" + Guid.NewGuid().ToString("N"));

        public ScratchFolder() => Directory.CreateDirectory(FullName);

        public void Dispose()
        {
            try { Directory.Delete(FullName, recursive: true); } catch (IOException) { }
        }
    }
}
