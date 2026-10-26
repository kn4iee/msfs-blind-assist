using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>The ILS control panel's two knobs: the whole megahertz (with PARK) and the fraction.</summary>
public enum Bbs757IlsKnob { Whole, Fraction }

/// <summary>
/// The ILS control panel as the app counts it. <paramref name="Whole"/> is the whole-megahertz knob's place
/// on its ring of five: 0 to 3 are 108 to 111, <see cref="Park"/> (4) is PARK. <paramref name="Fraction"/> is
/// the fraction knob's place on its ring of ten ILS channels, 0 to 9 for .10 to .95
/// (<see cref="FractionHundredths"/>); PARK keeps it (measured: the panel remembers the fraction while parked).
/// </summary>
public readonly record struct Bbs757IlsDial(int Whole, int Fraction)
{
    /// <summary>The whole knob's PARK, between 111 and 108 on its ring.</summary>
    public const int Park = 4;

    /// <summary>The fraction knob's ten channels in hundredths of a megahertz, in the order +1 turns them (measured).</summary>
    public static IReadOnlyList<int> FractionHundredths { get; } = new[] { 10, 15, 30, 35, 50, 55, 70, 75, 90, 95 };

    public int Whole { get; } = Whole is >= 0 and <= Park
        ? Whole
        : throw new ArgumentOutOfRangeException(nameof(Whole), Whole, "The whole knob has 108 to 111 (0 to 3) and PARK (4).");

    public int Fraction { get; } = Fraction >= 0 && Fraction < 10
        ? Fraction
        : throw new ArgumentOutOfRangeException(nameof(Fraction), Fraction, "The fraction knob has ten channels, .10 to .95 (0 to 9).");

    /// <summary>
    /// The panel as a flight loads it: PARK, remembering .10 (the owner's flight, in its starting panel
    /// state; the first step proved the fraction: PARK -1 gave 111.10). Other start types (on a runway, in
    /// the air, a saved flight) are unmeasured.
    /// </summary>
    public static Bbs757IlsDial LoadState { get; } = new(Park, 0);

    public bool IsParked => Whole == Park;

    /// <summary>The frequency this dial stands for, 108.10 to 111.95; null when parked.</summary>
    public double? Mhz => IsParked ? null : (10800 + 100 * Whole + FractionHundredths[Fraction]) / 100.0;
}

/// <summary>
/// The 757's ILS control panel (aft pedestal), which the FMC does not tune and no instrument view shows:
/// the knob model, the clicks that reach a frequency or PARK, and the words. Pure; BBS757Definition.Radios.cs
/// keeps the count and turns the knobs.
///
/// Measured on the ground (docs/b757/evidence/flight-04.md, sections C and F):
///   • No variable holds the frequency. The knobs are step requests, <c>L:B757_ILS_FREQ_WHOLE_INC</c> and
///     <c>L:B757_ILS_FREQ_FRAC_INC</c>, written +1 or -1 and set back to 0 by BlueBird within a frame.
///   • The whole knob is a ring of five, 108, 109, 110, 111, PARK, then 108 again (+1), the reverse for -1,
///     with no end stop. It keeps the fraction.
///   • The fraction knob steps through the ILS channels only, .10 .15 .30 .35 .50 .55 .70 .75 .90 .95 (+1
///     goes right), .95 to .10 with no carry into the megahertz. While parked it does nothing, and PARK
///     remembers the fraction.
///   • A write of 2 moves one channel: one write is one click. Two writes landing in one frame count once.
/// So the app cannot read the panel back, nor confirm a click: it counts the clicks it sends from a state it
/// knows (<see cref="Bbs757IlsDial.LoadState"/>) and its words never claim more than that count.
/// </summary>
public static class Bbs757IlsPanel
{
    public const string WholeVar = "B757_ILS_FREQ_WHOLE_INC", FractionVar = "B757_ILS_FREQ_FRAC_INC";

    /// <summary>What an empty ILS frequency box says.</summary>
    public const string EmptyMessage = "Type a frequency first.";

    /// <summary>What a typed frequency that is not an ILS channel says.</summary>
    public const string InvalidMessage = "ILS frequency must be 108.10 to 111.95, on an ILS channel.";

