using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The A330's Ctrl+H, Ctrl+S, Ctrl+A and Ctrl+V windows hold what the Fenix's windows hold, in the Fenix's words.</summary>
public class A330FcuWindowTests
{
    private static readonly IniA330Definition Def = new();
    private static readonly SimConnectManager Sim = new(IntPtr.Zero);

    private static A330FcuWindowSpec Heading(bool trk = false, Func<bool>? now = null) => Def.HeadingWindowSpec(Sim, null!, trk, now);
    private static A330FcuWindowSpec Speed() => Def.SpeedWindowSpec(Sim, null!);
    private static A330FcuWindowSpec Altitude() => Def.AltitudeWindowSpec(Sim, null!);
    private static A330FcuWindowSpec Vs(bool fpa = false, Func<bool>? now = null) => Def.VsWindowSpec(Sim, null!, fpa, now);

    private static string[] Labels(A330FcuWindowSpec s) => s.Buttons.Select(b => b.Label).ToArray();

    [Fact]
    public void Heading_window_is_the_Fenix_heading_window()
    {
        var s = Heading();
        Assert.Equal("Set FCU heading", s.Title);
        Assert.Equal("Heading (0-360):", s.FieldLabel);
        Assert.Equal("Heading value", s.ValueName);
        Assert.Equal(new[] { "Heading Push", "Heading Pull", "HDG/VS TRK/FPA" }, Labels(s));
    }

    [Fact]
    public void Heading_window_opened_in_TRK_FPA_says_track()
    {
        var s = Heading(trk: true, now: () => true);
        Assert.Equal("Track (0-360):", s.FieldLabel);
        Assert.Equal("Track value", s.ValueName);
        Assert.Equal("Please enter a track value", s.Commit("").Error);
        Assert.Equal("Track must be between 0 and 360 degrees", s.Commit("361").Error);
    }

    [Fact]
    public void Speed_window_is_the_Fenix_speed_window_with_the_A330_range()
    {
        var s = Speed();
        Assert.Equal("Set FCU Speed", s.Title);
        Assert.Equal("Speed (100-399 / 0.10-0.89):", s.FieldLabel);
        Assert.Equal("Speed value", s.ValueName);
        Assert.Equal(new[] { "Speed Push", "Speed Pull", "SPD/MACH" }, Labels(s));
    }

    [Fact]
    public void Altitude_window_is_the_Fenix_altitude_window_plus_metric_altitude()
    {
        var s = Altitude();
        Assert.Equal("Set FCU altitude", s.Title);
        Assert.Equal("Altitude (100-49000):", s.FieldLabel);
        Assert.Equal("Altitude value", s.ValueName);
        Assert.Equal(new[] { "Altitude Push", "Altitude Pull", "Metric altitude" }, Labels(s));
    }

    [Fact]
    public void Vs_window_is_the_Fenix_vertical_speed_window()
    {
        var s = Vs();
        Assert.Equal("Set FCU vertical speed", s.Title);
        Assert.Equal("V/S (-6000 to 6000):", s.FieldLabel);
        Assert.Equal("Vertical speed value", s.ValueName);
        Assert.Equal(new[] { "V/S Push", "V/S Pull" }, Labels(s));
    }

    [Fact]
    public void Vs_window_opened_in_TRK_FPA_says_FPA()
    {
        var s = Vs(fpa: true, now: () => true);
        Assert.Equal("FPA (-9.9 to 9.9):", s.FieldLabel);
        Assert.Equal("FPA value", s.ValueName);
        Assert.Equal(new[] { "V/S Push", "V/S Pull" }, Labels(s));
        Assert.Equal("Please enter a flight path angle value", s.Commit("  ").Error);
        Assert.Equal("Flight path angle must be between -9.9 and 9.9 degrees", s.Commit("10").Error);
    }

