using System.Buffers.Binary;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The 757 FMA reader finds the Captain PFD in the gauge manager by its vptr, follows it to the
/// autoflight and thrust objects, and reads each column's word through the layout.</summary>
public class Bbs757FmaReaderTests
{
    private const ulong Base = 0x20000000;
    private const uint Manager = 0x1000, CaptPfd = 0x4000, FoPfd = 0x4800, Afds = 0x5000, Tmc = 0x6000;
    private const uint PfdVptr = 0x1406c;

    private static readonly Bbs757FmaLayout Layout = new(
        SourceFile: "", AnchorAddress: 0x400, Anchor: new byte[] { 9, 9, 9, 9 },
        ManagerBase: Manager, ManagerSpan: 0x100, PfdVptr: PfdVptr, PfdToAfds: 612, PfdToTmc: 616,
        Pitch: 11924, PitchArmed: 11944, Roll: 11932, RollArmed: 11940, AtMode: 444,
        AtWords: new Bbs757WordTable(new Dictionary<int, string> { [1] = "SPD", [2] = "EPR", [5] = "FLCH" }),
        PitchWords: new Bbs757WordTable(new Dictionary<int, string> { [1] = "TO", [6] = "VNAV PTH" }),
        PitchArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [9] = "G/S" }),
        RollWords: new Bbs757WordTable(new Dictionary<int, string> { [5] = "LNAV", [8] = "TO" }),
        RollArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [3] = "LOC" }));

    private static void Put(byte[] b, uint at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan((int)at), v);

    private static byte[] Memory(int at = 1, int pitch = 1, int pitchArmed = 9, int roll = 5, int rollArmed = 3,
        bool captainVptr = true, bool foFirst = false)
    {
        var b = new byte[0x8000];
        // Manager: a couple of null slots, then the FO pfd then the Captain pfd (or FO first if asked), then
        // the autoflight/thrust pointers hanging off the Captain pfd.
        Put(b, Manager + 0x08, foFirst ? (int)FoPfd : (int)CaptPfd);
        Put(b, Manager + 0x10, foFirst ? (int)CaptPfd : (int)FoPfd);
        Put(b, CaptPfd, captainVptr ? (int)PfdVptr : 0x9999);
        Put(b, FoPfd, (int)PfdVptr);
        Put(b, CaptPfd + 612, (int)Afds);
        Put(b, CaptPfd + 616, (int)Tmc);
        Put(b, FoPfd + 612, (int)Afds);
        Put(b, FoPfd + 616, (int)Tmc);
        Put(b, Tmc + 444, at);
        Put(b, Afds + 11924, pitch);
        Put(b, Afds + 11944, pitchArmed);
        Put(b, Afds + 11932, roll);
        Put(b, Afds + 11940, rollArmed);
        return b;
    }

    private static Bbs757FmaReader Reader(byte[] bytes)
    {
        var src = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        return new Bbs757FmaReader(new ModuleMemory(src, Base), src, Layout);
    }

    [Fact]
    public void Reads_each_column_as_its_word()
    {
        using var r = Reader(Memory());
        Assert.True(r.TryRead(out var fma));
        Assert.False(fma.Corrupt);
        Assert.Equal("SPD", fma.AutoThrottle);
        Assert.Equal("TO", fma.Pitch);
        Assert.Equal("G/S", fma.PitchArmed);
        Assert.Equal("LNAV", fma.Roll);
        Assert.Equal("LOC", fma.RollArmed);
        Assert.Equal(new[] { ("A/T", "SPD"), ("Roll", "LNAV"), ("Pitch", "TO"), ("Roll armed", "LOC"), ("Pitch armed", "G/S") }, fma.Columns());
    }

    [Fact]
    public void A_zero_field_is_blank_and_dropped_from_the_columns()
    {
        using var r = Reader(Memory(pitchArmed: 0, rollArmed: 0));
        Assert.True(r.TryRead(out var fma));
        Assert.Null(fma.PitchArmed);
        Assert.Null(fma.RollArmed);
        Assert.Equal(new[] { ("A/T", "SPD"), ("Roll", "LNAV"), ("Pitch", "TO") }, fma.Columns());
    }

    [Fact]
    public void An_unknown_nonzero_value_reads_as_unrecognised()
    {
        using var r = Reader(Memory(at: 7));
        Assert.True(r.TryRead(out var fma));
        Assert.Equal(Bbs757WordTable.Unrecognised, fma.AutoThrottle);
    }

    [Fact]
    public void The_captain_pfd_is_the_first_vptr_match_not_the_first_officers()
    {
        // FO pfd sits at the lower manager slot; the reader must still pick the Captain's (also a vptr match)
        // only because it is first — so put the Captain's first and the FO's second, and check afds is followed.
        using var r = Reader(Memory());
        Assert.True(r.TryFindPfd(out uint pfd));
        Assert.Equal(CaptPfd, pfd);
    }

    [Fact]
    public void A_negative_field_is_corrupt()
    {
        using var r = Reader(Memory(roll: -5));
        Assert.True(r.TryRead(out var fma));
        Assert.True(fma.Corrupt);
    }

    [Fact]
    public void No_vptr_match_is_a_failed_read()
    {
        using var r = Reader(Memory(captainVptr: false));
        // Only the FO pfd matches now; it is found (also a Captain-equivalent in this fake). Make neither match:
        var bytes = Memory(captainVptr: false);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan((int)FoPfd), 0x1234);
        using var r2 = Reader(bytes);
        Assert.False(r2.TryRead(out _));
    }
}
