using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The A350 flashes a PFD line by leaving it out of every other draw (QNH above the transition altitude, live 2026-10-30: a read had
// "QNH: 1013, cyan", the next no baro at all). A status display says such a line as flashing and keeps its place.
public class A350FlashingLinesTests
{
    private static readonly DateTime Start = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static A350Pfd.PfdRead Read(params string[] lines) => new(lines, lines.Length);

    private static DateTime At(double seconds) => Start.AddSeconds(seconds);

    [Fact]
    public void A_line_that_leaves_and_comes_back_flashes_and_stays_in_its_place()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("Speed: 259 knots", "QNH: 1013, cyan", "Heading: 303"), At(0));
        Assert.Equal(new[] { "Speed: 259 knots", "Heading: 303" }, flashes.Apply(Read("Speed: 259 knots", "Heading: 303"), At(1)));
        Assert.Equal(new[] { "Speed: 260 knots", "QNH: 1013, cyan, flashing", "Heading: 303" },
            flashes.Apply(Read("Speed: 260 knots", "QNH: 1013, cyan", "Heading: 303"), At(2)));
        Assert.Equal(new[] { "Speed: 260 knots", "QNH: 1013, cyan, flashing", "Heading: 303" },
            flashes.Apply(Read("Speed: 260 knots", "Heading: 303"), At(3)));
    }

    [Fact]
    public void A_line_that_simply_goes_is_gone_at_once()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("Baro: STD, cyan in a yellow box"), At(0));
        Assert.Empty(flashes.Apply(Read(), At(1)));
        Assert.Empty(flashes.Apply(Read(), At(2)));
    }

    [Fact]
    public void A_line_that_stops_flashing_steady_reads_plain_again()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("QNH: 1013, cyan"), At(0));
        flashes.Apply(Read(), At(1));
        Assert.Equal(new[] { "QNH: 1013, cyan, flashing" }, flashes.Apply(Read("QNH: 1013, cyan"), At(2)));
        Assert.Equal(new[] { "QNH: 1013, cyan, flashing" }, flashes.Apply(Read("QNH: 1013, cyan"), At(3)));
        Assert.Equal(new[] { "QNH: 1013, cyan" }, flashes.Apply(Read("QNH: 1013, cyan"), At(2 + A350FlashingLines.Window.TotalSeconds + 0.5)));
    }

    [Fact]
    public void A_flashing_line_gone_for_good_leaves_after_the_window()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("QNH: 1013, cyan"), At(0));
        flashes.Apply(Read(), At(1));
        flashes.Apply(Read("QNH: 1013, cyan"), At(2));
        Assert.Equal(new[] { "QNH: 1013, cyan, flashing" }, flashes.Apply(Read(), At(3)));
        Assert.Empty(flashes.Apply(Read(), At(2 + A350FlashingLines.Window.TotalSeconds + 0.5)));
    }

    [Fact]
    public void A_line_back_long_after_it_left_is_no_flash()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("QNH: 1013, cyan"), At(0));
        flashes.Apply(Read(), At(1));
        Assert.Equal(new[] { "QNH: 1013, cyan" }, flashes.Apply(Read("QNH: 1013, cyan"), At(1 + A350FlashingLines.Window.TotalSeconds + 0.5)));
    }

    [Fact]
    public void A_flashing_value_reads_its_latest_value()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("QNH: 1013, cyan"), At(0));
        flashes.Apply(Read(), At(1));
        Assert.Equal(new[] { "QNH: 1014, cyan, flashing" }, flashes.Apply(Read("QNH: 1014, cyan"), At(2)));
        Assert.Equal(new[] { "QNH: 1014, cyan, flashing" }, flashes.Apply(Read(), At(3)));
    }

    // An FMA column ("LVR CLB" flashes at the thrust reduction altitude) and a flag of the rest flash the same way.
    [Fact]
    public void An_fma_column_and_a_flag_flash_too()
    {
        var flashes = new A350FlashingLines();
        var withBoth = new A350Pfd.PfdRead(new[] { "FMA autothrust: LVR CLB, white", "Speed: 200 knots", "ATT, red" }, 2);
        var without = new A350Pfd.PfdRead(new[] { "Speed: 200 knots" }, 1);
        flashes.Apply(withBoth, At(0));
        flashes.Apply(without, At(1));
        Assert.Equal(new[] { "FMA autothrust: LVR CLB, white, flashing", "Speed: 200 knots", "ATT, red, flashing" }, flashes.Apply(withBoth, At(2)));
        Assert.Equal(new[] { "FMA autothrust: LVR CLB, white, flashing", "Speed: 200 knots", "ATT, red, flashing" }, flashes.Apply(without, At(3)));
    }

    // The tapes' figures come and go by themselves: the vertical speed beyond its threshold, the radio altitude below 2500 ft.
    [Fact]
    public void A_figure_of_the_tapes_never_flashes()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("Vertical speed: 200 feet per minute up, green"), At(0));
        flashes.Apply(Read(), At(1));
        Assert.Equal(new[] { "Vertical speed: 300 feet per minute up, green" }, flashes.Apply(Read("Vertical speed: 300 feet per minute up, green"), At(2)));
        Assert.Empty(flashes.Apply(Read(), At(3)));
    }
}
