namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>Where BTV keeps its rollout: the BTV object, a static inside GEAR (1.2.7: 1292128), its fields as their accessors
/// read them, and the address of the autobrake's level in the variable table. Derived by name from the module
/// (<see cref="A350ModuleFacts.Btv"/>).</summary>
public sealed record A350BtvFacts(uint Updater, int RunwayEndLatOffset, int RunwayEndLonOffset, int RunwayLengthOffset,
    int ExitOffsetOffset, int RemainingOffset, int MarginOffset, uint AutobrakeLevel);

/// <summary>
/// BTV's rollout as its object holds it, from the display reader's memory copy: the A350 publishes no variable for it. On the
/// ground <c>BTVUpdater::UpdateRemainingDistance</c> takes the great-circle distance to the runway's far end, and from it what is
/// left to the exit, in metres, short of a margin (60 m): the exit's distance from the threshold, less the runway's length less
/// that far end's distance, less the margin. Each number is one aligned read, so a copy spanning two frames [A350-2] holds an
/// old or a new one, and the call-outs' bands say it once whichever comes.
/// </summary>
public readonly record struct A350BtvRollout(double RunwayEndLat, double RunwayEndLon, double RunwayLength, float ExitOffset, float Remaining, float Margin, double AutobrakeLevel)
{
    /// <summary>The autobrake's level on BTV (<c>AutobrakeSystem::disengageBTV()</c> asks for it).</summary>
    public const int BtvLevel = 1;

    /// <summary>A runway set (<c>BTVUpdater::IsBTVEnabled()</c>) and the autobrake on BTV.</summary>
    public bool Active => RunwayEndLat != -1 && RunwayEndLon != -1 && AutobrakeLevel == BtvLevel;

    /// <summary>The metres to the exit, null with no exit (<c>BTVUpdater::ResetExit()</c> leaves its distance 0).</summary>
    public double? ToExit => ExitOffset > 0 ? Remaining + Margin : null;

    /// <summary>The metres to the runway's far end, null with no runway set.</summary>
    public double? RunwayRemaining => RunwayLength > 0 ? RunwayLength - ExitOffset + Remaining + Margin : null;

    /// <summary>The rollout in <paramref name="copy"/>, or null when the copy does not hold it or holds what no number can be.</summary>
    public static A350BtvRollout? Read(A350MemoryCopy copy, A350BtvFacts facts)
    {
        double? F64(uint address)
        {
            var b = copy.Read(address, 8);
            return b.Length == 8 && BitConverter.ToDouble(b) is var v && double.IsFinite(v) ? v : null;
        }
        float? F32(int offset)
        {
            var b = copy.Read(facts.Updater + (uint)offset, 4);
            return b.Length == 4 && BitConverter.ToSingle(b) is var v && float.IsFinite(v) ? v : null;
        }
        return F64(facts.Updater + (uint)facts.RunwayEndLatOffset) is double lat
            && F64(facts.Updater + (uint)facts.RunwayEndLonOffset) is double lon
            && F64(facts.Updater + (uint)facts.RunwayLengthOffset) is double length
            && F32(facts.ExitOffsetOffset) is float exit && F32(facts.RemainingOffset) is float remaining && F32(facts.MarginOffset) is float margin
            && F64(facts.AutobrakeLevel) is double level
            ? new A350BtvRollout(lat, lon, length, exit, remaining, margin, level) : null;
    }
}
