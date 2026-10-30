namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The A350's module stopping (X7): the module bumps <c>L:INI_UPDATELOOP_HEARTBEAT</c> once every 256 passes of its update
/// loop (<c>UpdateLoopLogger::BeginLoop</c> calls <c>TickHeartbeat()</c>; about twice a second live 2026-10-30), and
/// iniBuilds' own crash report says "The WASM module has encountered a fatal error and stopped responding" once it stops for
/// 5 s. A stopped module leaves its displays frozen and its controls dead, and MSFSBA's display reader would go on reading
/// its last frame as live, so the pilot is told, once, and told again when it beats. The heartbeat must stand still while
/// both the sim's clock (ABSOLUTE TIME, which stands still while the sim is paused, so a pause is never a stop) and the wall
/// clock (so a clock jump or a high sim rate is never one either) run on for <see cref="StoppedAfter"/>, twice iniBuilds'
/// 5 s, so a very low frame rate is not taken for a stop.
/// </summary>
public sealed class A350ModuleWatchdog
{
    /// <summary>The heartbeat's key, which is the call-out's Ctrl+M row, and the sim's clock's key (no row).</summary>
    public const string HeartbeatKey = "A350_MODULE_HEARTBEAT", SimTimeKey = "A350_SIM_TIME";
    public const string HeartbeatVar = "INI_UPDATELOOP_HEARTBEAT";
    public const string MuteLabel = "A350 module stopped responding";

    public static readonly TimeSpan StoppedAfter = TimeSpan.FromSeconds(10);

    public const string Stopped = "A350 module stopped responding: its displays and controls no longer work";
    public const string Responding = "A350 module responding again";

    private double? _beat;        // the heartbeat last seen
    private double _beatSimTime;  // the sim's clock when it was first seen, seconds
    private long _beatWall;       // the wall clock then, ms
    private bool _stopped;

    /// <summary>One look, at a batch end: the heartbeat, the sim's clock (seconds) and the wall clock (ms); the sentence to
    /// say, or null. A changed heartbeat, or the sim's clock gone back (a flight load), starts the watch again.</summary>
    public string? Observe(double? heartbeat, double? simSeconds, long wallMs)
    {
        if (heartbeat is not double beat || simSeconds is not double sim)
            return null;
        if (_beat != beat || sim < _beatSimTime)
        {
            _beat = beat;
            _beatSimTime = sim;
            _beatWall = wallMs;
            if (!_stopped)
                return null;
            _stopped = false;
            return Responding;
        }
        if (_stopped || sim - _beatSimTime < StoppedAfter.TotalSeconds || wallMs - _beatWall < StoppedAfter.TotalMilliseconds)
            return null;
        _stopped = true;
        return Stopped;
    }

    public void Reset()
    {
        _beat = null;
        _stopped = false;
    }
}
