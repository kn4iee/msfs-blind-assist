namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A NanoVG fill colour, four floats from 0 to 1.</summary>
public readonly record struct A350Rgba(float R, float G, float B, float A)
{
    private static readonly (string Name, float R, float G, float B)[] Named =
    {
        ("green", 0, 1, 0), ("amber", 1, 0.6f, 0), ("cyan", 0, 1, 1), ("magenta", 1, 0, 1),
        ("white", 1, 1, 1), ("red", 1, 0, 0), ("yellow", 1, 1, 0), ("grey", 0.5f, 0.5f, 0.5f), ("black", 0, 0, 0),
        // The A350's soft magenta (the LS block's; the vertical display's MSA and MORA, 0.90 0.53 0.90, nearer white
        // than pure magenta): a sighted pilot's pink-magenta. After the pure one, so a name still maps back to it.
        ("magenta", 1, 0.47f, 0.91f),
    };

    /// <summary>A named colour back as a colour (white for an unknown name), so a text re-built from a cell keeps
    /// the colour it was read with: every name <see cref="NameOf"/> gives maps back to itself.</summary>
    public static A350Rgba Of(string name)
    {
        foreach (var (n, r, g, b) in Named)
            if (n == name) return new A350Rgba(r, g, b, 1);
        return new A350Rgba(1, 1, 1, 1);
    }

    /// <summary>The nearest ECAM colour by RGB distance ("?" for no colour), as the prototype named them.</summary>
    public static string NameOf(A350Rgba? c)
    {
        if (c is not A350Rgba v) return "?";
        string best = "?"; float bd = float.MaxValue;
        foreach (var (n, r, g, b) in Named)
        {
            float d = (v.R - r) * (v.R - r) + (v.G - g) * (v.G - g) + (v.B - b) * (v.B - b);
            if (d < bd) { bd = d; best = n; }
        }
        return best;
    }
}

/// <summary>A text the draw code drew: its screen position (through the transform), its local position,
/// its NanoVG context and its fill colour.</summary>
public sealed record DrawnText(string Text, float X, float Y, float LocalX, float LocalY, int Ctx, A350Rgba? Colour)
{
    public string ColourName => A350Rgba.NameOf(Colour);
}

/// <summary>A frame (DrawFrame / DrawWideFrame): the ECAM selection box is a cyan wide frame.</summary>
public sealed record DrawnFrame(float X, float Y, float W, float H, bool Wide, A350Rgba? Colour);

/// <summary>A UI element the draw code drew (an MFD field, button or dropdown): its class, its pointer in
/// the copy, its rectangle (element +4..+16) and where it lands; <see cref="TextStart"/> is the index of
/// the first text drawn after it.</summary>
public sealed record DrawnElement(int Ptr, string Class, float RectX, float RectY, float RectW, float RectH, float X, float Y, int TextStart);

/// <summary>Everything one gauge's Draw produced, or the error that stopped it.</summary>
public sealed record A350DrawCapture(
    string Gauge, IReadOnlyList<DrawnText> Texts, IReadOnlyList<DrawnFrame> Frames,
    IReadOnlyList<DrawnElement> Elements, string? Error, double Milliseconds)
{
    public bool Ok => Error == null;

    /// <summary>What NanoVG rendered, when the engine records shapes (<see cref="A350DrawEngine.RecordShapes"/>).</summary>
    public IReadOnlyList<DrawnShape>? Shapes { get; init; }
}
