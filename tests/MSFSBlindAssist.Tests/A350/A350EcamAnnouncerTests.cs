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

    [Fact]
    public void Reset_makes_the_next_read_a_baseline()
    {
        var a = new A350EcamAnnouncer();
        Twice(a, View());
        a.Reset();
        Assert.Empty(Twice(a, View("PARK BRK|green")));
    }
}
