using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.A350;

/// <summary>
/// Landing-light, seat-belt and altimeter management for the iniBuilds A350 on altitude crossings, the A380 monitor's structure with
/// the A350's rows. 10,000 ft: climbing through 10,300 the landing lights and nose light go off; descending through 9,700 they come on.
/// The transition altitude and level (SimBrief only, no default): climbing through the altitude both baro knobs go to STD; descending
/// through the level both to QNH, with the call to set the local pressure. Direction gates are "not descending" and "not climbing",
/// so a vertical speed lull on the crossing tick cannot burn the latch without firing. Called from the SimConnect message thread;
/// executor calls are fire-and-forget.
/// </summary>
public sealed class A350FlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly A350ActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;
    private readonly TransitionCrossingDetector _trans = new();

    public A350FlightPhaseMonitor(A350ActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        // Every crossing call is queued: the light call, the seat-belt call and a transition call can come from one sample.
        _seatbelt = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.Announce);
    }

    public void SetThresholds(int transAltFt, int transLevelFt) => _trans.SetThresholds(transAltFt, transLevelFt);

    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _noTransReminderFired = false;
        _seatbelt.Reset();
    }

    /// <inheritdoc/>
    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

    /// <inheritdoc/>
    public void SetPlannedCruiseAltitude(int? cruiseFt) => _seatbelt.PlannedCruiseFt = cruiseFt;

    /// <inheritdoc/>
    public bool AutoLights10kEnabled { get; set; } = true;

    /// <inheritdoc/>
    public bool AutoAltimetersEnabled { get; set; } = true;

    public void Update(double altitudeFt, double verticalSpeedFpm, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        bool climbing = verticalSpeedFpm > 150;
        bool descending = verticalSpeedFpm < -150;

        Check10kCrossing(altitudeFt, climbing, descending);
        _seatbelt.Update(altitudeFt, verticalSpeedFpm, onGround);

        if (_trans.HasThresholds)
            CheckTransitionCrossing(altitudeFt, climbing, descending);
        else
            CheckNoTransitionReminder(altitudeFt, climbing);
    }

    private void CheckNoTransitionReminder(double alt, bool climbing)
    {
        if (!_noTransReminderFired && climbing && alt > 18_000 + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.Announce(TransitionAltimeters.NoTransitionLoadedReminder(AutoAltimetersEnabled));
        }
        else if (_noTransReminderFired && alt < 17_000)
        {
            _noTransReminderFired = false;
        }
    }

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;

        if (AutoLights10kEnabled)
        {
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                _ = _executor.SetLandingLights(0);
                _ = _executor.SetNoseLight(2);       // Off
                _announcer.Announce("Above ten thousand. Landing lights off.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                _ = _executor.SetLandingLights(1);
                _ = _executor.SetNoseLight(0);       // T.O
                _announcer.Announce("Below ten thousand. Landing lights on.");
            }
        }

        // The latch keeps tracking while the setting is off, so re-enabling in flight cannot fire a stale crossing.
        if (nowAbove) _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;
    }

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        var crossing = _trans.Update(alt, climbing, descending);
        if (TransitionAltimeters.ReminderInstead(crossing, AutoAltimetersEnabled) is { } reminder)
        {
            _announcer.Announce(reminder);
            return;
        }

        switch (crossing)
        {
            case TransitionCrossingDetector.Crossing.ClimbToStd:
                _ = AnnounceWhenDoneAsync(_executor.SetBaroStd(true), ClimbToStdDone, ClimbToStdRefused);
                break;
            case TransitionCrossingDetector.Crossing.DescendToQnh:
                _ = AnnounceWhenDoneAsync(_executor.SetBaroStd(false), DescendToQnhDone, DescendToQnhRefused);
                break;
        }
    }

    internal const string ClimbToStdDone = "Transition altitude. Altimeters set to standard.";
    internal const string ClimbToStdRefused = "Transition altitude. Set both altimeters to standard.";
    internal const string DescendToQnhDone = "Transition level. Altimeters set to QNH mode. Set local pressure now.";
    internal const string DescendToQnhRefused = "Transition level. Set both altimeters to QNH and set local pressure.";

    // The call says "set" only once the write went out, and otherwise hands the Captain the action.
    private async Task AnnounceWhenDoneAsync(Task<bool> write, string done, string refused)
    {
        bool ok;
        try { ok = await write; }   // no ConfigureAwait(false): Announce stays on the caller's (UI) context
        catch { ok = false; }
        _announcer.Announce(ok ? done : refused);
    }
}
