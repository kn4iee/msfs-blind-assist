using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The E/WD as read from the CTR draw. Fixtures: a live cold-and-dark power-up at VCBI (2026-10-27):
// the SD's safety test, the FWS boot list, and APU EMER SHUTDOWN with its done action (white), the cursor
// on CLEAR (the cyan wide frame) with CLEAR's own action box (cyan, to do).
public class A350EwdTests
{
    private static A350DrawCapture Fixture(string name) =>
        A350Fixtures.Capture(name + ".json");

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
            new DrawnText("F/CTL PRIM 1 FAULT", 856, 400, 34, 400, 1, amber),
            new DrawnText("PRIM 1 .........OFF THEN ON", 886, 430, 64, 430, 1, cyan),
            new DrawnText("? PRIM RESET SUCCESSFUL ?", 886, 460, 64, 460, 1, white),
            new DrawnText("YES", 905, 490, 83, 490, 1, white),
            new DrawnText("NO", 1253, 490, 431, 490, 1, white),
            new DrawnText("FUEL L TK MAIN PMP FAULT", 856, 520, 34, 520, 1, amber),
        };
        var frames = new[] { new DrawnFrame(885, 472, 15, 20, false, white), new DrawnFrame(1233, 472, 15, 20, false, white) };
        var lines = A350Ewd.Parse(new A350DrawCapture("CTR", texts, frames, Array.Empty<DrawnElement>(), null, 1)).Warning;
        Assert.Equal(new[]
        {
            "F/CTL PRIM 1 FAULT, amber", "PRIM 1: OFF THEN ON, cyan", "? PRIM RESET SUCCESSFUL ?, white", "YES or NO, white",
            "FUEL L TK MAIN PMP FAULT, amber",
        }, lines.Select(l => l.Spoken));
        Assert.Equal(EcamBox.None, lines[3].Box);
    }

    // Live 2026-10-28 in the hold, failures cleared: the warning display's LIMITATIONS block (the heading, ALL PHASES
    // left and APPR & LDG right, FLAPS SLOW in cyan under APPR & LDG), then the memos, then STS at the bottom. The
    // headings straddle the memo column, so it read "ALL PHASES, STS, LIMITATIONS, APPR & LDG, FLAPS SLOW": no telling
    // which phase a limitation is for. The block reads in place, each limitation with its column.
    [Fact]
    public void The_limitations_read_each_under_its_phase_then_the_memos()
    {
        var lines = A350Ewd.Parse(Fixture("ctr_sd_status_inop_14")).Warning.Select(l => l.Spoken);
        Assert.Equal(new[]
        {
            "LIMITATIONS, white", "APPR & LDG: FLAPS SLOW, cyan", "WXR/PWS OFF, amber", "SEAT BELTS, green", "STS, white",
        }, lines);
    }

    // Review 2026-10-28 (minor 8): the headings' row was consumed whole, so another text on it was never read; a cyan
    // row under the block with the ECAM cursor or an action box (a procedure's action, never a limitation) was taken as
    // a limitation; and only an item's first text lost its dot leader.
    [Fact]
    public void The_limitations_block_takes_only_its_own()
    {
        var cap = Fixture("ctr_sd_status_inop_14");
        var cyan = new A350Rgba(0, 1, 1, 1);
        var amber = new A350Rgba(1, 0.6f, 0, 1);
        var texts = cap.Texts.Select(t => t.Text == "FLAPS SLOW" ? t with { Text = "FLAPS" } : t).ToList();
        texts.Add(new DrawnText("....SLOW", 1340, 385.1f, 504, 385.1f, 1, cyan));        // the item's action, its leader
        texts.Add(new DrawnText("APU AVAIL", 1253, 343.2f, 417, 343.2f, 1, amber));     // a memo on the headings' row
        texts.Add(new DrawnText("R TK MAIN PMP..OFF", 856, 430, 20, 430, 1, cyan));     // a procedure's action, boxed
        var frames = cap.Frames.Append(new DrawnFrame(830, 412, 15, 20, false, cyan)).ToList();
        var lines = A350Ewd.Parse(cap with { Texts = texts, Frames = frames }).Warning;
        var spoken = lines.Select(l => l.Spoken).ToList();
        Assert.Contains("APPR & LDG: FLAPS: SLOW, cyan", spoken);
        Assert.Contains("APU AVAIL, amber", spoken);
        var action = Assert.Single(lines, l => l.Spoken.Contains("R TK MAIN PMP", StringComparison.Ordinal));
        Assert.Equal("R TK MAIN PMP: OFF, cyan", action.Spoken);
        Assert.Equal(EcamBox.ToDo, action.Box);
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
