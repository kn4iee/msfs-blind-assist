using System;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.A350;

/// <summary>
/// Automated flap management for the iniBuilds A350: the A380's schedule on the A350's own speeds. Gear and autopilot are the app-wide
/// UniversalAutomationService's. Driven by the speed tape's variables (green dot <c>FMGEC_GD_SPD</c>, S <c>FCPC_S_SPEED</c>, F
/// <c>FCPC_F_SPEED</c>, VFE next <c>INI_VFE_SPEED</c>): extension on approach (capped at flaps 3 when PERF APPR's landing
/// configuration is CONF 3), retraction after takeoff or a go-around, and the Airbus go-around one step. The takeoff setting is
/// never automated (a Captain item).
/// </summary>
public sealed class A350FOAutoManager : IFoAutoManager
{
    private readonly A350ActionExecutor _executor;
    private readonly A350StateEvaluator _state;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<DateTime> _utcNow;

    /// <summary>The flap schedule's switch. Switched on while not known to be on the ground, the
    /// retraction waits for the next takeoff (or a go-around this manager detects).</summary>
    public bool AutoFlapsEnabled
    {
        get => _autoFlapsEnabled;
        set
        {
            if (value && !_autoFlapsEnabled && _lastOnGround != true) _retractionArmed = false;
            _autoFlapsEnabled = value;
        }
    }
    private bool _autoFlapsEnabled;

    private bool? _lastOnGround;   // null = no sample since construction or Reset

    // The retraction schedule runs only after a takeoff this manager watched (a ground sample) or a
    // go-around it detected, and only until the flaps first read up in the air, whoever moved them.
    // A window first opened in flight, or Auto Flaps switched on there, once started retracting at
    // once: an arrival's flaps 1 at S speed came up ("Flaps up."). So did a flaps 1 the pilot
    // selected level on the arrival after cleaning up the climb-out himself, because nothing but
    // Reset ended the takeoff's retraction.
    private bool _retractionArmed;

    // The highest MSL altitude since the leg began or the approach context was last left (NaN = none
    // yet). The approach context arms only on a descent at least ApproachArmDescentFt below it. MSL,
    // never AGL: AGL follows the terrain, so a level departure segment over rising ground "descended"
    // 300 ft and armed the approach context on the departure.
    private double _peakMsl = double.NaN;
    private const double ApproachArmDescentFt = 300;

    // When the current climb (VS above +200 fpm) began, on the clock; null while not climbing. The
    // go-around fallback needs an ESTABLISHED climb: one turbulence or ALT-capture sample of +250 fpm
    // level on a high final cleared the approach context and retracted flaps 1 ("Flaps up.").
    private DateTime? _climbingSinceUtc;

    // ---- Flap schedule state ----
    private const int FlapDwellSeconds = 4;      // min gap between automatic lever movements
    private const double VfeNextMarginKts = 5;   // extension overspeed guard vs VFE-next
    private const double MinActionIasKts = 30;   // sanity floor: no scheduling on bogus/zero IAS
    private const double GaClimbFpm = 500;       // go-around: sustained climb rate…
    private const double GaAglGainFt = 200;      // …plus AGL regained above the approach minimum
    private const double GaFallbackClimbSeconds = 10;   // the fallback's established climb (a judgement)

    // Nothing retracts below the acceleration segment (the manual's "between 1,000 and 1,500 ft"): the A350's F speed reads below
    // V2 (live 2026-10-30, F 138, V2 148 at 200 t), so without this the lever came up to 1 at lift-off and to 0 at 430 ft.
    private const double RetractionMinAglFt = 1000;

    // The managed speed holds what the next flap waits for ("the aircraft will not decelerate below green dot" clean, the manual; live 2026-10-30: 191
    // with green dot at 190.6, level at 4000 ft, flaps up): come down to it is enough.
    private const double ExtendWithinKts = 3;

    // Below this on the descent the next flap does not wait for its speed, only for VFE next: at idle on the glide path the speed can settle
    // just above it (live 2026-10-30: 154 with F at 152.9, flaps 2 through 1100 ft) and the manual's rule is "Always select your next flap
    // setting when below Vfe next". High enough for the two last steps to be out by 1000 ft, the dwell between them.
    private const double LandingFlapsGateAglFt = 1500;

    // ...on an established descent: one sag level at circuit height stepped the next flap.
    private const double LandingGateSinkFpm = 300;
    private const double LandingGateSinkSeconds = 5;

