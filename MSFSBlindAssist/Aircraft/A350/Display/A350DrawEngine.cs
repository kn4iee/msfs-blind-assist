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
    private List<DrawnShape> _shapes = new();

    /// <summary>Also record what NanoVG renders (fills, strokes, images) into <see cref="A350DrawCapture.Shapes"/>:
    /// the SD's valves, pumps and needles. Off, the render calls cost nothing.</summary>
    public bool RecordShapes { get; set; }

    private readonly Dictionary<string, int> _avarIds = new(), _unitIds = new();
    private readonly Dictionary<int, (string Name, uint Address)> _customVars = new();
    private readonly Dictionary<string, List<int>> _objects = new();
    private A350MemoryCopy? _copy;

    /// <summary>Calculator strings and A: vars the last draws asked for (diagnostics, A350Probe simq).</summary>
    public HashSet<string> Asked { get; } = new();

    /// <summary>How long one call of a gauge's Draw may run: past it the draw traps (epoch interruption, ticked every
    /// <see cref="EpochTickMs"/>) and the next one starts a fresh instance. A torn copy can give a list walk a cycle, and
    /// one draw that never returned stopped every later display read (review 2026-10-29).</summary>
    public TimeSpan DrawDeadline { get; set; } = TimeSpan.FromSeconds(5);

    private const int EpochTickMs = 50;
    private readonly System.Threading.Timer _epochTicker;

    private ulong DeadlineTicks => (ulong)Math.Max(1, Math.Ceiling(DrawDeadline.TotalMilliseconds / EpochTickMs));

    /// <summary>The most texts, frames, elements or shapes one draw records: a cycle that draws on every pass would
    /// otherwise grow them without bound, and a draw past it fails with what it kept.</summary>
    public int MaxRecorded { get; set; } = 100_000;
    private bool _overRecorded;

    private bool Room(int count)
    {
        if (count < MaxRecorded) return true;
        _overRecorded = true;
        return false;
    }

    private readonly Dictionary<string, int> _stubCalls = new();

    /// <summary>The imports the last draw called that only a stub answers, by "module.name", with how often
    /// (diagnostics, A350Probe draw).</summary>
    public IReadOnlyDictionary<string, int> StubCalls => _stubCalls;

    /// <summary>The value of an A: var asked by the draw code: "KOHLSMAN SETTING HG:1|inches of mercury".</summary>
    public Func<string, double?> AVarAnswer { get; set; } = _ => null;

    /// <summary>The string a calculator code asked by the draw code returns ("(A:NAV IDENT:2, String)"), or null.</summary>
    public Func<string, string?> CalcStringAnswer { get; set; } = _ => null;

    /// <summary>Answered strings live in the memory's last page: 64 MB past the copy, never the copy's or the
    /// module's heap. Slots of 64 bytes in turn, so the strings of one draw stay put while it runs.</summary>
    private const int StringSlot = 64, StringSlots = 64;
    private int _nextSlot;

    private int PutString(string text)
    {
        long page = _memory.GetLength() - StringSlot * StringSlots;
        int at = (int)(page + StringSlot * (_nextSlot++ % StringSlots));
        var bytes = Encoding.Latin1.GetBytes(text.Length >= StringSlot ? text[..(StringSlot - 1)] : text);
        var span = _memory.GetSpan(at, StringSlot);
        span.Clear();
        bytes.CopyTo(span);
        return at;
    }

    public A350DrawEngine(Engine engine, Module module, IReadOnlyList<string> uiClasses, A350ModuleFacts facts)
    {
        _uiClasses = uiClasses;
        _facts = facts;
        _engine = engine;
        _module = module;
        _epochTicker = new System.Threading.Timer(_ =>
        {
            try { _engine.IncrementEpoch(); } catch (Exception) { /* the engine is gone: nothing left to interrupt */ }
        }, null, EpochTickMs, EpochTickMs);
        Instantiate();
    }

    /// <summary>A new store and instance. A trap leaves the old one mid-call: its globals (the stack
    /// pointer above all) are not part of the memory copy, so restoring memory does not repair it, and
    /// every later draw on it traps (live, and in the prototype). The old one goes only once the new one is
    /// built: a new instance that fails (commit exhaustion, a trap in its start) leaves the old one in place and is
    /// tried again by the next draw (review 2026-10-29: the old store was disposed first, and every later copy's Load
    /// threw on it for the rest of the session).</summary>
    private void Instantiate()
    {
        var store = new Store(_engine);
        try
        {
            store.SetEpochDeadline(DeadlineTicks);   // a store's deadline starts at 0: without one, its first check traps
            var (instance, memory) = Build(store);
            var old = _store;
            (_store, _instance, _memory) = (store, instance, memory);
            _warmed.Clear();
            old?.Dispose();
        }
        catch
        {
            if (!ReferenceEquals(store, _store)) store.Dispose();
            throw;
        }
        if (_copy != null) GrowFor(_copy);
    }

    /// <summary>Links every import into <paramref name="store"/> and instantiates the module there.</summary>
    private (Instance Instance, Memory Memory) Build(Store store)
    {
        using var linker = new Linker(_engine);
        foreach (var imp in _module.Imports)
        {
            if (imp is not FunctionImport fi) continue;
            var ps = fi.Parameters.ToArray();
            var rs = fi.Results.ToArray();
            Function.UntypedCallbackDelegate body = imp.Name switch
            {
                A350ModulePatcher.HookImport => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                    r[0] = Hook(_memory, a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32(), a[3].AsInt32(), a[4].AsInt32(), a[5].AsInt32()),
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
                    // An id the copy holds no CustomVariable for answers 0, as an unknown A: var does: writing nothing left
                    // the draw reading a stale stack word as the value (review 2026-10-29).
                    int outPtr = a[2].AsInt32();
                    if (outPtr != 0) _memory.WriteDouble(outPtr, LVar(a[0].AsInt32()) ?? 0);
                },
                "fsRenderFill" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    if (RecordShapes && Room(_shapes.Count) && A350ShapeReader.Fill(_memory.GetSpan(0, (int)Math.Min(int.MaxValue, _memory.GetLength())), a[1].AsInt32(), a[5].AsInt32()) is { } s) _shapes.Add(s);
                },
                "fsRenderStroke" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    if (RecordShapes && Room(_shapes.Count) && A350ShapeReader.Stroke(_memory.GetSpan(0, (int)Math.Min(int.MaxValue, _memory.GetLength())), a[1].AsInt32(), a[6].AsInt32(), a[7].AsInt32()) is { } s) _shapes.Add(s);
                },
                "fsRenderTriangles" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    if (RecordShapes && Room(_shapes.Count) && A350ShapeReader.Triangles(_memory.GetSpan(0, (int)Math.Min(int.MaxValue, _memory.GetLength())), a[1].AsInt32(), a[4].AsInt32(), a[5].AsInt32()) is { } s) _shapes.Add(s);
                },
                "execute_calculator_code" => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
                {
                    // (code, FLOAT64* fvalue, SINT32* ivalue, PCSTRINGZ* svalue): a string answer is written to the
                    // memory's last page and *svalue pointed at it.
                    string code = CStr(_memory, a[0].AsInt32());
                    Asked.Add("CALC " + code);
                    int svalue = a.Length > 3 ? a[3].AsInt32() : 0;
                    if (svalue != 0 && CalcStringAnswer(code) is { } text)
                    {
                        _memory.WriteInt32(svalue, PutString(text));
                        r[0] = 1;
                    }
                    else r[0] = 0;
                },
                _ => Stub(imp.ModuleName, imp.Name, rs),
            };
            linker.Define(imp.ModuleName, imp.Name, Function.FromCallback(store, Guard(imp.Name, body, rs, OnHostError), ps, rs));
        }
        var instance = linker.Instantiate(store, _module);
        return (instance, instance.GetMemory("memory") ?? throw new A350ModuleException("A350 module exports no memory"));
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

    /// <summary>WASI's EBADF (no such file: nothing is open), ENOSYS, and the SimConnect E_FAIL ("no message").</summary>
    private const int Ebadf = 8, Enosys = 52, EFail = unchecked((int)0x80004005);

    /// <summary>An import the engine has no answer of its own for, answered as a sandbox with no files, no environment
    /// and a real clock; anything else answers zero. Review 2026-10-29: a bare "success" writes no out-parameter, so libc
    /// read a stale stack word, and musl's write loop (fd_write until all is written) or wasi-libc's preopen scan
    /// (fd_prestat_get until EBADF) never ended; a dispatch loop on SimConnect_GetNextDispatch's S_OK would not either.
    /// Each call is counted (<see cref="StubCalls"/>).</summary>
    private Function.UntypedCallbackDelegate Stub(string module, string name, ValueKind[] results)
    {
        string key = module + "." + name;
        Function.UntypedCallbackDelegate answer = (module, name) switch
        {
            ("wasi_snapshot_preview1", "clock_time_get") => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
            {
                _memory.WriteInt64((uint)a[2].AsInt32(), ClockNanoseconds(a[0].AsInt32()));
                r[0] = 0;
            },
            ("wasi_snapshot_preview1", "environ_sizes_get") => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
            {
                _memory.WriteInt32((uint)a[0].AsInt32(), 0);
                _memory.WriteInt32((uint)a[1].AsInt32(), 0);
                r[0] = 0;
            },
            ("wasi_snapshot_preview1", "poll_oneoff") => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => r[0] = Enosys,
            ("wasi_snapshot_preview1", _) when name.StartsWith("fd_", StringComparison.Ordinal) || name.StartsWith("path_", StringComparison.Ordinal) =>
                (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => r[0] = Ebadf,
            (_, "SimConnect_GetNextDispatch") => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => r[0] = EFail,
            _ => (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => Zero(results, r),
        };
        return (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
        {
            _stubCalls[key] = _stubCalls.GetValueOrDefault(key) + 1;
            answer(c, a, r);
        };
    }

    /// <summary>WASI's clocks in nanoseconds: 0 is the real time since 1970, the others a monotonic count.</summary>
    private static long ClockNanoseconds(int clockId)
    {
        if (clockId == 0) return (DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100;
        long t = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        return t / f * 1_000_000_000 + t % f * 1_000_000_000 / f;
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
        if (_copy == null) return null;
        if (!_customVars.TryGetValue(id, out var cv))
        {
            // Not found when the copy was scanned: created since, so a later copy is scanned again (an id of 0 is the
            // registration stubs' own answer, never a CustomVariable's).
            if (id > 0) _customVarMissed = true;
            return null;
        }
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

    /// <summary>A report from the draw code (0), or a question from its allocator (the answer).</summary>
    private int Hook(Memory mem, int a, int b, int c, int d, int e, int magic)
    {
        switch (magic)
        {
            case A350ModulePatcher.MallocMagic: return Allocate((uint)a, 16);
            case A350ModulePatcher.CallocMagic:
            {
                // count times size in 64 bits: a product beyond 32 bits is no block (C's calloc answers NULL).
                ulong size = (ulong)(uint)a * (uint)b;
                return size > uint.MaxValue ? 0 : AllocateCleared((long)size);
            }
            case A350ModulePatcher.ReallocMagic: return Reallocate((uint)a, (uint)b);
            case A350ModulePatcher.MemalignMagic: return Allocate((uint)b, (uint)a);
            case A350ModulePatcher.PosixMemalignMagic:
            {
                // *memptr = the block; a pointer outside the memory is no place to answer (a host write there threw, and
                // the guard answered 0, success, with nothing written).
                if ((long)(uint)a + 4 > mem.GetLength()) return 48;   // ENOMEM
                int p = Allocate((uint)c, (uint)b);
                if (p == 0) return 48;
                mem.WriteInt32((uint)a, p);
                return 0;
            }
            case A350ModulePatcher.TextMagic:
            {
                if (!Room(_texts.Count)) break;
                uint s = (uint)a, end = (uint)b;
                string text;
                // A torn copy's end pointer can lie far past its start: a text is never longer than a C string is read.
                if (end > s) text = Encoding.Latin1.GetString(mem.GetSpan(s, (int)Math.Min(end - s, 4096)));
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
                if (!Room(_frames.Count)) break;
                var (x, y) = _state.Apply(a, F(b), F(c));
                _frames.Add(new DrawnFrame(x, y, F(d), F(e), false, null));
                break;
            }
            case A350ModulePatcher.FrameColourMagic:
                if (_frames.Count > 0) _frames[^1] = _frames[^1] with { Wide = c != 0, Colour = ReadRgba(mem, b) };
                break;
            case A350ModulePatcher.ElementMagic:
            {
                if (!Room(_elements.Count)) break;
                uint el = (uint)a;
                float rx = mem.ReadSingle(el + 4), ry = mem.ReadSingle(el + 8), rw = mem.ReadSingle(el + 12), rh = mem.ReadSingle(el + 16);
                var (x, y) = _state.Apply(b, rx, ry);
                string cls = c >= 0 && c < _uiClasses.Count ? _uiClasses[c] : "?" + c;
                _elements.Add(new DrawnElement(a, cls, rx, ry, rw, rh, x, y, _texts.Count));
                break;
            }
        }
        return 0;
    }

    // The draw code's own heap: the head room above the copy, below the answered strings' page, used afresh by every
    // call of a Draw (what one draw allocates is dead when it returns). The copy's heap is never touched by an
    // allocation: live 2026-10-28 a torn copy's free lists sent the A350's malloc into text bytes.
    private long _heap, _heapStart, _heapEnd;
    private bool _heapExhausted;

    private void ResetHeap()
    {
        _heap = _heapStart = AlignUp(_copy?.End ?? 0, 16);
        _heapEnd = _memory.GetLength() - StringSlot * StringSlots;
        _heapExhausted = false;
    }

    private static long AlignUp(long v, long a) => (v + a - 1) / a * a;

    /// <summary>A block of <paramref name="size"/> bytes at a multiple of <paramref name="align"/>, after an 8-byte
    /// header holding its size as dlmalloc's chunk head (size | in use) for code that asks a block's size; 0 when the
    /// head room is used up.</summary>
    private int Allocate(long size, long align)
    {
        if (align < 16 || (align & (align - 1)) != 0) align = Math.Max(16, (long)System.Numerics.BitOperations.RoundUpToPowerOf2((ulong)Math.Max(align, 1)));
        long p = AlignUp(_heap + 8, align);
        long end = p + Math.Max(size, 1);
        if (end > _heapEnd || p > int.MaxValue)
        {
            _heapExhausted = true;
            return 0;
        }
        _memory.WriteInt32(p - 4, (int)(AlignUp(size, 8) + 8) | 3);
        _heap = end;
        return (int)p;
    }

    private int AllocateCleared(long size)
    {
        int p = Allocate(size, 16);
        if (p != 0) _memory.GetSpan(p, (int)size).Clear();
        return p;
    }

    /// <summary>A new block holding what the old one held (as many bytes as the new size, at most to the memory's
    /// end: a block's own size is not trusted on a torn copy, and reading past it is harmless).</summary>
    private int Reallocate(long old, long size)
    {
        int p = Allocate(size, 16);
        if (p != 0 && old != 0)
        {
            int n = (int)Math.Min(size, _memory.GetLength() - old);
            if (n > 0) _memory.GetSpan(old, n).CopyTo(_memory.GetSpan(p, n));
        }
        return p;
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
        // every one still holds its id and name (about 1,200 checks instead of a 38 MB scan), unless a draw asked
        // for one they lack (one created since), at most every RescanMissingAfter.
        bool missed = _customVarMissed && Elapsed(_customScanAt);
        if (!missed && _customVars.Count > 0 && _customVars.All(kv => NameAt(copy, kv.Value.Address, (uint)kv.Key) == kv.Value.Name)) return;
        _customVarMissed = false;
        _customScanAt = Environment.TickCount64;
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

    /// <summary>How long a scan that came up short (none of a gauge's objects, fewer than a draw asked for, or no
    /// CustomVariable for an id a draw asked) stands before a later copy is scanned again. A flight load builds the gauges
    /// and their variables after the copy's anchors appear, and a scan kept for good left a display unreadable for the
    /// session (review 2026-10-29); a scan is a pass over the whole copy, so not on every draw.</summary>
    public TimeSpan RescanMissingAfter { get; set; } = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, long> _shortAt = new();
    private bool _customVarMissed;
    private long? _customScanAt;

    private bool Elapsed(long? since) => since is not { } at || Environment.TickCount64 - at >= RescanMissingAfter.TotalMilliseconds;

    /// <summary>Forgets the gauge objects and CustomVariables found, and starts a new instance with the next draw: the
    /// reader found the A350 again (another one, or the same reloaded), which may hold more than the last one did.</summary>
    public void Forget()
    {
        _objects.Clear();
        _shortAt.Clear();
        _customVars.Clear();
        _customVarMissed = false;
        _customScanAt = null;
        _fresh = true;
    }

    public int CustomVariableCount => _customVars.Count;

    internal (string Name, uint Address)? CustomVariable(int id) => _customVars.TryGetValue(id, out var cv) ? cv : null;

    internal double? LVarValue(int id) => LVar(id);

    /// <summary>Every CustomVariable the copy holds: id, name, address (diagnostics: A350Probe sdmap).</summary>
    public IReadOnlyList<(int Id, string Name, uint Address)> CustomVariables =>
        _customVars.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Address)).ToList();

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

    /// <summary>The objects of a gauge class in the copy (first word is one of the class's vtables). A scan that finds
    /// none is not kept, and is made again on a later copy once <see cref="RescanMissingAfter"/> has passed.</summary>
    public IReadOnlyList<int> Objects(string cls)
    {
        if (_objects.TryGetValue(cls, out var cached)) return cached;
        if (!Elapsed(_shortAt.TryGetValue(cls, out var shortAt) ? shortAt : null)) return Array.Empty<int>();
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
        if (list.Count > 0) _objects[cls] = list;
        else _shortAt[cls] = Environment.TickCount64;
        return list;
    }

    /// <summary>Draws one gauge on the loaded copy: "CTR", "EFIS" (0 captain, 1 first officer), "MFD",
    /// "FCU", "RMP" (0-2), "SFD", "SND", "ISIS", "BAT", "HUD". A trap is returned as the capture's error.</summary>
    public A350DrawCapture Draw(string cls, int index = 0)
    {
        void Clear()
        {
            _texts = new(); _frames = new(); _elements = new(); _shapes = new(); _state.Clear();
            _overRecorded = false;
            _stubCalls.Clear();
        }
        Clear();
        var objs = Objects(cls);
        // Fewer objects than asked for (the first officer's EFIS built after the first scan): looked for again, at most
        // every RescanMissingAfter.
        if (index >= objs.Count && objs.Count > 0 && Elapsed(_shortAt.TryGetValue(cls, out var shortAt) ? shortAt : null))
        {
            _objects.Remove(cls);
            objs = Objects(cls);
            if (index >= objs.Count) _shortAt[cls] = Environment.TickCount64;
        }
        if (index >= objs.Count) return new A350DrawCapture(cls, _texts, _frames, _elements, $"no {cls} object #{index}", 0);
        var sw = Stopwatch.StartNew();
        string? error = null;
        try
        {
            if (_fresh) { Instantiate(); _fresh = false; }
            var f = _instance.GetFunction(A350ModulePatcher.ExportName(cls + "::Draw()"))
                ?? throw new A350ModuleException("no export for " + cls);
            void Run()
            {
                Restore();
                ResetHeap();
                _store.SetEpochDeadline(DeadlineTicks);
                if (cls is "SFD" or "SND") f.Invoke(objs[index], 0); else f.Invoke(objs[index]);
            }
            // The first draw of a gauge on a new instance draws only part of it (live: the E/WD's title
            // without its procedure lines): run it once as a warm-up and keep only the second.
            if (_warmed.Add(cls + "#" + index))
            {
                Run();
                Clear();
            }
            Run();
        }
        catch (TrapException ex) when (ex.Type == TrapCode.Interrupt)
        {
            error = $"trap: the draw ran past its {DrawDeadline.TotalSeconds:0.#} s deadline";
            LastTrap = ex.Message;
            _fresh = true;
        }
        catch (TrapException ex) { error = "trap: " + ex.Message.Split('\n')[0]; LastTrap = ex.Message; _fresh = true; }
        catch (WasmtimeException ex) { error = ex.Message.Split('\n')[0]; LastTrap = ex.Message; }
        if (error == null && _overRecorded)
            error = $"the draw recorded more than {MaxRecorded} items (a torn copy drawing in a cycle)";
        LastHeapBytes = _heap - _heapStart;
        LastHeapExhausted = _heapExhausted;
        return new A350DrawCapture(cls, _texts, _frames, _elements, error, sw.Elapsed.TotalMilliseconds)
            { Shapes = RecordShapes ? _shapes : null };
    }

    /// <summary>The last trap's whole message, its wasm backtrace included (diagnostics, A350Probe draw).</summary>
    public string? LastTrap { get; private set; }

    /// <summary>The head room the last draw allocated, in bytes.</summary>
    public long LastHeapBytes { get; private set; }

    /// <summary>Whether the last draw asked for more than the head room held (and got NULL).</summary>
    public bool LastHeapExhausted { get; private set; }

    public void Dispose()
    {
        _epochTicker.Dispose();
        _store.Dispose();
    }
}
