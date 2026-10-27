using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the display engine needs to know about one A350 module version, all re-derived from the module
/// itself: the C++ vtables by class name (a gauge object in the memory copy starts with its class's
/// vtable address) and the anchor strings that mark the A350's linear memory inside the sim process.
/// Port of tools/a350-probe/vtables.py and the anchors of fastlocate.py.
/// </summary>
public sealed class A350ModuleFacts
{
    /// <summary>Three strings in the A350's data segments; at their linear addresses, the sim's copy of
    /// the A350 memory holds them too (1.2.7; the 1.2.6 anchors no longer exist).</summary>
    public static readonly string[] AnchorStrings = { "INI_MCDU{}_ENTER", "INI_MCDU_OPTION", "INI_FMC_PAGE1" };

    public IReadOnlyDictionary<uint, string> VTables { get; }
    public IReadOnlyList<(string Text, int Linear)> Anchors { get; }

    private A350ModuleFacts(Dictionary<uint, string> vtables, List<(string, int)> anchors)
    {
        VTables = vtables;
        Anchors = anchors;
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
        return new A350ModuleFacts(vt, anchors);
    }
}
