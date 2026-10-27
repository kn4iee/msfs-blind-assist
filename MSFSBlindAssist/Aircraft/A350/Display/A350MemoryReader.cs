using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static MSFSBlindAssist.Aircraft.A350.Display.A350MemoryRegions;
using Region = MSFSBlindAssist.Aircraft.A350.Display.A350MemoryRegions.Region;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Copies the A350's linear memory out of the sim, read-only [A350-1]: the process is opened with
/// QUERY_INFORMATION and VM_READ only. Locating lists every region without reading, keeps allocations
/// shaped like a WASM memory (4 GB or more) and reads the three anchor strings at their linear
/// addresses (about 4 s, once per A350 load). A snapshot re-lists only that allocation's regions and reads
/// them back-to-back into one reused buffer [A350-2] (about 8-60 ms). Port of tools/a350-probe
/// fastlocate.py and fastdump2.py.
/// </summary>
public sealed class A350MemoryReader : IDisposable
{
    public static readonly string[] ProcessNames = { "FlightSimulator2024", "FlightSimulator" };
    public const ulong WasmMemorySpan = 1UL << 32;

    private const uint ProcessQueryInformation = 0x0400, ProcessVmRead = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation64
    {
        public ulong BaseAddress, AllocationBase;
        public uint AllocationProtect, Alignment1;
        public ulong RegionSize;
        public uint State, Protect, Type, Alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(IntPtr h, UIntPtr address, out MemoryBasicInformation64 info, UIntPtr length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadProcessMemory(IntPtr h, UIntPtr address, byte* buffer, UIntPtr size, out UIntPtr read);

    private readonly IReadOnlyList<(string Text, int Linear)> _anchors;
    private readonly int? _pid;
    private readonly ulong _minSpan;
    private readonly A350MemoryCopy _copy = new();
    private IntPtr _handle;
    private byte[] _buffer = Array.Empty<byte>();

    /// <summary>The A350 allocation's base in the sim, once located.</summary>
    public ulong Base { get; private set; }

    public A350MemoryReader(IReadOnlyList<(string Text, int Linear)> anchors, int? pid = null, ulong minSpan = WasmMemorySpan)
    {
        _anchors = anchors;
        _pid = pid;
        _minSpan = minSpan;
    }

    private static int? FindSim()
    {
        foreach (var name in ProcessNames)
        {
            var p = Process.GetProcessesByName(name).FirstOrDefault();
            if (p != null) return p.Id;
        }
        return null;
    }

    private IEnumerable<Region> RegionsFrom(ulong address, ulong limit = 0x7FFF_FFFF_FFFF)
    {
        while (address < limit)
        {
            if (VirtualQueryEx(_handle, (UIntPtr)address, out var m, (UIntPtr)Marshal.SizeOf<MemoryBasicInformation64>()) == UIntPtr.Zero) yield break;
            yield return new Region(m.BaseAddress, m.RegionSize, m.State, m.Protect, m.AllocationBase);
            if (m.RegionSize == 0) yield break;
            address = m.BaseAddress + m.RegionSize;
        }
    }

    private unsafe bool ReadExact(ulong address, byte[] into, int offset, int count)
    {
        fixed (byte* p = into)
            return ReadProcessMemory(_handle, (UIntPtr)address, p + offset, (UIntPtr)count, out var read) && (int)read == count;
    }

    private bool AnchorsAt(ulong allocationBase)
    {
        foreach (var (text, lin) in _anchors)
        {
            var want = Encoding.ASCII.GetBytes(text + "\0");
            var got = new byte[want.Length];
            if (!ReadExact(allocationBase + (ulong)lin, got, 0, got.Length) || !got.AsSpan().SequenceEqual(want)) return false;
        }
        return true;
    }

    /// <summary>Opens the sim read-only and finds the A350's allocation; false when the sim or the A350 is
    /// not there. Slow (every region is listed): run it off the UI thread.</summary>
    public bool TryLocate()
    {
        CloseProcess();
        int? pid = _pid ?? FindSim();
        if (pid == null) return false;
        _handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid.Value);
        if (_handle == IntPtr.Zero) return false;
        foreach (var b in Candidates(RegionsFrom(0), _minSpan))
            if (AnchorsAt(b)) { Base = b; return true; }
        CloseProcess();
        return false;
    }

    /// <summary>The anchors still read at the located base (the A350 or the sim has not reloaded).</summary>
    public bool IsStillValid() => _handle != IntPtr.Zero && Base != 0 && AnchorsAt(Base);

    /// <summary>A fresh copy of the allocation's readable regions, in the reader's reused copy (valid until
    /// the next snapshot). A region that fails to read is left out.</summary>
    public A350MemoryCopy Snapshot()
    {
        if (_handle == IntPtr.Zero || Base == 0) throw new InvalidOperationException("not located");
        var regions = ReadableOf(RegionsFrom(Base), Base);
        long total = regions.Sum(r => (long)r.Size);
        if (_buffer.Length < total) _buffer = new byte[total + (8 << 20)];
        var kept = new List<(uint, int)>(regions.Count);
        int off = 0;
        foreach (var r in regions)
        {
            if (!ReadExact(r.Base, _buffer, off, (int)r.Size)) continue;
            kept.Add(((uint)(r.Base - Base), (int)r.Size));
            off += (int)r.Size;
        }
        _copy.Set(kept, _buffer);
        return _copy;
    }

    private void CloseProcess()
    {
        if (_handle != IntPtr.Zero) CloseHandle(_handle);
        _handle = IntPtr.Zero;
        Base = 0;
    }

    public void Dispose() => CloseProcess();
}
