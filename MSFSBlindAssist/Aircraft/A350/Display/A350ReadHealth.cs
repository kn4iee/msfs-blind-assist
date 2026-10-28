namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Whether the displays can be read (pure; the service reports every draw). A draw fails when its copy
/// tears, and a few in a row are normal; live 2026-10-28 every draw failed for many minutes and the FMA
/// call-outs, the ECAM and the MFD window simply went quiet. When every draw has failed for
/// <see cref="UnreadableAfter"/> the pilot is told once, and told again when a draw succeeds.
/// </summary>
public sealed class A350ReadHealth
{
    public static readonly TimeSpan UnreadableAfter = TimeSpan.FromSeconds(10);

    private DateTime? _failingSince;
    private bool _said;

    /// <summary>One draw's outcome; the sentence to speak, if any.</summary>
    public string? Report(bool ok, DateTime now)
    {
        if (ok)
        {
            _failingSince = null;
            if (!_said) return null;
            _said = false;
            return A350DisplayStatus.Readable;
        }
        _failingSince ??= now;
        if (_said || now - _failingSince < UnreadableAfter) return null;
        _said = true;
        return A350DisplayStatus.Unreadable;
    }
}
