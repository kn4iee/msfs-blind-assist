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
        flashes.Apply(Read("Speed: 259 knots", "QNH: 1013, cyan", "Heading: 303"), At(2));
        flashes.Apply(Read("Speed: 259 knots", "Heading: 303"), At(3));
        Assert.Equal(new[] { "Speed: 260 knots", "QNH: 1013, cyan, flashing", "Heading: 303" },
            flashes.Apply(Read("Speed: 260 knots", "QNH: 1013, cyan", "Heading: 303"), At(4)));
        Assert.Equal(new[] { "Speed: 260 knots", "QNH: 1013, cyan, flashing", "Heading: 303" },
            flashes.Apply(Read("Speed: 260 knots", "Heading: 303"), At(5)));
    }

    /// <summary>The line out and back twice: it flashes from then on.</summary>
    private static void Flash(A350FlashingLines flashes, A350Pfd.PfdRead with, A350Pfd.PfdRead without)
    {
        flashes.Apply(with, At(0));
        flashes.Apply(without, At(1));
        flashes.Apply(with, At(2));
        flashes.Apply(without, At(3));
    }

    // Review 3 (M2): once out and back is a torn read, or a column blank through a mode change: it read "flashing" for 4 s.
    [Fact]
    public void One_gap_is_no_flash()
    {
        var flashes = new A350FlashingLines();
        flashes.Apply(Read("QNH: 1013, cyan"), At(0));
        flashes.Apply(Read(), At(1));
        Assert.Equal(new[] { "QNH: 1013, cyan" }, flashes.Apply(Read("QNH: 1013, cyan"), At(2)));
        Assert.Empty(flashes.Apply(Read(), At(3)));
    }

    // ...and after STD, the old QNH was held "flashing" beside "Baro: STD". The baro is one line, whatever its label says.
    [Fact]
    public void Pushing_std_ends_the_qnhs_flash()
    {
        var flashes = new A350FlashingLines();
        Flash(flashes, Read("QNH: 1013, cyan"), Read());
        flashes.Apply(Read("QNH: 1013, cyan"), At(4));
        Assert.Equal(new[] { "Baro: STD, cyan in a yellow box" }, flashes.Apply(Read("Baro: STD, cyan in a yellow box"), At(5)));
    }

    // Live 2026-10-30, a go-around: LVR CLB flashes under MAN TOGA, which stays. The column never leaves, so half the reads lost the call.
    [Fact]
    public void A_part_of_an_fma_column_flashes_as_a_line_does()
    {
        const string whole = "FMA autothrust: MAN; TOGA; LVR CLB, white", part = "FMA autothrust: MAN; TOGA, white";
        var flashes = new A350FlashingLines();
        Flash(flashes, Read(whole), Read(part));
        Assert.Equal(new[] { whole + ", flashing" }, flashes.Apply(Read(whole), At(4)));
        Assert.Equal(new[] { whole + ", flashing" }, flashes.Apply(Read(part), At(5)));
    }

    // It stops: the part left reads plain once the window has passed, and the next mode at once.
    [Fact]
    public void A_part_gone_for_good_reads_plain_after_the_window()
    {
        const string whole = "FMA autothrust: MAN; TOGA; LVR CLB, white", part = "FMA autothrust: MAN; TOGA, white";
        var flashes = new A350FlashingLines();
        Flash(flashes, Read(whole), Read(part));
        flashes.Apply(Read(whole), At(4));
        Assert.Equal(new[] { part }, flashes.Apply(Read(part), At(4 + A350FlashingLines.Window.TotalSeconds + 0.5)));
    }

    [Fact]
    public void The_next_mode_ends_a_parts_flash()
    {
        const string whole = "FMA autothrust: MAN; TOGA; LVR CLB, white", part = "FMA autothrust: MAN; TOGA, white", speed = "FMA autothrust: SPEED, green";
        var flashes = new A350FlashingLines();
        Flash(flashes, Read(whole), Read(part));
        flashes.Apply(Read(whole), At(4));
        Assert.Equal(new[] { speed }, flashes.Apply(Read(speed), At(5)));
    }

    // A part that goes once and stays gone says so at once: nothing to hold.
    [Fact]
    public void A_part_that_simply_goes_reads_at_once()
    {
        const string both = "FMA vertical: SRS, green; ALT, cyan", one = "FMA vertical: SRS, green";
        var flashes = new A350FlashingLines();
        flashes.Apply(Read(both), At(0));
        Assert.Equal(new[] { one }, flashes.Apply(Read(one), At(1)));
        Assert.Equal(new[] { one }, flashes.Apply(Read(one), At(2)));
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
        Flash(flashes, Read("QNH: 1013, cyan"), Read());
        Assert.Equal(new[] { "QNH: 1013, cyan, flashing" }, flashes.Apply(Read("QNH: 1013, cyan"), At(4)));
        Assert.Equal(new[] { "QNH: 1013, cyan, flashing" }, flashes.Apply(Read("QNH: 1013, cyan"), At(5)));
        Assert.Equal(new[] { "QNH: 1013, cyan" }, flashes.Apply(Read("QNH: 1013, cyan"), At(4 + A350FlashingLines.Window.TotalSeconds + 0.5)));
    }

    [Fact]
    public void A_flashing_line_gone_for_good_leaves_after_the_window()
    {
        var flashes = new A350FlashingLines();
        Flash(flashes, Read("QNH: 1013, cyan"), Read());
        flashes.Apply(Read("QNH: 1013, cyan"), At(4));
        Assert.Equal(new[] { "QNH: 1013, cyan, flashing" }, flashes.Apply(Read(), At(5)));
        Assert.Empty(flashes.Apply(Read(), At(4 + A350FlashingLines.Window.TotalSeconds + 0.5)));
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
        Flash(flashes, Read("QNH: 1013, cyan"), Read());
        Assert.Equal(new[] { "QNH: 1014, cyan, flashing" }, flashes.Apply(Read("QNH: 1014, cyan"), At(4)));
        Assert.Equal(new[] { "QNH: 1014, cyan, flashing" }, flashes.Apply(Read(), At(5)));
    }

    // An FMA column ("LVR CLB" flashes at the thrust reduction altitude) and a flag of the rest flash the same way.
    [Fact]
    public void An_fma_column_and_a_flag_flash_too()
    {
        var flashes = new A350FlashingLines();
        var withBoth = new A350Pfd.PfdRead(new[] { "FMA autothrust: LVR CLB, white", "Speed: 200 knots", "ATT, red" }, 2);
        var without = new A350Pfd.PfdRead(new[] { "Speed: 200 knots" }, 1);
        Flash(flashes, withBoth, without);
        Assert.Equal(new[] { "FMA autothrust: LVR CLB, white, flashing", "Speed: 200 knots", "ATT, red, flashing" }, flashes.Apply(withBoth, At(4)));
        Assert.Equal(new[] { "FMA autothrust: LVR CLB, white, flashing", "Speed: 200 knots", "ATT, red, flashing" }, flashes.Apply(without, At(5)));
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
