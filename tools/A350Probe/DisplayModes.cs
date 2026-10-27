using MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The display engine's offline modes (no SimConnect, no form):
/// `patch [out.wasm] [compare.wasm]` patches the installed A350 module with MSFSBA's own patcher, writes it
/// and compares it byte for byte with another build (the Python prototype's a350-all.wasm).</summary>
internal static class DisplayModes
{
    public static bool Handles(string mode) => mode is "patch" or "snapshot" or "draw" or "service" or "trap" or "traptiny" or "ecamwatch" or "ecamlisten" or "sdmap";

    public static int Run(string mode, string[] args)
    {
        if (Environment.GetEnvironmentVariable("A350_BIG_STACK") == "1" && Thread.CurrentThread.Name != "big")
        {
            int rc = 0;
            var t = new Thread(() => rc = Run(mode, args), 256 << 20) { Name = "big" };
            t.Start(); t.Join();
            return rc;
        }
        switch (mode)
        {
            case "patch": return Patch(args);
            case "snapshot": return Snapshot(args);
            case "draw": return Draw(args);
            case "service": return Service(args);
            case "trap": return TrapTest(args);
            case "traptiny": return TrapTiny();
            case "ecamwatch": return EcamWatch(args);
            case "ecamlisten": return EcamListen(args);
            case "sdmap": return SdMap(args);
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
        var variant = Environment.GetEnvironmentVariable("A350_WT_VARIANT");
        var cacheRoot = variant == null ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(Path.GetTempPath(), "a350wt-" + variant);
        using var engine = variant switch
        {
            "guard0" => new Wasmtime.Engine(new Wasmtime.Config().WithOptimizationLevel(Wasmtime.OptimizationLevel.Speed)
                .WithMemoryGuardSize(0).WithStaticMemoryMaximumSize(0).WithMemoryReservationForGrowth(0)),
            _ => A350ModuleCache.CreateEngine(),
        };
        using var module = A350ModuleCache.LoadOrBuild(engine, src, cacheRoot, out bool built);
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
        draw.HostError = (n, ex) => Console.Error.WriteLine($"HOST ERROR {n}: {ex.GetType().Name} {ex.Message.Split((char)10)[0]}");
        draw.AVarAnswer = k => k.StartsWith("KOHLSMAN SETTING HG") ? 29.92 : k.StartsWith("TURB ENG N2") ? 0 : null;
        static string F0(float v) => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var g in args.Skip(1))
        {
            if (g.StartsWith("poke:"))   // poke:linear=value on this copy only, then reload it into the engine
            {
                var kv = g[5..].Split('=');
                Console.Error.WriteLine($"poke {kv[0]}={kv[1]}: {copy.WriteInt32(uint.Parse(kv[0]), int.Parse(kv[1]))}");
                draw.Load(copy);
                continue;
            }
                        if (g == "reload")
            {
                if (reader == null) { Console.Error.WriteLine("reload needs live"); continue; }
                Console.Error.WriteLine("snapshot..."); var c2 = reader.Snapshot();
                Console.Error.WriteLine("load..."); draw.Load(c2);
                Console.Error.WriteLine("reloaded");
                continue;
            }
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

    /// <summary>`service [rounds]`: the A350DisplayService as MSFSBA runs it: ready, then rounds of CTR, EFIS 0/1
    /// and MFD on one fresh copy each, timed, with the WD rows of the last round.</summary>
    private static int Service(string[] args)
    {
        int rounds = args.Length > 0 ? int.Parse(args[0]) : 10;
        using var svc = new A350DisplayService();
        svc.Spoken += s => Console.WriteLine("SAY: " + s);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        svc.Start();
        while (svc.State == A350DisplayState.Preparing && sw.ElapsedMilliseconds < 120000) Thread.Sleep(50);
        Console.WriteLine($"state {svc.State} after {sw.ElapsedMilliseconds} ms {svc.Reason}");
        IReadOnlyList<A350DrawCapture>? caps = null;
        for (int r = 0; r < rounds; r++)
        {
            sw.Restart();
            caps = svc.DrawAsync(("CTR", 0), ("EFIS", 0), ("EFIS", 1), ("MFD", 0)).GetAwaiter().GetResult();
            Console.WriteLine($"round {r}: {sw.ElapsedMilliseconds} ms total; " + (caps == null ? "null" :
                string.Join(", ", caps.Select(c => $"{c.Gauge} {c.Texts.Count}t {c.Milliseconds:F1}ms{(c.Ok ? "" : " " + c.Error)}"))));
        }
        if (caps != null)
            foreach (var row in A350DrawLayout.Rows(A350DrawLayout.Region(caps[0].Texts, A350DrawLayout.CtrWarningDisplay)))
                Console.WriteLine("  WD: " + A350DrawLayout.Speak(row));
        return 0;
    }

    /// <summary>`trap <export> <arg>`: calls an export of the patched module with a nonsense argument on an
    /// empty instance, to see how a trap inside the A350 code comes back to .NET.</summary>
    private static int TrapTest(string[] args)
    {
        var src = A350ModuleCache.FindInstalledModule()!;
        var variant = Environment.GetEnvironmentVariable("A350_WT_VARIANT");
        var cfg = new Wasmtime.Config().WithOptimizationLevel(variant == "noopt" ? Wasmtime.OptimizationLevel.None : Wasmtime.OptimizationLevel.Speed);
        if (variant == "unwind") cfg = cfg.WithUnwindInfo(true);
        if (variant == "debug") cfg = cfg.WithDebugInfo(true);
        if (variant == "guard0") cfg = cfg.WithMemoryGuardSize(0).WithStaticMemoryMaximumSize(0).WithMemoryReservationForGrowth(0);
        using var engine = new Wasmtime.Engine(cfg);
        var root = variant == null ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : Path.Combine(Path.GetTempPath(), "a350wt-" + variant);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var module = A350ModuleCache.LoadOrBuild(engine, src, root, out _);
        Console.WriteLine($"module ready in {sw.ElapsedMilliseconds} ms");
        using var store = new Wasmtime.Store(engine);
        using var linker = new Wasmtime.Linker(engine);
        foreach (var imp in module.Imports)
            if (imp is Wasmtime.FunctionImport fi)
            {
                var rs = fi.Results.ToArray();
                linker.Define(imp.ModuleName, imp.Name, Wasmtime.Function.FromCallback(store,
                    (Wasmtime.Caller c, ReadOnlySpan<Wasmtime.ValueBox> a, Span<Wasmtime.ValueBox> r) =>
                    { for (int i = 0; i < r.Length; i++) r[i] = rs[i] == Wasmtime.ValueKind.Float64 ? 0d : rs[i] == Wasmtime.ValueKind.Int64 ? 0L : rs[i] == Wasmtime.ValueKind.Float32 ? 0f : 0; },
                    fi.Parameters.ToArray(), rs));
            }
        var inst = linker.Instantiate(store, module);
        Console.WriteLine("calling " + args[0] + "(" + args[1] + ")");
        try { inst.GetFunction(args[0])!.Invoke(int.Parse(args[1])); Console.WriteLine("returned"); }
        catch (Exception ex) { Console.WriteLine("caught " + ex.GetType().Name + ": " + ex.Message.Split((char)10)[0]); }
        return 0;
    }

    /// <summary>`traptiny`: a two-instruction module whose only function reads out of bounds.</summary>
    private static int TrapTiny()
    {
        // (module (memory 2) (func (export "oob") i32.const -16 i32.load drop))
        var bytes = new byte[] { 0,0x61,0x73,0x6D,1,0,0,0, 1,4,1,0x60,0,0, 3,2,1,0, 5,3,1,0,2, 7,7,1,3,0x6F,0x6F,0x62,0,0,
            10,10,1,8,0,0x41,0x70,0x28,2,0,0x1A,0x0B };
        using var engine = A350ModuleCache.CreateEngine();
        using var module = Wasmtime.Module.FromBytes(engine, "tiny", bytes);
        using var store = new Wasmtime.Store(engine);
        using var linker = new Wasmtime.Linker(engine);
        var inst = linker.Instantiate(store, module);
        try { inst.GetFunction("oob")!.Invoke(); Console.WriteLine("returned"); }
        catch (Exception ex) { Console.WriteLine("caught " + ex.GetType().Name + ": " + ex.Message.Split((char)10)[0]); }
        return 0;
    }

    /// <summary>`ecamwatch seconds [fixtureDir]`: the CTR drawn at 2 Hz through the service; each change of the
    /// E/WD (warning display, engine block) and SD, once two reads agree, printed with colours and (with a
    /// fixture dir) its capture saved as ctr_NNN.json.</summary>
    private static int EcamWatch(string[] args)
    {
        int seconds = int.Parse(args[0]);
        string? dir = args.Length > 1 ? args[1] : null;
        using var svc = new A350DisplayService();
        svc.Start();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (svc.State == A350DisplayState.Preparing && sw.ElapsedMilliseconds < 120000) Thread.Sleep(50);
        Console.WriteLine($"state {svc.State} {svc.Reason}");
        var stable = new A350StableValue<string>();
        int n = 0, traps = 0;
        var opts = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        string nl = Environment.NewLine;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            var caps = svc.DrawAsync(("CTR", 0)).GetAwaiter().GetResult();
            Thread.Sleep(500);
            if (caps == null) continue;
            var c = caps[0];
            if (!c.Ok) { traps++; continue; }
            string Dump(A350Rect r) => string.Join(nl, A350DrawLayout.Rows(A350DrawLayout.Region(c.Texts, r)).Select(A350DrawLayout.Speak));
            string state = "WD:" + nl + Dump(A350DrawLayout.CtrWarningDisplay) + nl + "ENG:" + nl + Dump(A350DrawLayout.CtrEngineDisplay)
                + nl + "SD:" + nl + Dump(A350DrawLayout.CtrSystemDisplay) + nl + "PERM:" + nl + Dump(A350DrawLayout.CtrPermanentData);
            if (!stable.Offer(state, out _)) continue;
            n++;
            Console.WriteLine($"===== state {n} at {sw.Elapsed.TotalSeconds:F1} s ({c.Frames.Count} frames: {string.Join(", ", c.Frames.Select(f => $"{f.X:0},{f.Y:0} {f.W:0}x{f.H:0} {A350Rgba.NameOf(f.Colour)}{(f.Wide ? " wide" : "")}"))})");
            Console.WriteLine(state);
            if (dir != null) File.WriteAllText(Path.Combine(dir, $"ctr_{n:000}.json"), System.Text.Json.JsonSerializer.Serialize(c, opts));
        }
        Console.WriteLine($"traps {traps}");
        return 0;
    }

    /// <summary>`ecamlisten seconds`: MSFSBA's own E/WD monitor over the service, printing each line it
    /// would speak, with the time.</summary>
    private static int EcamListen(string[] args)
    {
        int seconds = int.Parse(args[0]);
        using var svc = new A350DisplayService();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        svc.Spoken += s => Console.WriteLine($"{sw.Elapsed.TotalSeconds,6:F1} SAY (service): {s}");
        svc.Start();
        using var mon = new A350EcamMonitor(async () => (await svc.DrawAsync(("CTR", 0)))?.FirstOrDefault(), () => false,
            line => Console.WriteLine($"{sw.Elapsed.TotalSeconds,6:F1} SAY: {line}"));
        mon.Start();
        Thread.Sleep(seconds * 1000);
        return 0;
    }

    /// <summary>`sdmap copy.bin page [page...]`: which variable each drawn SD value shows. On the copy only:
    /// each CustomVariable value and each Variables-array slot (1.2.7: names at 920832, values at 1344512,
    /// 1939 entries) in turn set to 4321, the page redrawn, and every SD text that changed reported with the
    /// variable's name. The SD page is set in the copy (1451120).</summary>
    private static int SdMap(string[] args)
    {
        const uint SdPage = 1451120, VarNames = 920832, VarValues = 1344512; const int VarCount = 1939;
        var src = A350ModuleCache.FindInstalledModule()!;
        var mod = A350WasmModule.Parse(File.ReadAllBytes(src));
        var facts = A350ModuleFacts.From(mod);
        using var engine = A350ModuleCache.CreateEngine();
        using var module = A350ModuleCache.LoadOrBuild(engine, src, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), out _);
        using var draw = new A350DrawEngine(engine, module, A350ModulePatcher.UiDrawClassesOf(mod), facts);
        var copy = A350MemoryCopy.Load(args[0]);
        draw.Load(copy);
        var targets = new List<(string Name, uint Address)>();
        foreach (var cv in draw.CustomVariables) targets.Add(("L:" + cv.Name, cv.Address + 16));
        for (int i = 0; i < VarCount; i++)
        {
            var p = copy.Read(VarNames + (uint)(4 * i), 4);
            var name = p.Length == 4 ? mod.CString((int)BitConverter.ToUInt32(p)) : null;
            targets.Add(("V:" + (name ?? "#" + i), VarValues + (uint)(8 * i)));
        }
        Console.Error.WriteLine($"{targets.Count} variables");
        static Dictionary<(int, int), string> Cells(A350DrawCapture c) =>
            A350DrawLayout.Region(c.Texts, A350DrawLayout.CtrSystemDisplay).Where(t => !string.IsNullOrWhiteSpace(t.Text))
                .GroupBy(t => ((int)MathF.Round(t.X), (int)MathF.Round(t.Y))).ToDictionary(g => g.Key, g => string.Join("|", g.Select(t => t.Text.Trim() + "{" + t.ColourName + "}")));
        foreach (var pageArg in args.Skip(1))
        {
            int page = int.Parse(pageArg);
            copy.WriteInt32(SdPage, page);
            var baseCap = draw.Draw("CTR");
            if (!baseCap.Ok) { Console.WriteLine($"page {page}: base draw failed {baseCap.Error}"); continue; }
            var baseCells = Cells(baseCap);
            Console.WriteLine($"===== page {page}: {baseCells.Count} cells");
            var hits = new Dictionary<(int, int), List<string>>();
            foreach (var (name, addr) in targets)
            {
                var old = copy.Read(addr, 8).ToArray();
                if (old.Length != 8) continue;
                copy.WriteDouble(addr, 4321);
                var cap = draw.Draw("CTR");
                copy.WriteDouble(addr, BitConverter.ToDouble(old));
                if (!cap.Ok) continue;
                var cells = Cells(cap);
                foreach (var key in baseCells.Keys.Union(cells.Keys))
                {
                    baseCells.TryGetValue(key, out var a); cells.TryGetValue(key, out var b);
                    if (a == b) continue;
                    if (!hits.TryGetValue(key, out var l)) hits[key] = l = new();
                    l.Add($"{name} ({a ?? "-"} -> {b ?? "-"})");
                }
            }
            foreach (var kv in hits.OrderBy(k => k.Key.Item2).ThenBy(k => k.Key.Item1))
                Console.WriteLine($"{kv.Key.Item1},{kv.Key.Item2} [{(baseCells.TryGetValue(kv.Key, out var t0) ? t0 : "new")}]: {string.Join("; ", kv.Value.Take(6))}{(kv.Value.Count > 6 ? $" (+{kv.Value.Count - 6})" : "")}");
        }
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
