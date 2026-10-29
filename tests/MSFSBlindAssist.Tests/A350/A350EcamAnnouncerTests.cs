using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// What the E/WD says on its own: each line that appears, with its colour, once two reads agree. The first
// agreed read after start is a baseline (nothing spoken); a cold-and-dark power-up then speaks the whole
// list as it comes up.
public class A350EcamAnnouncerTests
{
    private static A350EwdView View(params string[] lines) => new(
        Array.Empty<IReadOnlyList<DrawnCell>>(),
        lines.Select((l, i) =>
        {
            var parts = l.Split('|');
            return new EcamLine(new[] { new DrawnCell(parts[0], parts[1], 905, 313 + i * 36) }, 313 + i * 36, false, EcamBox.None);
        }).ToList(),
        Array.Empty<IReadOnlyList<DrawnCell>>(), Array.Empty<IReadOnlyList<DrawnCell>>(),
        lines.Length > 0);

    private static IReadOnlyList<string> Twice(A350EcamAnnouncer a, A350EwdView v)
    {
        Assert.Empty(a.Observe(v));   // the first read of a change is never spoken
        return a.Observe(v);
    }

    [Fact]
    public void The_first_agreed_read_is_a_silent_baseline()
    {
        var a = new A350EcamAnnouncer();
        Assert.Empty(Twice(a, View("SURV TCAS STBY|amber")));
    }

    [Fact]
    public void New_lines_speak_in_order_with_their_colours_and_once()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("SURV TCAS STBY|amber"));
        Assert.Equal(new[] { "APU EMER SHUTDOWN, amber", "APU MASTER SW OFF, cyan" },
            Twice(a, View("APU EMER SHUTDOWN|amber", "APU MASTER SW OFF|cyan", "SURV TCAS STBY|amber")));
        Assert.Empty(a.Observe(View("APU EMER SHUTDOWN|amber", "APU MASTER SW OFF|cyan", "SURV TCAS STBY|amber")));
    }

    [Fact]
    public void A_colour_change_is_a_new_line()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("APU MASTER SW OFF|cyan"));
        Assert.Equal(new[] { "APU MASTER SW OFF, white" }, Twice(a, View("APU MASTER SW OFF|white")));
    }

    [Fact]
    public void A_line_that_returns_speaks_again()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("PARK BRK|green"));
        Twice(a, View());
        Assert.Equal(new[] { "PARK BRK, green" }, Twice(a, View("PARK BRK|green")));
    }

    [Fact]
    public void A_cold_and_dark_power_up_speaks_the_whole_list()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View());   // unpowered: the baseline is an empty E/WD
        Assert.Equal(new[] { "FWS FWS 1+2 & CPIOM FAULT, amber", "FUNCTIONS NOT AVAIL :, white" },
            Twice(a, View("FWS FWS 1+2 & CPIOM FAULT|amber", "FUNCTIONS NOT AVAIL :|white")));
    }

    [Fact]
    public void A_one_read_flicker_never_speaks()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("SURV TCAS STBY|amber"));
        Assert.Empty(a.Observe(View("SURV TCAS STBY|amber", "GARBAGE|red")));
        Assert.Empty(a.Observe(View("SURV TCAS STBY|amber")));
    }

    // Review 2026-10-29 (speech 6): the whole list had to agree, so one ticking line (a procedure's countdown) held back every
    // other: with reads a second apart a countdown never agrees, and new warnings went unsaid while it ran. Each line agrees
    // on its own, and a line changing only in its digits is the same line (a countdown), except a warning or a caution
    // (red, amber), whose digits say which engine or system it is.
    [Fact]
    public void A_ticking_line_holds_back_no_other_line()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("ANTI ICE|amber", "WAIT 40 S|cyan"));
        Assert.Empty(a.Observe(View("ANTI ICE|amber", "WAIT 39 S|cyan", "ENG 1 FIRE|red")));
        Assert.Equal(new[] { "ENG 1 FIRE, red" }, a.Observe(View("ANTI ICE|amber", "WAIT 38 S|cyan", "ENG 1 FIRE|red")));
    }

    [Fact]
    public void A_ticking_line_is_not_said_again_as_it_ticks()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("WAIT 40 S|cyan"));
        Assert.Empty(Twice(a, View("WAIT 39 S|cyan")));
        Assert.Empty(Twice(a, View("WAIT 38 S|cyan")));
    }

    [Fact]
    public void A_warning_differing_from_another_only_in_a_digit_is_said()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("ENG 1 FIRE|red"));
        Assert.Equal(new[] { "ENG 2 FIRE, red" }, Twice(a, View("ENG 2 FIRE|red")));   // one replaced by the other
        Assert.Equal(new[] { "ENG 1 FIRE, red" }, Twice(a, View("ENG 1 FIRE|red", "ENG 2 FIRE|red")));
    }

    [Fact]
    public void A_second_line_differing_only_in_a_digit_is_said()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View("THR LEVER 1 IDLE|cyan"));
        Assert.Equal(new[] { "THR LEVER 2 IDLE, cyan" }, Twice(a, View("THR LEVER 1 IDLE|cyan", "THR LEVER 2 IDLE|cyan")));
    }

    [Fact]
    public void Reset_makes_the_next_read_a_baseline()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View());
        a.Reset();
        Assert.Empty(Twice(a, View("PARK BRK|green")));
    }
}
