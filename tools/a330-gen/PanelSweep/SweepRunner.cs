using System.Diagnostics;
using System.Text;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace A330PanelSweep;

/// <summary>
/// The owner's second Ctrl+C: stop at once, but never inside a write. The handler only sets <see cref="Asked"/>; the
/// main thread exits between calls (<see cref="LinkIo"/>), so a push is never left without its release.
/// </summary>
internal static class ExitNow
{
    public static volatile bool Asked;
}

/// <summary>The app's runner core's view of the aircraft (<see cref="ISweepIo"/>), over the link. Every read is fresh.</summary>
internal sealed class LinkIo : ISweepIo
{
    private int _inCall;
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

    /// <summary>One write as a whole; a second Ctrl+C that came during it exits once it has ended.</summary>
    public void Uninterrupted(Action call)
    {
        _inCall++;
        try { call(); }
        finally { _inCall--; }
        ExitIfAsked();
    }

    public void Sleep(int ms)
    {
        ExitIfAsked();
        if (ms > 0) Thread.Sleep(ms);
        ExitIfAsked();
    }

    /// <summary>Between calls only (a press's hold sleeps inside its call): exit on the owner's second Ctrl+C.</summary>
    private void ExitIfAsked()
    {
        if (_inCall > 0 || !ExitNow.Asked) return;
        Console.WriteLine("Stopped at once, between two writes. Put back what was left with --restore and the journal.");
        Environment.Exit(130);
    }
}

/// <summary>A tab-separated file flushed after every line, so a crash or Ctrl+C leaves everything so far. Always a new
/// file: it is opened create-new, never over an earlier run's.</summary>
internal sealed class LineFile : IDisposable
{
    private readonly StreamWriter _w;

    private LineFile(string path)
    {
        Path = path;
        _w = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>One run's files in <paramref name="dir"/>, <c>&lt;stem&gt;-&lt;stamp&gt;.tsv</c> for each stem, with the
    /// same suffix when any is taken (<see cref="SweepTsv.FreeNames"/>). A file that appears between the check and the
    /// open moves them all to the next suffix.</summary>
    public static LineFile[] OpenNew(string dir, params string[] stems)
    {
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var names = SweepTsv.FreeNames(stamp, n => File.Exists(System.IO.Path.Combine(dir, n)), stems);
            var opened = new List<LineFile>();
            try
            {
                foreach (string n in names) opened.Add(new LineFile(System.IO.Path.Combine(dir, n)));
                return opened.ToArray();
            }
            catch (IOException) when (names.Any(n => File.Exists(System.IO.Path.Combine(dir, n))))
            {
                foreach (var f in opened) f.Dispose();   // another run took one of them first: try the next suffix
            }
        }
        throw new IOException($"Could not create new result files in {dir}.");
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
        var files = LineFile.OpenNew(outDir, "sweep", "journal");
        using var tsv = files[0];
        using var journal = files[1];
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
        if (outcome.ByHand.Count > 0)
        {
            w.WriteLine("NOT PUT BACK:");
            foreach (string f in outcome.ByHand) w.WriteLine("  " + f);
        }
        w.WriteLine($"Results: {tsv.Path}");
        w.WriteLine($"Journal: {journal.Path}");
        if (outcome.RestoreFailures.Count > 0) return 5;
        return link.Fault != null ? 6 : 0;
    }

    /// <summary><c>--restore</c>: puts every journaled control back, last first (a switch before the guard over it), each
    /// by its own starting value, and lists any that did not hold. A family of keys that share one state gets at most one
    /// press (the key the map names for its start); when that did not hold, or no key is known, the restore STOPS there
    /// and never presses through the family. Exit 0 all held, 5 some did not.</summary>
    public static int Restore(SimLink link, bool calc, IReadOnlyList<Control> controls, IReadOnlyDictionary<string, string> denied,
        IReadOnlyList<JournalEntry> entries, string outDir, Func<string?> cancelled, TextWriter w)
    {
        using var tsv = LineFile.OpenNew(outDir, "restore")[0];
        tsv.WriteLine(SweepTsv.Header);
        var session = new SweepSession(controls, denied, new LinkIo(link, calc),
            new SweepSinks(row => tsv.WriteLine(SweepTsv.Format(row)), _ => { }, w.WriteLine));
        var families = KeyFamilies.Find(controls, denied).ToDictionary(f => f.Key, StringComparer.Ordinal);
        var labels = controls.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);
        var failed = new List<string>();
        int tried = 0;
        foreach (var e in JournalRestore.RestoreOrder(entries))
        {
            // Ctrl+C: the entry in progress was finished; stop before the next
            if (cancelled() is string stop) { w.WriteLine($"{stop}: the rest is not tried."); break; }
            tried++;
            if (denied.ContainsKey(e.Key)) { failed.Add($"{e.Key}: denied, never written; put it back by hand"); continue; }
            string label = labels.GetValueOrDefault(e.Key) ?? (families.TryGetValue(e.Key, out var fam) ? fam.Label : "");
            bool held;
            try { held = session.Restore(e); }
            catch (SimLinkException ex) { held = false; w.WriteLine($"  {e.Key}: {ex.Message}"); }
            w.WriteLine($"{e.Key} ({label}): {(held ? "back at its start" : "NOT put back")}");
            if (held) continue;
            if (e.Kind == JournalKind.Family && families.TryGetValue(e.Key, out var f) && e.ReadValue is double start)
            {
                failed.Add($"{f.Label} ({e.Key}): set the {f.Noun} back to {f.NameOf(start)} by hand, pressing the key for {f.NameOf(start)} once in the cockpit.");
                w.WriteLine($"Stopped at the {f.Label}: nothing more is pressed. Put it back by hand, then run --restore again for the rest.");
                break;
            }
            failed.Add($"{e.Key} ({label})");
        }
        w.WriteLine();
        w.WriteLine($"Restore: {tried - failed.Count} of {entries.Count} back at their start" + (tried < entries.Count ? $", {entries.Count - tried} not tried." : "."));
        foreach (string f in failed) w.WriteLine("  NOT PUT BACK: " + f);
        w.WriteLine($"Results: {tsv.Path}");
        return failed.Count > 0 ? 5 : 0;
    }
}
