using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The reader's numbers for its log (pure; the service feeds it): one line a minute in a350_display.log, what the copies
/// and draws cost and how many failed, beside what memory the machine had left, so a slow or silent display can be read
/// against what the reader was doing, and the sim's growth (17-35 GB an hour on long A350 flights) can be measured on any
/// pilot's PC. Each draw failure's text goes in the log too, each display's each error once a minute at most: a display
/// that fails twice a second would fill it, and the minute's line counts them all.
/// </summary>
public sealed class A350ReaderStats
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>How many display and error pairs are remembered: errors with an address in them are all distinct.</summary>
    public const int MaxFailureKeys = 64;

    private DateTime? _since;
    private int _copies, _draws, _retries, _torn, _leftOut, _copyBytesMb;
    private double _copyMs, _copyMax, _drawMs, _drawMax, _rereadMax;
    private long _headRoomMax;
    private string _slowest = "";
    private readonly SortedDictionary<string, int> _failed = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, int, string), DateTime> _logged = new();

    internal int FailureKeys => _logged.Count;

    /// <summary>A memory copy taken at <paramref name="at"/>: its time, its size, what it read a second time and the
    /// regions that failed to read.</summary>
    public void Copied(DateTime at, double milliseconds, long bytes, int rereadBytes, int regionsLeftOut)
    {
        _since ??= at;
        _copies++;
        _copyMs += milliseconds;
        _copyMax = Math.Max(_copyMax, milliseconds);
        _copyBytesMb = (int)(bytes >> 20);
        _rereadMax = Math.Max(_rereadMax, rereadBytes / 1048576.0);
        _leftOut += regionsLeftOut;
    }

    /// <summary>A draw of a display: its time, whether it failed, how many of its texts came out torn (a character no text
    /// holds: its string was freed in a slow copy) and the head room it allocated.</summary>
    public void Drew(string gauge, int index, double milliseconds, bool ok, int tornTexts, long headRoomBytes)
    {
        string display = $"{gauge}#{index}";
        _draws++;
        _drawMs += milliseconds;
        if (milliseconds > _drawMax)
        {
            _drawMax = milliseconds;
            _slowest = display;
        }
        _torn += tornTexts;
        _headRoomMax = Math.Max(_headRoomMax, headRoomBytes);
        if (!ok) _failed[display] = _failed.GetValueOrDefault(display) + 1;
    }

    /// <summary>A trapped draw tried again on a new copy.</summary>
    public void Retried() => _retries++;

    /// <summary>Whether a line is due: a minute has passed since the first copy it counts.</summary>
    public bool IsDue(DateTime now) => _since is { } since && now - since >= Every;

    /// <summary>The minute's line once one is due, else null; the counts then start again.</summary>
    public string? Due(DateTime now, A350MachineMemory? machine = null)
    {
        if (!IsDue(now) || _since is not { } since) return null;
        var line = FormattableString.Invariant($"{(now - since).TotalSeconds:0} s: {_copies} copies, avg {_copyMs / Math.Max(1, _copies):0} ms, max {_copyMax:0} ms, {_copyBytesMb} MB, {_rereadMax:0.0} MB read again, {_leftOut} regions left out; ")
            + FormattableString.Invariant($"{_draws} draws, avg {_drawMs / Math.Max(1, _draws):0} ms, max {_drawMax:0} ms ({_slowest}), head room max {_headRoomMax >> 20} MB; ")
            + (_failed.Count == 0 ? "no failures"
                : FormattableString.Invariant($"{_failed.Values.Sum()} failed ({string.Join(", ", _failed.Select(f => $"{f.Key} {f.Value}"))})"))
            + FormattableString.Invariant($", {_retries} retried, {_torn} torn texts")
            + (machine is { } m ? "; " + m.Line : "");
        _since = null;
        _copies = _draws = _retries = _torn = _leftOut = _copyBytesMb = 0;
        _copyMs = _copyMax = _drawMs = _drawMax = _rereadMax = 0;
        _headRoomMax = 0;
        _slowest = "";
        _failed.Clear();
        return line;
    }

    /// <summary>Whether this failure's text goes in the log now.</summary>
    public bool LogFailure(string gauge, int index, string error, DateTime now)
    {
        var key = (gauge, index, error);
        if (_logged.TryGetValue(key, out var at) && now - at < Every) return false;
        if (_logged.Count >= MaxFailureKeys) _logged.Clear();
        _logged[key] = now;
        return true;
    }
}

/// <summary>What memory the machine has left, and what the sim and MSFSBA hold, in bytes (0: could not be read).</summary>
public readonly record struct A350MachineMemory(long SimCommit, long FreeCommit, long FreePhysical, long OwnCommit)
{
    public string Line => FormattableString.Invariant(
        $"sim commit {Gb(SimCommit):0.0} GB, free commit {Gb(FreeCommit):0.0} GB, free physical {Gb(FreePhysical):0.0} GB, MSFSBA {Gb(OwnCommit):0.0} GB");

    private static double Gb(long bytes) => bytes / 1073741824.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>The machine now; nothing here throws (it only feeds a log line and a warning).</summary>
    public static A350MachineMemory Read()
    {
        long sim = 0, own = 0, freeCommit = 0, freePhysical = 0;
        try
        {
            foreach (var name in A350MemoryReader.ProcessNames)
            {
                var found = Process.GetProcessesByName(name);
                foreach (var p in found)
                {
                    sim = Math.Max(sim, p.PrivateMemorySize64);
                    p.Dispose();
                }
                if (sim > 0) break;
            }
            using var me = Process.GetCurrentProcess();
            own = me.PrivateMemorySize64;
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                freeCommit = (long)status.AvailPageFile;
                freePhysical = (long)status.AvailPhys;
            }
        }
        catch (Exception) { /* a figure that cannot be read reads 0 */ }
        return new A350MachineMemory(sim, freeCommit, freePhysical, own);
    }
}

/// <summary>
/// The machine running out of memory, said aloud (pure): a long A350 flight grows the sim's commit 17-35 GB an hour (live
/// 2026-10-28; not MSFSBA's doing, measured with it closed), and when the commit limit is reached the sim dies with no
/// warning, the flight lost. A sighted pilot sees nothing either, so this informs and does nothing else: said once below
/// <see cref="LowGb"/>, once more below <see cref="CriticalGb"/>, armed again only above <see cref="ReArmGb"/>. Its Ctrl+M
/// row mutes it.
/// </summary>
public sealed class A350MemoryWarning
{
    public const string MuteKey = "A350_MEMORY_LOW";
    public const string MuteLabel = "Low memory warning";
    public const int LowGb = 10, CriticalGb = 5, ReArmGb = 12;

    private const long Gb = 1L << 30;
    private int _said;   // 0 nothing, 1 low, 2 critical

    /// <summary>Free commit in bytes (0: not read); the sentence to say, if any.</summary>
    public string? Observe(long freeCommit)
    {
        if (freeCommit <= 0) return null;
        if (freeCommit >= ReArmGb * Gb) _said = 0;
        int level = freeCommit < CriticalGb * Gb ? 2 : freeCommit < LowGb * Gb ? 1 : 0;
        if (level <= _said) return null;
        _said = level;
        return Sentence((int)(freeCommit / Gb));
    }

    public static string Sentence(int gigabytes) => string.Create(CultureInfo.InvariantCulture,
        $"Memory low: {gigabytes} gigabytes left. The sim may close soon: save the flight in the OIS, then restart it.");
}
