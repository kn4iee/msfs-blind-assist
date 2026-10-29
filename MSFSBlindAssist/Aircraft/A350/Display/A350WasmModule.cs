using System.Text;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The A350's module is not what the display engine expected: a missing or doubled function
/// name, a missing anchor, or not a WebAssembly file. The reader turns this into one spoken notice.</summary>
public sealed class A350ModuleException : Exception
{
    public A350ModuleException(string message) : base(message) { }
}

/// <summary>
/// A read-only view of a WebAssembly module (MVP + bulk memory): its sections, types, function
/// imports, function names (the A350's module keeps its full name section), active data segments and
/// its functions' instructions.
/// Everything the display engine needs is found here by name, never by a hard-coded index, so a new
/// A350 version is re-derived at load. Port of tools/a350-probe/wdis.py.
/// </summary>
public sealed class A350WasmModule
{
    public sealed record Section(byte Id, int HeaderStart, int ContentStart, int End);
    public sealed record FuncType(byte[] Params, byte[] Results);
    public sealed record FunctionImport(string Module, string Field, int Type);
    public sealed record DataSegment(int Base, byte[] Bytes);

    public IReadOnlyList<Section> Sections { get; }
    public IReadOnlyList<FuncType> Types { get; }
    public IReadOnlyList<FunctionImport> FunctionImports { get; }
    public int ImportedFunctionCount => FunctionImports.Count;
    public IReadOnlyDictionary<int, string> FunctionNames { get; }
    public IReadOnlyList<DataSegment> DataSegments { get; }

    private readonly List<int> _functionTypes;
    private readonly byte[] _bytes;
    private readonly List<(int Start, int End)> _bodies;   // each defined function's code, after its locals

    private A350WasmModule(List<Section> sections, List<FuncType> types, List<FunctionImport> imports,
        List<int> functionTypes, Dictionary<int, string> names, List<DataSegment> segments, byte[] bytes,
        List<(int, int)> bodies)
    {
        Sections = sections; Types = types; FunctionImports = imports;
        _functionTypes = functionTypes; FunctionNames = names; DataSegments = segments;
        _bytes = bytes; _bodies = bodies;
    }

    /// <summary>A defined function's instructions in order, its final <c>end</c> included, each with its immediate: a
    /// constant's value, a load's or store's offset, a call's function, a local's or global's index, a branch's depth, a
    /// 0xFC instruction's sub-opcode; 0 for the rest (a float constant, a block's type). Port of wdis.py's walk: the A350's
    /// module has no SIMD and no atomics, and an opcode it does not know has no immediate.</summary>
    public IEnumerable<(byte Op, long Immediate)> Instructions(int functionIndex)
    {
        var (start, end) = _bodies[functionIndex - ImportedFunctionCount];
        var b = _bytes;
        int p = start;
        while (p < end)
        {
            byte op = b[p++];
            long imm = 0;
            switch (op)
            {
                case 0x02 or 0x03 or 0x04:   // block, loop, if: a one-byte block type (never a type index here)
                    p++;
                    break;
                case 0x0C or 0x0D or 0x10 or (>= 0x20 and <= 0x24):   // br, br_if, call, local and global access
                    imm = (long)ReadU(b, ref p);
                    break;
                case 0x0E:   // br_table: the labels and the default
                {
                    int n = (int)ReadU(b, ref p);
                    for (int k = 0; k <= n; k++) ReadU(b, ref p);
                    break;
                }
                case 0x11:   // call_indirect: the type, then the table
                    imm = (long)ReadU(b, ref p);
                    ReadU(b, ref p);
                    break;
                case >= 0x28 and <= 0x3E:   // loads and stores: alignment, then offset
                    ReadU(b, ref p);
                    imm = (long)ReadU(b, ref p);
                    break;
                case 0x3F or 0x40:   // memory.size, memory.grow
                    p++;
                    break;
                case 0x41 or 0x42:
                    imm = ReadS(b, ref p);
                    break;
                case 0x43:
                    p += 4;
                    break;
                case 0x44:
                    p += 8;
                    break;
                case 0xFC:
                    imm = (long)ReadU(b, ref p);
                    switch (imm)
                    {
                        case 8: ReadU(b, ref p); p++; break;   // memory.init: segment, memory
                        case 9: ReadU(b, ref p); break;        // data.drop: segment
                        case 10: p += 2; break;                // memory.copy: two memories
                        case 11: p++; break;                   // memory.fill: memory
                    }
                    break;
            }
            yield return (op, imm);
        }
    }

