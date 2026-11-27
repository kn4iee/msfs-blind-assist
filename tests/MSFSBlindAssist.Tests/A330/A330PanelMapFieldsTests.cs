using MSFSBlindAssist.Aircraft.A330;

namespace MSFSBlindAssist.Tests;

/// <summary>The fields the generated map carries beyond the positions: which positions are proven, which
/// guard covers a switch, what a key's press changes, a slider's unit (plan 04). In-memory maps, so no
/// test depends on a label.</summary>
public class A330PanelMapFieldsTests
{
    [Fact]
    public void An_unproven_position_is_marked_in_the_map()
    {
        var map = A330ControlMap.Parse("""
            { "version": 1, "sections": [], "controls": [
              { "key": "K_A", "kind": "select", "event": "E", "positions": [
                  { "value": 0, "input": 0, "label": "a", "proven": true },
                  { "value": 1, "input": 1, "label": "b", "proven": false },
                  { "value": 2, "input": 2, "label": "c" } ] } ] }
            """);
        var ps = map.Controls.Single().Positions!;
        Assert.True(ps[0].Proven);
        Assert.False(ps[1].Proven);
        Assert.False(ps[2].Proven);

        var embedded = A330ControlMap.Embedded.Controls;
        Assert.All(embedded.Single(c => c.Key == "A330_WX_PWS").Positions!, p => Assert.True(p.Proven));
        Assert.All(embedded.Single(c => c.Key == "A330_STROBE").Positions!, p => Assert.False(p.Proven));
    }

    [Fact]
    public void A_guard_state_unit_and_proof_parse()
    {
        var map = A330ControlMap.Parse("""
            { "version": 1, "sections": [], "controls": [
              { "key": "K_SW", "kind": "select", "event": "E1", "guard": "K_SW_COVER" },
              { "key": "K_BTN", "kind": "button", "event": "E2", "state": "INI_X", "pressesToCycle": 3 },
              { "key": "K_TILT", "kind": "continuous", "event": "E3", "min": 0, "max": 30,
                "unit": { "name": "degrees", "min": -15, "max": 15 } },
              { "key": "K_PWS", "kind": "select", "event": "E4",
                "proof": { "kind": "ewd_memo", "text": "PRED W/S OFF", "when": "1", "measured": "2026-11-22" } },
              { "key": "K_PLAIN", "kind": "button", "event": "E5" } ] }
            """);
        var by = map.Controls.ToDictionary(c => c.Key);
        Assert.Equal("K_SW_COVER", by["K_SW"].Guard);
        Assert.Equal("INI_X", by["K_BTN"].State);
        Assert.Equal(3, by["K_BTN"].PressesToCycle);
        Assert.Equal("degrees", by["K_TILT"].Unit!.Name);
        Assert.Equal(-15, by["K_TILT"].Unit!.Min);
        Assert.Equal(15, by["K_TILT"].Unit!.Max);
        var proof = by["K_PWS"].Proof!;
        Assert.Equal("ewd_memo", proof.Kind);
        Assert.Equal("PRED W/S OFF", proof.Text);
        Assert.Equal("1", proof.When);
        Assert.Null(proof.Page);
        Assert.Null(by["K_PLAIN"].Guard);
        Assert.Null(by["K_PLAIN"].State);
        Assert.Null(by["K_PLAIN"].Unit);
        Assert.Null(by["K_PLAIN"].Proof);
        Assert.Null(by["K_PLAIN"].PressesToCycle);
    }
}
