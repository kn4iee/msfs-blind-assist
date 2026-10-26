using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// What the ILS control panel's display shows: PARK (<paramref name="Parked"/>, no frequency: the panel hides
/// the fraction it remembers), or a frequency on an ILS channel (<paramref name="Mhz"/>, 108.10 to 111.95), and
/// the course window in whole degrees (<paramref name="Course"/>; null when it shows dashes).
/// </summary>
public sealed record Bbs757IlsReading(bool Parked, double? Mhz, int? Course);

/// <summary>
/// Reads the 757's ILS control panel display from a picture. The panel is a WASM gauge (<c>wasm_gauge=ils_control_lcd</c>,
/// 668 × 93) no variable mirrors; the app lays its live view over the simulator's 2D UI page with a filter that
/// leaves white segments on black (<c>SimConnect/BBS757/CoherentBbs757IlsDisplay.cs</c>), captures the window, crops the
/// box (<see cref="OverlayCrop"/>) and decodes it here as a seven-segment display. Pure.
///
/// Measured (docs/b757/evidence/flight-04.md, "Round 5: reading the ILS display"; round 5's decoder read all
/// 19 captures right, every segment fully on or off):
///   • Five frequency cells at x 11, 73, 134, 195, 256 (three digits, a fixed decimal point, two digits) and
///     three course cells at x 420, 482, 543, each 48 px wide. PARK shows <c>-----</c> (only g lit) and no point.
///   • Each segment is sampled in its core, relative to its cell's left edge, and is on at 50 % or more.
///   • The point (x 184-192, y 79-88) is drawn only with a frequency: it has no unlit ghost.
///   • In this font 6 is acdefg, 7 abc and 9 abcfg (no bottom); a dash is g. Only these patterns are digits.
/// The method: resample to 668 × 93, threshold by Otsu (inverting a picture that is mostly lit), try offsets of
/// up to ±4 px and keep the one where every segment is most clearly on or off, then read the cells. Anything
/// that is not a valid ILS display (a cell that is not a digit or a dash, a frequency off the ILS channels, a
/// course over 359, a segment neither clearly on nor clearly off) is null: never a guess.
/// </summary>
public static class Bbs757IlsOcr
{
    /// <summary>The gauge's natural size: the overlay's box and the picture the decoder reads.</summary>
    public const int Width = 668, Height = 93;

    /// <summary>Where the overlay puts the box on the UI page, in CSS pixels (round 5: client pixels (16,16)-(683,108)).</summary>
    public const int OverlayLeft = 16, OverlayTop = 16;

    /// <summary>
    /// The lowest clarity a read may have: every sampled segment's (and the point's) distance from 50 % lit,
    /// doubled, the worst of them (1 is every one fully on or off). Round 5's filtered reads were all 1.00 over
    /// the segments and the bare scene under the box 0.21-0.33. Measured here: the fixtures 0.96 plain (the
    /// point's core is a pixel short of fully lit), 0.80 to 1.00 shifted, rescaled or unfiltered; crops with a
    /// margin round the box (not the overlay's rectangle) 0.30 at most.
    /// </summary>
    internal const double MinClarity = 0.6;

    private const int Search = 4;

    private static readonly int[] CellLeft = { 11, 73, 134, 195, 256, 420, 482, 543 };
    private const int FrequencyCells = 5;

    // Segment cores relative to a cell's left edge, inclusive: x0, y0, x1, y1 (measurements section G1.4).
    private static readonly (char Name, int X0, int Y0, int X1, int Y1)[] Segments =
    {
        ('a', 14, 15, 33, 20),
        ('b', 38, 22, 44, 41),
        ('c', 38, 58, 44, 78),
        ('d', 14, 81, 33, 86),
        ('e', 3, 58, 9, 78),
        ('f', 3, 22, 9, 41),
        ('g', 14, 48, 33, 53),
    };

    // The decimal point's core, after the third frequency cell (absolute, inclusive).
    private static readonly (int X0, int Y0, int X1, int Y1) PointCore = (185, 81, 191, 87);

    private static readonly Dictionary<string, char> Patterns = new(StringComparer.Ordinal)
    {
        ["abcdef"] = '0', ["bc"] = '1', ["abdeg"] = '2', ["abcdg"] = '3', ["bcfg"] = '4',
        ["acdfg"] = '5', ["acdefg"] = '6', ["abc"] = '7', ["abcdefg"] = '8', ["abcfg"] = '9',
        ["g"] = '-', [""] = ' ',
    };

