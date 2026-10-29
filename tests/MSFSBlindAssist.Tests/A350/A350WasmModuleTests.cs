using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// The display engine reads the A350's module by name: functions from the name section, strings from
// the data segments. These pin the reader on modules built here, never on an iniBuilds file.
public class A350WasmModuleTests
{
    private static (byte[] Bytes, int Text, int Draw) Sample()
    {
        var m = new WasmTestModule();
        int t6 = m.AddType(new[] { I32, I32, I32, I32, I32, I32 }, new[] { I32 });
        int tv = m.AddType(new[] { I32, F32, F32, I32, I32 }, Array.Empty<byte>());
        int t0 = m.AddType(Array.Empty<byte>(), Array.Empty<byte>());
        m.Import("wasi_snapshot_preview1", "fd_write", t6);
        m.Import("wasi_snapshot_preview1", "path_readlink", t6);
        int text = m.Func("nvgText", tv, Array.Empty<byte>());
        int draw = m.Func("CTR::Draw()", t0, Array.Empty<byte>());
        m.Func("nvgFillColor", t0, Array.Empty<byte>());
        m.Export("other", draw);
        m.Data(1024, "\0INI_MCDU{}_ENTER\0INI_FMC_PAGE1\0");
        m.Data(200000, new byte[300]);   // a segment whose size takes a two-byte LEB128
        return (m.Build(), text, draw);
    }

    [Fact]
    public void Parses_types_imports_and_names()
    {
        var (bytes, text, draw) = Sample();
        var mod = A350WasmModule.Parse(bytes);
        Assert.Equal(3, mod.Types.Count);
        Assert.Equal(new byte[] { I32, F32, F32, I32, I32 }, mod.Types[1].Params);
        Assert.Equal(2, mod.ImportedFunctionCount);
        Assert.Equal("path_readlink", mod.FunctionImports[1].Field);
        Assert.Equal("wasi_snapshot_preview1", mod.FunctionImports[1].Module);
        Assert.Equal("nvgText", mod.FunctionNames[text]);
        Assert.Equal(text, mod.FindFunction("nvgText"));
        Assert.Equal(draw, mod.FindFunction("CTR::Draw()"));
        Assert.Equal(2, mod.FunctionTypeOf(draw));
        Assert.Equal(1, mod.FunctionTypeOf(text));
    }

    [Fact]
    public void Finds_data_segments_and_strings()
    {
        var mod = A350WasmModule.Parse(Sample().Bytes);
        Assert.Equal(2, mod.DataSegments.Count);
        Assert.Equal(1024, mod.DataSegments[0].Base);
        Assert.Equal(300, mod.DataSegments[1].Bytes.Length);
        Assert.Equal(1025, mod.LinearAddressOf("INI_MCDU{}_ENTER"));
        Assert.Equal(1025 + "INI_MCDU{}_ENTER".Length + 1, mod.LinearAddressOf("INI_FMC_PAGE1"));
        Assert.Null(mod.LinearAddressOf("INI_NOT_THERE"));
        Assert.Equal("INI_FMC_PAGE1", mod.CString(1025 + "INI_MCDU{}_ENTER".Length + 1));
    }

    [Fact]
    public void A_missing_or_doubled_function_name_is_a_module_error()
    {
        var mod = A350WasmModule.Parse(Sample().Bytes);
        var e = Assert.Throws<A350ModuleException>(() => mod.FindFunction("EFIS::Draw()"));
        Assert.Contains("EFIS::Draw()", e.Message);

        var m = new WasmTestModule();
        int t0 = m.AddType(Array.Empty<byte>(), Array.Empty<byte>());
        m.Func("dup", t0, Array.Empty<byte>());
        m.Func("dup", t0, Array.Empty<byte>());
        Assert.Throws<A350ModuleException>(() => A350WasmModule.Parse(m.Build()).FindFunction("dup"));
    }

    [Fact]
    public void Not_a_wasm_file_is_a_module_error()
    {
        Assert.Throws<A350ModuleException>(() => A350WasmModule.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    // The FMS's take-off speeds are found in its code (A350ModuleFacts.TakeoffSpeeds): a function's instructions are
    // walked after its locals, each with its immediate (a constant, a load's offset, a call's function), the rest skipped.
    [Fact]
    public void A_function_s_instructions_are_walked_with_their_immediates()
    {
        var m = new WasmTestModule();
        int t1 = m.AddType(new[] { I32 }, new[] { I32 });
        int t0 = m.AddType(Array.Empty<byte>(), Array.Empty<byte>());
        int callee = m.Func("callee", t0, Array.Empty<byte>());
        // local.get 0; i32.load align=2 offset=964; block void; i32.const -5; drop; end; call callee;
        // br_table 0 1 (default 0); f64.const 1.5; drop; memory.fill 0; return
        var body = new List<byte> { 0x20, 0x00, 0x28, 0x02 };
        body.AddRange(U(964));
        body.AddRange(new byte[] { 0x02, 0x40, 0x41 });
        body.AddRange(S(-5));
        body.AddRange(new byte[] { 0x1A, 0x0B, 0x10 });
        body.AddRange(U(callee));
        body.AddRange(new byte[] { 0x0E, 0x02, 0x00, 0x01, 0x00, 0x44 });
        body.AddRange(BitConverter.GetBytes(1.5));
        body.AddRange(new byte[] { 0x1A, 0xFC, 0x0B, 0x00, 0x0F });
        int f = m.Func("FlightPlan::getV1Speed()", t1, body.ToArray(), localGroups: new byte[] { 1, 2, I32 });
        var mod = A350WasmModule.Parse(m.Build());

        Assert.Equal(new (byte, long)[]
        {
            (0x20, 0), (0x28, 964), (0x02, 0), (0x41, -5), (0x1A, 0), (0x0B, 0), (0x10, callee), (0x0E, 0), (0x44, 0),
            (0x1A, 0), (0xFC, 11), (0x0F, 0), (0x0B, 0),
        }, mod.Instructions(f).ToArray());
        Assert.Equal(new (byte, long)[] { (0x0B, 0) }, mod.Instructions(callee).ToArray());   // an empty body: its end only
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(69_270_541L)]
    public void Leb128_round_trips(long n)
    {
        var bytes = U(n);
        int p = 0;
        Assert.Equal(n, (long)A350WasmModule.ReadU(bytes, ref p));
        Assert.Equal(bytes.Length, p);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-200000L)]
    [InlineData(1451120L)]
    public void Signed_leb128_round_trips(long n)
    {
        var bytes = S(n);
        int p = 0;
        Assert.Equal(n, A350WasmModule.ReadS(bytes, ref p));
    }
}
