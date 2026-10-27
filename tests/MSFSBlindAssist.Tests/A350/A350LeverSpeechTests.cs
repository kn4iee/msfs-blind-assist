using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The levers speak as the A380's do: a thrust lever's detent once it reaches one ("All thrust levers Climb"
/// when both sit there), the speed brake handle in tenths; a silent baseline first, nothing mid-travel.</summary>
public class A350LeverSpeechTests
{
    [Theory]
    [InlineData(0, "Idle")]
    [InlineData(0.8, "Idle")]
    [InlineData(25, "Climb")]
    [InlineData(35, "Flex M C T")]
    [InlineData(45, "TOGA")]
    [InlineData(-6, "Reverse idle")]
    [InlineData(-20, "Maximum reverse")]
    [InlineData(12, null)]
    public void A_tla_names_its_detent_or_nothing_mid_travel(double tla, string? word) =>
        Assert.Equal(word, A350LeverSpeech.Detent(tla));

    [Fact]
    public void Both_levers_at_one_detent_read_as_all_and_a_split_reads_the_lever_that_moved()
    {
        var speech = new A350LeverSpeech();
        Assert.Null(speech.Thrust(1, 0));
        Assert.Null(speech.Thrust(2, 0));                                   // the baseline: silent
        Assert.Equal((A350LeverSpeech.Lever1, "Thrust lever 1 Climb"), speech.Thrust(1, 25));
        Assert.Equal((A350LeverSpeech.AllLevers, "All thrust levers Climb"), speech.Thrust(2, 25));
        Assert.Null(speech.Thrust(1, 30));                                  // mid-travel
        Assert.Null(speech.Thrust(1, 25));                                  // back where it was: nothing new
        Assert.Equal((A350LeverSpeech.Lever2, "Thrust lever 2 TOGA"), speech.Thrust(2, 45));
    }

    [Fact]
    public void A_reset_takes_a_fresh_silent_baseline()
    {
        var speech = new A350LeverSpeech();
        speech.Thrust(1, 0);
        speech.Thrust(2, 0);
        speech.Reset();
        Assert.Null(speech.Thrust(1, 25));
        Assert.Null(speech.Thrust(2, 25));
    }

    [Fact]
    public void The_speed_brake_handle_reads_in_tenths()
    {
        var speech = new A350LeverSpeech();
        Assert.Null(speech.SpeedBrake(0));                                  // the baseline
        Assert.Equal("Spoilers 40 percent", speech.SpeedBrake(0.366));
        Assert.Null(speech.SpeedBrake(0.37));                               // the same tenth
        Assert.Equal("Spoilers full", speech.SpeedBrake(1));
        Assert.Equal("Spoilers retracted", speech.SpeedBrake(0));
    }

    [Fact]
    public void A_seed_baselines_only_what_is_unseen_and_the_next_move_speaks()
    {
        var speech = new A350LeverSpeech();
        speech.SeedThrust(1, 25);
        speech.SeedThrust(2, 25);
        speech.SeedSpeedBrake(0);
        speech.SeedThrust(1, 45);                                           // already seen: a seed never overwrites
        Assert.Equal((A350LeverSpeech.Lever1, "Thrust lever 1 Idle"), speech.Thrust(1, 0));
        Assert.Equal("Spoilers 50 percent", speech.SpeedBrake(0.5));
    }
}
