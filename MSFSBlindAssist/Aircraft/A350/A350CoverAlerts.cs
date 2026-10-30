namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The OIS's cover banners, heard with its window closed (X6): the OIS shows "Engine covers are still in place." while
/// <c>L:INI_COVERS_ENABLED</c> is set (in MSFS 2024) and "Pitot covers are still in place." while <c>A:COVER ON:2</c> is set
/// and the aircraft is on the ground (its decoded bundle, 2026-10-30: the <c>covers</c> and <c>pitot</c> handlers; OIS GROUND
/// &gt; COVERS set both live). A sighted pilot sees the red banner whenever the OIS is in view, so each is said as it appears,
/// a banner showing at the first look after a load included, and nothing as it goes. Pure.
/// </summary>
public sealed class A350CoverAlerts
{
    /// <summary>The engine covers' key, which is the call-out's Ctrl+M row; the pitot covers' (no row).</summary>
    public const string EnginesKey = "A350_COVERS_ENGINES", PitotKey = "A350_COVERS_PITOT";
    public const string MuteLabel = "OIS cover alerts";

    public const string Engine = "Engine covers are still in place.";
    public const string Pitot = "Pitot covers are still in place.";

    private bool _engine, _pitot;   // the banners shown at the last look

    /// <summary>The banners that appeared since the last look, engines first.</summary>
    public IReadOnlyList<string> Observe(bool engines, bool pitot, bool onGround)
    {
        bool pitotShown = pitot && onGround;
        var said = new List<string>();
        if (engines && !_engine) said.Add(Engine);
        if (pitotShown && !_pitot) said.Add(Pitot);
        _engine = engines;
        _pitot = pitotShown;
        return said;
    }

    /// <summary>A flight load: the banners the new flight shows are said at its first look.</summary>
    public void Reset()
    {
        _engine = false;
        _pitot = false;
    }
}
