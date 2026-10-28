using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Utils;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace PanelSweep;

internal sealed class SimLinkException : Exception
{
    public SimLinkException(string message) : base(message) { }
}

/// <summary>
/// The sweep's whole contact with the sim, and the ONLY file that touches SimConnect. --dry-run never
/// reaches it.
///
/// Reads are SimConnect data definitions of "L:name" (units "number"), requested once per read, as the
/// app reads its L:vars. Writes are MobiFlight "MF.SimVars.Set.&lt;rpn&gt;" commands on the default
/// "MobiFlight.Command" client-data area, the channel the app's ExecuteCalculatorCode writes on: Set runs
/// the code ONCE. They are never sent as "MF.SimVars.Add", which REGISTERS an expression the module then
/// re-evaluates on every poll (measured 2026-10-28, docs/b757/evidence/flight-12.md: a write registered that
/// way re-ran every cycle and drove a trim to its stop). Every command carries a fresh "{seq} 0 * " prefix
/// so two identical writes are never byte-identical (CalcCommand).
/// </summary>
internal sealed class SimLink : IDisposable
{
    private enum AREA : uint { Command = 1000 }
    private enum CMD_DEF : uint { Command = 2000 }
    private enum DEF : uint { }
    private enum REQ : uint { }

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

    private sealed class Pending
    {
        public readonly ManualResetEventSlim Done = new(false);
        public double Value;
    }

    private readonly Microsoft.FlightSimulator.SimConnect.SimConnect _sc;
    private readonly TextWriter _out;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _opened = new(false);
    private readonly Dictionary<string, uint> _defs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, Pending> _pending = new();
    private Thread? _pump;
    private volatile bool _stop;
    private uint _nextDef = FirstDef;
    private uint _nextReq = FirstReq;
    private long _seq;
    private int _exceptions;

    /// <summary>True once the sim has told us it is quitting.</summary>
    public volatile bool SimQuit;

    /// <summary>How many SimConnect exception messages the sim has sent (a bad datum name, a dropped write, ...).</summary>
    public int Exceptions => Volatile.Read(ref _exceptions);

    private SimLink(Microsoft.FlightSimulator.SimConnect.SimConnect sc, TextWriter output)
    {
        _sc = sc;
        _out = output;
        _sc.OnRecvOpen += (s, e) => _opened.Set();
        _sc.OnRecvQuit += (s, e) => { SimQuit = true; _out.WriteLine("The sim is quitting."); };
        _sc.OnRecvException += (s, e) =>
        {
            Interlocked.Increment(ref _exceptions);
            _out.WriteLine($"SimConnect exception: {(SIMCONNECT_EXCEPTION)e.dwException} (sendId={e.dwSendID}, index={e.dwIndex})");
        };
        _sc.OnRecvSimobjectData += OnData;
    }

    /// <summary>
    /// Opens SimConnect, maps the MobiFlight command area and pings the module. Throws
    /// <see cref="SimLinkException"/> when the sim is not running or does not answer.
    /// </summary>
    public static SimLink Connect(TextWriter output)
    {
        EnsureNativeDll(output);

        Microsoft.FlightSimulator.SimConnect.SimConnect sc;
        try
        {
            sc = new Microsoft.FlightSimulator.SimConnect.SimConnect("PanelSweep", IntPtr.Zero, 0x0402, null, 0);
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

        // The area is the one MobiFlight's WASM module owns; the app maps it the same way
        // (MobiFlightWasmModule.MapClientDataAreas / SetupDataDefinitions).
        lock (link._gate)
        {
            sc.MapClientDataNameToID("MobiFlight.Command", AREA.Command);
            sc.AddToClientDataDefinition(CMD_DEF.Command, 0, CommandBytes, 0, 0);
        }
        link.SendCommand("MF.Ping");   // MobiFlight's documentation: the first command may be ignored
        Thread.Sleep(300);
        output.WriteLine("SimConnect open; MobiFlight command channel mapped.");
        return link;
    }

    /// <summary>
    /// Copies the SimConnect.dll for the running sim next to the exe, as the app does when it starts
    /// (Program.EnsureCorrectSimConnectDll). Must run before the first SimConnect call.
    /// </summary>
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
        _pump = new Thread(Pump) { IsBackground = true, Name = "PanelSweep SimConnect pump" };
        _pump.Start();
    }

    /// <summary>
    /// SimConnect is not thread-safe, so the pump and every call hold the same gate. A handler only
    /// sets an event or a counter, so holding the gate in a dispatch is harmless.
    /// </summary>
    private void Pump()
    {
        while (!_stop)
        {
            lock (_gate)
            {
                for (int i = 0; i < 16; i++)
                {
                    try { _sc.ReceiveMessage(); }
                    catch { break; }   // nothing queued (or a message that failed to dispatch)
                }
            }
            Thread.Sleep(10);
        }
    }

    private void OnData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (_pending.TryGetValue(data.dwRequestID, out var pending)
            && data.dwData is { Length: > 0 } && data.dwData[0] is SingleValue v)
        {
            pending.Value = v.value;
            pending.Done.Set();
        }
    }

    /// <summary>
    /// One L:var read fresh (a one-shot request), or null when no value arrived within
    /// <paramref name="timeoutMs"/>. With <paramref name="rebind"/> the definition is made again: a
    /// definition bound to an L:var that did not exist yet never delivers, even after the var is created
    /// (the app's RebindVariableDataDefinition).
    /// </summary>
    public double? ReadLVar(string name, int timeoutMs = 2000, bool rebind = false) =>
        Read("L:" + name, "number", timeoutMs, rebind);

    /// <summary>One stock SimVar read fresh (units "bool", "number", ...), or null.</summary>
    public double? ReadSimVar(string name, string units, int timeoutMs = 2000) =>
        Read(name, units, timeoutMs, rebind: false);

    private double? Read(string datumName, string units, int timeoutMs, bool rebind)
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
                _sc.RequestDataOnSimObject((REQ)reqId, (DEF)defId, UserObject,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
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

    /// <summary>
    /// One calculator write, run ONCE ("MF.SimVars.Set", never "Add"), made unique so MobiFlight cannot
    /// drop it as a repeat of the command before. Throws when it is longer than the command buffer
    /// rather than sending the cut-off half of a click.
    /// </summary>
    public void WriteCalc(string rpn)
    {
        if (SimQuit) throw new SimLinkException("The sim has quit.");
        string command = CalcCommand.Build(rpn, Interlocked.Increment(ref _seq));
        if (command.Length > CalcCommand.MaxChars)
            throw new SimLinkException($"The command is {command.Length} characters; the buffer takes {CalcCommand.MaxChars}, so it was not sent.");
        SendCommand(command);
    }

    private void SendCommand(string command)
    {
        try
        {
            lock (_gate)
            {
                _sc.SetClientData(AREA.Command, CMD_DEF.Command, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0,
                    new CommandData { command = command });
            }
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Sending a command failed: {ex.Message}");
        }
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
