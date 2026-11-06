using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Reads BlueBird's EFB tablet control tree straight from the 757 module's memory through a layout
/// (<see cref="Bbs757EfbMemLayout"/>), the replacement for the PaddleOCR capture-and-recognise path: exact text,
/// visibility and tap rectangles, no screen capture. Reaches the EFB object through the gauge manager, then BBOS,
/// then its app map; walks each front app's roots and current page, keeping the controls whose own visible byte
/// (and whose ancestors') is set. Pure over an <see cref="IEfbMemory"/> so it runs against a fake or the live
/// simulator. The std::map/std::vector/std::string shapes it reads are the Emscripten libc++ ABI, stable across
/// BlueBird builds; only the BlueBird-specific field offsets come from the layout.
/// </summary>
public sealed class Bbs757EfbMemory
{
    private readonly IEfbMemory _m;
    private readonly Bbs757EfbMemLayout _l;

    public Bbs757EfbMemory(IEfbMemory memory, Bbs757EfbMemLayout layout)
    {
        _m = memory;
        _l = layout;
    }

    /// <summary>One control read from memory: its class kind, name, text, rectangle, and child controls.</summary>
    public sealed record Node(string Kind, string Name, string Text, float X, float Y, float W, float H, uint Address, IReadOnlyList<Node> Children);

    /// <summary>One EFB app that is currently drawn (its front flag set), with the controls of its roots and its
    /// current page.</summary>
    public sealed record App(int Key, string Class, IReadOnlyList<Node> Controls);

    /// <summary>The front apps in key order, each with its visible control tree; empty when the module is not
    /// reachable (not located, or BBOS/app map not where the layout says).</summary>
    public IReadOnlyList<App> ReadFrontApps()
    {
        if (!TryEfb(out uint efb)) return Array.Empty<App>();
        if (!_m.TryReadPtr(efb + (uint)_l.EfbToBbos, out uint bbos) || bbos == 0) return Array.Empty<App>();
        var apps = new List<App>();
        foreach (var (key, app) in AppMapEntries(bbos))
        {
            if (!_m.TryReadInt(app + (uint)_l.AppFront, out int front) || front == 0) continue;
            var controls = new List<Node>();
            foreach (uint root in Vector(app + (uint)_l.AppRoots)) AddVisible(root, controls);
            if (_m.TryReadPtr(app + (uint)_l.AppPage, out uint page) && page != 0)
                foreach (uint root in Vector(page + (uint)_l.PageChildren)) AddVisible(root, controls);
            apps.Add(new App(key, ClassName(app), controls));
        }
        return apps;
    }

    /// <summary>The EFB object: the first gauge-manager pointer whose object carries the efb vptr.</summary>
    public bool TryEfb(out uint efb)
    {
        efb = 0;
        for (uint off = 0; off + 4 <= _l.ManagerSpan; off += 4)
        {
            if (!_m.TryReadPtr(_l.ManagerBase + off, out uint p) || p == 0) continue;
            if (_m.TryReadPtr(p, out uint vptr) && vptr == _l.EfbVptr) { efb = p; return true; }
        }
        return false;
    }

    // ---- the control tree ----------------------------------------------------

    private void AddVisible(uint ctrl, List<Node> into)
    {
        if (ctrl == 0 || !_m.TryReadByte(ctrl + (uint)_l.CtrlVisible, out byte vis) || (vis & 1) == 0) return;
        var children = new List<Node>();
        foreach (uint ch in Vector(ctrl + (uint)_l.CtrlChildren)) AddVisible(ch, children);
        into.Add(ReadNode(ctrl, children));
    }

    private Node ReadNode(uint ctrl, IReadOnlyList<Node> children)
    {
        string kind = (_m.TryReadPtr(ctrl, out uint vptr) ? _l.KindFor(vptr) : null) ?? "other";
        string name = _m.ReadStdString(ctrl + (uint)_l.CtrlName) ?? "";
        // A button keeps its text at CtrlButtonText, a label at CtrlLabelText; the other field is an empty string
        // (not null), so take the first non-empty of the two.
        string text = NonEmpty(_m.ReadStdString(ctrl + (uint)_l.CtrlButtonText))
            ?? NonEmpty(_m.ReadStdString(ctrl + (uint)_l.CtrlLabelText)) ?? "";
        float X = _m.ReadFloat(ctrl + (uint)_l.CtrlRect), Y = _m.ReadFloat(ctrl + (uint)_l.CtrlRect + 4);
        float W = _m.ReadFloat(ctrl + (uint)_l.CtrlRect + 8), H = _m.ReadFloat(ctrl + (uint)_l.CtrlRect + 12);
        return new Node(kind, name, text, X, Y, W, H, ctrl, children);
    }

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private string ClassName(uint obj) =>
        _m.TryReadPtr(obj, out uint vptr) ? _l.KindFor(vptr) ?? "app" : "app";

    // ---- BBOS app map (libc++ std::map<int, std::vector<app*>>) --------------

