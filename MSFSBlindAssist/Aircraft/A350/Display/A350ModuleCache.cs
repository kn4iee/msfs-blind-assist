using System.Diagnostics;
using System.Security.Cryptography;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Utils.Logging;
using Wasmtime;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Finds the pilot's installed A350 module and gives the display engine its compiled, patched copy.
/// The patched module is built on this machine from the pilot's own install (never shipped [A350-1]),
/// compiled once per A350 version (about 10 s, 1.4 GB peak) and cached by the module's SHA-256 and the patch's
/// version [A350-11] under %LOCALAPPDATA%\MSFSBlindAssist\a350; later starts deserialize it (about 125 ms).
/// </summary>
public static class A350ModuleCache
{
    public static readonly string ModuleRelativePath = Path.Combine(
        "SimObjects", "Airplanes", "A350", "attachments", "inibuilds", "Function_Interior_A350", "panel", "inibuilds-A350.wasm");

    /// <summary>The engine every A350 module is compiled with; a cache only loads into the same config, so a change here
    /// bumps <see cref="A350ModulePatcher.Version"/> too. Epoch interruption puts a deadline on every draw
    /// (<see cref="A350DrawEngine.DrawDeadline"/>).</summary>
    public static Engine CreateEngine() => new(new Config().WithOptimizationLevel(OptimizationLevel.Speed).WithEpochInterruption(true));

