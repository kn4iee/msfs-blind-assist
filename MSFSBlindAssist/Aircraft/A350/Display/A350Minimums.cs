namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>Where the FMS keeps the approach minimums: the active flight plan (<see cref="A350TakeoffSpeedFacts.FlightPlan"/>)
/// and the two fields <c>FlightPlan::resetApproachData()</c> clears, BARO then RADIO (1.2.7: +748 and +752). Derived by name
/// from the module (<see cref="A350ModuleFacts.Minimums"/>).</summary>
public sealed record A350MinimumsFacts(uint FlightPlan, int BaroOffset, int RadioOffset);

/// <summary>
/// The approach minimums PERF APPR holds, in feet: BARO (0 not set) and RADIO (0 not set, -1 NO DH). Live 2026-10-30: the
/// active flight plan's +748 and +752; entering one clears the other; CLR is refused on both; an approach change clears both
/// (<c>resetApproachData()</c>, from <c>enableTemporaryFlightplan</c>; a departure change did not). <c>INI_FMS_MDA</c> stayed
/// 0 with BARO 300 entered. Read from the display reader's copy, believed once the next read agrees.
/// </summary>
public readonly record struct A350Minimums(int Baro, int Radio)
{
    /// <summary>Above any minimum the FMS takes, in feet: a read beyond it is no read.</summary>
    public const int MaxFeet = 50000;

    /// <summary>The flight plan's minimums in <paramref name="copy"/>, or null when the copy does not hold them or holds what
    /// no minimum can be.</summary>
    public static A350Minimums? Read(A350MemoryCopy copy, A350MinimumsFacts facts)
    {
        int? At(int offset)
        {
            var b = copy.Read(facts.FlightPlan + (uint)offset, 4);
            return b.Length < 4 ? null : BitConverter.ToInt32(b);
        }
        return At(facts.BaroOffset) is int baro && baro is >= 0 and <= MaxFeet
            && At(facts.RadioOffset) is int radio && radio is >= -1 and <= MaxFeet
            ? new A350Minimums(baro, radio) : null;
    }
}