    /// <summary>A set refused because no calculator write can land.</summary>
    public const string UnavailablePhrase = "ILS unavailable";

    /// <summary>A set refused because the app does not know the panel's state (it has not seen the flight load).</summary>
    public const string UnknownRefusal = "ILS tuning unknown: use ILS sync first.";

    /// <summary>A set refused because another is still turning the knobs.</summary>
    public const string InProgressPhrase = "ILS tuning in progress";

    private const int WholePlaces = 5;
    private const int FractionPlaces = 10;

    /// <summary>
    /// A typed ILS frequency, read on any Windows culture (<see cref="NumberParsing.TryParseTyped"/>): digits
    /// and one "." or "," only, 108.10 to 111.95 on an ILS channel ("110.5" is 110.50, "110,55" 110.55). An
    /// empty box says <see cref="EmptyMessage"/>; anything else refused says <see cref="InvalidMessage"/>.
    /// </summary>
    public static bool TryParseIls(string? text, out double mhz, out string error)
    {
        mhz = 0;
        error = "";
        string typed = (text ?? "").Trim();
        if (typed.Length == 0)
        {
            error = EmptyMessage;
            return false;
        }
        if (!typed.All(c => char.IsAsciiDigit(c) || c is '.' or ',')
            || !NumberParsing.TryParseTyped(typed, out double value)
            || !TryDialFor(value, out var dial))
        {
            error = InvalidMessage;
            return false;
        }
        mhz = dial.Mhz!.Value;
        return true;
    }

    /// <summary>
    /// The panel showing <paramref name="mhz"/>: true only for an ILS channel, 108.10 to 111.95, a whole
    /// number of hundredths ending in one of <see cref="Bbs757IlsDial.FractionHundredths"/>.
    /// </summary>
    public static bool TryDialFor(double mhz, out Bbs757IlsDial dial)
    {
        dial = default;
        if (!double.IsFinite(mhz)) return false;
        double scaled = mhz * 100.0;
        double hundredths = Math.Round(scaled);
        if (Math.Abs(scaled - hundredths) > 1e-6 || hundredths < 10800 || hundredths >= 11200) return false;

        int h = (int)hundredths;
        int fraction = IndexOf(Bbs757IlsDial.FractionHundredths, h % 100);
        if (fraction < 0) return false;
        dial = new Bbs757IlsDial(h / 100 - 108, fraction);
        return true;
    }

