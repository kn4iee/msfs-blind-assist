namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// Says a new printout, as a sighted pilot hears the printer run. Nothing the sim streams tells of one: the paper's L:vars hold still
/// through a print, and INI_PRINTING_SOUND stays at 1 (live 2026-10-30, 1.2.7), so the paper's own text is read every
/// <see cref="EveryMs"/>. The first read is the baseline; a failed read (null) changes nothing, nor does a paper gone blank. Pure.
/// </summary>
public sealed class A350PrinterWatch
{
    public const string MuteKey = "A350_PRINTOUT";
    public const string MuteLabel = "New printouts";
    public const long EveryMs = 5000;

    private string? _paper;
    private long? _lastRead;
    private bool _reading;

    /// <summary>Whether a read is due; a read in flight is never doubled.</summary>
    public bool IsDue(long nowMs) => !_reading && (_lastRead is not { } last || nowMs - last >= EveryMs);

    public void Began(long nowMs)
    {
        _reading = true;
        _lastRead = nowMs;
    }

    /// <summary>A read's answer: the sentence for a new printout, else null.</summary>
    public string? Read(string? raw)
    {
        _reading = false;
        if (raw == null)
            return null;
        string? was = _paper;
        _paper = raw;
        if (was == null || raw == was || string.IsNullOrWhiteSpace(raw))
            return null;
        return "Printer: " + A350Printout.Title(raw);
    }

    /// <summary>Forgets the paper: a new flight's first read is its baseline.</summary>
    public void Reset()
    {
        _paper = null;
        _lastRead = null;
        _reading = false;
    }
}
