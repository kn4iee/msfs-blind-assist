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

    private static byte[] Module(bool firstDrawPartial = false, bool clip = false)
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
        int tRect = m.AddType(new[] { I32, F32, F32, F32, F32 }, Array.Empty<byte>());
        m.Import("wasi_snapshot_preview1", "path_readlink", t6);
        int guard = m.AddGlobal(0);
        int text = m.Func("nvgText", tText, new byte[] { 0x43, 0, 0, 0, 0 });
        int scissor = m.Func("nvgScissor", tRect, Array.Empty<byte>());
        foreach (var (n, t) in new[] { ("nvgFillColor", tCtxPtr), ("nvgSave", tCtx), ("nvgRestore", tCtx), ("nvgTranslate", tCtxFF),
                     ("nvgScale", tCtxFF), ("nvgRotate", tCtxF), ("nvgResetTransform", tCtx), ("nvgReset", tCtx), ("nvgTransform", tXf),
                     ("nvgResetScissor", tCtx),
                     ("DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)", tFrame),
                     ("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)", tWide),
                     ("SFD::Draw(int)", tCtxPtr), ("SND::Draw(int)", tCtxPtr), ("nvgBeginFrame", tCtxFF), ("nvgEndFrame", tCtx) })
            m.Func(n, t, Array.Empty<byte>());
        foreach (var g in new[] { "EFIS", "FCU", "HUD", "ISIS", "RMP", "BAT", "MFD", "FMGS", "Systems" })
            m.Func(g + "::Draw()", tCtx, Array.Empty<byte>());
        var body = new List<byte>();
        if (firstDrawPartial)
        {
            // Live: the first draw on a fresh instance drew part of the E/WD. This gauge draws "PART" on
            // its first call (it initialises the guard global) and "HELLO" after.
            body.AddRange(new byte[] { 0x23, (byte)guard, 0x45, 0x04, 0x40, 0x41, 0x01, 0x24, (byte)guard, 0x41, 0x01, 0x43 });
            body.AddRange(Float(10)); body.Add(0x43); body.AddRange(Float(20));
            body.Add(0x41); body.AddRange(S(Hello + 10)); body.Add(0x41); body.AddRange(S(Hello + 14));
            body.Add(0x10); body.AddRange(U(text)); body.Add(0x1A);
            body.AddRange(new byte[] { 0x0F, 0x0B });   // return; end
            body.Add(0x41); body.Add(0x01);
            body.Add(0x43); body.AddRange(Float(10)); body.Add(0x43); body.AddRange(Float(20));
            body.Add(0x41); body.AddRange(S(Hello)); body.Add(0x41); body.AddRange(S(Hello + 5));
            body.Add(0x10); body.AddRange(U(text)); body.Add(0x1A);
        }
        else body.AddRange(new byte[]
        {
            0x23, (byte)guard, 0x04, 0x40, 0x00, 0x0B,        // a guard left set by a trap: trap again
            0x41, 0x01, 0x24, (byte)guard,                    // guard = 1
            0x20, 0x00, 0x28, 0x02, 0x04, 0x04, 0x40, 0x00, 0x0B,   // the object's flag set: trap
            0x41, 0x01,                                       // ctx 1
        });
        if (clip)
        {
            // nvgScissor(ctx 1, 0, 0, 5, 5): the text at (10, 20) is outside it.
            body.Add(0x41); body.Add(0x01);
            foreach (var f in new[] { 0f, 0f, 5f, 5f }) { body.Add(0x43); body.AddRange(Float(f)); }
            body.Add(0x10); body.AddRange(U(scissor));
        }
        if (!firstDrawPartial)
        {
            body.Add(0x43); body.AddRange(Float(10));
            body.Add(0x43); body.AddRange(Float(20));
            body.Add(0x41); body.AddRange(S(Hello));
            body.Add(0x41); body.AddRange(S(Hello + 5));
            body.Add(0x10); body.AddRange(U(text)); body.Add(0x1A);
            body.AddRange(new byte[] { 0x41, 0x00, 0x24, (byte)guard });   // guard = 0
        }
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
        "PART"u8.ToArray().CopyTo(mem, Hello + 10);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);
        return copy;
    }

    private static A350DrawEngine Engine(bool firstDrawPartial = false, bool clip = false)
    {
        var original = Module(firstDrawPartial, clip);
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
        Assert.False(t.Clipped);
    }

    // The PFD clips its tapes (setClipArea -> nvgScissor); a text drawn outside the clip is not on the screen and says so
    // (live 2026-10-30: the altitude tape's labels scrolled through the FMA band and were read as the approach column).
    [Fact]
    public void A_text_drawn_outside_the_scissor_is_marked_clipped()
    {
        using var e = Engine(clip: true);
        e.Load(Copy(trap: false));
        var cap = e.Draw("CTR");
        Assert.True(cap.Ok, cap.Error);
        var t = Assert.Single(cap.Texts);
        Assert.Equal("HELLO", t.Text);
        Assert.True(t.Clipped);
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

    // Live 2026-10-28 the capture's one-line error ("trap: error while executing at wasm backtrace:") hid where the
    // draw died; the whole message showed the A350's own malloc faulting on a torn copy. The capture's error stays one
    // line, and the engine keeps the last trap's whole message, its wasm backtrace included, for diagnostics.
    [Fact]
    public void The_last_trap_keeps_its_whole_backtrace()
    {
        using var e = Engine();
        e.Load(Copy(trap: true));
        var bad = e.Draw("CTR");
        Assert.DoesNotContain('\n', bad.Error!);
        Assert.Contains("CTR::Draw()", e.LastTrap);
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

    // A CustomVariable {u32 id, std::string name (short: inline, length in byte 11), f64 value at +16}
    // answers the draw code's L:var reads from the copy. The engine keeps their addresses between copies
    // and rescans only when one no longer checks out: a variable that moved is found again.
    private static A350MemoryCopy CopyWithVariable(int at, double value)
    {
        var copy = Copy(trap: false);
        var mem = copy.Buffer;
        BitConverter.GetBytes(77u).CopyTo(mem, at);
        "INI_TESTV"u8.ToArray().CopyTo(mem, at + 4);
        mem[at + 4 + 11] = 9;
        BitConverter.GetBytes(value).CopyTo(mem, at + 16);
        return copy;
    }

    [Fact]
    public void A_custom_variable_is_found_and_found_again_after_it_moves()
    {
        using var e = Engine();
        e.Load(CopyWithVariable(8000, 1.5));
        Assert.Equal(("INI_TESTV", 8000u), e.CustomVariable(77));
        Assert.Equal(1.5, e.LVarValue(77));
        e.Load(CopyWithVariable(8000, 2.5));
        Assert.Equal(2.5, e.LVarValue(77));
        e.Load(CopyWithVariable(9000, 3.5));
        Assert.Equal(("INI_TESTV", 9000u), e.CustomVariable(77));
        Assert.Equal(3.5, e.LVarValue(77));
    }

    [Fact]
    public void The_first_draw_on_a_fresh_instance_is_a_warm_up_never_returned()
    {
        using var e = Engine(firstDrawPartial: true);
        e.Load(Copy(trap: false));
        Assert.Equal("HELLO", Assert.Single(e.Draw("CTR").Texts).Text);
        Assert.Equal("HELLO", Assert.Single(e.Draw("CTR").Texts).Text);
    }
    // Perf review 2026-10-30: each gauge class was looked for in its own pass over the copy, about 400 ms each at 339 MB
    // (3.1 s for the classes a tick asks for after a load). One pass finds every class's objects, each list in address
    // order across the copy's regions, and a word that is no class's vtable is no object.
    [Fact]
    public void One_pass_finds_every_classs_objects_in_address_order()
    {
        var mem = new byte[4096];
        void Word(int at, uint v) => BitConverter.GetBytes(v).CopyTo(mem, at);
        Word(16, 700); Word(64, 800); Word(128, 700); Word(200, 750);   // 750 lies between the vtables but is none
        Word(2048 + 32, 900); Word(2048 + 96, 800);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (1000u, 2048), (9000u, 2048) }, mem);
        var classOf = new Dictionary<uint, string> { [700] = "CTR", [800] = "MFD", [900] = "EFIS", [901] = "EFIS" };
        var found = A350DrawEngine.FindObjects(copy, classOf);
        Assert.Equal(new[] { 1016, 1128 }, found["CTR"]);
        Assert.Equal(new[] { 1064, 9096 }, found["MFD"]);
        Assert.Equal(new[] { 9032 }, found["EFIS"]);
        Assert.Equal(3, found.Count);
    }
}
