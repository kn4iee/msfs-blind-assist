using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Whether two captures of the tablet show the same thing.
///
/// Never pixel-identical: measured live 2026-10-27, two captures 250 ms apart of an unchanged page differ
/// in 15 to 73,000 pixels, every one by at most 4 levels (the ND pop-out's rendering noise). Text, a tile
/// changing colour or a page sliding in moves pixels by tens to hundreds of levels. So "the same" is: at
/// most <see cref="MaxChangedPixels"/> pixels below the status bar (its clock) differ by more than
/// <see cref="NoiseLevels"/> in any channel.
/// </summary>
public static class Bbs757EfbFrameCompare
{
    public const int NoiseLevels = 12;
    public const int MaxChangedPixels = 30;

    public static bool Same(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        int top = Math.Min(a.Height, Bbs757EfbScreen.StatusBottom + 4);
        var rect = new Rectangle(0, top, a.Width, a.Height - top);
        if (rect.Height <= 0) return true;
        var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = rect.Width * 4;
            var ra = new byte[rowBytes];
            var rb = new byte[rowBytes];
            int changed = 0;
            for (int y = 0; y < rect.Height; y++)
            {
                Marshal.Copy(da.Scan0 + y * da.Stride, ra, 0, rowBytes);
                Marshal.Copy(db.Scan0 + y * db.Stride, rb, 0, rowBytes);
                for (int x = 0; x < rowBytes; x += 4)
                {
                    if (Math.Abs(ra[x] - rb[x]) > NoiseLevels || Math.Abs(ra[x + 1] - rb[x + 1]) > NoiseLevels
                        || Math.Abs(ra[x + 2] - rb[x + 2]) > NoiseLevels)
                    {
                        if (++changed > MaxChangedPixels) return false;
                    }
                }
            }
            return true;
        }
        finally
        {
            a.UnlockBits(da);
            b.UnlockBits(db);
        }
    }
}