    /// <summary>The type index of a function (imported or defined).</summary>
    public int FunctionTypeOf(int functionIndex) => _functionTypes[functionIndex];

    /// <summary>The one DEFINED function with this exact name; a missing or doubled name is a module error.</summary>
    public int FindFunction(string name)
    {
        int found = -1, hits = 0;
        foreach (var (idx, n) in FunctionNames)
            if (idx >= ImportedFunctionCount && n == name) { found = idx; hits++; }
        if (hits != 1)
            throw new A350ModuleException(hits == 0 ? $"A350 module has no function {name}" : $"A350 module has {hits} functions named {name}");
        return found;
    }

    /// <summary>The index of the function import with this field name, or -1.</summary>
    public int FindImport(string field)
    {
        for (int i = 0; i < FunctionImports.Count; i++) if (FunctionImports[i].Field == field) return i;
        return -1;
    }

    /// <summary>The linear address of a zero-terminated string that also follows a zero in a data
    /// segment (a whole string, never the tail of a longer one), or null.</summary>
    public int? LinearAddressOf(string text)
    {
        var needle = new byte[text.Length + 2];
        Encoding.ASCII.GetBytes(text, 0, text.Length, needle, 1);
        foreach (var seg in DataSegments)
        {
            int k = seg.Bytes.AsSpan().IndexOf(needle);
            if (k >= 0) return seg.Base + k + 1;
        }
        return null;
    }

    /// <summary>The zero-terminated string at a linear address inside a data segment, or null.</summary>
    public string? CString(int address)
    {
        foreach (var seg in DataSegments)
        {
            if (address < seg.Base || address >= seg.Base + seg.Bytes.Length) continue;
            int start = address - seg.Base, end = start;
            while (end < seg.Bytes.Length && seg.Bytes[end] != 0) end++;
            return Encoding.Latin1.GetString(seg.Bytes, start, end - start);
        }
        return null;
    }

    /// <summary>A little-endian u32 from the data segments, or null outside them.</summary>
    public uint? U32(int address)
    {
        foreach (var seg in DataSegments)
            if (address >= seg.Base && address + 4 <= seg.Base + seg.Bytes.Length)
                return BitConverter.ToUInt32(seg.Bytes, address - seg.Base);
        return null;
    }

    public static ulong ReadU(byte[] b, ref int p)
    {
        ulong r = 0; int s = 0;
        while (true)
        {
            byte x = b[p++];
            r |= (ulong)(x & 0x7F) << s; s += 7;
            if ((x & 0x80) == 0) return r;
        }
    }

    public static long ReadS(byte[] b, ref int p)
    {
        long r = 0; int s = 0; byte x;
        do { x = b[p++]; r |= (long)(x & 0x7F) << s; s += 7; } while ((x & 0x80) != 0);
        if (s < 64 && (x & 0x40) != 0) r |= -1L << s;
        return r;
    }

    private static string ReadName(byte[] b, ref int p)
    {
        int n = (int)ReadU(b, ref p);
        var s = Encoding.UTF8.GetString(b, p, n);
        p += n;
        return s;
    }

    public static A350WasmModule Parse(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes[0] != 0 || bytes[1] != 0x61 || bytes[2] != 0x73 || bytes[3] != 0x6D)
            throw new A350ModuleException("not a WebAssembly module");

        var sections = new List<Section>();
        int i = 8;
        while (i < bytes.Length)
        {
            int header = i;
            byte id = bytes[i++];
            int size = (int)ReadU(bytes, ref i);
            sections.Add(new Section(id, header, i, i + size));
            i += size;
        }

        var types = new List<FuncType>();
        var imports = new List<FunctionImport>();
        var functionTypes = new List<int>();
        var names = new Dictionary<int, string>();
        var segments = new List<DataSegment>();
        var bodies = new List<(int, int)>();

