using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the display engine needs to know about one A350 module version, all re-derived from the module
/// itself: the C++ vtables by class name (a gauge object in the memory copy starts with its class's
/// vtable address), the anchor strings that mark the A350's linear memory inside the sim process, and
/// where the FMS keeps the take-off speeds. Port of tools/a350-probe/vtables.py and the anchors of fastlocate.py.
/// </summary>
public sealed class A350ModuleFacts
{
    /// <summary>Three strings in the A350's data segments; at their linear addresses, the sim's copy of
    /// the A350 memory holds them too (1.2.7; the 1.2.6 anchors no longer exist).</summary>
    public static readonly string[] AnchorStrings = { "INI_MCDU{}_ENTER", "INI_MCDU_OPTION", "INI_FMC_PAGE1" };

    public IReadOnlyDictionary<uint, string> VTables { get; }
    public IReadOnlyList<(string Text, int Linear)> Anchors { get; }

    /// <summary>Where the FMS keeps the take-off speeds (<see cref="A350TakeoffSpeeds"/>), or null when this version's
    /// module does not say: then only the take-off call-outs go without, never the displays.</summary>
    public A350TakeoffSpeedFacts? TakeoffSpeeds { get; }

    /// <summary>Where the FMS keeps the approach minimums (<see cref="A350Minimums"/>), or null when this version's module
    /// does not say: then only their call-outs go without.</summary>
    public A350MinimumsFacts? Minimums { get; }

    private A350ModuleFacts(Dictionary<uint, string> vtables, List<(string, int)> anchors, A350TakeoffSpeedFacts? speeds,
        A350MinimumsFacts? minimums)
    {
        VTables = vtables;
        Anchors = anchors;
        TakeoffSpeeds = speeds;
        Minimums = minimums;
    }

    public IReadOnlyList<uint> VTablesOf(string cls) => VTables.Where(kv => kv.Value == cls).Select(kv => kv.Key).OrderBy(a => a).ToList();

    // An Itanium-ABI typeinfo name of a plain class: its length, then the name ("3CTR").
    private static readonly Regex SimpleName = new(@"^(\d+)([A-Za-z_]\w*)$", RegexOptions.Compiled);

    public static A350ModuleFacts From(A350WasmModule mod)
    {
        var vt = new Dictionary<uint, string>();
        foreach (var seg in mod.DataSegments)
        {
            var b = seg.Bytes;
            for (int off = 8; off + 4 <= b.Length; off += 4)
            {
                if (BitConverter.ToUInt32(b, off - 8) != 0) continue;
                uint typeinfo = BitConverter.ToUInt32(b, off - 4);
                if (typeinfo < 1024 || typeinfo > int.MaxValue) continue;
                uint? namePtr = mod.U32((int)typeinfo + 4);
                if (namePtr is not uint np || np > int.MaxValue) continue;
                var s = mod.CString((int)np);
                if (string.IsNullOrEmpty(s)) continue;
                var m = SimpleName.Match(s);
                if (!m.Success || int.Parse(m.Groups[1].Value) != m.Groups[2].Value.Length) continue;
                vt[(uint)(seg.Base + off)] = m.Groups[2].Value;
            }
        }

        var anchors = new List<(string, int)>();
        foreach (var a in AnchorStrings)
            anchors.Add((a, mod.LinearAddressOf(a) ?? throw new A350ModuleException("A350 module has no anchor string " + a)));
        var speeds = TakeoffSpeedsOf(mod);
        return new A350ModuleFacts(vt, anchors, speeds, MinimumsOf(mod, speeds));
    }

    /// <summary>The function that clears the approach minimums: its two stores, BARO then RADIO (live 2026-10-30).</summary>
    public const string ApproachResetFunction = "FlightPlan::resetApproachData()";

