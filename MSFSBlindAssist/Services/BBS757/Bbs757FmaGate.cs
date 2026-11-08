namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The 757 FMA's comparison with the simulator (S3), the PMDG 737's FMA cross check for BlueBird's variables: memory is
/// trusted only while the Captain's and First Officer's F/D bytes agree with <c>BBS_FD1_SWITCH</c> and
/// <c>BBS_FD2_SWITCH</c>, and the MCP's selected altitude and heading with <c>BBS_AP_TARGET_ALTITUDE</c> and
/// <c>BBS_AP_TARGET_HEADING</c>. The simulator's variables may lag memory, so each field must match at least one
/// reading of the last <see cref="WindowMs"/> before the newest; different fields may match different readings (the F/D
/// switched while an MCP knob turns). Heading 0 is 360. The structural half of the gate is the sampler's: a reading is
/// only made when the objects are found and every read lies inside the module, and a corrupt one never matches.
/// </summary>
public static class Bbs757FmaGate
{
    public const string FdCaptainKey = "BBS_FD1_SWITCH";
    public const string FdFirstOfficerKey = "BBS_FD2_SWITCH";
    public const string AltitudeKey = "BBS_AP_TARGET_ALTITUDE";
    public const string HeadingKey = "BBS_AP_TARGET_HEADING";

    /// <summary>How far back a reading may match: the monitor's history.</summary>
    public const long WindowMs = Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading>.HistoryMs;

    /// <summary>True when every gate field matches some reading of <paramref name="history"/> (oldest first, the newest
    /// last) no more than <see cref="WindowMs"/> older than the newest. False with no reading, or when the simulator did
    /// not give a gate variable as a number.</summary>
    public static bool Passes(IReadOnlyList<(long AtMs, Bbs757FmaReading Reading)> history, Bbs757SimValues sim)
    {
        if (history is null || history.Count == 0) return false;
        if (Value(sim, FdCaptainKey) is not { } fd1 || Value(sim, FdFirstOfficerKey) is not { } fd2
            || Value(sim, AltitudeKey) is not { } altitude || Value(sim, HeadingKey) is not { } heading)
            return false;
        bool fdCaptain = fd1 > 0.5, fdFirstOfficer = fd2 > 0.5;
        int mcpAltitude = (int)Math.Round(altitude), mcpHeading = (int)Math.Round(heading);

        long newest = history[^1].AtMs;
        bool fd1Ok = false, fd2Ok = false, altOk = false, hdgOk = false;
        foreach (var (atMs, r) in history)
        {
            if (newest - atMs > WindowMs || r.Corrupt) continue;
            fd1Ok |= r.FdCaptain == fdCaptain;
            fd2Ok |= r.FdFirstOfficer == fdFirstOfficer;
            altOk |= r.McpAltitude == mcpAltitude;
            hdgOk |= HeadingEquals(r.McpHeading, mcpHeading);
        }
        return fd1Ok && fd2Ok && altOk && hdgOk;
    }

    /// <summary>Compass equality: 0 and 360 are the same heading.</summary>
    public static bool HeadingEquals(int a, int b) => ((a % 360) + 360) % 360 == ((b % 360) + 360) % 360;

    /// <summary>The simulator's value of <paramref name="key"/>, or null when it was not read or is not a number.</summary>
    private static double? Value(Bbs757SimValues sim, string key) => sim[key] is { } v && double.IsFinite(v) ? v : null;
}
