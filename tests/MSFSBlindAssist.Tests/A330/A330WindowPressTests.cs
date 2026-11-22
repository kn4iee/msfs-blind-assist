using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests;

/// <summary>A display window's press of a panel control is the panel's own write, and its label and state
/// are the panel's. Built on an in-memory map, so no test depends on the generated control map's labels.</summary>
public class A330WindowPressTests
{
    private const string SelectorKey = "TEST_SELECTOR";

    private static A330ControlMap Map() => new()
    {
        Controls =
        {
            new A330ControlMap.Control
            {
                Key = SelectorKey, Section = "Test section", Panel = "Test panel", Label = "Test selector", Kind = "select",
                Event = "AIRLINER_TEST_SELECTOR", Mode = "set", Read = "L:INI_TEST_SELECTOR",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Input = 1, Label = "Off" },
                    new A330ControlMap.Position { Value = 1, Input = 0, Label = "On" },
                },
            },
        },
    };

    private sealed class Recording : IniA330Definition
    {
        public Recording() : base(Map()) { }

        public List<(string Key, double Value, SimVarDefinition Def, bool Result)> Sets { get; } = new();

        public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
            SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        {
            // the real write needs the SimConnect SDK's native library, which the test run does not have: the
            // call is recorded and answered here, which is all the window press has to reach
            Sets.Add((varKey, value, varDef, true));
            return true;
        }
    }

    private sealed class QuietAnnouncer : ScreenReaderAnnouncer
    {
        public QuietAnnouncer() : base(IntPtr.Zero) { }
        public override void AnnounceImmediate(string message) { }
    }

    [Fact]
    public void A_window_press_takes_the_panels_write_path()
    {
        var def = new Recording();
        var sim = new SimConnectManager(IntPtr.Zero);
        var announcer = new QuietAnnouncer();
        var variable = def.GetVariables()[SelectorKey];
        Assert.Contains(SelectorKey, def.GetPanelControls()["Test panel"]);

        // what the panel does when its combo is set to "On" (read value 1)
        bool panel = def.HandleUIVariableSet(SelectorKey, 1, variable, sim, announcer);
        var fromPanel = Assert.Single(def.Sets);
        def.Sets.Clear();

        bool window = def.PressFromWindow(SelectorKey, 1, sim, announcer);

        var fromWindow = Assert.Single(def.Sets);
        Assert.Equal(fromPanel.Key, fromWindow.Key);
        Assert.Equal(fromPanel.Value, fromWindow.Value);
        Assert.Same(variable, fromWindow.Def);
        Assert.Equal(panel, window);
        Assert.Equal(fromWindow.Result, window);
    }

    [Fact]
    public void A_window_press_of_an_unknown_key_is_false()
    {
        var def = new Recording();
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.False(def.PressFromWindow("NO_SUCH_KEY", 1, sim, new QuietAnnouncer()));
        // a variable that is no panel control (a display-only key) is not pressable either
        Assert.False(def.PressFromWindow(A330SharedKeys.FcuAlt + "_NOT", 1, sim, new QuietAnnouncer()));
        Assert.Empty(def.Sets);
    }

    [Fact]
    public void Describe_says_the_label_and_the_cached_state()
    {
        var def = new Recording();
        Assert.Null(def.DescribeControl("NO_SUCH_KEY", new SimConnectManager(IntPtr.Zero)));

        // unread: no state yet
        Assert.Equal(("Test selector", (string?)null), def.DescribeControl(SelectorKey, new SimConnectManager(IntPtr.Zero)));

        // read: the position's own words, as the panel shows them
        var on = SeededSimConnectCache.With(("TEST_SELECTOR", 1));
        Assert.Equal(("Test selector", (string?)"On"), def.DescribeControl(SelectorKey, on));
        var off = SeededSimConnectCache.With(("TEST_SELECTOR", 0));
        Assert.Equal(("Test selector", (string?)"Off"), def.DescribeControl(SelectorKey, off));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Describe_says_a_value_with_no_position_as_an_invariant_number(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            var def = new Recording();
            var sim = SeededSimConnectCache.With(("TEST_SELECTOR", 2.5));
            Assert.Equal(("Test selector", (string?)"2.5"), def.DescribeControl(SelectorKey, sim));
            return 0;
        });
    }
}
