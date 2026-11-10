namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The 757 upper EICAS's comparison with the simulator (S3, as measured at both engine starts): memory is trusted only
/// while each engine's N1 as BlueBird keeps it (<see cref="Bbs757UpperEicasReading.GateN1Left"/>,
/// <see cref="Bbs757UpperEicasReading.GateN1Right"/>:
/// the engine field the draw reads, never the alternate) is within <see cref="N1Tolerance"/> of the simulator's
/// <c>TURB ENG N1:1</c> and <c>TURB ENG N1:2</c>. At steady idle the two agree to 0.0002 %; while N1 rises memory leads
/// the simulator by about 0.5 s, and at light-off the simulator's N1 jumps for about a second, so each engine must match
/// at least one reading of the last <see cref="WindowMs"/> before the newest, and the two engines may match different
/// readings. The manual thrust reference flag the sampler also takes is not compared. The structural half of the gate is
/// the sampler's: a reading is only made when the objects are found and every read lies inside the module, and a corrupt
/// one never matches.
/// </summary>
public static class Bbs757UpperEicasGate
{
    public const string N1LeftKey = "TURB ENG N1:1";
    public const string N1RightKey = "TURB ENG N1:2";

    /// <summary>How far memory's N1 may be from the simulator's, in % N1: measured at the engine starts, 2.5 leaves
    /// about 0.5 s failing per start against the monitor's 2 s grace.</summary>
    public const double N1Tolerance = 2.5;

    /// <summary>How far back a reading may match: the monitor's history.</summary>
    public const long WindowMs = Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.HistoryMs;

    /// <summary>True when, for the left and the right engine each, some reading of <paramref name="history"/> (oldest
    /// first, the newest last) that is not corrupt and no more than <see cref="WindowMs"/> older than the newest has its
    /// N1 within <see cref="N1Tolerance"/> of the simulator's. False with no reading, or when the simulator did not give
    /// either N1 as a number.</summary>
    public static bool Passes(IReadOnlyList<(long AtMs, Bbs757UpperEicasReading Reading)> history, Bbs757SimValues sim)
    {
        if (history is null || history.Count == 0) return false;
        if (Value(sim, N1LeftKey) is not { } left || Value(sim, N1RightKey) is not { } right) return false;

        long newest = history[^1].AtMs;
        bool leftOk = false, rightOk = false;
        foreach (var (atMs, r) in history)
        {
            if (newest - atMs > WindowMs || r is null || r.Corrupt) continue;
            leftOk |= Math.Abs(r.GateN1Left - left) <= N1Tolerance;
            rightOk |= Math.Abs(r.GateN1Right - right) <= N1Tolerance;
        }
        return leftOk && rightOk;
    }

    /// <summary>The simulator's value of <paramref name="key"/>, or null when it was not read or is not a number.</summary>
    private static double? Value(Bbs757SimValues sim, string key) => sim[key] is { } v && double.IsFinite(v) ? v : null;
}
