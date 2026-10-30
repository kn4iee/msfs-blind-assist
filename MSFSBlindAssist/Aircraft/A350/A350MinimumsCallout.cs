using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The approach minimums call-out (<see cref="Display.A350Minimums"/>), in the A380's words (<see cref="A380Minimums"/>,
/// <see cref="ApproachMinimums"/>) on the A350's own values: BARO 0 is not set; RADIO 0 is not set and -1 NO DH. Each
/// minimum has its own Ctrl+M row. Pure.
/// </summary>
public static class A350MinimumsCallout
{
    public const string BaroKey = "A350_MINIMUM_BARO", RadioKey = "A350_MINIMUM_RADIO";
    public const string BaroLabel = "Baro minimum", RadioLabel = "Decision height";

    /// <summary>The rows, for Ctrl+M.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Rows = new[] { (BaroKey, BaroLabel), (RadioKey, RadioLabel) };

    /// <summary>What changed from <paramref name="was"/> to <paramref name="now"/>, BARO then RADIO, each with its row; a clear
    /// only when <paramref name="sayClears"/>.</summary>
    public static IEnumerable<(string Key, string Text)> Changes(Display.A350Minimums was, Display.A350Minimums now, bool sayClears)
    {
        if (now.Baro != was.Baro && (now.Baro > 0 || sayClears))
            yield return (BaroKey, now.Baro > 0 ? Feet(BaroLabel, now.Baro) : $"{BaroLabel} cleared");
        if (now.Radio != was.Radio && (now.Radio != 0 || sayClears))
            yield return (RadioKey, now.Radio > 0 ? Feet(RadioLabel, now.Radio) : now.Radio < 0 ? A380Minimums.NoDh : $"{RadioLabel} cleared");
    }

    private static string Feet(string label, int feet) => string.Create(CultureInfo.InvariantCulture, $"{label} {feet} feet");
}
