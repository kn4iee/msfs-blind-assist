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
