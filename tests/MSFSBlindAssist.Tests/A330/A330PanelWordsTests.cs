using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The panels' words and the speedbrake lever sound like the other Airbus aircraft's: each A330 word is
/// pinned against the FlyByWire A32NX's or the Fenix's own code. The embedded map is used by key only.</summary>
public class A330PanelWordsTests
{
    private sealed class Clock { public long Now = 100_000; }

    private static (A330PanelSetTests.Def def, Clock clock) NewDef(A330PanelSetTests.FakeIo? io = null)
    {
        var clock = new Clock();
        return (new A330PanelSetTests.Def(io ?? new A330PanelSetTests.FakeIo(), A330ControlMap.Embedded, () => clock.Now), clock);
    }

    private static Dictionary<double, string> FenixOffOn() =>
        (Dictionary<double, string>)typeof(FenixA320Definition)
            .GetField("OffOn", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    // ---------------- lamps and status rows ----------------

    [Fact]
    public void A_lamp_says_on_not_lit()
    {
        var (def, clock) = NewDef();
        var vars = def.GetVariables();
        // no lamp or status row of the panels says "Lit" any more
        Assert.DoesNotContain(vars.Values.SelectMany(v => v.ValueDescriptions.Values), w => w == "Lit");
        // the Fenix's own words for a light
        var fenix = FenixOffOn();
        foreach (var key in vars.Keys.Where(k => k.Contains("_LAMP", StringComparison.Ordinal)).Append("A330_MASTER_WARN_LT"))
        {
            Assert.Equal(fenix[0], vars[key].ValueDescriptions[0]);
            Assert.Equal(fenix[1], vars[key].ValueDescriptions[1]);
        }
        // and said: the generated status row of the master warning lamp, a change after the quiet start, as
        // "<the row's name>: <the Fenix's word>" (the name is the generator's, so it is taken from the row)
        var said = new A330PanelSetTests.Spoken();
        def.ProcessSimVarUpdate("A330_MASTER_WARN_LT", 0, said);
        clock.Now += IniA330Definition.StartGraceMs;
        def.ProcessSimVarUpdate("A330_MASTER_WARN_LT", 1, said);
        Assert.Equal(new[] { $"{vars["A330_MASTER_WARN_LT"].DisplayName}: {fenix[1]}" }, said.Lines);
    }

    [Fact]
    public void A_lamp_row_of_a_control_says_on_not_lit()
    {
        var (def, clock) = NewDef();
        var lamp = def.GetVariables().Single(kv => kv.Key == "A330_APU_START_LAMP1");
        Assert.Equal("On", lamp.Value.ValueDescriptions[lamp.Value.DescriptionKeyFor(1)]);
        Assert.Equal("Off", lamp.Value.ValueDescriptions[lamp.Value.DescriptionKeyFor(0)]);
    }

    // ---------------- parking brake and autobrake ----------------

    [Fact]
    public void The_parking_brake_says_on()
    {
        var (def, _) = NewDef();
        var row = def.GetVariables()["A330_PARKBRK"];
        // the A32NX's lever words (FlyByWireA320Definition.cs, A32NX_PARK_BRAKE_LEVER_POS) and the Fenix's
        var a32nx = new FlyByWireA320Definition().GetVariables()["A32NX_PARK_BRAKE_LEVER_POS"].ValueDescriptions;
        Assert.Equal(a32nx[0], row.ValueDescriptions[0]);
        Assert.Equal(a32nx[1], row.ValueDescriptions[1]);
        Assert.Equal(FenixOffOn()[1], row.ValueDescriptions[1]);
        Assert.Equal("Parking brake", row.DisplayName);   // the control's own name stays
    }

    [Fact]
    public void The_autobrake_says_the_cockpits_words()
    {
        var a32nx = new FlyByWireA320Definition().GetVariables()["A32NX_AUTOBRAKES_ARMED_MODE"].ValueDescriptions;
        var levels = IniA330Definition.AutobrakeLevels;
        Assert.Equal(a32nx[1], levels[1]);   // LO
        Assert.Equal(a32nx[2], levels[2]);   // MED
        Assert.Equal(a32nx[3], levels[3]);   // MAX
        Assert.Equal("Off", levels[0]);      // the A330 has no DIS word: the panel's rest word stays
        var (def, _) = NewDef();
        var row = def.GetVariables()[A330SharedKeys.Autobrake];
        Assert.Equal(new[] { "Off", "LO", "MED", "MAX" }, row.ValueDescriptions.OrderBy(k => k.Key).Select(k => k.Value));
        Assert.Equal("Autobrake", row.DisplayName);
    }

    [Fact]
    public void The_landing_gear_says_down_and_up()
    {
        var (def, _) = NewDef();
        var row = def.GetVariables()[A330SharedKeys.Gear];
        Assert.Equal("Up", row.ValueDescriptions[0]);
        Assert.Equal("Down", row.ValueDescriptions[1]);
    }

    // ---------------- master warning ----------------

    [Fact]
    public void Master_warning_reads_back_after_its_press()
    {
        using var ui = new A330PanelSetTests.UiQueue();
        // INI_MASTER_WARNING_ACTIVE / INI_MASTER_CAUTION_ACTIVE are unmeasured names: the keys read them back by
        // name, once, through the key mechanism
        foreach (var (key, row) in new[] { ("A330_MASTER_WARN", "A330_MASTER_WARN_LT"), ("A330_MASTER_CAUT", "A330_MASTER_CAUT_LT") })
        {
            var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
            var (def, _) = NewDef(io);
            var said = new A330PanelSetTests.Spoken();
            Assert.True(def.HandleUIVariableSet(key, 1, def.GetVariables()[key], new SimConnectManager(IntPtr.Zero), said));
            Assert.Empty(said.Lines);
            A330PanelSetTests.Release(io, ui);
            // the lamp row's own word for 1, bare (the A32NX's button read-back form); the word is the generator's
            Assert.Equal(new[] { def.GetVariables()[row].ValueDescriptions[1] }, said.Lines);
            Assert.Equal(new[] { row }, io.FreshKeys);
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_words_do_not_depend_on_the_culture(string culture)
    {
        var (said, words, name) = TestCulture.Run(culture, () =>
        {
            var (def, clock) = NewDef();
            var s = new A330PanelSetTests.Spoken();
            def.ProcessSimVarUpdate("A330_MASTER_WARN_LT", 0, s);
            clock.Now += IniA330Definition.StartGraceMs;
            def.ProcessSimVarUpdate("A330_MASTER_WARN_LT", 1, s);
            var v = def.GetVariables();
            return (s.Lines, string.Join("|", v["A330_PARKBRK"].ValueDescriptions.Values.Concat(v[A330SharedKeys.Autobrake].ValueDescriptions.Values)),
                v["A330_MASTER_WARN_LT"].DisplayName);
        });
        Assert.Equal("Off|On|Off|LO|MED|MAX", words);
        Assert.Equal(new[] { $"{name}: {FenixOffOn()[1]}" }, said);
    }
}
