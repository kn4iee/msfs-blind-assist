using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// One read of the 757 FMA, as the words the ADI draws (null = blank, <see cref="Bbs757WordTable.Unrecognised"/>
/// for a value with no word). <see cref="Corrupt"/> is a torn or out-of-range reading, never spoken.
/// </summary>
public readonly record struct Bbs757FmaReading(string? AutoThrottle, string? Pitch, string? PitchArmed, string? Roll, string? RollArmed, bool Corrupt)
{
    /// <summary>The columns in ADI order (A/T, roll, pitch, roll armed, pitch armed) as (label, word) pairs,
    /// blank columns dropped; used for the window read-out and change detection.</summary>
    public IEnumerable<(string Label, string Word)> Columns()
    {
        if (AutoThrottle is { } a) yield return ("A/T", a);
        if (Roll is { } r) yield return ("Roll", r);
        if (Pitch is { } p) yield return ("Pitch", p);
        if (RollArmed is { } ra) yield return ("Roll armed", ra);
        if (PitchArmed is { } pa) yield return ("Pitch armed", pa);
    }
}

/// <summary>
/// Reads FMA samples from the 757 module's memory through a derived layout (BBS757-D2: no hardcoded offset).
/// The Captain PFD is the first pointer in the gauge manager (<see cref="Bbs757FmaLayout.ManagerBase"/>, within
/// <see cref="Bbs757FmaLayout.ManagerSpan"/>) whose object's first word is the pfd class vptr; its autoflight and
/// thrust objects hang off it. Owns its memory source. Every read is bounds-checked against committed module
/// memory (WASM-4), re-walked each call.
/// </summary>
public sealed class Bbs757FmaReader : IDisposable
{
    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Bbs757FmaLayout Layout { get; }

    public Bbs757FmaReader(ModuleMemory memory, IDisposable ownedSource, Bbs757FmaLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
    }

    /// <summary>The Captain PFD object: the first manager pointer whose target carries the pfd vptr. False when
    /// none in the manager's span does (the module is not where the anchor said, or not ready).</summary>
    public bool TryFindPfd(out uint pfd)
    {
        pfd = 0;
        for (uint off = 0; off + 4 <= Layout.ManagerSpan; off += 4)
        {
            if (!_memory.TryReadInt32(Layout.ManagerBase + off, out int pv)) continue;
            uint p = unchecked((uint)pv);
            if (p == 0 || !_memory.IsInsideModule(p, 4)) continue;
            if (_memory.TryReadInt32(p, out int vptr) && unchecked((uint)vptr) == Layout.PfdVptr)
            {
                pfd = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>False when the PFD cannot be found or a read falls outside the module; otherwise a reading, with
    /// <see cref="Bbs757FmaReading.Corrupt"/> set when a raw field value is negative or absurdly large.</summary>
    public bool TryRead(out Bbs757FmaReading reading)
    {
        reading = default;
        if (!TryFindPfd(out uint pfd)) return false;
        if (!Int(pfd, Layout.PfdToAfds, out int afdsPtr) || !Int(pfd, Layout.PfdToTmc, out int tmcPtr)) return false;
        uint afds = unchecked((uint)afdsPtr), tmc = unchecked((uint)tmcPtr);
        if (afds == 0 || tmc == 0) return false;

        if (!Int(tmc, Layout.AtMode, out int at) || !Int(afds, Layout.Pitch, out int pitch)
            || !Int(afds, Layout.PitchArmed, out int pitchArmed) || !Int(afds, Layout.Roll, out int roll)
            || !Int(afds, Layout.RollArmed, out int rollArmed))
            return false;

        bool corrupt = IsCorrupt(at) || IsCorrupt(pitch) || IsCorrupt(pitchArmed) || IsCorrupt(roll) || IsCorrupt(rollArmed);
        reading = new Bbs757FmaReading(
            Layout.AtWords.Lookup(at), Layout.PitchWords.Lookup(pitch), Layout.PitchArmedWords.Lookup(pitchArmed),
            Layout.RollWords.Lookup(roll), Layout.RollArmedWords.Lookup(rollArmed), corrupt);
        return true;
    }

    /// <summary>A raw field value that cannot be a mode index: negative, or far beyond any word table.</summary>
    private static bool IsCorrupt(int v) => v < 0 || v > 255;

    private bool Int(uint baseAddr, int offset, out int v) => _memory.TryReadInt32(baseAddr + (uint)offset, out v);

    public void Dispose() => _ownedSource.Dispose();
}
