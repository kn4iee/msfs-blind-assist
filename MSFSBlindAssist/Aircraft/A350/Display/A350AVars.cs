namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>One A: var the A350's draw code asks the sim for: its ask as the engine sees it
/// ("TURB ENG N2:1|Percent"), the definition key MSFSBA streams it under, its stock name and unit.</summary>
public sealed record A350AVar(string Ask, string Key, string Name, string Units);

/// <summary>
/// The A: vars the A350's draw code reads from the sim rather than from its own memory (live, every gauge
/// drawn on the stand: engine N2 on the CTR, the baro settings on the EFIS and the ISIS; in flight the aircraft's
/// position too). The memory copy cannot hold them, so the definition streams each as a stock variable and the
/// draw engine is answered from the cache. An ask not listed here answers 0 and is logged once, so a flight can show what else is asked.
/// </summary>
public static class A350AVars
{
    public static readonly IReadOnlyList<A350AVar> All = new[]
    {
        Var("TURB ENG N2", 1, "Percent"), Var("TURB ENG N2", 2, "Percent"),
        Var("KOHLSMAN SETTING HG", 1, "inches of mercury"), Var("KOHLSMAN SETTING HG", 2, "inches of mercury"),
        Var("KOHLSMAN SETTING HG", 3, "inches of mercury"),
        // Asked in flight by the EFIS (2026-10-27).
        Var("PLANE LATITUDE", 0, "Degrees"), Var("PLANE LONGITUDE", 0, "Degrees"),
    };

    /// <summary>Index 0 is a variable with no index ("PLANE LATITUDE:0" asks for PLANE LATITUDE).</summary>
    private static A350AVar Var(string name, int index, string units) =>
        new($"{name}:{index}|{units}", $"A350_AVAR_{name.Replace(' ', '_')}_{index}", index == 0 ? name : $"{name}:{index}", units);

    /// <summary>One inch of mercury in millibars: a baro asked in millibars is answered from the inches.</summary>
    public const double MillibarsPerInch = 33.8639;

    private static readonly Dictionary<string, string> KeyByAsk = All.ToDictionary(a => a.Ask, a => a.Key, StringComparer.Ordinal);

    public static bool TryKey(string ask, out string key) => KeyByAsk.TryGetValue(ask, out key!);
}

/// <summary>Answers the draw engine's A: var asks from a cache read (SimConnect's, by definition key); an ask
/// MSFSBA does not stream is reported once. Safe from the display thread: the cache is concurrent.</summary>
public sealed class A350AVarAnswers
{
    private readonly Func<string, double?> _cached;
    private readonly Action<string> _unknown;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _reported = new();

    public A350AVarAnswers(Func<string, double?> cached, Action<string> unknown)
    {
        _cached = cached;
        _unknown = unknown;
    }

    public double? Answer(string ask)
    {
        if (A350AVars.TryKey(ask, out var key)) return _cached(key);
        // A baro asked in millibars (the EFIS asks KOHLSMAN SETTING HG:2|millibar too): from its inches.
        const string Millibar = "|millibar";
        if (ask.EndsWith(Millibar, StringComparison.Ordinal)
            && A350AVars.TryKey(ask[..^Millibar.Length] + "|inches of mercury", out var inches))
            return _cached(inches) * A350AVars.MillibarsPerInch;
        if (_reported.TryAdd(ask, true)) _unknown(ask);
        return null;
    }
}
