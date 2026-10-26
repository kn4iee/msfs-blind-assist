using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's ILS control panel display, read from a picture (plan 04, task 6b). The panel's
/// frequency is in no variable: its display is a WASM gauge, laid by the app over the simulator's 2D UI
/// page with a filter that leaves white segments on black, captured with the window and decoded here as
/// a seven-segment display. The fixtures are round 5's crops (docs/b757/evidence/flight-04.md, "Round 5:
/// reading the ILS display"), each named after what the panel showed: "ocr_110.50_000.png" is 110.50 with
/// course 000, "ocr_PARK_000.png" the ILS parked. A picture that is not a valid ILS display decodes to
/// nothing, never to a guess.
/// </summary>
public class Bbs757IlsOcrTests
{
    private static string FixtureDir =>
        Path.Combine(Bbs757SeamsTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "Fixtures", "bbs757-ils");

    /// <summary>A fixture, read into memory (a bitmap made from a file keeps the file locked).</summary>
    private static Bitmap Load(string name)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(Path.Combine(FixtureDir, name)));
        using var image = new Bitmap(stream);
        return new Bitmap(image);
    }

    /// <summary>The ten filtered crops, by file name.</summary>
    private static List<string> Crops()
        => Directory.GetFiles(FixtureDir, "ocr_*.png").Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>What a crop's file name says the panel showed.</summary>
    private static Bbs757IlsReading Expected(string fileName)
    {
        string[] parts = Path.GetFileNameWithoutExtension(fileName).Split('_');   // "ocr", "110.50" or "PARK", "000"
        int course = int.Parse(parts[2], CultureInfo.InvariantCulture);
        return parts[1] == "PARK"
            ? new Bbs757IlsReading(true, null, course)
            : new Bbs757IlsReading(false, double.Parse(parts[1], CultureInfo.InvariantCulture), course);
    }

    /// <summary>The picture moved by (dx, dy) pixels, black where it uncovers.</summary>
    private static Bitmap Shift(Bitmap source, int dx, int dy)
    {
        var shifted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(shifted);
        g.Clear(Color.Black);
        g.DrawImageUnscaled(source, dx, dy);
        return shifted;
    }

    /// <summary>The picture resampled to <paramref name="scale"/> times its size, as a capture at another DPI would hold it.</summary>
    private static Bitmap Scale(Bitmap source, double scale)
    {
        var scaled = new Bitmap((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        return scaled;
    }

    /// <summary>The picture inside a black margin: a crop larger than the overlay's rectangle.</summary>
    private static Bitmap Pad(Bitmap source, int margin)
    {
        var padded = new Bitmap(source.Width + 2 * margin, source.Height + 2 * margin, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(padded);
        g.Clear(Color.Black);
        g.DrawImageUnscaled(source, margin, margin);
        return padded;
    }

    private static Bitmap Filled(Color color)
    {
        var bitmap = new Bitmap(Bbs757IlsOcr.Width, Bbs757IlsOcr.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
        return bitmap;
    }

    private static Bitmap Noise(Random random, int percentLit)
    {
        var bitmap = new Bitmap(Bbs757IlsOcr.Width, Bbs757IlsOcr.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                int v = random.Next(100) < percentLit ? 255 : random.Next(60);
                bitmap.SetPixel(x, y, Color.FromArgb(v, v, v));
            }
        return bitmap;
    }

    [Fact]
    public void Decodes_every_fixture()
    {
        var crops = Crops();
        Assert.Equal(10, crops.Count);
        foreach (string name in crops)
        {
            using var crop = Load(name);
            Assert.Equal((Bbs757IlsOcr.Width, Bbs757IlsOcr.Height), (crop.Width, crop.Height));
            Assert.True(Expected(name) == Bbs757IlsOcr.Decode(crop), $"{name} decoded as {Bbs757IlsOcr.Decode(crop)}");
        }

        // Every segment lit (round 5's calibration capture): the decoder looks where the segments are...
        using var all = Load("cal_all_segments.png");
        var cells = Bbs757IlsOcr.Analyze(all);
        Assert.Equal("88888888", cells.Characters);
        Assert.False(cells.DecimalPoint);   // the point has no ghost: it is drawn only with a frequency
        // ...and eights with no point are no ILS display.
        Assert.Null(Bbs757IlsOcr.Decode(all));
    }

    [Fact]
    public void Unfiltered_crop_decodes_or_is_null()
    {
        // Without the overlay's filter the lit segments are grey on navy and the unlit ones faintly drawn
        // (round 5: Otsu put the threshold at 45 and read it right). It still reads PARK and course 000:
        // the filter makes the read sure, it is not what makes it possible.
        using var raw = Load("raw_PARK_000_nofilter.png");

        Assert.Equal(new Bbs757IlsReading(true, null, 0), Bbs757IlsOcr.Decode(raw));
    }

    [Fact]
    public void Offset_crop_still_decodes()
    {
        // The overlay a few pixels from where the crop expects it (another UI scale rounding, say): the
        // decoder tries offsets of up to 4 px and keeps the one where every segment is clearly on or off.
        foreach (string name in Crops())
        {
            using var crop = Load(name);
            using var shifted = Shift(crop, dx: -3, dy: 2);
            Assert.True(Expected(name) == Bbs757IlsOcr.Decode(shifted), $"{name} shifted decoded as {Bbs757IlsOcr.Decode(shifted)}");
        }
    }

    [Fact]
    public void Invalid_scene_is_null()
    {
        using (var black = Filled(Color.Black)) Assert.Null(Bbs757IlsOcr.Decode(black));
        using (var white = Filled(Color.White)) Assert.Null(Bbs757IlsOcr.Decode(white));

        var random = new Random(757);
        foreach (int percentLit in new[] { 2, 10, 30, 50, 70 })
        {
            using var noise = Noise(random, percentLit);
            Assert.Null(Bbs757IlsOcr.Decode(noise));
        }

        // Never a guess: a crop that is not the overlay's rectangle reads right or not at all.
        foreach (string name in Crops())
        {
            foreach (int margin in new[] { 8, 12, 24 })
            {
                using var crop = Load(name);
                using var padded = Pad(crop, margin);
                var read = Bbs757IlsOcr.Decode(padded);
                Assert.True(read == null || read == Expected(name), $"{name} with a {margin} px margin decoded as {read}");
            }
        }
    }

    [Theory]
    [InlineData(1.5)]    // a 150 % display
    [InlineData(0.75)]
    public void Scaled_crop_decodes(double scale)
    {
        foreach (string name in Crops())
        {
            using var crop = Load(name);
            using var scaled = Scale(crop, scale);
            Assert.True(Expected(name) == Bbs757IlsOcr.Decode(scaled), $"{name} at x{scale} decoded as {Bbs757IlsOcr.Decode(scaled)}");
        }
    }

    [Fact]
    public void Crop_rectangle_from_page_size()
    {
        // Round 5's setup, 1920 x 1080 at 96 DPI: one CSS pixel is one pixel, the box at (16,16)-(683,108).
        Assert.Equal(new Rectangle(16, 16, 668, 93), Bbs757IlsOcr.OverlayCrop(new Size(1920, 1080), null, 1920, 1080));

        // A 2880 x 1620 capture of a page 1920 CSS pixels wide: 1.5 pixels to the CSS pixel.
        Assert.Equal(new Rectangle(24, 24, 1002, 140), Bbs757IlsOcr.OverlayCrop(new Size(2880, 1620), null, 1920, 1080));

        // A windowed simulator: the capture holds the window's frame, and the page starts at the client area.
        Assert.Equal(new Rectangle(24, 47, 668, 93),
            Bbs757IlsOcr.OverlayCrop(new Size(1936, 1119), new Rectangle(8, 31, 1920, 1080), 1920, 1080));

        // A page that does not fill the picture at one scale, or no page size: no crop, rather than a guess.
        Assert.Null(Bbs757IlsOcr.OverlayCrop(new Size(1936, 1119), null, 1920, 1080));
        Assert.Null(Bbs757IlsOcr.OverlayCrop(new Size(1920, 1080), null, 0, 0));
        Assert.Null(Bbs757IlsOcr.OverlayCrop(new Size(600, 80), null, 600, 80));   // the box would not fit
    }

    [Fact]
    public void The_gauge_is_looked_for_only_where_no_client_holds_the_view()
    {
        // One inspector socket per view ([DBG-9]): the tablet's and the EFB's pages are held for the 757's life.
        Assert.True(CoherentBbs757IlsDisplay.IsProbeable("VCockpit15"));
        Assert.True(CoherentBbs757IlsDisplay.IsProbeable("VCockpit30 - WasmInstrument - WasmInstrument"));
        Assert.False(CoherentBbs757IlsDisplay.IsProbeable("VCockpit28 - BBSTablet - WasmInstrument - WasmInstrument"));
        Assert.False(CoherentBbs757IlsDisplay.IsProbeable("VCockpit29 - BBSNavigraphMap"));
        Assert.False(CoherentBbs757IlsDisplay.IsProbeable("mainUi"));
        Assert.False(CoherentBbs757IlsDisplay.IsProbeable("Toolbar"));
    }

    [Fact]
    public void The_overlay_is_the_measured_box_and_filter()
    {
        // Round 5's overlay (measurements G1.1-G1.2): a fixed black box at (16,16) holding the live view at its
        // natural 668 x 93, on top of everything, never taking the mouse; white segments on black.
        string js = CoherentBbs757IlsDisplay.InjectScript("WasmGaugeLiveView_12");

        Assert.Contains("position:fixed;left:16px;top:16px;width:668px;height:93px;background:#000;z-index:2147483647;pointer-events:none", js);
        Assert.Contains("filter:grayscale(1) brightness(2.6) contrast(40)", js);
        Assert.Contains("\"WasmGaugeLiveView_12\"", js);
        Assert.Contains("innerWidth", js);
        Assert.Contains("devicePixelRatio", js);
        Assert.Contains(CoherentBbs757IlsDisplay.OverlayId, CoherentBbs757IlsDisplay.RemoveScript);
        Assert.Contains(CoherentBbs757IlsDisplay.OverlayId, js);   // a stale box of the same id is removed first
        Assert.Contains("wasm_gauge=ils_cont", CoherentBbs757IlsDisplay.FindGaugeScript);
    }

    [Fact]
    public async Task A_read_that_times_out_says_where_and_a_canceled_one_says_nothing()
    {
        // A Coherent debugger that takes the connection and never answers: a local listener, never the sim's port.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var held = new List<TcpClient>();
        var accepting = Task.Run(async () =>
        {
            try
            {
                while (true) held.Add(await listener.AcceptTcpClientAsync());
            }
            catch (Exception)
            {
                // the listener stopped
            }
        });
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var display = new CoherentBbs757IlsDisplay(() => Task.FromResult<byte[]?>(null), () => null,
            $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}", lines.Enqueue);
        try
        {
            // The page list never answers: the gauge search's own cap ends the read, and the log says where.
            Assert.Null(await display.ReadAsync(CancellationToken.None));
            Assert.Contains("ILS display: timed out (gauge search).", lines);

            // The caller's own cancellation (a flight load, a switch away): nothing in the log.
            lines.Clear();
            using var canceled = new CancellationTokenSource(200);
            Assert.Null(await display.ReadAsync(canceled.Token));
            Assert.Empty(lines);
        }
        finally
        {
            listener.Stop();
            await accepting;
            foreach (var client in held) client.Dispose();
        }
    }
}

/// <summary>
/// The ILS display as the definition's tests see it: what a read answers (null: the display could not be
/// read), every read counted and, when a log is given, written into it as "read display".
/// </summary>
internal sealed class FakeIlsDisplay : IBbs757IlsDisplay
{
    private readonly List<string>? _log;

    public FakeIlsDisplay(List<string>? log = null) => _log = log;

    /// <summary>What the next read answers; null: the display could not be read.</summary>
    public Func<Bbs757IlsReading?> Answer { get; set; } = () => null;

    public int Reads { get; private set; }

    public int Forgotten { get; private set; }

    public Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct)
    {
        Reads++;
        _log?.Add("read display");
        return Task.FromResult(ct.IsCancellationRequested ? null : Answer());
    }

    public void ForgetGauge() => Forgotten++;

    public void Dispose() { }
}
