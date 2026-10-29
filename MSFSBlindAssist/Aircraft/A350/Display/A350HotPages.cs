using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Which pages of the A350's memory are changing (pure; the reader feeds it each copy). Once the A350's heap
/// has grown a full copy takes many sim frames and tears (live 2026-10-28: 355 MB in 70-160 ms, nearly every
/// draw trapped), while only about 1% of the pages change between copies: the reader reads those again right
/// after the full copy, in about a millisecond, so the data the A350 is changing comes from one frame.
/// </summary>
public sealed class A350HotPages
{
    public const int PageSize = 4096;

    /// <summary>Copies a page stays hot after it last changed, so a page that changes every few seconds is
    /// read again too.</summary>
    public const int HotCopies = 8;

    private (uint Linear, int Length)[] _layout = Array.Empty<(uint, int)>();
    private int[] _regionOffset = Array.Empty<int>();
    private int[] _regionPage = Array.Empty<int>();
    private ulong[] _hash = Array.Empty<ulong>();
    private int[] _age = Array.Empty<int>();   // copies since the page last changed

    /// <summary>Takes a full copy (its regions back-to-back in <paramref name="buffer"/>) and returns the runs
    /// to read again: buffer offset, linear address, length; none for a first copy or a new layout.</summary>
    public IReadOnlyList<(int Offset, uint Linear, int Length)> Track(IReadOnlyList<(uint Linear, int Length)> regions, ReadOnlySpan<byte> buffer)
    {
        if (!SameLayout(regions))
        {
            Start(regions, buffer);
            return Array.Empty<(int, uint, int)>();
        }
        var runs = new List<(int, uint, int)>();
        for (int ri = 0; ri < _layout.Length; ri++)
        {
            var (linear, length) = _layout[ri];
            int offset = _regionOffset[ri], page = _regionPage[ri], runStart = -1;
            for (int p = 0; p < length; p += PageSize, page++)
            {
                ulong h = Hash(buffer.Slice(offset + p, Math.Min(PageSize, length - p)));
                if (h != _hash[page]) { _hash[page] = h; _age[page] = 0; }
                else if (_age[page] < HotCopies) _age[page]++;
                bool hot = _age[page] < HotCopies;
                if (hot && runStart < 0) runStart = p;
                else if (!hot && runStart >= 0) { runs.Add((offset + runStart, linear + (uint)runStart, p - runStart)); runStart = -1; }
            }
            if (runStart >= 0) runs.Add((offset + runStart, linear + (uint)runStart, length - runStart));
        }
        return runs;
    }

    /// <summary>Records what the runs read again hold, so the next copy compares with it.</summary>
    public void Reread(ReadOnlySpan<byte> buffer, IReadOnlyList<(int Offset, uint Linear, int Length)> runs)
    {
        foreach (var (offset, _, length) in runs)
        {
            int ri = RegionAt(offset);
            if (ri < 0) continue;
            int regionOffset = _regionOffset[ri], regionEnd = regionOffset + _layout[ri].Length;
            for (int p = offset; p < offset + length; p += PageSize)
            {
                int page = _regionPage[ri] + (p - regionOffset) / PageSize;
                ulong h = Hash(buffer.Slice(p, Math.Min(PageSize, regionEnd - p)));
                if (h != _hash[page]) { _hash[page] = h; _age[page] = 0; }
            }
        }
    }

    private bool SameLayout(IReadOnlyList<(uint Linear, int Length)> regions)
    {
        if (regions.Count != _layout.Length) return false;
        for (int i = 0; i < regions.Count; i++)
            if (regions[i] != _layout[i]) return false;
        return true;
    }

    private void Start(IReadOnlyList<(uint Linear, int Length)> regions, ReadOnlySpan<byte> buffer)
    {
        _layout = regions.ToArray();
        _regionOffset = new int[_layout.Length];
        _regionPage = new int[_layout.Length];
        int offset = 0, pages = 0;
        for (int i = 0; i < _layout.Length; i++)
        {
            _regionOffset[i] = offset;
            _regionPage[i] = pages;
            offset += _layout[i].Length;
            pages += (_layout[i].Length + PageSize - 1) / PageSize;
        }
        _hash = new ulong[pages];
        _age = new int[pages];
        Array.Fill(_age, HotCopies);   // cold until it is seen to change
        for (int i = 0; i < _layout.Length; i++)
        {
            int length = _layout[i].Length;
            for (int p = 0, page = _regionPage[i]; p < length; p += PageSize, page++)
                _hash[page] = Hash(buffer.Slice(_regionOffset[i] + p, Math.Min(PageSize, length - p)));
        }
    }

    private int RegionAt(int offset)
    {
        for (int i = 0; i < _layout.Length; i++)
            if (offset >= _regionOffset[i] && offset < _regionOffset[i] + _layout[i].Length) return i;
        return -1;
    }

    // Four independent multiply-xor-rotate lanes over the page's 64-bit words (about 28 ms for 372 MB, live), and only a
    // change has to be noticed (nothing is looked up by it). The rotate carries a difference down as well as up: without
    // it two flips of a word's top bit in one lane cancelled (review 2026-10-28).
    private static ulong Hash(ReadOnlySpan<byte> page)
    {
        const ulong Prime = 0x100000001B3;
        var w = MemoryMarshal.Cast<byte, ulong>(page);
        ulong a = 0x9E3779B97F4A7C15, b = 0xC2B2AE3D27D4EB4F, c = 0x165667B19E3779F9, d = 0x27D4EB2F165667C5;
        int i = 0;
        for (; i + 4 <= w.Length; i += 4)
        {
            a = System.Numerics.BitOperations.RotateLeft((a ^ w[i]) * Prime, 31);
            b = System.Numerics.BitOperations.RotateLeft((b ^ w[i + 1]) * Prime, 31);
            c = System.Numerics.BitOperations.RotateLeft((c ^ w[i + 2]) * Prime, 31);
            d = System.Numerics.BitOperations.RotateLeft((d ^ w[i + 3]) * Prime, 31);
        }
        for (; i < w.Length; i++) a = System.Numerics.BitOperations.RotateLeft((a ^ w[i]) * Prime, 31);
        for (int k = w.Length * 8; k < page.Length; k++) b = System.Numerics.BitOperations.RotateLeft((b ^ page[k]) * Prime, 31);
        return a ^ (b << 1 | b >> 63) ^ (c << 2 | c >> 62) ^ (d << 3 | d >> 61);
    }
}
