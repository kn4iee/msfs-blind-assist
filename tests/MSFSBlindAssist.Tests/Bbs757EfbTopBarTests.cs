using System.Drawing;
using System.Text.RegularExpressions;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet's top bar (plan 18) against plan 18's captures (Task 1, 2026-10-29): the departure countdown, the boarding
/// control and the notification control read into the page's <see cref="Bbs757EfbScreenModel.TopBar"/>; the clock, the
/// date and the outside air temperature left out. The keys are what keeps the screen reader's focus on an item while its
/// words change, so they are pinned as strings.
/// </summary>
public class Bbs757EfbTopBarTests
{
    private static Bbs757EfbItem Top(Bbs757EfbScreenModel m, string key) =>
        m.TopBar.FirstOrDefault(i => i.Key == key)
        ?? throw new Xunit.Sdk.XunitException($"No top bar item '{key}' on {m.PageName}: "
            + string.Join(" | ", m.TopBar.Select(i => i.Key + " = " + i.Text)));

    [Fact]
    public void During_boarding_the_bar_reads_the_countdown_the_minutes_left_and_the_count()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding");

        var departure = Top(m, "top:dep");
        Assert.Equal(Bbs757EfbKind.Text, departure.Kind);
        Assert.Equal("DEP -00:46", departure.Text);   // read "DEP-00:46"
        Assert.False(departure.Clickable);

        var boarding = Top(m, "top:boarding");
        Assert.Equal(Bbs757EfbKind.Text, boarding.Kind);
        Assert.Equal("Boarding… 10 min", boarding.Text);   // read "30 B0ARDIHG... 10 MIN": the icon, then a misread
        Assert.False(boarding.Clickable);

        var count = Top(m, "top:notifications");
        Assert.Equal(Bbs757EfbKind.Button, count.Kind);
        Assert.Equal("Notifications: 10", count.Text);
        Assert.True(count.Clickable);
        Assert.Equal(new Point(1373, 15), count.Hit);

