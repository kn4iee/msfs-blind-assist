using MSFSBlindAssist.Aircraft;
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
    // "Captain baro is on STD and its knob shows no preset here, as on the ground; pull it for QNH first.", and the echo it had armed
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
