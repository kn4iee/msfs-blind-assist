using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Regression tests for the A330 FCU value dialogs and the Ctrl+P autopilot window.</summary>
public class A330FcuTests
{
    [Fact]
    public void Autopilot_window_rows_have_distinct_access_keys()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var keys = def.AutopilotRows(sim, null!)
            .Select(r => r.Label)
            .Where(l => l.Contains('&'))
            .Select(l => char.ToUpperInvariant(l[l.IndexOf('&') + 1]))
            .ToList();
        var dups = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dups.Count == 0, "shared access keys: " + string.Join(", ", dups));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Typed_numbers_read_the_same_on_every_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            // FPA: "-0,5" is half a degree, never -5.0
            Assert.True(IniA330Definition.TryParseVs("-0,5", true, out double fpa));
            Assert.Equal(-0.5, fpa);
            Assert.True(IniA330Definition.TryParseVs("-2.5", true, out fpa));
            Assert.Equal(-2.5, fpa);
            // Mach with a comma
            Assert.True(IniA330Definition.TryParseSpeed("0,82", out double mach));
            Assert.Equal(0.82, mach);
            Assert.True(IniA330Definition.TryParseSpeed("0.82", out mach));
            Assert.Equal(0.82, mach);
            // inHg with a comma is 29.92 inHg, not 2992
            Assert.True(IniA330Definition.TryParseBaro("29,92", out double hpa));
            Assert.Equal(29.92 * 33.8639, hpa, 6);
            Assert.True(IniA330Definition.TryParseBaro("1013", out hpa));
            Assert.Equal(1013, hpa);
            // whole numbers
            Assert.True(IniA330Definition.TryParseHeading("270", out int h));
            Assert.Equal(270, h);
            Assert.True(IniA330Definition.TryParseAltitude("35000", out int a));
            Assert.Equal(35000, a);
            return 0;
        });
    }

    [Fact]
    public void Standard_button_leaves_its_verdict_to_ToggleStandard()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var std = Assert.Single(def.BaroToggles(sim, null!));
        Assert.True(std.SuppressStateAnnounce?.Invoke() == true);
    }

    [Fact]
    public void Knob_buttons_with_a_constant_state_do_not_read_back_but_stateful_ones_do()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var all = def.HeadingToggles(sim, null!).Concat(def.SpeedToggles(sim, null!))
            .Concat(def.AltitudeToggles(sim, null!)).Concat(def.VsToggles(sim, null!)).ToList();
        string Plain(ToggleButtonDef t) => t.Label.Replace("&", "");
        var silent = all.Where(t => t.SuppressStateAnnounce?.Invoke() == true).Select(Plain).OrderBy(s => s).ToList();
        var expectedSilent = new[]
        {
            "Heading pull (selected)", "Heading push (NAV)",
            "Altitude pull (open climb or descent)", "Altitude push (managed)", "ALT (hold the current altitude)",
            "V/S pull (engage)", "V/S push (level off)",
        }.OrderBy(s => s).ToList();
        Assert.Equal(expectedSilent, silent);
        // every other button keeps its read-back, so a button with a real state is never silenced
        Assert.Equal(all.Count - expectedSilent.Count, all.Count(t => t.SuppressStateAnnounce is null));
    }

    [Fact]
    public void Vs_dialog_checks_and_writes_for_the_mode_at_commit_time()
    {
        bool fpa = false;   // the dialog opened in HDG-V/S
        Func<bool> isFpa = () => fpa;
        Assert.True(IniA330Definition.VsValidator("-1500", isFpa).isValid);
        var vs = IniA330Definition.VsSet("-1500", isFpa);
        Assert.Equal(("INI_VVI_DIAL", "A330_FCU_VS", -1500.0), (vs!.Lvar, vs.CacheKey, vs.Target));

        fpa = true;         // the pilot pressed TRK/FPA in the dialog
        Assert.False(IniA330Definition.VsValidator("-1500", isFpa).isValid);
        Assert.True(IniA330Definition.VsValidator("-3", isFpa).isValid);
        var angle = IniA330Definition.VsSet("-3", isFpa);
        Assert.Equal(("INI_FPA_DIAL", "A330_FCU_FPA", -3.0), (angle!.Lvar, angle.CacheKey, angle.Target));
        Assert.Null(IniA330Definition.VsSet("-1500", isFpa));
    }

    [Fact]
    public void Heading_dialog_writes_the_dial_of_the_mode_at_commit_time()
    {
        bool trk = false;
        Func<bool> isTrk = () => trk;
        Assert.Equal("INI_HEADING_DIAL", IniA330Definition.HeadingSet("270", isTrk)!.Lvar);
        trk = true;
        var set = IniA330Definition.HeadingSet("270", isTrk)!;
        Assert.Equal(("INI_TRK_DIAL", "A330_FCU_TRK", 270.0, true), (set.Lvar, set.CacheKey, set.Target, set.Alt));
        Assert.Equal(0, IniA330Definition.HeadingSet("360", isTrk)!.Target);
    }

    [Fact]
    public void Ambiguous_or_out_of_range_input_is_refused()
    {
        Assert.False(IniA330Definition.TryParseVs("-10", true, out _));
        Assert.False(IniA330Definition.TryParseVs("6,001", false, out _));   // a thousands group, refused
        Assert.False(IniA330Definition.TryParseSpeed("NaN", out _));
        Assert.False(IniA330Definition.TryParseHeading("abc", out _));
        Assert.False(IniA330Definition.TryParseHeading("361", out _));
        Assert.False(IniA330Definition.TryParseAltitude("99", out _));
    }
}

