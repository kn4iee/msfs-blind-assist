namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Which draw a display window may show. After a key it shows a fresh draw; otherwise the background monitor's latest
/// (<see cref="A350PfdMonitor"/>). The monitor's latest at the moment of a fresh draw is older than it: shown on the next
/// tick, the list went back to the screen before the key and its change was spoken again (live 2026-10-28, the RMP window:
/// "standby 122.500", then "standby 123.450"). The draw worker runs one draw at a time, so the monitor's next draw is
/// newer. Told apart by identity: a capture is a record, and two draws of an unchanged screen are equal.
/// </summary>
public sealed class A350ShownCapture
{
    private A350DrawCapture? _superseded;

    /// <summary>A fresh draw is shown; <paramref name="latestNow"/>, the monitor's latest at this moment, is older.</summary>
    public void Fresh(A350DrawCapture? latestNow) => _superseded = latestNow;

    /// <summary>Whether <paramref name="latest"/> is older than the fresh draw shown, so not to be shown.</summary>
    public bool Superseded(A350DrawCapture? latest) => latest != null && ReferenceEquals(latest, _superseded);
}
