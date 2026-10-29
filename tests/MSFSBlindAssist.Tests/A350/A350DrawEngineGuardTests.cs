using MSFSBlindAssist.Aircraft.A350.Display;
using Wasmtime;

namespace MSFSBlindAssist.Tests.A350;

// The draw code calls back into MSFSBA (the hooks, L:var and A: var questions) with pointers from the
// memory copy, and a torn copy hands it garbage. An exception escaping a Wasmtime host callback fails
// the whole process fast (live: 0xC0000409 during an EFIS draw), so every callback is guarded: a throw
// becomes a zero answer and a logged error.
public class A350DrawEngineGuardTests
{
    [Fact]
    public void A_throwing_callback_answers_zero_and_reports_instead_of_throwing()
    {
        string? seen = null;
        var guarded = A350DrawEngine.Guard("fsVarsNamedVarGet", (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) =>
        {
            r[0] = 5d;
            throw new ArgumentOutOfRangeException("ptr");
        }, new[] { ValueKind.Float64 }, (name, ex) => seen = name + ": " + ex.GetType().Name);
        var results = new ValueBox[1];
        guarded(default, ReadOnlySpan<ValueBox>.Empty, results);
        Assert.Equal(0d, results[0].AsDouble());
        Assert.Equal("fsVarsNamedVarGet: ArgumentOutOfRangeException", seen);
    }

    [Fact]
    public void A_working_callback_is_untouched()
    {
        var guarded = A350DrawEngine.Guard("x", (Caller c, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => r[0] = 7,
            new[] { ValueKind.Int32 }, (_, _) => throw new InvalidOperationException("not called"));
        var results = new ValueBox[1];
        guarded(default, ReadOnlySpan<ValueBox>.Empty, results);
        Assert.Equal(7, results[0].AsInt32());
    }

    // A torn copy makes the draw code read out of bounds; that must come back as a trap the engine
    // reports, never take the process down.
    [Fact]
    public void An_out_of_bounds_read_in_wasm_is_a_trap_exception()
    {
        var m = new WasmTestModule();
        int t0 = m.AddType(Array.Empty<byte>(), Array.Empty<byte>());
        int f = m.Func("oob", t0, new byte[] { 0x41, 0x70, 0x28, 0x02, 0x00, 0x1A });   // i32.const -16; i32.load; drop
        m.Export("oob", f);
        using var engine = A350ModuleCache.CreateEngine();
        using var module = Module.FromBytes(engine, "t", m.Build());
        using var store = new Store(engine);
        using var linker = new Linker(engine);
        var inst = linker.Instantiate(store, module);
        Assert.Throws<TrapException>(() => inst.GetFunction("oob")!.Invoke());
        Assert.Throws<TrapException>(() => inst.GetFunction("oob")!.Invoke());
    }

    // [A350-9] Wasmtime returns from a trap with a non-local jump, which a CET shadow stack refuses with a
    // fast-fail (0xC0000409): .NET 9+ marks x64 apps CET-compatible by default, so every trap in the A350
    // draw code (a torn copy) would kill MSFSBA. Every exe that hosts the engine opts out: the app and A350Probe
    // (review 2026-10-29, engine M10: only the app was checked).
    [Theory]
    [InlineData("MSFSBlindAssist", "MSFSBlindAssist.csproj")]
    [InlineData("tools", "A350Probe", "A350Probe.csproj")]
    public void Every_exe_hosting_the_engine_is_not_cet_compatible_so_a_trap_cannot_kill_it(params string[] path)
    {
        var csproj = System.Xml.Linq.XDocument.Load(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot() }.Concat(path).ToArray()));
        var cet = csproj.Descendants().FirstOrDefault(e => e.Name.LocalName == "CETCompat");
        Assert.NotNull(cet);
        Assert.Equal("false", cet!.Value.Trim(), ignoreCase: true);
    }
}
