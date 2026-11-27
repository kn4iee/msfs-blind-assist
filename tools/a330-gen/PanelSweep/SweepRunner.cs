using System.Diagnostics;
using System.Text;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace A330PanelSweep;

/// <summary>The app's runner core's view of the aircraft (<see cref="ISweepIo"/>), over the link. Every read is fresh.</summary>
internal sealed class LinkIo : ISweepIo
{
    /// <summary>How long one poll's read waits for its value; a read that never answers (an L:var the aircraft does not
    /// have) costs this much per poll, not the 5 s limit.</summary>
    private const int ReadTimeoutMs = 500;
    private const int SnapshotTimeoutMs = 1500;

    private readonly SimLink _link;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public LinkIo(SimLink link, bool calcAnswering)
    {
        _link = link;
        CalcAnswering = calcAnswering;
    }

    public bool CalcAnswering { get; }
    public bool TrySetInputEvent(string inputEvent, double value) => _link.SetInputEvent(inputEvent, value);
    public void Calc(string rpn) => _link.WriteCalc(rpn);
    public double? Read(string read) => _link.Read(read, ReadTimeoutMs);
    public IReadOnlyDictionary<string, double> Snapshot() => _link.Snapshot(SnapshotTimeoutMs);
    public IReadOnlyList<string> TakeWriteErrors() => _link.TakeWriteErrors();
    public long NowMs() => _clock.ElapsedMilliseconds;
    public void Sleep(int ms) { if (ms > 0) Thread.Sleep(ms); }
}

/// <summary>A tab-separated file flushed after every line, so a crash or Ctrl+C leaves everything so far.</summary>
internal sealed class LineFile : IDisposable
{
    private readonly StreamWriter _w;

    public LineFile(string path)
    {
        Path = path;
        _w = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string Path { get; }

    /// <summary>Text as it is (the journal's lines carry their own line ends).</summary>
    public void Write(string text) => _w.Write(text);

    public void WriteLine(string line) => _w.WriteLine(line);

    public void Dispose() => _w.Dispose();
}

/// <summary>The live sweep and the restore: the pre-flight, the files, the summary. Everything decided is the app's
/// runner core's (<see cref="SweepSession"/>); this only connects it to the link and the console.</summary>
internal static class SweepRunner
{
    private const string ProbeVar = "A330PANELSWEEP_PROBE";

    /// <summary>
    /// Before anything is written: the sim must say the aircraft is on the ground with its engines off (positive
    /// evidence of danger refuses; a state that cannot be read only warns, the owner confirmed it), and the aircraft
    /// must list the map's input events (the A330 is loaded). The calculator channel is probed with a test value
    /// written to an L:var of the tool's own and read back: when it does not answer, the sweep still runs, and every
    /// press and calculator line is "not sent". Returns null to refuse; else whether the calculator answers.
    /// </summary>
    public static bool? Preflight(SimLink link, IReadOnlyList<Control> controls, TextWriter w)
    {
        double? onGround = link.ReadSimVar("SIM ON GROUND", "bool", 2000);
        double? eng1 = link.ReadSimVar("ENG COMBUSTION:1", "bool", 2000);
        double? eng2 = link.ReadSimVar("ENG COMBUSTION:2", "bool", 2000);
        if (onGround is 0) { w.WriteLine("Refused: the sim says the aircraft is not on the ground."); return null; }
        if (eng1 is > 0.5 || eng2 is > 0.5) { w.WriteLine("Refused: the sim says an engine is running."); return null; }
        w.WriteLine(onGround is null || eng1 is null || eng2 is null
            ? "Warning: could not read the ground or engine state; going on the owner's confirmation."
            : "On the ground, engines off.");

        var events = controls.Select(c => c.Event).Where(e => !string.IsNullOrEmpty(e)).Distinct(StringComparer.Ordinal).ToList();
        int listed = events.Count(e => link.HasInputEvent(e!));
        w.WriteLine($"The aircraft lists {listed} of the map's {events.Count} input events.");
        if (listed * 2 < events.Count)
        {
            w.WriteLine("Refused: fewer than half of the map's input events are listed. Is the iniBuilds A330 loaded (and fully started)?");
            return null;
        }

        long nonce = Random.Shared.NextInt64(100_000, 9_000_000);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            link.WriteCalc($"{nonce + attempt} (>L:{ProbeVar})");
            Thread.Sleep(SweepTiming.LaggedMs);
            if (link.ReadLVar(ProbeVar, 2000, rebind: true) is double back && Math.Abs(back - (nonce + attempt)) < 0.001)
            {
                w.WriteLine("The MobiFlight calculator channel answers (a test value written and read back).");
                link.TakeWriteErrors();
                return true;
            }
        }
        link.TakeWriteErrors();
        w.WriteLine("Warning: the MobiFlight calculator channel did not answer (is its WASM module installed?). Input events are still");
        w.WriteLine("written; every press and calculator line will be 'not sent'.");
        return false;
    }