    // The levers just past CLB: a go-around still sinking for seconds, which no flap extends into. After that it may be an approach with an
    // engine out, at MCT.
    private const double GoAroundLeverHoldSeconds = 10;

    // Approach context: armed by a DESCENDING sample below 5000 ft AGL at least ApproachArmDescentFt
    // below the leg's highest MSL altitude (_peakMsl), one-way until go-around or touchdown. Extension
    // runs ONLY while armed, and retraction ONLY while disarmed — without this gate a departure
    // level-off with takeoff flaps still out would read as "extension" (IAS < F is true by
    // construction until retraction completes), extend flaps on climb-out, and block its own
    // retraction schedule (final-review C1). Arming on descent also stops the FO retracting a
    // configuration the PILOT extended manually on a level deceleration segment (I3). One descending
    // sample once armed it, so an ALT* overshoot or a gust at a departure level-off extended flaps
    // ("Speed checked. Flaps 2.") with the takeoff flaps still out, and between 3000 and 5000 ft the
    // go-around fallback and the next such sample cycled the lever every dwell.
    private bool _extensionArmed;
    private double _minAglInApproach = double.MaxValue;
    private DateTime? _goAroundThrustAtUtc;      // when the levers first went to MCT or TOGA in this approach context
    private DateTime? _sinkingSinceUtc;          // below -LandingGateSinkFpm since, on the clock
    private int _lastExtendCommand = -1;         // don't re-fight a pilot who pulled flaps back
    private int _lastRetractCommand = 99;        // don't re-fight a pilot who re-extended
    private DateTime _lastFlapMoveUtc = DateTime.MinValue;

