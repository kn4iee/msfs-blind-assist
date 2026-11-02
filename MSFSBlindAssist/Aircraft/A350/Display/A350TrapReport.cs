using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

public enum A350TrapKind { PastTheMemory, InsideTheMemory, Deadline, Other }

/// <summary>
/// A trap in the A350's draw code read as what it was (pure): where it died and why, from Wasmtime's message. A pointer past
/// the copy's memory is a torn copy, the copy having changed under the draw; two in 3.6 hours on 2026-10-31, each while the
/// flight plan changed, each retried and drawn. It only feeds the log: a trap is never spoken ([A350-2]).
/// </summary>
public sealed partial record A350TrapReport(string? Function, IReadOnlyList<string> Frames, ulong? Address, ulong? MemorySize,
    A350TrapKind Kind)
{
    [GeneratedRegex(@"^\s*\d+:\s+0x[0-9a-fA-F]+\s+-\s+(?:[^!\n]*!)?(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex Frame();

    [GeneratedRegex(@"memory fault at wasm address 0x([0-9a-fA-F]+) in linear memory of size 0x([0-9a-fA-F]+)")]
    private static partial Regex Fault();

    /// <summary>Null when the message is not Wasmtime's backtrace format.</summary>
    public static A350TrapReport? Parse(string message)
    {
        const string Head = "wasm backtrace:", Cause = "Caused by:";
        int head = message.IndexOf(Head, StringComparison.Ordinal);
        if (head < 0) return null;
        int cause = message.IndexOf(Cause, head, StringComparison.Ordinal);
        string backtrace = cause < 0 ? message[head..] : message[head..cause];
        string why = cause < 0 ? "" : message[cause..];
        var frames = Frame().Matches(backtrace).Select(m => WithoutArguments(m.Groups[1].Value)).ToList();
        ulong? address = null, size = null;
        var kind = why.Contains("interrupt", StringComparison.OrdinalIgnoreCase) ? A350TrapKind.Deadline : A350TrapKind.Other;
        if (Fault().Match(why) is { Success: true } fault)
        {
            address = ulong.Parse(fault.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            size = ulong.Parse(fault.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            kind = address >= size ? A350TrapKind.PastTheMemory : A350TrapKind.InsideTheMemory;
        }
        return new A350TrapReport(frames.FirstOrDefault(), frames, address, size, kind);
    }

    private static string WithoutArguments(string frame)
    {
        int open = frame.IndexOf('(');
        return open > 0 ? frame[..open] : frame;
    }

    /// <summary>"EFIS#0 trap in ND_Logic::drawModernConnections: a pointer past the copy (0x405d46e6, memory 0x19d40000), retried".</summary>
    public string Summary(string gauge, int index, bool retried = true)
    {
        string what = Kind switch
        {
            A350TrapKind.PastTheMemory => $"a pointer past the copy ({Numbers()})",
            A350TrapKind.InsideTheMemory => $"a fault inside the copy ({Numbers()})",
            A350TrapKind.Deadline => "the draw ran past its deadline",
            _ => "a trap",
        };
        return $"{gauge}#{index} trap{(Function is null ? "" : " in " + Function)}: {what}{(retried ? ", retried" : "")}";
    }

    private string Numbers() => FormattableString.Invariant($"0x{Address:x}, memory 0x{MemorySize:x}");
}
