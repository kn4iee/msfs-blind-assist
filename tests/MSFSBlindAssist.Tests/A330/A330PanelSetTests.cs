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
    private const string EngineKey = "TEST_ENGINE_MASTER";

    internal static A330ControlMap Map() => new()
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
            // takes its position by value: pressed even when the read is unknown
            new A330ControlMap.Control
            {
                Key = EngineKey, Section = "S", Panel = "P", Label = "Test engine master", Kind = "select",
                Event = "AIRLINER_TEST_ENG", Mode = "toggle", Read = "L:INI_TEST_ENG",
                Rpn = "{value} (>B:AIRLINER_TEST_ENG_Set)",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Input = 0, Label = "Off" },
                    new A330ControlMap.Position { Value = 1, Input = 1, Label = "On" },
                },
            },
        },
    };

    internal sealed class FakeIo
    {
        public bool CanCalc = true, TrySetResult = true;
        public Dictionary<string, double> Cache = new();
        public double? Fresh;
        public List<string> Sent = new();
        public List<string> Requests = new();
        public List<string> FreshKeys = new();
        public List<int> Delays = new();
        public TaskCompletionSource? Gate;

        public A330PanelIo Io => new()
        {
            CanCalc = () => CanCalc,
            TrySetInput = (e, v) => { if (TrySetResult) Sent.Add($"set {e} {v}"); return TrySetResult; },
            Press = (e, ms) => Sent.Add($"press {e} {ms}"),
            Calc = rpn => Sent.Add($"calc {rpn}"),
            Cached = k => Cache.TryGetValue(k, out var v) ? v : null,
            ReadFresh = (k, ct) => { FreshKeys.Add(k); return Task.FromResult(Fresh); },
            Request = k => Requests.Add(k),
            Delay = ms => { Delays.Add(ms); return Gate?.Task ?? Task.CompletedTask; },
        };
    }

    internal sealed class Def : IniA330Definition
    {
        public Def(FakeIo io, A330ControlMap? map = null) : base(map ?? Map()) { PanelIo = _ => io.Io; }
    }

    internal sealed class Spoken : ScreenReaderAnnouncer
    {
        public Spoken() : base(IntPtr.Zero) { }
        public List<string> Lines { get; } = new();
        public override void Announce(string message) => Lines.Add(message);
        public override void AnnounceImmediate(string message) => Lines.Add(message);
    }

    private static bool Pick(Def def, string key, double value, Spoken said)
    {
        var sim = new SimConnectManager(IntPtr.Zero);
        return def.HandleUIVariableSet(key, value, def.GetVariables()[key], sim, said);
    }

    /// <summary>Opens the gate and yields until the read-backs it released have run, whichever context they
    /// captured (xunit's own queues them behind the test), then a moment more so a wrong extra one would show.</summary>
    private static async Task Release(FakeIo io)
    {
        io.Gate!.SetResult();
        await Task.Delay(100);
    }

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
        io.Cache[ToggleKey] = 0;
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
        calcRefused.Cache[ToggleKey] = 0;
        Assert.False(new Def(calcRefused).PressFromWindow(ToggleKey, 1, new SimConnectManager(IntPtr.Zero), new Spoken()));

        // a write that was sent is true, and the flag does not stay set for the next press
        var ok = new FakeIo();
        var def = new Def(ok);
        Assert.True(def.PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), new Spoken()));
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0" }, ok.Sent);
    }

    [Fact]
    public async Task A_pick_the_aircraft_ignored_resyncs_its_row_and_says_where_it_is()
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
        await Release(io);
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
    public async Task A_newer_pick_wins_its_rows_read_back()
    {
        // arrowing through a selector commits one pick per step: the first pick's settle ends while the second is
        // still settling, and its resync would set the combo back to the first position under the pilot
        var io = new FakeIo { Gate = new TaskCompletionSource() };
        var def = new Def(io);
        var said = new Spoken();
        Pick(def, SelectorKey, 1, said);
        Pick(def, SelectorKey, 0, said);
        Assert.Equal(2, io.Sent.Count);
        await Release(io);
        Assert.Equal(new[] { SelectorKey }, io.Requests);   // one resync: the newest pick's
        Assert.Empty(said.Lines);

        // another row's pick does not cancel it
        io = new FakeIo { Gate = new TaskCompletionSource() };
        def = new Def(io);
        io.Cache[ToggleKey] = 0;
        Pick(def, SelectorKey, 1, said);
        Pick(def, ToggleKey, 1, said);
        await Release(io);
        Assert.Equal(new[] { SelectorKey, ToggleKey }, io.Requests.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_newer_window_press_wins_its_rows_read_back()
    {
        // the aircraft reads Off (the second press's position): the first press, which wanted On, must not say so
        var io = new FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        var def = new Def(io);
        var said = new Spoken();
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.True(def.PressFromWindow(SelectorKey, 1, sim, said));
        Assert.True(def.PressFromWindow(SelectorKey, 0, sim, said));
        await Release(io);
        Assert.Empty(said.Lines);
        Assert.Equal(new[] { SelectorKey }, io.Requests);
    }

    [Fact]
    public void The_settle_time_is_a_second_and_a_half()
    {
        Assert.Equal(1500, IniA330Definition.ReadBackDelayMs);
        var io = new FakeIo();
        Pick(new Def(io), SelectorKey, 1, new Spoken());
        Assert.Equal(new[] { 1500 }, io.Delays);
    }

    [Fact]
    public void A_refusal_does_not_outlive_its_press_and_a_window_flag_does_not_leak_into_the_panel()
    {
        var io = new FakeIo { TrySetResult = false };
        var def = new Def(io);
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.False(def.PressFromWindow(SelectorKey, 1, sim, new Spoken()));
        io.TrySetResult = true;
        Assert.True(def.PressFromWindow(SelectorKey, 1, sim, new Spoken()));   // the same definition, the next press

        // a panel pick after a window press must not be treated as a window's: nothing said
        io = new FakeIo { Fresh = 0 };
        def = new Def(io);
        Assert.True(def.PressFromWindow(SelectorKey, 0, sim, new Spoken()));
        var said = new Spoken();
        io.Fresh = 0;
        Assert.True(Pick(def, SelectorKey, 1, said));   // the aircraft will read 0, but this one is a panel's
        Assert.Empty(said.Lines);
    }

    [Fact]
    public void A_reading_within_its_tolerance_is_the_position()
    {
        var set = Map().Controls[0];
        Assert.Equal(A330WriteOutcome.AtPosition, A330Write.ToPosition(set, 0.995, 1).Outcome);
        Assert.Equal(A330WriteOutcome.Writes, A330Write.ToPosition(set, 0.98, 1).Outcome);
        var toggle = Map().Controls[1];
        Assert.Equal(A330WriteOutcome.AtPosition, A330Write.ToPosition(toggle, 0.5, 0).Outcome);
        Assert.Equal(A330WriteOutcome.Writes, A330Write.ToPosition(toggle, 0.6, 0).Outcome);

        // the window's read-back judges a landing by the same hundredth
        var io = new FakeIo { Fresh = 0.995 };
        var said = new Spoken();
        new Def(io).PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), said);
        Assert.Empty(said.Lines);
        io = new FakeIo { Fresh = 0.98 };
        said = new Spoken();
        new Def(io).PressFromWindow(SelectorKey, 1, new SimConnectManager(IntPtr.Zero), said);
        Assert.Single(said.Lines);
    }

    [Fact]
    public void A_control_with_nothing_to_send_is_refused()
    {
        var noEvent = new A330ControlMap.Control { Key = "X", Label = "X", Kind = "select", Mode = "set", Positions = new() { new() { Value = 1, Input = 1 } } };
        Assert.Equal(A330WriteOutcome.Refused, A330Write.ToPosition(noEvent, 0, 1).Outcome);
        var noToggle = new A330ControlMap.Control { Key = "X", Label = "X", Kind = "select", Mode = "toggle" };
        Assert.Equal(A330WriteOutcome.Refused, A330Write.ToPosition(noToggle, 0, 1).Outcome);
        var rpn = new A330ControlMap.Control { Key = "X", Label = "X", Kind = "select", Mode = "toggle", Rpn = "{value} (>L:INI_X)" };
        Assert.Equal(new A330CalcCall("1 (>L:INI_X)"), Assert.Single(A330Write.ToPosition(rpn, 0, 1).Calls));
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

    [Fact]
    public void A_toggle_with_no_read_is_not_pressed()
    {
        // a press flips whatever the control is: with the read unknown it could switch the wrong way ([FO-18]: unread is
        // unknown, never "differs"), so it refuses aloud and sends nothing
        foreach (double? unread in new double?[] { null, double.NaN })
        {
            var io = new FakeIo();
            if (unread is double nan) io.Cache[ToggleKey] = nan;
            var def = new Def(io);
            var said = new Spoken();
            Assert.True(Pick(def, ToggleKey, 1, said));   // handled: MainForm never falls back to its raw SetLVar
            Assert.Empty(io.Sent);
            Assert.Equal(new[] { "Test toggle unavailable: its position is not read yet." }, said.Lines);
            Assert.Equal(new[] { ToggleKey }, io.Requests);   // asks for the read again
            Assert.Empty(io.Delays);
            Assert.False(def.PressFromWindow(ToggleKey, 1, new SimConnectManager(IntPtr.Zero), new Spoken()));
        }

        // a read that differs still presses once
        var read = new FakeIo();
        read.Cache[ToggleKey] = 0;
        Pick(new Def(read), ToggleKey, 1, new Spoken());
        Assert.Equal(new[] { "press AIRLINER_TEST_TOGGLE 150" }, read.Sent);
    }

    [Fact]
    public void An_engine_master_with_no_read_is_still_set_by_value()
    {
        var io = new FakeIo();
        var said = new Spoken();
        Assert.True(Pick(new Def(io), EngineKey, 1, said));
        Assert.Equal(new[] { "calc 1 (>B:AIRLINER_TEST_ENG_Set)" }, io.Sent);
        Assert.Empty(said.Lines);

        io = new FakeIo();
        io.Cache[EngineKey] = double.NaN;
        Pick(new Def(io), EngineKey, 0, new Spoken());
        Assert.Equal(new[] { "calc 0 (>B:AIRLINER_TEST_ENG_Set)" }, io.Sent);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_toggle_refusal_and_an_engine_value_do_not_depend_on_the_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            var io = new FakeIo();
            var said = new Spoken();
            Pick(new Def(io), ToggleKey, 1, said);
            Assert.Equal(new[] { "Test toggle unavailable: its position is not read yet." }, said.Lines);
            io = new FakeIo();
            Pick(new Def(io), EngineKey, 1, new Spoken());
            Assert.Equal(new[] { "calc 1 (>B:AIRLINER_TEST_ENG_Set)" }, io.Sent);
            return 0;
        });
    }
}
