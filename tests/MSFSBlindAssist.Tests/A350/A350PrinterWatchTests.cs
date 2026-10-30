using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

public class A350PrinterWatchTests
{
    private const string Printout = "\nFM ATC FLIGHT PLAN INITIALIZATION DATA              DATE: 30 OCT 26  \n     TIME: 10:50\n";

    [Fact]
    public void The_first_read_is_the_baseline_and_a_new_printout_is_said_by_its_title()
    {
        var watch = new A350PrinterWatch();
        Assert.Null(watch.Read(Printout));
        Assert.Null(watch.Read(Printout));
        Assert.Equal("Printer: FM ATC FLIGHT PLAN INITIALIZATION DATA", watch.Read(Printout.Replace("10:50", "10:51")));
    }

    [Fact]
    public void The_first_print_after_a_blank_paper_is_said()
    {
        var watch = new A350PrinterWatch();
        Assert.Null(watch.Read(""));
        Assert.Equal("Printer: FM ACTIVE TAKE-OFF DATA", watch.Read("FM ACTIVE TAKE-OFF DATA"));
    }

    [Fact]
    public void A_failed_read_changes_nothing_and_a_blank_paper_says_nothing()
    {
        var watch = new A350PrinterWatch();
        Assert.Null(watch.Read(null));
        Assert.Null(watch.Read("A"));   // a failed read is no baseline: the paper was never seen blank
        Assert.Null(watch.Read(null));
        Assert.Null(watch.Read("A"));
        Assert.Null(watch.Read(""));
        Assert.Equal("Printer: B", watch.Read("B"));
    }

    [Fact]
    public void A_read_is_due_every_few_seconds_and_never_doubled()
    {
        var watch = new A350PrinterWatch();
        Assert.True(watch.IsDue(0));
        watch.Began(0);
        Assert.False(watch.IsDue(A350PrinterWatch.EveryMs + 1));   // still in flight
        watch.Read("A");
        Assert.False(watch.IsDue(A350PrinterWatch.EveryMs - 1));
        Assert.True(watch.IsDue(A350PrinterWatch.EveryMs));
    }

    [Fact]
    public void A_reset_makes_the_next_read_a_baseline()
    {
        var watch = new A350PrinterWatch();
        watch.Read("A");
        watch.Reset();
        Assert.Null(watch.Read("B"));
    }

    [Fact]
    public void Its_row_is_in_the_monitor_manager()
    {
        var rows = A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        Assert.Contains(rows, r => r.Key == A350PrinterWatch.MuteKey && r.Label == A350PrinterWatch.MuteLabel);
    }
}
