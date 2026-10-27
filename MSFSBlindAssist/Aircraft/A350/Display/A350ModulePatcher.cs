using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A patched scratch copy of the A350 module, and the UI element classes in the order their
/// draw hook numbers them.</summary>
public sealed record A350PatchedModule(byte[] Bytes, IReadOnlyList<string> UiDrawClasses);

/// <summary>
/// Builds the scratch module the display engine runs: the A350's own module with a report call
/// prepended to each drawing function and each gauge's Draw exported. A report is a call to the WASI
/// import <c>path_readlink</c> (never called while drawing) with a magic number as its sixth argument,
/// so the engine learns every text, colour, transform, frame and UI element the draw code produces.
/// The installed module is only read; the result lives on the pilot's machine. Port of
/// tools/a350-probe/patchtext.py and patch2.py; magics as in docs/inibuilds-a350.md "The patch".
/// </summary>
public static class A350ModulePatcher
{
    public const int TextMagic = 0x7E57, FillMagic = 0x7E58, SaveMagic = 0x7E59, RestoreMagic = 0x7E5A,
        TranslateMagic = 0x7E5B, ScaleMagic = 0x7E5C, RotateMagic = 0x7E5D, ResetTransformMagic = 0x7E5E,
        ResetMagic = 0x7E5F, TransformMagic = 0x7E60, FrameMagic = 0x7E61, FrameColourMagic = 0x7E62,
        ElementMagic = 0x7E65;

    public const string HookImport = "path_readlink";

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

        var exports = DrawExports.Select(n => (ExportName(n), mod.FindFunction(n))).ToList();
        var bytes = AddExports(PrependToBodies(original, mod, hooks), exports);
        return new A350PatchedModule(bytes, ui.Select(n => n.Split("::")[0]).ToList());
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
