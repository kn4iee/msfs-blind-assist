using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

public class A350TcasCalloutsTests
{
    private static string Key(string lvar) => A350TcasCallouts.Advisories.Single(a => a.Var == lvar).Key;

    [Fact]
    public void Each_aural_has_its_words()
    {
        // The module's sound.xml binds each aural to its L:var (tcas_climb_climb to INI_TCAS_CLIMB, and so on).
        var words = A350TcasCallouts.Advisories.ToDictionary(a => a.Var, a => a.Said);
        Assert.Equal("Traffic", words["INI_TCAS_TRAFFIC"]);
        Assert.Equal("Climb", words["INI_TCAS_CLIMB"]);
        Assert.Equal("Climb, crossing climb", words["INI_TCAS_CLIMB_CROSS"]);
        Assert.Equal("Climb, climb now", words["INI_TCAS_CLIMB_NOW"]);
        Assert.Equal("Increase climb", words["INI_TCAS_CLIMB_MORE"]);
        Assert.Equal("Descend", words["INI_TCAS_DESCENT"]);
        Assert.Equal("Descend, crossing descend", words["INI_TCAS_DESCENT_CROSS"]);
        Assert.Equal("Descend, descend now", words["INI_TCAS_DESCENT_NOW"]);
        Assert.Equal("Increase descent", words["INI_TCAS_DESCENT_MORE"]);
        Assert.Equal("Level off", words["INI_TCAS_LEVEL_OFF"]);
        Assert.Equal("Adjust vertical speed", words["INI_TCAS_ADJUST_VS"]);
        Assert.Equal("Maintain vertical speed", words["INI_TCAS_MAINTAIN_VS"]);
        Assert.Equal("Maintain vertical speed, crossing maintain", words["INI_TCAS_MAINTAIN_VS_CROSS"]);
        Assert.Equal("Monitor vertical speed", words["INI_TCAS_MONITOR_VS"]);
        Assert.Equal("Clear of conflict", words["INI_TCAS_CLEAR"]);
        Assert.Equal(15, words.Count);
    }

    [Fact]
    public void An_advisory_is_said_as_its_aural_starts_with_the_rate_to_fly_after_a_moment()
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 1, nowMs: 0);
        Assert.Null(tcas.Flush(100, target: 1500));   // the guidance can lag the sound ([A380-18])
        Assert.Equal("TCAS: Climb. Fly vertical speed plus 1500 feet per minute.", tcas.Flush(A350TcasCallouts.GuidanceSettleMs, target: 1500));
        Assert.Null(tcas.Flush(A350TcasCallouts.GuidanceSettleMs + 1000, target: 1500));
    }

    [Fact]
    public void A_descent_says_its_rate_with_its_sign()
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key("INI_TCAS_DESCENT"), 1, nowMs: 0);
        Assert.Equal("TCAS: Descend. Fly vertical speed minus 1500 feet per minute.", tcas.Flush(1000, target: -1500));
    }

    [Theory]
    [InlineData("INI_TCAS_TRAFFIC", "TCAS: Traffic.")]
    [InlineData("INI_TCAS_CLEAR", "TCAS: Clear of conflict.")]
    [InlineData("INI_TCAS_MONITOR_VS", "TCAS: Monitor vertical speed.")]
    public void What_commands_no_rate_says_none(string lvar, string said)
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key(lvar), 1, nowMs: 0);
        Assert.Equal(said, tcas.Flush(1000, target: 1500));
    }

    [Fact]
    public void Without_a_target_the_advisory_still_speaks()
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 1, nowMs: 0);
        Assert.Equal("TCAS: Climb.", tcas.Flush(1000, target: 0));
    }

    [Fact]
    public void A_held_aural_flag_says_once_and_a_new_rise_says_again()
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 1, nowMs: 0);
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 1, nowMs: 100);
        Assert.NotNull(tcas.Flush(1000, target: 1500));
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 1, nowMs: 1200);
        Assert.Null(tcas.Flush(3000, target: 1500));
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 0, nowMs: 3100);
        tcas.Deliver(Key("INI_TCAS_CLIMB"), 1, nowMs: 3200);
        Assert.NotNull(tcas.Flush(5000, target: 1500));
    }

    [Fact]
    public void Two_advisories_in_one_moment_are_one_sentence_in_order()
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key("INI_TCAS_CLEAR"), 1, nowMs: 0);
        tcas.Deliver(Key("INI_TCAS_TRAFFIC"), 1, nowMs: 50);
        Assert.Equal("TCAS: Clear of conflict. Traffic.", tcas.Flush(1000, target: 0));
    }

    [Fact]
    public void What_is_up_at_a_reset_is_a_baseline()
    {
        var tcas = new A350TcasCallouts();
        tcas.Deliver(Key("INI_TCAS_TRAFFIC"), 1, nowMs: 0);
        tcas.Reset(seen: key => key == Key("INI_TCAS_TRAFFIC") ? 1 : 0);
        Assert.Null(tcas.Flush(1000, target: 0));
        tcas.Deliver(Key("INI_TCAS_TRAFFIC"), 1, nowMs: 1100);
        Assert.Null(tcas.Flush(3000, target: 0));
    }

    [Fact]
    public void Every_advisory_is_read_with_one_ctrl_m_row()
    {
        var def = new IniBuildsA350Definition();
        var vars = def.GetVariables();
        foreach (var a in A350TcasCallouts.Advisories)
        {
            Assert.Equal(a.Var, vars[a.Key].Name);
            Assert.Equal(UpdateFrequency.Continuous, vars[a.Key].UpdateFrequency);
            Assert.True(vars[a.Key].ExcludeFromMonitorManager);
        }
        Assert.Equal(A350TcasCallouts.TargetVar, vars[A350TcasCallouts.TargetKey].Name);
        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(vars);
        Assert.Contains(rows, r => r.Key == A350TcasCallouts.MuteKey && r.Label == A350TcasCallouts.MuteLabel);
    }
}
