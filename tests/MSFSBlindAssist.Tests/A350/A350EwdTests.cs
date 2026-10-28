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

    // Live after the second engine start, before the T.O CONFIG test (2026-10-28): the T.O memo in the main
    // column (x 856-886) and two memos in the right column (x 1253) drawn level with its first lines. Each column
    // reads on its own, the main one first; the memo's leaders (".....", "..") and a done item's " : " read as ": ".
    [Fact]
    public void The_to_memo_reads_before_the_memos_beside_it()
    {
        var ewd = A350Ewd.Parse(Fixture("ctr_to_memo"));
        Assert.Equal(new[]
        {
            "T.O, white", "SIGNS: ON, cyan", "CABIN: READY, cyan", "GND SPLRs: ARM, cyan", "FLAPS: TO, green",
            "AUTO BRK: RTO, cyan", "TO CONFIG: TEST, cyan", "ADS-B TRAFFIC ON, green", "COMPANY MSG, green",
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

    // Live 2026-10-28, F/CTL PRIM 1 FAULT in cruise: the question "? PRIM RESET SUCCESSFUL ?" then YES and NO on one row,
    // NO at x 1253 (inside the memo column's range), each beside a small white marker. NO read after every other line, and
    // YES as "done". A choice reads as one line after its question, and its markers are no action boxes.
    [Fact]
    public void A_procedure_choice_reads_as_one_line_after_its_question()
    {
        var white = new A350Rgba(1, 1, 1, 1);
        var cyan = new A350Rgba(0, 1, 1, 1);
        var amber = new A350Rgba(1, 0.6f, 0, 1);
        var texts = new[]
        {
            new DrawnText("F/CTL PRIM 1 FAULT", 856, 100, 34, 100, 1, amber),
            new DrawnText("PRIM 1 .........OFF THEN ON", 886, 130, 64, 130, 1, cyan),
            new DrawnText("? PRIM RESET SUCCESSFUL ?", 886, 160, 64, 160, 1, white),
            new DrawnText("YES", 905, 190, 83, 190, 1, white),
            new DrawnText("NO", 1253, 190, 431, 190, 1, white),
            new DrawnText("FUEL L TK MAIN PMP FAULT", 856, 220, 34, 220, 1, amber),
        };
        var frames = new[] { new DrawnFrame(885, 172, 15, 20, false, white), new DrawnFrame(1233, 172, 15, 20, false, white) };
        var lines = A350Ewd.Parse(new A350DrawCapture("CTR", texts, frames, Array.Empty<DrawnElement>(), null, 1)).Warning;
        Assert.Equal(new[]
        {
            "F/CTL PRIM 1 FAULT, amber", "PRIM 1: OFF THEN ON, cyan", "? PRIM RESET SUCCESSFUL ?, white", "YES or NO, white",
            "FUEL L TK MAIN PMP FAULT, amber",
        }, lines.Select(l => l.Spoken));
        Assert.Equal(EcamBox.None, lines[3].Box);
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
