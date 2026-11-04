using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// BTV armed from the ND window in one step (plan 08 Task 2), by the aircraft's own path, measured on 1.2.8 at VCBI (2026-11-03): the
/// ND in PLAN at the 2 NM zoom draws the airport map (ANF); SET LDG RWY on the runway's ident gives the map's function panel its
/// landing runway, which turns its LANDING tab's BTV EXIT AT field on; the exit's distance from the threshold typed there sets the exit;
/// the A/BRK pushbutton pressed after it arms BTV (pressed before, it arms BRK MED, and an exit set after leaves it there; off and on
/// again arms BTV); then the pilot's EFIS mode and range go back. Pure: the steps, the exits as the ND window reads the map, the words.
/// </summary>
public static class A350BtvArming
{
    /// <summary>The EFIS knobs' positions: PLAN, and the 2 NM zoom step (labelled 1 NM on PLAN).</summary>
    public const int PlanMode = 4, ZoomRange = 3;

    /// <summary>The EFIS mode and range knobs' positions by name, as the ND window lists them.</summary>
    public static readonly IReadOnlyList<string> ModeNames = new[] { "Rose LS", "Rose VOR", "Rose NAV", "Arc", "Plan" };
    public static readonly IReadOnlyList<string> RangeNames = new[]
    {
        "Zoom 0.2 NM", "Zoom 0.5 NM", "Zoom 1 NM", "Zoom 2 NM", "Zoom 5 NM", "10 NM", "20 NM", "40 NM", "80 NM", "160 NM", "320 NM", "640 NM",
    };

    public sealed record Exit(string Name, int Metres);
    public sealed record ExitsRead(string? Runway, IReadOnlyList<Exit> Exits);

    /// <summary>One thing the ND window does to arm or clear, in the aircraft's own controls.</summary>
    public abstract record Step;
    public sealed record SetEfis(int Mode, int Range) : Step;
    public sealed record SetLandingRunway(string Runway) : Step;
    public sealed record OpenLandingTab : Step;
    public sealed record TypeExit(int Metres) : Step;
    public sealed record ClearExit : Step;
    public sealed record CloseFunctions : Step;
    public sealed record PressAutobrake(int Times) : Step;

    private static readonly Regex Landing = new(@"^Landing runway: (\S+),", RegexOptions.Compiled);
    private static readonly Regex OnRunway = new(@"^Taxiway (\S+), [a-z]+: .*on runway (\S+), about (\d+) m from its threshold$", RegexOptions.Compiled);
    private static readonly Regex BtvExit = new(@"^BTV exit: (\d+) M\b", RegexOptions.Compiled);

    /// <summary>The landing runway's exits as the ND window reads a side's zoomed map: each taxiway on it with its distance from the
    /// threshold, nearest first; none while no map or no landing runway is drawn.</summary>
    public static ExitsRead ExitsOf(A350DrawCapture capture, int side)
    {
        var lines = A350NdPage.Read(capture, side).Select(l => l.Spoken).ToList();
        string? runway = lines.Select(l => Landing.Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        var exits = lines.Select(l => OnRunway.Match(l)).Where(m => m.Success && m.Groups[2].Value == runway)
            .Select(m => new Exit(m.Groups[1].Value, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))
            .DistinctBy(e => e.Name).OrderBy(e => e.Metres).ToList();
        return new ExitsRead(runway, exits);
    }

    /// <summary>The exit set, as the ND's BTV block shows it ("BTV exit: 2430 M"), or null.</summary>
    public static int? ExitSetOf(A350DrawCapture capture, int side) =>
        A350NdPage.Read(capture, side).Select(l => BtvExit.Match(l.Spoken)).FirstOrDefault(m => m.Success) is { } m
            ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>The autobrake's word in the PFD's FMA autothrust column ("BTV", "BRK MED"), or null with none armed.</summary>
    public static string? AutobrakeOf(IEnumerable<string> pfdLines) =>
        pfdLines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("FMA autothrust:", StringComparison.Ordinal))?["FMA autothrust:".Length..]
            .Split(';').Select(p => p.Split(',')[0].Trim()).FirstOrDefault(w => w == "BTV" || w.StartsWith("BRK ", StringComparison.Ordinal));

