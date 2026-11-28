using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>A continuous control with a unit is offered and spoken in it, in steps of its step, and its pick goes
/// through the same planner and read-back as a selector's. A control with no unit stays a 0..100 slider. In-memory
/// controls, so no test depends on the generated map's labels (the embedded map is read for a key and its unit only).</summary>
public class A330PanelSliderTests
{
    private const string TiltKey = "TEST_TILT";
    private const string TempKey = "TEST_TEMP";
    private const string PlainKey = "TEST_PLAIN";

    private static A330ControlMap Map() => new()
    {
        Controls =
        {
            // the weather radar tilt's scale: input 0..30 is -15..+15 degrees
            new A330ControlMap.Control
            {
                Key = TiltKey, Section = "S", Panel = "P", Label = "Test tilt", Kind = "continuous", Event = "AIRLINER_TEST_TILT",
                Mode = "set", Read = "B:AIRLINER_TEST_TILT", Min = 0, Max = 30, Step = 1,
                Unit = new A330ControlMap.UnitScale { Name = "degrees", Min = -15, Max = 15 },
            },
            // a temperature knob: input 0..300 is 18..30 degrees C, a step of 10 is 0.4 degrees
            new A330ControlMap.Control
            {
                Key = TempKey, Section = "S", Panel = "P", Label = "Test temperature", Kind = "continuous", Event = "AIRLINER_TEST_TEMP",
                Mode = "set", Read = "L:INI_TEST_TEMP", Min = 0, Max = 300, Step = 10,
                Unit = new A330ControlMap.UnitScale { Name = "degrees C", Min = 18, Max = 30 },
            },
            new A330ControlMap.Control
            {
                Key = PlainKey, Section = "S", Panel = "P", Label = "Test plain", Kind = "continuous", Event = "AIRLINER_TEST_PLAIN",
                Mode = "set", Read = "L:INI_TEST_PLAIN", Min = 0, Max = 100, Step = 5,
            },
        },
    };

    private static bool Pick(A330PanelSetTests.Def def, string key, double value, A330PanelSetTests.Spoken said) =>
        def.HandleUIVariableSet(key, value, def.GetVariables()[key], new SimConnectManager(IntPtr.Zero), said);

    [Fact]
    public void Tilt_is_said_in_degrees()
    {
        var control = A330ControlMap.Embedded.Controls.Single(c => c.Key == "A330_WX_TILT");
        Assert.Equal("degrees", control.Unit!.Name);

        var tilt = new IniA330Definition().GetVariables()["A330_WX_TILT"];
        // offered as its steps, not as the 0..100 position of a slider
        Assert.False(tilt.RenderAsSlider);
        Assert.Equal(31, tilt.ValueDescriptions.Count);
        Assert.Equal("-15 degrees", tilt.ValueDescriptions[0]);
        Assert.Equal("0 degrees", tilt.ValueDescriptions[15]);
        Assert.Equal("+3 degrees", tilt.ValueDescriptions[18]);
        Assert.Equal("+15 degrees", tilt.ValueDescriptions[30]);
    }

    [Fact]
    public void A_slider_moves_by_its_step()
    {
        var io = new A330PanelSetTests.FakeIo();
        var def = new A330PanelSetTests.Def(io, Map());
        var vars = def.GetVariables();

        var tilt = vars[TiltKey];
        Assert.False(tilt.RenderAsSlider);
        Assert.Equal(Enumerable.Range(0, 31).Select(i => (double)i), tilt.ValueDescriptions.Keys.OrderBy(k => k));
        // a reading between two steps is the nearest one
        Assert.Equal(7, tilt.DescriptionKeyFor(7.2));
        Assert.Equal(Enumerable.Range(0, 31).Select(i => i * 10.0), vars[TempKey].ValueDescriptions.Keys.OrderBy(k => k));

        // a control with no unit stays a 0..100 slider
        Assert.True(vars[PlainKey].RenderAsSlider);

        var said = new A330PanelSetTests.Spoken();
        io.Cache[TiltKey] = 15;
        Assert.True(Pick(def, TiltKey, 18, said));
        Assert.Equal(new[] { "set AIRLINER_TEST_TILT 18" }, io.Sent);

        // already within half a step of the pick: nothing is sent
        io.Sent.Clear();
        io.Cache[TiltKey] = 17.8;
        Assert.True(Pick(def, TiltKey, 18, said));
        Assert.Empty(io.Sent);
        Assert.Empty(said.Lines);
    }

