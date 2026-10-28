namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Whether each display can be read (pure; the service reports every draw). A draw fails when its copy tears, and a
/// few in a row are normal; live 2026-10-28 every draw failed for many minutes and the FMA call-outs, the ECAM and the
/// MFD window simply went quiet. When a display's draws have failed for <see cref="UnreadableAfter"/> the pilot is told
/// once which display it is, and told again once <see cref="GoodToRecover"/> of its draws in a row read (one good draw
/// among failures said so and flapped, review 2026-10-28). Each display keeps its own count: live 2026-10-28 the E/WD
/// read on some draws while the PFD, ND and MFD never did, and one count for all let each E/WD read hide them.
/// A pause in the reader's draws longer than <see cref="Pause"/> (no A350 while a flight loads, nothing asking) starts
/// every display's window again. A pause is the time from one draw's end to the next one's start, of any display: live
/// 2026-10-28, with the A350's memory at 495 MB, one draw took 5 to 9 s, so measuring from end to end called every draw
/// a pause and nothing was ever said; and a display the monitor draws in turn may come round only every half minute.
/// </summary>
public sealed class A350ReadHealth
{
    public static readonly TimeSpan UnreadableAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);
    public const int GoodToRecover = 3;

    private sealed class Display
    {
        public DateTime? FailingSince;
        public bool Said;
        public int Good;
    }

    private readonly Dictionary<(string, int), Display> _displays = new();
    private DateTime? _last;

    /// <summary>One draw of a display (<paramref name="gauge"/>, <paramref name="index"/>), started at
    /// <paramref name="started"/> and ended at <paramref name="ended"/>; the sentence to speak, if any.</summary>
    public string? Report(string gauge, int index, bool ok, DateTime started, DateTime ended)
    {
        if (_last is { } last && started - last > Pause)
            foreach (var d in _displays.Values)
                d.FailingSince = null;
        if (_last is not { } previous || ended > previous) _last = ended;
        if (!_displays.TryGetValue((gauge, index), out var display))
            _displays[(gauge, index)] = display = new Display();
        if (ok)
        {
            display.FailingSince = null;
            if (!display.Said || ++display.Good < GoodToRecover)
                return null;
            display.Said = false;
            display.Good = 0;
            return A350DisplayStatus.Readable(gauge, index);
        }
        display.Good = 0;
        display.FailingSince ??= started;
        if (display.Said || ended - display.FailingSince < UnreadableAfter) return null;
        display.Said = true;
        return A350DisplayStatus.Unreadable(gauge, index);
    }
}
