using MSFSBlindAssist.Aircraft.A350.Display;
using Wasmtime;

namespace MSFSBlindAssist.Tests.A350;

// The patched A350 module is compiled once per A350 version on the pilot's machine and cached by the
// module's SHA-256; later starts load the cache. These run on the patcher tests' sample module.
public class A350ModuleCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a350cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string WriteModule(string folder, string package = "inibuilds-aircraft-a350")
    {
        var path = Path.Combine(_root, "Packages", folder, package, A350ModuleCache.ModuleRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, A350ModulePatcherTests.Sample());
        return path;
    }

    [Fact]
    public void The_module_is_found_in_a_community_folder_or_an_official_one()
    {
        Assert.Null(A350ModuleCache.FindInstalledModule(Path.Combine(_root, "Packages")));
        Directory.CreateDirectory(Path.Combine(_root, "Packages", "Community", "inibuilds-aircraft-a350-zCore2_HUES"));
        Assert.Null(A350ModuleCache.FindInstalledModule(Path.Combine(_root, "Packages")));   // a livery pack, no module
        var official = WriteModule(Path.Combine("Official2024", "OneStore"), "inibuilds-aircraft-a350");
        Assert.Equal(official, A350ModuleCache.FindInstalledModule(Path.Combine(_root, "Packages")));
        var community = WriteModule("Community");
        Assert.Equal(community, A350ModuleCache.FindInstalledModule(Path.Combine(_root, "Packages")));   // Community wins, as the sim loads it over the official one
    }

    // Review 2026-10-29 (engine I4): the reader prepared the installed module once and never checked it again, so after an
    // A350 update with MSFSBA left open the old patched code drew the new memory. The module's identity (its size and
    // write time) is compared on each re-locate; an update changes it, and a missing module has none.
    [Fact]
    public void An_updated_module_has_another_identity()
    {
        var path = WriteModule("Community");
        var before = A350ModuleCache.IdentityOf(path);
        Assert.NotNull(before);
        Assert.Equal(before, A350ModuleCache.IdentityOf(path));
        File.WriteAllBytes(path, A350ModulePatcherTests.Sample().Concat(new byte[] { 0 }).ToArray());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.NotEqual(before, A350ModuleCache.IdentityOf(path));
        Assert.Null(A350ModuleCache.IdentityOf(path + ".gone"));
    }

    // Keyed by the hash alone, a module compiled from an older patch was loaded after the patch changed (2026-10-29:
    // the allocator patch would silently not apply). The patch's version is part of the key.
    [Fact]
    public void The_cache_is_keyed_by_the_module_hash_and_the_patch_version()
    {
        var a = A350ModuleCache.CachePath(_root, "abc");
        Assert.Equal(Path.Combine(_root, "MSFSBlindAssist", "a350", $"abc-p{A350ModulePatcher.Version}.cwasm"), a);
        Assert.NotEqual(a, A350ModuleCache.CachePath(_root, "abd"));
    }

    [Fact]
    public void First_load_builds_and_caches_then_the_cache_is_used()
    {
        var module = WriteModule("Community");
        using var engine = A350ModuleCache.CreateEngine();
        using (var m1 = A350ModuleCache.LoadOrBuild(engine, module, _root, out bool built1))
        {
            Assert.True(built1);
            Assert.Contains(m1.Exports, e => e.Name == "emu_CTR_Draw");
        }
        var sha = A350ModuleCache.Sha256Of(module);
        Assert.True(File.Exists(A350ModuleCache.CachePath(_root, sha)));
        using (var m2 = A350ModuleCache.LoadOrBuild(engine, module, _root, out bool built2))
        {
            Assert.False(built2);
            Assert.Contains(m2.Exports, e => e.Name == "emu_CTR_Draw");
        }
    }

    // Review 2026-10-29 (engine M3): the reader said "getting ready" only when no cache file existed, so a corrupt cache was
    // compiled again in silence. A build says so as it starts, whatever the reason; a cache load says nothing.
    [Fact]
    public void A_build_says_so_as_it_starts_and_a_cache_load_does_not()
    {
        var module = WriteModule("Community");
        var sha = A350ModuleCache.Sha256Of(module);
        var cache = A350ModuleCache.CachePath(_root, sha);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllBytes(cache, new byte[] { 1, 2, 3 });   // corrupt
        using var engine = A350ModuleCache.CreateEngine();
        int starts = 0;
        using (var m = A350ModuleCache.LoadOrBuild(engine, module, sha, _root, () => starts++, out bool built))
            Assert.True(built);
        Assert.Equal(1, starts);
        using (var m = A350ModuleCache.LoadOrBuild(engine, module, sha, _root, () => starts++, out bool built))
            Assert.False(built);
        Assert.Equal(1, starts);
    }

    // A cache that cannot be written (a full disk, a folder in its place) made the displays unavailable although the module
    // compiled, and leaked it: the compiled module is used anyway, and no half-written file is left.
    [Fact]
    public void A_cache_that_cannot_be_written_still_gives_the_compiled_module()
    {
        var module = WriteModule("Community");
        var sha = A350ModuleCache.Sha256Of(module);
        var cache = A350ModuleCache.CachePath(_root, sha);
        Directory.CreateDirectory(cache);   // a folder where the file goes: the move fails
        using var engine = A350ModuleCache.CreateEngine();
        using var m = A350ModuleCache.LoadOrBuild(engine, module, sha, _root, null, out bool built);
        Assert.True(built);
        Assert.Contains(m.Exports, e => e.Name == "emu_CTR_Draw");
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(cache)!, "*.tmp"));
    }

    // A killed write left a ~118 MB .tmp that nothing removed; two writes at once shared one .tmp name. Each write has a
    // name of its own, and one left over an hour is removed; one being written now is not.
    [Fact]
    public void A_left_over_write_is_removed_and_a_live_one_kept()
    {
        var module = WriteModule("Community");
        var sha = A350ModuleCache.Sha256Of(module);
        var dir = Path.GetDirectoryName(A350ModuleCache.CachePath(_root, sha))!;
        Directory.CreateDirectory(dir);
        var stale = Path.Combine(dir, "x.cwasm.1.dead.tmp");
        var live = Path.Combine(dir, "x.cwasm.2.live.tmp");
        File.WriteAllBytes(stale, new byte[] { 1 });
        File.WriteAllBytes(live, new byte[] { 2 });
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
        using var engine = A350ModuleCache.CreateEngine();
        using var m = A350ModuleCache.LoadOrBuild(engine, module, sha, _root, null, out _);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(live));
    }

    // Two builds with another patch version (the app and a probe, a release and a test build) removed each other's cache,
    // so each recompiled on every start. The module's caches of one other version are kept: the latest written.
    [Fact]
    public void Another_patch_versions_cache_of_the_same_module_is_kept()
    {
        var module = WriteModule("Community");
        var sha = A350ModuleCache.Sha256Of(module);
        var dir = Path.GetDirectoryName(A350ModuleCache.CachePath(_root, sha))!;
        Directory.CreateDirectory(dir);
        var newer = Path.Combine(dir, $"{sha}-p{A350ModulePatcher.Version + 1}.cwasm");
        var older = Path.Combine(dir, $"{sha}-p{A350ModulePatcher.Version - 1}.cwasm");
        File.WriteAllBytes(newer, new byte[] { 1 });
        File.WriteAllBytes(older, new byte[] { 2 });
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));
        using var engine = A350ModuleCache.CreateEngine();
        using var m = A350ModuleCache.LoadOrBuild(engine, module, sha, _root, null, out _);
        Assert.True(File.Exists(newer));
        Assert.False(File.Exists(older));
    }

    // Switching back to the A350 while its first compile ran started a second one (1.4 GB each) on the same file. One
    // build at a time: the second waits and loads the first's cache.
    [Fact]
    public void Two_loads_at_once_compile_once()
    {
        var module = WriteModule("Community");
        var sha = A350ModuleCache.Sha256Of(module);
        using var engine = A350ModuleCache.CreateEngine();
        int builds = 0;
        Parallel.For(0, 2, i =>
        {
            using var m = A350ModuleCache.LoadOrBuild(engine, module, sha, _root, () => Interlocked.Increment(ref builds), out _);
        });
        Assert.Equal(1, builds);
    }

    [Fact]
    public void A_corrupt_cache_is_rebuilt_and_an_old_version_cache_removed()
    {
        var module = WriteModule("Community");
        var sha = A350ModuleCache.Sha256Of(module);
        var cache = A350ModuleCache.CachePath(_root, sha);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllBytes(cache, new byte[] { 1, 2, 3 });
        var old = A350ModuleCache.CachePath(_root, "0ld");
        File.WriteAllBytes(old, new byte[] { 9 });
        // The name before the patch version was in it (<sha>.cwasm), for this very module: compiled from an older patch,
        // never loaded, and removed (review 2026-10-29).
        var legacy = Path.Combine(Path.GetDirectoryName(cache)!, sha + ".cwasm");
        File.WriteAllBytes(legacy, new byte[] { 7 });
        using var engine = A350ModuleCache.CreateEngine();
        using var m = A350ModuleCache.LoadOrBuild(engine, module, _root, out bool built);
        Assert.True(built);
        Assert.True(new FileInfo(cache).Length > 3);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(legacy));
    }
}
