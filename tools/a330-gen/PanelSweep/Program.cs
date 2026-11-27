using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace A330PanelSweep;

/// <summary>
/// The live sweep of the iniBuilds A330's cockpit controls (plan 04, Task 4). See README.md.
///
///   A330PanelSweep --dry-run [--pass apu-bleed] [--only K,K] [--from K]   prints the plan; never connects to the sim
///   A330PanelSweep [--pass apu-bleed] [--only K,K] [--from K] [--vars FILE] [--out DIR]   asks, then sweeps
///   A330PanelSweep --restore JOURNAL [--out DIR]                           asks, then puts the journal's controls back
///
/// Exit codes: 0 done, 1 bad arguments, 2 map, deny list or journal problem, 3 the sim cannot be reached, 4 the
/// pre-flight refused (nothing was written), 5 a control was not put back, 6 stopped on a SimConnect fault.
/// </summary>
internal static class Program
{
    private sealed record Options(bool DryRun, bool Help, string? Restore, string Pass, IReadOnlySet<string>? Only, string? From,
        string? VarsFile, string? OutDir);

    private const string FullPass = "full", ApuBleedPass = "apu-bleed";

    private static int Main(string[] args)
    {
        Options opts;
        try { opts = ParseArgs(args); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 1;
        }
        if (opts.Help) { PrintUsage(Console.Out); return 0; }

        var controls = A330ControlMap.Embedded.Controls;
        if (controls.Count == 0)
        {
            Console.Error.WriteLine("The control map is empty: a330_control_map.json did not load.");
            return 2;
        }

        // The deny list is always the one shipped next to the exe; a missing or empty one stops everything.
        string denyPath = Path.Combine(AppContext.BaseDirectory, "denylist.txt");
        Dictionary<string, string> denied;
        try { denied = DenyList.Parse(File.ReadAllText(denyPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Console.Error.WriteLine($"Cannot use the deny list {denyPath}: {ex.Message}");
            return 2;
        }
        if (denied.Count == 0)
        {
            Console.Error.WriteLine($"The deny list {denyPath} has no entries; refusing to go on without one.");
            return 2;
        }

        Func<ControlPlan, bool>? include;
        try { include = Include(controls, opts); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

        if (opts.DryRun)
        {
            Console.WriteLine($"Deny list: {denyPath} ({denied.Count} controls)");
            var plan = SweepPlanner.Plan(controls, PlanReport.AssumedStarts(controls), denied, SweepSession.EcamPageKeys(controls));
            var shown = include == null ? plan : new SweepPlan(plan.Controls.Where(include).ToList());
            PlanReport.Print(shown, controls, opts.Pass, Console.Out);
            return 0;
        }

        List<JournalEntry>? journal = null;
        if (opts.Restore != null)
        {
            try { journal = SweepJournal.Parse(File.ReadAllText(opts.Restore)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                Console.Error.WriteLine($"Cannot read the journal {opts.Restore}: {ex.Message}");
                return 2;
            }
        }
        return RunLive(opts, controls, denied, include, journal);
    }

    // The live path is its own method, so a dry run never loads anything that talks to the sim.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunLive(Options opts, IReadOnlyList<Control> controls, IReadOnlyDictionary<string, string> denied,
        Func<ControlPlan, bool>? include, List<JournalEntry>? journal)
    {
        IReadOnlyList<string> extraVars = Array.Empty<string>();
        if (opts.VarsFile != null)
        {
            try { extraVars = SweepSnapshot.ParseVarsFile(File.ReadAllText(opts.VarsFile)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Cannot read {opts.VarsFile}: {ex.Message}");
                return 2;
            }
        }

        Console.WriteLine(journal != null
            ? $"A330PanelSweep: put back the {journal.Count} controls of {opts.Restore}, each by its own starting value."
            : $"A330PanelSweep: live sweep of the A330's cockpit controls ({opts.Pass} pass).");
        Console.WriteLine();
        Console.WriteLine("Before you go on:");
        Console.WriteLine("  - you are at the keyboard and listening to the aircraft;");
        Console.WriteLine("  - the iniBuilds A330 is loaded, on the ground, parked, engines off, external power on"
            + (opts.Pass == ApuBleedPass ? ", the APU running with its bleed on;" : ";"));
        Console.WriteLine("  - MSFSBA is closed, so nothing else is writing to the cockpit;");
        Console.WriteLine("  - Ctrl+C stops after the control in progress has been put back.");
        Console.Write("Type YES and press Enter to start: ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "YES", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Not started.");
            return 0;
        }

        SimLink link;
        try { link = SimLink.Connect(Console.Out); }
        catch (SimLinkException ex) { Console.Error.WriteLine(ex.Message); return 3; }

        using (link)
        {
            bool calc;
            try
            {
                if (SweepRunner.Preflight(link, controls, Console.Out) is not bool answers) return 4;
                calc = answers;
                if (journal == null && opts.Pass == ApuBleedPass)
                {
                    var apuBleed = controls.First(c => c.Key == SweepSnapshot.ApuBleedKey);
                    double? read = link.Read(apuBleed.Read!, 2000);
                    if (!SweepSnapshot.ApuBleedIsOn(apuBleed, read))
                    {
                        Console.WriteLine($"Refused: the APU bleed ({apuBleed.Read}) reads {SweepTsv.Num(read)}, not On. Start the APU and switch its bleed on by hand first.");
                        return 4;
                    }
                    Console.WriteLine("The APU bleed reads On.");
                }
            }
            catch (SimLinkException ex) { Console.Error.WriteLine(ex.Message); return 3; }

            string outDir = opts.OutDir ?? Directory.GetCurrentDirectory();
            if (journal != null) return SweepRunner.Restore(link, calc, controls, denied, journal, outDir, Console.Out);

            string? cancel = null;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;   // never let the default kill the process between a write and its put-back
                if (cancel != null)
                {
                    Console.WriteLine("Second Ctrl+C: stopping now. Put the control in progress back with --restore and the journal.");
                    Environment.Exit(130);
                }
                cancel = "stopped with Ctrl+C";
                Console.WriteLine("Ctrl+C: stopping once the control in progress is put back. Press again to stop at once.");
            };
            return SweepRunner.Sweep(link, calc, controls, denied, include, extraVars, outDir, () => cancel, Console.Out);
        }
    }

    /// <summary>Which controls this run keeps: the pass, then <c>--only</c>, then <c>--from</c> (that key and every one
    /// after it in the map's order). Null: all.</summary>
    private static Func<ControlPlan, bool>? Include(IReadOnlyList<Control> controls, Options o)
    {
        var order = controls.Select((c, i) => (c.Key, i)).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);
        foreach (string k in (o.Only ?? new HashSet<string>()).Concat(o.From != null ? new[] { o.From } : Array.Empty<string>()))
            if (!order.ContainsKey(k)) throw new ArgumentException($"'{k}' is no control of the map.");
        var pass = o.Pass == ApuBleedPass ? new HashSet<string>(SweepSnapshot.ApuBleedPassKeys, StringComparer.Ordinal) : null;
        int from = o.From != null ? order[o.From] : 0;
        if (pass == null && o.Only == null && o.From == null) return null;
        return cp => (pass == null || pass.Contains(cp.Key)) && (o.Only == null || o.Only.Contains(cp.Key))
            && (!order.TryGetValue(cp.Key, out int i) || i >= from);
    }

    private static Options ParseArgs(string[] args)
    {
        bool dryRun = false, help = false;
        string? restore = null, from = null, vars = null, outDir = null;
        string pass = FullPass;
        HashSet<string>? only = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--dry-run": dryRun = true; break;
                case "-h": case "--help": case "/?": help = true; break;
                case "--restore": restore = Value(args, ref i, a); break;
                case "--pass":
                    pass = Value(args, ref i, a).ToLowerInvariant();
                    if (pass != FullPass && pass != ApuBleedPass) throw new ArgumentException($"--pass is '{FullPass}' or '{ApuBleedPass}', not '{pass}'.");
                    break;
                case "--only":
                    only = Value(args, ref i, a).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                    break;
                case "--from": from = Value(args, ref i, a); break;
                case "--vars": vars = Value(args, ref i, a); break;
                case "--out": outDir = Value(args, ref i, a); break;
                default: throw new ArgumentException($"Unknown argument '{a}'.");
            }
        }
        if (restore != null && (dryRun || pass != FullPass || only != null || from != null))
            throw new ArgumentException("--restore takes only --out.");
        return new Options(dryRun, help, restore, pass, only, from, vars, outDir);
    }

    private static string Value(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
        return args[++i];
    }

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("A330PanelSweep: live sweep of the iniBuilds A330's cockpit controls (plan 04, Task 4).");
        w.WriteLine();
        w.WriteLine("  A330PanelSweep --dry-run [--pass apu-bleed] [--only K,K] [--from K]   print the plan; never connects");
        w.WriteLine("  A330PanelSweep [--pass apu-bleed] [--only K,K] [--from K] [--vars FILE] [--out DIR]");
        w.WriteLine("                                     sweep (asks for YES first)");
        w.WriteLine("  A330PanelSweep --restore JOURNAL [--out DIR]   put a journal's controls back (asks for YES first)");
        w.WriteLine();
        w.WriteLine("  --pass apu-bleed  only the packs, engine bleeds and crossbleed; refuses unless the APU bleed reads On");
        w.WriteLine("  --only K,K        only these control keys");
        w.WriteLine("  --from K          this control key and every one after it in the map's order (to go on after a stop)");
        w.WriteLine("  --vars FILE       more L:vars for the snapshot, one name a line");
        w.WriteLine("  --out DIR         where sweep-<date>.tsv and journal-<date>.tsv go (default: the current folder)");
        w.WriteLine();
        w.WriteLine("See tools/a330-gen/PanelSweep/README.md.");
    }
}
