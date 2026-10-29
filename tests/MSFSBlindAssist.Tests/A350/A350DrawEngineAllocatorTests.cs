using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-28 most failed draws died inside the A350's own malloc: a memory copy taken across frames tears the
// allocator's free lists, and the draw code's first allocation (a std::string in PFD::DrawFMA) followed one into text
// bytes. On the two copies saved in flight, 7 of 8 draws trapped there (dlmalloc <- malloc <- operator new). The
// engine gives the draw code an allocator of its own in the head room above the copy, so no allocation reads or
// writes the copy's heap. Here the module's allocator traps whenever it is reached, as the A350's does on a torn copy.
public class A350DrawEngineAllocatorTests
{
    private const int VTable = 2008, Object = 5000;

    private static byte[] U32(params uint[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] Float(float f) => BitConverter.GetBytes(f);
    private static byte[] Call(int f) => new byte[] { 0x10 }.Concat(U(f)).ToArray();
    private static byte[] I32Const(long v) => new byte[] { 0x41 }.Concat(S(v)).ToArray();

    private sealed record Fns(int Text, int Malloc, int Free, int Calloc, int Realloc, int Memalign, int PosixMemalign, int Flag,
        int DlMalloc, int DlFree);

    /// <summary>Traps unless local <paramref name="local"/> is a block above the copy (the copy ends at 65536): a NULL
    /// answer, or a block carved out of the copy, fails the draw (review 2026-10-29: without it every test passed with
    /// the allocator answering 0, as address 0 of the restored copy reads back what the draw wrote).</summary>
    private static byte[] AboveCopy(int local) => new byte[] { 0x20, (byte)local }.Concat(I32Const(65536))
        .Concat(new byte[] { 0x49, 0x04, 0x40, 0x00, 0x0B }).ToArray();

    /// <summary>A CTR gauge whose Draw is <paramref name="draw"/> (two i32 locals besides the object), over a module
    /// whose allocator traps when reached. <paramref name="otherSignatures"/>: a version whose free returns a value and
    /// whose internal_memalign keeps dlmalloc's mstate first.</summary>
    private static byte[] Module(Func<Fns, byte[]> draw, bool otherSignatures = false)
    {
        var m = new WasmTestModule();
        int t6 = m.AddType(new[] { I32, I32, I32, I32, I32, I32 }, new[] { I32 });
        int tText = m.AddType(new[] { I32, F32, F32, I32, I32 }, new[] { F32 });
        int tCtx = m.AddType(new[] { I32 }, Array.Empty<byte>());
        int tCtxPtr = m.AddType(new[] { I32, I32 }, Array.Empty<byte>());
        int tCtxFF = m.AddType(new[] { I32, F32, F32 }, Array.Empty<byte>());
        int tCtxF = m.AddType(new[] { I32, F32 }, Array.Empty<byte>());
        int tFrame = m.AddType(new[] { I32, F32, F32, F32, F32, I32 }, Array.Empty<byte>());
        int tWide = m.AddType(new[] { I32, F32, F32, F32, F32, F32, I32 }, Array.Empty<byte>());
        int tXf = m.AddType(new[] { I32, F32, F32, F32, F32, F32, F32 }, Array.Empty<byte>());
        int tI = m.AddType(new[] { I32 }, new[] { I32 });
        int tII = m.AddType(new[] { I32, I32 }, new[] { I32 });
        int tIII = m.AddType(new[] { I32, I32, I32 }, new[] { I32 });
        m.Import("wasi_snapshot_preview1", "path_readlink", t6);
        int flag = m.AddGlobal(0);
        int text = m.Func("nvgText", tText, new byte[] { 0x43, 0, 0, 0, 0 });
        foreach (var (n, t) in new[] { ("nvgFillColor", tCtxPtr), ("nvgSave", tCtx), ("nvgRestore", tCtx), ("nvgTranslate", tCtxFF),
                     ("nvgScale", tCtxFF), ("nvgRotate", tCtxF), ("nvgResetTransform", tCtx), ("nvgReset", tCtx), ("nvgTransform", tXf),
                     ("DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)", tFrame),
                     ("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)", tWide),
                     ("SFD::Draw(int)", tCtxPtr), ("SND::Draw(int)", tCtxPtr), ("nvgBeginFrame", tCtxFF), ("nvgEndFrame", tCtx) })
            m.Func(n, t, Array.Empty<byte>());
        foreach (var g in new[] { "EFIS", "FCU", "HUD", "ISIS", "RMP", "BAT", "MFD", "FMGS", "Systems" })
            m.Func(g + "::Draw()", tCtx, Array.Empty<byte>());
        // The allocator: every function the heap state reaches traps (0x00 unreachable); malloc and free call in.
        int dlmalloc = m.Func("dlmalloc", tI, new byte[] { 0x00 });
        int malloc = m.Func("malloc", tI, new byte[] { 0x20, 0x00 }.Concat(Call(dlmalloc)).ToArray());
        int dlfree = m.Func("dlfree", tCtx, new byte[] { 0x00 });
        int free = otherSignatures ? m.Func("free", tI, new byte[] { 0x00 })
            : m.Func("free", tCtx, new byte[] { 0x20, 0x00 }.Concat(Call(dlfree)).ToArray());
        int calloc = m.Func("calloc", tII, new byte[] { 0x00 });
        int realloc = m.Func("realloc", tII, new byte[] { 0x00 });
        int memalign = m.Func("internal_memalign", otherSignatures ? tIII : tII, new byte[] { 0x00 });
        int posix = m.Func("posix_memalign", tIII, new byte[] { 0x00 });
        var fns = new Fns(text, malloc, free, calloc, realloc, memalign, posix, flag, dlmalloc, dlfree);
        m.Func("CTR::Draw()", tCtx, draw(fns), new byte[] { 0x01, 0x02, I32 });
        m.Data(2000, U32(0, 3000, 11));
        m.Data(3000, U32(1, 3100));
        m.Data(3100, "3CTR\0");
        m.Data(4000, "\0INI_MCDU{}_ENTER\0INI_MCDU_OPTION\0INI_FMC_PAGE1\0");
        return m.Build();
    }

    private static A350MemoryCopy Copy()
    {
        var mem = new byte[65536];
        U32(0, 3000, 11).CopyTo(mem, 2000);
        U32(1, 3100).CopyTo(mem, 3000);
        "3CTR\0"u8.ToArray().CopyTo(mem, 3100);
        U32(VTable, 0).CopyTo(mem, Object);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);
        return copy;
    }

