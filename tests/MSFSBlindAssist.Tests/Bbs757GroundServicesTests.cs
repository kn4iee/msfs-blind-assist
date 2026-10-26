using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 11 Task 2: the ground service tiles, their refusals, results, phrases and flow.</summary>
public class Bbs757GroundServicesTests
{
    private static Bbs757GroundService Svc(string key) => Bbs757GroundServices.All.Single(s => s.Key == key);

    [Fact]
    public void Every_tile_names_a_state_variable_and_two_words()
    {
        Assert.All(Bbs757GroundServices.All, s =>
        {
            Assert.Matches("^[A-Za-z0-9_]+$", s.StateVar);
            Assert.NotEmpty(s.OnWord);
            Assert.NotEmpty(s.OffWord);
            Assert.False(Bbs757EfbScreen.UpdateGuard.Contains(s.Tile.X, s.Tile.Y));
        });
    }

    [Theory]
    [InlineData("FUEL TRUCK OFF", "FUEL TRUCK")]
    [InlineData("FUEL TRUCK", "FUEL TRUCK")]
    [InlineData("AFT STAIRS ON", "AFT STAIRS")]
    [InlineData("CHOCKS", "CHOCKS")]
    public void A_tile_is_found_by_its_words(string words, string key) =>
        Assert.Equal(key, Bbs757GroundServices.ForTile(words)!.Key);

    [Fact]
    public void An_unread_state_taps_nothing() =>
        Assert.Equal("The fuel truck state cannot be read, so nothing was tapped.",
            Bbs757GroundServices.Refusal(Svc("FUEL TRUCK"), true, null));

    [Fact]
    public void Already_there_taps_nothing()
    {
        Assert.Equal("Fuel truck already called.", Bbs757GroundServices.Refusal(Svc("FUEL TRUCK"), true, 1));
        Assert.Equal("Chocks already removed.", Bbs757GroundServices.Refusal(Svc("CHOCKS"), false, 0));
        Assert.Null(Bbs757GroundServices.Refusal(Svc("FUEL TRUCK"), true, 0));
    }

    [Fact]
    public void A_result_is_judged_from_the_read_back()
    {
        var s = Svc("CATERING");
        Assert.Equal("Catering called.", Bbs757GroundServices.Result(s, true, 0, 1));
        // An unchanged read-back (BlueBird moved the tile, or ignored the tap) is never a false success.
        Assert.Equal("BlueBird did not change the catering.", Bbs757GroundServices.Result(s, true, 0, 0));
        Assert.Equal("The catering state cannot be read now.", Bbs757GroundServices.Result(s, true, 0, null));
    }

    [Fact]
    public void Remove_all_is_refused_with_external_power_on_line()
    {
        Assert.NotNull(Bbs757GroundServices.RemoveAllRefusal(1));
        Assert.NotNull(Bbs757GroundServices.RemoveAllRefusal(null));
        Assert.Null(Bbs757GroundServices.RemoveAllRefusal(0));
    }

    [Theory]
    [InlineData(100, 100, "Door L1 open")]
    [InlineData(0, 100, "Door L1 closed")]
    [InlineData(50, 100, null)]
    [InlineData(200, 200, "Door L1 open")]
    public void Door_phrases(double pos, double max, string? expected) =>
        Assert.Equal(expected, Bbs757GroundServices.DoorPhrase("L1", pos, max));

    [Fact]
    public void Change_phrases() =>
        Assert.Equal("Aft stairs attached", Bbs757GroundServices.ChangePhrase(Svc("AFT STAIRS"), 1));

    private sealed class Fake
    {
        public double? State;
        public double? AfterTap;
        public int Taps;
        public long Now;
        public bool Armed, Settled;
        public Bbs757EfbRunResult Run = Bbs757EfbRunResult.Done;
        public bool MayHaveClicked = true;

        public Bbs757GroundServiceIo Io() => new()
        {
            ReadState = (_, _) => Task.FromResult(State),
            Run = (_, _) => { Taps++; if (Run == Bbs757EfbRunResult.Done) State = AfterTap; return Task.FromResult((Run, MayHaveClicked)); },
            Delay = (ms, _) => { Now += ms; return Task.CompletedTask; },
            NowMs = () => Now,
            ArmEcho = _ => Armed = true,
            SettleEcho = (_, _) => Settled = true,
        };
    }

    [Fact]
    public async Task Flow_taps_once_and_reads_back()
    {
        var f = new Fake { State = 0, AfterTap = 1 };
        var o = await Bbs757GroundServices.SwitchAsync(Svc("FUEL TRUCK"), true, f.Io(), default);
        Assert.Equal(1, f.Taps);
        Assert.Equal(Bbs757GroundPowerOutcomeKind.Result, o.Kind);
        Assert.Equal("Fuel truck called.", o.Text);
        Assert.True(f.Armed && f.Settled);
    }

    [Fact]
    public async Task Flow_refuses_without_tapping_or_arming()
    {
        var f = new Fake { State = null };
        var o = await Bbs757GroundServices.SwitchAsync(Svc("FUEL TRUCK"), true, f.Io(), default);
        Assert.Equal(0, f.Taps);
        Assert.Equal(Bbs757GroundPowerOutcomeKind.Refused, o.Kind);
        Assert.False(f.Armed);
    }

    [Fact]
    public async Task Flow_says_did_not_when_the_state_never_moves()
    {
        var f = new Fake { State = 0, AfterTap = 0 };
        var o = await Bbs757GroundServices.SwitchAsync(Svc("CARGO SVC"), true, f.Io(), default);
        Assert.Equal("BlueBird did not change the cargo service.", o.Text);
        Assert.True(f.Settled);
    }

    [Fact]
    public async Task A_run_that_tapped_nothing_is_a_run_problem()
    {
        var f = new Fake { State = 0, Run = Bbs757EfbRunResult.Unreachable, MayHaveClicked = false };
        var o = await Bbs757GroundServices.SwitchAsync(Svc("CATERING"), true, f.Io(), default);
        Assert.Equal(Bbs757GroundPowerOutcomeKind.RunProblem, o.Kind);
        Assert.True(f.Settled);
    }
}