    /// <summary>The app map's (key, app pointer) entries. The map node is libc++'s: left/right/parent at 0/4/8,
    /// the key int at 0x10, the value vector (begin,end) at 0x14; the vector holds 8-byte entries (app*, pad).</summary>
    private IEnumerable<(int Key, uint App)> AppMapEntries(uint bbos)
    {
        uint header = bbos + (uint)_l.BbosToAppMap;
        if (!_m.TryReadPtr(header + 4, out uint root) || root == 0) yield break;
        if (!_m.TryReadInt(header + 8, out int size) || size is <= 0 or > 64) yield break;
        var seen = new HashSet<uint>();
        var stack = new Stack<uint>();
        stack.Push(root);
        int guard = 0;
        while (stack.Count > 0 && guard++ < 128)
        {
            uint n = stack.Pop();
            if (n == 0 || !seen.Add(n)) continue;
            if (!_m.TryReadInt(n + 0x10, out int key)) continue;
            uint vb = 0, ve = 0;
            if (_m.TryReadPtr(n + 0x14, out vb) && _m.TryReadPtr(n + 0x18, out ve) && vb != 0 && ve > vb && ve - vb <= 0x400)
                for (uint p = vb; p + 4 <= ve; p += 8)
                    if (_m.TryReadPtr(p, out uint app) && app != 0) yield return (key, app);
            if (_m.TryReadPtr(n, out uint l)) stack.Push(l);
            if (_m.TryReadPtr(n + 4, out uint r)) stack.Push(r);
        }
    }

    /// <summary>A libc++ std::vector of pointers stored in 8-byte entries (pointer, padding): the pointers in
    /// [begin, end), capped so a torn read cannot loop.</summary>
    private IEnumerable<uint> Vector(uint vec)
    {
        if (!_m.TryReadPtr(vec, out uint begin) || !_m.TryReadPtr(vec + 4, out uint end)) yield break;
        if (begin == 0 || end <= begin || end - begin > 0x4000) yield break;
        for (uint p = begin; p + 4 <= end; p += 8)
            if (_m.TryReadPtr(p, out uint e) && e != 0) yield return e;
    }
}

/// <summary>Read-only access to the located 757 module's memory, by wasm address, for the EFB reader. The live
/// implementation wraps <see cref="ModuleMemory"/>; tests supply a fake.</summary>
public interface IEfbMemory
{
    bool TryReadInt(uint wasmAddress, out int value);
    bool TryReadByte(uint wasmAddress, out byte value);
    float ReadFloat(uint wasmAddress);

    /// <summary>A pointer-sized (32-bit wasm) read; false outside the module.</summary>
    bool TryReadPtr(uint wasmAddress, out uint value);

    /// <summary>A libc++ std::string at this address (short-string optimisation or long form); null when it is not
    /// a readable printable string.</summary>
    string? ReadStdString(uint wasmAddress);
}

/// <summary>
/// <see cref="IEfbMemory"/> over a located <see cref="ModuleMemory"/>. The std::string read is the Emscripten
/// libc++ 12-byte layout this build uses (validated 2026-11-06): byte +11's high bit is the long flag; long form
/// is pointer +0, size +4; short form stores the size in +11's low 7 bits with the data from +0.
/// </summary>
public sealed class ModuleEfbMemory : IEfbMemory
{
    private readonly ModuleMemory _memory;

    public ModuleEfbMemory(ModuleMemory memory) => _memory = memory;

    public bool TryReadInt(uint wasmAddress, out int value) => _memory.TryReadInt32(wasmAddress, out value);
    public bool TryReadByte(uint wasmAddress, out byte value) => _memory.TryReadByte(wasmAddress, out value);

    public bool TryReadPtr(uint wasmAddress, out uint value)
    {
        value = 0;
        if (!_memory.TryReadInt32(wasmAddress, out int v)) return false;
        value = unchecked((uint)v);
        return true;
    }

    public float ReadFloat(uint wasmAddress) =>
        _memory.TryReadInt32(wasmAddress, out int v) ? BitConverter.Int32BitsToSingle(v) : 0f;

    public string? ReadStdString(uint wasmAddress)
    {
        Span<byte> head = stackalloc byte[12];
        if (!_memory.TryRead(wasmAddress, head)) return null;
        bool isLong = (head[11] & 0x80) != 0;
        if (isLong)
        {
            uint ptr = (uint)(head[0] | head[1] << 8 | head[2] << 16 | head[3] << 24);
            int size = head[4] | head[5] << 8 | head[6] << 16 | head[7] << 24;
            if (size <= 0 || size > 4096 || ptr == 0) return null;
            var buffer = new byte[size];
            if (!_memory.TryRead(ptr, buffer)) return null;
            return Printable(buffer);
        }
        int shortSize = head[11] & 0x7f;
        if (shortSize is <= 0 or > 10) return shortSize == 0 ? "" : null;
        return Printable(head[..shortSize]);
    }

    /// <summary>The bytes as text when every one is printable ASCII (or tab/newline); null otherwise, so a
    /// non-string field never reads as garbage.</summary>
    private static string? Printable(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            if (!(b is >= 0x20 and < 0x7f || b is 9 or 10 or 13)) return null;
        return System.Text.Encoding.ASCII.GetString(bytes);
    }
}
