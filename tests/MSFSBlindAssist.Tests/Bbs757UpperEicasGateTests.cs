// The 757 upper EICAS's gate (S3, as measured at both engine starts): memory is trusted only while each engine's N1 as
// BlueBird keeps it is within 2.5 % N1 of the simulator's TURB ENG N1:1 and :2. The simulator may lag memory (and its
// N1 spikes for about a second at light-off), so each engine must match at least one reading of the last 3 s; the two
// engines may match different readings. The display's power is the reading's own. Pure logic.

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757UpperEicasGateTests
{
    private static Bbs757UpperEicasReading R(double left, double right, bool corrupt = false) =>
        Bbs757UpperEicasSpeechTests.Eicas() with { GateN1Left = left, GateN1Right = right, Corrupt = corrupt };

    private static Bbs757SimValues Sim(double? left = 19.93, double? right = 19.93, double? manual = 0)
    {
        var values = new Dictionary<string, double>();
        if (left is double l) values["TURB ENG N1:1"] = l;
        if (right is double r) values["TURB ENG N1:2"] = r;
        if (manual is double m) values["BBS757_THRREF_PULL"] = m;
        return new Bbs757SimValues(values);
    }

    private static bool Passes(Bbs757SimValues sim, params (long AtMs, Bbs757UpperEicasReading Reading)[] history) =>
        Bbs757UpperEicasGate.Passes(history, sim);

    [Fact]
    public void The_gate_compares_both_engines_n1_within_2_5_percent_over_3_s()
    {
        Assert.Equal("TURB ENG N1:1", Bbs757UpperEicasGate.N1LeftKey);
        Assert.Equal("TURB ENG N1:2", Bbs757UpperEicasGate.N1RightKey);
        Assert.Equal(2.5, Bbs757UpperEicasGate.N1Tolerance);
        Assert.Equal(3000, Bbs757UpperEicasGate.WindowMs);
    }

    [Fact]
    public void Passes_when_n1_matches_within_3_s()
    {
        // Steady idle: memory is the simulator's N1.
        Assert.True(Passes(Sim(), (0, R(19.93, 19.93))));
        Assert.True(Passes(Sim(), (0, R(19.93, 19.93)), (1000, R(19.93, 19.93)), (3000, R(19.93, 19.93))));
        // Within 2.5 % N1 either way, and no further.
        Assert.True(Passes(Sim(left: 22.5, right: 17.5), (0, R(20, 20))));
        Assert.False(Passes(Sim(left: 22.625), (0, R(20, 19.93))));
        Assert.False(Passes(Sim(right: 17.375), (0, R(19.93, 20))));

        // While N1 rises memory leads the simulator: the simulator's value is one memory held a moment ago.
        Assert.True(Passes(Sim(left: 24.0, right: 24.0), (0, R(24.1, 24.1)), (250, R(26.0, 26.0)), (500, R(28.0, 28.0))));

        // The window rule, with light-off's numbers: memory holds 26.3 % while the simulator's right N1 reads 33.2 %. A
        // tick passes only while the window still holds a reading within 2.5 % of the simulator's, here a made-up earlier
        // 31.0 %. At a real light-off memory never comes that close, so those ticks fail here and the monitor's 2 s grace
        // carries them (Bbs757UpperEicasDisplayTests.An_engine_start_light_off_says_no_stopped).
        var before = (0L, R(19.93, 31.0));
        var held = new[] { (250L, R(19.93, 26.3)), (500L, R(19.93, 26.3)), (750L, R(19.93, 26.3)), (1000L, R(19.93, 26.3)), (1250L, R(19.93, 26.3)) };
        var jump = Sim(right: 33.2);
        for (int i = 1; i <= held.Length; i++)
            Assert.True(Passes(jump, held.Take(i).Prepend(before).ToArray()), $"{i} readings at 26.3");
        // Without it, 6.9 % apart, it fails.
        Assert.False(Passes(jump, held));
        // A reading that matched counts until it is more than 3 s older than the newest.
        Assert.True(Passes(jump, before, (3000, R(19.93, 26.3))));
        Assert.False(Passes(jump, before, (3001, R(19.93, 26.3))));
    }

    [Fact]
    public void Each_engine_may_match_a_different_reading()
    {
        // The left engine matches the older reading, the right the newer: no one reading matches both, yet it passes.
        var sim = Sim(left: 30.0, right: 40.0);
        Assert.True(Passes(sim, (1000, R(30.0, 20.0)), (2000, R(20.0, 40.0))));
        Assert.False(Passes(sim, (1000, R(30.0, 20.0))));
        Assert.False(Passes(sim, (2000, R(20.0, 40.0))));
        // Either engine never matching fails it.
        Assert.False(Passes(Sim(left: 50.0), (0, R(19.93, 19.93)), (1000, R(19.93, 19.93))));
        Assert.False(Passes(Sim(right: 50.0), (0, R(19.93, 19.93)), (1000, R(19.93, 19.93))));
    }

    [Fact]
    public void Nothing_to_compare_fails()
    {
        // No reading.
        Assert.False(Bbs757UpperEicasGate.Passes(Array.Empty<(long, Bbs757UpperEicasReading)>(), Sim()));
        // An N1 the simulator did not give, or one that is not a number.
        Assert.False(Passes(Sim(left: null), (0, R(19.93, 19.93))));
        Assert.False(Passes(Sim(right: null), (0, R(19.93, 19.93))));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.False(Passes(Sim(left: bad), (0, R(19.93, 19.93))));
            Assert.False(Passes(Sim(right: bad), (0, R(19.93, 19.93))));
        }
        // A corrupt reading never matches (the monitor never keeps one; the gate does not trust one either).
        Assert.False(Passes(Sim(), (0, R(19.93, 19.93, corrupt: true))));
        Assert.True(Passes(Sim(), (0, R(19.93, 19.93)), (250, R(19.93, 19.93, corrupt: true))));
        // A memory N1 that is not a number never matches.
        Assert.False(Passes(Sim(), (0, R(double.NaN, 19.93))));
    }

    [Fact]
    public void The_manual_thrust_flag_is_not_compared()
    {
        // The manual key is read for the thrust reference (S10), not the gate: missing or set, the gate is the N1's.
        Assert.True(Passes(Sim(manual: null), (0, R(19.93, 19.93))));
        Assert.True(Passes(Sim(manual: 1), (0, R(19.93, 19.93))));
        Assert.True(Passes(Sim(manual: double.NaN), (0, R(19.93, 19.93))));
    }

    [Fact]
    public void Unpowered_is_not_lit()
    {
        // The display's power is the reading's (Task 4: powered, not failed, not timed blank, no computer blank), never
        // "no power state": a dark screen is never compared, so its power-up is never spoken (S4).
        IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> display = new Bbs757UpperEicasDisplay();
        Assert.False(display.Lit(Bbs757UpperEicasSpeechTests.Eicas(powered: false), Sim()));
        Assert.True(display.Lit(Bbs757UpperEicasSpeechTests.Eicas(powered: true), Sim()));
        // Whatever the simulator gives.
        Assert.False(display.Lit(Bbs757UpperEicasSpeechTests.Eicas(powered: false), Sim(left: null, right: null, manual: null)));
        Assert.True(display.Lit(Bbs757UpperEicasSpeechTests.Eicas(powered: true), new Bbs757SimValues(new Dictionary<string, double>())));
        // The gate itself compares N1 whatever the power: memory carries it either way.
        Assert.True(Passes(Sim(), (0, Bbs757UpperEicasSpeechTests.Eicas(powered: false))));
    }
}
