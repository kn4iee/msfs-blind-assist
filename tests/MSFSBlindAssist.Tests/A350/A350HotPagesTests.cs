using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-28: once the A350's heap had grown to 355 MB a full copy took 70-160 ms, many sim frames,
// and nearly every draw trapped on a torn copy; between two copies only about 1% of the pages changed.
// The reader reads those pages again right after the full copy (about a millisecond), so the data the
// A350 is changing comes from one frame.
public class A350HotPagesTests
{
    private const int Page = A350HotPages.PageSize;

    private static (uint, int)[] OneRegion(int pages) => new[] { (0x10000u, pages * Page) };

    private static byte[] Pages(int count, params (int Page, byte Value)[] marks)
    {
        var b = new byte[count * Page];
        foreach (var (p, v) in marks) b[p * Page + 100] = v;
        return b;
    }

    [Fact]
    public void A_first_copy_reads_nothing_again()
    {
        var hot = new A350HotPages();
        Assert.Empty(hot.Track(OneRegion(4), Pages(4)));
    }

    [Fact]
    public void Only_a_page_that_changed_since_the_last_copy_is_read_again()
    {
        var hot = new A350HotPages();
        hot.Track(OneRegion(4), Pages(4));
        Assert.Equal(new[] { (2 * Page, 0x10000u + 2 * Page, Page) }, hot.Track(OneRegion(4), Pages(4, (2, 7))));
    }

    [Fact]
    public void Neighbouring_hot_pages_are_one_run_that_stops_at_a_region_end()
    {
        var regions = new[] { (0x10000u, 2 * Page), (0x90000u, 2 * Page) };
        var hot = new A350HotPages();
        hot.Track(regions, Pages(4));
        // Pages 1 and 2 touch in the buffer but belong to two regions: two reads.
        Assert.Equal(new[] { (Page, 0x10000u + Page, Page), (2 * Page, 0x90000u, 2 * Page) },
            hot.Track(regions, Pages(4, (1, 1), (2, 1), (3, 1))));
    }

    [Fact]
    public void A_page_stays_hot_for_a_few_copies_after_it_changed_then_cools()
    {
        var hot = new A350HotPages();
        hot.Track(OneRegion(2), Pages(2));
        var changed = Pages(2, (0, 5));
        Assert.Single(hot.Track(OneRegion(2), changed));
        for (int i = 1; i < A350HotPages.HotCopies; i++)
            Assert.Single(hot.Track(OneRegion(2), changed));   // a page changing every few seconds is still caught
        Assert.Empty(hot.Track(OneRegion(2), changed));
    }

    [Fact]
    public void A_page_read_again_is_known_by_what_was_read()
    {
        var hot = new A350HotPages();
        hot.Track(OneRegion(1), Pages(1));
        var runs = hot.Track(OneRegion(1), Pages(1, (0, 5)));
        var reread = Pages(1, (0, 6));   // it changed again between the full copy and the second read
        hot.Reread(reread, runs);
        for (int i = 1; i < A350HotPages.HotCopies; i++)
            Assert.Single(hot.Track(OneRegion(1), reread));
        Assert.Empty(hot.Track(OneRegion(1), reread));
    }

    [Fact]
    public void A_new_layout_starts_over()
    {
        var hot = new A350HotPages();
        hot.Track(OneRegion(2), Pages(2));
        Assert.Empty(hot.Track(OneRegion(3), Pages(3, (0, 9))));   // the heap grew: nothing to compare with
        Assert.Single(hot.Track(OneRegion(3), Pages(3, (0, 8))));
    }
}
