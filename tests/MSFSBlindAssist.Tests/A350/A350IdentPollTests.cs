using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Whole-branch review (2026-11-13): the A350's ident timer asked the sim for the NAV radios every 2 s, each ask leaving a
/// handler subscribed until its answer came, so a sim that did not answer collected one every 2 s. One ask is out at a time; an
/// ask not answered in half a minute is given up, and its late answer dropped.</summary>
public class A350IdentPollTests
{
    private static readonly DateTime T0 = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void One_ask_is_out_at_a_time()
    {
        var poll = new A350IdentPoll();
        Assert.NotNull(poll.Ask(T0));
        Assert.Null(poll.Ask(T0.AddSeconds(2)));
        Assert.Null(poll.Ask(T0.AddSeconds(28)));
    }

    [Fact]
    public void An_answer_lets_the_next_tick_ask()
    {
        var poll = new A350IdentPoll();
        int ticket = poll.Ask(T0)!.Value;
        Assert.True(poll.Answered(ticket));
        Assert.NotNull(poll.Ask(T0.AddSeconds(2)));
    }

    [Fact]
    public void An_ask_never_answered_is_given_up_and_its_late_answer_dropped()
    {
        var poll = new A350IdentPoll();
        int first = poll.Ask(T0)!.Value;
        int second = poll.Ask(T0 + A350IdentPoll.GiveUpAfter)!.Value;
        Assert.NotEqual(first, second);
        Assert.False(poll.Answered(first));    // late: not the one waited for
        Assert.Null(poll.Ask(T0 + A350IdentPoll.GiveUpAfter + TimeSpan.FromSeconds(2)));   // the second is still out
        Assert.True(poll.Answered(second));
    }
}