    [Fact]
    public void The_four_windows_hold_the_Fenix_buttons_and_only_the_one_metric_extra()
    {
        string dir = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "FenixA320");
        var fenix = new List<string>();
        foreach (var f in new[] { "FenixHeadingWindow.cs", "FenixSpeedWindow.cs", "FenixAltitudeWindow.cs", "FenixVSWindow.cs" })
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(dir, f)), @"new Button\s*\{\s*Text = ""([^""]*)"""))
                if (m.Groups[1].Value != "Close") fenix.Add(m.Groups[1].Value);
        Assert.NotEmpty(fenix);
        var ours = Labels(Heading()).Concat(Labels(Speed())).Concat(Labels(Altitude())).Concat(Labels(Vs())).ToList();
        // the A330's one extra: the metric altitude key, which the A320 does not have
        Assert.Equal("Metric altitude", ours.Single(l => !fenix.Contains(l)));
        ours.Remove("Metric altitude");
        Assert.Equal(fenix, ours);
    }

    [Fact]
    public void Each_label_is_named_as_in_the_Fenix_windows()
    {
        Assert.Equal("Heading Label", Heading().LabelName);
        Assert.Equal("Speed Label", Speed().LabelName);
        Assert.Equal("Altitude Label", Altitude().LabelName);
        Assert.Equal("Vertical Speed Label", Vs().LabelName);
        Assert.Equal("Track Label", Heading(true).LabelName);
        Assert.Equal("FPA Label", Vs(true).LabelName);
        // and the names the Fenix source gives them
        string dir = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "FenixA320");
        foreach (var (file, name) in new[] { ("FenixHeadingWindow.cs", "Heading Label"), ("FenixSpeedWindow.cs", "Speed Label"),
                     ("FenixAltitudeWindow.cs", "Altitude Label"), ("FenixVSWindow.cs", "Vertical Speed Label") })
            Assert.Contains($"AccessibleName = \"{name}\"", File.ReadAllText(Path.Combine(dir, file)));
    }

    [Fact]
    public void No_button_label_carries_an_access_key_or_a_state()
    {
        foreach (var s in new[] { Heading(), Heading(true), Speed(), Altitude(), Vs(), Vs(true) })
            foreach (var l in Labels(s))
            {
                Assert.DoesNotContain('&', l);
                Assert.DoesNotContain(':', l);
            }
    }

    [Fact]
    public void The_other_A330_extras_left_the_four_windows()
    {
        var all = new[] { Heading(), Speed(), Altitude(), Vs() }.SelectMany(Labels).ToList();
        foreach (var gone in new[] { "LOC", "ATHR", "APPR", "AP1", "AP2" }) Assert.DoesNotContain(gone, all);
        Assert.DoesNotContain(all, l => l.StartsWith("ALT") || l.StartsWith("Knob"));
        Assert.DoesNotContain("HDG/VS TRK/FPA", Labels(Vs()));
    }

    private static A330FcuWindowSpec ByName(string n) => n switch
    {
        "heading" => Heading(), "speed" => Speed(), "altitude" => Altitude(), _ => Vs(),
    };

    [Theory]
    [InlineData("heading", "", "Please enter a heading value")]
    [InlineData("heading", "abc", "Invalid number format")]
    [InlineData("heading", "361", "Heading must be between 0 and 360 degrees")]
    [InlineData("heading", "-1", "Heading must be between 0 and 360 degrees")]
    [InlineData("speed", "", "Please enter a speed value")]
    [InlineData("speed", "fast", "Invalid number format")]
    [InlineData("speed", "50", "Speed must be 100-399 knots or 0.10-0.89 Mach")]
    [InlineData("speed", "0.95", "Speed must be 100-399 knots or 0.10-0.89 Mach")]
    [InlineData("speed", "400", "Speed must be 100-399 knots or 0.10-0.89 Mach")]
    [InlineData("altitude", "", "Please enter an altitude value")]
    [InlineData("altitude", "x", "Invalid number format")]
    [InlineData("altitude", "99", "Altitude must be between 100 and 49000 feet")]
    [InlineData("altitude", "49001", "Altitude must be between 100 and 49000 feet")]
    [InlineData("vs", "", "Please enter a vertical speed value")]
    [InlineData("vs", "up", "Invalid number format")]
    [InlineData("vs", "6001", "Vertical speed must be between -6000 and 6000 feet per minute")]
    [InlineData("vs", "-6001", "Vertical speed must be between -6000 and 6000 feet per minute")]
    public void The_commit_speaks_the_Fenix_message(string window, string typed, string message)
        => Assert.Equal(message, ByName(window).Commit(typed).Error);

    [Theory]
    [InlineData("heading", "270")]
    [InlineData("heading", "270.5")]
    [InlineData("heading", "360")]
    [InlineData("speed", "250")]
    [InlineData("speed", "0.82")]
    [InlineData("speed", "0,82")]
    [InlineData("altitude", "35000")]
    [InlineData("vs", "-1500")]
    [InlineData("vs", "6000")]
    public void A_good_value_commits_on_every_culture(string window, string typed)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            TestCulture.Run(culture, () =>
            {
                var c = ByName(window).Commit(typed);
                Assert.Null(c.Error);
                Assert.NotNull(c.Set);
                return 0;
            });
    }

    [Fact]
    public void A_comma_FPA_commits_on_every_culture()
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            TestCulture.Run(culture, () =>
            {
                var c = Vs(true, () => true).Commit("-0,5");
                Assert.Null(c.Error);
                Assert.NotNull(c.Set);
                return 0;
            });
    }

    [Fact]
    public void A_fractional_heading_is_rounded_as_on_the_Fenix()
    {
        Assert.Equal(270, IniA330Definition.HeadingSet("270.4", () => false)!.Target);
        Assert.Equal(271, IniA330Definition.HeadingSet("270.6", () => false)!.Target);
        // half to even, as the Fenix's Math.Round
        Assert.Equal(270, IniA330Definition.HeadingSet("270.5", () => false)!.Target);
        Assert.Equal(272, IniA330Definition.HeadingSet("271.5", () => false)!.Target);
        Assert.Equal(0, IniA330Definition.HeadingSet("359.6", () => false)!.Target);
    }

    [Fact]
    public void Heading_commit_follows_the_mode_at_commit_time()
    {
        bool trk = false;
        var s = Heading(false, () => trk);
        Assert.Equal("Please enter a heading value", s.Commit("").Error);
        trk = true;   // the pilot pressed HDG/VS TRK/FPA after the window opened
        Assert.Equal("Please enter a track value", s.Commit("").Error);
        Assert.Equal("Track must be between 0 and 360 degrees", s.Commit("400").Error);
    }

    [Fact]
    public void Vs_commit_checks_the_mode_at_commit_time()
    {
        bool fpa = false;
        var s = Vs(false, () => fpa);
        Assert.Null(s.Commit("-1500").Error);
        fpa = true;
        Assert.Equal("Flight path angle must be between -9.9 and 9.9 degrees", s.Commit("-1500").Error);
        Assert.Null(s.Commit("-3").Error);
    }

    [Fact]
    public void The_dials_written_still_follow_the_mode_at_commit_time()
    {
        bool trk = false, fpa = false;
        Assert.Equal("INI_HEADING_DIAL", IniA330Definition.HeadingSet("270", () => trk)!.Lvar);
        trk = true;
        var set = IniA330Definition.HeadingSet("270", () => trk)!;
        Assert.Equal(("INI_TRK_DIAL", "A330_FCU_TRK", 270.0, true), (set.Lvar, set.CacheKey, set.Target, set.Alt));
        Assert.Equal(0, IniA330Definition.HeadingSet("360", () => trk)!.Target);
        var vs = IniA330Definition.VsSet("-1500", () => fpa)!;
        Assert.Equal(("INI_VVI_DIAL", -1500.0), (vs.Lvar, vs.Target));
        fpa = true;
        var angle = IniA330Definition.VsSet("-3", () => fpa)!;
        Assert.Equal(("INI_FPA_DIAL", "A330_FCU_FPA", -3.0), (angle.Lvar, angle.CacheKey, angle.Target));
        Assert.Null(IniA330Definition.VsSet("-1500", () => fpa));
    }

    [Fact]
    public void The_hotkey_guide_names_what_each_window_holds()
    {
        string guide = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "HotkeyGuides", "iniBuilds_A330_Hotkeys.txt"));
        Assert.DoesNotContain("knob steps (100", guide, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metric altitude, AP1", guide);
        Assert.DoesNotContain("and ATHR.", guide);
    }
}
