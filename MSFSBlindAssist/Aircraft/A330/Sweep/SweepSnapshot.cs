using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>A variable that moved with a write, besides the control's own read: a side effect (radio panel 1's NAV
/// backup is the known one), and a candidate proof.</summary>
internal sealed record SideEffect(string Name, double Before, double After);

/// <summary>
/// The sweep's snapshots of the aircraft's <c>INI_*</c> L:vars (Task 5's differential method) and the fixed tables
/// the runner records with each verdict. Pure.
/// </summary>
internal static class SweepSnapshot
{
    /// <summary>The aircraft's own value of a continuous control, where it has one besides the control's read,
    /// recorded with each of its points. Hard-coded: the map has no field for it. The landing elevation knob reads
    /// 0..100 and the aircraft keeps the elevation it selects; the radar gain's input event reads 0..100 and the
    /// aircraft keeps the same setting as 0..10.</summary>
    public static readonly IReadOnlyDictionary<string, string> AircraftValues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A330_LDG_ELEV"] = "INI_LANDING_ELEVATION",
        ["A330_WX_GAIN"] = "INI_WXR_GAIN",
    };

    /// <summary>The electrical, bleed and hydraulic state recorded with each verdict: these controls' own reads
    /// (external power, the generators, the APU and engine bleeds, the hydraulic pumps).</summary>
    public static readonly string[] SystemStateKeys =
    {
        "A330_EXT_A", "A330_EXT_B", "A330_GEN1", "A330_GEN2", "A330_APU_GEN",
        "A330_APU_BLEED", "A330_ENG1_BLEED", "A330_ENG2_BLEED",
        "A330_HYD_GREEN", "A330_HYD_GREEN_ENG2", "A330_HYD_BLUE", "A330_HYD_YELLOW",
        "A330_HYD_GREEN_ELEC_OFF", "A330_HYD_BLUE_ELEC_OFF", "A330_HYD_YELLOW_ELEC_OFF",
    };

    /// <summary>The second pass, <c>--pass apu-bleed</c>: the packs, the engine bleeds and the crossbleed, swept again
    /// with the APU bleed on (the owner starts the APU by hand: it is denied).</summary>
    public static readonly string[] ApuBleedPassKeys = { "A330_PACK1", "A330_PACK2", "A330_ENG1_BLEED", "A330_ENG2_BLEED", "A330_XBLEED" };

    public const string ApuBleedKey = "A330_APU_BLEED";

    /// <summary>True when the APU bleed's read is the value of its position named On.</summary>
    public static bool ApuBleedIsOn(Control apuBleed, double? read) =>
        apuBleed.Positions?.FirstOrDefault(p => p.Label == "On") is { } on && SweepTiming.Same(read, on.Value);

    /// <summary>Every L:var the map names (each control's read, state, lamps and proof), the aircraft's own values
    /// above, and <paramref name="extra"/>; sorted, each once. Input events and simvars are left out.</summary>
    public static IReadOnlyList<string> Names(IEnumerable<Control> controls, IEnumerable<string> extra)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        void Add(string? read)
        {
            if (string.IsNullOrWhiteSpace(read)) return;
            var (kind, name, _) = A330ControlMap.ParseReadKind(read);
            if (kind == A330ControlMap.ReadKind.LVar && name.Length > 0) names.Add(name);
        }
        foreach (var c in controls)
        {
            Add(c.Read);
            Add(c.State);
            Add(c.Proof?.Read);
            foreach (var lamp in c.Lamps ?? new()) Add(lamp.Read);
        }
        foreach (var name in AircraftValues.Values) Add(name);
        foreach (var name in extra) Add(name);
        return names.ToList();
    }

    /// <summary>How many variables one snapshot block holds: the tool's block struct is exactly this many doubles.</summary>
    public const int SlotsPerBlock = 100;

    /// <summary>The datum that fills a block's unused slots: a simulator variable that always answers, never read back
    /// (the app pads its continuous batches with the same one).</summary>
    public const string FillerDatum = "SIMULATION TIME", FillerUnits = "seconds";

    /// <summary>
    /// The snapshot's data definitions as the tool registers them: <paramref name="names"/> in ordinal order,
    /// <see cref="SlotsPerBlock"/> a block, the last block filled to its full size with fillers (null slots,
    /// <see cref="FillerDatum"/>). SimConnect sends a block only as many doubles as its definition has, while the managed
    /// SimConnect library copies the whole fixed-size struct out of the message, so a short block reads past the
    /// message's end; across an unmapped page that kills the process inside the marshaler (the app's
    /// SimConnectManager.Setup.cs records the crash and pads for it; the sweep tool crashed the same way on 2026-11-27).
    /// A filler sorts after every <c>L:</c> name, so the names keep the first slots in either order.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> Blocks(IReadOnlyList<string> names)
    {
        var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var blocks = new List<IReadOnlyList<string?>>();
        for (int i = 0; i < sorted.Count; i += SlotsPerBlock)
        {
            var block = new List<string?>(sorted.Skip(i).Take(SlotsPerBlock));
            while (block.Count < SlotsPerBlock) block.Add(null);
            blocks.Add(block);
        }
        return blocks;
    }

    /// <summary>A <c>--vars</c> file: one name a line, <c>#</c> comments and blank lines ignored.</summary>
    public static IReadOnlyList<string> ParseVarsFile(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    /// <summary>The variables that moved between two snapshots taken with no write: they move on their own.</summary>
    public static IReadOnlySet<string> Noisy(IReadOnlyDictionary<string, double> first, IReadOnlyDictionary<string, double> second) =>
        first.Where(kv => second.TryGetValue(kv.Key, out double v) && !SweepTiming.Same(kv.Value, v)).Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>What moved between the snapshots around a write, besides <paramref name="own"/> (the control's read and
    /// its proof) and the <paramref name="noisy"/> ones; in name order.</summary>
    public static IReadOnlyList<SideEffect> Moved(IReadOnlyDictionary<string, double> before, IReadOnlyDictionary<string, double> after,
        IEnumerable<string?> own, IReadOnlySet<string>? noisy = null)
    {
        var skip = own.Where(n => n != null).Select(n => n!).ToHashSet(StringComparer.Ordinal);
        return before
            .Where(kv => !skip.Contains(kv.Key) && noisy?.Contains(kv.Key) != true
                && after.TryGetValue(kv.Key, out double now) && !SweepTiming.Same(kv.Value, now))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new SideEffect(kv.Key, kv.Value, after[kv.Key]))
            .ToList();
    }

    /// <summary>The system state in a snapshot: each <see cref="SystemStateKeys"/> control (its key without
    /// <c>A330_</c>) and its read's value; null where the snapshot has none.</summary>
    public static IReadOnlyList<(string Name, double? Value)> SystemState(IEnumerable<Control> controls, IReadOnlyDictionary<string, double> snapshot)
    {
        var byKey = controls.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var state = new List<(string, double?)>();
        foreach (string key in SystemStateKeys)
        {
            if (!byKey.TryGetValue(key, out var c) || string.IsNullOrWhiteSpace(c.Read)) continue;
            var (kind, name, _) = A330ControlMap.ParseReadKind(c.Read);
            double? v = kind == A330ControlMap.ReadKind.LVar && snapshot.TryGetValue(name, out double x) ? x : null;
            state.Add((key.StartsWith("A330_", StringComparison.Ordinal) ? key[5..] : key, v));
        }
        return state;
    }
}