    /// <summary>The sweep: reads the starts, plans, writes the journal, sweeps. Exit 0 done, 5 a restore did not hold,
    /// 6 stopped on a link fault.</summary>
    public static int Sweep(SimLink link, bool calc, IReadOnlyList<Control> controls, IReadOnlyDictionary<string, string> denied,
        Func<ControlPlan, bool>? include, IReadOnlyList<string> extraVars, string outDir, Func<string?> cancelled, TextWriter w)
    {
        link.DefineSnapshot(SweepSnapshot.Names(controls, extraVars));
        string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(outDir);
        using var tsv = new LineFile(Path.Combine(outDir, $"sweep-{stamp}.tsv"));
        using var journal = new LineFile(Path.Combine(outDir, $"journal-{stamp}.tsv"));
        tsv.WriteLine(SweepTsv.Header);

        string? Stop() => link.Fault != null ? "stopped: SimConnect fault" : link.SimQuit ? "stopped: the sim quit" : cancelled();
        var session = new SweepSession(controls, denied, new LinkIo(link, calc),
            new SweepSinks(row => tsv.WriteLine(SweepTsv.Format(row)), journal.Write, w.WriteLine), Stop);

        w.WriteLine("Reading every control's start...");
        var prepared = session.Prepare(include);
        w.WriteLine($"{prepared.Plan.PlannedCount} controls to sweep, {prepared.Plan.SkippedCount} skipped; {prepared.Journal.Count} journaled in {journal.Path}.");
        var outcome = session.Run(prepared);

        w.WriteLine();
        w.WriteLine($"Done: {outcome.Swept} controls swept, {outcome.NotReached} not reached.");
        foreach (var (verdict, count) in outcome.Verdicts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            w.WriteLine($"  {verdict}: {count}");
        if (outcome.Noisy.Count > 0)
            w.WriteLine($"Moving with nothing written, so left out of the side effects: {string.Join(", ", outcome.Noisy.OrderBy(n => n, StringComparer.Ordinal))}.");
        w.WriteLine($"SimConnect exceptions: {link.Exceptions}.");
        if (outcome.StopReason != null) w.WriteLine($"STOPPED: {outcome.StopReason}.");
        if (outcome.RestoreFailures.Count > 0)
        {
            w.WriteLine("NOT PUT BACK (set these by hand, or run --restore with the journal):");
            foreach (string f in outcome.RestoreFailures) w.WriteLine("  " + f);
        }
        w.WriteLine($"Results: {tsv.Path}");
        w.WriteLine($"Journal: {journal.Path}");
        if (outcome.RestoreFailures.Count > 0) return 5;
        return link.Fault != null ? 6 : 0;
    }

    /// <summary><c>--restore</c>: puts every journaled control back, last first (a switch before the guard over it), each
    /// by its own starting value, and lists any that did not hold. Exit 0 all held, 5 some did not.</summary>
    public static int Restore(SimLink link, bool calc, IReadOnlyList<Control> controls, IReadOnlyDictionary<string, string> denied,
        IReadOnlyList<JournalEntry> entries, string outDir, TextWriter w)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(outDir);
        using var tsv = new LineFile(Path.Combine(outDir, $"restore-{stamp}.tsv"));
        tsv.WriteLine(SweepTsv.Header);
        var session = new SweepSession(controls, denied, new LinkIo(link, calc),
            new SweepSinks(row => tsv.WriteLine(SweepTsv.Format(row)), _ => { }, w.WriteLine));
        var byKey = controls.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var failed = new List<string>();
        foreach (var e in JournalRestore.RestoreOrder(entries))
        {
            if (!byKey.TryGetValue(e.Key, out var c)) { failed.Add($"{e.Key}: not a control of this map"); continue; }
            if (denied.ContainsKey(e.Key)) { failed.Add($"{e.Key}: denied, never written; put it back by hand"); continue; }
            bool held;
            try { held = session.Restore(e, c); }
            catch (SimLinkException ex) { held = false; w.WriteLine($"  {e.Key}: {ex.Message}"); }
            w.WriteLine($"{e.Key} ({c.Label}): {(held ? "back at its start" : "NOT put back")}");
            if (!held) failed.Add($"{e.Key} ({c.Label})");
        }
        w.WriteLine();
        w.WriteLine($"Restore: {entries.Count - failed.Count} of {entries.Count} back at their start.");
        foreach (string f in failed) w.WriteLine("  NOT PUT BACK: " + f);
        w.WriteLine($"Results: {tsv.Path}");
        return failed.Count > 0 ? 5 : 0;
    }
}
