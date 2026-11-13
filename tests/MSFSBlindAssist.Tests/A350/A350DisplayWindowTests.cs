using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Plan 26 Task 2: the A350's display window, the Fenix display window's behaviour (`FenixChannelWindowTests`) on the A350's draws: one
/// live list named "&lt;display&gt; display", first in the tab order, row 0 on the first start only, the pilot's row followed through a
/// refresh, one status row in place of the content, the controls after the list, each a panel row. The window is built, never shown:
/// a control on a form never shown is not Visible, so a click is raised through OnClick (reflection), as the pilot's click raises it.
/// </summary>
public class A350DisplayWindowTests
{
    private static A350DrawCapture Draw(string gauge) => new(gauge, Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(),
        Array.Empty<DrawnElement>(), null, 1);

    /// <summary>A feed whose draws are read by a table: each capture's lines, set by the test.</summary>
    private sealed class Feed
    {
        public readonly Dictionary<A350DrawCapture, IReadOnlyList<string>> Lines = new(ReferenceEqualityComparer.Instance);
        public readonly A350DrawCapture?[] Latest = new A350DrawCapture?[2];
        public readonly bool[] Dark = new bool[2];
        public A350DrawCapture? Fresh;
        public int Drawn;

        public A350DrawCapture Show(int side, params string[] lines)
        {
            var cap = Draw("EFIS");
            Lines[cap] = lines;
            Latest[side] = cap;
            return cap;
        }

        public A350DisplayFeed ToFeed() => new(side => Latest[side],
            side => { Drawn++; return Task.FromResult(Fresh); },
            side => Dark[side],
            (cap, side) => Lines[cap]);
    }

    private sealed class Rows : IA350WindowRows
    {
        public readonly Dictionary<string, string> States = new(StringComparer.Ordinal);
        public readonly List<string> Log = new();
        public string? State(string row) => States.TryGetValue(row, out var s) ? s : null;
        public void Press(string row, double value = 1) => Log.Add($"press {row} {value}");
        public void Toggle(string row) => Log.Add($"toggle {row}");
    }

    /// <summary>A window with a key row, so its controls can be held to the panel's rows.</summary>
    private sealed class Window : A350DisplayWindow
    {
        public static readonly IReadOnlyList<A350WindowKey> Keys = new A350WindowKey[]
        {
            new("LS", side => side == 0 ? "A350_EFIS_LS_LEFT" : "A350_EFIS_LS_RIGHT", Toggle: true),
            new("Baro STD", side => side == 0 ? "A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH" : "A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH"),
        };

        public Window(A350DisplayFeed feed, IA350WindowRows rows, int sides = 1) : base("PFD", feed, rows, sides)
        {
            AddKeyRow(Keys);
        }
    }

    private static DisplayListBox ListOf(Form form) => form.Controls.OfType<DisplayListBox>().Single();

    private static string[] ItemsOf(ListBox list) => list.Items.Cast<string>().ToArray();