/// <summary>The A330's FCU words read the same on a comma-decimal PC.</summary>
public class A330FcuCultureTests
{
    public static TheoryData<string> Cultures() => new() { "sv-SE", "de-DE", "en-US" };

    private static (IniA330Definition def, SimConnectManager sim) Shown(params (string Key, double Value)[] cache)
    {
        var sim = Tests.FirstOfficer.SeededSimConnectCache.With(cache);
        var def = new IniA330Definition();
        def.GetVariables();
        def.OnDisplayPanelShown("test", sim);   // hands the definition its cache, as MainForm does
        return (def, sim);
    }

    private static string Display(IniA330Definition def, string key, double value)
    {
        Assert.True(def.TryGetDisplayOverride(key, value, out var text));
        return text;
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_altimeter_reads_hectopascals_and_inches_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            Assert.Equal("Altimeter 1013 hectopascals, 29.92 inches", IniA330Definition.DescribeAltimeter(1013.2, 0));
            Assert.Equal("Altimeter 1020 hectopascals, 30.12 inches", IniA330Definition.DescribeAltimeter(1020, 1));
            Assert.Equal("Altimeter standard", IniA330Definition.DescribeAltimeter(1013.2, 3));
            Assert.Equal("Altimeter not available", IniA330Definition.DescribeAltimeter(null, 0));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_speed_window_reads_mach_with_a_decimal_point_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            var (mach, _) = Shown(("A330_M_MACH", 1));
            Assert.Equal("Mach 0.82", Display(mach, "A330_FCU_SPD", 0.82));
            var (knots, _) = Shown(("A330_M_MACH", 0));
            Assert.Equal("250 knots", Display(knots, "A330_FCU_SPD", 250.4));
            var (managed, _) = Shown(("A330_M_SPD_MANAGED", 1));
            Assert.Equal("Managed", Display(managed, "A330_FCU_SPD", 250));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_vertical_window_reads_flight_path_angle_with_a_decimal_point_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            var (fpa, _) = Shown(("A330_M_TRKFPA", 1), ("A330_FCU_FPA", -2.5));
            Assert.Equal("flight path angle -2.5 degrees", Display(fpa, "A330_FCU_VS", 0));
            var (vs, _) = Shown(("A330_M_TRKFPA", 0));
            string text = Display(vs, "A330_FCU_VS", 1500);
            Assert.Equal("1500 feet per minute", text);
            // sv-SE writes its own minus sign (U+2212) for a whole negative number: a sign a screen reader
            // says as "minus", not a separator, so only the digits and the absence of a group mark are pinned
            Assert.Equal("-1500 feet per minute", Display(vs, "A330_FCU_VS", -1500).Replace('−', '-'));
            return 0;
        });
}
