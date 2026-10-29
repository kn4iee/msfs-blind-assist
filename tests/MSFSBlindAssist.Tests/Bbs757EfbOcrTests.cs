using System.Drawing;
using System.Drawing.Imaging;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.SimConnect.BBS757;
using RapidOcrNet;
using SkiaSharp;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The picture the tablet reader's OCR is given (plan 18): the tablet scaled so its brightest luminance below the
/// top bar is 255. The small model misses faint text on a tablet at 0 percent brightness (the Maintenance page's
/// pink gear-door OPEN buttons, measured), and Bbs757EfbPixels already scales the parser's pixels the same way.
/// Pure pixel work: no model runs here (CI has none), the engine is a fake behind PaddleOcrEngine's test seam.
/// </summary>
public class Bbs757EfbOcrTests
{
    [Fact]
    public void Brighten_scales_the_frame_white_to_255()
    {
        using var tablet = Frame(3, 40, PixelFormat.Format24bppRgb, (0, 37, Color.FromArgb(80, 80, 80)), (1, 38, Color.FromArgb(40, 0, 0)));

        using var bright = Bbs757EfbOcr.Brighten(tablet);

        Assert.Equal((255, 255, 255), Rgb(bright.GetPixel(0, 37)));
        Assert.Equal((128, 0, 0), Rgb(bright.GetPixel(1, 38)));   // 40 x 255 / 80 = 127.5, up
        Assert.Equal((0, 0, 0), Rgb(bright.GetPixel(2, 39)));     // black stays black
    }

    [Fact]
    public void Brighten_ignores_the_status_bar()
    {
        // The clock in the status bar is far brighter than the page's own white, and the page is dim.
        using var tablet = Frame(3, 40, PixelFormat.Format24bppRgb,
            (0, 5, Color.FromArgb(200, 200, 200)), (1, 5, Color.FromArgb(40, 0, 0)), (2, 37, Color.FromArgb(80, 80, 80)));

        using var bright = Bbs757EfbOcr.Brighten(tablet);

        Assert.Equal((255, 255, 255), Rgb(bright.GetPixel(2, 37)));   // the page's white sets the scale, not the clock's
        Assert.Equal((255, 255, 255), Rgb(bright.GetPixel(0, 5)));    // the status bar is scaled like the rest (it saturates)
        Assert.Equal((128, 0, 0), Rgb(bright.GetPixel(1, 5)));
    }

    [Theory]
    [InlineData(35, 255)]   // the last row of the top bar's margin: its 100 does not set the scale, the page's 80 does
    [InlineData(36, 204)]   // the first row that does: 100 is the white, the page's 80 becomes 80 x 255 / 100
    public void Brighten_reads_its_white_from_y_36_down(int row, int expectedPage)
    {
        using var tablet = Frame(3, 40, PixelFormat.Format24bppRgb, (0, row, Color.FromArgb(100, 100, 100)), (2, 37, Color.FromArgb(80, 80, 80)));

        using var bright = Bbs757EfbOcr.Brighten(tablet);

        Assert.Equal((expectedPage, expectedPage, expectedPage), Rgb(bright.GetPixel(2, 37)));
    }

    [Fact]
    public void Brighten_takes_the_white_from_luminance_not_from_the_strongest_channel()
    {
        // Green (0, 100, 0) has the luminance 58.7: the scale is 255 / 58.7, so a (30, 0, 0) is 130.3 and not the
        // 76.5 that the strongest channel (100) would give.
        using var tablet = Frame(3, 40, PixelFormat.Format24bppRgb, (0, 37, Color.FromArgb(0, 100, 0)), (1, 38, Color.FromArgb(30, 0, 0)));

        using var bright = Bbs757EfbOcr.Brighten(tablet);

        Assert.Equal((0, 255, 0), Rgb(bright.GetPixel(0, 37)));
        Assert.Equal((130, 0, 0), Rgb(bright.GetPixel(1, 38)));
    }

    [Fact]
    public void Brighten_does_not_stretch_a_frame_darker_than_30()
    {
        // A tablet that shows next to nothing (switched off, a blank page) is raised at most to 255 / 30, never
        // blown up into noise: 10 becomes 85, not 255.
        using var tablet = Frame(3, 40, PixelFormat.Format24bppRgb, (0, 37, Color.FromArgb(10, 10, 10)));

        using var bright = Bbs757EfbOcr.Brighten(tablet);

        Assert.Equal((85, 85, 85), Rgb(bright.GetPixel(0, 37)));
        Assert.Equal((0, 0, 0), Rgb(bright.GetPixel(1, 37)));
    }

    [Theory]
    [InlineData(PixelFormat.Format24bppRgb)]
    [InlineData(PixelFormat.Format32bppArgb)]
    public void Brighten_gives_a_new_24_bit_copy_of_the_same_size_and_leaves_the_tablet_as_it_was(PixelFormat format)
    {
        using var tablet = Frame(7, 41, format, (3, 40, Color.FromArgb(80, 80, 80)));

        using var bright = Bbs757EfbOcr.Brighten(tablet);

        Assert.NotSame(tablet, bright);
        Assert.Equal(PixelFormat.Format24bppRgb, bright.PixelFormat);
        Assert.Equal(new Size(7, 41), bright.Size);
        Assert.Equal((255, 255, 255), Rgb(bright.GetPixel(3, 40)));
        Assert.Equal((80, 80, 80), Rgb(tablet.GetPixel(3, 40)));   // the caller's picture is not the one scaled
    }

    [Fact]
    public async Task ReadAsync_reads_the_brightened_copy_and_gives_the_boxes_in_the_tablets_pixels()
    {
        SKColor seen = default;
        using var engine = new PaddleOcrEngine((bitmap, _) =>
        {
            seen = bitmap.GetPixel(1, 38);
            return new[] { Block("OPEN", 1, 37, 2, 39) };
        });
        using var tablet = Frame(3, 40, PixelFormat.Format24bppRgb, (0, 37, Color.FromArgb(80, 80, 80)), (1, 38, Color.FromArgb(40, 0, 0)));

        var lines = await Bbs757EfbOcr.ReadAsync(engine, tablet);

        Assert.Equal(new SKColor(128, 0, 0), seen);                      // the engine was given the brightened picture
        Assert.Equal((40, 0, 0), Rgb(tablet.GetPixel(1, 38)));           // and the caller's tablet is untouched
        var line = Assert.Single(lines);
        Assert.Equal("OPEN", line.Text);
        Assert.Equal(new Rectangle(1, 37, 1, 2), line.Box);              // the same size, so the same pixels
        Assert.Equal('P', line.Pass);
    }

    // ---------------------------------------------------------------- builders

    /// <summary>A black picture of <paramref name="format"/> with the given pixels set.</summary>
    private static Bitmap Frame(int width, int height, PixelFormat format, params (int X, int Y, Color Color)[] pixels)
    {
        var frame = new Bitmap(width, height, format);
        using (var g = Graphics.FromImage(frame)) g.Clear(Color.Black);
        foreach (var (x, y, color) in pixels) frame.SetPixel(x, y, color);
        return frame;
    }

    private static (int R, int G, int B) Rgb(Color c) => (c.R, c.G, c.B);

    private static TextBlock Block(string text, int left, int top, int right, int bottom) => new()
    {
        Text = text,
        BoxPoints = new[] { new SKPointI(left, top), new SKPointI(right, top), new SKPointI(right, bottom), new SKPointI(left, bottom) },
        Chars = null,
        CharScores = null,
        WordResults = null,
    };
}