    /// <summary>
    /// What the cells of a picture show: one character per cell (five frequency cells, then three course
    /// cells; '?' for a pattern that is no digit, a dash or blank), whether the point is lit, the clarity
    /// (see <see cref="MinClarity"/>) and the offset the cells were found at.
    /// </summary>
    internal sealed record Cells(string Characters, bool DecimalPoint, double Clarity, Point Offset);

    /// <summary>
    /// The ILS display a crop of the overlay shows, or null when it is not a valid one (see the class). The crop
    /// may have any size: it is resampled to <see cref="Width"/> × <see cref="Height"/> first.
    /// </summary>
    public static Bbs757IlsReading? Decode(Bitmap crop)
    {
        var cells = Analyze(crop);
        return cells.Clarity < MinClarity ? null : Interpret(cells.Characters, cells.DecimalPoint);
    }

    /// <summary>The cells of a crop, valid display or not (for the decode and its tests).</summary>
    internal static Cells Analyze(Bitmap crop)
    {
        byte[] grey = Grey(crop);
        bool[] lit = Threshold(grey);

        var best = (Score: double.NegativeInfinity, Dx: 0, Dy: 0);
        for (int dy = -Search; dy <= Search; dy++)
        {
            for (int dx = -Search; dx <= Search; dx++)
            {
                double score = 0;
                foreach (int x0 in CellLeft)
                    foreach (var s in Segments)
                        score += Math.Abs(Fraction(lit, x0 + s.X0 + dx, s.Y0 + dy, x0 + s.X1 + dx, s.Y1 + dy) - 0.5);
                // The clearest offset; of equally clear ones, the nearest to where the box should be.
                bool clearer = score > best.Score + 1e-9;
                bool asClearAndNearer = Math.Abs(score - best.Score) <= 1e-9
                    && Math.Abs(dx) + Math.Abs(dy) < Math.Abs(best.Dx) + Math.Abs(best.Dy);
                if (clearer || asClearAndNearer) best = (score, dx, dy);
            }
        }

        double clarity = 1;
        var chars = new char[CellLeft.Length];
        for (int i = 0; i < CellLeft.Length; i++)
        {
            int x0 = CellLeft[i] + best.Dx;
            string pattern = "";
            foreach (var s in Segments)
            {
                double f = Fraction(lit, x0 + s.X0, s.Y0 + best.Dy, x0 + s.X1, s.Y1 + best.Dy);
                clarity = Math.Min(clarity, Math.Abs(f - 0.5) * 2);
                if (f >= 0.5) pattern += s.Name;
            }
            chars[i] = Patterns.TryGetValue(pattern, out char c) ? c : '?';
        }

        double point = Fraction(lit, PointCore.X0 + best.Dx, PointCore.Y0 + best.Dy, PointCore.X1 + best.Dx, PointCore.Y1 + best.Dy);
        clarity = Math.Min(clarity, Math.Abs(point - 0.5) * 2);
        return new Cells(new string(chars), point >= 0.5, clarity, new Point(best.Dx, best.Dy));
    }

    /// <summary>
    /// The reading the characters stand for, or null when they are no valid ILS display: <c>-----</c> with no
    /// point is PARK; five digits with the point a frequency on an ILS channel (<see cref="Bbs757IlsPanel.TryDialFor"/>);
    /// the course three digits up to 359, or three dashes (no course).
    /// </summary>
    private static Bbs757IlsReading? Interpret(string chars, bool point)
    {
        string frequency = chars[..FrequencyCells], course = chars[FrequencyCells..];

        int? degrees;
        if (course == "---") degrees = null;
        else if (course.All(char.IsAsciiDigit) && int.Parse(course, System.Globalization.CultureInfo.InvariantCulture) is int d && d <= 359) degrees = d;
        else return null;

        if (frequency == "-----") return point ? null : new Bbs757IlsReading(true, null, degrees);
        if (!point || !frequency.All(char.IsAsciiDigit)) return null;

        int hundredths = int.Parse(frequency, System.Globalization.CultureInfo.InvariantCulture);
        return Bbs757IlsPanel.TryDialFor(hundredths / 100.0, out var dial) ? new Bbs757IlsReading(false, dial.Mhz, degrees) : null;
    }

