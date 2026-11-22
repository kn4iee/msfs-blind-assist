namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

internal static class A330McduGeometry
{
    public const int Rows = 14, Columns = 24;

    // Measured on the 450 x 400 display: a text's first character sits at Left + column * ColumnPitch, its baseline at Top + row * RowPitch.
    public const float Left = 22f, ColumnPitch = 16.87f, Top = 43f, RowPitch = 25.85f;

    public const string LargeFont = "FMGSfont_A", SmallFont = "FMGSfont_B";

    /// The nearest whole cell to a fractional position; half-way rounds to the lower one.
    public static int Nearest(double v)
    {
        double floor = Math.Floor(v);
        return (int)(v - floor <= 0.5 ? floor : floor + 1);
    }

    public static int ColumnOf(float x) => Nearest((x - Left) / (double)ColumnPitch);

    public static int RowOf(float baseline) => Nearest(RowPosition(baseline));

    /// How many rows down a baseline sits, as a fraction (0 is the title row's baseline).
    public static double RowPosition(float baseline) => (baseline - Top) / (double)RowPitch;
}
