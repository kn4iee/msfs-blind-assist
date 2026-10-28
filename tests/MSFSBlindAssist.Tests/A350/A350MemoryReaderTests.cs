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