    /// <summary>
    /// One click of a knob, by the measured model: the whole knob moves round its ring of five through PARK
    /// and keeps the fraction; the fraction knob moves round its ring of ten with no carry, and does nothing
    /// while parked. <paramref name="direction"/> is +1 or -1 (a write of 2 still moves one channel).
    /// </summary>
    public static Bbs757IlsDial Step(Bbs757IlsDial dial, Bbs757IlsKnob knob, int direction)
    {
        int d = Click(direction);
        return knob switch
        {
            Bbs757IlsKnob.Whole => new Bbs757IlsDial(Ring(dial.Whole + d, WholePlaces), dial.Fraction),
            Bbs757IlsKnob.Fraction => dial.IsParked ? dial : new Bbs757IlsDial(dial.Whole, Ring(dial.Fraction + d, FractionPlaces)),
            _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "The ILS panel has a whole and a fraction knob."),
        };
    }

    /// <summary>
    /// The clicks, one per write, from <paramref name="from"/> to <paramref name="to"/> (null, or a parked
    /// dial: PARK, whatever fraction it names). The fewest by the model, with one rule: the ILS is parked only
    /// where the plan starts or ends, never on the way.
    ///   • To PARK: the whole knob only, the shorter way round its ring of five (never a tie); PARK keeps the
    ///     fraction. Parked already: no click.
    ///   • Out of PARK: the whole knob first, the shorter way (the fraction knob does nothing until then),
    ///     then the fraction the shorter way round its ring of ten.
    ///   • Between two frequencies: the fraction first, the shorter way, then the whole knob straight to the
    ///     megahertz, never through PARK. The way round through PARK is never shorter by more than one click
    ///     (108 to 111: two against three), and it would park the ILS for a moment, which the manual warns can
    ///     raise an RW/ILS alert while approach guidance is selected (Complete Flight Guide, p. 192).
    /// A tie round the fraction ring (five clicks either way) goes up (+1).
    /// </summary>
    public static IReadOnlyList<(Bbs757IlsKnob Knob, int Direction)> PlanSteps(Bbs757IlsDial from, Bbs757IlsDial? to)
    {
        var plan = new List<(Bbs757IlsKnob Knob, int Direction)>();
        if (to is not { IsParked: false } target)
        {
            if (!from.IsParked) Add(plan, Bbs757IlsKnob.Whole, Shorter(from.Whole, Bbs757IlsDial.Park, WholePlaces));
            return plan;
        }

        var fraction = Shorter(from.Fraction, target.Fraction, FractionPlaces);
        if (from.IsParked)
        {
            Add(plan, Bbs757IlsKnob.Whole, Shorter(Bbs757IlsDial.Park, target.Whole, WholePlaces));
            Add(plan, Bbs757IlsKnob.Fraction, fraction);
        }
        else
        {
            Add(plan, Bbs757IlsKnob.Fraction, fraction);
            Add(plan, Bbs757IlsKnob.Whole, (Math.Sign(target.Whole - from.Whole), Math.Abs(target.Whole - from.Whole)));
        }
        return plan;
    }

    /// <summary>The step-request variable a knob's click is written to.</summary>
    public static string Variable(Bbs757IlsKnob knob) => knob switch
    {
        Bbs757IlsKnob.Whole => WholeVar,
        Bbs757IlsKnob.Fraction => FractionVar,
        _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "The ILS panel has a whole and a fraction knob."),
    };

    /// <summary>
    /// One click as the calculator write that makes it: "-1 (>L:B757_ILS_FREQ_WHOLE_INC)". No culture in it
    /// (sv-SE would write -1 with a U+2212 minus). Send it through <c>ExecuteCalculatorCodeUnique</c>: a click
    /// repeats the same string, and MobiFlight drops an identical repeat.
    /// </summary>
    public static string StepCode(Bbs757IlsKnob knob, int direction)
        => $"{Click(direction).ToString(CultureInfo.InvariantCulture)} (>L:{Variable(knob)})";

    /// <summary>A frequency as spoken: two decimals and a dot whatever the PC's culture ("110.50").</summary>
    public static string Frequency(double mhz) => mhz.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The panel as counted: "ILS 110.50", "ILS parked", or "ILS tuning unknown" when the app has no count.
    /// What the count says, never more: nothing reads the panel back.
    /// </summary>
    public static string Phrase(Bbs757IlsDial? dial)
    {
        if (dial is not Bbs757IlsDial d) return "ILS tuning unknown";
        return d.Mhz is double mhz ? $"ILS {Frequency(mhz)}" : "ILS parked";
    }

    /// <summary>A set that sent its clicks: "ILS 110.50 set", "ILS parked".</summary>
    public static string SetPhrase(Bbs757IlsDial dial) => dial.IsParked ? Phrase(dial) : $"{Phrase(dial)} set";

    /// <summary>A set that needed no click: "ILS already 110.50", "ILS already parked".</summary>
    public static string AlreadyPhrase(Bbs757IlsDial dial)
        => dial.Mhz is double mhz ? $"ILS already {Frequency(mhz)}" : "ILS already parked";

    // A click is one step either way.
    private static int Click(int direction)
        => direction is 1 or -1
            ? direction
            : throw new ArgumentOutOfRangeException(nameof(direction), direction, "A knob click is +1 or -1: one write is one click.");

    private static int Ring(int place, int places) => ((place % places) + places) % places;

    // The shorter way round a ring from one place to another: up on a tie.
    private static (int Direction, int Clicks) Shorter(int from, int to, int places)
    {
        int up = Ring(to - from, places);
        return up <= places - up ? (+1, up) : (-1, places - up);
    }

    private static void Add(List<(Bbs757IlsKnob Knob, int Direction)> plan, Bbs757IlsKnob knob, (int Direction, int Clicks) turn)
    {
        for (int i = 0; i < turn.Clicks; i++) plan.Add((knob, turn.Direction));
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }
}
