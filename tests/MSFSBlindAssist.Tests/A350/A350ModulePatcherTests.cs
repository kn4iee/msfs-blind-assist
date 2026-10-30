using MSFSBlindAssist.Aircraft.A350.Display;
using Wasmtime;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// The patcher prepends a report call to the A350's drawing functions (through the never-used WASI
// path_readlink import, tagged with a magic number) and exports each gauge's Draw. These run the
// patched result in Wasmtime: a module built here, never an iniBuilds file.
public class A350ModulePatcherTests
{
    private static readonly string[] Gauges =
        { "CTR", "EFIS", "FCU", "HUD", "ISIS", "RMP", "BAT", "MFD", "FMGS", "Systems" };

    /// <summary>A module with every name the patcher hooks or exports, each a stub of the A350's shape.
    /// nvgText stores 99 at address 0 so a test can tell the hook ran before the original body.</summary>
    public static byte[] Sample(string? leaveOut = null, bool withUiDraw = true)
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
        int tUi = m.AddType(new[] { I32, I32, I32, I32 }, Array.Empty<byte>());
        int tXf = m.AddType(new[] { I32, F32, F32, F32, F32, F32, F32 }, Array.Empty<byte>());
        int tRect = m.AddType(new[] { I32, F32, F32, F32, F32 }, Array.Empty<byte>());
        m.Import("wasi_snapshot_preview1", "fd_write", t6);
        m.Import("wasi_snapshot_preview1", "path_readlink", t6);
        void F(string name, int type, byte[]? body = null)
        {
            if (name == leaveOut) return;
            int idx = m.Func(name, type, body ?? Array.Empty<byte>());
            if (name.StartsWith("nvg") || name.StartsWith("Foo") || name.StartsWith("Bar")) m.Export("raw_" + name.Split('(')[0].Replace("::", "_"), idx);
        }
        // nvgText: store 99 at 0, return 0.0
        F("nvgText", tText, new byte[] { 0x41, 0x00, 0x41, 0xE3, 0x00, 0x36, 0x02, 0x00, 0x43, 0, 0, 0, 0 });
        F("nvgFillColor", tCtxPtr);
        F("nvgSave", tCtx); F("nvgRestore", tCtx);
        F("nvgTranslate", tCtxFF); F("nvgScale", tCtxFF); F("nvgRotate", tCtxF);
        F("nvgResetTransform", tCtx); F("nvgReset", tCtx); F("nvgTransform", tXf);
        F("nvgScissor", tRect); F("nvgResetScissor", tCtx);
        F("DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)", tFrame);
        F("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)", tWide);
        foreach (var g in Gauges) F(g + "::Draw()", tCtx);
        F("SFD::Draw(int)", tCtxPtr); F("SND::Draw(int)", tCtxPtr);
        F("nvgBeginFrame", tCtxFF); F("nvgEndFrame", tCtx);
        if (withUiDraw) { F("Foo::draw(NVGcontext*, bool, int)", tUi); F("Bar::draw(NVGcontext*, bool, int)", tUi); }
        return m.Build();
    }

    // [A350-11] (review 2026-10-29, engine M10: only the cache path's format was pinned): the compiled module is cached by
    // the patch's version, so a patch whose output changes without a new version loads the old compile and the change never
    // applies. The patch's output on the sample is pinned beside its version: when this fails, bump
    // A350ModulePatcher.Version, then record the new hash here.
    [Fact]
    public void The_patch_version_moves_whenever_the_patch_output_does()
    {
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(A350ModulePatcher.Patch(Sample()).Bytes));
        Assert.Equal((5, "633ab6ec9eb310ae8ad08ebc8dee773bae74b7d53a73d460b6e596e2e1d03326"), (A350ModulePatcher.Version, hash));
    }

    [Fact]
    public void The_patched_module_exports_every_gauge_draw()
    {
        var patched = A350ModulePatcher.Patch(Sample());
        using var engine = new Engine();
        using var module = Module.FromBytes(engine, "a350", patched.Bytes);
        var exports = module.Exports.Select(e => e.Name).ToHashSet();
        foreach (var g in Gauges.Append("SFD").Append("SND")) Assert.Contains("emu_" + g + "_Draw", exports);
        Assert.Contains("emu_nvgBeginFrame", exports);
        Assert.Contains("emu_nvgEndFrame", exports);
        Assert.Equal(new[] { "Bar", "Foo" }, patched.UiDrawClasses);
    }

    private sealed record Report(int A, int B, int C, int D, int E, int Magic, int MemAtCall);

    private static (Instance Inst, Store Store, List<Report> Reports) Run(byte[] bytes)
    {
        var engine = new Engine();
        var module = Module.FromBytes(engine, "a350", bytes);
        var store = new Store(engine);
        var linker = new Linker(engine);
        var reports = new List<Report>();
        Memory? mem = null;
        linker.Define("wasi_snapshot_preview1", "fd_write", Function.FromCallback(store, (int a, int b, int c, int d, int e, int f) => 0));
        linker.Define("wasi_snapshot_preview1", "path_readlink", Function.FromCallback(store, (int a, int b, int c, int d, int e, int f) =>
        {
            reports.Add(new Report(a, b, c, d, e, f, mem!.ReadInt32(0)));
            return 0;
        }));
        var inst = linker.Instantiate(store, module);
        mem = inst.GetMemory("memory");
        return (inst, store, reports);
    }

    [Fact]
    public void NvgText_reports_string_end_ctx_and_position_before_drawing()
    {
        var (inst, _, reports) = Run(A350ModulePatcher.Patch(Sample()).Bytes);
        var text = inst.GetFunction("raw_nvgText")!;
        text.Invoke(7, 1.5f, -2f, 1000, 1004);
        var r = Assert.Single(reports);
        Assert.Equal(0x7E57, r.Magic);
        Assert.Equal((1000, 1004, 7), (r.A, r.B, r.C));
        Assert.Equal(1.5f, BitConverter.Int32BitsToSingle(r.D));
        Assert.Equal(-2f, BitConverter.Int32BitsToSingle(r.E));
        Assert.Equal(0, r.MemAtCall);   // reported before the original body stored 99
        Assert.Equal(99, inst.GetMemory("memory")!.ReadInt32(0));
    }

    [Fact]
    public void Each_drawing_call_reports_its_own_magic()
    {
        var (inst, _, reports) = Run(A350ModulePatcher.Patch(Sample()).Bytes);
        inst.GetFunction("raw_nvgFillColor")!.Invoke(7, 64);
        inst.GetFunction("raw_nvgSave")!.Invoke(7);
        inst.GetFunction("raw_nvgRestore")!.Invoke(7);
        inst.GetFunction("raw_nvgTranslate")!.Invoke(7, 3f, 4f);
        inst.GetFunction("raw_nvgScale")!.Invoke(7, 2f, 2f);
        inst.GetFunction("raw_nvgRotate")!.Invoke(7, 0.5f);
        inst.GetFunction("raw_nvgResetTransform")!.Invoke(7);
        inst.GetFunction("raw_nvgReset")!.Invoke(7);
        inst.GetFunction("raw_Foo_draw")!.Invoke(500, 7, 1, 0);
        inst.GetFunction("raw_Bar_draw")!.Invoke(600, 7, 1, 0);
        Assert.Equal(new[] { 0x7E58, 0x7E59, 0x7E5A, 0x7E5B, 0x7E5C, 0x7E5D, 0x7E5E, 0x7E5F, 0x7E65, 0x7E65 }, reports.Select(r => r.Magic));
        Assert.Equal((7, 64), (reports[0].A, reports[0].B));
        Assert.Equal(3f, BitConverter.Int32BitsToSingle(reports[3].B));
        Assert.Equal(4f, BitConverter.Int32BitsToSingle(reports[3].C));
        // The UI element hook: element pointer, ctx, its class's index in UiDrawClasses (Bar 0, Foo 1).
        Assert.Equal((500, 7, 1), (reports[8].A, reports[8].B, reports[8].C));
        Assert.Equal((600, 7, 0), (reports[9].A, reports[9].B, reports[9].C));
    }

    // The clip (live 2026-10-30: the PFD's tapes draw their labels beyond it): nvgScissor reports ctx and its rectangle,
    // nvgResetScissor its ctx; a module without them still patches.
    [Fact]
    public void The_scissor_reports_its_rectangle_and_its_reset()
    {
        var (inst, _, reports) = Run(A350ModulePatcher.Patch(Sample()).Bytes);
        inst.GetFunction("raw_nvgScissor")!.Invoke(7, 1f, 2f, 30f, 40f);
        inst.GetFunction("raw_nvgResetScissor")!.Invoke(7);
        Assert.Equal(new[] { A350ModulePatcher.ScissorMagic, A350ModulePatcher.ResetScissorMagic }, reports.Select(r => r.Magic));
        Assert.Equal(7, reports[0].A);
        Assert.Equal(new[] { 1f, 2f, 30f, 40f }, new[] { reports[0].B, reports[0].C, reports[0].D, reports[0].E }.Select(BitConverter.Int32BitsToSingle));
        Assert.Equal(7, reports[1].A);
        A350ModulePatcher.Patch(Sample(leaveOut: "nvgScissor"));
    }

    [Fact]
    public void A_frame_reports_its_rectangle_then_its_colour_and_a_wide_frame_says_so()
    {
        var patched = A350ModulePatcher.Patch(Sample());
        var mod = A350WasmModule.Parse(patched.Bytes);
        Assert.Contains("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)", mod.FunctionNames.Values);
        // Frames are not exported by the sample; run them through a re-export of the patched module.
        var (inst, _, reports) = Run(Reexport(patched.Bytes, mod, "DrawFrame(", "DrawWideFrame("));
        inst.GetFunction("x_DrawFrame")!.Invoke(7, 1f, 2f, 30f, 40f, 88);
        inst.GetFunction("x_DrawWideFrame")!.Invoke(7, 1f, 2f, 30f, 40f, 3f, 99);
        Assert.Equal(new[] { 0x7E61, 0x7E62, 0x7E61, 0x7E62 }, reports.Select(r => r.Magic));
        Assert.Equal(30f, BitConverter.Int32BitsToSingle(reports[0].D));
        Assert.Equal((7, 88, 0), (reports[1].A, reports[1].B, reports[1].C));
        Assert.Equal((7, 99, 1), (reports[3].A, reports[3].B, reports[3].C));
    }

    private static byte[] Reexport(byte[] bytes, A350WasmModule mod, params string[] prefixes)
    {
        var extra = mod.FunctionNames.Where(kv => kv.Key >= mod.ImportedFunctionCount && prefixes.Any(p => kv.Value.StartsWith(p)))
            .Select(kv => ("x_" + kv.Value.Split('(')[0], kv.Key)).ToList();
        return A350ModulePatcher.AddExports(bytes, extra);
    }

    [Fact]
    public void A_missing_drawing_function_names_itself()
    {
        var e = Assert.Throws<A350ModuleException>(() => A350ModulePatcher.Patch(Sample(leaveOut: "EFIS::Draw()")));
        Assert.Contains("EFIS::Draw()", e.Message);
        e = Assert.Throws<A350ModuleException>(() => A350ModulePatcher.Patch(Sample(leaveOut: "nvgFillColor")));
        Assert.Contains("nvgFillColor", e.Message);
    }
}
