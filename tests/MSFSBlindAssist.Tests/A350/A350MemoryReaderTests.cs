using System.Runtime.InteropServices;
using System.Text;
using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Aircraft.A350.Display.A350MemoryRegions;
using Region = MSFSBlindAssist.Aircraft.A350.Display.A350MemoryRegions.Region;

namespace MSFSBlindAssist.Tests.A350;

// The reader finds the A350's linear memory inside the sim by listing regions (no reading), keeping
// allocations as large as a WASM memory reservation, and checking three anchor strings; it then copies
// only that allocation's readable regions. The Win32 part runs here on the test process itself.
public class A350MemoryReaderTests
{
    private const uint Free = 0x10000, Commit = 0x1000, Reserve = 0x2000, ReadWrite = 0x04, Guard = 0x100, NoAccess = 0x01;

    [Fact]
    public void Allocations_are_spans_from_their_base_and_only_large_ones_are_candidates()
    {
        var regions = new[]
        {
            new Region(0x10000, 0x1000, Free, 0, 0),
            new Region(0x100000, 0x10000, Commit, ReadWrite, 0x100000),
            new Region(0x110000, 0x100000, Reserve, 0, 0x100000),
            new Region(0x210000, 0x20000, Commit, ReadWrite, 0x210000),
        };
        Assert.Equal(new ulong[] { 0x100000 }, Candidates(regions, minSpan: 0x110000));
        Assert.Equal(new ulong[] { 0x100000, 0x210000 }, Candidates(regions, minSpan: 0x20000).OrderBy(x => x));
    }

    [Fact]
    public void Only_committed_readable_regions_of_the_allocation_are_copied()
    {
        var fromBase = new[]
        {
            new Region(0x100000, 0x10000, Commit, ReadWrite, 0x100000),
            new Region(0x110000, 0x10000, Commit, ReadWrite | Guard, 0x100000),
            new Region(0x120000, 0x10000, Commit, NoAccess, 0x100000),
            new Region(0x130000, 0x10000, Reserve, 0, 0x100000),
            new Region(0x140000, 0x8000, Commit, ReadWrite, 0x100000),
            new Region(0x150000, 0x10000, Commit, ReadWrite, 0x150000),   // the next allocation: stop
        };
        Assert.Equal(new[] { (0x100000UL, 0x10000UL), (0x140000UL, 0x8000UL) }, ReadableOf(fromBase, 0x100000).Select(r => (r.Base, r.Size)));
    }

    [Fact]
    public void A_copy_saves_and_loads_the_prototype_format()
    {
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, 4), (65536u, 3) }, new byte[] { 1, 2, 3, 4, 9, 8, 7 });
        var path = Path.Combine(Path.GetTempPath(), "a350copy-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            copy.Save(path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(8 + 4 + 8 + 3, bytes.Length);
            Assert.Equal(65536u, BitConverter.ToUInt32(bytes, 12));
            var back = A350MemoryCopy.Load(path);
            Assert.Equal(copy.Regions, back.Regions);
            Assert.Equal(new byte[] { 9, 8, 7 }, back.RegionBytes(1).ToArray());
            Assert.Equal(65539u, back.End);
        }
        finally { File.Delete(path); }
    }

    // Live: the CustomVariable scan reads through any word that looks like a pointer; one near 4 GB
    // wrapped the bounds check around and threw.
    [Fact]
    public void A_read_past_the_end_of_the_address_space_is_empty()
    {
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, 16) }, new byte[16]);
        Assert.True(copy.Read(0xFFFFFFF8u, 16).IsEmpty);   // wraps to 8, inside the region, in uint arithmetic
        Assert.True(copy.Read(12, 8).IsEmpty);
        Assert.Equal(8, copy.Read(8, 8).Length);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);

    [Fact]
    public void Locates_and_copies_an_allocation_by_its_anchors_in_a_live_process()
    {
        const int size = 64 << 20;
        IntPtr block = VirtualAlloc(IntPtr.Zero, (UIntPtr)size, Commit | Reserve, ReadWrite);
        Assert.NotEqual(IntPtr.Zero, block);
        try
        {
            var anchors = new List<(string, int)> { ("INI_TEST_ANCHOR_ONE", 4096), ("INI_TEST_ANCHOR_TWO", 1 << 20) };
            foreach (var (text, lin) in anchors)
            {
                var b = Encoding.ASCII.GetBytes(text + "\0");
                Marshal.Copy(b, 0, block + lin, b.Length);
            }
            Marshal.WriteInt32(block + (32 << 20), 0x12345678);
            using var reader = new A350MemoryReader(anchors, Environment.ProcessId, minSpan: size);
            Assert.True(reader.TryLocate());
            Assert.Equal((ulong)block, reader.Base);
            Assert.True(reader.IsStillValid());
            var copy = reader.Snapshot();
            Assert.Equal(0x12345678, BitConverter.ToInt32(copy.Read(32 << 20, 4)));
            Assert.Equal("INI_TEST_ANCHOR_TWO", Encoding.ASCII.GetString(copy.Read(1 << 20, 19)));
            Marshal.WriteByte(block + (1 << 20), (byte)'X');
            Assert.False(reader.IsStillValid());
        }
        finally { VirtualFree(block, UIntPtr.Zero, 0x8000); }
    }

    // A350HotPages: the pages that changed since the last copy are read a second time, right after the full
    // copy, so what the A350 is changing comes from one frame even when the full copy spans several.
    [Fact]
    public void A_page_that_changed_since_the_last_copy_is_read_a_second_time()
    {
        const int size = 64 << 20;
        IntPtr block = VirtualAlloc(IntPtr.Zero, (UIntPtr)size, Commit | Reserve, ReadWrite);
        Assert.NotEqual(IntPtr.Zero, block);
        try
        {
            var anchors = new List<(string, int)> { ("INI_TEST_ANCHOR_HOT", 4096) };
            var b = Encoding.ASCII.GetBytes("INI_TEST_ANCHOR_HOT\0");
            Marshal.Copy(b, 0, block + 4096, b.Length);
            using var reader = new A350MemoryReader(anchors, Environment.ProcessId, minSpan: size);
            Assert.True(reader.TryLocate());
            reader.Snapshot();
            Assert.Equal(0, reader.LastReread);
            Marshal.WriteInt32(block + (32 << 20), 42);
            var copy = reader.Snapshot();
            Assert.Equal(A350HotPages.PageSize, reader.LastReread);
            Assert.Equal(42, BitConverter.ToInt32(copy.Read(32 << 20, 4)));
        }
        finally { VirtualFree(block, UIntPtr.Zero, 0x8000); }
    }
}