    private static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { EventArgs.Empty });

    [Fact]
    public void The_list_is_named_for_its_display_and_comes_first()
    {
        var feed = new Feed();
        using var form = new Window(feed.ToFeed(), new Rows());
        Assert.Equal("A350 PFD", form.Text);
        var list = ListOf(form);
        Assert.Equal("PFD display", list.AccessibleName);
        Assert.Equal(0, list.TabIndex);
        Assert.All(form.Controls.Cast<Control>().Where(c => c != list), c => Assert.True(c.TabIndex > 0));
    }

    [Fact]
    public void The_first_start_reads_and_selects_the_first_row_a_second_does_not_move_the_pilot()
    {
        var feed = new Feed();
        feed.Show(0, "FMA: SPEED", "Speed 250 knots", "Altitude 3000 feet");
        using var form = new Window(feed.ToFeed(), new Rows());
        form.StartLive();
        var list = ListOf(form);
        Assert.Equal(new[] { "FMA: SPEED", "Speed 250 knots", "Altitude 3000 feet" }, ItemsOf(list));
        Assert.Equal(0, list.SelectedIndex);
        list.SelectedIndex = 2;
        form.StartLive();
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void A_refresh_keeps_the_row_by_its_words()
    {
        var feed = new Feed();
        feed.Show(0, "FMA: SPEED", "Altitude 3000 feet");
        using var form = new Window(feed.ToFeed(), new Rows());
        form.StartLive();
        var list = ListOf(form);
        list.SelectedIndex = 1;
        feed.Show(0, "FMA: SPEED", "Speed 250 knots", "Altitude 3100 feet");
        form.RefreshNow();
        Assert.Equal("Altitude 3100 feet", list.SelectedItem);
    }

    [Fact]
    public void A_dark_display_shows_one_status_row_and_the_row_comes_back_after_it()
    {
        var feed = new Feed();
        feed.Show(0, "FMA: SPEED", "Altitude 3000 feet");
        using var form = new Window(feed.ToFeed(), new Rows());
        form.StartLive();
        var list = ListOf(form);
        list.SelectedIndex = 1;
        feed.Dark[0] = true;
        form.RefreshNow();
        Assert.Equal(new[] { "PFD: not available, display off" }, ItemsOf(list));
        feed.Dark[0] = false;
        feed.Show(0, "FMA: SPEED", "Altitude 3200 feet");
        form.RefreshNow();
        Assert.Equal("Altitude 3200 feet", list.SelectedItem);
    }

    [Fact]
    public void A_display_not_drawn_yet_is_not_read_yet()
    {
        using var form = new Window(new Feed().ToFeed(), new Rows());
        form.StartLive();
        Assert.Equal(new[] { "PFD: not available, not read yet" }, ItemsOf(ListOf(form)));
    }

    [Fact]
    public void A_key_button_reads_its_rows_state_and_its_bare_label_while_unread()
    {
        var rows = new Rows();
        rows.States["A350_EFIS_LS_LEFT"] = "On";
        using var form = new Window(new Feed().ToFeed(), rows);
        form.StartLive();
        Assert.Equal(new[] { "LS: On", "Baro STD" }, form.KeyButtons.Select(b => b.Text));
        rows.States.Remove("A350_EFIS_LS_LEFT");
        form.RefreshNow();
        Assert.Equal("LS", form.KeyButtons[0].Text);
    }

    [Fact]
    public void A_click_presses_its_row_once_and_a_toggle_toggles_it()
    {
        var rows = new Rows();
        using var form = new Window(new Feed().ToFeed(), rows);
        form.StartLive();
        Click(form.KeyButtons[1]);
        Click(form.KeyButtons[0]);
        Assert.Equal(new[] { "press A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH 1", "toggle A350_EFIS_LS_LEFT" }, rows.Log);
    }

    [Fact]
    public void With_two_sides_the_side_box_reads_side_1_and_presses_its_rows()
    {
        var feed = new Feed();
        feed.Show(0, "Captain PFD line");
        feed.Show(1, "First officer PFD line");
        var rows = new Rows();
        using var form = new Window(feed.ToFeed(), rows, sides: 2);
        form.StartLive();
        var side = form.Controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Side");
        Assert.Equal(new[] { "Captain", "First officer" }, side.Items.Cast<string>());
        side.SelectedIndex = 1;
        Assert.Equal(new[] { "First officer PFD line" }, ItemsOf(ListOf(form)));
        Click(form.KeyButtons[1]);
        Assert.Equal(new[] { "press A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH 1" }, rows.Log);
    }

    [Fact]
    public void A_one_sided_window_has_no_side_box()
    {
        using var form = new Window(new Feed().ToFeed(), new Rows());
        Assert.DoesNotContain(form.Controls.OfType<ComboBox>(), c => c.AccessibleName == "Side");
    }

    [Fact]
    public void A_press_is_read_from_a_fresh_draw_and_the_older_latest_never_replaces_it()
    {
        var feed = new Feed();
        var older = feed.Show(0, "LS off");
        using var form = new Window(feed.ToFeed(), new Rows()) { Pause = (_, _) => Task.CompletedTask };
        form.StartLive();
        feed.Fresh = Draw("EFIS");
        feed.Lines[feed.Fresh] = new[] { "LS on" };
        form.AfterPressAsync().GetAwaiter().GetResult();
        Assert.Equal(1, feed.Drawn);
        Assert.Equal(new[] { "LS on" }, ItemsOf(ListOf(form)));
        form.RefreshNow();   // the monitor's latest is still the draw from before the press
        Assert.Equal(new[] { "LS on" }, ItemsOf(ListOf(form)));
        var newer = feed.Show(0, "LS on, newer");
        form.RefreshNow();
        Assert.Equal(new[] { "LS on, newer" }, ItemsOf(ListOf(form)));
        Assert.NotSame(older, newer);
    }

    [Fact]
    public void A_read_that_throws_costs_only_the_list()
    {
        var feed = new Feed();
        var cap = feed.Show(0);
        var broken = new A350DisplayFeed(_ => cap, _ => Task.FromResult<A350DrawCapture?>(null), _ => false, (_, _) => throw new InvalidOperationException("torn"));
        using var form = new Window(broken, new Rows());
        form.StartLive();
        Assert.Equal(new[] { "PFD: not available, read failed" }, ItemsOf(ListOf(form)));
    }
}