        // Left to right, as the bar shows them.
        Assert.Equal(new[] { "top:dep", "top:boarding", "top:notifications" }, m.TopBar.Select(i => i.Key));
    }

    [Fact]
    public void Start_boarding_is_a_button_at_its_green_box()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding");

        var boarding = Top(m, "top:boarding");
        Assert.Equal(Bbs757EfbKind.Button, boarding.Kind);
        Assert.Equal("Start boarding", boarding.Text);   // read "3) START BOARDING": the icon's glyph is no word
        Assert.True(boarding.Clickable);
        // The green box spans x 1142-1321, y 2-30 on the capture; START BOARDING was tapped live at (1231,24).
        Assert.InRange(boarding.Box.Left, 1138, 1146);
        Assert.InRange(boarding.Box.Right, 1316, 1326);
        Assert.InRange(boarding.Hit.X, 1200, 1262);
        Assert.InRange(boarding.Hit.Y, 8, 26);
        Assert.Equal("DEP -03:19", Top(m, "top:dep").Text);
    }

    [Theory]
    [InlineData("bright", "12-aircraft-fuel-boarding")]
    [InlineData("bright", "18-aircraft-ground-services-notification")]
    [InlineData("bright", "19-notification-list")]
    [InlineData("bright", "20-fuel-start-boarding")]
    [InlineData("bright", "40-checklist")]
    [InlineData("bright", "42-checklist-header-unticked")]
    [InlineData("dim", "15-aircraft-maintenance")]
    public void The_clock_and_oat_are_left_out(string set, string name)
    {
        var m = Bbs757EfbScreenTests.Load(set, name);

        // Every capture shows the notification arrow at least; nothing but the three controls is taken.
        Assert.Contains(m.TopBar, i => i.Key == "top:notifications");
        Assert.All(m.TopBar, i => Assert.Contains(i.Key, new[] { "top:dep", "top:boarding", "top:notifications" }));
        Assert.DoesNotContain(m.TopBar.Concat(m.Items).Concat(m.Menu), i => i.Text.Contains("OAT", StringComparison.OrdinalIgnoreCase)
            || i.Text.Contains("2026", StringComparison.Ordinal) || Regex.IsMatch(i.Text, @"\d{1,2}:\d{2}[LZ]"));
    }

    [Fact]
    public void Top_bar_keys_stay_when_the_words_change()
    {
        var boarding = Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding");
        var start = Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding");
        var open = Bbs757EfbScreenTests.Load("bright", "19-notification-list");

        // Text while boarding runs, a button before it starts: one key.
        Assert.NotEqual(Top(boarding, "top:boarding").Text, Top(start, "top:boarding").Text);
        Assert.NotEqual(Top(boarding, "top:boarding").Kind, Top(start, "top:boarding").Kind);
        Assert.NotEqual(Top(boarding, "top:dep").Text, Top(start, "top:dep").Text);
        // The count while the list is closed, Close notifications while it is open: one key, one place.
        Assert.NotEqual(Top(boarding, "top:notifications").Text, Top(open, "top:notifications").Text);
        Assert.Equal(Top(boarding, "top:notifications").Hit, Top(open, "top:notifications").Hit);

        Assert.Equal("top:dep", Bbs757EfbTopBar.DepartureKey);
        Assert.Equal("top:boarding", Bbs757EfbTopBar.BoardingKey);
        Assert.Equal("top:notifications", Bbs757EfbTopBar.NotificationsKey);
    }

    [Fact]
    public void An_open_notification_list_is_closed_at_the_arrow()
    {
        // The list is open: BlueBird's panel shows and the count pill does not.
        var n = Top(Bbs757EfbScreenTests.Load("bright", "19-notification-list"), "top:notifications");

        Assert.Equal(Bbs757EfbKind.Button, n.Kind);
        Assert.Equal("Close notifications", n.Text);
        Assert.True(n.Clickable);
        Assert.Equal(new Point(1373, 15), n.Hit);
    }

    [Theory]
    [InlineData("18-aircraft-ground-services-notification")]   // BlueBird's popup over the page, the count still showing
    [InlineData("20-fuel-start-boarding")]                     // a count whose figure OCR did not read
    [InlineData("40-checklist")]                               // no count at all
    public void A_closed_list_is_opened_at_the_arrow(string name)
    {
        var n = Top(Bbs757EfbScreenTests.Load("bright", name), "top:notifications");

        Assert.Equal(Bbs757EfbKind.Button, n.Kind);
        Assert.StartsWith("Notifications", n.Text, StringComparison.Ordinal);
        Assert.True(n.Clickable);
        Assert.Equal(new Point(1373, 15), n.Hit);
    }

    [Theory]
    [InlineData("DEP-00:46", "DEP -00:46")]
    [InlineData("DEP -03:21", "DEP -03:21")]
    [InlineData("DEP+00:05", "DEP +00:05")]
    [InlineData("DEP −00:46", "DEP -00:46")]   // a minus sign read as U+2212
    [InlineData("DEP 01:10", "DEP 01:10")]
    [InlineData("Thu, 29th Oct 2026 | 12:46L | 11:46Z | OAT 26 C", null)]
    [InlineData("10", null)]
    public void The_countdown_is_said_as_the_tablet_writes_it(string read, string? expected) =>
        Assert.Equal(expected, Bbs757EfbTopBar.DepartureText(read));

    [Theory]
    [InlineData("3) START BOARDING", true, "Start boarding")]
    [InlineData("» START BOARDING", true, "Start boarding")]
    [InlineData("START BOARDING", true, "Start boarding")]
    [InlineData("30 B0ARDIHG... 10 MIN", false, "Boarding… 10 min")]
    [InlineData("» BOARDING… 3 MIN", false, "Boarding… 3 min")]
    [InlineData("BOARDING... 12 MIN", false, "Boarding… 12 min")]
    // Words the box was never seen with are said as read, as text.
    [InlineData("» DEBOARDING... 5 MIN", false, "Deboarding... 5 min")]
    public void The_boarding_box_is_a_button_only_while_it_offers_to_start(string read, bool button, string text)
    {
        var said = Bbs757EfbTopBar.BoardingText(read);

        Assert.NotNull(said);
        Assert.Equal(button, said!.Value.IsButton);
        Assert.Equal(text, said.Value.Text);
    }

    [Fact]
    public void A_boarding_box_with_no_words_read_says_nothing() =>
        Assert.Null(Bbs757EfbTopBar.BoardingText("3)"));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_top_bar_texts_do_not_follow_the_pcs_culture(string culture)
    {
        var boarding = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding"));
        var open = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "19-notification-list"));
        var start = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding"));

        Assert.Equal(new[] { "DEP -00:46", "Boarding… 10 min", "Notifications: 10" }, boarding.TopBar.Select(i => i.Text));
        Assert.Equal("Close notifications", Top(open, "top:notifications").Text);
        Assert.Equal("Start boarding", Top(start, "top:boarding").Text);
        Assert.Equal("DEP -00:46", TestCulture.Run(culture, () => Bbs757EfbTopBar.DepartureText("DEP−00:46")));
    }
}
