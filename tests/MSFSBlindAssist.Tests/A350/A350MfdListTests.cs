using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 09 Task 4: a long list on the MFD (OMDB 12R's SIDs, ten under nine rows; measured on 1.2.8, 2026-11-03). The KCCU's
/// DOWN moves a cyan box down the rows, from the first, and once it stands on the last row each press scrolls the list a row; at the
/// list's end DOWN does nothing, and SCROLL_DOWN never moves a list. The scrollbar's thumb, right of the rows, is as long against the
/// rows as the rows are against the list, and as far down.</summary>
public class A350MfdListTests
{
    private static A350DrawCapture Cap(string fixture) => A350Fixtures.Capture(fixture + ".json");

    [Fact]
    public void The_list_reads_its_rows_place_and_highlight()
    {
        Assert.Equal(new MfdList(9, 0, 10, null), A350MfdPage.ListOf(Cap("p09_mfd_departure_sid_top"), 0));
        Assert.Equal(new MfdList(9, 1, 10, 8), A350MfdPage.ListOf(Cap("p09_mfd_departure_sid_end"), 0));
        Assert.Equal(new MfdList(9, 1, 10, 5), A350MfdPage.ListOf(Cap("p09_mfd_departure_sid_up3"), 0));
        Assert.Equal(new MfdList(5, 0, 5, null), A350MfdPage.ListOf(Cap("p09_mfd_approach_list_short"), 0));
        Assert.Null(A350MfdPage.ListOf(Cap("mfd_init_empty"), 0));
    }

    // From the top, eight presses take the box to the last row and one more scrolls the list's one row left.
    [Fact]
    public void PageDown_moves_the_list_a_screen_and_reads_it()
    {
        Assert.Equal(9, A350MfdPage.PageDownPresses(new MfdList(9, 0, 10, null)));
        Assert.Equal(8 - 2 + 9, A350MfdPage.PageDownPresses(new MfdList(9, 0, 30, 2)));   // a screen: nine rows on
        Assert.Equal(3 + 5, A350MfdPage.PageUpPresses(new MfdList(9, 5, 30, 3)));          // up to the top row, then the five above
    }

    // The window reads the list again only once the draw shows it moved, never the screen it had.
    [Fact]
    public void The_window_reads_only_after_the_list_has_moved()
    {
        var top = A350MfdPage.ListOf(Cap("p09_mfd_departure_sid_top"), 0)!.Value;
        var end = A350MfdPage.ListOf(Cap("p09_mfd_departure_sid_end"), 0)!.Value;
        Assert.True(A350MfdPage.ListMoved(top, end));
        Assert.False(A350MfdPage.ListMoved(top, top));
    }

    [Fact]
    public void PageDown_at_the_end_says_end_of_list()
    {
        Assert.Null(A350MfdPage.PageDownPresses(A350MfdPage.ListOf(Cap("p09_mfd_departure_sid_end"), 0)!.Value));
        Assert.Null(A350MfdPage.PageUpPresses(new MfdList(9, 0, 10, 0)));
    }

    [Fact]
    public void A_short_list_sends_no_scroll_key() =>
        Assert.Null(A350MfdPage.PageDownPresses(A350MfdPage.ListOf(Cap("p09_mfd_approach_list_short"), 0)!.Value));

    // The cyan box a sighted pilot follows: the row under it says so.
    [Fact]
    public void The_highlighted_row_says_so()
    {
        var shown = A350MfdWindow.PageLines(Cap("p09_mfd_departure_sid_up3"), 0, dark: false).Shown;
        Assert.Contains("MIRO3G button, highlighted", shown);
        Assert.Single(shown, l => l.Contains("highlighted", StringComparison.Ordinal));
    }
}
