using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The HUD (the option on, the combiner deployed; live 2026-10-30, on the stand and in the climb): all green, its FMA in the PFD's columns,
// its scales' numbers cut off at their ends by the clip.
public class A350HudTests
{
    private static IReadOnlyList<string> Read(string fixture) => A350GroundDisplays.ReadHud(A350Fixtures.Capture(fixture + ".json"));

    [Fact]
    public void The_fma_reads_by_column()
    {
        var lines = Read("hud_climb");
        Assert.Contains("FMA autothrust: THR DCLB, green", lines);
        Assert.Contains("FMA vertical: OP CLB; ALT, green", lines);
        Assert.Contains("FMA lateral: NAV, green", lines);
        Assert.Contains("FMA engagement: ap1; 1 FD 2; A/THR, green", lines);
    }

    [Fact]
    public void Its_values_are_named()
    {
        var lines = Read("hud_climb");
        Assert.True(lines.Contains("Selected altitude: 10000, green"), string.Join(" | ", lines));
        Assert.Contains("QNH: 1013, green", lines);
        Assert.Contains("Wind: 303 / 8, green", lines);
        Assert.Contains("Speed scale: 160, 180, 200, 220, green", lines);
        Assert.Contains("Altitude scale: 030, 035, 040, green", lines);
        Assert.Contains("Heading scale: 28, 29, 30, green", lines);
    }

    [Fact]
    public void On_the_stand_it_says_the_ground_speed_and_only_what_the_clip_leaves()
    {
        var lines = Read("hud_ground");
        Assert.Contains("Ground speed: 0, green", lines);
        Assert.Contains("FMA engagement: 1 FD 2, green", lines);
        Assert.Contains("Heading scale: 21, 22, 23, green", lines);
        Assert.DoesNotContain(lines, l => l.Contains("19", StringComparison.Ordinal));
    }

    [Fact]
    public void Stowed_it_is_blank() =>
        Assert.Equal(new[] { "HUD: blank" }, A350GroundDisplays.ReadHud(new A350DrawCapture("HUD", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 0)));
}
