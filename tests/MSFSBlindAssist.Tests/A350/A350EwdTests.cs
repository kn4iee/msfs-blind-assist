using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The E/WD as read from the CTR draw. Fixtures: a live cold-and-dark power-up at VCBI (2026-10-27):
// the SD's safety test, the FWS boot list, and APU EMER SHUTDOWN with its done action (white), the cursor
// on CLEAR (the cyan wide frame) with CLEAR's own action box (cyan, to do).
public class A350EwdTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void Warning_lines_read_in_order_with_their_colours_without_dot_rows()
    {
        var ewd = A350Ewd.Parse(Fixture("ctr_apu_emer_shutdown"));
        Assert.Equal(new[]
        {
            "APU EMER SHUTDOWN, amber", "APU MASTER SW OFF, white", "CLEAR, cyan",
            "ELEC BAT EMER 2 OFF, amber", "ELEC BAT EMER 1 OFF, amber", "SURV TCAS STBY, amber",
        }, ewd.Warning.Select(l => l.Spoken));
    }

    [Fact]
    public void The_cursor_and_the_action_box_belong_to_their_line()
    {
        var ewd = A350Ewd.Parse(Fixture("ctr_apu_emer_shutdown"));
        var clear = Assert.Single(ewd.Warning, l => l.Selected);
        Assert.Equal("CLEAR", clear.Text);
        Assert.Equal(EcamBox.ToDo, clear.Box);
        Assert.All(ewd.Warning.Where(l => l.Text != "CLEAR"), l => Assert.Equal(EcamBox.None, l.Box));
    }

    [Fact]
    public void The_fws_boot_list_reads_whole()
    {
        var ewd = A350Ewd.Parse(Fixture("ctr_powerup_fws_fault"));
        Assert.Equal(12, ewd.Warning.Count);
        Assert.Equal("FWS FWS 1+2 & CPIOM FAULT, amber", ewd.Warning[0].Spoken);
        Assert.Contains(ewd.Warning, l => l.Spoken == "FUNCTIONS NOT AVAIL :, white");
        Assert.Contains(ewd.Warning, l => l.Spoken == "ALT ALERT & AUTO CALLOUT / NORM C/L & MEMOS, amber");
        Assert.DoesNotContain(ewd.Warning, l => l.Selected);
    }

    [Fact]
    public void The_sd_and_the_permanent_data_are_apart_from_the_warning_display()
    {
        var ewd = A350Ewd.Parse(Fixture("ctr_powerup_safety_test"));
        Assert.True(ewd.Powered);
        Assert.Empty(ewd.Warning);
        Assert.Contains(ewd.SystemDisplay, r => A350DrawLayout.Speak(r) == "SAFETY TEST IN PROGRESS, green");
        var apu = A350Ewd.Parse(Fixture("ctr_apu_emer_shutdown"));
        Assert.Contains(apu.PermanentData, r => r.Any(c => c.Text == "FLT"));
        Assert.DoesNotContain(apu.Warning, l => l.Text.Contains("FLT"));
        Assert.Contains(apu.Engine, r => A350DrawLayout.Speak(r) == "THR XX THR XX, amber");
    }

    [Fact]
    public void An_unpowered_ctr_is_not_powered()
    {
        var ewd = A350Ewd.Parse(new A350DrawCapture("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1));
        Assert.False(ewd.Powered);
        Assert.Empty(ewd.Warning);
    }

    // Live: "WXR ..............................SYS 2" and "APU MASTER SW ......................OFF" came out
    // with their dot leaders, which a screen reader reads as "dot" thirty times. A leader reads as a colon.
    [Theory]
    [InlineData("WXR ..............................SYS 2", "WXR: SYS 2, cyan")]
    [InlineData("APU MASTER SW ......................OFF", "APU MASTER SW: OFF, cyan")]
    [InlineData("ECAM SD & OVHD PNL...............MONITOR", "ECAM SD & OVHD PNL: MONITOR, cyan")]
    [InlineData("FUNCTIONS NOT AVAIL :", "FUNCTIONS NOT AVAIL :, cyan")]
    [InlineData("STS ...", "STS, cyan")]
    public void A_dot_leader_reads_as_a_colon(string drawn, string spoken)
    {
        var cap = new A350DrawCapture("CTR", new[] { new DrawnText(drawn, 905, 313, 69, 313, 1, new A350Rgba(0, 1, 1, 1)) },
            Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);
        Assert.Equal(spoken, Assert.Single(A350Ewd.Parse(cap).Warning).Spoken);
    }
}