    private static List<Step> Zoomed(int mode, int range, IEnumerable<Step> inner)
    {
        bool move = (mode, range) != (PlanMode, ZoomRange);
        var steps = new List<Step>();
        if (move) steps.Add(new SetEfis(PlanMode, ZoomRange));
        steps.AddRange(inner);
        if (move) steps.Add(new SetEfis(mode, range));
        return steps;
    }

    /// <summary>Arms <paramref name="exit"/> on <paramref name="runway"/>: A/BRK pressed once when off, twice when already armed (on a
    /// level, which an exit set after it leaves; on BTV, so the new exit is taken), always after the exit.</summary>
    public static IReadOnlyList<Step> ArmSteps(string runway, Exit exit, int mode, int range, string? autobrake) =>
        Zoomed(mode, range, new Step[]
        {
            new SetLandingRunway(runway), new OpenLandingTab(), new TypeExit(exit.Metres), new CloseFunctions(), new PressAutobrake(autobrake == null ? 1 : 2),
        });

    /// <summary>Clears the exit (BTV EXIT AT, CLR then ENTER: it cleared in flight, not on the stand, and the manual names no way)
    /// and, with BTV armed, takes it off with one A/BRK press, the one sure clear (2026-11-03); an autobrake level stays the pilot's.</summary>
    public static IReadOnlyList<Step> ClearSteps(int mode, int range, string? autobrake)
    {
        var inner = new List<Step> { new OpenLandingTab(), new ClearExit(), new CloseFunctions() };
        if (autobrake == "BTV") inner.Add(new PressAutobrake(1));
        return Zoomed(mode, range, inner);
    }

    /// <summary>Makes <paramref name="runway"/> the map's landing runway alone.</summary>
    public static IReadOnlyList<Step> RunwaySteps(string runway, int mode, int range) =>
        Zoomed(mode, range, new Step[] { new SetLandingRunway(runway) });

    public static string ExitsSentence(ExitsRead read) =>
        read.Runway == null || read.Exits.Count == 0
            ? "The airport map shows no exits for the landing runway."
            : $"Runway {read.Runway} exits: " + string.Join(", ", read.Exits.Select((e, i) => i == 0 ? $"{e.Name} {e.Metres} meters" : $"{e.Name} {e.Metres}")) + ".";

    /// <summary>The exit chosen from an earlier read, as a fresh read of the map has it now: armed at its distance now, or refused when the
    /// landing runway changed or the exit is gone (review 2026-11-04: a list read at the gate armed the departure's runway on approach).</summary>
    public static (Exit? Exit, string? Refusal) Recheck(Exit chosen, string runway, ExitsRead fresh)
    {
        if (fresh.Runway == null || fresh.Exits.Count == 0) return (null, ExitsSentence(fresh));
        if (fresh.Runway != runway) return (null, $"The landing runway is now {fresh.Runway}, not {runway}: choose an exit again, then Arm exit.");
        return fresh.Exits.FirstOrDefault(e => e.Name == chosen.Name) is { } now
            ? (now, null)
            : (null, $"Exit {chosen.Name} is no longer on runway {runway}: choose an exit again, then Arm exit.");
    }

    /// <summary>Said before a read of the exits turns the side's EFIS knobs to the zoomed Plan, and where they come back to.</summary>
    public static string ReadingSentence(int mode, int range) =>
        (mode, range) == (PlanMode, ZoomRange) || mode < 0 || mode >= ModeNames.Count || range < 0 || range >= RangeNames.Count
            ? "Reading the exits."
            : $"Reading the exits: the ND goes to Plan, zoomed, then back to {ModeNames[mode]}, {RangeNames[range]}.";

    /// <summary>The exit was typed on the KCCU, so the side's MFD was put on its F-PLN page first (<see cref="A350NdInput"/>): said.</summary>
    public static string ArmedSentence(Exit exit, int mode, int range) =>
        $"BTV armed, exit {exit.Name}, {exit.Metres} meters." + Back(mode, range) + MfdOnFlightPlan;

    /// <summary>What the pilot's MFD now shows after a value was typed into the ND.</summary>
    public const string MfdOnFlightPlan = " The MFD shows its F-PLN.";

    /// <summary>" ND back to Arc, 20 NM." when the ND was moved for the arming, else nothing.</summary>
    public static string Back(int mode, int range) =>
        (mode, range) == (PlanMode, ZoomRange) || mode < 0 || mode >= ModeNames.Count || range < 0 || range >= RangeNames.Count
            ? "" : $" ND back to {ModeNames[mode]}, {RangeNames[range]}.";
}