    /// <summary>The installed module under a packages root: Community folders first (the sim loads an
    /// add-on there over an official package), then the Official folders' packages. A package folder
    /// whose name starts with inibuilds-aircraft-a350 but holds no module (a livery pack) is skipped.</summary>
    public static string? FindInstalledModule(string packagesRoot)
    {
        foreach (var folder in MsfsPackageLayout.PackageFolders(packagesRoot))
        {
            string[] packages;
            try { packages = Directory.GetDirectories(folder, "inibuilds-aircraft-a350*"); }
            catch (Exception) { continue; }
            Array.Sort(packages, StringComparer.OrdinalIgnoreCase);
            foreach (var p in packages)
            {
                var path = Path.Combine(p, ModuleRelativePath);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    /// <summary>The installed module for MSFS 2024, or null.</summary>
    public static string? FindInstalledModule()
    {
        var root = MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024");
        return root == null ? null : FindInstalledModule(root);
    }

    /// <summary>What tells an updated module from the one prepared, without hashing 69 MB: its size and write time.</summary>
    public readonly record struct ModuleIdentity(long Length, DateTime WrittenUtc);

    /// <summary>The module's identity, or null when it cannot be read (gone, or being replaced).</summary>
    public static ModuleIdentity? IdentityOf(string path)
    {
        try
        {
            var f = new FileInfo(path);
            return f.Exists ? new ModuleIdentity(f.Length, f.LastWriteTimeUtc) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The installed package's version, from the manifest.json at the package's root, or null when it cannot be
    /// read (for the log: the control map names the version it was measured on).</summary>
    public static string? PackageVersionOf(string modulePath)
    {
        try
        {
            // The module's relative path has as many folders as it has separators: up that many from the module.
            var root = modulePath;
            for (int n = ModuleRelativePath.Count(c => c == Path.DirectorySeparatorChar) + 1; n > 0; n--)
                root = Path.GetDirectoryName(root) ?? throw new IOException("no package root");
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
            return manifest.RootElement.TryGetProperty("package_version", out var version) ? version.GetString() : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The compiled module's cache, keyed by the installed module's hash and the patch's version.</summary>
    public static string CachePath(string localAppData, string sha256) =>
        Path.Combine(localAppData, "MSFSBlindAssist", "a350", $"{sha256}-p{A350ModulePatcher.Version}.cwasm");

    public static string Sha256Of(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    /// <summary>The compiled patched module: the cache when it loads, else patched, compiled and cached.
    /// <paramref name="built"/> says it was compiled now.</summary>
    public static Module LoadOrBuild(Engine engine, string modulePath, string localAppData, out bool built) =>
        LoadOrBuild(engine, modulePath, Sha256Of(modulePath), localAppData, null, out built);

    /// <summary>One build at a time in this process: a second load waits and takes the first's cache (review 2026-10-29:
    /// switching back to the A350 while its first compile ran started a second, 1.4 GB each, on the same file).</summary>
    private static readonly object BuildGate = new();

    /// <summary>A write left this long is a killed one: a compile and its write take seconds.</summary>
    private static readonly TimeSpan StaleWrite = TimeSpan.FromHours(1);

    /// <summary>The compiled patched module for a module whose hash the caller has (hashed once per start): the cache when
    /// it loads, else patched, compiled and cached; <paramref name="beforeBuild"/> runs as a compile starts, for whatever
    /// reason (no cache, a corrupt one: review 2026-10-29, a corrupt cache was compiled again in silence).</summary>
    public static Module LoadOrBuild(Engine engine, string modulePath, string sha, string localAppData, Action? beforeBuild,
        out bool built)
    {
        var cache = CachePath(localAppData, sha);
        if (TryLoad(engine, cache, sha) is { } loaded)
        {
            built = false;
            return loaded;
        }
        lock (BuildGate)
        {
            if (TryLoad(engine, cache, sha) is { } meanwhile)
            {
                built = false;   // compiled by the load before this one
                return meanwhile;
            }
            beforeBuild?.Invoke();
            var sw = Stopwatch.StartNew();
            var patched = A350ModulePatcher.Patch(File.ReadAllBytes(modulePath));
            var module = Module.FromBytes(engine, "a350", patched.Bytes);
            Log.Info("A350Display", $"module patched and compiled in {sw.ElapsedMilliseconds} ms ({Short(sha)}); allocator answered: "
                + string.Join(", ", patched.Allocator)
                + (patched.AllocatorLeftAlone.Count > 0 ? "; left to run, another signature: " + string.Join(", ", patched.AllocatorLeftAlone) : ""));
            WriteCache(module, cache, sha);
            built = true;
            return module;
        }
    }

    private static Module? TryLoad(Engine engine, string cache, string sha)
    {
        if (!File.Exists(cache))
            return null;
        var sw = Stopwatch.StartNew();
        try
        {
            var cached = Module.Deserialize(engine, "a350", File.ReadAllBytes(cache));
            Log.Info("A350Display", $"module cache loaded in {sw.ElapsedMilliseconds} ms ({Short(sha)})");
            return cached;
        }
        catch (Exception ex)
        {
            Log.Warn("A350Display", "module cache unreadable, rebuilding: " + ex.Message);
            return null;
        }
    }

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;

    /// <summary>Writes the cache under a name of its own and moves it in (two writes never share a file). One that cannot
    /// be written is logged, never a failure: the compiled module is used this time (review 2026-10-29: the displays went
    /// unavailable and the module leaked).</summary>
    private static void WriteCache(Module module, string cache, string sha)
    {
        string tmp = $"{cache}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllBytes(tmp, module.Serialize());
            File.Move(tmp, cache, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("A350Display", "module cache not written, the compiled module is used: " + ex.Message);
            try { File.Delete(tmp); } catch (Exception) { }
            return;
        }
        Prune(Path.GetDirectoryName(cache)!, cache, sha);
    }

    /// <summary>Removes other modules' caches and older names, writes left over an hour (a killed one), and of this
    /// module's other patch versions all but the latest written: two builds of different versions (the app and a probe, a
    /// release and a test build) keep each other's instead of compiling on every start (review 2026-10-29).</summary>
    private static void Prune(string dir, string cache, string sha)
    {
        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch (Exception) { return; }
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cache };
        var otherVersion = files
            .Where(f => !keep.Contains(f) && f.EndsWith(".cwasm", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(f).StartsWith(sha + "-p", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (otherVersion != null)
            keep.Add(otherVersion);
        var now = DateTime.UtcNow;
        foreach (var f in files)
        {
            if (keep.Contains(f))
                continue;
            bool write = f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
            if (write ? now - File.GetLastWriteTimeUtc(f) < StaleWrite : !f.EndsWith(".cwasm", StringComparison.OrdinalIgnoreCase))
                continue;   // a write under way, or no file of ours
            try { File.Delete(f); } catch (Exception) { }
        }
    }
}
