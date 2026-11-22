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
    public void No_A330_FCU_window_label_carries_an_access_key()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var labels = def.AutopilotRows(sim, null!).Select(r => r.Label)
            .Concat(def.BaroToggles(sim, null!).Select(t => t.Label))
            .Where(l => l.Contains('&')).ToList();
        Assert.True(labels.Count == 0, "labels with an Alt key: " + string.Join(", ", labels));
    }

    [Fact]
    public void The_Ctrl_P_rows_start_with_the_Fenix_buttons_then_the_A330_extras()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.Equal(new[]
        {
            "AP1", "AP2", "ATHR", "APPR", "LOC",
            "Speed Push", "Speed Pull", "Heading Push", "Heading Pull", "Altitude Push", "Altitude Pull",
            "V/S Push", "V/S Pull", "ALT (hold this altitude)", "Read flight modes",
        }, def.AutopilotRows(sim, null!).Select(r => r.Label));
    }

    [Fact]
    public void The_four_value_windows_are_no_longer_ValueInputForm_dialogs()
    {
        string src = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "IniA330Definition.Fcu.cs"));
        // only the altimeter dialog (Ctrl+B) keeps the shared dialog
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(src, @"new ValueInputForm").Count);
    }

    [Theory]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleAutopilot1)]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleAutopilot2)]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleAutothrust)]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleLocalizer)]
    [InlineData(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleApproachMode)]
    public void The_autoflight_toggle_hotkeys_are_not_handled_as_on_the_Fenix(MSFSBlindAssist.Hotkeys.HotkeyAction action)
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.False(def.HandleHotkeyAction(action, sim, new SpeechCapture(), null!, null!));
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
