using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;

namespace MSFSBlindAssist.Tests;

/// <summary>The speedbrake lever is said as the FlyByWire A32NX says it (its own bands and words, driven through
/// its own code), the first reading silent, through the mute wrap; its Ctrl+M row exists because it mutes it.</summary>
public class A330SpeedbrakeTests
{
    private sealed class Clock { public long Now = 100_000; }

    private static A330PanelSetTests.Def NewDef()
    {
        var clock = new Clock();
        return new A330PanelSetTests.Def(new A330PanelSetTests.FakeIo(), A330ControlMap.Embedded, () => clock.Now);
    }

    private static List<string> A32nxSays(params double[] values)
    {
        var said = new A330PanelSetTests.Spoken();
        var fbw = new FlyByWireA320Definition();
        foreach (var v in values) fbw.ProcessSimVarUpdate("A32NX_SPOILERS_HANDLE_POSITION", v, said);
        return said.Lines;
    }

    private static List<string> A330Says(bool muted, params double[] values)
    {
        var def = NewDef();
        var said = new A330PanelSetTests.Spoken { Suppressed = muted };
        foreach (var v in values) def.ProcessSimVarUpdate("A330_SPDBRK", v, said);
        return said.Lines;
    }

    [Fact]
    public void The_first_reading_is_silent()
    {
        Assert.Empty(A330Says(false, 0.4));
        Assert.Empty(A32nxSays(0.4));
    }

    [Fact]
    public void A_band_change_is_said_once()
    {
        double[] run = { 0, 0, 0.02, 0.4, 0.42, 0.4, 0.5, 0.97, 1.0, 1.0, 0.0 };
        var said = A330Says(false, run);
        Assert.Equal(new[] { "Spoilers 40 percent", "Spoilers 50 percent", "Spoilers full", "Spoilers retracted" }, said);
        Assert.Equal(A32nxSays(run), said);   // the A32NX's own bands and words
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        // MainForm's mute wrap sets Suppressed around the delivery (INI_A330 is in DefAnnounceMuteSets)
        Assert.Empty(A330Says(true, 0, 0.4, 1.0, 0));
        // the row exists for Ctrl+M now, because it mutes something
        var def = NewDef();
        Assert.False(def.GetVariables()["A330_SPDBRK"].ExcludeFromMonitorManager);
        Assert.NotNull(def.GetVariables()["A330_SPDBRK"].RefreshControlWhenDefHandled);
    }

    [Fact]
    public void A_muted_band_still_moves_the_baseline_so_unmuting_says_only_the_next_change()
    {
        var def = NewDef();
        var said = new A330PanelSetTests.Spoken();
        def.ProcessSimVarUpdate("A330_SPDBRK", 0, said);
        said.Suppressed = true;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.4, said);
        said.Suppressed = false;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.4, said);
        Assert.Empty(said.Lines);
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.6, said);
        Assert.Equal(new[] { "Spoilers 60 percent" }, said.Lines);
    }

    private static (A330PanelSetTests.Def def, Clock clock, A330PanelSetTests.Spoken said, A330PanelSetTests.FakeIo io) Seeded()
    {
        var clock = new Clock();
        var io = new A330PanelSetTests.FakeIo();
        var def = new A330PanelSetTests.Def(io, A330ControlMap.Embedded, () => clock.Now);
        var said = new A330PanelSetTests.Spoken();
        def.ProcessSimVarUpdate("A330_SPDBRK", 0, said);   // the silent first reading
        return (def, clock, said, io);
    }

    private static bool Pick(A330PanelSetTests.Def def, double value, A330PanelSetTests.Spoken said) =>
        def.HandleUIVariableSet("A330_SPDBRK", value, def.GetVariables()["A330_SPDBRK"],
            new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero), said);

    [Fact]
    public void A_pilots_own_pick_is_not_followed_by_its_travel()
    {
        var (def, clock, said, io) = Seeded();
        Assert.True(Pick(def, 1.0, said));
        Assert.Single(io.Sent);
        // the lever sweeps through every band to full: the pick was already said by the screen reader (CORE-7)
        foreach (double v in new[] { 0.1, 0.25, 0.4, 0.55, 0.7, 0.85, 1.0 })
        {
            clock.Now += 300;
            def.ProcessSimVarUpdate("A330_SPDBRK", v, said);
        }
        Assert.Empty(said.Lines);
        // and it stays quiet at the target: the quiet ended there, the next change is a background one
        clock.Now += 300;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.0, said);
        Assert.Equal(new[] { "Spoilers retracted" }, said.Lines);
    }

    [Fact]
    public void A_band_change_after_the_pick_settled_is_said()
    {
        var (def, clock, said, _) = Seeded();
        Pick(def, 0.5, said);
        clock.Now += 500;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.5, said);   // landed: the quiet ends
        clock.Now += 500;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.8, said);
        Assert.Equal(new[] { "Spoilers 80 percent" }, said.Lines);
    }

    [Fact]
    public void A_band_change_after_the_cap_is_said()
    {
        var (def, clock, said, _) = Seeded();
        Pick(def, 1.0, said);
        clock.Now += 2L * IniA330Definition.ReadBackDelayMs - 1;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.3, said);   // still travelling, inside the cap: quiet
        Assert.Empty(said.Lines);
        clock.Now += 1;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.6, said);   // the cap passed without reaching the target
        Assert.Equal(new[] { "Spoilers 60 percent" }, said.Lines);
    }

    [Fact]
    public void A_newer_pick_rearms_the_quiet_for_its_own_target()
    {
        var (def, clock, said, _) = Seeded();
        Pick(def, 1.0, said);
        clock.Now += 2000;
        Pick(def, 0.0, said);
        clock.Now += 2000;   // past the first cap, inside the second
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.5, said);
        Assert.Empty(said.Lines);
    }

    [Fact]
    public void A_suppressed_write_arms_no_quiet()
    {
        var (def, clock, said, io) = Seeded();
        said.Suppressed = true;   // a First Officer write
        Assert.True(Pick(def, 1.0, said));
        Assert.Single(io.Sent);
        said.Suppressed = false;
        clock.Now += 300;
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.4, said);
        Assert.Equal(new[] { "Spoilers 40 percent" }, said.Lines);
    }

    [Fact]
    public void A_refused_pick_arms_no_quiet()
    {
        var (def, clock, said, io) = Seeded();
        io.CanCalc = false;
        Pick(def, 1.0, said);
        Assert.Empty(io.Sent);
        said.Lines.Clear();
        def.ProcessSimVarUpdate("A330_SPDBRK", 0.4, said);
        Assert.Equal(new[] { "Spoilers 40 percent" }, said.Lines);
    }

    [Fact]
    public void A_reset_makes_the_next_reading_the_silent_one_again()
    {
        var def = NewDef();
        var said = new A330PanelSetTests.Spoken();
        def.ProcessSimVarUpdate("A330_SPDBRK", 0, said);
        def.OnSimContextReset();
        def.ProcessSimVarUpdate("A330_SPDBRK", 1.0, said);
        Assert.Empty(said.Lines);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_speedbrake_words_do_not_depend_on_the_culture(string culture)
    {
        var said = TestCulture.Run(culture, () => A330Says(false, 0, 0.35, 0.75, 1.0, 0));
        Assert.Equal(A32nxSays(0, 0.35, 0.75, 1.0, 0), said);
        Assert.Equal(4, said.Count);
        Assert.DoesNotContain(said, l => l.Contains(',') || l.Contains('.'));
    }
}
