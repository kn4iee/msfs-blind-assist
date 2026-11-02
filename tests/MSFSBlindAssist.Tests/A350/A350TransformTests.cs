using MSFSBlindAssist.Aircraft.A350.Display;
using static MSFSBlindAssist.Tests.A350.WasmTestModule;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-31 the draw code called nvgTransform after an MFD click in the menu bar, and the reader only warned: its six
// numbers did not fit the hook, so every text after it sat in the wrong place. The patch now hands them over in two reports.
public class A350TransformTests
{
    private static byte[] CallCtx(int function) => new byte[] { 0x41, 0x01, 0x10 }.Concat(U(function)).ToArray();

    /// <summary>nvgTransform(ctx 1, 2, 0, 0, 2, 100, 200): twice the size, moved by (100, 200).</summary>
    private static byte[] Transform()
    {
        var body = new List<byte> { 0x41, 0x01 };
        foreach (var f in new[] { 2f, 0f, 0f, 2f, 100f, 200f })
        {
            body.Add(0x43);
            body.AddRange(BitConverter.GetBytes(f));
        }
        body.Add(0x10);
        body.AddRange(U(A350DrawEngineTests.NvgTransformFunction));
        return body.ToArray();
    }

    private static DrawnText Drawn(byte[] prelude)
    {
        using var engine = A350DrawEngineTests.Engine(prelude: prelude);
        engine.Load(A350DrawEngineTests.Copy(trap: false));
        var capture = engine.Draw("CTR");
        Assert.True(capture.Ok, capture.Error);
        return Assert.Single(capture.Texts);
    }

    [Fact]
    public void A_text_after_nvgTransform_is_placed_by_its_matrix()
    {
        var text = Drawn(Transform());
        Assert.Equal((120f, 240f), (text.X, text.Y));   // 10 * 2 + 100, 20 * 2 + 200
    }

    [Fact]
    public void nvgRestore_undoes_a_transform()
    {
        var text = Drawn(CallCtx(A350DrawEngineTests.NvgSaveFunction).Concat(Transform()).Concat(CallCtx(A350DrawEngineTests.NvgRestoreFunction)).ToArray());
        Assert.Equal((10f, 20f), (text.X, text.Y));
    }

    [Fact]
    public void nvgResetTransform_clears_it()
    {
        var text = Drawn(Transform().Concat(CallCtx(A350DrawEngineTests.NvgResetTransformFunction)).ToArray());
        Assert.Equal((10f, 20f), (text.X, text.Y));
    }

    [Fact]
    public void A_transform_multiplies_onto_the_one_in_force()
    {
        var text = Drawn(Transform().Concat(Transform()).ToArray());
        Assert.Equal((340f, 680f), (text.X, text.Y));   // 2 * (2 * 10 + 100) + 100, 2 * (2 * 20 + 200) + 200
    }

    // [A350-11]: a patch change bumps the version, so an old compile in the cache is never loaded.
    [Fact]
    public void The_patch_version_moves_with_the_transform_hook() => Assert.True(A350ModulePatcher.Version >= 6);

    // Another version's nvgTransform with other parameters: hooked as if it had these, the module would fail validation and
    // take every display with it. Left alone, the positions after it are what is lost, and the log says so.
    [Fact]
    public void A_transform_with_other_parameters_still_compiles()
    {
        var patched = A350ModulePatcher.Patch(A350ModulePatcherTests.Sample(transformTakesOnlyTheContext: true)).Bytes;
        using var engine = A350ModuleCache.CreateEngine();
        using var module = Wasmtime.Module.FromBytes(engine, "a350", patched);
        Assert.NotNull(module);
    }

    // Live 2026-11-02 on 1.2.8 (patch 6), at the VCBI stand: what calls nvgTransform is both EFIS draws, not the MFD; the ND's
    // airport map draws each label at its own origin (local 0, 0) and places it with nvgTransform alone, so before patch 6 every
    // stand, terminal and pier label sat at one point. Captured live (A350Probe draw live EFIS#0 with A350_DRAW_JSON).
    [Fact]
    public void The_airport_maps_labels_are_placed_by_nvgTransform_across_the_nd()
    {
        var capture = A350Fixtures.Capture("p03_efis_transform.json");
        var labels = capture.Texts.Where(t => t.Text is "TERMINAL" or "PIER 1" or "CARGO VILLAGE TERMINAL" or "TWR").ToList();
        Assert.Equal(4, labels.Count);
        Assert.All(labels, t => Assert.Equal((0f, 0f), (t.LocalX, t.LocalY)));   // drawn at the origin: the transform alone places them
        Assert.All(labels, t => Assert.True(t.X > 836, $"{t.Text} at {t.X} is not on the ND"));
        Assert.Equal(labels.Count, labels.Select(t => (t.X, t.Y)).Distinct().Count());
    }
}