    [Fact]
    public void A_step_that_did_not_land_is_said_in_the_unit()
    {
        using var ui = new A330PanelSetTests.UiQueue();
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 15 };
        var def = new A330PanelSetTests.Def(io, Map());
        var said = new A330PanelSetTests.Spoken();
        io.Cache[TiltKey] = 15;

        // the aircraft ignored the pick (a window's press has no combo to follow the row, so the definition says it)
        Assert.True(def.PressFromWindow(TiltKey, 18, new SimConnectManager(IntPtr.Zero), said));
        A330PanelSetTests.Release(io, ui);
        Assert.Equal(new[] { "Test tilt: 0 degrees" }, said.Lines);
    }

    [Fact]
    public void A_step_that_landed_says_nothing()
    {
        using var ui = new A330PanelSetTests.UiQueue();
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 18 };
        var def = new A330PanelSetTests.Def(io, Map());
        var said = new A330PanelSetTests.Spoken();
        io.Cache[TiltKey] = 15;

        Assert.True(def.PressFromWindow(TiltKey, 18, new SimConnectManager(IntPtr.Zero), said));
        A330PanelSetTests.Release(io, ui);
        Assert.Equal(new[] { TiltKey }, io.Requests);   // the read-back ran to its end, and said nothing
        Assert.Empty(said.Lines);
    }

    [Fact]
    public void A_temperature_is_said_in_degrees_C_under_de_DE()
    {
        var c = Map().Controls.Single(x => x.Key == TempKey);

        // input 130 of 0..300 is 23.2 degrees: the decimal point is the invariant one, whatever the PC's culture (CORE-21)
        Assert.Equal("23.2 degrees C", TestCulture.Run("de-DE", () => A330Units.Say(c, 130)));
        Assert.Equal("23.2 degrees C", TestCulture.Run("sv-SE", () => A330Units.Say(c, 130)));
        Assert.Equal("23.2 degrees C", TestCulture.Run("de-DE", () => new A330PanelSetTests.Def(new A330PanelSetTests.FakeIo(), Map())
            .GetVariables()[TempKey].ValueDescriptions[130]));
        // no plus sign where the range has no negative side
        Assert.Equal("18 degrees C", A330Units.Say(c, 0));
        Assert.Equal("30 degrees C", A330Units.Say(c, 300));

        // the A32NX's zone temperature sentence form: "{t:0.#} degrees" (FlyByWireA320Definition, the zone
        // temperature set), with no sign, from a unit named plain "degrees"
        var plain = new A330ControlMap.Control
        {
            Key = "T", Kind = "continuous", Min = 0, Max = 300, Step = 25,
            Unit = new A330ControlMap.UnitScale { Name = "degrees", Min = 18, Max = 30 },
        };
        double t = 24;
        Assert.Equal(FormattableString.Invariant($"{t:0.#} degrees"), TestCulture.Run("de-DE", () => A330Units.Say(plain, 150)));
    }

    [Fact]
    public void A_range_with_a_negative_side_signs_its_positive_steps()
    {
        // the Fenix's tilt reads "-1°", "0°", "+1°": the same signs, in words
        var c = Map().Controls.Single(x => x.Key == TiltKey);
        Assert.Equal("-1 degrees", A330Units.Say(c, 14));
        Assert.Equal("0 degrees", A330Units.Say(c, 15));
        Assert.Equal("+1 degrees", A330Units.Say(c, 16));
    }
}
