using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The TCAS's advisories in words, as the A32NX and A380 say theirs (<see cref="TcasRaGuidance"/>): the module's sound.xml
/// binds each aural to an L:var it sets to 1 as the aural starts and to 0 once the advisory is over (TcasSystem_Base's
/// updateTcasSounds and clear_ra_sounds), and its AP/FD TCAS mode flies <see cref="TargetVar"/>, so a rise says the aural's words
/// and the vertical speed to fly. The sentence waits <see cref="GuidanceSettleMs"/>, as [A380-18] asks: the guidance can lag the
/// sound. One Ctrl+M row mutes it all, and what is up at a reset is its baseline. Pure.
/// </summary>
public sealed class A350TcasCallouts
{
    public const string MuteKey = "A350_TCAS_ADVISORY";
    public const string MuteLabel = "TCAS advisories";
    public const string TargetKey = "A350_TCAS_VS_TGT", TargetVar = "INI_TCAS_VS_TGT";
    public const int GuidanceSettleMs = 800;

    /// <summary>Each aural's key, L:var and words, and whether it commands a vertical speed.</summary>
    public static readonly IReadOnlyList<(string Key, string Var, string Said, bool Rate)> Advisories = new[]
    {
        ("A350_TCAS_TRAFFIC", "INI_TCAS_TRAFFIC", "Traffic", false),
        ("A350_TCAS_CLIMB", "INI_TCAS_CLIMB", "Climb", true),
        ("A350_TCAS_CLIMB_CROSS", "INI_TCAS_CLIMB_CROSS", "Climb, crossing climb", true),
        ("A350_TCAS_CLIMB_NOW", "INI_TCAS_CLIMB_NOW", "Climb, climb now", true),
        ("A350_TCAS_CLIMB_MORE", "INI_TCAS_CLIMB_MORE", "Increase climb", true),
        ("A350_TCAS_DESCENT", "INI_TCAS_DESCENT", "Descend", true),
        ("A350_TCAS_DESCENT_CROSS", "INI_TCAS_DESCENT_CROSS", "Descend, crossing descend", true),
        ("A350_TCAS_DESCENT_NOW", "INI_TCAS_DESCENT_NOW", "Descend, descend now", true),
        ("A350_TCAS_DESCENT_MORE", "INI_TCAS_DESCENT_MORE", "Increase descent", true),
        ("A350_TCAS_LEVEL_OFF", "INI_TCAS_LEVEL_OFF", "Level off", true),
        ("A350_TCAS_ADJUST_VS", "INI_TCAS_ADJUST_VS", "Adjust vertical speed", true),
        ("A350_TCAS_MAINTAIN_VS", "INI_TCAS_MAINTAIN_VS", "Maintain vertical speed", true),
        ("A350_TCAS_MAINTAIN_VS_CROSS", "INI_TCAS_MAINTAIN_VS_CROSS", "Maintain vertical speed, crossing maintain", true),
        ("A350_TCAS_MONITOR_VS", "INI_TCAS_MONITOR_VS", "Monitor vertical speed", false),
        ("A350_TCAS_CLEAR", "INI_TCAS_CLEAR", "Clear of conflict", false),
    };

    private static readonly HashSet<string> AdvisoryKeys = Advisories.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsAdvisory(string key) => AdvisoryKeys.Contains(key);

    private readonly HashSet<string> _up = new(StringComparer.Ordinal);
    private readonly List<(string Said, bool Rate)> _pending = new();
    private long _pendingAt;

    /// <summary>An aural's flag delivered: a rise waits for the next <see cref="Flush"/> after the settle.</summary>
    public void Deliver(string key, double value, long nowMs)
    {
        if (value < 0.5)
        {
            _up.Remove(key);
            return;
        }
        if (!_up.Add(key))
            return;
        var advisory = Advisories.Single(a => a.Key == key);
        if (_pending.Count == 0)
            _pendingAt = nowMs;
        _pending.Add((advisory.Said, advisory.Rate));
    }

    /// <summary>The sentence due now, if any, with the vertical speed to fly where the advisory commands one.</summary>
    public string? Flush(long nowMs, double target)
    {
        if (_pending.Count == 0 || nowMs - _pendingAt < GuidanceSettleMs)
            return null;
        string said = "TCAS: " + string.Join(". ", _pending.Select(p => p.Said)) + ".";
        if (_pending.Any(p => p.Rate) && Math.Abs(target) >= 1)
            said += $" Fly vertical speed {TcasRaGuidance.FmtSignedFpm(target)} feet per minute.";
        _pending.Clear();
        return said;
    }

    /// <summary>A flight load or a reconnect: nothing waiting, and what is up now is no news.</summary>
    public void Reset(Func<string, double?> seen)
    {
        _pending.Clear();
        _up.Clear();
        foreach (var a in Advisories)
            if (seen(a.Key) >= 0.5)
                _up.Add(a.Key);
    }
}