    /// <summary>
    /// The overlay's box in a capture, in the capture's pixels. <paramref name="client"/> is where the
    /// simulator's client area lies in the capture (null: the whole capture, a borderless window); the UI page
    /// covers it, <paramref name="innerWidth"/> × <paramref name="innerHeight"/> CSS pixels, so one CSS pixel is
    /// the client's width over <paramref name="innerWidth"/> pixels (never assumed 1:1: round 5 measured only
    /// 1920 × 1080 at 96 DPI and UI scale 1). Null when there is no page size, when the page does not fill the
    /// client at one scale (within 1 %), or when the box would not fit in the capture.
    /// </summary>
    public static Rectangle? OverlayCrop(Size capture, Rectangle? client, double innerWidth, double innerHeight)
    {
        var area = client ?? new Rectangle(Point.Empty, capture);
        if (!(innerWidth > 0) || !(innerHeight > 0) || area.Width <= 0 || area.Height <= 0) return null;

        double scale = area.Width / innerWidth;
        if (Math.Abs(area.Height / innerHeight - scale) > 0.01 * scale) return null;

        int Px(double css) => (int)Math.Round(css * scale, MidpointRounding.AwayFromZero);
        var box = Rectangle.FromLTRB(area.X + Px(OverlayLeft), area.Y + Px(OverlayTop),
            area.X + Px(OverlayLeft + Width), area.Y + Px(OverlayTop + Height));
        return new Rectangle(Point.Empty, capture).Contains(box) ? box : null;
    }

    // ---- Pixels -------------------------------------------------------------------------------------------

    /// <summary>The crop as grey levels, (R+G+B)/3, resampled (nearest pixel) to <see cref="Width"/> × <see cref="Height"/>.</summary>
    private static byte[] Grey(Bitmap crop)
    {
        int w = crop.Width, h = crop.Height;
        var data = crop.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bgra;
        int stride;
        try
        {
            stride = Math.Abs(data.Stride);
            bgra = new byte[stride * h];
            Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
        }
        finally
        {
            crop.UnlockBits(data);
        }

        var grey = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            int sy = Math.Min(h - 1, (int)((y + 0.5) * h / Height));
            for (int x = 0; x < Width; x++)
            {
                int sx = Math.Min(w - 1, (int)((x + 0.5) * w / Width));
                int p = sy * stride + sx * 4;
                grey[y * Width + x] = (byte)((bgra[p] + bgra[p + 1] + bgra[p + 2]) / 3);
            }
        }
        return grey;
    }

    /// <summary>Lit pixels by Otsu's threshold; a picture mostly lit (dark digits on a light ground) is inverted.</summary>
    private static bool[] Threshold(byte[] grey)
    {
        var histogram = new int[256];
        foreach (byte v in grey) histogram[v]++;

        long total = grey.Length, sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += (long)i * histogram[i];

        long weightBack = 0, sumBack = 0;
        double bestBetween = -1;
        int threshold = 128;
        for (int t = 0; t < 256; t++)
        {
            weightBack += histogram[t];
            if (weightBack == 0) continue;
            long weightFore = total - weightBack;
            if (weightFore == 0) break;
            sumBack += (long)t * histogram[t];
            double meanBack = (double)sumBack / weightBack, meanFore = (double)(sumAll - sumBack) / weightFore;
            double between = (double)weightBack * weightFore * (meanBack - meanFore) * (meanBack - meanFore);
            if (between > bestBetween)
            {
                bestBetween = between;
                threshold = t;
            }
        }

        var lit = new bool[grey.Length];
        int on = 0;
        for (int i = 0; i < grey.Length; i++)
            if (lit[i] = grey[i] > threshold) on++;
        if (on > grey.Length / 2)
            for (int i = 0; i < lit.Length; i++) lit[i] = !lit[i];
        return lit;
    }

    /// <summary>The share of lit pixels in a box (inclusive), clipped to the picture; 0 for a box wholly outside it.</summary>
    private static double Fraction(bool[] lit, int x0, int y0, int x1, int y1)
    {
        int on = 0, n = 0;
        for (int y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
        {
            int row = y * Width;
            for (int x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++)
            {
                n++;
                if (lit[row + x]) on++;
            }
        }
        return n == 0 ? 0 : (double)on / n;
    }
}
