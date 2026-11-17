using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The Altimeter window's entry run through the definition: one sentence for a refusal, every target read back, and a side
/// that left STD unasked said (the first flight, 2026-10-31: the ISIS's click took both sides off STD at LINKED INSTR 2).</summary>
public class A350BaroEntryRunTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly List<string> _sent = new();
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _fresh = new(StringComparer.Ordinal);
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly IniBuildsA350Definition _def;

    public A350BaroEntryRunTests()
    {
        _def = new IniBuildsA350Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            Delay = _ => Task.CompletedTask,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
            IsMuted = _ => false,
            IsisStdRead = () => false,
        };
        _def.Attach(_sim);
    }

    private string Key(string name) => _def.KeyForInput(name);

    private void Baros(double link, double mode1 = 0, double mode2 = 0, double unit1 = 1, double unit2 = 1, double mb = 1013.25)
    {
        void Both(string name, double value)
        {
            _cache[Key(name)] = value;
            _fresh[Key(name)] = value;
        }
        Both(A350BaroEntry.LinkedVar, link);
        Both("XMLVAR_Baro1_Mode", mode1);
        Both("XMLVAR_Baro2_Mode", mode2);
        Both("XMLVAR_Baro_Selector_HPA_1", unit1);
        Both("XMLVAR_Baro_Selector_HPA_2", unit2);
        foreach (var side in new[] { 1, 2, 3 })
            Both($"KOHLSMAN SETTING MB:{side}", mb);
        Both("INI_BARO_PRESET_CPT", 0);
        Both("INI_BARO_PRESET_CPT_SHOW", 0);
        Both("INI_BARO_PRESET_FO", 0);
        Both("INI_BARO_PRESET_FO_SHOW", 0);
    }

    private void Fresh(string name, double value) => _fresh[Key(name)] = value;

    private Task Enter(double typed) => _def.PressBaroEntry(typed, _sim, _speech);

    private static A350FcuWindow Window(string name) => A350FcuWindows.All.Single(w => w.Name == name);

    /// <summary>A background delivery of a window's input (cached first, as MainForm does), then its batch's end.</summary>
    private void Deliver(string input, double value)
    {
        _cache[Key(input)] = value;
        _def.ProcessSimVarUpdate(Key(input), value, _speech);
    }

    private void BatchAfter(int ms)
    {
        _now += ms;
        _def.OnContinuousBatchDelivered(0);
    }

    [Fact]
    public async Task A_refusal_is_said_once_and_nothing_is_sent()
    {
        Baros(link: 0, mode1: 3, mode2: 3);
        await Enter(30.5);
        Assert.Equal(new[] { "QNH must be between 900 and 1100 hectopascals." }, _speech.All);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Sent_nowhere_when_the_write_cannot_land()
    {
        Baros(link: 0);
        _def.CanLand = _ => false;
        await Enter(1033);
        Assert.Equal(new[] { "Altimeter unavailable" }, _speech.All);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Not_linked_each_target_runs_in_order_and_reads_back()
    {
        Baros(link: 0);
        foreach (var side in new[] { 1, 2, 3 })
            Fresh($"KOHLSMAN SETTING MB:{side}", 1033);
        await Enter(1033);
        Assert.Equal(new[]
        {
            "16512 1 r (>K:2:KOHLSMAN_SET)", "1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_LEFT_Set)",
            "16512 2 r (>K:2:KOHLSMAN_SET)", "1 (>B:AIRLINER_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_Set)",
            "16512 3 r (>K:2:KOHLSMAN_SET)", "1 (>B:AIRLINER_ISIS_KNOB_1_Set)",
        }, _sent);
        Assert.Equal(new[]
        {
            "Captain altimeter set 1033 hectopascals", "First officer altimeter set 1033 hectopascals", "ISIS altimeter set 1033 hectopascals",
        }, _speech.All);
    }

    [Fact]
    public async Task A_side_left_std_unasked_is_said_once()
    {
        Baros(link: 2, mode1: 3, mode2: 3);
        Fresh("INI_BARO_PRESET_CPT_SHOW", 1);
        Fresh("INI_BARO_PRESET_CPT", 1033);
        Fresh("XMLVAR_Baro1_Mode", 0);
        Fresh("KOHLSMAN SETTING MB:1", 1033.25);
        await Enter(1033);
        Assert.Equal(new[] { "Captain altimeter preset 1033 hectopascals", "Captain altimeter left STD, now 1033 hectopascals" }, _speech.All);
    }

    [Fact]
    public async Task A_side_left_std_in_inches_is_said_in_inches()
    {
        Baros(link: 2, mode1: 3, mode2: 3, unit1: 0, unit2: 0);
        Fresh("INI_BARO_PRESET_CPT_SHOW", 1);
        Fresh("INI_BARO_PRESET_CPT", 2992);
        Fresh("XMLVAR_Baro2_Mode", 0);
        Fresh("KOHLSMAN SETTING MB:2", 29.92 * 33.8639);
        await Enter(29.92);
        Assert.Equal("First officer altimeter left STD, now 29.92 inches", _speech.All[^1]);
    }

    [Fact]
    public async Task Sides_still_on_std_say_nothing_more()
    {
        Baros(link: 2, mode1: 3, mode2: 3);
        Fresh("INI_BARO_PRESET_CPT_SHOW", 1);
        Fresh("INI_BARO_PRESET_CPT", 1033);
        await Enter(1033);
        Assert.Equal(new[] { "Captain altimeter preset 1033 hectopascals" }, _speech.All);
    }

    [Fact]
    public async Task An_unread_mode_afterwards_is_no_claim_that_a_side_left_std()
    {
        Baros(link: 2, mode1: 3, mode2: 3);
        Fresh("INI_BARO_PRESET_CPT_SHOW", 1);
        Fresh("INI_BARO_PRESET_CPT", 1033);
        _fresh.Remove(Key("XMLVAR_Baro1_Mode"));
        await Enter(1033);
        Assert.Equal(new[] { "Captain altimeter preset 1033 hectopascals" }, _speech.All);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_linked_copy_is_not_said_twice(double link)
    {
        Baros(link);
        var fo = Window("First officer baro");
        Assert.NotNull(fo);
        BatchAfter(1000);   // the first values are a baseline
        foreach (var side in new[] { 1, 3 })
            Fresh($"KOHLSMAN SETTING MB:{side}", 1033);
        var entry = Enter(1033);
        Deliver("KOHLSMAN SETTING MB:2", 1033);   // the aircraft's copy, arriving as the entry runs
        BatchAfter(1000);
        await entry;
        BatchAfter(1000);
        Assert.Single(_speech.All, s => s.StartsWith("Captain altimeter set 1033", StringComparison.Ordinal));
        Assert.DoesNotContain(_speech.All, s => s.StartsWith("First officer altimeter", StringComparison.Ordinal));
    }

    // In flight 2026-11-02: the window's QNH button pressed while the entry still stepped the captain's preset was said back as
    // "Captain altimeter left STD": the pilot asked for that pull.
    [Theory]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL")]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL")]
    public async Task A_pull_asked_while_the_entry_runs_is_not_said_as_leaving_std(string pullRow)
    {
        Baros(link: 2, mode1: 3, mode2: 3);
        Fresh("INI_BARO_PRESET_CPT_SHOW", 1);
        Fresh("INI_BARO_PRESET_CPT", 1033);
        Fresh("XMLVAR_Baro1_Mode", 0);
        Fresh("XMLVAR_Baro2_Mode", 0);
        var gate = new TaskCompletionSource();
        _def.Delay = _ => gate.Task;
        var entry = Enter(1033);
        _def.HandleUIVariableSet(pullRow, 1, _def.GetVariables()[pullRow], _sim, _speech);
        _def.Delay = _ => Task.CompletedTask;
        gate.SetResult();
        await entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(_speech.All, s => s.Contains("left STD", StringComparison.Ordinal));
    }

    // In flight 2026-11-02: pulled while the captain's preset was still stepping, the knob's preset went out of view and the entry said
    // "Captain baro is on STD and its knob shows no preset; pull it for QNH first.", and the echo it had armed
    // kept the background call-out of the new QNH quiet: the pilot heard a wrong sentence and not the setting.
    [Fact]
    public async Task A_side_pulled_while_its_preset_steps_is_said_with_its_qnh()
    {
        Baros(link: 0, mode1: 3, mode2: 3);
        Fresh("INI_BARO_PRESET_CPT_SHOW", 0);   // out of view: pulled
        Fresh("XMLVAR_Baro1_Mode", 0);
        Fresh("KOHLSMAN SETTING MB:1", 1033.25);
        Fresh("INI_BARO_PRESET_FO_SHOW", 1);
        Fresh("INI_BARO_PRESET_FO", 1033);
        Fresh("KOHLSMAN SETTING MB:3", 1033);
        var gate = new TaskCompletionSource();
        _def.Delay = _ => gate.Task;
        var entry = Enter(1033);
        _def.HandleUIVariableSet("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", 1,
            _def.GetVariables()["A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL"], _sim, _speech);
        _def.Delay = _ => Task.CompletedTask;
        gate.SetResult();
        await entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(_speech.All, s => s.Contains("shows no preset", StringComparison.Ordinal));
        Assert.Equal(new[]
        {
            "First officer altimeter preset 1033 hectopascals", "ISIS altimeter set 1033 hectopascals", "Captain altimeter 1033 hectopascals",
        }, _speech.All.Where(s => !s.EndsWith("pressed", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_speed_turned_elsewhere_is_said_once_and_its_muted_row_says_nothing()
    {
        var speed = Window("Speed window");
        void Speed(double dial)
        {
            Deliver("INI_FCU_SPD_DASHED", 0);
            Deliver("INI_MANAGED_SPEED", 0);
            Deliver("INI_FCU_SPD_DOT", 0);
            Deliver("INI_PRESET_DIAL", 0);
            Deliver("INI_PRESET_DIAL_IS_MACH", 0);
            _cache[speed.Key] = dial;
            _def.ProcessSimVarUpdate(speed.Key, dial, _speech);
        }
        Speed(250);
        BatchAfter(1000);
        Assert.Empty(_speech.All);   // a baseline
        Speed(251);
        BatchAfter(1000);
        Assert.Equal(new[] { "Speed 251 knots" }, _speech.All);
        _def.IsMuted = key => key == speed.Key;
        Speed(252);
        BatchAfter(1000);
        Assert.Equal(new[] { "Speed 251 knots" }, _speech.All);
    }

    [Fact]
    public async Task An_isis_on_its_own_std_is_left_alone_and_said_last()
    {
        Baros(link: 0);
        _def.IsisStdRead = () => true;
        foreach (var side in new[] { 1, 2 })
            Fresh($"KOHLSMAN SETTING MB:{side}", 1033);
        await Enter(1033);
        Assert.DoesNotContain(_sent, rpn => rpn.Contains(" 3 r (>K:2:KOHLSMAN_SET)", StringComparison.Ordinal) || rpn.Contains("ISIS", StringComparison.Ordinal));
        Assert.Equal(A350BaroEntry.IsisOnStdNotice, _speech.All[^1]);
    }

    // The A350-1000's controls pass, 2026-11-17: the ISIS window's typed QNH with the ISIS on its own STD said "ISIS QNH did not
    // take 1019" after writes the ISIS ignores ([A350-14]); the Altimeter entry already said why and sent nothing.
    [Fact]
    public void A_typed_isis_qnh_on_the_isis_std_says_so_and_sends_nothing()
    {
        Baros(link: 0);
        _def.IsisStdRead = () => true;
        _def.HandleUIVariableSet(A350IsisWindow.QnhRow, 1019, _def.GetVariables()[A350IsisWindow.QnhRow], _sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A350BaroEntry.IsisOnStdNotice }, _speech.All);
    }

    // Review 2026-11-17: an ISIS whose STD is unknown (its display unread) is no refusal: the field writes, and its read-back says what
    // the ISIS then shows.
    [Fact]
    public void A_typed_isis_qnh_with_the_isis_std_unknown_is_sent()
    {
        Baros(link: 0);
        _def.IsisStdRead = () => null;
        _def.HandleUIVariableSet(A350IsisWindow.QnhRow, 1019, _def.GetVariables()[A350IsisWindow.QnhRow], _sim, _speech);
        Assert.NotEmpty(_sent);
        Assert.DoesNotContain(A350BaroEntry.IsisOnStdNotice, _speech.All);
    }

    // The ISIS's draws for the run tests below: on STD until `_isisShows` changes. With `ticksOnDelay` the waits the definition awaits move
    // the monitor's clock on and tick it every 500 ms of them, as the real monitor ticks (review 3: ticking on every wait hid the cadence).
    private DateTime _monitorNow = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private A350DrawCapture _isisShows = A350Fixtures.Capture("sfd_isis_std_stand.json");
    private int _sinceTick;

    private static A350DrawCapture OnStd => A350Fixtures.Capture("sfd_isis_std_stand.json");
    private static A350DrawCapture OffStd => A350Fixtures.Capture("ground_SFD.json");

    private async Task<A350PfdMonitor> IsisDrawn(bool ticksOnDelay)
    {
        var monitor = new A350PfdMonitor(A350PfdMonitorTests.Each((g, _) => g == "SFD" ? _isisShows : null), _ => false, _ => { }, () => _monitorNow);
        _def.Pfd = monitor;
        _def.IsisStdRead = null;   // the definition's own: the ISIS's draw
        for (int i = 0; i < 2; i++)
        {
            await monitor.TickAsync();
            _monitorNow += TimeSpan.FromMilliseconds(A350PfdMonitor.PeriodMs);
        }
        if (ticksOnDelay)
            _def.Delay = async ms =>
            {
                _monitorNow += TimeSpan.FromMilliseconds(ms);
                for (_sinceTick += ms; _sinceTick >= A350PfdMonitor.PeriodMs; _sinceTick -= A350PfdMonitor.PeriodMs)
                    await monitor.TickAsync();
            };
        return monitor;
    }

    private void TypeIsis(double qnh = 1019) =>
        _def.HandleUIVariableSet(A350IsisWindow.QnhRow, qnh, _def.GetVariables()[A350IsisWindow.QnhRow], _sim, _speech);

    private void Press(string row) => _def.HandleUIVariableSet(row, 1, _def.GetVariables()[row], _sim, _speech);

    private bool IsisWritten(double qnh) => _sent.Any(rpn => rpn.StartsWith(
        ((int)Math.Round((qnh - 1) * 16)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 3 r (>K:2:KOHLSMAN_SET)", StringComparison.Ordinal));

    // Review 2026-11-17: the ISIS's STD is read from its draw, kept up to half a minute; just after its STD/CHANGE key a draw from before
    // the press said "ISIS not set: it is on STD." though the pilot had just taken it off STD. The entry waits for a draw since the key
    // (review 2, minor 3: writing blind could read back unchanged) and goes by it.
    [Fact]
    public async Task After_the_isis_std_key_its_qnh_waits_for_a_draw_since()
    {
        Baros(link: 0);
        await IsisDrawn(ticksOnDelay: true);
        TypeIsis();
        Assert.Equal(new[] { A350BaroEntry.IsisOnStdNotice }, _speech.All);   // the draw shows STD
        Assert.Empty(_sent);

        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _isisShows = OffStd;   // from the next draw on
        _sent.Clear();
        TypeIsis();
        Assert.True(IsisWritten(1019));
        Assert.Single(_speech.All, A350BaroEntry.IsisOnStdNotice);
    }

    [Fact]
    public async Task After_the_isis_std_key_a_draw_since_still_on_std_refuses()
    {
        Baros(link: 0);
        _isisShows = OffStd;
        await IsisDrawn(ticksOnDelay: true);
        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _isisShows = OnStd;   // the key put it on STD
        _sent.Clear();
        TypeIsis();
        Assert.Empty(_sent);
        Assert.Equal(A350BaroEntry.IsisOnStdNotice, _speech.All[^1]);
    }

    // Review 2026-11-17 (2), important 1: at LINKED INSTR 2 a side's push puts the ISIS on STD and its pull takes it off ([A350-13]):
    // they move the ISIS's STD as its own key does. At 0 they leave it alone.
    [Theory]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", false)]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL", false)]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH", true)]
    [InlineData("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH", true)]
    public async Task At_linked_instr_2_a_sides_push_or_pull_moves_the_isis_std(string row, bool toStd)
    {
        Baros(link: 2, mode1: toStd ? 0 : 3, mode2: toStd ? 0 : 3);
        _isisShows = toStd ? OffStd : OnStd;
        await IsisDrawn(ticksOnDelay: true);
        Press(row);
        _isisShows = toStd ? OnStd : OffStd;
        _sent.Clear();
        TypeIsis();
        Assert.Equal(!toStd, IsisWritten(1019));
        Assert.Equal(toStd, _speech.All.Contains(A350BaroEntry.IsisOnStdNotice));
    }

    [Fact]
    public async Task At_linked_instr_0_a_sides_pull_leaves_the_isis_std_known()
    {
        Baros(link: 0, mode1: 3);
        await IsisDrawn(ticksOnDelay: false);
        Press("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL");
        _sent.Clear();
        TypeIsis();
        Assert.Empty(_sent);
        Assert.Equal(A350BaroEntry.IsisOnStdNotice, _speech.All[^1]);
    }

    // The Altimeter entry waits for a draw since the key too, and leaves the ISIS out when that draw shows STD.
    [Fact]
    public async Task The_altimeter_entry_finds_the_isis_on_std_after_its_wait()
    {
        Baros(link: 0);
        _isisShows = OffStd;
        await IsisDrawn(ticksOnDelay: true);
        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _isisShows = OnStd;
        foreach (var side in new[] { 1, 2 })
            Fresh($"KOHLSMAN SETTING MB:{side}", 1033);
        _sent.Clear();
        await Enter(1033);
        Assert.DoesNotContain(_sent, rpn => rpn.Contains(" 3 r (>K:2:KOHLSMAN_SET)", StringComparison.Ordinal));
        Assert.Equal(A350BaroEntry.IsisOnStdNotice, _speech.All[^1]);
    }

    // Review 2026-11-17 (3): two ISIS entries waiting for the same draw, each polling on its own phase: the newer one may wake first and
    // run; the older one, waking after, stands down quietly instead of running last and leaving its value.
    [Fact]
    public async Task Of_two_isis_entries_waiting_the_newer_runs_whichever_wakes_first()
    {
        Baros(link: 0);
        var monitor = await IsisDrawn(ticksOnDelay: false);
        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _isisShows = OffStd;
        var older = new TaskCompletionSource();
        var newer = new TaskCompletionSource();
        _sent.Clear();
        _def.Delay = _ => older.Task;
        TypeIsis(1019);
        _def.Delay = _ => newer.Task;
        TypeIsis(1022);
        Assert.Empty(_sent);
        _monitorNow += TimeSpan.FromSeconds(1);
        await monitor.TickAsync();
        _def.Delay = _ => Task.CompletedTask;
        newer.SetResult();
        for (int i = 0; i < 100 && !IsisWritten(1022); i++)
            await Task.Delay(10);   // the continuations run on the test's context, after it yields
        Assert.True(IsisWritten(1022));
        older.SetResult();
        await Task.Delay(300);
        Assert.False(IsisWritten(1019));
    }

    // Review 2026-11-17 (3): an Altimeter entry superseded while it waited for the ISIS says nothing when it wakes, not even its refusal.
    [Fact]
    public async Task A_superseded_altimeter_entry_says_nothing_after_its_wait()
    {
        Baros(link: 0);
        var monitor = await IsisDrawn(ticksOnDelay: false);
        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _isisShows = OffStd;
        var older = new TaskCompletionSource();
        var newer = new TaskCompletionSource();
        _def.Delay = _ => older.Task;
        var first = Enter(500);   // refused once planned: outside every unit
        _def.Delay = _ => newer.Task;
        var second = Enter(1033);
        _monitorNow += TimeSpan.FromSeconds(1);
        await monitor.TickAsync();
        _def.Delay = _ => Task.CompletedTask;
        newer.SetResult();
        await second;
        older.SetResult();
        await first;
        Assert.DoesNotContain(_speech.All, s => s.Contains("must be", StringComparison.OrdinalIgnoreCase));
    }

    // Review 2026-11-17 (3): an aircraft switched while an entry waits: nothing is sent for it.
    [Fact]
    public async Task An_isis_entry_waiting_when_the_aircraft_goes_sends_nothing()
    {
        Baros(link: 0);
        var monitor = await IsisDrawn(ticksOnDelay: false);
        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _isisShows = OffStd;
        var gate = new TaskCompletionSource();
        _def.Delay = _ => gate.Task;
        _sent.Clear();
        TypeIsis(1019);
        _def.Dispose();
        _monitorNow += TimeSpan.FromSeconds(1);
        await monitor.TickAsync();
        _def.Delay = _ => Task.CompletedTask;
        gate.SetResult();
        await Task.Delay(300);   // the continuation runs on the test's context, after it yields
        Assert.Empty(_sent);
    }

    // With no draw of the ISIS since the press (the reader stalled), the wait ends at its bound and the entry writes, its read-back
    // saying what the ISIS then shows: as with the ISIS unread.
    [Fact]
    public async Task An_isis_entry_with_no_draw_since_writes_after_its_bound()
    {
        Baros(link: 0);
        _isisShows = OffStd;
        await IsisDrawn(ticksOnDelay: false);
        int waited = 0;
        _def.Delay = ms => { waited += ms; return Task.CompletedTask; };
        Press(IniBuildsA350Definition.IsisStdKeyRow);
        _sent.Clear();
        TypeIsis(1019);
        Assert.True(IsisWritten(1019));
        Assert.InRange(waited, IniBuildsA350Definition.IsisStdWaitMs, IniBuildsA350Definition.IsisStdWaitMs + 5000);
    }

    [Fact]
    public async Task A_newer_entry_stops_the_older_ones_targets()
    {
        Baros(link: 0);
        var gates = new Queue<TaskCompletionSource>();
        _def.Delay = _ =>
        {
            var gate = new TaskCompletionSource();
            gates.Enqueue(gate);
            return gate.Task;
        };
        var older = Enter(1033);
        var newer = Enter(1040);
        while (gates.Count > 0 || !(older.IsCompleted && newer.IsCompleted))
        {
            if (gates.Count > 0) gates.Dequeue().SetResult();
            else await Task.Delay(1);
        }
        await Task.WhenAll(older, newer).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(_sent, rpn => rpn == "16512 2 r (>K:2:KOHLSMAN_SET)");   // the older entry's first officer
        Assert.DoesNotContain(_sent, rpn => rpn == "16512 3 r (>K:2:KOHLSMAN_SET)");   // and its ISIS
        Assert.Contains("16624 3 r (>K:2:KOHLSMAN_SET)", _sent);
    }

    [Theory]
    [InlineData("STD, cyan", true)]
    [InlineData("Baro: 1013, cyan", false)]
    public void The_isis_std_is_read_from_its_display(string line, bool onStd) =>
        Assert.Equal(onStd, A350BaroEntry.IsisOnStd(new[] { "Speed: 250 knots", line, "Heading: 274" }));

    [Fact]
    public void A_display_that_shows_neither_cannot_say()
    {
        Assert.Null(A350BaroEntry.IsisOnStd(Array.Empty<string>()));
        Assert.Null(A350BaroEntry.IsisOnStd(new[] { "Speed: 250 knots" }));
        Assert.Null(A350BaroEntry.IsisOnStd(null));
    }

    [Fact]
    public void The_isis_line_says_std_where_its_display_does()
    {
        _cache[_def.KeyForInput("KOHLSMAN SETTING MB:3")] = 1013.25;
        _def.IsisStdRead = () => true;
        Assert.Equal("STD", _def.IsisBaroText(_sim));
        _def.IsisStdRead = () => false;
        Assert.Equal("1013 hectopascals, 29.92 inches", _def.IsisBaroText(_sim));
        _def.IsisStdRead = () => null;
        Assert.Equal("1013 hectopascals, 29.92 inches", _def.IsisBaroText(_sim));
    }

    [Fact]
    public void Enter_runs_one_entry()
    {
        var pressed = new List<double>();
        var said = new List<string>();
        Assert.True(A350BaroWindow.SubmitQnh("1033", pressed.Add, said.Add));
        Assert.Equal(new[] { 1033.0 }, pressed);
        Assert.Empty(said);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Enter_takes_a_point_under_a_comma_culture(string culture)
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
        try
        {
            var pressed = new List<double>();
            Assert.True(A350BaroWindow.SubmitQnh("29.92", pressed.Add, _ => { }));
            Assert.Equal(new[] { 29.92 }, pressed);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Enter_on_no_number_says_so_and_runs_nothing()
    {
        var pressed = new List<double>();
        var said = new List<string>();
        Assert.False(A350BaroWindow.SubmitQnh("abc", pressed.Add, said.Add));
        Assert.Empty(pressed);
        Assert.Equal(new[] { "Invalid number" }, said);
    }
}
