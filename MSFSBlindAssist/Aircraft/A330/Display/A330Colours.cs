namespace MSFSBlindAssist.Aircraft.A330.Display;

/// A drawn colour's word, by hue: "white", "grey", "red", "amber", "yellow", "green", "cyan", "blue" or "magenta".
/// Seen on the aircraft: MCDU amber (0.98, 0.76, 0.29), cyan (0.31, 0.82, 1.00), greyed prompt (0.30, 0.30, 0.30); ECAM amber (1.00, 0.55, 0.00),
/// green (0.38, 0.80, 0.39), cyan (0.40, 0.86, 1.00); ISIS green (0.46, 1.00, 0.46); ND flag red (1.00, 0.00, 0.00).
public static class A330Colours
{
    // Components this close together are a colourless colour: white above half brightness, grey below.
    private const float ColourlessSpread = 0.12f;

    public static string Name(A330Rgb colour)
    {
        float max = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        float min = Math.Min(colour.R, Math.Min(colour.G, colour.B));
        float spread = max - min;
        if (spread <= ColourlessSpread)
            return (colour.R + colour.G + colour.B) / 3f > 0.5f ? "white" : "grey";

        double hue;
        if (max == colour.R) hue = 60.0 * ((colour.G - colour.B) / spread);
        else if (max == colour.G) hue = 60.0 * (2.0 + (colour.B - colour.R) / spread);
        else hue = 60.0 * (4.0 + (colour.R - colour.G) / spread);
        if (hue < 0) hue += 360.0;

        if (hue < 15 || hue >= 340) return "red";
        if (hue < 50) return "amber";
        if (hue < 75) return "yellow";
        if (hue < 165) return "green";
        if (hue < 210) return "cyan";
        if (hue < 270) return "blue";
        return "magenta";
    }

    /// The colour word of a text, or null when the reader gave it no colour.
    public static string? Word(A330Text text) => text.Colour is { } c ? Name(c) : null;
}
