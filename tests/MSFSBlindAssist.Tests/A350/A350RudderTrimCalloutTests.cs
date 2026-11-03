using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 11 Task 4 (the owner's backlog, 2026-10-27): the rudder trim is said as it moves, as the pitch trim is (the base
/// class's "Trim up 1.4"): in tenths of a degree as the SD shows it, with the pitch trim's hysteresis, the first read silent,
/// in the status line's words ("0.5 degrees left").</summary>
public class A350RudderTrimCalloutTests
{
    [Fact]
    public void The_first_read_is_silent()
    {
        Assert.Null(new A350RudderTrimCallout().Observe(-1.2));
    }

    [Fact]
    public void A_step_of_a_tenth_is_said_with_its_side()
    {
        var c = new A350RudderTrimCallout();
        c.Observe(0);
        Assert.Equal("Rudder trim 0.5 degrees left", c.Observe(-0.5));
        Assert.Equal("Rudder trim 0.3 degrees right", c.Observe(0.3));
        Assert.Equal("Rudder trim neutral", c.Observe(0.01));
    }

    [Fact]
    public void Jitter_at_a_step_boundary_is_not_said()
    {
        var c = new A350RudderTrimCallout();
        c.Observe(-0.5);
        Assert.Null(c.Observe(-0.5));
        Assert.Null(c.Observe(-0.54));   // rounds to 0.5
        Assert.Null(c.Observe(-0.56));   // rounds to 0.6, but within the hysteresis of 0.55
        Assert.Equal("Rudder trim 0.6 degrees left", c.Observe(-0.59));
    }

    [Fact]
    public void Reset_makes_the_next_read_silent_again()
    {
        var c = new A350RudderTrimCallout();
        c.Observe(0);
        c.Reset();
        Assert.Null(c.Observe(-2.0));
    }

    [Fact]
    public void The_words_hold_in_a_comma_decimal_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var c = new A350RudderTrimCallout();
            c.Observe(0);
            Assert.Equal("Rudder trim 1.5 degrees right", c.Observe(1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
