using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's announced lights and states (plan 06, <see cref="Bbs757Annunciators"/>): what each row
/// registers, which status display shows it, what a change says, and the EXT PWR button's read-back,
/// which says the light's state instead of "pressed".
/// </summary>
public class Bbs757AnnunciatorsTests
{
    private const string OnKey = Bbs757SharedKeys.ExtPwrOn;
    private const string AvailKey = Bbs757SharedKeys.ExtPwrAvail;

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; } = 100_000;
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig(Bbs757ControlMap? map = null)
            => Def = map == null
                ? new BBS757Definition { NestRunning = () => true, NowMs = () => Now }
                : new BBS757Definition(map) { NestRunning = () => true, NowMs = () => Now };

        public bool Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);
    }

    /// <summary>A map of one control on <paramref name="panel"/>, so no test depends on the generated map's names.</summary>
    private static Bbs757ControlMap MapWithPanel(string panel) => Bbs757ControlMap.Parse($$"""
        {
          "controls": [
            {
              "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "{{panel}}",
              "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON"
            }
          ]
        }
        """);

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- Task 1: EXT PWR ----------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, "EXT PWR on")]
    [InlineData(0.0, "EXT PWR off")]
    [InlineData(0.51, "EXT PWR on")]
    [InlineData(0.5, "EXT PWR off")]
    [InlineData(null, "EXT PWR unavailable")]
    [InlineData(double.NaN, "EXT PWR unavailable")]
    public void Ext_pwr_press_reads_back(double? value, string said)
    {
        // The press reads the ON light back instead of saying "EXT PWR pressed".
        Assert.Equal(OnKey, BBS757Definition.LinkedStatusKey(Bbs757Annunciators.ExtPwrButtonNode));
        Assert.Equal("B757_OVHD_EXT_PWR_BUTTON", Bbs757Annunciators.ExtPwrButtonNode);
        Assert.Equal(said, BBS757Definition.LinkedPhrase(OnKey, value));

        // The read-back rebaselines the row with this phrase, so the row's own words must be the same, or the
        // next delivery would announce the press a second time.
        if (value is double v && double.IsFinite(v))
            Assert.Equal(said, Bbs757Annunciators.ForKey(OnKey)!.Phrase(v, null));
    }

    [Fact]
    public void Ext_pwr_rows_registered_as_announced_status_rows()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();

        foreach (var (key, lvar, label) in new[]
        {
            (OnKey, "B757_OVHD_EXT_PWR_BUTTON_ON", "External Power On"),
            (AvailKey, "B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL", "External Power Available"),
        })
        {
            var row = vars[key];
            Assert.Equal(lvar, row.Name);
            Assert.Equal(label, row.DisplayName);
            Assert.Equal(SimVarType.LVar, row.Type);
            Assert.Equal("number", row.Units);
            Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
            Assert.True(row.IsAnnounced);
            Assert.True(row.RenderAsReadOnlyStatus);
            Assert.False(row.ExcludeFromMonitorManager);
            Assert.Contains(def.MonitorRows(), r => r.Key == key && r.Label == label);
            Assert.DoesNotContain(key, def.GetPanelControls().SelectMany(p => p.Value));
        }

        Assert.True(def.TryGetDisplayOverride(OnKey, 1, out string on));
        Assert.Equal("On", on);
        Assert.True(def.TryGetDisplayOverride(OnKey, 0, out string off));
        Assert.Equal("Off", off);
        Assert.True(def.TryGetDisplayOverride(AvailKey, 1, out string avail));
        Assert.Equal("Available", avail);
        Assert.True(def.TryGetDisplayOverride(AvailKey, 0, out string notAvail));
        Assert.Equal("Not available", notAvail);
        Assert.True(def.TryGetDisplayOverride(AvailKey, double.NaN, out string unread));
        Assert.Equal("unavailable", unread);
    }

    [Theory]
    [InlineData("Electrical", true)]
    [InlineData("Pedestal", false)]
    public void Ext_pwr_rows_on_electrical(string panel, bool joins)
    {
        var def = new Rig(MapWithPanel(panel)).Def;
        var displays = def.GetPanelDisplayVariables();

        if (joins)
        {
            var rows = displays[Bbs757Annunciators.ElectricalPanel];
            Assert.Equal(new[] { OnKey, AvailKey }, rows.Where(r => r is OnKey or AvailKey));
            Assert.Single(displays.SelectMany(p => p.Value), OnKey);
            Assert.Single(displays.SelectMany(p => p.Value), AvailKey);
            // Plan 07's ground power row follows: the button's lights, then the cart.
            Assert.True(rows.IndexOf(AvailKey) < rows.IndexOf(Bbs757SharedKeys.GpuConnected));
        }
        else
        {
            Assert.DoesNotContain(OnKey, displays.SelectMany(p => p.Value));
            Assert.DoesNotContain(AvailKey, displays.SelectMany(p => p.Value));
        }
    }

    [Theory]
    [InlineData(OnKey, 0.0, 1.0, "EXT PWR on")]
    [InlineData(OnKey, 1.0, 0.0, "EXT PWR off")]
    [InlineData(AvailKey, 0.0, 1.0, "External power available")]
    [InlineData(AvailKey, 1.0, 0.0, "External power not available")]
    public void Ext_pwr_change_announced_once(string key, double first, double then, string said)
    {
        var rig = new Rig();

        Assert.True(rig.Deliver(key, first));   // the cockpit as found: a silent baseline
        rig.Deliver(key, first);
        rig.Deliver(key, double.NaN);           // unreadable: neither spoken nor a baseline
        Assert.Empty(rig.Speech.All);

        Assert.True(rig.Deliver(key, then));
        rig.Deliver(key, then);
        Assert.Equal(new[] { said }, rig.Speech.All);

        rig.Def.OnSimContextReset();            // a flight load: the next reading is a baseline again
        rig.Deliver(key, first);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public void A_ground_power_window_run_is_heard_once_with_both_rows_present()
    {
        // Plan 07's handoff: the GPU tap changes the AVAIL light too (about 0.17 s after it); the window says
        // what came of its taps, so neither the Ground Power row nor the AVAIL row may say it again.
        var rig = new Rig();
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 0);
        rig.Deliver(AvailKey, 0);

        rig.Def.ArmGroundPowerEcho();
        rig.Now += 6_000;
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 1);
        rig.Deliver(AvailKey, 1);
        rig.Def.SettleGroundPowerEcho(1);
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 1);
        rig.Deliver(AvailKey, 1);
        Assert.Empty(rig.Speech.All);

        // A later change from elsewhere (the cart removed by BlueBird) speaks both rows.
        rig.Now += 60_000;
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 0);
        rig.Deliver(AvailKey, 0);
        Assert.Equal(new[] { "Ground power disconnected", "External power not available" }, rig.Speech.All);
    }

    [Fact]
    public void Every_row_speaks_the_same_under_a_comma_decimal_culture()
    {
        double[] values = { 0, 0.3, 0.5, 0.7, 1, 2, 5, 50, 90, 95, 100, 150, 180, 200, double.NaN };
        double?[] related = { null, 0, 1, 2 };

        List<string?> Phrases()
            => Bbs757Annunciators.Rows.SelectMany(r => values.SelectMany(v => related.Select(c => r.Phrase(v, c)))).ToList();

        List<string?> expected = new();
        InCulture("", () => expected = Phrases());
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(expected, Phrases()));
    }
}
