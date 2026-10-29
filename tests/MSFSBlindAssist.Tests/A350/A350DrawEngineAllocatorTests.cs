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

    private sealed record Fns(int Text, int Malloc, int Free, int Calloc, int Realloc, int Memalign, int PosixMemalign, int Flag);

    /// <summary>A CTR gauge whose Draw is <paramref name="draw"/> (one i32 local besides the object), over a module
    /// whose allocator traps when reached.</summary>
    private static byte[] Module(Func<Fns, byte[]> draw)
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
        int free = m.Func("free", tCtx, new byte[] { 0x20, 0x00 }.Concat(Call(dlfree)).ToArray());
        int calloc = m.Func("calloc", tII, new byte[] { 0x00 });
        int realloc = m.Func("realloc", tII, new byte[] { 0x00 });
        int memalign = m.Func("internal_memalign", tII, new byte[] { 0x00 });
        int posix = m.Func("posix_memalign", tIII, new byte[] { 0x00 });
        var fns = new Fns(text, malloc, free, calloc, realloc, memalign, posix, flag);
        m.Func("CTR::Draw()", tCtx, draw(fns), new byte[] { 0x01, 0x01, I32 });
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

    // local 1 = <allocation>; then a 32-bit word stored at local 1 + offset.
    private static byte[] Store(int offset, uint word) => new byte[] { 0x20, 0x01 }.Concat(I32Const(unchecked((int)word)))
        .Concat(new byte[] { 0x36, 0x02 }).Concat(U(offset)).ToArray();

    [Fact]
    public void Malloc_and_free_never_reach_the_copys_own_heap()
    {
        var texts = Draw(f => I32Const(8).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 })
            .Concat(Store(0, 0x4C4C4548))                                                        // "HELL"
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const('O')).Concat(new byte[] { 0x3A, 0x00, 0x04 })   // 'O' at +4
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
            .Concat(I32Const(8)).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 })
            .Concat(Store(0, 0x58585858)).Concat(Store(4, 0x58585858))                            // "XXXXXXXX"
            .Concat(new byte[] { 0x0F, 0x0B })                                                    // return; end
            .Concat(I32Const(1)).Concat(I32Const(8)).Concat(Call(f.Calloc)).Concat(new byte[] { 0x21, 0x01 })
            .Concat(TextOfLocal(f, 8)).ToArray());
        Assert.Equal(new string('\0', 8), Assert.Single(texts).Text);
    }

    [Fact]
    public void Realloc_keeps_what_the_block_held()
    {
        var texts = Draw(f => I32Const(4).Concat(Call(f.Malloc)).Concat(new byte[] { 0x21, 0x01 })
            .Concat(Store(0, 0x44434241))                                                         // "ABCD"
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(64)).Concat(Call(f.Realloc)).Concat(new byte[] { 0x21, 0x01 })
            .Concat(TextOfLocal(f, 4)).ToArray());
        Assert.Equal("ABCD", Assert.Single(texts).Text);
    }

    // internal_memalign(64, 8) and posix_memalign(&p, 128, 8): the pointers honour the alignment (drawn as "OK").
    [Fact]
    public void Aligned_allocations_are_aligned()
    {
        var ok = I32Const(0x4B4F);   // "OK"
        var texts = Draw(f => I32Const(64).Concat(I32Const(8)).Concat(Call(f.Memalign)).Concat(new byte[] { 0x21, 0x01 })
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(63)).Append((byte)0x71).Append((byte)0x45)  // (p & 63) == 0
            .Concat(new byte[] { 0x04, 0x40 })
            .Concat(I32Const(6100)).Concat(I32Const(128)).Concat(I32Const(8)).Concat(Call(f.PosixMemalign)).Append((byte)0x1A)
            .Concat(I32Const(6100)).Concat(new byte[] { 0x28, 0x02, 0x00, 0x21, 0x01 })            // local 1 = *(6100)
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(127)).Append((byte)0x71).Append((byte)0x45)
            .Concat(new byte[] { 0x04, 0x40 })
            .Concat(new byte[] { 0x20, 0x01 }).Concat(ok).Concat(new byte[] { 0x3B, 0x01, 0x00 })     // i32.store16 "OK"
            .Concat(TextOfLocal(f, 2))
            .Concat(new byte[] { 0x0B, 0x0B }).ToArray());
        Assert.Equal("OK", Assert.Single(texts).Text);
    }
}