/// <summary>Review 2026-10-29 (engine M6): a copy's edge cases. The casts from a region to its place in the copy were
/// unchecked (a region past 4 GB, or of 2 GB or more, would land at a wrong address); the copy's end wrapped at 4 GB; and a
/// region that failed to read kept the previous copy's bytes in the draw's memory, so a draw ran on two copies.</summary>
public class A350MemoryCopyEdgeTests
{
    private const ulong Base = 0x7000_0000_0000;
    private static A350MemoryRegions.Region R(ulong offset, ulong size) => new(Base + offset, size, 0x1000, 0x04, Base);

    [Fact]
    public void A_region_inside_the_span_is_one_piece_at_its_linear_address()
    {
        var p = Assert.Single(A350MemoryRegions.Pieces(R(0x1000, 0x2000), Base));
        Assert.Equal((Base + 0x1000, 0x1000u, 0x2000), p);
    }

    [Fact]
    public void A_region_is_clipped_to_the_4_gb_a_wasm_memory_spans()
    {
        Assert.Empty(A350MemoryRegions.Pieces(R(1UL << 32, 0x1000), Base));                     // past the span: none
        var p = Assert.Single(A350MemoryRegions.Pieces(R((1UL << 32) - 0x1000, 0x3000), Base)); // across its end: clipped
        Assert.Equal(0x1000, p.Length);
    }

    [Fact]
    public void A_region_of_2_gb_or_more_is_read_in_pieces_whose_lengths_never_wrap()
    {
        var pieces = A350MemoryRegions.Pieces(R(0, 3UL << 30), Base).ToList();
        Assert.Equal(3, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(A350MemoryRegions.MaxPiece, p.Length));
        Assert.Equal(new uint[] { 0, 1u << 30, 2u << 30 }, pieces.Select(p => p.Linear));
    }

    [Fact]
    public void A_copy_ending_at_4_gb_says_so()
    {
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (uint.MaxValue - 0xFFFu, 0x1000) }, new byte[0x1000]);
        Assert.Equal(1L << 32, copy.End);
    }

    [Fact]
    public void A_region_the_last_copy_held_and_this_one_lacks_is_gone()
    {
        var before = new[] { (0x1000u, 0x1000), (0x4000u, 0x2000), (0x9000u, 0x1000) };
        var now = new[] { (0x1000u, 0x1000), (0x9000u, 0x1000) };
        Assert.Equal(new[] { (0x4000u, 0x2000) }, A350MemoryRegions.Gone(before, now));
        Assert.Empty(A350MemoryRegions.Gone(now, now));
    }
}
