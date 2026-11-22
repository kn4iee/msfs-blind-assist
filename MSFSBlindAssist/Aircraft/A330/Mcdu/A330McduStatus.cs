using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>The MCDU window's status row while it shows no page, and the reason it says the MCDU is unavailable (spec
/// D3), or null while the reader is only on its way (starting, waiting for the aircraft, reading).</summary>
internal readonly record struct A330McduStatusRow(string Text, string? UnavailableReason);

/// <summary>
/// The MCDU window's status row, in the display windows' own words (<see cref="Forms.IniA330.A330DisplayWindow"/>, its
/// remarks): the reader state's words while it is not Running; "&lt;Display&gt; is off" for a fresh newest frame that
/// is not drawn; "Reading the &lt;display&gt;"; and once <see cref="Forms.IniA330.A330DisplayWindow.CannotReadAfterMs"/>
/// have passed with no page read, "The display reader cannot read the &lt;display&gt;". The count runs from when the
/// window began wanting the display, from when the reader became Running, or from the last page read or fresh not-drawn
/// frame, whichever is latest. Also the row over a page: the display's name, or "Colours not read yet" (spec M14), and
/// the row for a frame that does not leave hidden rows out (spec M15). Pure: the clock is the caller's.
/// </summary>
internal sealed class A330McduStatus
{
    public const string ColoursNotReadRow = "Colours not read yet";
    public const string ListRowsNotReadRow = "List rows not read yet";

    private const string UnavailablePrefix = "A330 displays unavailable: ";

    private readonly int _refreshMs;
    private long? _countFrom;

    /// <param name="refreshMs">The window's refresh: a not-drawn frame is fresh for three of them, as the client's latest frame is.</param>
    public A330McduStatus(int refreshMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshMs);
        _refreshMs = refreshMs;
    }

    /// <summary>The "cannot read" count starts again now: the window began wanting the display, or a page was read.</summary>
    public void Restart(long nowMs) => _countFrom = nowMs;

    /// <summary>The status row while no page is shown, and the unavailable reason when there is one; it also keeps the count.</summary>
    public A330McduStatusRow Row(A330ReaderState state, string stateText, A330Frame? newest, A330Gauge gauge, long nowMs)
    {
        string name = A330GaugeNames.Name(gauge);
        if (state != A330ReaderState.Running)
        {
            _countFrom = null;   // the count runs from when the reader is Running again
            return new A330McduStatusRow(stateText, A330ReaderStatus.IsLasting(state) ? Reason(stateText) : null);
        }

        // Only a fresh one: the client keeps the newest frame after the display is let go.
        if (newest is { Drawn: false } off && nowMs - off.ReadTicks <= 3L * _refreshMs)
        {
            _countFrom = nowMs;
            return new A330McduStatusRow(Capitalised(name) + " is off", null);
        }

        _countFrom ??= nowMs;
        if (nowMs - _countFrom.Value < Forms.IniA330.A330DisplayWindow.CannotReadAfterMs)
            return new A330McduStatusRow("Reading the " + name, null);
        string cannot = "the display reader cannot read the " + name;
        return new A330McduStatusRow(Capitalised(cannot), cannot);
    }

    /// <summary>The status row over a page: the display's name, or "Colours not read yet" while the frame has none.</summary>
    public static string PageRow(A330Gauge gauge, bool coloursRead) =>
        coloursRead ? Capitalised(A330GaugeNames.Name(gauge)) : ColoursNotReadRow;

    /// <summary>A lasting state's words without the displays' own lead-in ("the display reader is not installed").</summary>
    private static string Reason(string stateText) =>
        stateText.StartsWith(UnavailablePrefix, StringComparison.Ordinal) ? stateText[UnavailablePrefix.Length..] : stateText;

    private static string Capitalised(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
