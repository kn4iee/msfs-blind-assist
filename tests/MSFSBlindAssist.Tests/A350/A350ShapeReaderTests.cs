using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The SD's graphics (valves, pumps, needles, door shapes) reach the sim as NanoVG's render calls:
// fsRenderFill(ctx, paint, op, scissor, fringe, bounds, paths, npaths), fsRenderStroke(ctx, paint, op,
// scissor, fringe, width, paths, npaths) and fsRenderTriangles(ctx, paint, op, scissor, verts, nverts).
// wasm32 layouts: NVGpaint 76 bytes, innerColor at +40, image at +72; NVGpath 40 bytes, stroke at +24,
// nstroke at +28; NVGvertex x, y, u, v.
public class A350ShapeReaderTests
{
    private static void F(byte[] m, int at, params float[] v)
    {
        for (int i = 0; i < v.Length; i++) BitConverter.GetBytes(v[i]).CopyTo(m, at + 4 * i);
    }

    private static void I(byte[] m, int at, params int[] v)
    {
        for (int i = 0; i < v.Length; i++) BitConverter.GetBytes(v[i]).CopyTo(m, at + 4 * i);
    }

    private static byte[] WithPaint(int paint, float r, float g, float b, int image = 0)
    {
        var m = new byte[4096];
        F(m, paint, 1, 0, 0, 1, 0, 0);   // identity transform
        F(m, paint + 40, r, g, b, 1);
        I(m, paint + 72, image);
        return m;
    }

    [Fact]
    public void A_fill_is_its_bounds_and_inner_colour()
    {
        var m = WithPaint(100, 0, 1, 0, image: 7);
        F(m, 300, 10, 20, 30, 45);
        var s = A350ShapeReader.Fill(m, 100, 300);
        Assert.NotNull(s);
        Assert.Equal(new DrawnShape("fill", 10, 20, 30, 45, 7), s with { Colour = null });
        Assert.Equal("green", A350Rgba.NameOf(s.Colour));
    }

    [Fact]
    public void A_stroke_is_the_box_around_its_paths_stroke_vertices()
    {
        var m = WithPaint(100, 1, 0.55f, 0);
        I(m, 400, 0, 0, 0, 0, 0, 0, 1000, 2);    // path 1: 2 stroke vertices at 1000
        I(m, 440, 0, 0, 0, 0, 0, 0, 1100, 1);    // path 2: 1 at 1100
        F(m, 1000, 50, 60, 0, 0, 70, 65, 0, 0);
        F(m, 1100, 40, 80, 0, 0);
        var s = A350ShapeReader.Stroke(m, 100, 400, 2);
        Assert.NotNull(s);
        Assert.Equal(("stroke", 40f, 60f, 70f, 80f), (s.Kind, s.X0, s.Y0, s.X1, s.Y1));
        Assert.Equal("amber", A350Rgba.NameOf(s.Colour));
    }

    [Fact]
    public void Triangles_are_the_box_around_their_vertices()
    {
        var m = WithPaint(100, 1, 1, 1, image: 3);
        F(m, 2000, 5, 6, 0, 0, 9, 2, 0, 0, 7, 8, 0, 0);
        var s = A350ShapeReader.Triangles(m, 100, 2000, 3);
        Assert.NotNull(s);
        Assert.Equal(("triangles", 5f, 2f, 9f, 8f, 3), (s.Kind, s.X0, s.Y0, s.X1, s.Y1, s.Image));
    }

    // An image paint is a pattern: xform translation (+16, +20) is where the image's origin lands and extent
    // (+24, +28) its drawn size, which together say which sprite of an atlas the shape shows.
    [Fact]
    public void An_image_fill_keeps_its_pattern_origin_and_size()
    {
        var m = WithPaint(100, 1, 1, 1, image: 22);
        F(m, 100, 1, 0, 0, 1, -300, 120, 1024, 512);
        F(m, 300, 84, 594, 126, 636);
        var s = A350ShapeReader.Fill(m, 100, 300)!;
        Assert.Equal((-300f, 120f, 1024f, 512f), (s.PatternX, s.PatternY, s.PatternW, s.PatternH));
    }

    // A needle or a turned valve is a rotated pattern: the sprite is found by mapping the shape's centre back
    // into the image, and its turn is the pattern's angle.
    [Fact]
    public void A_rotated_sprite_maps_back_to_its_place_in_the_atlas_with_its_angle()
    {
        var m = WithPaint(100, 1, 1, 1, image: 22);
        // 90 degrees: image x runs down the screen, image y runs left. Origin lands at (500, 100).
        F(m, 100, 0, 1, -1, 0, 500, 100, 2048, 2048);
        // The sprite centred at atlas (30, 40) lands at screen (500 - 40, 100 + 30) = (460, 130).
        F(m, 300, 450, 120, 470, 140);
        var s = A350ShapeReader.Fill(m, 100, 300)!;
        var (sx, sy) = A350ShapeReader.SpriteCentre(s);
        Assert.Equal(30, sx, 3);
        Assert.Equal(40, sy, 3);
        Assert.Equal(90, A350ShapeReader.AngleDegrees(s), 3);
    }

    [Fact]
    public void Pointers_outside_memory_give_no_shape()
    {
        var m = new byte[256];
        Assert.Null(A350ShapeReader.Fill(m, 100, 5000));
        Assert.Null(A350ShapeReader.Stroke(m, 100, 5000, 3));
        Assert.Null(A350ShapeReader.Triangles(m, 9000, 0, 3));
    }
}
