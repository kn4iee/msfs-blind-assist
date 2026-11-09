// The 757 lower EICAS's gate (R10): each engine's HP spool within 0.1 of the simulator's N2, and the page value equal to
// L:BBS_ECAM_Mode truncated, each field matching any reading of the last 3 s. Pure logic.

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757LowerEicasGateTests
{
    internal static Bbs757LowerEicasReading R(int page = 1, double left = 77.0, double right = 77.5, bool corrupt = false,
        string[]? messages = null, bool powered = true)
    {
        string? word = !powered ? null : page switch { 1 => "engine", 2 => "STATUS", 3 => "engine off", _ => "blank" };
        return new Bbs757LowerEicasReading(page, word, Array.Empty<string>(), messages ?? Array.Empty<string>(), left, right,
            powered, corrupt);
    }

    internal static Bbs757SimValues Sim(double n1 = 77.0, double n2 = 77.5, double mode = 1) =>
        new(new Dictionary<string, double>
        {
            ["BBS757_LOWER_EICAS_N2_1"] = n1,
            ["BBS757_LOWER_EICAS_N2_2"] = n2,
            ["BBS_ECAM_Mode"] = mode,
        });

    private static bool Passes(Bbs757SimValues sim, params (long AtMs, Bbs757LowerEicasReading R)[] history) =>
        Bbs757LowerEicasGate.Passes(history, sim);

    [Fact]
    public void Passes_when_n2_matches_within_3_s()
    {
        Assert.True(Passes(Sim(), (0, R())));
        Assert.True(Passes(Sim(n1: 77.09), (0, R())));
        Assert.False(Passes(Sim(n1: 77.3), (0, R())));
        Assert.False(Passes(Sim(n2: 70), (0, R())));
        // The simulator lags memory: an older reading within 3 s of the newest still matches.
        Assert.True(Passes(Sim(n1: 60), (0, R(left: 60)), (2000, R(left: 77))));
        // ... but not one older than 3 s.
        Assert.False(Passes(Sim(n1: 60), (0, R(left: 60)), (3500, R(left: 77))));
    }

    [Fact]
    public void Passes_only_when_the_page_matches_the_variable()
    {
        Assert.True(Passes(Sim(mode: 2), (0, R(page: 2))));
        Assert.False(Passes(Sim(mode: 1), (0, R(page: 2))));
        // BlueBird's L:var reader truncates toward zero.
        Assert.True(Passes(Sim(mode: 2.9), (0, R(page: 2))));
        Assert.True(Passes(Sim(mode: 0.9), (0, R(page: 0))));
    }

    [Fact]
    public void Each_gate_field_may_match_a_different_sample_within_3_s()
    {
        // Left matches the old sample, the page the new one.
        var history = new[] { (0L, R(page: 1, left: 60, right: 77.5)), (1000L, R(page: 2, left: 77, right: 77.5)) };
        Assert.True(Bbs757LowerEicasGate.Passes(history, Sim(n1: 60, mode: 2)));
    }

    [Fact]
    public void A_corrupt_reading_never_passes()
    {
        Assert.False(Passes(Sim(), (0, R(corrupt: true))));
        // A good older reading still counts: the corrupt one is skipped, not a veto.
        Assert.True(Passes(Sim(), (0, R()), (500, R(corrupt: true))));
    }

    [Fact]
    public void A_missing_or_non_finite_simulator_value_fails()
    {
        var noMode = new Bbs757SimValues(new Dictionary<string, double> { ["BBS757_LOWER_EICAS_N2_1"] = 77, ["BBS757_LOWER_EICAS_N2_2"] = 77.5 });
        Assert.False(Passes(noMode, (0, R())));
        Assert.False(Passes(Sim(mode: double.NaN), (0, R())));
        Assert.False(Passes(default, (0, R())));
        Assert.False(Bbs757LowerEicasGate.Passes(Array.Empty<(long, Bbs757LowerEicasReading)>(), Sim()));
    }

    [Fact]
    public void The_gate_variables_are_two_stock_n2_variables_and_one_l_var()
    {
        var vars = Bbs757LowerEicasGate.Variables;
        Assert.Equal(new[] { "BBS757_LOWER_EICAS_N2_1", "BBS757_LOWER_EICAS_N2_2", "BBS_ECAM_Mode" }, vars.Select(v => v.Key));
        Assert.Equal(new[] { "TURB ENG N2:1", "TURB ENG N2:2", "BBS_ECAM_Mode" }, vars.Select(v => v.Name));
        Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.SimVar, vars[0].Type);
        Assert.Equal("percent", vars[0].Units);
        Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.LVar, vars[2].Type);
        Assert.Equal("BBS_ECAM_Mode", vars[2].Name);
    }
}
