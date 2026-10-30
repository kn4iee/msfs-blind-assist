namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>Where the FMS keeps the take-off speeds: its active flight plan, a static object (1.2.7: 1418656), and each
/// speed's offset in it as its getter returns it (1.2.7: <c>FlightPlan::getV1Speed()</c> returns this+964, VR +968, V2
/// +972; <c>getFlexTemperature()</c> this+936, -1 when a version has no such getter). Derived by name from the module
/// (<see cref="A350ModuleFacts.TakeoffSpeeds"/>).</summary>
public sealed record A350TakeoffSpeedFacts(uint FlightPlan, int V1Offset, int VrOffset, int V2Offset, int FlexOffset = -1);

/// <summary>
/// The take-off speeds the A350's FMS holds, in knots, 0 while not set: what PERF T.O shows in cyan and the PFD's speed
/// tape marks. Read from the display reader's memory copy, as the aircraft writes them nowhere MSFSBA can read: INI_V1,
/// INI_VR and INI_V2 are in its variable table, but nothing in 1.2.7 writes them (sim 2026-10-30: 142, 143 and 148 on PERF,
/// on the PFD and in the flight plan, the three variables 0). A copy can span two frames [A350-2], so the definition
/// believes a read only once the next agrees (<see cref="A350StableValue{T}"/>). The FLEX temperature beside them is what arms a
/// FLX take-off (without one the levers at FLX give MCT and no SRS; live 2026-10-30, and the manual's p.108).
/// </summary>
public readonly record struct A350TakeoffSpeeds(int V1, int Vr, int V2, int Flex = 0)
{
    /// <summary>Above any speed the FMS takes: a read beyond it, or below 0, is no read.</summary>
    public const int MaxKnots = 400;

    /// <summary>The flight plan's speeds in <paramref name="copy"/>, or null when the copy does not hold them or holds
    /// what no speed can be.</summary>
    public static A350TakeoffSpeeds? Read(A350MemoryCopy copy, A350TakeoffSpeedFacts facts)
    {
        int? At(int offset)
        {
            var b = copy.Read(facts.FlightPlan + (uint)offset, 4);
            if (b.Length < 4) return null;
            int v = BitConverter.ToInt32(b);
            return v is >= 0 and <= MaxKnots ? v : null;
        }
        return At(facts.V1Offset) is int v1 && At(facts.VrOffset) is int vr && At(facts.V2Offset) is int v2
            ? new A350TakeoffSpeeds(v1, vr, v2, facts.FlexOffset >= 0 ? At(facts.FlexOffset) ?? 0 : 0) : null;
    }
}
