namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Whether the displays can be read (pure; the service reports every draw). A draw fails when its copy
/// tears, and a few in a row are normal; live 2026-10-28 every draw failed for many minutes and the FMA
/// call-outs, the ECAM and the MFD window simply went quiet. When every draw has failed for
/// <see cref="UnreadableAfter"/> the pilot is told once, and told again once <see cref="GoodToRecover"/> draws in a
/// row read (one good draw among failures said so and flapped, review 2026-10-28). A pause in the draws longer than
/// <see cref="Pause"/> (no A350 while a flight loads, nothing asking) starts the window again. A pause is the time from
/// one draw's end to the next one's start: live 2026-10-28, with the A350's memory at 495 MB, one draw took 5 to 9 s,
/// so measuring from end to end called every draw a pause and nothing was ever said.
/// </summary>
public sealed class A350ReadHealth
{
    public static readonly TimeSpan UnreadableAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);
    public const int GoodToRecover = 3;

    private DateTime? _failingSince;
    private DateTime? _last;
    private bool _said;
    private int _good;

    /// <summary>One draw's outcome, started at <paramref name="started"/> and ended at <paramref name="ended"/>; the
    /// sentence to speak, if any.</summary>
    public string? Report(bool ok, DateTime started, DateTime ended)
    {
        if (_last is { } last && started - last > Pause)
            _failingSince = null;
        _last = ended;
        if (ok)
        {
            _failingSince = null;
            if (!_said || ++_good < GoodToRecover)
                return null;
            _said = false;
            _good = 0;
            return A350DisplayStatus.Readable;
        }
        _good = 0;
        _failingSince ??= started;
        if (_said || ended - _failingSince < UnreadableAfter) return null;
        _said = true;
        return A350DisplayStatus.Unreadable;
    }
}
