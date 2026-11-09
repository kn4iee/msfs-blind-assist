using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The lower EICAS's comparison with the simulator (S3, R10): memory is trusted only while each engine's HP spool (the N2
/// BlueBird draws) is within <see cref="SpoolTolerance"/> percent points of the simulator's <c>TURB ENG N2</c> for that
/// engine, and the page value equals <c>L:BBS_ECAM_Mode</c> truncated toward zero (BlueBird's L:var reader truncates).
/// The simulator's value may lag memory, so each field must match at least one reading of the last
/// <see cref="WindowMs"/> before the newest; different fields may match different readings. A corrupt reading never
/// matches.
/// </summary>
public static class Bbs757LowerEicasGate
{
    // Bare identifiers (R24): the registry builds each hidden copy's key as "BBS757_DISPLAY_GATE_" + key, and no 757
    // variable key carries a space or a colon.
    public const string LeftN2Key = "BBS757_LOWER_EICAS_N2_1";
    public const string RightN2Key = "BBS757_LOWER_EICAS_N2_2";
    public const string PageKey = "BBS_ECAM_Mode";

    /// <summary>How far apart (percent points) the memory's HP spool and the simulator's N2 may be.</summary>
    public const double SpoolTolerance = 0.1;

    /// <summary>How far back a reading may match: the monitor's history.</summary>
    public const long WindowMs = Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading>.HistoryMs;

    /// <summary>The three variables, stock N2 as simulator variables in percent (never L:vars, [VAR-2]), BlueBird's page an
    /// L:var.</summary>
    public static IReadOnlyList<Bbs757GateVariable> Variables { get; } = Array.AsReadOnly(new[]
    {
        new Bbs757GateVariable(LeftN2Key, "TURB ENG N2:1", SimVarType.SimVar, "percent"),
        new Bbs757GateVariable(RightN2Key, "TURB ENG N2:2", SimVarType.SimVar, "percent"),
        new Bbs757GateVariable(PageKey, "BBS_ECAM_Mode", SimVarType.LVar, null),
    });

    /// <summary>True when every gate field matches some reading of <paramref name="history"/> (oldest first, newest last)
    /// no more than <see cref="WindowMs"/> older than the newest. False with no reading or a missing simulator value.</summary>
    public static bool Passes(IReadOnlyList<(long AtMs, Bbs757LowerEicasReading Reading)> history, Bbs757SimValues sim)
    {
        if (history is null || history.Count == 0) return false;
        if (Value(sim, LeftN2Key) is not { } left || Value(sim, RightN2Key) is not { } right
            || Value(sim, PageKey) is not { } page)
            return false;
        if (Math.Abs(page) > int.MaxValue) return false;
        int pageValue = (int)Math.Truncate(page);

        long newest = history[^1].AtMs;
        bool leftOk = false, rightOk = false, pageOk = false;
        foreach (var (atMs, r) in history)
        {
            if (newest - atMs > WindowMs || r.Corrupt) continue;
            leftOk |= Math.Abs(r.LeftHpSpool - left) <= SpoolTolerance;
            rightOk |= Math.Abs(r.RightHpSpool - right) <= SpoolTolerance;
            pageOk |= r.PageValue == pageValue;
        }
        return leftOk && rightOk && pageOk;
    }

    private static double? Value(Bbs757SimValues sim, string key) => sim[key] is { } v && double.IsFinite(v) ? v : null;
}
