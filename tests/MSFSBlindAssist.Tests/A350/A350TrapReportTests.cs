using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The two traps of the flight of 2026-10-31, verbatim (their backtraces hold no pilot data). The log named neither the
// cause nor the cost; each is a pointer past the copy, the copy having changed under the draw.
public class A350TrapReportTests
{
    private const string NdTrap = """
        error while executing at wasm backtrace:
            0: 0x1cf3340 - <unknown>!ND_Logic::drawModernConnections(NVGcontext*, FlightPlan*, int, bool, nd_draw_variables_struct)
            1: 0x1cf6300 - <unknown>!ND_Logic::drawWaypointConnections(NVGcontext*, FlightPlan*, int, bool, nd_draw_variables_struct)
            2: 0x1d4318a - <unknown>!ND::drawSymbols(NVGcontext*, bool)
            3: 0x1d67bb4 - <unknown>!ND::DrawRose(NVGcontext*, bool)
            4: 0x1d75914 - <unknown>!ND::DrawNDInstance(NVGcontext*, bool)
            5: 0x1d81091 - <unknown>!ND::Draw(float, float, GaugeData*)
            6: 0x1f8d16a - <unknown>!EFIS::Draw()
        Caused by:
            0: memory fault at wasm address 0x405d46e6 in linear memory of size 0x19d40000
            1: wasm trap: out of bounds memory access
        """;

    private const string MfdTrap = """
        error while executing at wasm backtrace:
            0: 0x1390d0a - <unknown>!FlightPlanButtonNew::draw(NVGcontext*, int, double, double, bool, int)
            1: 0x138eb48 - <unknown>!FMC_FPLAN::draw(NVGcontext*, float, float, float, float, bool)
            2: 0x1148edd - <unknown>!FMGS::DrawFMS_A350(NVGcontext*, bool)
            3: 0x114bd3b - <unknown>!FMGS::Draw()
            4: 0x1fa0eb3 - <unknown>!MFD::Draw()
        Caused by:
            0: memory fault at wasm address 0x44a8575c in linear memory of size 0x19d40000
            1: wasm trap: out of bounds memory access
        """;

    [Fact]
    public void The_nd_trap_of_31_october_is_a_pointer_past_the_copy()
    {
        var r = A350TrapReport.Parse(NdTrap)!;
        Assert.Equal("ND_Logic::drawModernConnections", r.Function);
        Assert.Equal(7, r.Frames.Count);
        Assert.Equal("EFIS::Draw", r.Frames[^1]);
        Assert.Equal(A350TrapKind.PastTheMemory, r.Kind);
        Assert.Equal(0x405d46e6UL, r.Address);
        Assert.Equal(0x19d40000UL, r.MemorySize);
        Assert.Equal("EFIS#0 trap in ND_Logic::drawModernConnections: a pointer past the copy (0x405d46e6, memory 0x19d40000), retried",
            r.Summary("EFIS", 0));
    }

    [Fact]
    public void The_mfd_trap_of_31_october_is_a_pointer_past_the_copy()
    {
        var r = A350TrapReport.Parse(MfdTrap)!;
        Assert.Equal("FlightPlanButtonNew::draw", r.Function);
        Assert.Equal(A350TrapKind.PastTheMemory, r.Kind);
        Assert.Equal("MFD#0 trap in FlightPlanButtonNew::draw: a pointer past the copy (0x44a8575c, memory 0x19d40000), retried",
            r.Summary("MFD", 0));
    }

    [Fact]
    public void A_fault_inside_the_memory_is_named_so()
    {
        var r = A350TrapReport.Parse(NdTrap.Replace("0x405d46e6", "0x00001000"))!;
        Assert.Equal(A350TrapKind.InsideTheMemory, r.Kind);
        Assert.Contains("a fault inside the copy (0x1000, memory 0x19d40000)", r.Summary("EFIS", 0));
    }

    [Fact]
    public void A_deadline_trap_is_named_so()
    {
        var r = A350TrapReport.Parse("""
            error while executing at wasm backtrace:
                0: 0x1f8d16a - <unknown>!EFIS::Draw()
            Caused by:
                wasm trap: interrupt
            """)!;
        Assert.Equal(A350TrapKind.Deadline, r.Kind);
        Assert.Null(r.Address);
        Assert.Equal("EFIS#1 trap in EFIS::Draw: the draw ran past its deadline, retried", r.Summary("EFIS", 1));
    }

    [Theory]
    [InlineData("wasm trap: wasm `unreachable` instruction executed")]
    [InlineData("wasm trap: call stack exhausted")]
    public void A_message_without_a_fault_line_still_names_its_function(string cause)
    {
        var r = A350TrapReport.Parse($"""
            error while executing at wasm backtrace:
                0: 0x10 - <unknown>!PFD::DrawFMA(NVGcontext*)
                1: 0x20 - <unknown>!EFIS::Draw()
            Caused by:
                {cause}
            """)!;
        Assert.Equal("PFD::DrawFMA", r.Function);
        Assert.Equal(A350TrapKind.Other, r.Kind);
        Assert.Equal("EFIS#0 trap in PFD::DrawFMA: a trap, retried", r.Summary("EFIS", 0));
    }

    // A trap that is not retried (the request's second, or the retry itself) does not say it was.
    [Fact]
    public void A_trap_not_retried_does_not_say_retried()
    {
        var r = A350TrapReport.Parse(NdTrap)!;
        Assert.Equal("EFIS#0 trap in ND_Logic::drawModernConnections: a pointer past the copy (0x405d46e6, memory 0x19d40000)",
            r.Summary("EFIS", 0, retried: false));
    }

    // A Wasmtime version that words it differently: the log keeps today's line.
    [Theory]
    [InlineData("")]
    [InlineData("something else went wrong")]
    [InlineData("wasm trap: out of bounds memory access")]
    public void An_unknown_format_parses_to_null(string message) => Assert.Null(A350TrapReport.Parse(message));
}
