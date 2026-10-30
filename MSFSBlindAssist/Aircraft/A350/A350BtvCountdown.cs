using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The BTV rollout's countdown, in the A380's words, bands and row names (<c>FlyByWireA380Definition.ProcessSimVarUpdate</c>):
/// each band said once as the distance falls through it, every band above the distance marked with it, so a rollout starting
/// below the top bands says where it is once and never fires them later. Pure; one instance per aircraft.
/// </summary>
public sealed class A350BtvCountdown
{
    public const string ExitKey = "A350_BTV_DISTANCE_TO_EXIT", RunwayKey = "A350_BTV_RUNWAY_REMAINING";

    /// <summary>The rows, for Ctrl+M.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Rows = new[] { (ExitKey, "BTV Distance to Exit"), (RunwayKey, "Runway Remaining") };

    /// <summary>Beyond any runway: what the BTV object holds with no runway set is the distance to latitude -1, longitude -1.</summary>
    private const double MaxMetres = 9000;

    private static readonly int[] ExitBands = { 1200, 800, 500, 300, 150 }, RunwayBands = { 900, 600, 300, 150 };

    private readonly HashSet<int> _exitSaid = new(), _runwaySaid = new();

    /// <summary>What to say for one read, each line with its row; nothing, and a new count, while not <paramref name="rolling"/>.</summary>
    public IReadOnlyList<(string Key, string Text)> Observe(bool rolling, double? toExit, double? runwayRemaining)
    {
        if (!rolling)
        {
            _exitSaid.Clear();
            _runwaySaid.Clear();
            return Array.Empty<(string, string)>();
        }
        var said = new List<(string, string)>(2);
        if (Crossed(toExit, ExitBands, _exitSaid) is int exit)
            said.Add((ExitKey, string.Create(CultureInfo.InvariantCulture, $"{exit} meters to exit")));
        if (Crossed(runwayRemaining, RunwayBands, _runwaySaid) is int runway)
            said.Add((RunwayKey, string.Create(CultureInfo.InvariantCulture, $"{runway} meters runway remaining")));
        return said;
    }

    /// <summary>The distance to the nearest 10 m when it newly enters a band, else null.</summary>
    private static int? Crossed(double? metres, int[] bands, HashSet<int> said)
    {
        if (metres is not double m || !double.IsFinite(m) || m <= 0 || m > MaxMetres)
            return null;
        bool entered = false;
        foreach (int band in bands)
            if (m <= band && said.Add(band))
                entered = true;
        return entered ? (int)(Math.Round(m / 10.0, MidpointRounding.AwayFromZero) * 10) : null;
    }
}
