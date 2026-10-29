using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 11 Task 4: the read page as the shared EFB window's elements, and what each press does. (Plan 18, Task 7: every
/// control presses and the window refuses nothing; the new element tests are in <see cref="Bbs757EfbElementsTests"/>.)</summary>
public class Bbs757EfbBridgeTests
{
    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

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
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services"), null, NoExtras);
        Assert.Equal("Aircraft, Ground services", b.Data["page"]);
        Assert.Equal("true", Field(b, "Fuel truck off", "clickable"));
        Assert.Equal("true", Field(b, "Fuel truck off", "announceChange"));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Fuel truck off").Kind);
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, ActionFor(b, "Home").Kind);
        Assert.Equal("Ground services (current page)", ItemsByText(b).Keys.First(k => k.StartsWith("Ground services", StringComparison.Ordinal)));
    }

    [Fact]
    public void Settings_switches_are_check_boxes_and_known_sliders_are_choice_lists()
    {
        var b = Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft"), null, NoExtras);
        Assert.Equal("checkbox", Field(b, "Nosewheel steering: Realistic", "type"));
        Assert.Equal("true", Field(b, "Nosewheel steering: Realistic", "value"));
        Assert.Equal("select", Field(b, "IRS alignment time", "type"));
        // The choices are BlueBird's own words, in the order of its slider (the settings catalog's).
        Assert.Equal("Full (7 min+)", Field(b, "IRS alignment time", "value"));
        Assert.Contains("2 min", Field(b, "IRS alignment time", "options").Split((char)0x1f));
        // A switch's key does not carry its state, so the window keeps focus on it when it flips.
        Assert.Equal("tgl:NOSEWHEEL STEERING", Field(b, "Nosewheel steering: Realistic", "key"));
    }

    [Fact]
    public void An_alert_is_the_first_element_and_assertive()
    {
        // The page name is the window's own title line (the "page" key), never a list item.
        var b = Bbs757EfbElements.Build(null, "The tablet reader could not open.", NoExtras);
        Assert.Equal("BlueBird tablet", b.Data["page"]);
        Assert.Equal("alert", b.Data["items.0.kind"]);
        Assert.Equal("assertive", b.Data["items.0.live"]);
        Assert.Equal("alert", b.Data["items.0.key"]);
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
