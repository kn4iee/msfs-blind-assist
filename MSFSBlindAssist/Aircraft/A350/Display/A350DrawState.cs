namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// NanoVG's per-context drawing state as the draw hooks report it: the fill colour and the 2-D affine
/// transform [a b c d e f] (x' = a·x + c·y + e, y' = b·x + d·y + f), saved and restored as a stack.
/// The math is the prototype's (emu2.mjs): a new matrix multiplies onto the current one.
/// </summary>
public sealed class A350DrawState
{
    private static readonly float[] Identity = { 1, 0, 0, 1, 0, 0 };

    private readonly Dictionary<int, A350Rgba> _fill = new();
    private readonly Dictionary<int, float[]> _xf = new();
    private readonly Dictionary<int, Stack<(A350Rgba? Fill, float[] Xf)>> _stack = new();

    public void Clear() { _fill.Clear(); _xf.Clear(); _stack.Clear(); }

    public A350Rgba? FillOf(int ctx) => _fill.TryGetValue(ctx, out var c) ? c : null;

    public void Fill(int ctx, A350Rgba colour) => _fill[ctx] = colour;

    private float[] X(int ctx) => _xf.TryGetValue(ctx, out var t) ? t : Identity;

    private void Mul(int ctx, float m0, float m1, float m2, float m3, float m4, float m5)
    {
        var t = X(ctx);
        _xf[ctx] = new[]
        {
            m0 * t[0] + m1 * t[2], m0 * t[1] + m1 * t[3],
            m2 * t[0] + m3 * t[2], m2 * t[1] + m3 * t[3],
            m4 * t[0] + m5 * t[2] + t[4], m4 * t[1] + m5 * t[3] + t[5],
        };
    }

    public void Translate(int ctx, float x, float y) => Mul(ctx, 1, 0, 0, 1, x, y);
    public void Scale(int ctx, float x, float y) => Mul(ctx, x, 0, 0, y, 0, 0);

    public void Rotate(int ctx, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        Mul(ctx, c, s, -s, c, 0, 0);
    }

    public void ResetTransform(int ctx) => _xf[ctx] = Identity;

    /// <summary>nvgReset: the context's whole state back to NanoVG's start, the fill white as well as the transform
    /// (review 2026-10-29: only the transform was, so a text after a reset kept the colour before it).</summary>
    public void Reset(int ctx)
    {
        _xf[ctx] = Identity;
        _fill[ctx] = new A350Rgba(1, 1, 1, 1);
    }

    public void Save(int ctx)
    {
        if (!_stack.TryGetValue(ctx, out var st)) _stack[ctx] = st = new();
        st.Push((FillOf(ctx), X(ctx)));
    }

    public void Restore(int ctx)
    {
        if (!_stack.TryGetValue(ctx, out var st) || st.Count == 0) return;
        var (fill, xf) = st.Pop();
        if (fill is A350Rgba f) _fill[ctx] = f; else _fill.Remove(ctx);
        _xf[ctx] = xf;
    }

    public (float X, float Y) Apply(int ctx, float x, float y)
    {
        var t = X(ctx);
        return (x * t[0] + y * t[2] + t[4], x * t[1] + y * t[3] + t[5]);
    }
}
