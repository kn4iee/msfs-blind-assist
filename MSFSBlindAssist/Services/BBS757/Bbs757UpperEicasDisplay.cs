using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The 757's upper EICAS as a reader of <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>: its layouts from the
/// shared module session's builds, a <see cref="Bbs757UpperEicasReader"/> per located build on its own lease, the gate
/// (<see cref="Bbs757UpperEicasGate"/>: each engine's N1 against the simulator's), the screen's own power, and its words
/// (<see cref="Bbs757UpperEicasSpeech"/>) on three Ctrl+M rows: the messages (which also carry the monitor's status
/// lines), the thrust reference and the engine gauge colors. Called on the UI thread only.
/// </summary>
public sealed class Bbs757UpperEicasDisplay : IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>
{
    public const string Name = "EICAS";
    public const string LogChannel = "bbs757-upper-eicas";
    public const int TickMs = 250;

    /// <summary>The Ctrl+M row of the messages' lines and the monitor's status lines.</summary>
    public const string MessagesKey = "MSFSBA_BBS757_EICAS_MESSAGES";
    public const string MessagesLabel = "EICAS messages";

    /// <summary>The Ctrl+M row of the thrust reference's line.</summary>
    public const string ThrustKey = "MSFSBA_BBS757_THRUST_REFERENCE";
    public const string ThrustLabel = "Thrust reference";

    /// <summary>The Ctrl+M row of the N1 and EGT colors' line (R5).</summary>
    public const string ColorsKey = "MSFSBA_BBS757_ENGINE_COLORS";
    public const string ColorsLabel = "Engine gauge colors";

    /// <summary>The row of the monitor's status lines ("unavailable", "stopped", "resumed"): the messages'.</summary>
    public const string MuteKey = MessagesKey;

    /// <summary>The simulator's variables the gate compares memory with, then the manual thrust reference flag the
    /// sampler takes where BlueBird's draw does (S10).</summary>
    public static IReadOnlyList<string> GateKeys { get; } = Array.AsReadOnly(new[]
    {
        Bbs757UpperEicasGate.N1LeftKey, Bbs757UpperEicasGate.N1RightKey, Bbs757UpperEicasReader.ManualKey,
    });

    /// <summary>The variable behind each of <see cref="GateKeys"/>: the two N1s are stock simulator variables in percent,
    /// never L:vars of that name ([VAR-2]); the manual flag is BlueBird's L:var.</summary>
    public static IReadOnlyList<Bbs757GateVariable> GateVariables { get; } = Array.AsReadOnly(new[]
    {
        Stock(Bbs757UpperEicasGate.N1LeftKey), Stock(Bbs757UpperEicasGate.N1RightKey),
        Bbs757GateVariable.LVar(Bbs757UpperEicasReader.ManualKey),
    });

    string IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.Name => Name;

    string IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.MuteKey => MuteKey;

    string IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.LogChannel => LogChannel;

    int IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.TickMs => TickMs;

    IReadOnlyList<string> IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.GateKeys => GateKeys;

    IReadOnlyList<Bbs757GateVariable> IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.GateVariables => GateVariables;

    /// <summary>The distinct upper EICAS layouts of the analyzed builds, in file order; a build without one is skipped
    /// (another reader's layout found it).</summary>
    public IReadOnlyList<Bbs757UpperEicasLayout> LayoutsIn(ModuleAnalysis analysis)
    {
        var layouts = new List<Bbs757UpperEicasLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Bbs757UpperEicasLayout>(Bbs757Module.UpperEicas) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        return layouts;
    }

    /// <summary>A sampler when <paramref name="module"/>'s build has <paramref name="layout"/>, holding its own lease of
    /// the shared simulator handle; else null. A handle closed meanwhile throws <see cref="ObjectDisposedException"/>,
    /// which the monitor counts as "not found yet".</summary>
    public IBbs757DisplaySampler<Bbs757UpperEicasReading>? SamplerFor(LocatedModule module, Bbs757UpperEicasLayout layout)
    {
        if (!layout.Equals(module.Build.Layout<Bbs757UpperEicasLayout>(Bbs757Module.UpperEicas))) return null;
        return new Bbs757UpperEicasReader(module.Memory, module.AcquireLease(), layout);
    }

    public bool Passes(IReadOnlyList<(long AtMs, Bbs757UpperEicasReading Reading)> history, Bbs757SimValues sim) =>
        Bbs757UpperEicasGate.Passes(history, sim);

    /// <summary>The screen's own power, as the reading decoded it: never null, so a dark screen is never compared and its
    /// power-up messages are a silent baseline (S4).</summary>
    public bool? Lit(Bbs757UpperEicasReading reading, Bbs757SimValues sim) => reading.Powered;

    /// <summary>Each line on its own row; <paramref name="withPrefix"/> changes nothing (R32): "EICAS warning: …" names the
    /// display, and "Thrust reference: …" has no prefix.</summary>
    public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757UpperEicasReading heard, Bbs757UpperEicasReading now, bool withPrefix) =>
        Bbs757UpperEicasSpeech.Changes(heard, now);

    public IReadOnlyList<string> Rows(Bbs757UpperEicasReading reading) => Bbs757UpperEicasSpeech.Rows(reading);

    public string DescribeAll(Bbs757UpperEicasReading reading) => Bbs757UpperEicasSpeech.DescribeAll(reading);

    private static Bbs757GateVariable Stock(string key) => new(key, key, SimVarType.SimVar, "percent");
}
