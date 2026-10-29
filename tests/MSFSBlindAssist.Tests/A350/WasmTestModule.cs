using System.Text;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Builds small, valid WebAssembly modules for the A350 display-engine tests: types, function
/// imports, named functions, exports, one memory and active data segments. Imports come before
/// functions, as in the binary format, so a function's index is the import count plus its own.</summary>
internal sealed class WasmTestModule
{
    public const byte I32 = 0x7F, I64 = 0x7E, F32 = 0x7D, F64 = 0x7C;

    private readonly List<(byte[] Params, byte[] Results)> _types = new();
    private readonly List<(string Module, string Field, int Type)> _imports = new();
    private readonly List<(string Name, int Type, byte[] Locals, byte[] Body)> _funcs = new();
    private readonly List<(string Name, int Func)> _exports = new();
    private readonly List<(int Base, byte[] Bytes)> _data = new();
    private readonly List<int> _globals = new();
    private int? _start;

    /// <summary>The function run when the module is instantiated (the start section).</summary>
    public void Start(int func) => _start = func;

    /// <summary>A mutable i32 global with its initial value; returns its index.</summary>
    public int AddGlobal(int initial)
    {
        _globals.Add(initial);
        return _globals.Count - 1;
    }

    public int AddType(byte[] parameters, byte[] results)
    {
        _types.Add((parameters, results));
        return _types.Count - 1;
    }

    public int Import(string module, string field, int type)
    {
        if (_funcs.Count > 0) throw new InvalidOperationException("imports first");
        _imports.Add((module, field, type));
        return _imports.Count - 1;
    }

    /// <summary>A function with its name in the name section. <paramref name="body"/> is its code
    /// without the final <c>end</c>; <paramref name="localGroups"/> is the raw locals declaration
    /// (count of groups, then count/type pairs), empty for none.</summary>
    public int Func(string name, int type, byte[] body, byte[]? localGroups = null)
    {
        _funcs.Add((name, type, localGroups ?? new byte[] { 0 }, body));
        return _imports.Count + _funcs.Count - 1;
    }

    public void Export(string name, int func) => _exports.Add((name, func));

    public void Data(int baseAddress, byte[] bytes) => _data.Add((baseAddress, bytes));

    public void Data(int baseAddress, string text) => Data(baseAddress, Encoding.ASCII.GetBytes(text));

    public byte[] Build()
    {
        var o = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };

        var types = new List<byte>(U(_types.Count));
        foreach (var (p, r) in _types) { types.Add(0x60); types.AddRange(U(p.Length)); types.AddRange(p); types.AddRange(U(r.Length)); types.AddRange(r); }
        Section(o, 1, types);

        var imports = new List<byte>(U(_imports.Count));
        foreach (var (m, f, t) in _imports) { imports.AddRange(Name(m)); imports.AddRange(Name(f)); imports.Add(0); imports.AddRange(U(t)); }
        Section(o, 2, imports);

        var funcs = new List<byte>(U(_funcs.Count));
        foreach (var f in _funcs) funcs.AddRange(U(f.Type));
        Section(o, 3, funcs);

        Section(o, 5, new List<byte> { 1, 0, 2 });   // one memory, min 2 pages

        if (_globals.Count > 0)
        {
            var globals = new List<byte>(U(_globals.Count));
            foreach (var g in _globals) { globals.Add(I32); globals.Add(1); globals.Add(0x41); globals.AddRange(S(g)); globals.Add(0x0B); }
            Section(o, 6, globals);
        }

        var exports = new List<byte>(U(_exports.Count + 1));
        exports.AddRange(Name("memory")); exports.Add(2); exports.Add(0);
        foreach (var (n, f) in _exports) { exports.AddRange(Name(n)); exports.Add(0); exports.AddRange(U(f)); }
        Section(o, 7, exports);

        if (_start is int start) Section(o, 8, new List<byte>(U(start)));

        var code = new List<byte>(U(_funcs.Count));
        foreach (var f in _funcs)
        {
            var body = new List<byte>(f.Locals);
            body.AddRange(f.Body);
            body.Add(0x0B);
            code.AddRange(U(body.Count)); code.AddRange(body);
        }
        Section(o, 10, code);

        var data = new List<byte>(U(_data.Count));
        foreach (var (b, bytes) in _data)
        {
            data.Add(0); data.Add(0x41); data.AddRange(S(b)); data.Add(0x0B);
            data.AddRange(U(bytes.Length)); data.AddRange(bytes);
        }
        Section(o, 11, data);

        var names = new List<byte>(U(_funcs.Count));
        for (int i = 0; i < _funcs.Count; i++) { names.AddRange(U(_imports.Count + i)); names.AddRange(Name(_funcs[i].Name)); }
        var sub = new List<byte> { 1 };
        sub.AddRange(U(names.Count)); sub.AddRange(names);
        var custom = new List<byte>(Name("name"));
        custom.AddRange(sub);
        Section(o, 0, custom);

        return o.ToArray();
    }

    private static void Section(List<byte> o, byte id, List<byte> content)
    {
        o.Add(id); o.AddRange(U(content.Count)); o.AddRange(content);
    }

    private static byte[] Name(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return U(b.Length).Concat(b).ToArray();
    }

    public static byte[] U(long n)
    {
        var r = new List<byte>();
        do { byte b = (byte)(n & 0x7F); n >>= 7; r.Add((byte)(b | (n != 0 ? 0x80 : 0))); } while (n != 0);
        return r.ToArray();
    }

    public static byte[] S(long n)
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
