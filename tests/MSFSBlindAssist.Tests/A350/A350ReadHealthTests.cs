using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-28 every draw trapped on a torn copy for many minutes and the displays simply went quiet: the FMA
// call-outs, the ECAM, the MFD window. A few failed draws in a row are normal; when a display's draws have failed for
// ten seconds the pilot is told once which display it is, and told again once it reads steadily.
public class A350ReadHealthTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);
    private const string PfdGone = "A350 captain PFD and ND cannot be read at the moment";

    /// <summary>A quick draw (it ends as it starts) of <paramref name="gauge"/> at <paramref name="s"/> seconds.</summary>
    private static string? Draw(A350ReadHealth health, string gauge, int index, bool ok, int s) =>
        health.Report(gauge, index, ok, T0.AddSeconds(s), T0.AddSeconds(s));

    /// <summary>A quick draw of the captain's EFIS at <paramref name="s"/> seconds.</summary>
    private static string? At(A350ReadHealth health, bool ok, int s) => Draw(health, "EFIS", 0, ok, s);

    /// <summary>The captain's EFIS failing every second from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static void Fail(A350ReadHealth health, int from, int to)
    {
        for (int s = from; s < to; s++)
            At(health, false, s);
    }

    [Fact]
    public void A_few_failed_draws_say_nothing()
    {
        var health = new A350ReadHealth();
        Assert.Null(At(health, false, 0));
        Assert.Null(At(health, false, 5));
    }

    [Fact]
    public void Draws_failing_for_ten_seconds_are_said_once()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 10);
        Assert.Equal(PfdGone, At(health, false, 10));
        Assert.Null(At(health, false, 11));
    }

    // Review 2026-10-28: one good draw among failures said "readable again", and the next failures "cannot be read"
    // again, over and over. A display reads again once three of its draws in a row are good.
    [Fact]
    public void A_display_reads_again_after_three_good_draws_in_a_row()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 11);
        Assert.Null(At(health, true, 11));
        Assert.Null(At(health, false, 12));   // a lone good draw: still unreadable, nothing said
        Assert.Null(At(health, true, 13));
        Assert.Null(At(health, true, 14));
        Assert.Equal("A350 captain PFD and ND readable again", At(health, true, 15));
        Assert.Null(At(health, true, 16));
    }

    [Fact]
    public void A_good_draw_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 5);
        Assert.Null(At(health, true, 5));
        Fail(health, 6, 16);
        Assert.Equal(PfdGone, At(health, false, 16));
    }

    // Review 2026-10-28: a pause in the draws (no A350 found while a flight loads, no window asking) kept the first
    // failure's time, so the first torn draw after it said "cannot be read" at once. A pause starts the window again.
    [Fact]
    public void A_pause_in_the_draws_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        At(health, false, 0);
        Assert.Null(At(health, false, 60));
        Fail(health, 61, 70);
        Assert.Equal(PfdGone, At(health, false, 70));
    }

    // Live 2026-10-28, the A350's memory at 495 MB: one draw took 5 to 9 seconds and every one failed for a minute,
    // yet nothing was said, because each gap between two draws' ends was longer than the pause. A pause is time with
    // no draw running: from one draw's end to the next one's start.
    [Fact]
    public void Slow_draws_back_to_back_are_not_a_pause()
    {
        var health = new A350ReadHealth();
        Assert.Null(health.Report("EFIS", 0, false, T0, T0.AddSeconds(8)));
        Assert.Equal(PfdGone, health.Report("EFIS", 0, false, T0.AddSeconds(8), T0.AddSeconds(16)));
    }

    // Live 2026-10-28 the E/WD read on some draws while the PFD, ND and MFD never did. With one health for every
    // display, each E/WD read started the ten seconds again, so nothing was said while the PFD was gone.
    [Fact]
    public void A_display_that_reads_does_not_hide_one_that_cannot()
    {
        var health = new A350ReadHealth();
        for (int s = 0; s < 10; s++)
        {
            Assert.Null(At(health, false, s));
            Assert.Null(Draw(health, "CTR", 0, true, s));
        }
        Assert.Equal(PfdGone, At(health, false, 10));
    }

    // The PFD monitor draws the other displays in turn, so one of them may come round only every half minute while
    // the reader never stops drawing: that is no pause.
    [Fact]
    public void A_display_drawn_rarely_while_others_are_drawn_is_not_paused()
    {
        var health = new A350ReadHealth();
        Assert.Null(Draw(health, "RMP", 1, false, 0));
        for (int s = 1; s < 30; s++)
            Draw(health, "EFIS", 0, true, s);
        Assert.Equal("A350 RMP 2 display cannot be read at the moment", Draw(health, "RMP", 1, false, 30));
    }

    [Theory]
    [InlineData("CTR", 0, "E/WD and SD")]
    [InlineData("EFIS", 0, "captain PFD and ND")]
    [InlineData("EFIS", 1, "first officer PFD and ND")]
    [InlineData("MFD", 0, "MFDs")]
    [InlineData("FCU", 0, "FCU display")]
    [InlineData("RMP", 0, "RMP 1 display")]
    [InlineData("RMP", 2, "RMP 3 display")]
    [InlineData("SFD", 0, "standby flight display")]
    [InlineData("SND", 0, "standby navigation display")]
    [InlineData("BAT", 0, "BAT display")]
    public void Each_display_is_named_as_the_pilot_knows_it(string gauge, int index, string name)
    {
        Assert.Equal($"A350 {name} cannot be read at the moment", A350DisplayStatus.Unreadable(gauge, index));
        Assert.Equal($"A350 {name} readable again", A350DisplayStatus.Readable(gauge, index));
    }

    // A display added to the reader without a name would be spoken by its gauge's code name.
    [Fact]
    public void Every_display_drawn_has_a_name()
    {
        var drawn = IniBuildsA350Definition.DisplayPanels.Select(d => (d.Gauge, d.Index))
            .Concat(A350PfdMonitor.Others).Append(("CTR", 0)).Distinct();
        foreach (var (gauge, index) in drawn)
            Assert.NotEqual(gauge, A350DisplayStatus.NameOf(gauge, index));
    }
}
