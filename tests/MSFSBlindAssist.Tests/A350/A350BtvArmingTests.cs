using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Aircraft.A350.A350BtvArming;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 08 Task 2: BTV armed from the ND window in one step, by the aircraft's own path measured on 1.2.8 at VCBI (2026-11-03):
/// PLAN at the 2 NM zoom, SET LDG RWY on the runway's ident, the exit's distance into the LANDING tab's BTV EXIT AT, then A/BRK pressed
/// after the exit (pressed before, it arms BRK MED and stays), and the pilot's EFIS mode and range put back.</summary>
public class A350BtvArmingTests
{
    private static A350DrawCapture Cap(string fixture) => A350Fixtures.Capture(fixture + ".json");

    [Fact]
    public void The_exits_of_the_landing_runway_are_listed()
    {
        var read = ExitsOf(Cap("p08_efis_anf_exits"), 0);
        Assert.Equal("04", read.Runway);
        Assert.Equal(new[] { ("E", 220), ("D", 900), ("C", 1420), ("B", 2430), ("A", 3120), ("END", 3140) },
            read.Exits.Select(e => (e.Name, e.Metres)));
        Assert.Equal("Runway 04 exits: E 220 meters, D 900, C 1420, B 2430, A 3120, END 3140.", ExitsSentence(read));
    }

    [Fact]
    public void A_runway_with_no_exit_says_so()
    {
        var read = ExitsOf(Cap("efis0_climb_bi614"), 0);   // no airport map drawn
        Assert.Empty(read.Exits);
        Assert.Equal("The airport map shows no exits for the landing runway.", ExitsSentence(read));
    }

    [Fact]
    public void Arming_an_exit_takes_the_aircrafts_own_steps() =>
        Assert.Equal(new Step[]
        {
            new SetEfis(PlanMode, ZoomRange), new SetLandingRunway("04"), new OpenLandingTab(), new TypeExit(2430), new CloseFunctions(),
            new PressAutobrake(1), new SetEfis(3, 6),
        }, ArmSteps("04", new Exit("B", 2430), mode: 3, range: 6, autobrake: null));

    // Pressed before the exit, A/BRK arms BRK MED and an exit set after leaves it there; off and on again with the exit set arms BTV.
    [Theory]
    [InlineData(null, 1)]
    [InlineData("BRK MED", 2)]
    [InlineData("BRK LO", 2)]
    [InlineData("BTV", 2)]
    public void Autobrake_is_pressed_after_the_exit_as_often_as_it_takes(string? autobrake, int presses)
    {
        var steps = ArmSteps("04", new Exit("B", 2430), 3, 6, autobrake);
        Assert.Equal(new PressAutobrake(presses), steps.OfType<PressAutobrake>().Single());
        Assert.True(steps.ToList().IndexOf(new PressAutobrake(presses)) > steps.ToList().IndexOf(new TypeExit(2430)));
    }

    [Fact]
    public void The_pilots_mode_and_range_are_put_back()
    {
        Assert.Equal(new SetEfis(2, 7), ArmSteps("04", new Exit("A", 3120), 2, 7, null)[^1]);
        // Already on PLAN at the zoom: nothing to set or put back.
        Assert.DoesNotContain(ArmSteps("04", new Exit("A", 3120), PlanMode, ZoomRange, null), s => s is SetEfis);
        Assert.Equal("BTV armed, exit B, 2430 meters. ND back to Arc, 20 NM.", ArmedSentence(new Exit("B", 2430), 3, 6));
        Assert.Equal("BTV armed, exit B, 2430 meters.", ArmedSentence(new Exit("B", 2430), PlanMode, ZoomRange));
    }

    // The exit field cleared in flight (CLR, ENTER) but not on the stand, and the manual names no way: armed, A/BRK pressed once
    // takes BTV off, the one sure clear; not armed, only the field.
    [Fact]
    public void Clear_btv_clears_it()
    {
        Assert.Equal(new Step[] { new SetEfis(PlanMode, ZoomRange), new OpenLandingTab(), new ClearExit(), new CloseFunctions(), new SetEfis(3, 6) },
            ClearSteps(3, 6, autobrake: null));
        Assert.Equal(new Step[] { new SetEfis(PlanMode, ZoomRange), new OpenLandingTab(), new ClearExit(), new CloseFunctions(), new PressAutobrake(1), new SetEfis(3, 6) },
            ClearSteps(3, 6, autobrake: "BTV"));
        Assert.DoesNotContain(ClearSteps(3, 6, autobrake: "BRK MED"), s => s is PressAutobrake);
    }

    [Fact]
    public void Arming_a_runway_sets_it_on_the_map_alone() =>
        Assert.Equal(new Step[] { new SetEfis(PlanMode, ZoomRange), new SetLandingRunway("22"), new SetEfis(3, 6) }, RunwaySteps("22", 3, 6));

    // What the aircraft shows once armed: the FMA's autobrake word, and the ND's BTV block.
    [Theory]
    [InlineData("  FMA autothrust: SPEED, green; BTV, cyan", "BTV")]
    [InlineData("  FMA autothrust: SPEED, green; BRK MED, cyan", "BRK MED")]
    [InlineData("  FMA autothrust: THR IDLE, green; BTV, green", "BTV")]
    [InlineData("  FMA autothrust: SPEED, green", null)]
    public void The_autobrake_is_read_from_the_fma(string fma, string? word) =>
        Assert.Equal(word, AutobrakeOf(new[] { "  Speed: 250 knots", fma }));

    [Fact]
    public void The_exit_set_is_read_from_the_nd()
    {
        Assert.Equal(2430, ExitSetOf(Cap("p08_efis_anf_btv_set_map"), 0));
        Assert.Null(ExitSetOf(Cap("p08_efis_anf_exits"), 0));
    }
}
