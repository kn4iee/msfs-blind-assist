using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using MSFSBlindAssist.Utils;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace A330PanelSweep;

internal sealed class SimLinkException : Exception
{
    public SimLinkException(string message) : base(message) { }
}

/// <summary>
/// A one-shot calculator write as it goes on MobiFlight's channel: the "Set" command (run ONCE; "Add" registers an
/// expression MobiFlight re-runs on every poll), made textually unique with the inert "{seq} 0 * " prefix the app's
/// <c>IniA330Definition.Write</c> puts on every line ([SIM-17]): MobiFlight drops a byte-identical repeat, so a second
/// press would never run.
/// </summary>
internal static class CalcCommand
{
    public const string SetPrefix = "MF.SimVars.Set.";

    /// <summary>MobiFlight's command buffer is 1024 bytes with its NUL; a longer command is cut off silently.</summary>
    public const int MaxChars = 1023;

    public static string Build(string rpn, long seq) => SetPrefix + seq.ToString(CultureInfo.InvariantCulture) + " 0 * " + rpn;
}

/// <summary>
/// The sweep's whole contact with the sim, and the ONLY file that touches SimConnect; <c>--dry-run</c> never reaches
/// it. It copies the app's mechanisms, it does not link them (SimConnectManager is the app's):
/// <list type="bullet">
/// <item>Input events, as <c>SimConnectManager.TrySetInputEvent</c>: the aircraft's input events are enumerated once
/// (EnumerateInputEvents, paged until the last entry), each name kept with its hash; a set is SetInputEvent by that
/// hash; a name the aircraft did not list is refused (nothing sent). An event's own value is read with GetInputEvent.</item>
/// <item>Calculator lines on MobiFlight's default "MobiFlight.Command" client-data area (<see cref="CalcCommand"/>),
/// the channel the app's ExecuteCalculatorCode writes on.</item>
/// <item>Reads: an L:var or a simvar as a one-shot data definition request; a snapshot of many L:vars in blocks of
/// <see cref="SweepSnapshot.SlotsPerBlock"/>, their names added in ordinal order (SimConnect returns a definition's
/// datums sorted by name; the app's ContinuousBatchLayout keeps the same order), the last block filled with fillers
/// to its full size, as the app pads its batches.</item>
/// </list>
/// Each write records the id of the packet it went in, so a SimConnect exception the sim raises for it can be told
/// apart (<see cref="TakeWriteErrors"/>). A message handler that throws, or a receive that fails with anything but
/// SimConnect's own COMException, is never swallowed: it is kept in <see cref="Fault"/> and the sweep stops.
/// </summary>
internal sealed class SimLink : IDisposable
{
    private enum AREA : uint { Command = 1000 }
    private enum CMD_DEF : uint { Command = 2000 }
    private enum DEF : uint { }
    private enum REQ : uint { Enumerate = 3000 }

