using MSFSBlindAssist.Aircraft.A350.Display;
using Wasmtime;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// The engine end to end on a hand-built "A350": a CTR object in the memory copy (found by its vtable),
// a CTR::Draw that draws "HELLO" through nvgText, and that traps when a flag in the copy is set. Like
// the A350's stack pointer after a trap, its guard global is left set, so a draw after a trap only
// works on a fresh instance (live: after one trapped draw on a torn copy, every later draw trapped).
public class A350DrawEngineTests
{
    private const int VTable = 2008, Object = 5000, Hello = 6000;

    private static byte[] Float(float f) => BitConverter.GetBytes(f);
    private static byte[] U32(params uint[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();

    private static byte[] Module()
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
        m.Import("wasi_snapshot_preview1", "path_readlink", t6);
        int guard = m.AddGlobal(0);
        int text = m.Func("nvgText", tText, new byte[] { 0x43, 0, 0, 0, 0 });
        foreach (var (n, t) in new[] { ("nvgFillColor", tCtxPtr), ("nvgSave", tCtx), ("nvgRestore", tCtx), ("nvgTranslate", tCtxFF),
                     ("nvgScale", tCtxFF), ("nvgRotate", tCtxF), ("nvgResetTransform", tCtx), ("nvgReset", tCtx), ("nvgTransform", tXf),
                     ("DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)", tFrame),
                     ("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)", tWide),
                     ("SFD::Draw(int)", tCtxPtr), ("SND::Draw(int)", tCtxPtr), ("nvgBeginFrame", tCtxFF), ("nvgEndFrame", tCtx) })
            m.Func(n, t, Array.Empty<byte>());
        foreach (var g in new[] { "EFIS", "FCU", "HUD", "ISIS", "RMP", "BAT", "MFD", "FMGS", "Systems" })
            m.Func(g + "::Draw()", tCtx, Array.Empty<byte>());
        var body = new List<byte>
        {
            0x23, (byte)guard, 0x04, 0x40, 0x00, 0x0B,        // a guard left set by a trap: trap again
            0x41, 0x01, 0x24, (byte)guard,                    // guard = 1
            0x20, 0x00, 0x28, 0x02, 0x04, 0x04, 0x40, 0x00, 0x0B,   // the object's flag set: trap
            0x41, 0x01,                                       // ctx 1
        };
        body.Add(0x43); body.AddRange(Float(10));
        body.Add(0x43); body.AddRange(Float(20));
        body.Add(0x41); body.AddRange(S(Hello));
        body.Add(0x41); body.AddRange(S(Hello + 5));
        body.Add(0x10); body.AddRange(U(text)); body.Add(0x1A);
        body.AddRange(new byte[] { 0x41, 0x00, 0x24, (byte)guard });   // guard = 0
        m.Func("CTR::Draw()", tCtx, body.ToArray());
        m.Data(2000, U32(0, 3000, 11));
        m.Data(3000, U32(1, 3100));
        m.Data(3100, "3CTR\0");
        m.Data(4000, "\0INI_MCDU{}_ENTER\0INI_MCDU_OPTION\0INI_FMC_PAGE1\0");
        return m.Build();
    }

    private static A350MemoryCopy Copy(bool trap)
    {
        var mem = new byte[65536];
        U32(0, 3000, 11).CopyTo(mem, 2000);
        U32(1, 3100).CopyTo(mem, 3000);
        "3CTR\0"u8.ToArray().CopyTo(mem, 3100);
        U32(VTable, trap ? 1u : 0u).CopyTo(mem, Object);
        "HELLO"u8.ToArray().CopyTo(mem, Hello);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);
        return copy;
    }

    private static A350DrawEngine Engine()
    {
        var original = Module();
        var mod = A350WasmModule.Parse(original);
        var engine = A350ModuleCache.CreateEngine();
        var module = Wasmtime.Module.FromBytes(engine, "a350", A350ModulePatcher.Patch(original).Bytes);
        return new A350DrawEngine(engine, module, A350ModulePatcher.UiDrawClassesOf(mod), A350ModuleFacts.From(mod));
    }

    [Fact]
    public void A_gauge_is_found_by_its_vtable_and_its_text_captured()
    {
        using var e = Engine();
        e.Load(Copy(trap: false));
        Assert.Equal(new[] { Object }, e.Objects("CTR"));
        var cap = e.Draw("CTR");
        Assert.True(cap.Ok, cap.Error);
        var t = Assert.Single(cap.Texts);
        Assert.Equal(("HELLO", 10f, 20f), (t.Text, t.X, t.Y));
    }

    [Fact]
    public void A_trap_is_the_captures_error_and_the_next_draw_works()
    {
        using var e = Engine();
        e.Load(Copy(trap: true));
        var bad = e.Draw("CTR");
        Assert.False(bad.Ok);
        Assert.StartsWith("trap", bad.Error);
        e.Load(Copy(trap: false));
        var good = e.Draw("CTR");
        Assert.True(good.Ok, good.Error);
        Assert.Equal("HELLO", Assert.Single(good.Texts).Text);
    }

    [Fact]
    public void A_missing_gauge_object_is_an_error_not_a_throw()
    {
        using var e = Engine();
        e.Load(Copy(trap: false));
        var cap = e.Draw("EFIS", 1);
        Assert.False(cap.Ok);
        Assert.Contains("no EFIS object", cap.Error);
    }
}
