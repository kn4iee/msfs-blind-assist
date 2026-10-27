using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// The engine finds the A350's gauge objects in the memory copy by their vtable (the C++ class's
// typeinfo name, "3CTR" for CTR), and the A350's allocation in the sim by three strings at their
// data-segment addresses. Both are re-derived from the module at load.
public class A350ModuleFactsTests
{
    private static byte[] U32(params uint[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();

    private static A350WasmModule Sample(bool withAnchors = true)
    {
        var m = new WasmTestModule();
        int t0 = m.AddType(Array.Empty<byte>(), Array.Empty<byte>());
        m.Func("f", t0, Array.Empty<byte>());
        // vtables: zero, typeinfo pointer, then the vtable proper (whose address is what objects hold)
        m.Data(2000, U32(0, 3000, 11, 12, 0, 3200, 13));
        // typeinfo objects: {vptr, name pointer}
        m.Data(3000, U32(1, 3100));
        m.Data(3200, U32(1, 3150));
        m.Data(3100, "3CTR\0");
        m.Data(3150, "12NotLength\0");          // a name whose length prefix does not match is no class
        if (withAnchors) m.Data(5000, "\0INI_MCDU{}_ENTER\0INI_MCDU_OPTION\0INI_FMC_PAGE1\0");
        return A350WasmModule.Parse(m.Build());
    }

    [Fact]
    public void A_vtable_is_named_by_its_typeinfo()
    {
        var facts = A350ModuleFacts.From(Sample());
        Assert.Equal("CTR", facts.VTables[2008]);
        Assert.Single(facts.VTables);
        Assert.Equal(new uint[] { 2008 }, facts.VTablesOf("CTR"));
        Assert.Empty(facts.VTablesOf("EFIS"));
    }

    [Fact]
    public void The_anchors_are_found_by_their_strings()
    {
        var facts = A350ModuleFacts.From(Sample());
        Assert.Equal(3, facts.Anchors.Count);
        Assert.Equal(("INI_MCDU{}_ENTER", 5001), facts.Anchors[0]);
        Assert.Equal("INI_FMC_PAGE1", facts.Anchors[2].Text);
    }

    [Fact]
    public void A_missing_anchor_is_a_module_error()
    {
        var e = Assert.Throws<A350ModuleException>(() => A350ModuleFacts.From(Sample(withAnchors: false)));
        Assert.Contains("INI_MCDU{}_ENTER", e.Message);
    }
}