    private const uint FirstDef = 10000;
    private const uint FirstReq = 100000;
    private const int CommandBytes = 1024;
    private const uint UserObject = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    internal struct CommandData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CommandBytes)]
        public string command;
    }

    // Filled by SimConnect's marshaller (RegisterDataDefineStruct), never assigned in code (CS0649 is off in the csproj).
    internal struct SingleValue
    {
        public double value;
    }

    /// <summary>One snapshot block: exactly <see cref="SweepSnapshot.SlotsPerBlock"/> doubles, so every block's definition
    /// is that many datums (<see cref="SweepSnapshot.Blocks"/> fills the last with fillers, never read back). A shorter
    /// definition made SimConnect send fewer doubles than this struct, and the managed library's copy of it read past
    /// the message: the crash of 2026-11-27 23:05 ("Internal CLR error (0x80131506)" in the ByValArray marshaler
    /// under ReceiveMessage), which the app's own continuous batches avoid the same way.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct SnapshotBlock
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = SweepSnapshot.SlotsPerBlock)]
        public double[] values;
    }

    private sealed class Pending
    {
        public readonly ManualResetEventSlim Done = new(false);
        public double? Value;
        public double[]? Values;
    }

    private readonly Microsoft.FlightSimulator.SimConnect.SimConnect _sc;
    private readonly TextWriter _out;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _opened = new(false);
    private readonly ManualResetEventSlim _enumerated = new(false);
    private readonly Dictionary<string, uint> _defs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _hashes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, Pending> _pending = new();
    private readonly ConcurrentDictionary<uint, SIMCONNECT_EXCEPTION> _exceptionsBySendId = new();
    private readonly ConcurrentQueue<uint> _writeSendIds = new();
    private readonly List<(uint def, string[] names)> _snapshotBlocks = new();
    private Thread? _pump;
    private volatile bool _stop;
    private uint _nextDef = FirstDef;
    private uint _nextReq = FirstReq;
    private long _seq;
    private int _exceptions;
    private int _faults;
    private volatile string? _fault;

    /// <summary>True once the sim has told us it is quitting.</summary>
    public volatile bool SimQuit;

    /// <summary>How many SimConnect exception messages the sim has sent.</summary>
    public int Exceptions => Volatile.Read(ref _exceptions);

    /// <summary>The first fault on the pump thread, or null. Once set, the sweep puts back the control in progress and stops.</summary>
    public string? Fault => _fault;

    public int Faults => Volatile.Read(ref _faults);

    private SimLink(Microsoft.FlightSimulator.SimConnect.SimConnect sc, TextWriter output)
    {
        _sc = sc;
        _out = output;
        _sc.OnRecvOpen += (s, e) => Handle("open", () => _opened.Set());
        _sc.OnRecvQuit += (s, e) => Handle("quit", () => { SimQuit = true; _out.WriteLine("The sim is quitting."); });
        _sc.OnRecvException += (s, e) => Handle("exception", () =>
        {
            Interlocked.Increment(ref _exceptions);
            _exceptionsBySendId[e.dwSendID] = (SIMCONNECT_EXCEPTION)e.dwException;
        });
        _sc.OnRecvSimobjectData += (s, e) => Handle("data", () => OnData(e));
        _sc.OnRecvEnumerateInputEvents += (s, e) => Handle("input event list", () => OnEnumerate(e));
        _sc.OnRecvGetInputEvent += (s, e) => Handle("input event read", () => OnGetInputEvent(e));
    }

    private void Handle(string handler, Action body)
    {
        try { body(); }
        catch (Exception ex) { RecordFault($"the {handler} handler threw {ex.GetType().Name}: {ex.Message}"); }
    }

    private void RecordFault(string message)
    {
        if (Interlocked.Increment(ref _faults) > 1) return;
        _fault = message;
        try { _out.WriteLine($"SimConnect FAULT: {message}. The sweep stops once the control in progress is put back."); }
        catch { /* the console is gone: Fault still stops the run */ }
    }

    /// <summary>Opens SimConnect, maps the MobiFlight command area and enumerates the aircraft's input events. Throws
    /// <see cref="SimLinkException"/> when the sim is not running or does not answer.</summary>
    public static SimLink Connect(TextWriter output)
    {
        EnsureNativeDll(output);
        return Open(output);
    }

    /// <summary>The half of <see cref="Connect"/> that names SimConnect's types, never inlined: the JIT loads the managed
    /// SimConnect assembly when it compiles a method naming one of its types, and that assembly needs the native
    /// SimConnect.dll beside it, which <see cref="EnsureNativeDll"/> copies first (the 757 tool's first run, 2026-10-28).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SimLink Open(TextWriter output)
    {
        Microsoft.FlightSimulator.SimConnect.SimConnect sc;
        try
        {
            sc = new Microsoft.FlightSimulator.SimConnect.SimConnect("A330PanelSweep", IntPtr.Zero, 0x0402, null, 0);
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"SimConnect did not open: {ex.Message}. Is MSFS running?");
        }

        var link = new SimLink(sc, output);
        link.StartPump();
        if (!link._opened.Wait(TimeSpan.FromSeconds(5)))
        {
            link.Dispose();
            throw new SimLinkException("Timed out waiting for SimConnect to open.");
        }

        lock (link._gate)
        {
            sc.MapClientDataNameToID("MobiFlight.Command", AREA.Command);
            sc.AddToClientDataDefinition(CMD_DEF.Command, 0, CommandBytes, 0, 0);
            sc.EnumerateInputEvents(REQ.Enumerate);
        }
        link.SendCommand("MF.Ping");   // MobiFlight's documentation: the first command may be ignored
        if (!link._enumerated.Wait(TimeSpan.FromSeconds(15)))
            output.WriteLine("The aircraft's input event list did not finish within 15 s; going on with what came.");
        output.WriteLine($"SimConnect open; MobiFlight command channel mapped; {link.InputEventCount} input events listed.");
        return link;
    }

    /// <summary>Copies the SimConnect.dll for the running sim next to the exe, as the app does when it starts.</summary>
    private static void EnsureNativeDll(TextWriter output)
    {
        string dir = AppContext.BaseDirectory;
        string sim = SimulatorDetector.DetectRunningSimulator();
        if (sim == "Unknown")
            throw new SimLinkException("No Microsoft Flight Simulator process (FlightSimulator2024 or FlightSimulator) is running.");

        string sourceName = SimulatorDetector.GetSimConnectDllName(sim);
        string source = Path.Combine(dir, sourceName);
        string target = Path.Combine(dir, "SimConnect.dll");
        if (!File.Exists(source))
            throw new SimLinkException($"{sourceName} is missing from {dir}; rebuild the tool.");

        bool same = File.Exists(target)
            && new FileInfo(source).Length == new FileInfo(target).Length
            && Math.Abs((new FileInfo(source).LastWriteTime - new FileInfo(target).LastWriteTime).TotalSeconds) < 2;
        if (!same)
        {
            try { File.Copy(source, target, overwrite: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SimLinkException($"Could not copy {sourceName} to SimConnect.dll ({ex.Message}). Is another copy of the tool running?");
            }
        }
        output.WriteLine($"Simulator: {sim} (using {sourceName}).");
    }

    private void StartPump()
    {
        _pump = new Thread(Pump) { IsBackground = true, Name = "A330PanelSweep SimConnect pump" };
        _pump.Start();
    }

    /// <summary>SimConnect is not thread-safe, so the pump and every call hold the same gate.</summary>
    private void Pump()
    {
        while (!_stop)
        {
            lock (_gate)
            {
                for (int i = 0; i < 16; i++)
                {
                    try { _sc.ReceiveMessage(); }
                    catch (COMException) { break; }   // nothing queued, or the sim is closing
                    catch (Exception ex)
                    {
                        RecordFault($"receiving a SimConnect message threw {ex.GetType().Name}: {ex.Message}");
                        break;
                    }
                }
            }
            Thread.Sleep(10);
        }
    }

    // ---------------- input events ----------------

    /// <summary>How many input events the aircraft listed.</summary>
    public int InputEventCount { get { lock (_gate) return _hashes.Count; } }

    public bool HasInputEvent(string name) { lock (_gate) return _hashes.ContainsKey(name); }

    /// <summary>Runs on the pump thread, under the gate. The list comes in pages; the last says it is the last.</summary>
    private void OnEnumerate(SIMCONNECT_RECV_ENUMERATE_INPUT_EVENTS data)
    {
        if (data.rgData != null)
            foreach (var item in data.rgData)
                if (item is SIMCONNECT_INPUT_EVENT_DESCRIPTOR d && !string.IsNullOrEmpty(d.Name))
                    _hashes[d.Name] = d.Hash;
        if (data.dwEntryNumber + 1 >= data.dwOutOf) _enumerated.Set();
    }

    private void OnGetInputEvent(SIMCONNECT_RECV_GET_INPUT_EVENT data)
    {
        if (!_pending.TryGetValue(data.dwRequestID, out var pending)) return;
        if (data.Value is { Length: > 0 } v && v[0] != null)
        {
            try
            {
                double d = Convert.ToDouble(v[0], CultureInfo.InvariantCulture);
                if (double.IsFinite(d)) pending.Value = d;
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { /* not a number: no value */ }
        }
        pending.Done.Set();
    }

    /// <summary>SetInputEvent by the listed hash; false (nothing sent) when the aircraft does not list the event.</summary>
    public bool SetInputEvent(string name, double value)
    {
        if (SimQuit) throw new SimLinkException("The sim has quit.");
        try
        {
            lock (_gate)
            {
                if (!_hashes.TryGetValue(name, out ulong hash)) return false;
                _sc.SetInputEvent(hash, value);
                RecordSendId();
                return true;
            }
        }
        catch (Exception ex) when (ex is not SimLinkException)
        {
            throw new SimLinkException($"SetInputEvent {name} failed: {ex.Message}");
        }
    }

    /// <summary>An input event's own value read fresh (GetInputEvent), or null.</summary>
    public double? ReadInputEvent(string name, int timeoutMs)
    {
        if (SimQuit) return null;
        var pending = new Pending();
        uint reqId;
        try
        {
            lock (_gate)
            {
                if (!_hashes.TryGetValue(name, out ulong hash)) return null;
                reqId = _nextReq++;
                _pending[reqId] = pending;
                _sc.GetInputEvent((REQ)reqId, hash);
            }
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Reading B:{name} failed: {ex.Message}");
        }
        bool got = pending.Done.Wait(timeoutMs);
        _pending.TryRemove(reqId, out _);
        return got ? pending.Value : null;
    }

    // ---------------- reads ----------------

    /// <summary>A map read (<c>INI_x</c>, <c>A:NAME, unit</c>, <c>B:EVENT</c>) read fresh, or null.</summary>
    public double? Read(string read, int timeoutMs)
    {
        var (kind, name, units) = A330ControlMap.ParseReadKind(read);
        return kind switch
        {
            A330ControlMap.ReadKind.InputEvent => ReadInputEvent(name, timeoutMs),
            A330ControlMap.ReadKind.SimVar => ReadDatum(name, units, timeoutMs, rebind: false),
            _ => ReadDatum("L:" + name, "number", timeoutMs, rebind: false),
        };
    }

    /// <summary>One L:var read fresh; with <paramref name="rebind"/> the definition is made again (a definition bound to
    /// an L:var that did not exist yet never delivers, the app's RebindVariableDataDefinition).</summary>
    public double? ReadLVar(string name, int timeoutMs, bool rebind = false) => ReadDatum("L:" + name, "number", timeoutMs, rebind);

    public double? ReadSimVar(string name, string units, int timeoutMs) => ReadDatum(name, units, timeoutMs, rebind: false);

    private double? ReadDatum(string datumName, string units, int timeoutMs, bool rebind)
    {
        if (SimQuit) return null;
        string key = datumName + "|" + units;
        var pending = new Pending();
        uint reqId;
        try
        {
            lock (_gate)
            {
                if (rebind && _defs.TryGetValue(key, out uint old))
                {
                    _sc.ClearDataDefinition((DEF)old);
                    _defs.Remove(key);
                }
                if (!_defs.TryGetValue(key, out uint defId))
                {
                    defId = _nextDef++;
                    _sc.AddToDataDefinition((DEF)defId, datumName, units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    _sc.RegisterDataDefineStruct<SingleValue>((DEF)defId);
                    _defs[key] = defId;
                }
                reqId = _nextReq++;
                _pending[reqId] = pending;
                _sc.RequestDataOnSimObject((REQ)reqId, (DEF)defId, UserObject, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Reading {datumName} failed: {ex.Message}");
        }
        bool got = pending.Done.Wait(timeoutMs);
        _pending.TryRemove(reqId, out _);
        return got ? pending.Value : null;
    }

    private void OnData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (!_pending.TryGetValue(data.dwRequestID, out var pending) || data.dwData is not { Length: > 0 }) return;
        switch (data.dwData[0])
        {
            case SingleValue v: pending.Value = v.value; break;
            case SnapshotBlock b: pending.Values = b.values; break;
            default: return;
        }
        pending.Done.Set();
    }

    /// <summary>Defines the snapshot: <paramref name="names"/> (L:vars) in the core's blocks, ordinal order, each block
    /// filled to the struct's full size so no message is ever shorter than <see cref="SnapshotBlock"/>.</summary>
    public void DefineSnapshot(IReadOnlyList<string> names)
    {
        lock (_gate)
        {
            foreach (var block in SweepSnapshot.Blocks(names))
            {
                uint defId = _nextDef++;
                foreach (string? name in block)
                {
                    if (name != null)
                        _sc.AddToDataDefinition((DEF)defId, "L:" + name, "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    else
                        _sc.AddToDataDefinition((DEF)defId, SweepSnapshot.FillerDatum, SweepSnapshot.FillerUnits, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                }
                _sc.RegisterDataDefineStruct<SnapshotBlock>((DEF)defId);
                _snapshotBlocks.Add((defId, block.Where(n => n != null).Select(n => n!).ToArray()));
            }
        }
    }

    /// <summary>Every snapshot name's value now; a block that did not answer within <paramref name="timeoutMs"/> is left out.</summary>
    public IReadOnlyDictionary<string, double> Snapshot(int timeoutMs)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (SimQuit) return values;
        var asked = new List<(uint req, string[] names, Pending pending)>();
        try
        {
            lock (_gate)
            {
                foreach (var (defId, names) in _snapshotBlocks)
                {
                    uint reqId = _nextReq++;
                    var pending = new Pending();
                    _pending[reqId] = pending;
                    _sc.RequestDataOnSimObject((REQ)reqId, (DEF)defId, UserObject, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                    asked.Add((reqId, names, pending));
                }
            }
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Taking a snapshot failed: {ex.Message}");
        }
        foreach (var (req, names, pending) in asked)
        {
            if (pending.Done.Wait(timeoutMs) && pending.Values is { } got)
                for (int i = 0; i < names.Length && i < got.Length; i++) values[names[i]] = got[i];
            _pending.TryRemove(req, out _);
        }
        return values;
    }

    // ---------------- calculator writes ----------------

    /// <summary>One calculator line, run ONCE and made unique. Throws when it would not fit the buffer, rather than send half.</summary>
    public void WriteCalc(string rpn)
    {
        if (SimQuit) throw new SimLinkException("The sim has quit.");
        string command = CalcCommand.Build(rpn, Interlocked.Increment(ref _seq));
        if (command.Length > CalcCommand.MaxChars)
            throw new SimLinkException($"The command is {command.Length} characters; the buffer takes {CalcCommand.MaxChars}, so it was not sent.");
        SendCommand(command);
        lock (_gate) RecordSendId();
    }

    private void SendCommand(string command)
    {
        try
        {
            lock (_gate)
                _sc.SetClientData(AREA.Command, CMD_DEF.Command, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, new CommandData { command = command });
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Sending a command failed: {ex.Message}");
        }
    }

    /// <summary>Under the gate, right after a write, so no other packet went in between.</summary>
    private void RecordSendId()
    {
        try { _writeSendIds.Enqueue(_sc.GetLastSentPacketID()); }
        catch (COMException) { /* SimConnect would not say: that write's refusals cannot be told apart */ }
    }

    /// <summary>The sim's refusals of the writes sent since the last call (their exception names), and forgets them.
    /// An exception comes a little after its packet: ask after the write has settled.</summary>
    public IReadOnlyList<string> TakeWriteErrors()
    {
        var errors = new List<string>();
        while (_writeSendIds.TryDequeue(out uint id))
            if (id != 0 && _exceptionsBySendId.TryRemove(id, out var kind))
                errors.Add(kind.ToString().Replace("SIMCONNECT_EXCEPTION_", "", StringComparison.Ordinal));
        return errors;
    }

    public void Dispose()
    {
        _stop = true;
        _pump?.Join(500);
        lock (_gate)
        {
            try { _sc.Dispose(); } catch { /* the sim may already be gone */ }
        }
    }
}
