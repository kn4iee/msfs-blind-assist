using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26's review minor: the SD's draw names no page, so its rows are read by the page the A350's variable says. After a page
/// key the new page was drawn before that variable arrived and read by the old page's fields for a tick. The window now holds its rows
/// until the page has changed (at most <see cref="A350SdWindow.SettleMs"/>), and takes its fresh draw then.</summary>
public class A350SdWindowTests
{
    private static A350DrawCapture Draw() => new("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);

    private sealed class Rows : IA350WindowRows
    {
        public string? State(string row) => null;
        public void Press(string row, double value = 1) { }
        public void Toggle(string row) { }
        public void Type(string row, string text) { }
    }

    private static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { EventArgs.Empty });

    private static string[] Items(Form form) => form.Controls.OfType<DisplayListBox>().Single().Items.Cast<string>().ToArray();

    [Fact]
    public void A_page_keys_fresh_draw_is_taken_once_the_page_has_changed()
    {
        double page = 3;
        double? drawnAtPage = null;
        var lines = new Dictionary<A350DrawCapture, string[]>(ReferenceEqualityComparer.Instance);
        var before = Draw();
        lines[before] = new[] { "Elec DC page" };
        var fresh = Draw();
        lines[fresh] = new[] { "Engine page" };
        var feed = new A350DisplayFeed(_ => before, _ => { drawnAtPage = page; return Task.FromResult<A350DrawCapture?>(fresh); }, _ => false,
            (c, _) => lines[c]);
        int polls = 0;
        using var form = new A350SdWindow(feed, new Rows(), () => page)
        {
            Pause = (ms, _) =>
            {
                if (ms == A350SdWindow.PollMs && ++polls == 3) page = 1;   // the page variable arrives on the third poll
                return Task.CompletedTask;
            },
        };
        form.StartLive();
        Click(form.KeyButtons[0]);   // ENG
        Assert.Equal(1, drawnAtPage);
        Assert.Equal(new[] { "Engine page" }, Items(form));
    }

    [Fact]
    public void While_the_page_has_not_changed_the_rows_hold()
    {
        double page = 3;
        var lines = new Dictionary<A350DrawCapture, string[]>(ReferenceEqualityComparer.Instance);
        var shown = Draw();
        lines[shown] = new[] { "Elec DC page" };
        var latest = shown;
        var poll = new TaskCompletionSource();
        var feed = new A350DisplayFeed(_ => latest, _ => Task.FromResult<A350DrawCapture?>(latest), _ => false, (c, _) => lines[c]);
        using var form = new A350SdWindow(feed, new Rows(), () => page)
        {
            Pause = (ms, _) => ms == A350SdWindow.PollMs ? poll.Task : Task.CompletedTask,
        };
        form.StartLive();
        Click(form.KeyButtons[0]);
        latest = Draw();
        lines[latest] = new[] { "Engine page, read by the old page" };
        form.RefreshNow();
        Assert.Equal(new[] { "Elec DC page" }, Items(form));   // held: the page variable still says the old page
        page = 1;
        form.RefreshNow();
        Assert.Equal(new[] { "Engine page, read by the old page" }, Items(form));
    }

    // A key that changes no page (the page shown pressed again) holds the rows no longer than the wait's cap.
    [Fact]
    public void A_key_that_changes_no_page_holds_no_longer_than_the_cap()
    {
        double page = 1;
        int drawn = 0, waited = 0;
        var cap = Draw();
        var feed = new A350DisplayFeed(_ => cap, _ => { drawn++; return Task.FromResult<A350DrawCapture?>(cap); }, _ => false, (_, _) => new[] { "Engine page" });
        using var form = new A350SdWindow(feed, new Rows(), () => page)
        {
            Pause = (ms, _) => { if (ms == A350SdWindow.PollMs) waited += ms; return Task.CompletedTask; },
        };
        form.StartLive();
        Click(form.KeyButtons[0]);
        Assert.Equal(1, drawn);
        Assert.InRange(waited, A350SdWindow.SettleMs - A350SdWindow.PollMs, A350SdWindow.SettleMs);
    }
}
