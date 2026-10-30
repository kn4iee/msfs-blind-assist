using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Whether two captures of the 757 tablet show the same thing (<see cref="Bbs757EfbFrameCompare.Same"/>), which every tablet press
/// leans on before its tap (B757-18) and the bridge before it reads again: at most <see cref="Bbs757EfbFrameCompare.MaxChangedPixels"/>
/// pixels from 4 rows below the status bar down may differ by more than <see cref="Bbs757EfbFrameCompare.NoiseLevels"/> levels in a
/// channel. Measured 2026-10-27: two captures of an unchanged page differ in up to 73,000 pixels, each by at most 4 levels (the ND
/// pop-out's rendering noise), while a word, a tile changing color or a page sliding in moves pixels by tens to hundreds.
/// </summary>
public class Bbs757EfbFrameCompareTests
{
    private const int Width = 120, Height = 100;

    /// <summary>The first row the compare looks at: the status bar, with its clock, and the 4 rows under it are left out.</summary>
    private static readonly int FirstRow = Bbs757EfbScreen.StatusBottom + 4;

    private static readonly Color Page = Color.FromArgb(22, 29, 37);   // the page's slate at 100 % (measured)

    private static Bitmap Frame()
    {
        var bitmap = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Page);
        return bitmap;
    }

    /// <summary>The page with <paramref name="count"/> pixels from row <paramref name="row"/> on, left to right, each with its red
    /// raised by <paramref name="levels"/>.</summary>
    private static Bitmap Changed(int count, int levels, int row)
    {
        var bitmap = Frame();
        for (int i = 0; i < count; i++)
            bitmap.SetPixel(i % Width, row + i / Width, Color.FromArgb(Page.R + levels, Page.G, Page.B));
        return bitmap;
    }

    [Fact]
    public void The_thresholds_are_the_measured_ones()
    {
        Assert.Equal(12, Bbs757EfbFrameCompare.NoiseLevels);
        Assert.Equal(30, Bbs757EfbFrameCompare.MaxChangedPixels);
    }

    [Fact]
    public void Thirty_pixels_moved_by_13_levels_are_the_same_frame_and_31_are_not()
    {
        using var page = Frame();
        using var thirty = Changed(30, 13, FirstRow);
        using var thirtyOne = Changed(31, 13, FirstRow);

        Assert.True(Bbs757EfbFrameCompare.Same(page, thirty));
        Assert.False(Bbs757EfbFrameCompare.Same(page, thirtyOne));
        Assert.False(Bbs757EfbFrameCompare.Same(thirtyOne, page));
    }

    [Fact]
    public void Any_number_of_pixels_moved_by_12_levels_are_noise()
    {
        using var page = Frame();
        using var noisy = Changed(Width * (Height - FirstRow), 12, FirstRow);   // every pixel compared

        Assert.True(Bbs757EfbFrameCompare.Same(page, noisy));
    }

    [Fact]
    public void Differences_above_the_compared_rows_are_the_same_frame()
    {
        Assert.Equal(36, FirstRow);
        using var page = Frame();
        // The status bar and the 4 rows under it changed altogether (its clock, the departure countdown): the same frame.
        using var bar = Changed(Width * FirstRow, 200, 0);
        // 31 pixels on the last row left out are nothing; on the first compared row, a different frame.
        using var above = Changed(31, 200, FirstRow - 1);
        using var below = Changed(31, 200, FirstRow);

        Assert.True(Bbs757EfbFrameCompare.Same(page, bar));
        Assert.True(Bbs757EfbFrameCompare.Same(page, above));
        Assert.False(Bbs757EfbFrameCompare.Same(page, below));
    }

    [Fact]
    public void Captures_of_two_sizes_are_never_the_same()
    {
        using var page = Frame();
        using var other = new Bitmap(Width, Height + 1);

        Assert.False(Bbs757EfbFrameCompare.Same(page, other));
    }
}
