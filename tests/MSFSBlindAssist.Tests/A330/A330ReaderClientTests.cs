using MSFSBlindAssist.Aircraft.A330.Display;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330ReaderClientTests
{
    private sealed class FakeTransport : IA330ReaderTransport
    {
        public readonly List<string> Written = new();
        public bool Disposed;
        public event Action<string>? LineReceived;
        public event Action? Exited;
        public void WriteLine(string line) => Written.Add(line);
        public void Raise(string line) => LineReceived?.Invoke(line);
        public void Exit() => Exited?.Invoke();
        public void Dispose() => Disposed = true;
    }

    private sealed class Rig
    {
        public long Clock;
        public readonly List<FakeTransport> Transports = new();
        public int Starts;
        public bool Installed = true;
        public readonly A330ReaderClient Client;

        public Rig()
        {
            Client = new A330ReaderClient(() =>
            {
                Starts++;
                if (!Installed) return null;
                var t = new FakeTransport();
                Transports.Add(t);
                return t;
            }, () => Clock, runTimer: false);
        }

        public FakeTransport Current => Transports[^1];

        public Rig Started()
        {
            Client.Start();
            return this;
        }

        public Rig Hello()
        {
            Current.Raise("{\"hello\":1}");
            return this;
        }
    }

    private static string FrameLine(string gauge, bool complete, params string[] texts)
    {
        var parts = new List<string>();
        for (int i = 0; i < texts.Length; i++)
            parts.Add("{\"order\":" + i + ",\"x\":10,\"baseline\":20,\"text\":\"" + texts[i] +
                      "\",\"font\":\"F\",\"size\":12,\"charX\":[10,20]}");
        return "{\"frame\":{\"gauge\":\"" + gauge + "\",\"complete\":" + (complete ? "true" : "false") +
               ",\"missing\":" + (complete ? 0 : 3) + ",\"texts\":[" + string.Join(",", parts) + "]}}";
    }

    [Fact]
    public void Before_Start_the_state_is_Stopped()
    {
        var rig = new Rig();
        Assert.Equal(A330ReaderState.Stopped, rig.Client.State);
        Assert.Equal(A330ReaderStatus.Text(A330ReaderState.Stopped), rig.Client.StatusText);
    }

    [Fact]
    public void No_program_is_NotInstalled_and_nothing_is_retried()
    {
        var rig = new Rig { Installed = false };
        var states = new List<A330ReaderState>();
        rig.Client.StateChanged += states.Add;
        rig.Client.Start();
        Assert.Equal(A330ReaderState.NotInstalled, rig.Client.State);
        rig.Clock = 600_000;
        rig.Client.Tick();
        rig.Client.Start();
        rig.Client.Tick();
        Assert.Equal(1, rig.Starts);
        Assert.Equal(new[] { A330ReaderState.NotInstalled }, states);
    }

    [Fact]
    public void Start_sends_hello_and_stays_Starting_until_the_reader_says_otherwise()
    {
        var rig = new Rig().Started();
        Assert.Equal(new[] { "{\"hello\":1}" }, rig.Current.Written);
        Assert.Equal(A330ReaderState.Starting, rig.Client.State);
        rig.Client.Start();
        Assert.Equal(1, rig.Starts);
        rig.Hello();
        rig.Current.Raise("{\"state\":\"Running\"}");
        Assert.Equal(A330ReaderState.Running, rig.Client.State);
        rig.Current.Raise("{\"state\":\"AircraftNotFound\"}");
        Assert.Equal(A330ReaderState.AircraftNotFound, rig.Client.State);
    }

    [Fact]
    public void A_hello_of_another_version_is_OutOfDate()
    {
        var rig = new Rig().Started();
        var t = rig.Current;
        t.Raise("{\"hello\":2}");
        Assert.Equal(A330ReaderState.OutOfDate, rig.Client.State);
        Assert.Equal("{\"quit\":true}", t.Written[^1]);
        Assert.True(t.Disposed);
        t.Raise("{\"state\":\"Running\"}");
        t.Exit();
        rig.Clock = 60_000;
        rig.Client.Tick();
        Assert.Equal(A330ReaderState.OutOfDate, rig.Client.State);
        Assert.Equal(1, rig.Starts);
    }

    [Fact]
    public void No_hello_in_five_seconds_is_Failed()
    {
        var rig = new Rig().Started();
        rig.Clock = 4_999;
        rig.Client.Tick();
        Assert.Equal(A330ReaderState.Starting, rig.Client.State);
        rig.Clock = 5_000;
        rig.Client.Tick();
        Assert.Equal(A330ReaderState.Failed, rig.Client.State);
        rig.Clock = 600_000;
        rig.Client.Tick();
        Assert.Equal(1, rig.Starts);
    }

    [Fact]
    public void A_hello_in_time_clears_the_deadline()
    {
        var rig = new Rig().Started().Hello();
        rig.Clock = 60_000;
        rig.Client.Tick();
        Assert.Equal(A330ReaderState.Starting, rig.Client.State);
    }

    [Fact]
    public void Two_owners_of_one_gauge_send_one_want_with_the_smaller_refresh()
    {
        var rig = new Rig();
        var a = new object();
        var b = new object();
        rig.Client.Want(A330Gauge.McduCaptain, a, 500);
        rig.Client.Want(A330Gauge.McduCaptain, b, 250);
        rig.Started();
        Assert.Equal(new[] { "{\"hello\":1}", "{\"want\":\"McduCaptain\",\"refreshMs\":250}" }, rig.Current.Written);

        // Live: a larger refresh sends nothing, a smaller one sends a new want, a repeat sends nothing.
        var c = new object();
        rig.Client.Want(A330Gauge.McduCaptain, c, 1000);
        rig.Client.Want(A330Gauge.McduCaptain, b, 250);
        Assert.Equal(2, rig.Current.Written.Count);
        var d = new object();
        rig.Client.Want(A330Gauge.McduCaptain, d, 100);
        Assert.Equal("{\"want\":\"McduCaptain\",\"refreshMs\":100}", rig.Current.Written[^1]);
        Assert.Equal(3, rig.Current.Written.Count);
    }

    [Fact]
    public void Releasing_the_smallest_owner_sends_the_new_smallest()
    {
        var rig = new Rig().Started();
        var a = new object();
        var b = new object();
        rig.Client.Want(A330Gauge.Ewd, a, 500);
        rig.Client.Want(A330Gauge.Ewd, b, 250);
        rig.Client.Release(A330Gauge.Ewd, b);
        Assert.Equal("{\"want\":\"Ewd\",\"refreshMs\":500}", rig.Current.Written[^1]);
    }

    [Fact]
    public void The_last_release_sends_release()
    {
        var rig = new Rig().Started();
        var a = new object();
        var b = new object();
        rig.Client.Want(A330Gauge.Sd, a, 500);
        rig.Client.Want(A330Gauge.Sd, b, 500);
        int before = rig.Current.Written.Count;
        rig.Client.Release(A330Gauge.Sd, a);
        Assert.Equal(before, rig.Current.Written.Count);
        rig.Client.Release(A330Gauge.Sd, b);
        Assert.Equal("{\"release\":\"Sd\"}", rig.Current.Written[^1]);
        int after = rig.Current.Written.Count;
        rig.Client.Release(A330Gauge.Sd, b);
        Assert.Equal(after, rig.Current.Written.Count);
    }

    [Fact]
    public void A_complete_frame_becomes_latest_and_raises_FrameChanged_once()
    {
        var rig = new Rig().Started().Hello();
        rig.Client.Want(A330Gauge.McduCaptain, new object(), 250);
        var raised = new List<A330Gauge>();
        rig.Client.FrameChanged += raised.Add;
        rig.Clock = 1_000;
        rig.Current.Raise(FrameLine("McduCaptain", true, "INIT"));
        Assert.Equal(new[] { A330Gauge.McduCaptain }, raised);
        var latest = rig.Client.Latest(A330Gauge.McduCaptain);
        Assert.NotNull(latest);
        Assert.Equal(1_000, latest!.ReadTicks);
        Assert.Same(latest, rig.Client.Newest(A330Gauge.McduCaptain));
        Assert.Null(rig.Client.Latest(A330Gauge.Ewd));
    }

    [Fact]
    public void The_same_frame_again_raises_nothing()
    {
        var rig = new Rig().Started().Hello();
        rig.Client.Want(A330Gauge.McduCaptain, new object(), 250);
        int raised = 0;
        rig.Client.FrameChanged += _ => raised++;
        rig.Current.Raise(FrameLine("McduCaptain", true, "INIT"));
        rig.Clock = 100;
        rig.Current.Raise(FrameLine("McduCaptain", true, "INIT"));
        Assert.Equal(1, raised);
        Assert.Equal(100, rig.Client.Latest(A330Gauge.McduCaptain)!.ReadTicks);
        rig.Current.Raise(FrameLine("McduCaptain", true, "INIT", "MORE"));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void An_incomplete_frame_is_newest_but_not_latest()
    {
        var rig = new Rig().Started().Hello();
        rig.Client.Want(A330Gauge.Ewd, new object(), 250);
        int raised = 0;
        rig.Client.FrameChanged += _ => raised++;
        rig.Current.Raise(FrameLine("Ewd", false, "HALF"));
        Assert.Null(rig.Client.Latest(A330Gauge.Ewd));
        Assert.NotNull(rig.Client.Newest(A330Gauge.Ewd));
        Assert.False(rig.Client.Newest(A330Gauge.Ewd)!.Complete);
        Assert.Equal(0, raised);

        rig.Current.Raise(FrameLine("Ewd", true, "FULL"));
        rig.Current.Raise(FrameLine("Ewd", false, "HALF"));
        Assert.Equal("FULL", rig.Client.Latest(A330Gauge.Ewd)!.Texts[0].Text);
        Assert.Equal("HALF", rig.Client.Newest(A330Gauge.Ewd)!.Texts[0].Text);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Latest_goes_null_after_three_refreshes()
    {
        var rig = new Rig().Started().Hello();
        rig.Client.Want(A330Gauge.Sd, new object(), 250);
        rig.Clock = 1_000;
        rig.Current.Raise(FrameLine("Sd", true, "X"));
        rig.Clock = 1_750;
        Assert.NotNull(rig.Client.Latest(A330Gauge.Sd));
        rig.Clock = 1_751;
        Assert.Null(rig.Client.Latest(A330Gauge.Sd));
        Assert.NotNull(rig.Client.Newest(A330Gauge.Sd));
    }

    [Fact]
    public void A_not_drawn_frame_is_newest_and_never_latest()
    {
        var rig = new Rig().Started().Hello();
        rig.Current.Raise("{\"frame\":{\"gauge\":\"Isis\",\"complete\":false,\"missing\":0,\"drawn\":false,\"texts\":[]}}");
        Assert.Null(rig.Client.Latest(A330Gauge.Isis));
        Assert.False(rig.Client.Newest(A330Gauge.Isis)!.Drawn);
    }

    [Fact]
    public void Scratchpad_lines_set_that_sides_text()
    {
        var rig = new Rig().Started().Hello();
        Assert.Null(rig.Client.McduScratchpad(false));
        rig.Current.Raise("{\"scratchpad\":{\"gauge\":\"McduCaptain\",\"text\":\"CHECK\"}}");
        rig.Current.Raise("{\"scratchpad\":{\"gauge\":\"McduFirstOfficer\",\"text\":\"\"}}");
        rig.Current.Raise("{\"scratchpad\":{\"gauge\":\"Ewd\",\"text\":\"IGNORED\"}}");
        Assert.Equal("CHECK", rig.Client.McduScratchpad(false));
        Assert.Equal("", rig.Client.McduScratchpad(true));
    }

    [Fact]
    public void An_exit_restarts_and_asks_for_the_wanted_gauges_again()
    {
        var rig = new Rig().Started().Hello();
        rig.Client.Want(A330Gauge.McduCaptain, new object(), 250);
        rig.Client.Want(A330Gauge.Ewd, new object(), 500);
        rig.Clock = 1_000;
        rig.Current.Raise(FrameLine("McduCaptain", true, "INIT"));
        rig.Current.Raise("{\"scratchpad\":{\"gauge\":\"McduCaptain\",\"text\":\"CHECK\"}}");
        var first = rig.Current;

        rig.Clock = 2_000;
        first.Exit();
        Assert.Equal(A330ReaderState.Starting, rig.Client.State);
        Assert.Null(rig.Client.Newest(A330Gauge.McduCaptain));
        Assert.Null(rig.Client.McduScratchpad(false));
        Assert.True(first.Disposed);

        rig.Clock = 6_999;
        rig.Client.Tick();
        Assert.Equal(1, rig.Starts);
        rig.Clock = 7_000;
        rig.Client.Tick();
        Assert.Equal(2, rig.Starts);
        Assert.Equal(new[]
        {
            "{\"hello\":1}",
            "{\"want\":\"McduCaptain\",\"refreshMs\":250}",
            "{\"want\":\"Ewd\",\"refreshMs\":500}",
        }.OrderBy(s => s), rig.Current.Written.OrderBy(s => s));
        Assert.Equal("{\"hello\":1}", rig.Current.Written[0]);
        rig.Client.Tick();
        Assert.Equal(2, rig.Starts);
    }

    [Fact]
    public void A_restart_with_no_program_is_NotInstalled()
    {
        var rig = new Rig().Started().Hello();
        rig.Current.Exit();
        rig.Installed = false;
        rig.Clock = 5_000;
        rig.Client.Tick();
        Assert.Equal(A330ReaderState.NotInstalled, rig.Client.State);
    }

    [Fact]
    public void A_fourth_exit_in_five_minutes_is_Failed()
    {
        var rig = new Rig().Started().Hello();
        for (int i = 0; i < 3; i++)
        {
            rig.Current.Exit();
            Assert.Equal(A330ReaderState.Starting, rig.Client.State);
            rig.Clock += 5_000;
            rig.Client.Tick();
            Assert.Equal(i + 2, rig.Starts);
            rig.Hello();
        }
        rig.Current.Exit();
        Assert.Equal(A330ReaderState.Failed, rig.Client.State);
        rig.Clock += 60_000;
        rig.Client.Tick();
        Assert.Equal(4, rig.Starts);
    }

    [Fact]
    public void Exits_more_than_five_minutes_apart_do_not_add_up()
    {
        var rig = new Rig().Started().Hello();
        for (int i = 0; i < 6; i++)
        {
            rig.Current.Exit();
            rig.Clock += 5_000;
            rig.Client.Tick();
            rig.Hello();
            rig.Clock += 300_000;
        }
        Assert.NotEqual(A330ReaderState.Failed, rig.Client.State);
        Assert.Equal(7, rig.Starts);
    }

    [Fact]
    public void Reset_forgets_every_frame_and_sends_reset()
    {
        var rig = new Rig().Started().Hello();
        rig.Client.Want(A330Gauge.Sd, new object(), 250);
        rig.Current.Raise(FrameLine("Sd", true, "X"));
        rig.Current.Raise("{\"scratchpad\":{\"gauge\":\"McduCaptain\",\"text\":\"CHECK\"}}");
        rig.Client.Reset();
        Assert.Equal("{\"reset\":true}", rig.Current.Written[^1]);
        Assert.Null(rig.Client.Newest(A330Gauge.Sd));
        Assert.Null(rig.Client.Latest(A330Gauge.Sd));
        Assert.Null(rig.Client.McduScratchpad(false));
        // The want is kept: after a restart it is asked for again.
        rig.Current.Exit();
        rig.Clock = 5_000;
        rig.Client.Tick();
        Assert.Contains("{\"want\":\"Sd\",\"refreshMs\":250}", rig.Current.Written);
    }

    [Fact]
    public void Stop_sends_quit()
    {
        var rig = new Rig().Started().Hello();
        var t = rig.Current;
        rig.Client.Stop();
        Assert.Equal("{\"quit\":true}", t.Written[^1]);
        Assert.Equal(A330ReaderState.Stopped, rig.Client.State);
        t.Exit();
        rig.Clock = 60_000;
        rig.Client.Tick();
        Assert.Equal(1, rig.Starts);
        Assert.Equal(A330ReaderState.Stopped, rig.Client.State);
        rig.Client.Dispose();
    }

    [Fact]
    public void A_broken_line_is_dropped_and_the_client_carries_on()
    {
        var rig = new Rig().Started().Hello();
        rig.Current.Raise("{\"state\":\"Running\"}");
        rig.Client.Want(A330Gauge.Sd, new object(), 250);
        int raised = 0;
        rig.Client.FrameChanged += _ => raised++;
        rig.Current.Raise(FrameLine("Sd", true, "ONE"));
        rig.Current.Raise("this is not json");
        rig.Current.Raise(FrameLine("NoSuchGauge", true, "BAD"));
        rig.Current.Raise("{\"frame\":");
        rig.Current.Raise(FrameLine("Sd", true, "TWO"));
        Assert.Equal(2, raised);
        Assert.Equal("TWO", rig.Client.Latest(A330Gauge.Sd)!.Texts[0].Text);
        Assert.Equal(A330ReaderState.Running, rig.Client.State);
    }

    [Fact]
    public void A_handler_may_call_back_into_the_client()
    {
        var rig = new Rig().Started().Hello();
        var owner = new object();
        rig.Client.FrameChanged += g =>
        {
            Assert.NotNull(rig.Client.Latest(g));
            rig.Client.Want(g, owner, 100);
        };
        rig.Client.StateChanged += _ => _ = rig.Client.State;
        rig.Current.Raise(FrameLine("Sd", true, "X"));
        rig.Current.Raise("{\"state\":\"Running\"}");
        Assert.Contains("{\"want\":\"Sd\",\"refreshMs\":100}", rig.Current.Written);
    }

    [Fact]
    public void Each_client_starts_its_own_program()
    {
        var a = new Rig();
        var b = new Rig();
        a.Client.Start();
        b.Client.Start();
        Assert.Equal(1, a.Starts);
        Assert.Equal(1, b.Starts);
        Assert.NotSame(a.Current, b.Current);
    }
}