    private static A350DrawEngine Engine(byte[] original)
    {
        var mod = A350WasmModule.Parse(original);
        var engine = A350ModuleCache.CreateEngine();
        var module = Wasmtime.Module.FromBytes(engine, "a350", A350ModulePatcher.Patch(original).Bytes);
        return new A350DrawEngine(engine, module, A350ModulePatcher.UiDrawClassesOf(mod), A350ModuleFacts.From(mod));
    }

    /// <summary>The CTR drawn once (after its warm-up); its texts, failing the test on a trap.</summary>
    private static IReadOnlyList<DrawnText> Draw(Func<Fns, byte[]> draw)
    {
        using var e = Engine(Module(draw));
        e.Load(Copy());
        var cap = e.Draw("CTR");
        Assert.True(cap.Ok, cap.Error + "\n" + e.LastTrap);
        return cap.Texts;
    }

    // nvgText(1, 10, 20, local 1, local 1 + length): the text the allocated block holds.
    private static byte[] TextOfLocal(Fns f, int length) => new byte[] { 0x41, 0x01, 0x43 }.Concat(Float(10)).Append((byte)0x43)
        .Concat(Float(20)).Concat(new byte[] { 0x20, 0x01, 0x20, 0x01 }).Concat(I32Const(length)).Append((byte)0x6A)
        .Concat(Call(f.Text)).Append((byte)0x1A).ToArray();

