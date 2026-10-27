namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>Which of a process's memory regions hold the A350's linear memory (pure; the reader feeds it
/// what VirtualQueryEx lists).</summary>
public static class A350MemoryRegions
{
    public readonly record struct Region(ulong Base, ulong Size, uint State, uint Protect, ulong AllocationBase);

    private const uint MemFree = 0x10000, MemCommit = 0x1000, PageNoAccess = 0x01, PageGuard = 0x100;

    /// <summary>Allocation bases whose span (from the base to the end of their last region) is at least
    /// <paramref name="minSpan"/>: a WASM linear memory is a reservation of 4 GB or more.</summary>
    public static IReadOnlyList<ulong> Candidates(IEnumerable<Region> regions, ulong minSpan)
    {
        var end = new Dictionary<ulong, ulong>();
        foreach (var r in regions)
        {
            if (r.State == MemFree || r.AllocationBase == 0) continue;
            ulong e = r.Base + r.Size;
            if (!end.TryGetValue(r.AllocationBase, out var cur) || e > cur) end[r.AllocationBase] = e;
        }
        return end.Where(kv => kv.Value - kv.Key >= minSpan).Select(kv => kv.Key).ToList();
    }

    /// <summary>The committed, readable regions of one allocation, walking from its base until a region
    /// belongs to another allocation.</summary>
    public static IReadOnlyList<Region> ReadableOf(IEnumerable<Region> fromBase, ulong allocationBase)
    {
        var list = new List<Region>();
        foreach (var r in fromBase)
        {
            if (r.AllocationBase != allocationBase) break;
            if (r.State == MemCommit && (r.Protect & (PageGuard | PageNoAccess)) == 0 && r.Protect != 0) list.Add(r);
        }
        return list;
    }
}

/// <summary>
/// One consistent copy of the A350's linear memory: its readable regions, each at its linear address,
/// laid back-to-back in one buffer. The reader reuses one copy (valid until its next snapshot); the file
/// format is the prototype's (u32 linear address, u32 length, bytes, repeated).
/// </summary>
public sealed class A350MemoryCopy
{
    private readonly List<(uint Linear, int Length)> _regions = new();
    private readonly List<int> _offsets = new();

    public IReadOnlyList<(uint Linear, int Length)> Regions => _regions;
    public byte[] Buffer { get; private set; } = Array.Empty<byte>();

    /// <summary>The end of the highest region: the linear memory the engine must hold.</summary>
    public uint End { get; private set; }

    public void Set(IEnumerable<(uint Linear, int Length)> regions, byte[] buffer)
    {
        _regions.Clear(); _offsets.Clear();
        int off = 0; uint end = 0;
        foreach (var r in regions)
        {
            _regions.Add(r); _offsets.Add(off);
            off += r.Length;
            end = Math.Max(end, r.Linear + (uint)r.Length);
        }
        if (off > buffer.Length) throw new ArgumentException("buffer shorter than its regions");
        Buffer = buffer;
        End = end;
    }

    public ReadOnlySpan<byte> RegionBytes(int i) => Buffer.AsSpan(_offsets[i], _regions[i].Length);

    /// <summary>Bytes at a linear address inside one region; empty when outside the copy.</summary>
    public ReadOnlySpan<byte> Read(uint linear, int count)
    {
        for (int i = 0; i < _regions.Count; i++)
        {
            var (lin, len) = _regions[i];
            if (linear >= lin && linear + (uint)count <= lin + (uint)len)
                return Buffer.AsSpan(_offsets[i] + (int)(linear - lin), count);
        }
        return ReadOnlySpan<byte>.Empty;
    }

    public void Save(string path)
    {
        using var f = File.Create(path);
        for (int i = 0; i < _regions.Count; i++)
        {
            f.Write(BitConverter.GetBytes(_regions[i].Linear));
            f.Write(BitConverter.GetBytes(_regions[i].Length));
            f.Write(RegionBytes(i));
        }
    }

    public static A350MemoryCopy Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var regions = new List<(uint, int)>();
        var buffer = new byte[bytes.Length];
        int i = 0, off = 0;
        while (i + 8 <= bytes.Length)
        {
            uint lin = BitConverter.ToUInt32(bytes, i);
            int len = (int)BitConverter.ToUInt32(bytes, i + 4);
            Array.Copy(bytes, i + 8, buffer, off, len);
            regions.Add((lin, len));
            off += len; i += 8 + len;
        }
        var copy = new A350MemoryCopy();
        copy.Set(regions, buffer);
        return copy;
    }
}
