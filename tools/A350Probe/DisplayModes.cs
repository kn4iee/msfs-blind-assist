using MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The display engine's offline modes (no SimConnect, no form):
/// `patch [out.wasm] [compare.wasm]` patches the installed A350 module with MSFSBA's own patcher, writes it
/// and compares it byte for byte with another build (the Python prototype's a350-all.wasm).</summary>
internal static class DisplayModes
{
    public static bool Handles(string mode) => mode is "patch" or "snapshot" or "draw";

    public static int Run(string mode, string[] args)
    {
        switch (mode)
        {
            case "patch": return Patch(args);
            case "snapshot": return Snapshot(args);
            case "draw": return Draw(args);
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

    /// <summary>`draw copy.bin|live Gauge[#n] ...`: run the gauges' draws in MSFSBA's engine and print the
    /// raw texts, frames and elements in xy.mjs's format (diff against `node xy.mjs`).</summary>
    private static int Draw(string[] args)
    {
        var src = A350ModuleCache.FindInstalledModule();
        if (src == null) { Console.WriteLine("A350 module not found"); return 1; }
        var mod = A350WasmModule.Parse(File.ReadAllBytes(src));
        var facts = A350ModuleFacts.From(mod);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var engine = A350ModuleCache.CreateEngine();
        using var module = A350ModuleCache.LoadOrBuild(engine, src, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), out bool built);
        Console.Error.WriteLine($"module {(built ? "built" : "from cache")} in {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        using var draw = new A350DrawEngine(engine, module, A350ModulePatcher.UiDrawClassesOf(mod), facts);
        Console.Error.WriteLine($"instantiated in {sw.ElapsedMilliseconds} ms");
        A350MemoryCopy copy;
        A350MemoryReader? reader = null;
        if (args[0] == "live")
        {
            reader = new A350MemoryReader(facts.Anchors);
            if (!reader.TryLocate()) { Console.WriteLine("A350 not found in the sim"); return 1; }
            copy = reader.Snapshot();
        }
        else copy = A350MemoryCopy.Load(args[0]);
        sw.Restart();
        draw.Load(copy);
        Console.Error.WriteLine($"loaded copy in {sw.ElapsedMilliseconds} ms, {draw.CustomVariableCount} custom vars");
        draw.AVarAnswer = k => k.StartsWith("KOHLSMAN SETTING HG") ? 29.92 : k.StartsWith("TURB ENG N2") ? 0 : null;
        static string F0(float v) => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var g in args.Skip(1))
        {
            var parts = g.Split('#');
            var cap = draw.Draw(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 0);
            var jsonDir = Environment.GetEnvironmentVariable("A350_DRAW_JSON");
            if (jsonDir != null)
                File.WriteAllText(Path.Combine(jsonDir, g.Replace('#', '_') + ".json"), System.Text.Json.JsonSerializer.Serialize(cap,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.Error.WriteLine($"== {g}: {cap.Texts.Count} texts, {cap.Frames.Count} frames, {cap.Elements.Count} elements in {cap.Milliseconds:F1} ms {cap.Error}");
            foreach (var t in cap.Texts)
                Console.WriteLine($"{F0(t.X).PadLeft(5)} {F0(t.Y).PadLeft(5)} ( {F0(t.LocalX)} {F0(t.LocalY)} ) {t.ColourName.PadRight(7)} {System.Text.Json.JsonSerializer.Serialize(t.Text, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}");
            foreach (var f in cap.Frames)
                Console.WriteLine($"FRAME {F0(f.X)} {F0(f.Y)} {F0(f.W)} {F0(f.H)} {A350Rgba.NameOf(f.Colour)} {(f.Wide ? "WIDE" : "")}");
            foreach (var e in cap.Elements)
                Console.WriteLine($"EL {e.Class.PadRight(22)} ptr {e.Ptr} rect {F0(e.RectX)},{F0(e.RectY)},{F0(e.RectW)},{F0(e.RectH)} abs {F0(e.X)} {F0(e.Y)}");
        }
        foreach (var q in draw.Asked) Console.Error.WriteLine("asked " + q);
        reader?.Dispose();
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
