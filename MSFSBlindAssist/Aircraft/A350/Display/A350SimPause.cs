namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>Where the variable table holds the pause's two variables, <c>INI_IS_PAUSED</c> and <c>FMGS_PAUSE_TYPE</c>, found by
/// name (<see cref="A350ModuleFacts.Pause"/>).</summary>
public sealed record A350PauseFacts(uint IsPaused, uint Type);

/// <summary>
/// Whether the sim is paused and why, as the OIS asks: 1 the top of descent, 2 the autopilot dropping under time compression, 0
/// anything else. Read from the display reader's copy, as nothing says the aircraft's own pause through the sim's stream while it
/// is paused; a copy needs no running sim.
/// </summary>
public readonly record struct A350SimPause(bool IsPaused, int Type)
{
    public const int TopOfDescent = 1, AutopilotDisconnect = 2;

    /// <summary>The pause in <paramref name="copy"/>, or null when the copy does not hold it or holds what no variable can be.</summary>
    public static A350SimPause? Read(A350MemoryCopy copy, A350PauseFacts facts)
    {
        double? At(uint address)
        {
            var b = copy.Read(address, 8);
            return b.Length == 8 && BitConverter.ToDouble(b) is var v && double.IsFinite(v) ? v : null;
        }
        return At(facts.IsPaused) is double paused && At(facts.Type) is double type && Math.Abs(type) < 1000
            ? new A350SimPause(paused >= 0.5, (int)type) : null;
    }
}