        foreach (var sec in sections)
        {
            int p = sec.ContentStart;
            switch (sec.Id)
            {
                case 1:
                {
                    int n = (int)ReadU(bytes, ref p);
                    for (int k = 0; k < n; k++)
                    {
                        if (bytes[p++] != 0x60) throw new A350ModuleException("unexpected type form");
                        int np = (int)ReadU(bytes, ref p); var ps = bytes[p..(p + np)]; p += np;
                        int nr = (int)ReadU(bytes, ref p); var rs = bytes[p..(p + nr)]; p += nr;
                        types.Add(new FuncType(ps, rs));
                    }
                    break;
                }
                case 2:
                {
                    int n = (int)ReadU(bytes, ref p);
                    for (int k = 0; k < n; k++)
                    {
                        string mod = ReadName(bytes, ref p), field = ReadName(bytes, ref p);
                        byte kind = bytes[p++];
                        switch (kind)
                        {
                            case 0: { int t = (int)ReadU(bytes, ref p); imports.Add(new FunctionImport(mod, field, t)); break; }
                            case 1: { p++; ulong fl = ReadU(bytes, ref p); ReadU(bytes, ref p); if ((fl & 1) != 0) ReadU(bytes, ref p); break; }
                            case 2: { ulong fl = ReadU(bytes, ref p); ReadU(bytes, ref p); if ((fl & 1) != 0) ReadU(bytes, ref p); break; }
                            case 3: p += 2; break;
                            default: throw new A350ModuleException("unexpected import kind " + kind);
                        }
                    }
                    break;
                }
                case 3:
                {
                    int n = (int)ReadU(bytes, ref p);
                    for (int k = 0; k < n; k++) functionTypes.Add((int)ReadU(bytes, ref p));
                    break;
                }
                case 10:
                {
                    int n = (int)ReadU(bytes, ref p);
                    for (int k = 0; k < n; k++)
                    {
                        int size = (int)ReadU(bytes, ref p), end = p + size, q = p;
                        int groups = (int)ReadU(bytes, ref q);
                        for (int g = 0; g < groups; g++) { ReadU(bytes, ref q); q++; }
                        bodies.Add((q, end));
                        p = end;
                    }
                    break;
                }
                case 11:
                {
                    int n = (int)ReadU(bytes, ref p);
                    for (int k = 0; k < n; k++)
                    {
                        int flag = (int)ReadU(bytes, ref p);
                        int? baseAddr = null;
                        if (flag == 0 || flag == 2)
                        {
                            if (flag == 2) ReadU(bytes, ref p);
                            if (bytes[p] != 0x41) throw new A350ModuleException("unexpected data offset expression");
                            p++;
                            baseAddr = (int)ReadS(bytes, ref p);
                            if (bytes[p++] != 0x0B) throw new A350ModuleException("unexpected data offset end");
                        }
                        int len = (int)ReadU(bytes, ref p);
                        if (baseAddr is int b) segments.Add(new DataSegment(b, bytes[p..(p + len)]));
                        p += len;
                    }
                    break;
                }
                case 0:
                {
                    string cname = ReadName(bytes, ref p);
                    if (cname != "name") break;
                    while (p < sec.End)
                    {
                        byte sub = bytes[p++];
                        int ssz = (int)ReadU(bytes, ref p), send = p + ssz;
                        if (sub == 1)
                        {
                            int q = p, cnt = (int)ReadU(bytes, ref q);
                            for (int k = 0; k < cnt; k++)
                            {
                                int idx = (int)ReadU(bytes, ref q);
                                names[idx] = ReadName(bytes, ref q);
                            }
                        }
                        p = send;
                    }
                    break;
                }
            }
        }

        var allTypes = new List<int>(imports.Count + functionTypes.Count);
        foreach (var imp in imports) allTypes.Add(imp.Type);
        allTypes.AddRange(functionTypes);
        for (int k = 0; k < imports.Count; k++) names.TryAdd(k, "import:" + imports[k].Field);

        return new A350WasmModule(sections, types, imports, allTypes, names, segments, bytes, bodies);
    }
}
