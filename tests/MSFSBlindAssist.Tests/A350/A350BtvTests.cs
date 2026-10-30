using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The BTV rollout's countdown, in the A380's words and bands ("500 meters to exit", "300 meters runway remaining"): the A350
/// publishes no variable for it, and its BTV object keeps the distance to the exit in memory (BTVUpdater, 1.2.7: the runway's
/// far end at +8 and +16, its length at +32, the exit's distance from the threshold at +92, what is left to the exit at +96,
/// short of a 60 m margin at +108; live 2026-10-30, on the stand after a landing: +8 and +16 -1, +32 0, +92 0, and +96 the 6.7
/// million metres to the far end -1,-1 shows it is in metres).
/// </summary>
public class A350BtvTests
{
    private static readonly A350BtvFacts Facts = new(2000, 8, 16, 32, 92, 96, 108, 4000);

    private static A350MemoryCopy Copy(double endLat = 25.24, double endLon = 55.38, double length = 3900, float exit = 2087, float remaining = 500, float margin = 60, double level = 1)
    {
        var mem = new byte[8192];
        BitConverter.GetBytes(endLat).CopyTo(mem, 2008);
        BitConverter.GetBytes(endLon).CopyTo(mem, 2016);
        BitConverter.GetBytes(length).CopyTo(mem, 2032);
        BitConverter.GetBytes(exit).CopyTo(mem, 2092);
        BitConverter.GetBytes(remaining).CopyTo(mem, 2096);
        BitConverter.GetBytes(margin).CopyTo(mem, 2108);
        BitConverter.GetBytes(level).CopyTo(mem, 4000);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);
        return copy;
    }

    [Fact]
    public void The_rollout_is_read_from_the_btv_object_in_a_copy()
    {
        var read = A350BtvRollout.Read(Copy(), Facts);
        Assert.NotNull(read);
        Assert.True(read!.Value.Active);
        Assert.Equal(560.0, read.Value.ToExit);                  // what is left, with the margin it stops short of
        Assert.Equal(3900.0 - 2087 + 560, read.Value.RunwayRemaining);
    }

    [Fact]
    public void A_read_outside_the_copy_is_no_read() =>
        Assert.Null(A350BtvRollout.Read(Copy(), Facts with { Updater = 8100 }));

    [Theory]
    [InlineData(double.NaN, 55.38, 3900.0, 2087f, 500f, 60f)]
    [InlineData(25.24, double.PositiveInfinity, 3900.0, 2087f, 500f, 60f)]
    [InlineData(25.24, 55.38, double.NaN, 2087f, 500f, 60f)]
    [InlineData(25.24, 55.38, 3900.0, float.NaN, 500f, 60f)]
    [InlineData(25.24, 55.38, 3900.0, 2087f, float.PositiveInfinity, 60f)]
    [InlineData(25.24, 55.38, 3900.0, 2087f, 500f, float.NaN)]
    public void A_number_no_rollout_can_hold_is_no_read(double lat, double lon, double length, float exit, float remaining, float margin) =>
        Assert.Null(A350BtvRollout.Read(Copy(lat, lon, length, exit, remaining, margin), Facts));

    // BTVUpdater::IsBTVEnabled() is the far end set; AutobrakeSystem::GetLevel() 1 is BTV.
    [Theory]
    [InlineData(25.24, 55.38, 1, true)]
    [InlineData(-1, 55.38, 1, false)]
    [InlineData(25.24, -1, 1, false)]
    [InlineData(25.24, 55.38, 0, false)]
    [InlineData(25.24, 55.38, 2, false)]
    public void It_is_active_only_with_a_runway_and_the_autobrake_on_btv(double lat, double lon, double level, bool active) =>
        Assert.Equal(active, A350BtvRollout.Read(Copy(lat, lon, level: level), Facts)!.Value.Active);

    // ResetExit leaves +92 at 0: no exit, and what +96 holds then is no distance to one; the runway still has its far end.
    [Fact]
    public void With_no_exit_only_the_runway_remains()
    {
        var read = A350BtvRollout.Read(Copy(exit: 0, remaining: -1560), Facts)!.Value;
        Assert.Null(read.ToExit);
        Assert.Equal(2400.0, read.RunwayRemaining);
    }

    [Fact]
    public void With_no_runway_length_nothing_remains() =>
        Assert.Null(A350BtvRollout.Read(Copy(length: 0), Facts)!.Value.RunwayRemaining);

    private static string[] Said(IEnumerable<(string Key, string Text)> said) => said.Select(s => s.Text).ToArray();

    [Fact]
    public void Each_band_is_said_once_in_the_a380s_words()
    {
        var c = new A350BtvCountdown();
        Assert.Empty(c.Observe(true, 1500, 2000));
        Assert.Equal(new[] { "1200 meters to exit" }, Said(c.Observe(true, 1198, 2000)));
        Assert.Empty(c.Observe(true, 1150, 2000));
        Assert.Equal(new[] { "800 meters to exit", "900 meters runway remaining" }, Said(c.Observe(true, 797, 897)));
        Assert.Equal(new[] { "500 meters to exit" }, Said(c.Observe(true, 496, 750)));
        Assert.Equal(new[] { "300 meters to exit" }, Said(c.Observe(true, 301 - 2, 750)));
        Assert.Equal(new[] { "150 meters to exit", "600 meters runway remaining" }, Said(c.Observe(true, 149, 598)));
        Assert.Equal(new[] { "300 meters runway remaining" }, Said(c.Observe(true, 40, 299)));
        Assert.Equal(new[] { "150 meters runway remaining" }, Said(c.Observe(true, 5, 145)));
    }

    // Starting already below the top bands (a short exit), the bands skipped never fire later: said once, at the distance.
    [Fact]
    public void A_rollout_starting_below_the_top_bands_says_where_it_is_once()
    {
        var c = new A350BtvCountdown();
        Assert.Equal(new[] { "650 meters to exit" }, Said(c.Observe(true, 652, 5000)));
        Assert.Empty(c.Observe(true, 640, 5000));
        Assert.Equal(new[] { "500 meters to exit" }, Said(c.Observe(true, 497, 5000)));
    }

    [Fact]
    public void Out_of_range_or_past_it_says_nothing()
    {
        var c = new A350BtvCountdown();
        Assert.Empty(c.Observe(true, 6721854, 6721854));
        Assert.Empty(c.Observe(true, -3, -3));
        Assert.Empty(c.Observe(true, 0, 0));
        Assert.Empty(c.Observe(true, null, null));
        Assert.Empty(c.Observe(true, double.NaN, double.NaN));
    }

    [Fact]
    public void A_new_rollout_counts_down_again_and_none_is_said_while_not_rolling()
    {
        var c = new A350BtvCountdown();
        Assert.Empty(c.Observe(false, 498, 498));
        Assert.NotEmpty(c.Observe(true, 498, 498));
        Assert.Empty(c.Observe(true, 497, 497));
        Assert.Empty(c.Observe(false, 497, 497));
        Assert.NotEmpty(c.Observe(true, 498, 498));
    }

    [Fact]
    public void Each_line_names_its_own_row()
    {
        var said = new A350BtvCountdown().Observe(true, 499, 299);
        Assert.Equal(new[] { A350BtvCountdown.ExitKey, A350BtvCountdown.RunwayKey }, said.Select(s => s.Key).ToArray());
    }

    [Fact]
    public void The_words_hold_in_a_comma_decimal_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(new[] { "1200 meters to exit" }, Said(new A350BtvCountdown().Observe(true, 1198, 5000)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void The_definition_says_a_read_on_the_ground_behind_each_row()
    {
        var speech = new SpeechCapture();
        var def = new IniBuildsA350Definition();
        def.ObserveBtv(A350BtvRollout.Read(Copy(remaining: 438), Facts)!.Value, speech);
        Assert.Equal(new[] { "500 meters to exit" }, speech.All);
    }

    [Fact]
    public void A_muted_row_says_nothing_and_the_other_still_speaks()
    {
        var speech = new SpeechCapture();
        var def = new IniBuildsA350Definition { IsMuted = key => key == A350BtvCountdown.ExitKey };
        def.ObserveBtv(A350BtvRollout.Read(Copy(length: 2480, remaining: 438), Facts)!.Value, speech);
        Assert.Equal(new[] { "890 meters runway remaining" }, speech.All);
    }

    [Fact]
    public void A_rollout_not_on_btv_says_nothing()
    {
        var speech = new SpeechCapture();
        new IniBuildsA350Definition().ObserveBtv(A350BtvRollout.Read(Copy(remaining: 438, level: 3), Facts)!.Value, speech);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Ctrl_m_lists_a_row_for_each_as_the_a380_names_them()
    {
        var rows = A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        Assert.Equal("BTV Distance to Exit", Assert.Single(rows, r => r.Key == A350BtvCountdown.ExitKey).Label);
        Assert.Equal("Runway Remaining", Assert.Single(rows, r => r.Key == A350BtvCountdown.RunwayKey).Label);
    }
}
