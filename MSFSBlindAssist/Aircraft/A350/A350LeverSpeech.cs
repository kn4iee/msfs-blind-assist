using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// What the A350's levers say as they move, in the A380's words (<c>FlyByWireA380Definition</c>'s TLA detents and
/// speed brake bands): a thrust lever speaks its detent once it reaches one, "All thrust levers Climb" while both sit at
/// it, "Thrust lever 2 TOGA" when they are split, nothing mid-travel; the speed brake handle in tenths. The first
/// values are a silent baseline. Each sentence names the Ctrl+M row it belongs to. Pure.
/// </summary>
public sealed class A350LeverSpeech
{
    public const string Lever1 = "A350_ENGINE_THROTTLE_1";
    public const string Lever2 = "A350_ENGINE_THROTTLE_2";
    public const string AllLevers = "A350_THRUST_LEVERS_ALL";

    private readonly double[] _tla = { double.NaN, double.NaN };
    private readonly string?[] _lastDetent = new string?[2];
    private string? _lastAll;
    private int _lastBand = -1;

    /// <summary>The detent a TLA sits at (the A350's: reverse -20 to 0, reverse idle -6, CLB 25, FLX/MCT 35, TOGA 45),
    /// or null between detents. "Flex M C T" so a speech engine spells it.</summary>
    public static string? Detent(double tla) =>
        Math.Abs(tla) < 1.5 ? "Idle" :
        Math.Abs(tla - 25) < 2.5 ? "Climb" :
        Math.Abs(tla - 35) < 2.5 ? "Flex M C T" :
        Math.Abs(tla - 45) < 2.5 ? "TOGA" :
        Math.Abs(tla + 6) < 2.5 ? "Reverse idle" :
        tla <= -17.5 ? "Maximum reverse" : null;

    /// <summary>A lever's new TLA: the row that speaks and its sentence, or null.</summary>
    public (string Row, string Text)? Thrust(int lever, double tla)
    {
        int i = lever - 1;
        bool baseline = double.IsNaN(_tla[0]) || double.IsNaN(_tla[1]);
        _tla[i] = tla;
        string? detent = Detent(tla);
        if (baseline)
        {
            _lastDetent[i] = detent;
            if (!double.IsNaN(_tla[0]) && !double.IsNaN(_tla[1]))
                _lastAll = Detent(_tla[0]) is { } d0 && d0 == Detent(_tla[1]) ? d0 : null;
            return null;
        }
        if (detent == null)
            return null;   // mid-travel
        if (Detent(_tla[1 - i]) == detent)
        {
            _lastDetent[0] = _lastDetent[1] = detent;
            if (detent == _lastAll)
                return null;
            _lastAll = detent;
            return (AllLevers, $"All thrust levers {detent}");
        }
        if (detent == _lastDetent[i])
            return null;
        _lastDetent[i] = detent;
        _lastAll = null;
        return (i == 0 ? Lever1 : Lever2, $"Thrust lever {lever} {detent}");
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
            Thrust(lever, tla);
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
        _lastAll = null;
        _lastBand = -1;
    }
}
