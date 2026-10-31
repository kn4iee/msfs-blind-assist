using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Seams the display monitor calls into, so a background reader can live in a file of its own without editing the monitor
/// (A350PfdMonitor). Each `partial void` is optional: with no implementation its call compiles to nothing.
/// </summary>
public partial class IniBuildsA350Definition
{
    /// <summary>The captain's EFIS as the display monitor drew it this tick: a good draw only, once a tick, after the FMA
    /// read. It runs on the monitor's POOL thread, so it must never touch SimConnect ([SIM-5], [SIM-20]): it reads the
    /// capture and speaks only through <paramref name="speak"/>, which posts the line to the UI thread. MainForm hands it on
    /// only while this definition is the current aircraft.</summary>
    /// <param name="capture">The draw. The monitor keeps it as the display's latest for the status displays and the windows:
    /// read it, never change it.</param>
    /// <param name="at">When the tick drew it.</param>
    /// <param name="settling">True while the call-outs follow a flight just loaded silently.</param>
    /// <param name="muted">Whether a Ctrl+M row is unticked, asked by its key.</param>
    /// <param name="speak">Says a line: posted to the UI thread, where the announcer speaks.</param>
    internal void ObserveCaptainEfis(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted,
        Action<string> speak) => ObserveNd(capture, at, settling, muted, speak);

    /// <summary>Plan 08's (IniBuildsA350Definition.Nd.cs); nothing until it is written.</summary>
    partial void ObserveNd(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted, Action<string> speak);
}
