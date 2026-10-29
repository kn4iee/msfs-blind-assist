using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// Review 2026-10-29 (critical): a draw had no time limit, and the engine's own stubs gave the A350's libc a way to loop
// forever. Every import the engine did not answer returned 0 ("success") and wrote none of its out-parameters, so musl's
// __stdio_write (fd_write until all is written) and wasi-libc's preopen scan (fd_prestat_get until EBADF) could spin on
// a stale stack word; the A350's MFD and EFIS draws reach both through the OANS profiler (ScopedTimer -> recordSpike ->
// fprintf, ofstream). A hung draw stopped every display read for the rest of the session, unannounced. Each call of a
// Draw now runs under a deadline, and the stubs answer as a sandbox with no files, no environment and a real clock.
public class A350DrawEngineStubTests
{
    private const int VTable = 2008, Object = 5000, Hello = 6000;

    private static byte[] Float(float f) => BitConverter.GetBytes(f);
    private static byte[] U32(params uint[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] Call(int f) => new byte[] { 0x10 }.Concat(U(f)).ToArray();
    private static byte[] I32Const(long v) => new byte[] { 0x41 }.Concat(S(v)).ToArray();
    private static byte[] I64Const(long v) => new byte[] { 0x42 }.Concat(S(v)).ToArray();
    private static readonly byte[] TrapIfTrue = { 0x04, 0x40, 0x00, 0x0B };   // if; unreachable; end

    /// <summary>The imports the tests call, by name: (module, parameter types, result types).</summary>
    private static readonly (string Module, string Name, byte[] Params, byte[] Results)[] Imports =
    {
        ("wasi_snapshot_preview1", "fd_write", new[] { I32, I32, I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "fd_read", new[] { I32, I32, I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "fd_seek", new[] { I32, I64, I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "fd_fdstat_get", new[] { I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "fd_prestat_get", new[] { I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "fd_close", new[] { I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "path_open", new[] { I32, I32, I32, I32, I32, I64, I64, I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "path_filestat_get", new[] { I32, I32, I32, I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "poll_oneoff", new[] { I32, I32, I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "clock_time_get", new[] { I32, I64, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "environ_sizes_get", new[] { I32, I32 }, new[] { I32 }),
        ("wasi_snapshot_preview1", "random_get", new[] { I32, I32 }, new[] { I32 }),
        ("env", "SimConnect_GetNextDispatch", new[] { I64, I32, I32 }, new[] { I32 }),
        ("env", "aircraft_varget", new[] { I32, I32, I32 }, new[] { F64 }),
    };

    /// <summary>An import's function index: the hook (path_readlink) is import 0, the table above follows it.</summary>
    private static int Imp(string name) => 1 + Array.FindIndex(Imports, i => i.Name == name);

    /// <summary>A call of an import with every argument zero.</summary>
    private static byte[] CallWithZeros(string name) =>
        Imports.First(i => i.Name == name).Params.SelectMany(p => p switch
        {
            I64 => I64Const(0), F32 => new byte[] { 0x43 }.Concat(Float(0)), F64 => new byte[] { 0x44 }.Concat(new byte[8]),
            _ => I32Const(0),
        }).Concat(Call(Imp(name))).ToArray();

    /// <summary>A CTR gauge whose Draw is <paramref name="draw"/> (one i32 local besides the object), given nvgText's
    /// function index; <paramref name="start"/>, when given, runs as the module is instantiated.</summary>
    private static byte[] Module(Func<int, byte[]> draw, byte[]? start = null)
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
        foreach (var (module, name, ps, rs) in Imports)
            m.Import(module, name, m.AddType(ps, rs));
        int text = m.Func("nvgText", tText, new byte[] { 0x43, 0, 0, 0, 0 });
        foreach (var (n, t) in new[] { ("nvgFillColor", tCtxPtr), ("nvgSave", tCtx), ("nvgRestore", tCtx), ("nvgTranslate", tCtxFF),
                     ("nvgScale", tCtxFF), ("nvgRotate", tCtxF), ("nvgResetTransform", tCtx), ("nvgReset", tCtx), ("nvgTransform", tXf),
                     ("DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)", tFrame),
                     ("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)", tWide),
                     ("SFD::Draw(int)", tCtxPtr), ("SND::Draw(int)", tCtxPtr), ("nvgBeginFrame", tCtxFF), ("nvgEndFrame", tCtx) })
            m.Func(n, t, Array.Empty<byte>());
        foreach (var g in new[] { "EFIS", "FCU", "HUD", "ISIS", "RMP", "BAT", "MFD", "FMGS", "Systems" })
            m.Func(g + "::Draw()", tCtx, Array.Empty<byte>());
        m.Func("CTR::Draw()", tCtx, draw(text), new byte[] { 0x01, 0x01, I32 });
        if (start != null) m.Start(m.Func("start", m.AddType(Array.Empty<byte>(), Array.Empty<byte>()), start));
        m.Data(2000, U32(0, 3000, 11));
        m.Data(3000, U32(1, 3100));
        m.Data(3100, "3CTR\0");
        m.Data(4000, "\0INI_MCDU{}_ENTER\0INI_MCDU_OPTION\0INI_FMC_PAGE1\0");
        return m.Build();
    }

    /// <summary>The copy: the CTR object (its flag at +4), "HELLO", and all ones at 8300 to 8307.</summary>
    private static A350MemoryCopy Copy(uint flag = 0)
    {
        var mem = new byte[65536];
        U32(0, 3000, 11).CopyTo(mem, 2000);
        U32(1, 3100).CopyTo(mem, 3000);
        "3CTR\0"u8.ToArray().CopyTo(mem, 3100);
        U32(VTable, flag).CopyTo(mem, Object);
        "HELLO"u8.ToArray().CopyTo(mem, Hello);
        U32(uint.MaxValue, uint.MaxValue).CopyTo(mem, 8300);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);
        return copy;
    }

    private static A350DrawEngine Engine(Func<int, byte[]> draw, byte[]? start = null)
    {
        var original = Module(draw, start);
        var mod = A350WasmModule.Parse(original);
        var engine = A350ModuleCache.CreateEngine();
        var module = Wasmtime.Module.FromBytes(engine, "a350", A350ModulePatcher.Patch(original).Bytes);
        return new A350DrawEngine(engine, module, A350ModulePatcher.UiDrawClassesOf(mod), A350ModuleFacts.From(mod));
    }

    /// <summary>The CTR drawn once on a fresh copy; the capture, failing the test on a trap.</summary>
    private static A350DrawCapture Drawn(A350DrawEngine e)
    {
        e.Load(Copy());
        var cap = e.Draw("CTR");
        Assert.True(cap.Ok, cap.Error + "\n" + e.LastTrap);
        return cap;
    }

    [Fact]
    public void A_draw_that_never_ends_is_cut_off_at_its_deadline_and_the_next_one_works()
    {
        // local.get 0; i32.load offset=4; if; loop; br 0; end; end: spins while the object's flag is set.
        var e = Engine(_ => new byte[] { 0x20, 0x00, 0x28, 0x02, 0x04, 0x04, 0x40, 0x03, 0x40, 0x0C, 0x00, 0x0B, 0x0B });
        e.DrawDeadline = TimeSpan.FromMilliseconds(200);
        e.Load(Copy(flag: 1));
        var draw = Task.Run(() => e.Draw("CTR"));
        Assert.True(draw.Wait(TimeSpan.FromSeconds(10)), "the draw never ended");   // left running, so left undisposed
        try
        {
            Assert.False(draw.Result.Ok);
            Assert.Contains("deadline", draw.Result.Error);
            e.Load(Copy(flag: 0));
            Assert.True(e.Draw("CTR").Ok);
        }
        finally { e.Dispose(); }
    }

    // musl's __stdio_write calls writev until all is written, wasi-libc's preopen scan asks fd_prestat_get until EBADF,
    // and a stdio or fstream call on a closed file fails on EBADF: nothing is open, so every file call fails, never
    // "succeeds" with its out-parameters unwritten.
    [Theory]
    [InlineData("fd_write")]
    [InlineData("fd_read")]
    [InlineData("fd_seek")]
    [InlineData("fd_fdstat_get")]
    [InlineData("fd_prestat_get")]
    [InlineData("fd_close")]
    [InlineData("path_open")]
    [InlineData("path_filestat_get")]
    [InlineData("poll_oneoff")]
    [InlineData("SimConnect_GetNextDispatch")]   // "no message": a dispatch loop on S_OK would never end
    public void A_call_with_out_parameters_the_engine_cannot_fill_fails(string import)
    {
        using var e = Engine(_ => CallWithZeros(import).Concat(new byte[] { 0x45 }).Concat(TrapIfTrue).ToArray());
        Drawn(e);
    }

    [Fact]
    public void The_clock_is_real_and_monotonic()
    {
        byte[] Ask(int clock, int at) => I32Const(clock).Concat(I64Const(0)).Concat(I32Const(at)).Concat(Call(Imp("clock_time_get")))
            .Concat(TrapIfTrue).ToArray();
        byte[] Load64(int at) => I32Const(at).Concat(new byte[] { 0x29, 0x03, 0x00 }).ToArray();
        using var e = Engine(_ => Ask(1, 8200).Concat(Ask(1, 8208)).Concat(Ask(0, 8216))
            .Concat(Load64(8200)).Append((byte)0x50).Concat(TrapIfTrue)                                    // monotonic: not zero
            .Concat(Load64(8208)).Concat(Load64(8200)).Append((byte)0x54).Concat(TrapIfTrue)               // never backwards
            .Concat(Load64(8216)).Concat(I64Const(1_577_836_800_000_000_000)).Append((byte)0x54).Concat(TrapIfTrue)   // realtime: after 2020
            .ToArray());
        Drawn(e);
    }

    [Fact]
    public void The_environment_is_empty()
    {
        byte[] Load32(int at) => I32Const(at).Concat(new byte[] { 0x28, 0x02, 0x00 }).ToArray();
        using var e = Engine(_ => I32Const(8300).Concat(I32Const(8304)).Concat(Call(Imp("environ_sizes_get"))).Concat(TrapIfTrue)
            .Concat(Load32(8300)).Concat(TrapIfTrue).Concat(Load32(8304)).Concat(TrapIfTrue).ToArray());
        Drawn(e);
    }

    // A torn copy can give a list a cycle that draws on every pass: what one draw records is capped, and past the cap
    // the capture fails rather than grow without bound.
    [Fact]
    public void A_draw_that_records_past_the_cap_fails_with_what_it_kept()
    {
        // loop: nvgText(1, 10, 20, HELLO); local 1 += 1; while local 1 < 100
        byte[] Body(int text) => new byte[] { 0x03, 0x40 }.Concat(I32Const(1)).Append((byte)0x43).Concat(Float(10)).Append((byte)0x43)
            .Concat(Float(20)).Concat(I32Const(Hello)).Concat(I32Const(Hello + 5)).Concat(Call(text)).Append((byte)0x1A)
            .Concat(new byte[] { 0x20, 0x01 }).Concat(I32Const(1)).Append((byte)0x6A).Concat(new byte[] { 0x22, 0x01 })
            .Concat(I32Const(100)).Concat(new byte[] { 0x48, 0x0D, 0x00, 0x0B }).ToArray();
        using var e = Engine(Body);
        e.MaxRecorded = 50;
        e.Load(Copy());
        var cap = e.Draw("CTR");
        Assert.False(cap.Ok);
        Assert.Contains("more than 50", cap.Error);
        Assert.Equal(50, cap.Texts.Count);
    }

    // Review 2026-10-29 (engine I5): a new instance whose start failed (commit exhaustion, a trap in its start) left the
    // engine on the store it had already disposed, and the next copy's Load threw on it for good: every later draw of the
    // session failed, unsaid. The old instance goes only once a new one is built, and a failed one is tried again.
    [Fact]
    public void A_new_instance_that_fails_to_start_is_tried_again_on_the_next_draw()
    {
        bool failStart = false;
        // start: aircraft_varget(1, 1, 0) > 0 traps. CTR::Draw traps while the object's flag is set.
        byte[] start = I32Const(1).Concat(I32Const(1)).Concat(I32Const(0)).Concat(Call(Imp("aircraft_varget")))
            .Concat(new byte[] { 0x44, 0, 0, 0, 0, 0, 0, 0, 0, 0x64 }).Concat(TrapIfTrue).ToArray();
        using var e = Engine(_ => new byte[] { 0x20, 0x00, 0x28, 0x02, 0x04 }.Concat(TrapIfTrue).ToArray(), start);
        e.AVarAnswer = _ => failStart ? 1 : 0;
        e.Load(Copy(flag: 1));
        Assert.False(e.Draw("CTR").Ok);   // a trap: the next draw starts a new instance
        failStart = true;
        e.Load(Copy());
        Assert.False(e.Draw("CTR").Ok);   // the new instance's start traps
        failStart = false;
        e.Load(Copy());
        var cap = e.Draw("CTR");
        Assert.True(cap.Ok, cap.Error);
    }

    [Fact]
    public void An_import_only_a_stub_answers_is_counted_by_name()
    {
        using var e = Engine(_ => CallWithZeros("random_get").Append((byte)0x1A).Concat(CallWithZeros("fd_write")).Append((byte)0x1A).ToArray());
        Drawn(e);
        Assert.Equal(1, e.StubCalls["wasi_snapshot_preview1.random_get"]);
        Assert.Equal(1, e.StubCalls["wasi_snapshot_preview1.fd_write"]);
    }
}
