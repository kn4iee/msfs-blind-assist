using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 11 Task 4: the read page as the shared EFB window's elements, and what each press does.</summary>
public class Bbs757EfbBridgeTests
{
    private static Dictionary<string, string> ItemsByText(Bbs757EfbElements.Built b)
    {
        var o = new Dictionary<string, string>();
        for (int i = 0; i < b.Count; i++) o[b.Data[$"items.{i}.text"]] = i.ToString();
        return o;
    }

    private static string Field(Bbs757EfbElements.Built b, string text, string field)
    {
        var map = ItemsByText(b);
        Assert.True(map.ContainsKey(text), "no element " + text + ": " + string.Join(" | ", map.Keys));
        return b.Data.TryGetValue($"items.{map[text]}.{field}", out var v) ? v : "";
    }

    private static Bbs757EfbElements.Action ActionFor(Bbs757EfbElements.Built b, string text) =>
        b.Actions[int.Parse(Field(b, text, "aidx"))];

    [Fact]
    public void Ground_services_become_buttons_whose_state_change_is_spoken()
    {
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services"), null);
        Assert.Equal("Aircraft, Ground services", b.Data["page"]);
        Assert.Equal("true", Field(b, "Fuel truck off", "clickable"));
        Assert.Equal("true", Field(b, "Fuel truck off", "announceChange"));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Fuel truck off").Kind);
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Home").Kind);
        Assert.Equal(Bbs757EfbElements.ActionKind.Classic, ActionFor(b, Bbs757EfbElements.ClassicWindowText).Kind);
        Assert.Equal("Ground services (current page)", ItemsByText(b).Keys.First(k => k.StartsWith("Ground services", StringComparison.Ordinal)));
    }

    [Fact]
    public void Settings_switches_are_check_boxes_and_known_sliders_are_choice_lists()
    {
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft"), null);
        Assert.Equal("checkbox", Field(b, "Nosewheel steering: Realistic", "type"));
        Assert.Equal("true", Field(b, "Nosewheel steering: Realistic", "value"));
        Assert.Equal("select", Field(b, "IRS alignment time", "type"));
        Assert.Equal("FULL (7 MIN+)", Field(b, "IRS alignment time", "value"));
        Assert.Contains("2 MIN", Field(b, "IRS alignment time", "options").Split((char)0x1f));
        // A switch's key does not carry its state, so the window keeps focus on it when it flips.
        Assert.Equal("tgl:NOSEWHEEL STEERING", Field(b, "Nosewheel steering: Realistic", "key"));
    }

    [Fact]
    public void Save_and_load_user_config_are_refused()
    {
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "10-aircraft-presets"), null);
        var a = ActionFor(b, "Save user config");
        Assert.Equal(Bbs757EfbElements.ActionKind.Refuse, a.Kind);
        Assert.Equal(Bbs757EfbElements.SaveLoadRefusal, a.Refusal);
        Assert.Equal("true", Field(b, "Load user config", "disabled"));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Before start").Kind);
    }

    [Fact]
    public void An_alert_is_the_second_element_and_assertive()
    {
        var b = Bbs757EfbElements.Build(null, "The tablet reader could not open.");
        Assert.Equal("alert", b.Data["items.1.kind"]);
        Assert.Equal("assertive", b.Data["items.1.live"]);
    }

    [Theory]
    [InlineData("COLD & DARK", "Cold and dark")]
    [InlineData("READY FOR TAKEOFF", "Ready for takeoff")]
    [InlineData("CRUISE", "Cruise")]
    [InlineData("PUSH ALL CBs", null)]
    public void Preset_words_map_to_the_gated_presets(string words, string? name) =>
        Assert.Equal(name, Bbs757EfbElements.PresetName(words));

    [Fact]
    public void Options_spread_evenly_along_the_track()
    {
        var s = new Bbs757EfbSlider(895, 1190, 1000);
        Assert.Equal(895, Bbs757EfbElements.OptionX(s, 0, 4));
        Assert.Equal(1190, Bbs757EfbElements.OptionX(s, 3, 4));
        // Measured: STANDARD (index 1 of 4) between taps at 970 and 1045.
        Assert.InRange(Bbs757EfbElements.OptionX(s, 1, 4), 970, 1045);
        // Measured: 10 SEC (index 4 of 16 options) at x 973.
        Assert.InRange(Bbs757EfbElements.OptionX(s, 4, 16), 965, 985);
    }

    [Fact]
    public void A_still_tablet_reads_as_unchanged()
    {
        using var a = new Bitmap(1397, 1048);
        using var c = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(c)) g.FillRectangle(Brushes.Red, 300, 300, 300, 200);
        var sa = Bbs757EfbBridge.Signature(a);
        Assert.False(Bbs757EfbBridge.Differs(sa, Bbs757EfbBridge.Signature(a)));
        Assert.True(Bbs757EfbBridge.Differs(sa, Bbs757EfbBridge.Signature(c)));
        // The status bar (its clock) is not part of the picture compared.
        using var d = new Bitmap(1397, 1048);
        using (var g = Graphics.FromImage(d)) g.FillRectangle(Brushes.White, 0, 0, 400, 30);
        Assert.False(Bbs757EfbBridge.Differs(sa, Bbs757EfbBridge.Signature(d)));
    }
}
