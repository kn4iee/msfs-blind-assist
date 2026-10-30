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

    // The FMS keeps the take-off speeds in its active flight plan (FlightPlan::getV1Speed() returns this+964 in 1.2.7);
    // INI_V1, INI_VR and INI_V2 are never written (sim 2026-10-30). Found by name: each getter's returned load is the
    // speed's offset, and update_v1() passes the active flight plan, a static object, as the getter's constant `this`,
    // through a local as the unoptimised module does, or pushed straight.
    private static A350WasmModule WithFlightPlan(bool throughLocal = true, bool withGetters = true, bool withApproachReset = true)
    {
        var m = new WasmTestModule();
        int t1 = m.AddType(new[] { I32 }, new[] { I32 });
        int t0 = m.AddType(Array.Empty<byte>(), Array.Empty<byte>());
        int Getter(string name, int offset) => m.Func(name, t1,
            new byte[] { 0x20, 0x00, 0x21, 0x01, 0x20, 0x01, 0x28, 0x02 }.Concat(U(offset)).Concat(new byte[] { 0x0F }).ToArray(),
            localGroups: new byte[] { 1, 1, I32 });
        int v1 = withGetters ? Getter("FlightPlan::getV1Speed()", 964) : m.Func("other", t1, new byte[] { 0x20, 0x00 });
        if (withGetters)
        {
            Getter("FlightPlan::getVRSpeed()", 968);
            Getter("FlightPlan::getV2Speed()", 972);
        }
        var call = new List<byte> { 0x41 };
        call.AddRange(S(7));   // a constant that is not the flight plan, consumed first
        call.Add(0x1A);
        call.Add(0x41);
        call.AddRange(S(1418656));
        if (throughLocal) call.AddRange(new byte[] { 0x21, 0x00, 0x20, 0x00 });
        call.Add(0x10);
        call.AddRange(U(v1));
        call.Add(0x1A);
        m.Func("update_v1()", t0, call.ToArray(), localGroups: new byte[] { 1, 1, I32 });
        if (withApproachReset)
        {
            // resetApproachData() as the unoptimised module has it: the prologue stores `this` into the frame (offset 12),
            // then each field gets 0, the first through a local: local.get 0; i32.const 0; local.set 1; local.get 1;
            // i32.store offset=748; local.get 0; i32.const 0; i32.store offset=752.
            var reset = new List<byte> { 0x20, 0x00, 0x20, 0x00, 0x36, 0x02, 0x0C };
            reset.AddRange(new byte[] { 0x20, 0x00, 0x41, 0x00, 0x21, 0x01, 0x20, 0x01, 0x36, 0x02 });
            reset.AddRange(U(748));
            reset.AddRange(new byte[] { 0x20, 0x00, 0x41, 0x00, 0x36, 0x02 });
            reset.AddRange(U(752));
            m.Func("FlightPlan::resetApproachData()", m.AddType(new[] { I32 }, Array.Empty<byte>()), reset.ToArray(),
                localGroups: new byte[] { 1, 1, I32 });
        }
        m.Data(5000, "\0INI_MCDU{}_ENTER\0INI_MCDU_OPTION\0INI_FMC_PAGE1\0");
        return A350WasmModule.Parse(m.Build());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_take_off_speeds_are_found_by_the_flight_plan_s_getters(bool throughLocal)
    {
        var facts = A350ModuleFacts.From(WithFlightPlan(throughLocal));
        Assert.Equal(new A350TakeoffSpeedFacts(1418656, 964, 968, 972), facts.TakeoffSpeeds);
    }

    // The approach minimums (live 2026-10-30): PERF APPR's BARO at the active flight plan's +748, RADIO at +752 (0 not set,
    // -1 NO DH), the two fields FlightPlan::resetApproachData() clears, in that order.
    [Fact]
    public void The_minimums_are_the_fields_the_approach_reset_clears()
    {
        Assert.Equal(new A350MinimumsFacts(1418656, 748, 752), A350ModuleFacts.From(WithFlightPlan()).Minimums);
        Assert.Null(A350ModuleFacts.From(WithFlightPlan(withApproachReset: false)).Minimums);
        Assert.Null(A350ModuleFacts.From(WithFlightPlan(withGetters: false)).Minimums);   // no flight plan found
    }

    // A version that moved them leaves the call-outs without speeds, never the displays without a reader.
    [Fact]
    public void A_module_without_the_getters_has_no_take_off_speeds()
    {
        Assert.Null(A350ModuleFacts.From(WithFlightPlan(withGetters: false)).TakeoffSpeeds);
        Assert.Null(A350ModuleFacts.From(Sample()).TakeoffSpeeds);
    }
}
