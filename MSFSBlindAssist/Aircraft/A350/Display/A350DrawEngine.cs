using System.Diagnostics;
using System.Text;
using Wasmtime;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Runs the A350's own draw code on a copy of its memory and returns what it drew. The patched module
/// (A350ModuleCache) is instantiated once; each draw restores the copy into the instance's memory and
/// calls the gauge's exported Draw on its object, found in the copy by its class's vtable. The draw code
/// asks the sim for a few values: L:vars come from the copy's own CustomVariable cache, A: vars from
/// <see cref="AVarAnswer"/> (fed from SimConnect), everything else answers zero. Port of emu2.mjs.
/// Not thread-safe: one worker thread owns an engine (A350DisplayService).
/// </summary>
public sealed class A350DrawEngine : IDisposable
{
    /// <summary>Head room above the copy for the draw code's own allocations (the prototype's 64 MB).</summary>
    public const long ExtraBytes = 64L << 20;

    private readonly Engine _engine;
    private readonly Module _module;
    private Store _store = null!;
    private Instance _instance = null!;
    private Memory _memory = null!;
    private bool _fresh;   // set after a trap: the next draw starts a new instance
    private readonly HashSet<string> _warmed = new();   // gauges drawn once on this instance
    private readonly IReadOnlyList<string> _uiClasses;
    private readonly A350ModuleFacts _facts;
    private readonly A350DrawState _state = new();

    private List<DrawnText> _texts = new();
    private List<DrawnFrame> _frames = new();
    private List<DrawnElement> _elements = new();

    private readonly Dictionary<string, int> _avarIds = new(), _unitIds = new();
    private readonly Dictionary<int, (string Name, uint Address)> _customVars = new();
    private readonly Dictionary<string, List<int>> _objects = new();
    private A350MemoryCopy? _copy;

    /// <summary>Calculator strings and A: vars the last draws asked for (diagnostics, A350Probe simq).</summary>
    public HashSet<string> Asked { get; } = new();

    /// <summary>The value of an A: var asked by the draw code: "KOHLSMAN SETTING HG:1|inches of mercury".</summary>
    public Func<string, double?> AVarAnswer { get; set; } = _ => null;

    public A350DrawEngine(Engine engine, Module module, IReadOnlyList<string> uiClasses, A350ModuleFacts facts)
    {
        _uiClasses = uiClasses;
        _facts = facts;
        _engine = engine;
        _module = module;
        Instantiate();
    }

