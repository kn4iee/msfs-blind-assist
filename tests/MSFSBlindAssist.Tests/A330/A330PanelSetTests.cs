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
    private const string PushToggleKey = "TEST_PUSH_TOGGLE";

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
            // a push-button the aircraft toggles (measured 2026-11-27): pressed by one set of its press value. Its
            // positions' inputs are the opposite of the press on purpose: they must drive no write
            new A330ControlMap.Control
            {
                Key = PushToggleKey, Section = "S", Panel = "P", Label = "Test push toggle", Kind = "select",
                Event = "AIRLINER_TEST_PUSH", Mode = "toggle", Read = "L:INI_TEST_PUSH", Press = 1,
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 1, Input = 0, Label = "On" },
                    new A330ControlMap.Position { Value = 0, Input = 1, Label = "Off" },
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
        public Def(FakeIo io, A330ControlMap? map = null, Func<long>? clock = null) : base(map ?? Map())
        {
            PanelIo = _ => io.Io;
            if (clock != null) NowMs = clock;   // the definition's own clock seam (Displays partial)
        }
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

    /// <summary>
    /// The UI thread's queue, in a test. While it is current (from its creation to its disposal), a pick's awaits capture
    /// it, as they capture the app's UI context (the definition awaits there, SIM-20); an opened gate hands their
    /// continuations to it, and <see cref="Run"/> runs them on the test's own thread, and those they queue, until none
    /// is left. So every read-back the gate released is over when <see cref="Release"/> returns, and no test waits on a
    /// clock.
    /// </summary>
    internal sealed class UiQueue : SynchronizationContext, IDisposable
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();
        private readonly SynchronizationContext? _previous;

        public UiQueue()
        {
            _previous = Current;
            SetSynchronizationContext(this);
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted) _posted.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;

        public void Run()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) next;
                lock (_posted)
                {
                    if (_posted.Count == 0) return;
                    next = _posted.Dequeue();
                }
                next.Callback(next.State);
            }
        }

        public void Dispose() => SetSynchronizationContext(_previous);
    }

    /// <summary>Opens the gate and runs every read-back it released to its end, on this thread.</summary>
    internal static void Release(FakeIo io, UiQueue ui)
    {
        io.Gate!.SetResult();
        ui.Run();
    }

    [Fact]
    public void The_panel_tests_never_wait_on_a_clock()
    {
        // a real-time wait flakes under load (one did); a gated read-back is run by the test's own UI queue instead
        var wait = new System.Text.RegularExpressions.Regex(@"\b(Task\.Delay|Thread\.Sleep)\s*\(");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "A330"), "A330Panel*Tests.cs");
        Assert.NotEmpty(files);
        var waits = files.SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => wait.IsMatch(x.line)).Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.True(waits.Count == 0, string.Join(", ", waits));
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
    public void A_pick_the_aircraft_ignored_resyncs_its_row_and_says_where_it_is()
    {
        using var ui = new UiQueue();
        // panel: read first (Off), write the position's INPUT (On = 0, [INI-1]), read back only after the settle time
        var io = new FakeIo { Gate = new TaskCompletionSource() };
        io.Cache[SelectorKey] = 0;
        var def = new Def(io);
        var said = new Spoken();
        Assert.True(Pick(def, SelectorKey, 1, said));
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0" }, io.Sent);
        Assert.Equal(new[] { IniA330Definition.ReadBackDelayMs }, io.Delays);
        Assert.Empty(io.Requests);
        Release(io, ui);
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
    public void A_newer_pick_wins_its_rows_read_back()
    {
        using var ui = new UiQueue();
        // arrowing through a selector commits one pick per step: the first pick's settle ends while the second is
        // still settling, and its resync would set the combo back to the first position under the pilot
        var io = new FakeIo { Gate = new TaskCompletionSource() };
        var def = new Def(io);
        var said = new Spoken();
        Pick(def, SelectorKey, 1, said);
        Pick(def, SelectorKey, 0, said);
        Assert.Equal(2, io.Sent.Count);
        Release(io, ui);
        Assert.Equal(new[] { SelectorKey }, io.Requests);   // one resync: the newest pick's
        Assert.Empty(said.Lines);

        // another row's pick does not cancel it
        io = new FakeIo { Gate = new TaskCompletionSource() };
        def = new Def(io);
        io.Cache[ToggleKey] = 0;
        Pick(def, SelectorKey, 1, said);
        Pick(def, ToggleKey, 1, said);
        Release(io, ui);
        Assert.Equal(new[] { SelectorKey, ToggleKey }, io.Requests.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_newer_window_press_wins_its_rows_read_back()
    {
        using var ui = new UiQueue();
        // the aircraft reads Off (the second press's position): the first press, which wanted On, must not say so
        var io = new FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        var def = new Def(io);
        var said = new Spoken();
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.True(def.PressFromWindow(SelectorKey, 1, sim, said));
        Assert.True(def.PressFromWindow(SelectorKey, 0, sim, said));
        Release(io, ui);
        Assert.Empty(said.Lines);
        Assert.Equal(new[] { SelectorKey }, io.Requests);
    }

    [Fact]
    public void A_pick_and_a_quick_pick_back_both_reach_the_aircraft()
    {
        using var ui = new UiQueue();
        // the cache is the continuous batch, refreshed once a second: arrowing Off to On and back within it, the second
        // pick still sees Off. While a pick of the row is being read back, the cache is not trusted
        var said = new Spoken();

        // a selector writes anyway (writing a position again is harmless)
        var io = new FakeIo { Gate = new TaskCompletionSource() };
        io.Cache[SelectorKey] = 0;
        var def = new Def(io);
        Pick(def, SelectorKey, 1, said);
        Pick(def, SelectorKey, 0, said);   // the cache still reads 0
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0", "set AIRLINER_TEST_SELECTOR 1" }, io.Sent);
        Release(io, ui);
        Assert.Equal(new[] { SelectorKey }, io.Requests);   // one resync, the newest pick's
        Pick(def, SelectorKey, 0, said);                    // read back: the cache is trusted again
        Assert.Equal(2, io.Sent.Count);

        // a toggle takes the pending pick as where it is: the pick back is a second press, the same pick again none
        io = new FakeIo { Gate = new TaskCompletionSource() };
        io.Cache[PushToggleKey] = 0;
        def = new Def(io);
        Pick(def, PushToggleKey, 1, said);
        Pick(def, PushToggleKey, 0, said);
        Assert.Equal(new[] { "set AIRLINER_TEST_PUSH 1", "set AIRLINER_TEST_PUSH 1" }, io.Sent);
        Pick(def, PushToggleKey, 0, said);
        Assert.Equal(2, io.Sent.Count);
        Release(io, ui);
        Assert.Equal(new[] { PushToggleKey }, io.Requests);

        // the parking brake is a by-value write too
        io = new FakeIo { Gate = new TaskCompletionSource() };
        io.Cache["A330_PARKBRK"] = 0;
        def = new Def(io);
        Pick(def, "A330_PARKBRK", 1, said);
        Pick(def, "A330_PARKBRK", 0, said);
        Assert.Equal(new[] { "set AIRLINER_PARKINGBRAKE 0", "set AIRLINER_PARKINGBRAKE 1" }, io.Sent);
        Release(io, ui);
        Assert.Equal(new[] { "A330_PARKBRK" }, io.Requests);
        Assert.Empty(said.Lines);
    }

    [Fact]
    public void A_pick_planned_as_already_there_takes_over_the_read_back_of_the_pick_before_it()
    {
        using var ui = new UiQueue();
        // a panel pick of On, then a window press of On before it is read back: the press finds the pending pick there
        // and sends nothing more. It is the newest pick, so the older read-back never resyncs over it, and it is read
        // back itself: the window, which has no combo, hears where the aircraft really is
        var io = new FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        io.Cache[PushToggleKey] = 0;
        var def = new Def(io);
        var said = new Spoken();
        Assert.True(Pick(def, PushToggleKey, 1, said));
        Assert.True(def.PressFromWindow(PushToggleKey, 1, new SimConnectManager(IntPtr.Zero), said));
        Assert.Equal(new[] { "set AIRLINER_TEST_PUSH 1" }, io.Sent);
        Release(io, ui);
        Assert.Equal(new[] { "Test push toggle: Off" }, said.Lines);   // once, the window press's
        Assert.Equal(new[] { PushToggleKey }, io.Requests);            // one resync

        // a refused pick sends nothing, so the earlier pick's read-back still runs and ends its pending state
        io = new FakeIo { Gate = new TaskCompletionSource() };
        io.Cache[SelectorKey] = 0;
        def = new Def(io);
        Pick(def, SelectorKey, 1, said);
        io.TrySetResult = false;
        Pick(def, SelectorKey, 0, said);
        Assert.Equal(new[] { SelectorKey }, io.Requests);              // the refusal's own resync, at once
        Release(io, ui);
        Assert.Equal(new[] { SelectorKey, SelectorKey }, io.Requests);  // and the sent pick's read-back
        io.TrySetResult = true;
        Pick(def, SelectorKey, 0, said);                                // read back: the cache (0) is trusted again
        Assert.Equal(new[] { "set AIRLINER_TEST_SELECTOR 0" }, io.Sent);
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

    [Fact]
    public void A_toggle_push_button_is_pressed_by_one_set_when_its_read_differs()
    {
        // every SetInputEvent to these events is a press, whatever the value (the model the 2026-11-27 sweep and its 23:05
        // trial fit: set 1, a hold and set 0 left the read where it was): a press is ONE set of the press value, whichever
        // way the switch goes, with no release after it; the positions' own inputs (On 0, Off 1) are never sent
        foreach (var (read, pick) in new[] { (0.0, 1.0), (1.0, 0.0) })
        {
            var io = new FakeIo { CanCalc = false };   // an input event needs no MobiFlight
            io.Cache[PushToggleKey] = read;
            var def = new Def(io);
            var said = new Spoken();
            Assert.True(Pick(def, PushToggleKey, pick, said));
            Assert.Equal(new[] { "set AIRLINER_TEST_PUSH 1" }, io.Sent);
            Assert.Equal(new[] { IniA330Definition.ReadBackDelayMs }, io.Delays);   // no hold: only the read-back waits
            Assert.Empty(said.Lines);
            Assert.Equal(new[] { PushToggleKey }, io.Requests);   // read back and resynced like any pick
        }

        // with the read unknown it is the toggle refusal: a press could switch it the wrong way
        var unread = new FakeIo();
        var unreadSaid = new Spoken();
        Assert.True(Pick(new Def(unread), PushToggleKey, 1, unreadSaid));
        Assert.Empty(unread.Sent);
        Assert.Equal(new[] { "Test push toggle unavailable: its position is not read yet." }, unreadSaid.Lines);

        // an event the aircraft does not list: refused aloud, the row resynced, nothing waited for
        var missing = new FakeIo { TrySetResult = false };
        missing.Cache[PushToggleKey] = 0;
        var missingSaid = new Spoken();
        Assert.True(Pick(new Def(missing), PushToggleKey, 1, missingSaid));
        Assert.Equal(new[] { "Test push toggle unavailable: this aircraft does not have that control." }, missingSaid.Lines);
        Assert.Equal(new[] { PushToggleKey }, missing.Requests);
        Assert.Empty(missing.Delays);

        // the planner: one input-event press of the map's press value
        var c = Map().Controls.Single(x => x.Key == PushToggleKey);
        Assert.True(A330Write.PressedByValue(c));
        Assert.Equal(new A330InputEventPressCall("AIRLINER_TEST_PUSH", 1), Assert.Single(A330Write.ToPosition(c, 0, 1).Calls));
        Assert.Equal(new A330InputEventPressCall("AIRLINER_TEST_PUSH", 1), Assert.Single(A330Write.ToPosition(c, 1, 0).Calls));

        // the First Officer's route for it: the input event, no calculator, refused while unread
        Assert.Equal<(bool, string?, bool, int)?>((false, "AIRLINER_TEST_PUSH", true, 0), new Def(new FakeIo()).FoWriteRoute(PushToggleKey));
    }

    [Fact]
    public void A_toggle_push_button_at_the_pick_is_not_pressed()
    {
        foreach (var (read, pick) in new[] { (1.0, 1.0), (0.0, 0.0), (0.6, 1.0), (0.4, 0.0) })
        {
            var io = new FakeIo();
            io.Cache[PushToggleKey] = read;
            var said = new Spoken();
            Assert.True(Pick(new Def(io), PushToggleKey, pick, said));
            Assert.Empty(io.Sent);
            Assert.Empty(io.Delays);
            Assert.Empty(io.Requests);
            Assert.Empty(said.Lines);
        }
    }
}
