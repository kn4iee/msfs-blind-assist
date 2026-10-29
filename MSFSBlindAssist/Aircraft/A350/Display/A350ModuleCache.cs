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

    /// <summary>The engine every A350 module is compiled with; a cache only loads into the same config.</summary>
    public static Engine CreateEngine() => new(new Config().WithOptimizationLevel(OptimizationLevel.Speed));

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

    /// <summary>The compiled module's cache, keyed by the installed module's hash and the patch's version.</summary>
    public static string CachePath(string localAppData, string sha256) =>
        Path.Combine(localAppData, "MSFSBlindAssist", "a350", $"{sha256}-p{A350ModulePatcher.Version}.cwasm");

    public static string Sha256Of(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    /// <summary>The compiled patched module: the cache when it loads, else patched, compiled and cached
    /// (replacing other versions' caches). <paramref name="built"/> says it was compiled now.</summary>
    public static Module LoadOrBuild(Engine engine, string modulePath, string localAppData, out bool built)
    {
        var sha = Sha256Of(modulePath);
        var cache = CachePath(localAppData, sha);
        var sw = Stopwatch.StartNew();
        if (File.Exists(cache))
        {
            try
            {
                var cached = Module.Deserialize(engine, "a350", File.ReadAllBytes(cache));
                built = false;
                Log.Info("A350Display", $"module cache loaded in {sw.ElapsedMilliseconds} ms ({sha[..12]})");
                return cached;
            }
            catch (Exception ex)
            {
                Log.Warn("A350Display", "module cache unreadable, rebuilding: " + ex.Message);
            }
        }

        var patched = A350ModulePatcher.Patch(File.ReadAllBytes(modulePath));
        var module = Module.FromBytes(engine, "a350", patched.Bytes);
        Log.Info("A350Display", $"module patched and compiled in {sw.ElapsedMilliseconds} ms ({sha[..12]}); allocator answered: "
            + string.Join(", ", patched.Allocator)
            + (patched.AllocatorLeftAlone.Count > 0 ? "; left to run, another signature: " + string.Join(", ", patched.AllocatorLeftAlone) : ""));
        var dir = Path.GetDirectoryName(cache)!;
        Directory.CreateDirectory(dir);
        var tmp = cache + ".tmp";
        File.WriteAllBytes(tmp, module.Serialize());
        File.Move(tmp, cache, overwrite: true);
        foreach (var other in Directory.GetFiles(dir, "*.cwasm"))
            if (!string.Equals(other, cache, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(other); } catch (Exception) { }
        built = true;
        return module;
    }
}
