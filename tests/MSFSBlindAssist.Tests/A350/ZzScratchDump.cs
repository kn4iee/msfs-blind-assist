using MSFSBlindAssist.Aircraft.A350.Display;
using Xunit;
namespace MSFSBlindAssist.Tests.A350;
public class ZzScratchDump
{
    [Fact]
    public void Dump()
    {
        var o = new List<string>();
        foreach (var d in new[] { "efis0_anf_arc_02", "efis0_anf_arc_1_menu", "efis0_anf_rose_01", "efis0_anf_plan_01" })
        {
            o.Add("=== " + d);
            foreach (var l in A350NdPage.Read(A350Fixtures.Capture(d + ".json"), 0)) o.Add($"{l.Kind,-8} {l.ClickX,7:0.0} {l.ClickY,7:0.0} {l.Spoken}");
        }
        File.WriteAllLines(@"C:\Users\franc\AppData\Local\Temp\claude\C--Users-franc-Documents-development\e887ae42-6f70-46ff-9438-cdb661cb6926\scratchpad\nd_lines.txt", o);
    }
}
