using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 tablet screen reader after the team review of 2026-10-27: read-only, and a refresh of an
/// unchanged tablet says the same thing in the same order.
/// </summary>
public class Bbs757EfbReadOnlyTests
{
    private static Bbs757EfbTextLine Line(string text, int x, int y, int h = 12) =>
        new(text, new Rectangle(x, y, 60, h), Array.Empty<Bbs757EfbWord>(), 'A');

    [Fact]
    public void A_pixel_of_wobble_does_not_swap_two_things_on_one_line()
    {
        // With the old 12 px bands, y 11 and y 12 fell in different bands and the right-hand item
        // came FIRST on one read and second on the next.
        foreach (int wobble in new[] { 0, 1, 2 })
        {
            var lines = new[] { Line("RIGHT", 300, 11 + wobble), Line("LEFT", 50, 12 - wobble) };
            var order = Bbs757EfbReadingOrder.Lines(lines).Select(l => l.Text).ToArray();
            Assert.Equal(new[] { "LEFT", "RIGHT" }, order);
        }
    }

    [Fact]
    public void Lines_a_row_apart_stay_in_top_to_bottom_order()
    {
        var lines = new[] { Line("SECOND", 50, 40), Line("FIRST", 300, 20), Line("THIRD", 10, 60) };
        Assert.Equal(new[] { "FIRST", "SECOND", "THIRD" }, Bbs757EfbReadingOrder.Lines(lines).Select(l => l.Text));
    }

    [Fact]
    public void A_column_of_close_lines_is_not_chained_into_one_row()
    {
        // Anchored on a row's first line: 5 px steps must not pull a whole column into one row.
        var lines = new[] { Line("C", 10, 20), Line("A", 200, 0), Line("B", 100, 10) };
        Assert.Equal(new[] { "A", "B", "C" }, Bbs757EfbReadingOrder.Lines(lines, 6).Select(l => l.Text));
    }

    private static Bbs757EfbScreenModel Model(params Bbs757EfbItem[] items) =>
        new("AIRCRAFT", "Ground services", new List<Bbs757EfbItem>
        {
            new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(0, 100, 80, 20), new Point(40, 110), true, "menu:FUEL"),
        }, items.ToList());

    [Fact]
    public void Read_only_offers_only_page_navigation_as_controls()
    {
        var m = Model(
            new Bbs757EfbItem(Bbs757EfbKind.Button, "GPU", new Rectangle(300, 300, 80, 40), new Point(340, 320), true, "btn:GPU"),
            new Bbs757EfbItem(Bbs757EfbKind.Toggle, "Seat belt sign", new Rectangle(300, 400, 80, 20), new Point(340, 410), true, "tgl:SEAT", On: true),
            new Bbs757EfbItem(Bbs757EfbKind.Checkbox, "Chocks", new Rectangle(300, 450, 80, 20), new Point(340, 460), true, "chk:CHOCKS", On: false));
        var built = Bbs757EfbElements.Build(m, null, readOnly: true);

        var clickable = Enumerable.Range(0, built.Count)
            .Where(i => built.Data[$"items.{i}.clickable"] == "true")
            .Select(i => built.Data[$"items.{i}.text"]).ToList();
        Assert.Contains("Home", clickable);
        Assert.Contains("Fuel", clickable);
        Assert.DoesNotContain("GPU", clickable);
        Assert.DoesNotContain(clickable, t => t.StartsWith("Seat belt sign", StringComparison.Ordinal));

        var texts = Enumerable.Range(0, built.Count).Select(i => built.Data[$"items.{i}.text"]).ToList();
        Assert.Contains("Seat belt sign: On", texts);
        Assert.Contains("Chocks: Off", texts);
        Assert.Contains(Bbs757EfbElements.ReadOnlyNote, texts);
        Assert.DoesNotContain(Enumerable.Range(0, built.Count), i => built.Data[$"items.{i}.type"] is "checkbox" or "select" or "range");
    }

    [Fact]
    public void Two_reads_that_say_the_same_have_the_same_fingerprint()
    {
        var a = Model(new Bbs757EfbItem(Bbs757EfbKind.Button, "GPU", new Rectangle(300, 300, 80, 40), new Point(340, 320), true, "btn:GPU"));
        var b = Model(new Bbs757EfbItem(Bbs757EfbKind.Button, "GPU", new Rectangle(301, 299, 80, 40), new Point(341, 319), true, "btn:GPU"));
        var c = Model(new Bbs757EfbItem(Bbs757EfbKind.Button, "GPV", new Rectangle(300, 300, 80, 40), new Point(340, 320), true, "btn:GPV"));
        Assert.Equal(Bbs757EfbPress.Fingerprint(a), Bbs757EfbPress.Fingerprint(b));
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(a), Bbs757EfbPress.Fingerprint(c));
    }

    [Fact]
    public void A_notification_appearing_over_an_unchanged_page_changes_the_fingerprint()
    {
        // The bridge pushes a read only when its fingerprint moved: a notification over a page that did not change must count.
        var button = new Bbs757EfbItem(Bbs757EfbKind.Button, "GPU", new Rectangle(300, 300, 80, 40), new Point(340, 320), true, "btn:GPU");
        var quiet = Model(button);
        var toast = Model(button) with { Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:17Z") } };

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(quiet), Bbs757EfbPress.Fingerprint(toast));
    }
}

public class Bbs757EfbNavigationCheckTests
{
    private static Bbs757EfbScreenModel Page(string app, string section) =>
        new(app, section, new List<Bbs757EfbItem>(), new List<Bbs757EfbItem>());

    [Theory]
    [InlineData("AIRCRAFT", "", "Aircraft", true)]                       // an app tapped on Home: its first section shows
    [InlineData("AIRCRAFT", "FUEL", "Aircraft", true)]
    [InlineData("AIRCRAFT", "FUEL", "Fuel", true)]                       // a section tapped in the menu
    [InlineData("HOME", "", "Home", true)]
    [InlineData("HOME", "", "Aircraft", false)]                          // still the old page: read again
    [InlineData("AIRCRAFT", "FUEL", "Fuel (current page)", true)]
    [InlineData("AIRCRAFT", "GROUND SERVICES", "Fuel", false)]
    public void A_navigation_tap_is_judged_by_the_page_name(string app, string section, string tapped, bool landed)
    {
        Assert.Equal(landed, Bbs757EfbBridge.PageIs(Page(app, section), tapped));
    }
}
