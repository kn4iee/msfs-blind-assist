using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The levers speak as the A380's do: a thrust lever's detent once it reaches one ("All thrust levers Climb"
/// when both sit there), the speed brake handle in tenths; a silent baseline first, nothing mid-travel. A lever's own
/// sentence waits for the other lever (<see cref="A350LeverSpeech.GatherMs"/>).</summary>
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
    [InlineData(18, null)]
    public void A_tla_names_its_detent_or_nothing_mid_travel(double tla, string? word) =>
        Assert.Equal(word, A350LeverSpeech.Detent(tla));

    // A lever's own sentence waits GatherMs for the other lever (live 2026-10-28: MSFSBA moving both to FLX spoke "Thrust
    // lever 1 Flex M C T", then "All thrust levers Flex M C T", lever 1 arriving a delivery ahead): both at one detent
    // read as all, a split reads the lever that moved once the gather is over.
    [Fact]
    public void Both_levers_at_one_detent_read_as_all_and_a_split_reads_the_lever_that_moved()
    {
        var speech = new A350LeverSpeech();
        Assert.Null(speech.Thrust(1, 0, 0));
        Assert.Null(speech.Thrust(2, 0, 0));                                // the baseline: silent
        Assert.Null(speech.Thrust(1, 25, 100));                             // lever 1 first: the other may be on its way
        Assert.Equal((A350LeverSpeech.AllLevers, "All thrust levers Climb"), speech.Thrust(2, 25, 120));
        Assert.Empty(speech.Flush(1000));                                   // lever 1's lone sentence never comes
        Assert.Null(speech.Thrust(1, 30, 1100));                            // mid-travel
        Assert.Null(speech.Thrust(1, 25, 1200));                            // back where it was: nothing new
        Assert.Empty(speech.Flush(2000));
        Assert.Null(speech.Thrust(2, 45, 3000));                            // a split, held for the gather
        Assert.Empty(speech.Flush(3000 + A350LeverSpeech.GatherMs - 1));
        Assert.Equal(new[] { (A350LeverSpeech.Lever2, "Thrust lever 2 TOGA") }, speech.Flush(3000 + A350LeverSpeech.GatherMs));
        Assert.Empty(speech.Flush(9000));                                   // said once
    }

    // Final review: the take-off's first step is a word on the ground; in the air the lever only passes it, and N1 there is no 50 percent.
    [Fact]
    public void The_takeoff_step_is_no_word_in_the_air()
    {
        var speech = new A350LeverSpeech();
        speech.Thrust(1, 25, 0, onGround: false);
        speech.Thrust(2, 25, 0, onGround: false);
        Assert.Null(speech.Thrust(1, 12, 10, onGround: false));
        Assert.Null(speech.Thrust(2, 12, 10, onGround: false));
        Assert.Empty(speech.Flush(100_000));

        var ground = new A350LeverSpeech();
        ground.Thrust(1, 0, 0);
        ground.Thrust(2, 0, 0);
        ground.Thrust(1, 12, 10);
        Assert.Equal(("All thrust levers 50 percent N1"), ground.Thrust(2, 12, 10)?.Text);
    }

    [Fact]
    public void A_held_lever_that_moves_on_speaks_where_it_stopped()
    {
        var speech = new A350LeverSpeech();
        speech.Thrust(1, 0, 0);
        speech.Thrust(2, 0, 0);
        Assert.Null(speech.Thrust(1, 35, 100));
        Assert.Null(speech.Thrust(1, 40, 200));
        Assert.Null(speech.Thrust(1, 45, 300));
        Assert.Equal(new[] { (A350LeverSpeech.Lever1, "Thrust lever 1 TOGA") }, speech.Flush(300 + A350LeverSpeech.GatherMs));
    }

    // A delivery inside MainForm's echo window (the pilot's own pick, said by the screen reader) never speaks later either.
    [Fact]
    public void A_suppressed_lever_sentence_is_dropped_not_held()
    {
        var speech = new A350LeverSpeech();
        speech.Thrust(1, 0, 0);
        speech.Thrust(2, 0, 0);
        Assert.Null(speech.Thrust(1, 25, 100, suppressed: true));
        Assert.Empty(speech.Flush(5000));
        Assert.Null(speech.Thrust(1, 25, 6000));                            // already where it was said to be
        Assert.Empty(speech.Flush(9000));
    }

    [Fact]
    public void A_reset_takes_a_fresh_silent_baseline()
    {
        var speech = new A350LeverSpeech();
        speech.Thrust(1, 0, 0);
        speech.Thrust(2, 0, 0);
        speech.Reset();
        Assert.Null(speech.Thrust(1, 25, 100));
        Assert.Null(speech.Thrust(2, 25, 100));
        Assert.Empty(speech.Flush(9000));
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
        Assert.Null(speech.Thrust(1, 0, 0));
        Assert.Equal(new[] { (A350LeverSpeech.Lever1, "Thrust lever 1 Idle") }, speech.Flush(A350LeverSpeech.GatherMs));
        Assert.Equal("Spoilers 50 percent", speech.SpeedBrake(0.5));
    }
}