    /// <param name="utcNow">The clock (a test's own; the system clock when omitted). Every duration
    /// here is clock time, never a sample count: position samples arrive whenever any feature asks
    /// for them (SeatbeltAutomation's rule).</param>
    public A350FOAutoManager(
        A350ActionExecutor executor,
        A350StateEvaluator state,
        ScreenReaderAnnouncer announcer,
        Func<DateTime>? utcNow = null)
    {
        _executor = executor;
        _state = state;
        _announcer = announcer;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void Reset()
    {
        _lastOnGround = null;
        _retractionArmed = false;
        ResetFlapState();
    }

    private void ResetFlapState()
    {
        _extensionArmed     = false;
        _minAglInApproach   = double.MaxValue;
        _goAroundThrustAtUtc = null;
        _sinkingSinceUtc    = null;
        _lastExtendCommand  = -1;
        _lastRetractCommand = 99;
        _lastFlapMoveUtc    = DateTime.MinValue;
        _peakMsl            = double.NaN;
        _climbingSinceUtc   = null;
    }

    // Leave the approach context (go-around detected): extension disarms and a
    // fresh extension ladder is armed for the next approach, measured from a new peak.
    private void Disarm()
    {
        _extensionArmed    = false;
        _minAglInApproach  = double.MaxValue;
        _goAroundThrustAtUtc = null;
        _lastExtendCommand = -1;
        _peakMsl           = double.NaN;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        if (onGround)
        {
            if (_lastOnGround != true)
                ResetFlapState();   // touchdown: after-landing flaps-up is a Captain item
            _lastOnGround = true;
            _retractionArmed = true;   // a takeoff from here is watched
            return;
        }
        _lastOnGround = false;
        if (!double.IsNaN(altitudeMsl) && (double.IsNaN(_peakMsl) || altitudeMsl > _peakMsl))
            _peakMsl = altitudeMsl;

        bool climbing   = verticalSpeedFpm >  200;
        bool descending = verticalSpeedFpm < -100;
        if (!climbing) _climbingSinceUtc = null;
        else _climbingSinceUtc ??= _utcNow();
        if (verticalSpeedFpm < -LandingGateSinkFpm) _sinkingSinceUtc ??= _utcNow();
        else _sinkingSinceUtc = null;

        if (AutoFlapsEnabled)
            CheckFlaps(airspeedKts, verticalSpeedFpm, altitudeAgl, altitudeMsl, climbing, descending);
    }

    // -----------------------------------------------------------------------
    // Flap schedule — extension on approach, retraction after takeoff /
    // go-around, Airbus-SOP go-around one step. Speeds are the
    // speed tape's variables; the flap lever 0..4 (0/1/2/3/FULL).
    // -----------------------------------------------------------------------
    private void CheckFlaps(double ias, double vs, double agl, double msl, bool climbing, bool descending)
    {
        if (double.IsNaN(agl) || double.IsNaN(ias)) return;   // I3: makes the accidental :127 NaN work explicit
        if (ias < MinActionIasKts) return;   // bogus/cold IAS — never schedule on it

        double flapsRaw = _state.GetValue("A350_FLAPS");
        if (double.IsNaN(flapsRaw)) return;  // handle position unknown — do nothing
        int flaps = (int)System.Math.Round(flapsRaw);

        // The takeoff (or go-around) retraction is complete once the flaps read up in the air,
        // whoever moved them: only a detected go-around arms it again.
        if (flaps == 0) _retractionArmed = false;

        // CONF 3 landing selected on the MFD PERF APPR page? NaN/0 = FULL.
        double conf3Raw = _state.GetValue(A350StateEvaluator.LandingConf3Field);
        bool conf3 = !double.IsNaN(conf3Raw) && conf3Raw > 0.5;

        // Arm the approach context on a real descent below 5000 ft AGL (one-way until
        // go-around or touchdown). See the _extensionArmed field comment for why extension
        // must never run unarmed, and why one descending sample is not a descent.
        if (!_extensionArmed && descending && agl < 5000 && msl <= _peakMsl - ApproachArmDescentFt)
            _extensionArmed = true;

        // Track the lowest AGL seen while in the approach context — the go-around
        // detector needs a real climb-out signature a turbulence gust can't fake.
        if (_extensionArmed && agl < _minAglInApproach)
            _minAglInApproach = agl;

        // The levers may be back at CL before the height is regained, so once is enough.
        if (_extensionArmed && _state.GetValue(A350StateEvaluator.GoAroundThrustField) > 0.5)
            _goAroundThrustAtUtc ??= _utcNow();

        // ---- Go-around: disarm + SOP one-step retraction ----
        if (_extensionArmed)
        {
            // Primary signature: the levers of a go-around, a sustained climb AND real height
            // regained above the approach's minimum — a bare VS spike can never fire the lever,
            // nor ALT taking back a sag at 1000 fpm (live 2026-10-30: the flaps came up at 9 NM).
            bool gaPrimary  = _goAroundThrustAtUtc != null && vs > GaClimbFpm && agl >= _minAglInApproach + GaAglGainFt;
            // 737-style high safeguard: clears a stuck approach context on an ESTABLISHED
            // climb (above +200 fpm for GaFallbackClimbSeconds) above 3000 ft AGL.
            // LATCH-CLEAR ONLY — it must never command the SOP step (final-review I1) nor
            // arm a retraction: one climbing sample on a high final once armed one, and the
            // pilot's flaps 1 came up ("Flaps up.").
            bool gaFallback = ClimbEstablished() && agl > 3000;

            if (gaPrimary && flaps >= 3)
            {
                // SOP "GO-AROUND — FLAPS": one immediate speed-independent step
                // (FULL -> 3, or 3 -> 2 on a CONF 3 approach). Retraction is
                // always VFE-safe. Dwell-blocked (balked landing seconds after
                // "Flaps full")? Stay armed and retry next tick — the SOP step
                // must never be silently lost (final-review I2).
                if (!DwellElapsed()) return;
                int target = flaps - 1;
                CommandFlaps(target, $"Go-around. Flaps {FlapName(target)}.");
                _lastRetractCommand = target;
                Disarm();
                _retractionArmed = true;   // the climb-out after a go-around retracts too
                return;   // one movement per tick
            }
            if (gaPrimary)
            {
                Disarm();
                _lastRetractCommand = 99;   // fresh retraction sequence for the climb-out
                _retractionArmed = true;
            }
            else if (gaFallback)
            {
                Disarm();   // the approach latch only: nothing armed, nothing moved
            }
        }

        // ---- Retraction (climbing or level, NOT in the approach context) ----
        // Same rule for flaps-2 and flaps-3 takeoffs (Airbus SOP: at F speed select
        // flaps 1, at S speed clean). Deliberately independent of CONF 3. Only after a
        // takeoff or go-around this manager watched, until the flaps first read up
        // (_retractionArmed).
        if (_retractionArmed && !_extensionArmed && !descending && agl >= RetractionMinAglFt)
        {
            double f = _state.GetValue(A350StateEvaluator.FSpeedField);
            double s = _state.GetValue(A350StateEvaluator.SSpeedField);

            if (flaps >= 2 && !double.IsNaN(f) && ias >= f)
            {
                TryRetract(1, "Flaps 1.");
                return;
            }
            if (flaps == 1 && !double.IsNaN(s) && ias >= s)
            {
                TryRetract(0, "Flaps up.");
                return;
            }
        }

        // ---- Extension (ARMED approach context, not climbing, below 5000 AGL,
        // VFE-next protected) ----
        if (_extensionArmed && !climbing && agl < 5000)
        {
            if (_goAroundThrustAtUtc is { } leversAt && (_utcNow() - leversAt).TotalSeconds < GoAroundLeverHoldSeconds)
                return;
            double vfeNext = _state.GetValue(A350StateEvaluator.VfeNextField);
            if (double.IsNaN(vfeNext) || ias >= vfeNext - VfeNextMarginKts)
                return;   // unknown or too fast for the next config — hold

            double gearRaw = _state.GetValue("A350_LDG_LEVER");
            bool gearDown = !double.IsNaN(gearRaw) && gearRaw > 0.5;   // unknown = NOT down (gate holds)

            double gd = _state.GetValue(A350StateEvaluator.GreenDotField);
            double s  = _state.GetValue(A350StateEvaluator.SSpeedField);
            double f  = _state.GetValue(A350StateEvaluator.FSpeedField);
            bool landingGate = agl < LandingFlapsGateAglFt && _sinkingSinceUtc is { } sinking
                && (_utcNow() - sinking).TotalSeconds >= LandingGateSinkSeconds;

            switch (flaps)
            {
                case 0 when landingGate || (!double.IsNaN(gd) && ias < gd + ExtendWithinKts):
                    TryExtend(1, "Speed checked. Flaps 1.");
                    break;
                case 1 when landingGate || (!double.IsNaN(s) && ias < s + ExtendWithinKts):
                    TryExtend(2, "Speed checked. Flaps 2.");
                    break;
                case 2 when landingGate || (!double.IsNaN(f) && ias < f + ExtendWithinKts):
                    // With CONF 3 selected this IS the final landing step -> gear gate.
                    if (!conf3 || gearDown)
                        TryExtend(3, "Speed checked. Flaps 3.");
                    break;
                case 3 when !conf3 && (landingGate || (!double.IsNaN(f) && ias < f + ExtendWithinKts)) && gearDown:
                    // Final landing step for a FULL landing -> gear gate. With CONF 3
                    // selected the schedule stops at 3 (never commands FULL).
                    TryExtend(4, "Speed checked. Flaps full.");
                    break;
            }
        }
    }

    // Extension is monotonic per approach: if the pilot pulled flaps back after we
    // extended, we do not re-fight them (target must exceed the last auto command).
    private void TryExtend(int target, string announcement)
    {
        if (target <= _lastExtendCommand || !DwellElapsed()) return;
        CommandFlaps(target, announcement);
        _lastExtendCommand = target;
        // No latch to set here: extension only runs while _extensionArmed, which
        // was armed by the descending sample that opened the approach context.
    }

    // Retraction is monotonic per climb-out: if the pilot re-extended after we
    // retracted, we do not re-fight them.
    private void TryRetract(int target, string announcement)
    {
        if (target >= _lastRetractCommand || !DwellElapsed()) return;
        CommandFlaps(target, announcement);
        _lastRetractCommand = target;
        _lastExtendCommand = -1;   // a genuine retraction re-arms a later approach
    }

    // The call is QUEUED (Announce), never interrupting: AnnounceImmediate cancelled NVDA's whole
    // queue, so a flow's Captain reminder or a phase-monitor call spoken just before it was cut off
    // (the phase monitors queue every call for the same reason).
    private void CommandFlaps(int target, string announcement)
    {
        _ = _executor.Set("A350_FLAPS", target);
        _announcer.Announce(announcement);
        _lastFlapMoveUtc = _utcNow();
    }

    private bool DwellElapsed()
        => (_utcNow() - _lastFlapMoveUtc).TotalSeconds >= FlapDwellSeconds;

    private bool ClimbEstablished()
        => _climbingSinceUtc is DateTime since
           && (_utcNow() - since).TotalSeconds >= GaFallbackClimbSeconds;

    private static string FlapName(int idx) => idx switch
    {
        0 => "up",
        4 => "full",
        _ => idx.ToString(),
    };
}
