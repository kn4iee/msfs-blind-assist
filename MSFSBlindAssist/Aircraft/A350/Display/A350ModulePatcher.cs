using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A patched scratch copy of the A350 module, and the UI element classes in the order their
/// draw hook numbers them.</summary>
public sealed record A350PatchedModule(byte[] Bytes, IReadOnlyList<string> UiDrawClasses)
{
    /// <summary>The allocator functions the engine answers [A350-10].</summary>
    public IReadOnlyList<string> Allocator { get; init; } = Array.Empty<string>();

    /// <summary>The allocator functions found with another signature than the patch's, left to run.</summary>
    public IReadOnlyList<string> AllocatorLeftAlone { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Builds the scratch module the display engine runs: the A350's own module with a report call
/// prepended to each drawing function and each gauge's Draw exported. A report is a call to the WASI
/// import <c>path_readlink</c> (never called while drawing) with a magic number as its sixth argument,
/// so the engine learns every text, colour, transform, frame and UI element the draw code produces. The A350's
/// allocator is answered by the engine instead of run [A350-10], which the prototype did not do.
/// The installed module is only read; the result lives on the pilot's machine. Port of
/// tools/a350-probe/patchtext.py and patch2.py; magics as in docs/inibuilds-a350.md "The patch".
/// </summary>
public static class A350ModulePatcher
{
    public const int TextMagic = 0x7E57, FillMagic = 0x7E58, SaveMagic = 0x7E59, RestoreMagic = 0x7E5A,
        TranslateMagic = 0x7E5B, ScaleMagic = 0x7E5C, RotateMagic = 0x7E5D, ResetTransformMagic = 0x7E5E,
        ResetMagic = 0x7E5F, TransformMagic = 0x7E60, FrameMagic = 0x7E61, FrameColourMagic = 0x7E62,
        ElementMagic = 0x7E65;

    /// <summary>The allocator's questions to the engine, whose answer the patched function returns.</summary>
    public const int MallocMagic = 0x7E70, CallocMagic = 0x7E71, ReallocMagic = 0x7E72, MemalignMagic = 0x7E73,
        PosixMemalignMagic = 0x7E74;

    public const string HookImport = "path_readlink";

    /// <summary>The patch's version, part of the compiled module cache's key: bump it whenever the patch changes, or
    /// a module compiled from the older patch is loaded.</summary>
    public const int Version = 3;

    /// <summary>The functions exported for the engine to call: export name emu_&lt;Class&gt;_Draw.</summary>
    public static readonly string[] DrawExports =
    {
        "CTR::Draw()", "EFIS::Draw()", "FCU::Draw()", "HUD::Draw()", "ISIS::Draw()", "RMP::Draw()",
        "BAT::Draw()", "MFD::Draw()", "FMGS::Draw()", "SFD::Draw(int)", "SND::Draw(int)", "Systems::Draw()",
        "nvgBeginFrame", "nvgEndFrame",
    };

    public static string ExportName(string function) => "emu_" + function.Split('(')[0].Replace("::", "_");

    private static readonly Regex UiDraw = new(@"^[A-Za-z_]+::draw\(NVGcontext\*, bool, int\)$", RegexOptions.Compiled);

    // Wasm bytes: local.get n = 0x20 n; i32.reinterpret_f32 = 0xBC; i32.const 0 = 0x41 0x00.
    private static byte[] Get(int n) => new byte[] { 0x20, (byte)n };
    private static byte[] GetF(int n) => new byte[] { 0x20, (byte)n, 0xBC };
    private static readonly byte[] Zero = { 0x41, 0x00 };
    private static byte[] Const(int v) => Concat(new byte[] { 0x41 }, Sleb(v));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Call(int hookIndex, int magic, params byte[][] args)
    {
        if (args.Length != 5) throw new ArgumentException("a report takes five arguments");
        return Concat(Concat(args), Const(magic), Concat(new byte[] { 0x10 }, Uleb(hookIndex)), new byte[] { 0x1A });
    }

    /// <summary>A question to the engine whose answer the function returns at once: the rest of its body never runs.</summary>
    private static byte[] Answer(int hookIndex, int magic, params byte[][] args)
    {
        if (args.Length != 5) throw new ArgumentException("a question takes five arguments");
        return Concat(Concat(args), Const(magic), Concat(new byte[] { 0x10 }, Uleb(hookIndex)), new byte[] { 0x0F });
    }

    private static readonly byte[] Return = { 0x0F };

    /// <summary>
    /// The A350's allocator (wasi-libc's dlmalloc), answered by the engine from the head room above the copy, free a
    /// no-op: a copy taken across frames tears the allocator's free lists, and live 2026-10-28 the draws that trapped
    /// died there (dlmalloc &lt;- malloc &lt;- operator new). Every entry point that reaches the heap state is answered,
    /// so no allocation reads or writes the copy's heap. A function is answered in each signature the patch knows
    /// (dlmalloc's own internal_memalign keeps its mstate first); one this A350 version lacks, or has with a signature the
    /// patch does not know, is left alone (an answer reading shifted arguments hands out wrong blocks; a bare return
    /// where a result is due does not compile).
    /// </summary>
    private static IEnumerable<(string Name, byte[] Params, byte[] Results, byte[] Code)> AllocatorPatches(int hook) => new[]
    {
        ("malloc", I32s(1), I32s(1), Answer(hook, MallocMagic, Get(0), Zero, Zero, Zero, Zero)),
        ("dlmalloc", I32s(1), I32s(1), Answer(hook, MallocMagic, Get(0), Zero, Zero, Zero, Zero)),
        ("calloc", I32s(2), I32s(1), Answer(hook, CallocMagic, Get(0), Get(1), Zero, Zero, Zero)),
        ("realloc", I32s(2), I32s(1), Answer(hook, ReallocMagic, Get(0), Get(1), Zero, Zero, Zero)),
        ("internal_memalign", I32s(2), I32s(1), Answer(hook, MemalignMagic, Get(0), Get(1), Zero, Zero, Zero)),
        ("internal_memalign", I32s(3), I32s(1), Answer(hook, MemalignMagic, Get(1), Get(2), Zero, Zero, Zero)),   // (mstate, alignment, bytes)
        ("posix_memalign", I32s(3), I32s(1), Answer(hook, PosixMemalignMagic, Get(0), Get(1), Get(2), Zero, Zero)),
        ("free", I32s(1), I32s(0), Return),
        ("dlfree", I32s(1), I32s(0), Return),
    };

    /// <summary>A signature's value types: <paramref name="n"/> i32s (0x7F).</summary>
    private static byte[] I32s(int n) => Enumerable.Repeat((byte)0x7F, n).ToArray();

    /// <summary>The function defined by that one name, or null when none or several are.</summary>
    private static int? TryFindFunction(A350WasmModule mod, string name)
    {
        var hits = mod.FunctionNames.Where(kv => kv.Key >= mod.ImportedFunctionCount && kv.Value == name).Select(kv => kv.Key).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    public static A350PatchedModule Patch(byte[] original)
    {
        var mod = A350WasmModule.Parse(original);
        int hook = mod.FindImport(HookImport);
        if (hook < 0) throw new A350ModuleException("A350 module has no " + HookImport + " import");

        var hooks = new Dictionary<int, byte[]>
        {
            // nvgText(ctx, x, y, string, end): reports string, end, ctx, x, y
            [mod.FindFunction("nvgText")] = Call(hook, TextMagic, Get(3), Get(4), Get(0), GetF(1), GetF(2)),
            [mod.FindFunction("nvgFillColor")] = Call(hook, FillMagic, Get(0), Get(1), Zero, Zero, Zero),
            [mod.FindFunction("nvgSave")] = Call(hook, SaveMagic, Get(0), Zero, Zero, Zero, Zero),
            [mod.FindFunction("nvgRestore")] = Call(hook, RestoreMagic, Get(0), Zero, Zero, Zero, Zero),
            [mod.FindFunction("nvgTranslate")] = Call(hook, TranslateMagic, Get(0), GetF(1), GetF(2), Zero, Zero),
            [mod.FindFunction("nvgScale")] = Call(hook, ScaleMagic, Get(0), GetF(1), GetF(2), Zero, Zero),
            [mod.FindFunction("nvgRotate")] = Call(hook, RotateMagic, Get(0), GetF(1), Zero, Zero, Zero),
            [mod.FindFunction("nvgResetTransform")] = Call(hook, ResetTransformMagic, Get(0), Zero, Zero, Zero, Zero),
            [mod.FindFunction("nvgReset")] = Call(hook, ResetMagic, Get(0), Zero, Zero, Zero, Zero),
            [mod.FindFunction("nvgTransform")] = Call(hook, TransformMagic, Get(0), Zero, Zero, Zero, Zero),
            // DrawFrame(ctx, x, y, w, h, colour&): the rectangle, then the colour (not wide)
            [mod.FindFunction("DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)")] = Concat(
                Call(hook, FrameMagic, Get(0), GetF(1), GetF(2), GetF(3), GetF(4)),
                Call(hook, FrameColourMagic, Get(0), Get(5), Zero, Zero, Zero)),
            // DrawWideFrame(ctx, x, y, w, h, width, colour&): the ECAM selection box
            [mod.FindFunction("DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)")] = Concat(
                Call(hook, FrameMagic, Get(0), GetF(1), GetF(2), GetF(3), GetF(4)),
                Call(hook, FrameColourMagic, Get(0), Get(6), Const(1), Zero, Zero)),
        };

        // Every UI element's draw: (element, ctx, class index), sorted by name as the prototype numbered them.
        var ui = UiDrawFunctions(mod);
        for (int k = 0; k < ui.Count; k++)
            hooks[mod.FindFunction(ui[k])] = Call(hook, ElementMagic, Get(0), Get(1), Const(k), Zero, Zero);

        var answered = new List<string>();
        var leftAlone = new List<string>();
        foreach (var function in AllocatorPatches(hook).GroupBy(p => p.Name))
        {
            if (TryFindFunction(mod, function.Key) is not int fn)
                continue;
            var type = mod.Types[mod.FunctionTypeOf(fn)];
            var known = function.FirstOrDefault(p => type.Params.AsSpan().SequenceEqual(p.Params) && type.Results.AsSpan().SequenceEqual(p.Results));
            if (known.Code == null)
            {
                leftAlone.Add(function.Key);
                continue;
            }
            hooks[fn] = known.Code;
            answered.Add(function.Key);
        }

        var exports = DrawExports.Select(n => (ExportName(n), mod.FindFunction(n))).ToList();
        var bytes = AddExports(PrependToBodies(original, mod, hooks), exports);
        return new A350PatchedModule(bytes, ui.Select(n => n.Split("::")[0]).ToList()) { Allocator = answered, AllocatorLeftAlone = leftAlone };
    }

    private static List<string> UiDrawFunctions(A350WasmModule mod) =>
        mod.FunctionNames.Where(kv => kv.Key >= mod.ImportedFunctionCount && UiDraw.IsMatch(kv.Value))
            .Select(kv => kv.Value).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The UI element classes in hook-index order, from the ORIGINAL module (a cached compiled
    /// module is not re-patched).</summary>
    public static IReadOnlyList<string> UiDrawClassesOf(A350WasmModule mod) =>
        UiDrawFunctions(mod).Select(n => n.Split("::")[0]).ToList();

    /// <summary>Prepends code to the given functions' bodies, after their locals declaration.</summary>
    public static byte[] PrependToBodies(byte[] data, A350WasmModule mod, IReadOnlyDictionary<int, byte[]> hooks)
    {
        var code = mod.Sections.Single(s => s.Id == 10);
        int p = code.ContentStart;
        int n = (int)A350WasmModule.ReadU(data, ref p);
        using var body = new MemoryStream();
        body.Write(Uleb(n));
        for (int fi = 0; fi < n; fi++)
        {
            int entry = p;
            int size = (int)A350WasmModule.ReadU(data, ref p);
            int start = p, end = p + size;
            p = end;
            if (!hooks.TryGetValue(fi + mod.ImportedFunctionCount, out var hook))
            {
                body.Write(data, entry, end - entry);
                continue;
            }
            int q = start;
            int groups = (int)A350WasmModule.ReadU(data, ref q);
            for (int g = 0; g < groups; g++) { A350WasmModule.ReadU(data, ref q); q++; }
            int newSize = (q - start) + hook.Length + (end - q);
            body.Write(Uleb(newSize));
            body.Write(data, start, q - start);
            body.Write(hook);
            body.Write(data, q, end - q);
        }
        return Replace(data, code, 10, body.ToArray());
    }

    /// <summary>Appends function exports to the export section.</summary>
    public static byte[] AddExports(byte[] data, IEnumerable<(string Name, int Function)> extra)
    {
        var mod = A350WasmModule.Parse(data);
        var sec = mod.Sections.FirstOrDefault(s => s.Id == 7) ?? throw new A350ModuleException("A350 module has no export section");
        int p = sec.ContentStart;
        int n = (int)A350WasmModule.ReadU(data, ref p);
        var list = extra.ToList();
        using var content = new MemoryStream();
        content.Write(Uleb(n + list.Count));
        content.Write(data, p, sec.End - p);
        foreach (var (name, fn) in list)
        {
            var nb = Encoding.UTF8.GetBytes(name);
            content.Write(Uleb(nb.Length)); content.Write(nb); content.WriteByte(0); content.Write(Uleb(fn));
        }
        return Replace(data, sec, 7, content.ToArray());
    }

    private static byte[] Replace(byte[] data, A350WasmModule.Section sec, byte id, byte[] content)
    {
        using var o = new MemoryStream(data.Length + 4096);
        o.Write(data, 0, sec.HeaderStart);
        o.WriteByte(id);
        o.Write(Uleb(content.Length));
        o.Write(content);
        o.Write(data, sec.End, data.Length - sec.End);
        return o.ToArray();
    }

    public static byte[] Uleb(long n)
    {
        var r = new List<byte>();
        do { byte b = (byte)(n & 0x7F); n >>= 7; r.Add((byte)(b | (n != 0 ? 0x80 : 0))); } while (n != 0);
        return r.ToArray();
    }

    public static byte[] Sleb(long n)
    {
        var r = new List<byte>();
        while (true)
        {
            byte b = (byte)(n & 0x7F); n >>= 7;
            bool done = (n == 0 && (b & 0x40) == 0) || (n == -1 && (b & 0x40) != 0);
            r.Add((byte)(b | (done ? 0 : 0x80)));
            if (done) return r.ToArray();
        }
    }
}
