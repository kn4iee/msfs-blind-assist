using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;
using Xunit.Abstractions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A manual live check of the EFB memory reader against a running MSFS with the 757 loaded. Skipped unless
/// BBS_EFB_LIVE=1, so it never runs in CI. It opens the simulator read-only, locates the bbs757 module by the
/// FMA anchor, and prints the front apps' visible controls so a human can compare them with the tablet screen.
/// </summary>
public class Bbs757EfbMemoryLiveTests
{
    private readonly ITestOutputHelper _out;
    public Bbs757EfbMemoryLiveTests(ITestOutputHelper o) => _out = o;

    // The anchor and offsets measured on BBS752.wasm (2026-11-06); the live reader uses the same ones the
    // generator derives. anchor = "N3bbs14eicas_computerE\0" + 24 bytes at 0x181a4 (base64 below).
    private const uint AnchorAddress = 98788;
    private const string AnchorBase64 = "TjNiYnMxNGVpY2FzX2NvbXB1dGVyRQAAcCEAAOSBAQDkTAAAmg8AAJsPAAAAAAAA";

    private static Bbs757EfbMemLayout Layout(byte[] anchor) => new(
        SourceFile: "", AnchorAddress: AnchorAddress, Anchor: anchor,
        ManagerBase: 0xf1334, ManagerSpan: 0x100, EfbVptr: 0x13128, EfbToBbos: 132, BbosToAppMap: 0x238,
        AppFront: 0x140, AppPage: 0x144, AppRoots: 0x17c, PageChildren: 0x4c,
        CtrlName: 0x1c, CtrlRect: 0x38, CtrlVisible: 0x8c, CtrlChildren: 0xf4, CtrlParent: 0xe8,
        CtrlLabelText: 0x128, CtrlButtonText: 0x144,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("button", 0x30164), new Bbs757EfbClassVptr("label", 0x30cd4),
            new Bbs757EfbClassVptr("toggle", 0x30e7c), new Bbs757EfbClassVptr("slider", 0x30c48),
            new Bbs757EfbClassVptr("grid", 0x30320),
            // app classes, keyed by the token the model reader shows
            new Bbs757EfbClassVptr("HOME", 0x27bdc), new Bbs757EfbClassVptr("AIRCRAFT", 0x381c4),
            new Bbs757EfbClassVptr("NAVIGATION", 0x28d10), new Bbs757EfbClassVptr("SYSTEMS", 0x2e530),
            new Bbs757EfbClassVptr("CHECKLIST", 0x253f4), new Bbs757EfbClassVptr("SETTINGS", 0x2c860),
            new Bbs757EfbClassVptr("SIGN IN", 0x2e3d0),
        });

    [Fact]
    public void Prints_the_live_tablet_controls()
    {
        if (Environment.GetEnvironmentVariable("BBS_EFB_LIVE") != "1") return; // manual: MSFS + the 757 running
        using var src = SimProcess.TryOpen("FlightSimulator2024", out string? failure);
        Assert.True(src is not null, failure);
        byte[] anchor = Convert.FromBase64String(AnchorBase64);
        ulong? b = WasmMemoryLocator.FindLinearBase(src!, AnchorAddress, anchor);
        Assert.True(b is not null, "the bbs757 module was not located by the anchor");
        var reader = new Bbs757EfbMemory(new ModuleEfbMemory(new ModuleMemory(src!, b!.Value)), Layout(anchor));

        var apps = reader.ReadFrontApps();
        Assert.NotEmpty(apps);
        foreach (var app in apps)
            _out.WriteLine($"== app key {app.Key} {app.Class} ({Count(app.Controls)} visible controls)");

        if (Environment.GetEnvironmentVariable("BBS_EFB_RAW") == "1")
            foreach (var a in apps.Where(a => a.Controls.Count > 0))
            {
                _out.WriteLine($"\n== RAW app key {a.Key} {a.Class}:");
                foreach (var r in a.Controls) Print(r, 0);
            }

        var model = new Bbs757EfbModelReader(reader).Read();
        Assert.NotNull(model);
        _out.WriteLine($"\n== MODEL page={model!.PageName}  App={model.App}");
        _out.WriteLine("-- menu:");
        foreach (var it in model.Menu) _out.WriteLine($"   {it.Kind,-8} {it.Text}");
        _out.WriteLine("-- items:");
        foreach (var it in model.Items)
            _out.WriteLine($"   {it.Kind,-8} {it.Text}{(it.Value.Length > 0 ? " = " + it.Value : "")}{(it.On is { } on ? $" (on={on})" : "")}  @({it.Hit.X},{it.Hit.Y}) key={it.Key}");
    }

    /// <summary>
    /// Writes the front apps' trees, scrubbed, as a <c>.tree.json</c> fixture. Skipped unless BBS_EFB_CAPTURE names
    /// the output file. Node texts are never printed.
    /// </summary>
    [Fact]
    public void Captures_the_tablet_as_a_tree_fixture()
    {
        string? outFile = Environment.GetEnvironmentVariable("BBS_EFB_CAPTURE");
        if (string.IsNullOrEmpty(outFile)) return; // manual: MSFS + the 757 running
        using var src = SimProcess.TryOpen("FlightSimulator2024", out string? failure);
        Assert.True(src is not null, failure);
        byte[] anchor = Convert.FromBase64String(AnchorBase64);
        ulong? b = WasmMemoryLocator.FindLinearBase(src!, AnchorAddress, anchor);
        Assert.True(b is not null, "the bbs757 module was not located by the anchor");
        var reader = new Bbs757EfbMemory(new ModuleEfbMemory(new ModuleMemory(src!, b!.Value)), Layout(anchor));

        var apps = reader.ReadFrontApps();
        Assert.NotEmpty(apps);
        File.WriteAllText(outFile, Bbs757EfbTreeJson.Write(Bbs757EfbTreeScrub.Scrub(apps)));
        _out.WriteLine($"wrote {apps.Count} app(s), {apps.Sum(a => Count(a.Controls))} controls");
    }

    private static int Count(IReadOnlyList<Bbs757EfbMemory.Node> ns) => ns.Count + ns.Sum(n => Count(n.Children));

    private void Print(Bbs757EfbMemory.Node n, int depth)
    {
        string text = n.Text.Length > 0 ? $" \"{n.Text}\"" : "";
        _out.WriteLine($"{new string(' ', depth * 2)}{n.Kind,-8} {n.Name,-28} [{n.X:0},{n.Y:0} {n.W:0}x{n.H:0}]{text}");
        foreach (var c in n.Children) Print(c, depth + 1);
    }
}