    // "OK" drawn from a fresh block: the draw went on to its end.
    private static byte[] OkText(Fns f) => I32Const(2).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
        .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(0x4B4F)).Concat(new byte[] { 0x3B, 0x01, 0x00 }).Concat(TextOfLocal(f, 2)).ToArray();

    // local 1 = <allocation>; then a 32-bit word stored at local 1 + offset.
    private static byte[] Store(int offset, uint word) => new byte[] { 0x20, 0x01 }.Concat(I32Const(unchecked((int)word)))
        .Concat(new byte[] { 0x36, 0x02 }).Concat(U(offset)).ToArray();

    // Also dlmalloc and dlfree called directly (on 1.2.7 only patched functions call them, but a version may not), and
    // a second block that lies after the first, never over it.
    [Fact]
    public void Malloc_and_free_never_reach_the_copys_own_heap()
    {
        var texts = Draw(f => I32Const(8).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
            .Concat(Store(0, 0x4C4C4548))                                                        // "HELL"
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const('O')).Concat(new byte[] { 0x3A, 0x00, 0x04 })   // 'O' at +4
            .Concat(I32Const(8)).Concat(Call(f.DlMalloc)).Concat(new byte[] { 0x21, 0x02 }).Concat(AboveCopy(2))
            .Concat(new byte[] { 0x20, 0x02, 0x20, 0x01 }).Concat(I32Const(8)).Concat(new byte[] { 0x6A, 0x49, 0x04, 0x40, 0x00, 0x0B })   // second < first + 8: trap
            .Concat(new byte[] { 0x20, 0x02 }).Concat(Call(f.DlFree))
            .Concat(TextOfLocal(f, 5))
            .Concat(new byte[] { 0x20, 0x01 }).Concat(Call(f.Free)).ToArray());
        Assert.Equal("HELLO", Assert.Single(texts).Text);
    }

    // The allocator starts again at every draw (the warm-up's blocks are reused), so calloc must clear what it hands out.
    [Fact]
    public void Calloc_clears_a_block_an_earlier_draw_wrote()
    {
        var texts = Draw(f => new byte[] { 0x23, (byte)f.Flag, 0x45, 0x04, 0x40 }                      // first call (the warm-up):
            .Concat(I32Const(1)).Concat(new byte[] { 0x24, (byte)f.Flag })
            .Concat(I32Const(8)).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
            .Concat(Store(0, 0x58585858)).Concat(Store(4, 0x58585858))                            // "XXXXXXXX"
            .Concat(new byte[] { 0x0F, 0x0B })                                                    // return; end
            .Concat(I32Const(1)).Concat(I32Const(8)).Concat(Call(f.Calloc)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
            .Concat(TextOfLocal(f, 8)).ToArray());
        Assert.Equal(new string('\0', 8), Assert.Single(texts).Text);
    }

    [Fact]
    public void Realloc_keeps_what_the_block_held()
    {
        var texts = Draw(f => I32Const(4).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
            .Concat(Store(0, 0x44434241))                                                         // "ABCD"
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(64)).Concat(Call(f.Realloc)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
            .Concat(TextOfLocal(f, 4)).ToArray());
        Assert.Equal("ABCD", Assert.Single(texts).Text);
    }

    // internal_memalign(64, 8) and posix_memalign(&p, 128, 8): the pointers honour the alignment (drawn as "OK").
    [Fact]
    public void Aligned_allocations_are_aligned()
    {
        var ok = I32Const(0x4B4F);   // "OK"
        var texts = Draw(f => I32Const(64).Concat(I32Const(8)).Concat(Call(f.Memalign)).Concat(new byte[] { 0x21, 0x01 }).Concat(AboveCopy(1))
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(63)).Append((byte)0x71).Append((byte)0x45)  // (p & 63) == 0
            .Concat(new byte[] { 0x04, 0x40 })
            .Concat(I32Const(6100)).Concat(I32Const(128)).Concat(I32Const(8)).Concat(Call(f.PosixMemalign))
            .Concat(new byte[] { 0x04, 0x40, 0x00, 0x0B })                                        // posix_memalign != 0: trap
            .Concat(I32Const(6100)).Concat(new byte[] { 0x28, 0x02, 0x00, 0x21, 0x01 }).Concat(AboveCopy(1))   // local 1 = *(6100)
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(127)).Append((byte)0x71).Append((byte)0x45)
            .Concat(new byte[] { 0x04, 0x40 })
            .Concat(new byte[] { 0x20, 0x01 }).Concat(ok).Concat(new byte[] { 0x3B, 0x01, 0x00 })     // i32.store16 "OK"
            .Concat(TextOfLocal(f, 2))
            .Concat(new byte[] { 0x0B, 0x0B }).ToArray());
        Assert.Equal("OK", Assert.Single(texts).Text);
    }

    // calloc(0xFFFFFFFF, 0xFFFFFFFF): the size overflows 64 bits and was answered with a 1-byte block (review 2026-10-29);
    // C's calloc answers NULL when count times size does not fit.
    [Fact]
    public void Calloc_of_a_size_that_does_not_fit_fails()
    {
        var texts = Draw(f => I32Const(-1).Concat(I32Const(-1)).Concat(Call(f.Calloc))
            .Concat(new byte[] { 0x04, 0x40, 0x00, 0x0B })                                        // calloc != 0: trap
            .Concat(OkText(f)).ToArray());
        Assert.Equal("OK", Assert.Single(texts).Text);
    }

    // posix_memalign with its pointer's address outside the memory: the write failed inside the host, which answered 0
    // (success) with nothing written (review 2026-10-29). It fails with ENOMEM (48), as when the head room is used up.
    [Fact]
    public void Posix_memalign_to_an_address_out_of_the_memory_fails()
    {
        var texts = Draw(f => I32Const(-16).Concat(I32Const(16)).Concat(I32Const(8)).Concat(Call(f.PosixMemalign))
            .Concat(I32Const(48)).Append((byte)0x47)                                                // i32.ne
            .Concat(new byte[] { 0x04, 0x40, 0x00, 0x0B })                                        // != 48: trap
            .Concat(OkText(f)).ToArray());
        Assert.Equal("OK", Assert.Single(texts).Text);
    }

    // A draw asking for more than the head room (MFD::Draw reaches OANSAirportRenderer::allocateSharedBuffer) gets NULL,
    // and the engine says so, with the heap the draw used (review 2026-10-29: it was silent, and repeats every draw).
    [Fact]
    public void A_draw_that_runs_out_of_head_room_says_so()
    {
        using (var e = Engine(Module(f => I32Const(0x7FFFFFF0).Concat(Call(f.Malloc)).Append((byte)0x1A).Concat(OkText(f)).ToArray())))
        {
            e.Load(Copy());
            var cap = e.Draw("CTR");
            Assert.True(cap.Ok, cap.Error + "\n" + e.LastTrap);
            Assert.True(e.LastHeapExhausted);
        }
        using (var e = Engine(Module(OkText)))
        {
            e.Load(Copy());
            Assert.True(e.Draw("CTR").Ok);
            Assert.False(e.LastHeapExhausted);
            Assert.InRange(e.LastHeapBytes, 2, 1 << 20);
        }
    }

    // A version whose allocator functions take other arguments (dlmalloc's mstate kept in internal_memalign) or return
    // a value (a free returning one) is left alone there: a bare return where a result is due does not even compile,
    // and an answer reading shifted arguments would hand out wrong blocks (review 2026-10-29). The rest is answered.
    [Fact]
    public void An_allocator_function_with_another_signature_is_left_alone()
    {
        var patched = A350ModulePatcher.Patch(Module(OkText, otherSignatures: true));
        using var engine = A350ModuleCache.CreateEngine();
        using var module = Wasmtime.Module.FromBytes(engine, "a350", patched.Bytes);   // valid: compiles
        Assert.Equal(new[] { "calloc", "dlfree", "dlmalloc", "malloc", "posix_memalign", "realloc" },
            patched.Allocator.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { "free", "internal_memalign" }, patched.AllocatorLeftAlone.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(8, A350ModulePatcher.Patch(Module(OkText)).Allocator.Count);   // the usual signatures: every one answered
    }
}
