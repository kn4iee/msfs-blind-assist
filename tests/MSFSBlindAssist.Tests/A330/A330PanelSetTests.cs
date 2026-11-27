using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>What a panel row does with a pick: it reads the control first, writes only when it differs, reads
/// back after the settle time and resyncs its row. Built on in-memory rows, so no test depends on the generated
/// map's labels, and on a fake sim access, so nothing sleeps or touches SimConnect.</summary>
public class A330PanelSetTests
{
    private const string SelectorKey = "TEST_SELECTOR";
    private const string ToggleKey = "TEST_TOGGLE";

    private static A330ControlMap Map() => new()
    {
        Controls =
        {
            new A330ControlMap.Control
            {
                Key = SelectorKey, Section = "S", Panel = "P", Label = "Test selector", Kind = "select",
                Event = "AIRLINER_TEST_SELECTOR", Mode = "set", Read = "L:INI_TEST_SELECTOR",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Input = 1, Label = "Off" },
                    new A330ControlMap.Position { Value = 1, Input = 0, Label = "On" },
                },
            },
            new A330ControlMap.Control
            {
                Key = ToggleKey, Section = "S", Panel = "P", Label = "Test toggle", Kind = "select",
                Event = "AIRLINER_TEST_TOGGLE", Mode = "toggle", Read = "L:INI_TEST_TOGGLE",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Input = 0, Label = "Off" },
                    new A330ControlMap.Position { Value = 1, Input = 1, Label = "On" },
                },
            },
        },
    };

    private sealed class FakeIo
    {
        public bool CanCalc = true, TrySetResult = true;
        public Dictionary<string, double> Cache = new();
        public double? Fresh;
        public List<string> Sent = new();
        public List<string> Requests = new();
        public List<int> Delays = new();
        public TaskCompletionSource? Gate;

        public A330PanelIo Io => new()
        {
            CanCalc = () => CanCalc,
            TrySetInput = (e, v) => { if (TrySetResult) Sent.Add($"set {e} {v}"); return TrySetResult; },
            Press = (e, ms) => Sent.Add($"press {e} {ms}"),
            Calc = rpn => Sent.Add($"calc {rpn}"),
            Cached = k => Cache.TryGetValue(k, out var v) ? v : null,
            ReadFresh = (k, ct) => Task.FromResult(Fresh),
            Request = k => Requests.Add(k),
            Delay = ms => { Delays.Add(ms); return Gate?.Task ?? Task.CompletedTask; },
        };
    }

    private sealed class Def : IniA330Definition
    {
        public Def(FakeIo io) : base(Map()) { PanelIo = _ => io.Io; }
    }

    private sealed class Spoken : ScreenReaderAnnouncer
    {
        public Spoken() : base(IntPtr.Zero) { }
        public List<string> Lines { get; } = new();
        public override void Announce(string message) => Lines.Add(message);
        public override void AnnounceImmediate(string message) => Lines.Add(message);
    }

    private static bool Pick(Def def, string key, double value, Spoken said) =>
        def.HandleUIVariableSet(key, value, def.GetVariables()[key], new SimConnectManager(IntPtr.Zero), said);

    [Fact]
    public void A_refused_write_says_so_and_resyncs_its_row()
    {
        var io = new FakeIo { TrySetResult = false };
        var def = new Def(io);
        var said = new Spoken();

        // handled, so MainForm never falls back to its own raw SetLVar of the READ variable ([INI-1])
        Assert.True(Pick(def, SelectorKey, 1, said));
        Assert.Equal(new[] { "Test selector unavailable: this aircraft does not have that control." }, said.Lines);
        Assert.Equal(new[] { SelectorKey }, io.Requests);   // the row goes back to where the aircraft is, at once
        Assert.Empty(io.Delays);                             // nothing was sent, so nothing is read back

        io = new FakeIo { CanCalc = false };
        def = new Def(io);
        said = new Spoken();
        Assert.True(Pick(def, ToggleKey, 1, said));
        Assert.Equal(new[] { "Test toggle unavailable: the MobiFlight module is not responding." }, said.Lines);
        Assert.Equal(new[] { ToggleKey }, io.Requests);
        Assert.Empty(io.Sent);
    }

    [Fact]
    public void A_refused_press_from_a_window_returns_false()
    {
        var refused = new FakeIo { TrySetResult = false };
        Assert.False(new Def(refused).PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), new Spoken()));

        var calcRefused = new FakeIo { CanCalc = false };
        Assert.False(new Def(calcRefused).PressFromWindow(ToggleKey, 1, new SimConnectManager(IntPtr.Zero), new Spoken()));

        // a write that was sent is true, and the flag does not stay set for the next press
        var ok = new FakeIo();
        var def = new Def(ok);
        Assert.True(def.PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), new Spoken()));
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0" }, ok.Sent);
    }

    [Fact]
    public void A_pick_the_aircraft_ignored_resyncs_its_row_and_says_where_it_is()
    {
        // panel: read first (Off), write the position's INPUT (On = 0, [INI-1]), read back only after the settle time
        var io = new FakeIo { Gate = new TaskCompletionSource() };
        io.Cache[SelectorKey] = 0;
        var def = new Def(io);
        var said = new Spoken();
        Assert.True(Pick(def, SelectorKey, 1, said));
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0" }, io.Sent);
        Assert.Equal(new[] { IniA330Definition.ReadBackDelayMs }, io.Delays);
        Assert.Empty(io.Requests);
        io.Gate.SetResult();
        Assert.Equal(new[] { SelectorKey }, io.Requests);
        // the panel's combo follows the resync and the screen reader reads it: the definition adds no second voice
        Assert.Empty(said.Lines);

        // a window has no combo: its press says the real position once, as the generic announce words it
        io = new FakeIo { Fresh = 0 };
        io.Cache[SelectorKey] = 0;
        def = new Def(io);
        said = new Spoken();
        Assert.True(def.PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), said));
        Assert.Equal(new[] { "Test selector: Off" }, said.Lines);
        Assert.Equal(new[] { SelectorKey }, io.Requests);
    }

    [Fact]
    public void A_pick_that_landed_says_nothing()
    {
        var io = new FakeIo { Fresh = 1 };
        io.Cache[SelectorKey] = 0;
        var def = new Def(io);
        var said = new Spoken();
        Assert.True(def.PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), said));
        Assert.Empty(said.Lines);
        Assert.Equal(new[] { SelectorKey }, io.Requests);   // still resynced: a same-index set is silent

        // a pick the control already reads sends nothing and checks nothing
        io = new FakeIo();
        io.Cache[SelectorKey] = 1;
        def = new Def(io);
        said = new Spoken();
        Assert.True(Pick(def, SelectorKey, 1, said));
        Assert.Empty(io.Sent);
        Assert.Empty(io.Requests);
        Assert.Empty(io.Delays);
        Assert.Empty(said.Lines);

        // an unread control is written, as before
        io = new FakeIo();
        def = new Def(io);
        Pick(def, SelectorKey, 1, new Spoken());
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0" }, io.Sent);

        // a toggle presses by its own code only when the reading differs
        io = new FakeIo();
        io.Cache[ToggleKey] = 1;
        Pick(new Def(io), ToggleKey, 1, new Spoken());
        Assert.Empty(io.Sent);
        io.Cache[ToggleKey] = 0;
        Pick(new Def(io), ToggleKey, 1, new Spoken());
        Assert.Equal(new[] { "press AIRLINER_TEST_TOGGLE 150" }, io.Sent);
    }

    [Fact]
    public void The_real_position_is_heard_once_when_the_combo_follows_the_resync()
    {
        // MainForm sets the combo from the delivered value only when the definition does not consume it, and speaks
        // the row itself only on a change from its own baseline: neither fires for the unchanged real position, so
        // the screen reader's reading of the combo is the one voice (CORE-7)
        var io = new FakeIo();
        var def = new Def(io);
        var said = new Spoken();
        Assert.False(def.ProcessSimVarUpdate(SelectorKey, 0, said));
        Assert.Empty(said.Lines);
        var variable = def.GetVariables()[SelectorKey];
        Assert.Equal("Off", variable.ValueDescriptions[variable.DescriptionKeyFor(0)]);
        // the sentence a window says is the generic announce's: "<label>: <position>"
        Assert.Equal("Test selector: Off", A330Write.PositionSentence(variable, 0));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_position_with_no_name_is_said_as_an_invariant_number(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            var def = new Def(new FakeIo());
            Assert.Equal("Test selector: 2.5", A330Write.PositionSentence(def.GetVariables()[SelectorKey], 2.5));
            return 0;
        });
    }

    [Fact]
    public void The_planner_is_pure()
    {
        var c = Map().Controls[0];
        Assert.Equal(A330WriteOutcome.AtPosition, A330Write.ToPosition(c, 1, 1).Outcome);
        Assert.Equal(A330WriteOutcome.Writes, A330Write.ToPosition(c, null, 1).Outcome);
        Assert.Equal(A330WriteOutcome.Writes, A330Write.ToPosition(c, double.NaN, 1).Outcome);
        Assert.Equal(A330WriteOutcome.Refused, A330Write.ToPosition(c, 0, double.NaN).Outcome);
        Assert.Equal(A330WriteOutcome.AtPosition, A330Write.ParkingBrake(1, true).Outcome);
        var set = A330Write.ParkingBrake(0, true);
        Assert.Equal(new A330InputEventCall("AIRLINER_PARKINGBRAKE", 0), Assert.Single(set.Calls));
        Assert.Equal(new A330InputEventCall("AIRLINER_PARKINGBRAKE", 1), Assert.Single(A330Write.ParkingBrake(1, false).Calls));
    }

    [Fact]
    public void The_parking_brake_is_read_first_and_read_back()
    {
        var io = new FakeIo();
        io.Cache["A330_PARKBRK"] = 1;
        var def = new Def(io);
        var said = new Spoken();
        Assert.True(Pick(def, "A330_PARKBRK", 1, said));   // already set: nothing sent
        Assert.Empty(io.Sent);
        Assert.True(Pick(def, "A330_PARKBRK", 0, said));
        Assert.Equal(new[] { "set AIRLINER_PARKINGBRAKE 1" }, io.Sent);
        Assert.Equal(new[] { "A330_PARKBRK" }, io.Requests);
    }
}
