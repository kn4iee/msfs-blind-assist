using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// What the A350's levers say as they move, in the A380's words (<c>FlyByWireA380Definition</c>'s TLA detents and
/// speed brake bands): a thrust lever speaks its detent once it reaches one, "All thrust levers Climb" while both sit at
/// it, "Thrust lever 2 TOGA" when they are split, nothing mid-travel; the speed brake handle in tenths. The first
/// values are a silent baseline. A lever's own sentence waits <see cref="GatherMs"/> for the other lever, said at a batch
/// end (<see cref="Flush"/>): moved together they arrive a delivery apart. Each sentence names the Ctrl+M row it belongs
/// to. Pure.
/// </summary>
public sealed class A350LeverSpeech
{
    public const string Lever1 = "A350_ENGINE_THROTTLE_1";
    public const string Lever2 = "A350_ENGINE_THROTTLE_2";
    public const string AllLevers = "A350_THRUST_LEVERS_ALL";

    /// <summary>How long a lever's own sentence waits for the other lever (live 2026-10-28: MSFSBA moving both to FLX
    /// spoke "Thrust lever 1 Flex M C T", then "All thrust levers Flex M C T").</summary>
    public const int GatherMs = 400;

    private readonly double[] _tla = { double.NaN, double.NaN };
    private readonly string?[] _lastDetent = new string?[2];
    private string? _lastAll;
    private int _lastBand = -1;

    /// <summary>Each lever's sentence waiting for the other lever: its detent, when it arrived, and whether its delivery
    /// was inside MainForm's echo window (the pilot's own pick, which the screen reader said: dropped, never said).</summary>
    private readonly (string Detent, long At, bool Suppressed)?[] _held = new (string, long, bool)?[2];
    private bool _onGround = true;

    /// <summary>The take-off's first step (<see cref="A350Levers"/>).</summary>
    public const string TakeoffSpool = "50 percent N1";

    /// <summary>The detent a TLA sits at (the A350's: reverse -20 to 0, reverse idle -6, CLB 25, FLX/MCT 35, TOGA 45; and the
    /// take-off's first step, 12), or null between detents. "Flex M C T" so a speech engine spells it.</summary>
    public static string? Detent(double tla) =>
        Math.Abs(tla) < 1.5 ? "Idle" :
        Math.Abs(tla - 12) < 1.5 ? TakeoffSpool :
        Math.Abs(tla - 25) < 2.5 ? "Climb" :
        Math.Abs(tla - 35) < 2.5 ? "Flex M C T" :
        Math.Abs(tla - 45) < 2.5 ? "TOGA" :
        Math.Abs(tla + 6) < 2.5 ? "Reverse idle" :
        tla <= -17.5 ? "Maximum reverse" : null;

    /// <summary>The word for a TLA as this flight stands: the take-off's first step only on the ground; in the air a lever passes it, and N1
    /// there is no 50 percent.</summary>
    private string? Said(double tla) => Detent(tla) is var word && word == TakeoffSpool && !_onGround ? null : word;

    /// <summary>A lever's new TLA: "All thrust levers" now when both sit at its detent, else null, its own sentence held
    /// for <see cref="Flush"/>. <paramref name="suppressed"/>: the delivery is inside MainForm's echo window.</summary>
    public (string Row, string Text)? Thrust(int lever, double tla, long now, bool suppressed = false, bool onGround = true)
    {
        _onGround = onGround;
        int i = lever - 1;
        bool baseline = double.IsNaN(_tla[0]) || double.IsNaN(_tla[1]);
        _tla[i] = tla;
        string? detent = Said(tla);
        if (baseline)
        {
            _lastDetent[i] = detent;
            if (!double.IsNaN(_tla[0]) && !double.IsNaN(_tla[1]))
                _lastAll = Said(_tla[0]) is { } d0 && d0 == Said(_tla[1]) ? d0 : null;
            return null;
        }
        if (detent == null)
            return null;   // mid-travel: a held sentence waits for where the lever stops
        if (Said(_tla[1 - i]) == detent)
        {
            _held[0] = _held[1] = null;
            _lastDetent[0] = _lastDetent[1] = detent;
            if (detent == _lastAll)
                return null;
            _lastAll = detent;
            return (AllLevers, $"All thrust levers {detent}");
        }
        _held[i] = detent == _lastDetent[i] ? null : (detent, now, suppressed);
        return null;
    }

    /// <summary>The lone levers' sentences whose gather is over, each under its lever's row; a lever that moved on since
    /// says nothing (where it stops holds its own).</summary>
    public IReadOnlyList<(string Row, string Text)> Flush(long now)
    {
        List<(string Row, string Text)>? said = null;
        for (int i = 0; i < 2; i++)
        {
            if (_held[i] is not { } held || now - held.At < GatherMs)
                continue;
            _held[i] = null;
            if (Said(_tla[i]) != held.Detent)
                continue;
            _lastDetent[i] = held.Detent;
            _lastAll = null;
            if (!held.Suppressed)
                (said ??= new()).Add((i == 0 ? Lever1 : Lever2, $"Thrust lever {i + 1} {held.Detent}"));
        }
        return said ?? (IReadOnlyList<(string Row, string Text)>)Array.Empty<(string Row, string Text)>();
    }

    /// <summary>The speed brake handle (0 to 1) in tenths: "Spoilers retracted", "Spoilers 40 percent",
    /// "Spoilers full"; null while the tenth is unchanged.</summary>
    public string? SpeedBrake(double handle)
    {
        int band = (int)Math.Round(Math.Clamp(handle, 0, 1) * 10, MidpointRounding.AwayFromZero);
        if (_lastBand < 0 || band == _lastBand)
        {
            _lastBand = band;
            return null;
        }
        _lastBand = band;
        return band == 0 ? "Spoilers retracted"
            : band == 10 ? "Spoilers full"
            : $"Spoilers {(band * 10).ToString(CultureInfo.InvariantCulture)} percent";
    }

    /// <summary>A lever's baseline from the cache where no delivery has set one (a flight load re-delivers only what it
    /// changed); silent.</summary>
    public void SeedThrust(int lever, double tla)
    {
        if (double.IsNaN(_tla[lever - 1]))
            Thrust(lever, tla, 0);
    }

    /// <summary>The speed brake's baseline from the cache where none is set; silent.</summary>
    public void SeedSpeedBrake(double handle)
    {
        if (_lastBand < 0)
            SpeedBrake(handle);
    }

    /// <summary>A disconnect or a flight load: the next values are a fresh baseline.</summary>
    public void Reset()
    {
        _tla[0] = _tla[1] = double.NaN;
        _lastDetent[0] = _lastDetent[1] = null;
        _held[0] = _held[1] = null;
        _lastAll = null;
        _lastBand = -1;
    }
}