    /// <summary>A new store and instance. A trap leaves the old one mid-call: its globals (the stack
    /// pointer above all) are not part of the memory copy, so restoring memory does not repair it, and
    /// every later draw on it traps (live, and in the prototype).</summary>
    private void Instantiate()
    {
        _store?.Dispose();
        _warmed.Clear();
        _store = new Store(_engine);
        using var linker = new Linker(_engine);
        foreach (var imp in _module.Imports)
        {
            if (imp is not FunctionImport fi) continue;
            var ps = fi.Parameters.ToArray();
            var rs = fi.Results.ToArray();
            Function.UntypedCallbackDelegate body = imp.Name switch
            {
                A350ModulePatcher.HookImport => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    Hook(_memory, a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32(), a[3].AsInt32(), a[4].AsInt32(), a[5].AsInt32());
                    r[0] = 0;
                },
                "get_aircraft_var_enum" or "fsVarsGetAircraftVarId" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                    r[0] = IdFor(_avarIds, CStr(_memory, a[0].AsInt32())),
                "get_units_enum" or "fsVarsGetUnitId" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                    r[0] = IdFor(_unitIds, CStr(_memory, a[0].AsInt32())),
                "aircraft_varget" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                    r[0] = AVar(a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32()),
                "fsVarsAircraftVarGet" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    int outPtr = a[3].AsInt32();
                    if (outPtr != 0) _memory.WriteDouble(outPtr, AVar(a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32()));
                    r[0] = 0;
                },
                "fsVarsNamedVarGet" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    int outPtr = a[2].AsInt32();
                    if (outPtr != 0 && LVar(a[0].AsInt32()) is double v) _memory.WriteDouble(outPtr, v);
                },
                "execute_calculator_code" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    Asked.Add("CALC " + CStr(_memory, a[0].AsInt32()));
                    r[0] = 0;
                },
                _ => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => Zero(rs, r),
            };
            linker.Define(imp.ModuleName, imp.Name, Function.FromCallback(_store, Guard(imp.Name, body, rs, OnHostError), ps, rs));
        }
        _instance = linker.Instantiate(_store, _module);
        _memory = _instance.GetMemory("memory") ?? throw new A350ModuleException("A350 module exports no memory");
        if (_copy != null) GrowFor(_copy);
    }

    private void GrowFor(A350MemoryCopy copy)
    {
        long needPages = ((long)copy.End + ExtraBytes) / 65536 + 1;
        long have = _memory.GetSize();
        if (needPages > have) _memory.Grow(needPages - have);
    }

    /// <summary>Host callback errors so far (a torn copy hands the callbacks garbage pointers).</summary>
    public int HostErrors { get; private set; }

    /// <summary>Called with the import's name and the exception a guarded callback caught.</summary>
    public Action<string, Exception>? HostError { get; set; }

    private void OnHostError(string name, Exception ex)
    {
        HostErrors++;
        HostError?.Invoke(name, ex);
    }

    /// <summary>Wraps a host callback so nothing escapes into Wasmtime's native frames: an exception there
    /// fails the whole process fast (live: 0xC0000409 during an EFIS draw). A throw answers zero.</summary>
    public static Function.UntypedCallbackDelegate Guard(string name, Function.UntypedCallbackDelegate inner, ValueKind[] results,
        Action<string, Exception> onError) =>
        (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
        {
            try { inner(c, a, r); }
            catch (Exception ex)
            {
                Zero(results, r);
                try { onError(name, ex); } catch (Exception) { }
            }
        };

    private static void Zero(ValueKind[] kinds, Span<ValueBox> r)
    {
        for (int i = 0; i < r.Length; i++)
            r[i] = kinds[i] switch
            {
                ValueKind.Int64 => (ValueBox)0L,
                ValueKind.Float32 => (ValueBox)0f,
                ValueKind.Float64 => (ValueBox)0d,
                _ => (ValueBox)0,
            };
    }

    private static int IdFor(Dictionary<string, int> table, string name)
    {
        if (!table.TryGetValue(name, out var id)) table[name] = id = table.Count + 1;
        return id;
    }

    private static string NameOf(Dictionary<string, int> table, int id)
    {
        foreach (var (k, v) in table) if (v == id) return k;
        return "?" + id;
    }

    private double AVar(int varId, int unitId, int index)
    {
        var key = NameOf(_avarIds, varId) + ":" + index + "|" + NameOf(_unitIds, unitId);
        Asked.Add("A " + key);
        return AVarAnswer(key) ?? 0;
    }

    private double? LVar(int id)
    {
        if (_copy == null || !_customVars.TryGetValue(id, out var cv)) return null;
        var b = _copy.Read(cv.Address + 16, 8);
        return b.Length == 8 ? BitConverter.ToDouble(b) : null;
    }

    private static string CStr(Memory mem, int ptr)
    {
        var span = mem.GetSpan((uint)ptr, (int)Math.Min(4096, mem.GetLength() - (uint)ptr));
        int n = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(n < 0 ? span : span[..n]);
    }

    private static float F(int bits) => BitConverter.Int32BitsToSingle(bits);

    private void Hook(Memory mem, int a, int b, int c, int d, int e, int magic)
    {
        switch (magic)
        {
            case A350ModulePatcher.TextMagic:
            {
                uint s = (uint)a, end = (uint)b;
                string text;
                if (end > s) text = Encoding.Latin1.GetString(mem.GetSpan(s, (int)(end - s)));
                else text = CStr(mem, a);
                float lx = F(d), ly = F(e);
                var (x, y) = _state.Apply(c, lx, ly);
                _texts.Add(new DrawnText(text, x, y, lx, ly, c, _state.FillOf(c)));
                break;
            }
            case A350ModulePatcher.FillMagic:
                _state.Fill(a, ReadRgba(mem, b));
                break;
            case A350ModulePatcher.SaveMagic: _state.Save(a); break;
            case A350ModulePatcher.RestoreMagic: _state.Restore(a); break;
            case A350ModulePatcher.TranslateMagic: _state.Translate(a, F(b), F(c)); break;
            case A350ModulePatcher.ScaleMagic: _state.Scale(a, F(b), F(c)); break;
            case A350ModulePatcher.RotateMagic: _state.Rotate(a, F(b)); break;
            case A350ModulePatcher.ResetTransformMagic:
            case A350ModulePatcher.ResetMagic: _state.ResetTransform(a); break;
            case A350ModulePatcher.FrameMagic:
            {
                var (x, y) = _state.Apply(a, F(b), F(c));
                _frames.Add(new DrawnFrame(x, y, F(d), F(e), false, null));
                break;
            }
            case A350ModulePatcher.FrameColourMagic:
                if (_frames.Count > 0) _frames[^1] = _frames[^1] with { Wide = c != 0, Colour = ReadRgba(mem, b) };
                break;
            case A350ModulePatcher.ElementMagic:
            {
                uint el = (uint)a;
                float rx = mem.ReadSingle(el + 4), ry = mem.ReadSingle(el + 8), rw = mem.ReadSingle(el + 12), rh = mem.ReadSingle(el + 16);
                var (x, y) = _state.Apply(b, rx, ry);
                string cls = c >= 0 && c < _uiClasses.Count ? _uiClasses[c] : "?" + c;
                _elements.Add(new DrawnElement(a, cls, rx, ry, rw, rh, x, y, _texts.Count));
                break;
            }
        }
    }

    private static A350Rgba ReadRgba(Memory mem, int p) =>
        new(mem.ReadSingle(p), mem.ReadSingle(p + 4), mem.ReadSingle(p + 8), mem.ReadSingle(p + 12));

    /// <summary>Takes a new copy: grows the instance's memory to hold it, scans its CustomVariables for
    /// L:var answers and checks the remembered gauge objects still hold their vtables.</summary>
    public void Load(A350MemoryCopy copy)
    {
        _copy = copy;
        GrowFor(copy);
        ScanCustomVariables();
        foreach (var cls in _objects.Keys.ToList())
        {
            var vts = _facts.VTablesOf(cls);
            if (_objects[cls].Any(o => !vts.Contains(U32(o)))) _objects.Remove(cls);
        }
    }

    private uint U32(int linear)
    {
        var b = _copy!.Read((uint)linear, 4);
        return b.Length == 4 ? BitConverter.ToUInt32(b) : 0;
    }

    /// <summary>Puts the copy back into the instance's memory exactly as captured (a draw changes state).</summary>
    private void Restore()
    {
        var copy = _copy ?? throw new InvalidOperationException("no copy loaded");
        for (int i = 0; i < copy.Regions.Count; i++)
        {
            var (lin, len) = copy.Regions[i];
            copy.RegionBytes(i).CopyTo(_memory.GetSpan(lin, len));
        }
    }

    // CustomVariable objects: {u32 id, std::string name, f64 cached value at +16}. A libc++ short string
    // holds up to 10 bytes inline with its length in byte 11; a long one has its high bit set there.
    private void ScanCustomVariables()
    {
        var copy = _copy!;
        // The objects live on the A350's heap and stay put while it is loaded: keep their addresses while
        // every one still holds its id and name (about 1,200 checks instead of a 38 MB scan).
        if (_customVars.Count > 0 && _customVars.All(kv => NameAt(copy, kv.Value.Address, (uint)kv.Key) == kv.Value.Name)) return;
        _customVars.Clear();
        for (int ri = 0; ri < copy.Regions.Count; ri++)
        {
            var (lin, len) = copy.Regions[ri];
            var b = copy.RegionBytes(ri);
            for (int o = 0; o + 24 <= len; o += 4)
            {
                uint id = BitConverter.ToUInt32(b[o..]);
                if (id < 1 || id > 65535) continue;
                int s = o + 4;
                bool isLong = (b[s + 11] & 0x80) != 0;
                ReadOnlySpan<byte> name;
                if (isLong)
                {
                    uint p = BitConverter.ToUInt32(b[s..]), n = BitConverter.ToUInt32(b[(s + 4)..]);
                    if (n < 4 || n > 80 || p < 1024) continue;
                    name = copy.Read(p, (int)n);
                    if (name.Length == 0) continue;
                }
                else
                {
                    int n = b[s + 11];
                    if (n < 4 || n > 10) continue;
                    name = b.Slice(s, n);
                }
                byte first = name[0];
                if (!(first >= 'A' && first <= 'Z')) continue;
                bool ok = true;
                foreach (var ch in name)
                    if (!((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || ch == '_' || ch == ':')) { ok = false; break; }
                if (!ok) continue;
                _customVars.TryAdd((int)id, (Encoding.ASCII.GetString(name), lin + (uint)o));
            }
        }
    }

    public int CustomVariableCount => _customVars.Count;

    internal (string Name, uint Address)? CustomVariable(int id) => _customVars.TryGetValue(id, out var cv) ? cv : null;

    internal double? LVarValue(int id) => LVar(id);

    /// <summary>The name of the CustomVariable at an address if it still holds this id, else null.</summary>
    private static string? NameAt(A350MemoryCopy copy, uint address, uint id)
    {
        var head = copy.Read(address, 16);
        if (head.Length < 16 || BitConverter.ToUInt32(head) != id) return null;
        var s = head[4..];
        if ((s[11] & 0x80) == 0)
        {
            int n = s[11];
            return n <= 10 ? Encoding.ASCII.GetString(s[..n]) : null;
        }
        uint p = BitConverter.ToUInt32(s), len = BitConverter.ToUInt32(s[4..]);
        var name = len <= 80 ? copy.Read(p, (int)len) : ReadOnlySpan<byte>.Empty;
        return name.Length == 0 ? null : Encoding.ASCII.GetString(name);
    }

    /// <summary>The objects of a gauge class in the copy (first word is one of the class's vtables).</summary>
    public IReadOnlyList<int> Objects(string cls)
    {
        if (_objects.TryGetValue(cls, out var cached)) return cached;
        var vts = _facts.VTablesOf(cls).ToHashSet();
        var list = new List<int>();
        var copy = _copy ?? throw new InvalidOperationException("no copy loaded");
        if (vts.Count > 0)
            for (int ri = 0; ri < copy.Regions.Count; ri++)
            {
                var (lin, len) = copy.Regions[ri];
                var b = copy.RegionBytes(ri);
                for (int o = 0; o + 4 <= len; o += 4)
                    if (vts.Contains(BitConverter.ToUInt32(b[o..]))) list.Add((int)lin + o);
            }
        _objects[cls] = list;
        return list;
    }

    /// <summary>Draws one gauge on the loaded copy: "CTR", "EFIS" (0 captain, 1 first officer), "MFD",
    /// "FCU", "RMP" (0-2), "SFD", "SND", "ISIS", "BAT", "HUD". A trap is returned as the capture's error.</summary>
    public A350DrawCapture Draw(string cls, int index = 0)
    {
        _texts = new(); _frames = new(); _elements = new(); _state.Clear();
        var objs = Objects(cls);
        if (index >= objs.Count) return new A350DrawCapture(cls, _texts, _frames, _elements, $"no {cls} object #{index}", 0);
        var sw = Stopwatch.StartNew();
        string? error = null;
        try
        {
            if (_fresh) { Instantiate(); _fresh = false; }
            var f = _instance.GetFunction(A350ModulePatcher.ExportName(cls + "::Draw()"))
                ?? throw new A350ModuleException("no export for " + cls);
            // The first draw of a gauge on a new instance draws only part of it (live: the E/WD's title
            // without its procedure lines): run it once as a warm-up and keep only the second.
            if (_warmed.Add(cls + "#" + index))
            {
                Restore();
                if (cls is "SFD" or "SND") f.Invoke(objs[index], 0); else f.Invoke(objs[index]);
                _texts = new(); _frames = new(); _elements = new(); _state.Clear();
            }
            Restore();
            if (cls is "SFD" or "SND") f.Invoke(objs[index], 0); else f.Invoke(objs[index]);
        }
        catch (TrapException ex) { error = "trap: " + ex.Message.Split('\n')[0]; _fresh = true; }
        catch (WasmtimeException ex) { error = ex.Message.Split('\n')[0]; }
        return new A350DrawCapture(cls, _texts, _frames, _elements, error, sw.Elapsed.TotalMilliseconds);
    }

    public void Dispose() => _store.Dispose();
}
