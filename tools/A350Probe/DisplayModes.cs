using MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The display engine's offline modes (no SimConnect, no form):
/// `patch [out.wasm] [compare.wasm]` patches the installed A350 module with MSFSBA's own patcher, writes it
/// and compares it byte for byte with another build (the Python prototype's a350-all.wasm).</summary>
internal static class DisplayModes
{
    public static bool Handles(string mode) => mode is "patch" or "snapshot";

    public static int Run(string mode, string[] args)
    {
        switch (mode)
        {
            case "patch": return Patch(args);
            case "snapshot": return Snapshot(args);
            default: return 2;
        }
    }

    /// <summary>`snapshot out.bin [more.bin ...]`: locate the A350 in the sim and save copies (prototype format).</summary>
    private static int Snapshot(string[] args)
    {
        var src = A350ModuleCache.FindInstalledModule();
        if (src == null) { Console.WriteLine("A350 module not found"); return 1; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var facts = A350ModuleFacts.From(A350WasmModule.Parse(File.ReadAllBytes(src)));
        Console.WriteLine($"module facts in {sw.ElapsedMilliseconds} ms: {facts.VTables.Count} vtables, anchors {string.Join(", ", facts.Anchors.Select(a => a.Text + "@" + a.Linear))}");
        using var reader = new A350MemoryReader(facts.Anchors);
        sw.Restart();
        if (!reader.TryLocate()) { Console.WriteLine("A350 not found in the sim"); return 1; }
        Console.WriteLine($"located at 0x{reader.Base:x} in {sw.ElapsedMilliseconds} ms");
        foreach (var path in args)
        {
            sw.Restart();
            var copy = reader.Snapshot();
            var ms = sw.Elapsed.TotalMilliseconds;
            copy.Save(path);
            Console.WriteLine($"{path}: {copy.Regions.Count} regions, {copy.Regions.Sum(r => (long)r.Length) / 1e6:F1} MB in {ms:F1} ms");
        }
        Console.WriteLine("still valid: " + reader.IsStillValid());
        return 0;
    }

    private static int Patch(string[] args)
    {
        var src = A350ModuleCache.FindInstalledModule();
        if (src == null) { Console.WriteLine("A350 module not found"); return 1; }
        Console.WriteLine("module " + src + " sha " + A350ModuleCache.Sha256Of(src));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var patched = A350ModulePatcher.Patch(File.ReadAllBytes(src));
        Console.WriteLine($"patched in {sw.ElapsedMilliseconds} ms: {patched.Bytes.Length} bytes, {patched.UiDrawClasses.Count} UI element classes");
        if (args.Length > 0) File.WriteAllBytes(args[0], patched.Bytes);
        if (args.Length > 1)
        {
            var other = File.ReadAllBytes(args[1]);
            int diff = -1;
            for (int i = 0; i < Math.Min(other.Length, patched.Bytes.Length); i++) if (other[i] != patched.Bytes[i]) { diff = i; break; }
            Console.WriteLine(other.Length == patched.Bytes.Length && diff < 0
                ? "IDENTICAL to " + args[1]
                : $"DIFFERS from {args[1]}: lengths {patched.Bytes.Length} vs {other.Length}, first difference at {diff}");
        }
        return 0;
    }
}
