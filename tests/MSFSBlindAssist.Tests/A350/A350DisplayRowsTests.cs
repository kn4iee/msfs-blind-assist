using System.Globalization;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26 Task 1: a display window's rows, as the Fenix's display windows make theirs (`FenixChannelDisplayForm.RowsFor`,
/// `RowToFollow`): never empty, one status row "&lt;display&gt;: not available, &lt;reason&gt;" in place of the content, and the pilot's
/// row followed through a refresh by its leading words.</summary>
public class A350DisplayRowsTests
{
    private static readonly A350DrawCapture Capture = new("EFIS", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(),
        Array.Empty<DrawnElement>(), null, 1);

    private static IReadOnlyList<string> Lines(params string[] lines) => lines;

    [Fact]
    public void A_read_display_shows_its_rows() =>
        Assert.Equal(new[] { "FMA: SPEED", "Altitude 3000 feet" }, A350DisplayRows.For("PFD", Capture, dark: false, _ => Lines("FMA: SPEED", "Altitude 3000 feet")));

    [Fact]
    public void A_dark_display_is_off_whatever_its_draw() =>
        Assert.Equal(new[] { "PFD: not available, display off" }, A350DisplayRows.For("PFD", Capture, dark: true, _ => Lines("FMA: SPEED")));

    [Fact]
    public void A_display_not_drawn_yet_is_not_read_yet() =>
        Assert.Equal(new[] { "SD: not available, not read yet" }, A350DisplayRows.For("SD", null, dark: false, _ => Lines("ENG")));

    [Fact]
    public void A_display_drawing_nothing_shows_nothing() =>
        Assert.Equal(new[] { "ISIS: not available, nothing shown" }, A350DisplayRows.For("ISIS", Capture, dark: false, _ => Lines()));

    [Fact]
    public void A_null_row_is_nothing_shown_never_a_crash() =>
        Assert.Equal(new[] { "ISIS: not available, nothing shown" }, A350DisplayRows.For("ISIS", Capture, dark: false, _ => new string[] { "A", null! }));

    [Fact]
    public void A_read_that_throws_is_read_failed_and_reported()
    {
        var reported = new List<string>();
        var rows = A350DisplayRows.For("E/WD", Capture, dark: false, _ => throw new InvalidOperationException("torn"), reported.Add);
        Assert.Equal(new[] { "E/WD: not available, read failed" }, rows);
        Assert.Equal(new[] { "read failed: InvalidOperationException: torn" }, reported);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_status_rows_read_the_same_under_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(new[] { "SND: not available, display off" }, A350DisplayRows.For("SND", Capture, dark: true, _ => Lines()));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_status_row_is_known_as_one()
    {
        Assert.True(A350DisplayRows.IsStatusRow("PFD", Lines("PFD: not available, display off")));
        Assert.False(A350DisplayRows.IsStatusRow("PFD", Lines("PFD: not available, display off", "Altitude 3000 feet")));
        Assert.False(A350DisplayRows.IsStatusRow("PFD", Lines("Altitude 3000 feet")));
        Assert.False(A350DisplayRows.IsStatusRow("ND", Lines("PFD: not available, display off")));
    }

    [Fact]
    public void A_row_whose_value_changed_is_followed_by_its_words() =>
        Assert.Equal(2, A350DisplayRows.RowToFollow(Lines("FMA: SPEED", "Speed 250 knots", "Altitude -64 feet, STD"), "Altitude -63 feet, STD", 1));

    [Fact]
    public void An_exact_row_wins_the_nearest_to_the_cursor() =>
        Assert.Equal(3, A350DisplayRows.RowToFollow(Lines("WPT", "Altitude 3000 feet", "WPT", "WPT"), "WPT", 3));

    [Fact]
    public void A_row_sharing_no_word_is_not_followed() =>
        Assert.Equal(-1, A350DisplayRows.RowToFollow(Lines("FMA: SPEED", "Heading 094"), "Altitude 3000 feet", 0));

    [Fact]
    public void No_row_followed_is_none() => Assert.Equal(-1, A350DisplayRows.RowToFollow(Lines("A"), null, 0));

    [Fact]
    public void The_row_with_more_leading_words_in_common_wins() =>
        Assert.Equal(1, A350DisplayRows.RowToFollow(Lines("Altitude 3000 feet", "Altitude target 5000 feet"), "Altitude target 6000 feet", 0));
}