    private static A350MinimumsFacts? MinimumsOf(A350WasmModule mod, A350TakeoffSpeedFacts? speeds)
    {
        var hits = mod.FunctionNames.Where(kv => kv.Key >= mod.ImportedFunctionCount && kv.Value == ApproachResetFunction).Select(kv => kv.Key).ToList();
        if (speeds is null || hits.Count != 1)
            return null;
        // The fields get 0; the prologue's store of `this` into the frame is no field.
        var stores = WithKnownTop(mod, hits[0]).Where(i => i.Op == 0x36 && i.Top == 0).Select(i => (int)i.Immediate).ToList();
        return stores.Count == 2 ? new A350MinimumsFacts(speeds.FlightPlan, stores[0], stores[1]) : null;
    }

    /// <summary>A function's instructions, each with the constant the stack's top holds as it runs, where one is known: the
    /// unoptimised module pushes a constant straight, or sets a local from an <c>i32.const</c> and gets it back.</summary>
    private static IEnumerable<(byte Op, long Immediate, long? Top)> WithKnownTop(A350WasmModule mod, int function)
    {
        var locals = new Dictionary<long, long>();
        long? top = null;
        foreach (var (op, imm) in mod.Instructions(function))
        {
            yield return (op, imm, top);
            switch (op)
            {
                case 0x41:   // i32.const
                    top = imm;
                    break;
                case 0x21:   // local.set
                    if (top is long c) locals[imm] = c; else locals.Remove(imm);
                    top = null;
                    break;
                case 0x20:   // local.get
                    top = locals.TryGetValue(imm, out var v) ? v : null;
                    break;
                default:     // local.tee and every other instruction: the stack's top is no longer a known constant
                    if (op == 0x22) locals.Remove(imm);
                    top = null;
                    break;
            }
        }
    }

    /// <summary>The getters of the active flight plan's take-off speeds, and the function that calls the V1 getter on that
    /// plan with a constant <c>this</c> (1.2.7: <c>update_v1()</c>, the aircraft's own V1 call-out).</summary>
    public static readonly (string V1, string Vr, string V2, string Caller) TakeoffSpeedFunctions =
        ("FlightPlan::getV1Speed()", "FlightPlan::getVRSpeed()", "FlightPlan::getV2Speed()", "update_v1()");

    private static A350TakeoffSpeedFacts? TakeoffSpeedsOf(A350WasmModule mod)
    {
        int? Defined(string name)
        {
            var hits = mod.FunctionNames.Where(kv => kv.Key >= mod.ImportedFunctionCount && kv.Value == name).Select(kv => kv.Key).ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        var (v1Name, vrName, v2Name, callerName) = TakeoffSpeedFunctions;
        if (Defined(v1Name) is not int v1 || Defined(vrName) is not int vr || Defined(v2Name) is not int v2
            || Defined(callerName) is not int caller)
            return null;
        if (ReturnedLoad(mod, v1) is not int v1At || ReturnedLoad(mod, vr) is not int vrAt || ReturnedLoad(mod, v2) is not int v2At
            || ConstantArgument(mod, caller, v1) is not uint plan)
            return null;
        return new A350TakeoffSpeedFacts(plan, v1At, vrAt, v2At);
    }

    /// <summary>The offset of a getter's last <c>i32.load</c>: the field it returns.</summary>
    private static int? ReturnedLoad(A350WasmModule mod, int getter)
    {
        int? offset = null;
        foreach (var (op, imm) in mod.Instructions(getter))
            if (op == 0x28) offset = (int)imm;
        return offset;
    }

    /// <summary>The constant passed to the first call of <paramref name="callee"/> (a one-argument function) in
    /// <paramref name="caller"/>: pushed straight, or, as the unoptimised module does, set into a local from an
    /// <c>i32.const</c> and got back for the call.</summary>
    private static uint? ConstantArgument(A350WasmModule mod, int caller, int callee)
    {
        foreach (var (op, imm, top) in WithKnownTop(mod, caller))
            if (op == 0x10 && imm == callee)
                return top is long a && a > 0 && a <= uint.MaxValue ? (uint)a : null;
        return null;
    }
}
