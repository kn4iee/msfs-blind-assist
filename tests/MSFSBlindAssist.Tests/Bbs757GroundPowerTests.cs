using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 EFB window's ground power buttons, as far as they need no simulator. BlueBird's Ground Services
/// GPU tile TOGGLES <c>L:BBS_757_GPU_AC_PWR_CONNECTED</c> (measured 2026-10-25, session 8, and again
/// 2026-10-26), so the tile is tapped only when that variable reads the other way from the wish and, for a
/// disconnect, external power is not on line. This pins what is said instead of a tap and after one, what
/// the Ground Power row says when it changes, and the tile's click sequence. The second half is the
/// definition's side (BBS757Definition.Efb.cs): the row on the Electrical panel, its speech, and the echo the
/// window opens around its own run (armed before its taps, ended when it settles) so a change it made is said
/// once, by the window.
/// </summary>
public class Bbs757GroundPowerTests
{
    private const string Unread = "The ground power cannot be read, so nothing was tapped.";
    private const string AlreadyConnected = "Ground power is already connected.";
    private const string AlreadyDisconnected = "Ground power is already disconnected.";
    private const string OnLine =
        "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or the engines " +
        "powering the aircraft, then try again.";
    private const string ExtPwrUnread = "External power cannot be read, so nothing was tapped.";

    private const string Connected = "Ground power connected. Press EXT PWR on the Electrical panel to put it on line.";
    private const string Disconnected = "Ground power disconnected.";
    private const string NotConnected = "BlueBird did not connect the ground power.";
    private const string NotDisconnected = "BlueBird did not disconnect the ground power.";
    private const string ResultUnread = "The ground power cannot be read now.";

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

    // ---- whether the tile is tapped -------------------------------------------------------------

    [Theory]
    // The connection unread: nothing says which way a tap would go, whatever else reads.
    [InlineData(true, null, null, Unread)]
    [InlineData(true, null, 0.0, Unread)]
    [InlineData(true, double.NaN, 0.0, Unread)]
    [InlineData(false, null, 0.0, Unread)]
    [InlineData(false, null, 1.0, Unread)]                       // before the EXT PWR refusal
    [InlineData(false, double.PositiveInfinity, null, Unread)]
    [InlineData(false, double.NegativeInfinity, 0.0, Unread)]
    // A connect while connected, whatever EXT PWR reads: a tap would disconnect it.
    [InlineData(true, 1.0, 0.0, AlreadyConnected)]
    [InlineData(true, 1.0, null, AlreadyConnected)]
    [InlineData(true, 1.0, 1.0, AlreadyConnected)]
    [InlineData(true, 0.51, null, AlreadyConnected)]             // above 0.5 reads 1
    // A disconnect while disconnected, ahead of the EXT PWR refusals: a tap would connect it.
    [InlineData(false, 0.0, 0.0, AlreadyDisconnected)]
    [InlineData(false, 0.0, 1.0, AlreadyDisconnected)]
    [InlineData(false, 0.0, null, AlreadyDisconnected)]
    [InlineData(false, 0.5, 0.0, AlreadyDisconnected)]           // 0.5 and below reads 0
    // A disconnect while connected: external power on line, or its state unread.
    [InlineData(false, 1.0, 1.0, OnLine)]
    [InlineData(false, 1.0, 0.51, OnLine)]
    [InlineData(false, 1.0, null, ExtPwrUnread)]
    [InlineData(false, 1.0, double.NaN, ExtPwrUnread)]
    [InlineData(false, 1.0, double.PositiveInfinity, ExtPwrUnread)]
    public void Refusals(bool connect, double? connected, double? extPwrOn, string expected)
    {
        Assert.Equal(expected, Bbs757GroundPower.Refusal(connect, connected, extPwrOn));
    }

    [Theory]
    [InlineData(true, 0.0, null, true)]            // disconnected, connecting: tap (a connect never looks at EXT PWR)
    [InlineData(true, 0.0, 0.0, true)]
    [InlineData(true, 0.0, 1.0, true)]
    [InlineData(true, 0.0, double.NaN, true)]
    [InlineData(true, 0.5, null, true)]            // 0.5 still reads 0 ...
    [InlineData(true, 0.51, null, false)]          // ... and above it reads 1
    [InlineData(false, 1.0, 0.0, true)]            // connected, EXT PWR off: tap
    [InlineData(false, 0.7, 0.5, true)]
    [InlineData(true, 1.0, 0.0, false)]            // already connected: a tap would undo it
    [InlineData(false, 0.0, 0.0, false)]           // already disconnected: a tap would undo it
    [InlineData(false, 1.0, 1.0, false)]           // EXT PWR on line: refused before any tap
    [InlineData(false, 1.0, null, false)]          // EXT PWR unread
    [InlineData(false, 1.0, double.NaN, false)]
    [InlineData(true, null, 0.0, false)]           // unread: never guess on a toggle
    [InlineData(false, null, 0.0, false)]
    [InlineData(true, double.NaN, null, false)]
    [InlineData(false, double.PositiveInfinity, 0.0, false)]
    public void Taps_only_when_different(bool connect, double? connected, double? extPwrOn, bool tap)
    {
        Assert.Equal(tap, Bbs757GroundPower.Refusal(connect, connected, extPwrOn) is null);
    }

    // ---- what is said after the taps ------------------------------------------------------------

    [Theory]
    [InlineData(true, 0.0, 1.0, Connected)]
    [InlineData(false, 1.0, 0.0, Disconnected)]
    [InlineData(true, 0.0, 0.0, NotConnected)]
    [InlineData(false, 1.0, 1.0, NotDisconnected)]
    [InlineData(true, 0.0, null, ResultUnread)]
    [InlineData(false, 1.0, null, ResultUnread)]
    [InlineData(true, 0.0, double.NaN, ResultUnread)]
    [InlineData(false, 1.0, double.PositiveInfinity, ResultUnread)]
    [InlineData(true, 0.0, 0.51, Connected)]                     // above 0.5 reads 1
    [InlineData(true, 0.0, 0.5, NotConnected)]                   // 0.5 and below reads 0
    // Only the read after decides, whatever was read before.
    [InlineData(true, null, 1.0, Connected)]
    [InlineData(false, double.NaN, 0.0, Disconnected)]
    [InlineData(true, 1.0, 1.0, Connected)]
    [InlineData(true, 1.0, 0.0, NotConnected)]
    [InlineData(false, 0.0, 1.0, NotDisconnected)]
    [InlineData(true, null, null, ResultUnread)]
    public void Results(bool connect, double? before, double? after, string expected)
    {
        Assert.Equal(expected, Bbs757GroundPower.Result(connect, before, after));
    }

    [Theory]
    [InlineData(1.0, "Ground power connected")]
    [InlineData(0.0, "Ground power disconnected")]
    [InlineData(0.51, "Ground power connected")]
    [InlineData(0.5, "Ground power disconnected")]
    [InlineData(2.0, "Ground power connected")]
    [InlineData(-1.0, "Ground power disconnected")]
    [InlineData(double.NaN, null)]                               // not finite: nothing to say
    [InlineData(double.PositiveInfinity, null)]
    [InlineData(double.NegativeInfinity, null)]
    public void A_change_of_the_ground_power_row_says_connected_or_disconnected(double value, string? phrase)
    {
        Assert.Equal(phrase, Bbs757GroundPower.ChangePhrase(value));
    }

    [Fact]
    public void Every_phrase_is_the_same_under_a_comma_decimal_culture()
    {
        // Values with a fraction beside the ones that read as unread: no phrase may follow the PC's culture.
        double?[] values = { null, double.NaN, 0.0, 0.3, 0.5, 0.7, 1.0 };

        List<string?> Phrases()
        {
            var said = new List<string?>();
            foreach (bool connect in new[] { true, false })
            {
                foreach (double? a in values)
                {
                    said.Add(Bbs757GroundPower.Result(connect, a, a));
                    foreach (double? b in values) said.Add(Bbs757GroundPower.Refusal(connect, a, b));
                }
            }
            foreach (double v in values.OfType<double>()) said.Add(Bbs757GroundPower.ChangePhrase(v));
            return said;
        }

        List<string?> expected = new();
        InCulture("", () => expected = Phrases());

        Assert.Contains(Connected, expected);
        Assert.Contains(OnLine, expected);
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(expected, Phrases()));
    }

    // ---- the variables and the run window ----------------------------------------------------------

    [Fact]
    public void The_variable_names_and_the_run_window_are_pinned()
    {
        Assert.Equal("BBS_757_GPU_AC_PWR_CONNECTED", Bbs757GroundPower.ConnectedVar);
        Assert.Equal("B757_OVHD_EXT_PWR_BUTTON_ON", Bbs757GroundPower.ExtPwrOnVar);
        // The window's own OnRequest key for the L:var the announced EXT PWR row also reads: another key.
        Assert.Equal("BBS757_EFB_EXT_PWR_ON", Bbs757GroundPower.ExtPwrOnKey);
        Assert.NotEqual(Bbs757SharedKeys.ExtPwrOn, Bbs757GroundPower.ExtPwrOnKey);
        Assert.Equal(60_000, Bbs757GroundPower.WindowRunMaxMs);
        // No run outlasts its window: the four taps take six seconds.
        Assert.True(Bbs757EfbLayout.TotalMs(Bbs757EfbLayout.ToggleGroundPower()) < Bbs757GroundPower.WindowRunMaxMs);
    }

    // ---- the click sequence -----------------------------------------------------------------------

    [Fact]
    public void Sequence_ends_on_the_gpu_tile()
    {
        // Measured 2026-10-25 (session 8) and again 2026-10-26: the same four taps, 1.5 s apart, toggled the
        // ground power each time they ran.
        var seq = Bbs757EfbLayout.ToggleGroundPower();

        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, Bbs757EfbLayout.AircraftMenuGroundServices,
            Bbs757EfbLayout.GpuTile,
        }, seq.Select(s => s.Point));
        Assert.Equal(new EfbPoint(1005, 231), Bbs757EfbLayout.GpuTile);
        Assert.Single(seq, s => s.Point == Bbs757EfbLayout.GpuTile);   // one tap: it toggles
        Assert.All(seq, s => Assert.Equal(Bbs757EfbLayout.NavigateDelayMs, s.DelayAfterMs));
    }

    // ---- the definition: the Ground Power row, its speech and the window's echo ---------------------------

    private const string GpuKey = Bbs757SharedKeys.GpuConnected;
    private const string RowConnected = "Ground power connected";
    private const string RowDisconnected = "Ground power disconnected";

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; } = 100_000;
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig() => Def = new BBS757Definition { NestRunning = () => true, NowMs = () => Now };

        /// <summary>One delivery of the ground power row; what ProcessSimVarUpdate answered.</summary>
        public bool Deliver(double value) => Def.ProcessSimVarUpdate(GpuKey, value, Speech);
    }

    [Fact]
    public void Ground_power_row_on_electrical()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();

        // The connection: a streamed, announced status row with its Ctrl+M row.
        var gpu = vars[GpuKey];
        Assert.Equal("BBS_757_GPU_AC_PWR_CONNECTED", gpu.Name);
        Assert.Equal(Bbs757GroundPower.ConnectedVar, gpu.Name);
        Assert.Equal("Ground Power", gpu.DisplayName);
        Assert.Equal(SimVarType.LVar, gpu.Type);
        Assert.Equal("number", gpu.Units);
        Assert.Equal(UpdateFrequency.Continuous, gpu.UpdateFrequency);
        Assert.True(gpu.IsAnnounced);
        Assert.True(gpu.RenderAsReadOnlyStatus);
        Assert.False(gpu.ExcludeFromMonitorManager);
        Assert.Equal(new Dictionary<double, string> { [0] = "Not connected", [1] = "Connected" }, gpu.ValueDescriptions);
        Assert.Contains(def.MonitorRows(), r => r.Key == GpuKey && r.Label == "Ground Power");

        // It is no control. Which panel's status display shows it is proved below through an in-memory map, not
        // the generated one (the plan set's convention: tests never depend on another plan's map).
        Assert.DoesNotContain(GpuKey, def.GetPanelControls().SelectMany(p => p.Value));

        // The status display says it in words.
        Assert.True(def.TryGetDisplayOverride(GpuKey, 1, out string connected));
        Assert.Equal("Connected", connected);
        Assert.True(def.TryGetDisplayOverride(GpuKey, 0, out string notConnected));
        Assert.Equal("Not connected", notConnected);
        Assert.True(def.TryGetDisplayOverride(GpuKey, double.NaN, out string unread));
        Assert.Equal("unavailable", unread);

        // The window's own read of external power: on request, never spoken, no Ctrl+M row ([VAR-9]), on no
        // panel; and it never streams beside plan 06's announced row on the same L:var ([VAR-7]).
        var ext = vars[Bbs757GroundPower.ExtPwrOnKey];
        Assert.Equal("B757_OVHD_EXT_PWR_BUTTON_ON", ext.Name);
        Assert.Equal("External Power On", ext.DisplayName);
        Assert.Equal(SimVarType.LVar, ext.Type);
        Assert.Equal("number", ext.Units);
        Assert.Equal(UpdateFrequency.OnRequest, ext.UpdateFrequency);
        Assert.False(ext.IsAnnounced);
        Assert.True(ext.ExcludeFromMonitorManager);
        Assert.DoesNotContain(def.MonitorRows(), r => r.Key == Bbs757GroundPower.ExtPwrOnKey);
        Assert.DoesNotContain(Bbs757GroundPower.ExtPwrOnKey, def.GetPanelControls().SelectMany(p => p.Value));
        Assert.DoesNotContain(Bbs757GroundPower.ExtPwrOnKey, def.GetPanelDisplayVariables().SelectMany(p => p.Value));
    }

    [Theory]
    [InlineData(BBS757Definition.GroundPowerPanel, true)]
    [InlineData("Pedestal", false)]
    public void The_row_joins_the_electrical_panel_only_when_the_map_has_one(string panel, bool joins)
    {
        // An in-memory map of one control, on the panel under test (no dependence on the generated map).
        var map = Bbs757ControlMap.Parse($$"""
            {
              "controls": [
                {
                  "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "{{panel}}",
                  "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON"
                }
              ]
            }
            """);
        var def = new BBS757Definition(map) { NestRunning = () => true };
        var displays = def.GetPanelDisplayVariables();

        if (joins)
        {
            Assert.Single(displays[BBS757Definition.GroundPowerPanel], GpuKey);   // on that panel's display ...
            Assert.Single(displays.SelectMany(p => p.Value), GpuKey);            // ... and on no other
        }
        else
        {
            // A display of a panel the map lacks would be an orphan entry whose rows silently vanish.
            Assert.DoesNotContain(BBS757Definition.GroundPowerPanel, def.GetPanelControls().Keys);
            Assert.DoesNotContain(BBS757Definition.GroundPowerPanel, displays.Keys);
            Assert.DoesNotContain(GpuKey, displays.SelectMany(p => p.Value));
        }
        Assert.Contains(GpuKey, def.GetVariables().Keys);   // the row speaks whatever panels the map has
        Assert.DoesNotContain(GpuKey, def.GetPanelControls().SelectMany(p => p.Value));
    }

    [Theory]
    [InlineData(0.0, 1.0, RowConnected)]
    [InlineData(1.0, 0.0, RowDisconnected)]
    public void Change_announced_after_silent_first(double first, double then, string said)
    {
        var rig = new Rig();

        // The cockpit as found, or BlueBird's own GPU/jetway connecting at the flight load: the baseline.
        Assert.True(rig.Deliver(first));
        rig.Deliver(first);
        Assert.Empty(rig.Speech.All);

        // A change speaks once; the same value again does not.
        Assert.True(rig.Deliver(then));
        rig.Deliver(then);
        Assert.Equal(new[] { said }, rig.Speech.All);

        // A flight load, then a reconnect: the next reading is a baseline each time.
        rig.Def.OnSimContextReset();
        rig.Deliver(first);
        rig.Def.ResetAnnouncementBaselines();
        rig.Deliver(then);
        Assert.Equal(new[] { said }, rig.Speech.All);
    }

    [Fact]
    public void An_unreadable_reading_is_neither_spoken_nor_a_baseline()
    {
        var rig = new Rig();

        Assert.True(rig.Deliver(double.NaN));   // consumed: the generic path never says a raw value
        rig.Deliver(1);                         // the first reading there is: the baseline
        rig.Deliver(double.PositiveInfinity);
        rig.Deliver(1);
        Assert.Empty(rig.Speech.All);

        rig.Deliver(0);
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void The_window_reads_of_external_power_are_left_to_the_generic_path_and_say_nothing()
    {
        var rig = new Rig();

        Assert.False(rig.Def.ProcessSimVarUpdate(Bbs757GroundPower.ExtPwrOnKey, 1, rig.Speech));
        Assert.False(rig.Def.ProcessSimVarUpdate(Bbs757GroundPower.ExtPwrOnKey, 0, rig.Speech));
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Window_run_not_said_twice()
    {
        var rig = new Rig();
        rig.Deliver(0);                       // not connected: the baseline
        rig.Def.ArmGroundPowerEcho();         // right before the window's taps

        rig.Now += 30_000;                    // a slow run, inside the 60 s
        rig.Deliver(1);                       // the tile took: tracked, not spoken
        Assert.Empty(rig.Speech.All);

        rig.Def.SettleGroundPowerEcho(1);     // the run is over; the window said the result itself
        rig.Deliver(1);                       // the delivery in flight: no change
        Assert.Empty(rig.Speech.All);

        rig.Deliver(0);                       // another source takes it away, right after: spoken
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void A_settle_rebaselines_to_what_the_window_read()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();

        // The window read the connection after its taps (the delivery had not come yet) and said it.
        rig.Def.SettleGroundPowerEcho(1);
        rig.Deliver(1);                       // that delivery is no change, though the echo is over

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_late_change_after_a_run_whose_read_back_missed_the_wish_is_spoken()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();         // Connect ground power
        rig.Now += 6000;                      // the taps took their six seconds ...

        // ... and the read-back was still not connected: the window said "BlueBird did not connect the ground power".
        rig.Def.SettleGroundPowerEcho(0);
        rig.Now += 3000;
        rig.Deliver(0);                       // nothing yet
        Assert.Empty(rig.Speech.All);

        rig.Deliver(1);                       // BlueBird connects it after all: the aircraft's change, spoken
        Assert.Equal(new[] { RowConnected }, rig.Speech.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unread_settle_keeps_the_baseline_and_ends_the_echo(double? after)
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();
        rig.Now += 2000;                      // a quick run that read nothing at the end

        rig.Def.SettleGroundPowerEcho(after);
        rig.Deliver(0);                       // still as it was: nothing
        Assert.Empty(rig.Speech.All);

        rig.Deliver(1);                       // a change from the baseline of 0, after the run: spoken
        Assert.Equal(new[] { RowConnected }, rig.Speech.All);
    }

    [Fact]
    public void A_change_during_the_run_stays_silent_when_the_settle_reads_nothing()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();
        rig.Now += 5000;
        rig.Deliver(1);                       // during the armed run, before the settle: tracked, not spoken
        Assert.Empty(rig.Speech.All);

        rig.Def.SettleGroundPowerEcho(null);  // nothing read at the end: the baseline is what the run tracked
        rig.Deliver(1);                       // still: nothing
        Assert.Empty(rig.Speech.All);

        rig.Deliver(0);                       // another source's change, right after the run: spoken
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void A_run_that_never_settles_hides_changes_for_no_longer_than_its_longest_time()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();

        rig.Now += Bbs757GroundPower.WindowRunMaxMs - 1;
        rig.Deliver(1);                       // still inside the longest run: its echo
        Assert.Empty(rig.Speech.All);

        rig.Now += 2;
        rig.Deliver(0);                       // past it: another source's change
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void A_muted_ground_power_row_says_nothing()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new GatedSpeechCapture();
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { GpuKey } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, GpuKey, 0);
        MuteWrap.Deliver(def, speech, settings, GpuKey, 1);
        MuteWrap.Deliver(def, speech, settings, Bbs757Readouts.FlapsHandleKey, 0);
        MuteWrap.Deliver(def, speech, settings, Bbs757Readouts.FlapsHandleKey, 1);

        Assert.Equal(new[] { "Flaps 1" }, speech.All);
        Assert.False(def.IsMuteWrapExempt(GpuKey));

        // The same deliveries with nothing muted speak: the silence above is the mute's, not the row's.
        var loud = new GatedSpeechCapture();
        var unmuted = new UserSettings();
        unmuted.RebuildDisabledMonitorVariableCaches();
        var other = new BBS757Definition { NestRunning = () => true };
        MuteWrap.Deliver(other, loud, unmuted, GpuKey, 0);
        MuteWrap.Deliver(other, loud, unmuted, GpuKey, 1);
        Assert.Equal(new[] { RowConnected }, loud.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_row_reads_the_same_under_a_comma_decimal_culture(string culture)
    {
        // The texts are words, but a reading with a fraction must not make either follow the PC's culture:
        // 0 is the baseline, 0.51 is the first reading above one half (connected), the unreadable ones
        // change nothing, and the last 0.3 is the change back.
        double[] values = { 0, 0.3, 0.5, 0.51, 0.7, 1, double.NaN, double.PositiveInfinity, 0.3 };

        (List<string> Display, List<string> Spoken) Texts()
        {
            var rig = new Rig();
            var display = new List<string>();
            foreach (double v in values)
            {
                Assert.True(rig.Def.TryGetDisplayOverride(GpuKey, v, out string text));
                display.Add(text);
                rig.Deliver(v);
            }
            return (display, rig.Speech.All.ToList());
        }

        (List<string> Display, List<string> Spoken) expected = default;
        InCulture("", () => expected = Texts());

        Assert.Equal(new[]
        {
            "Not connected", "Not connected", "Not connected", "Connected", "Connected", "Connected",
            "unavailable", "unavailable", "Not connected",
        }, expected.Display);
        Assert.Equal(new[] { RowConnected, RowDisconnected }, expected.Spoken);
        InCulture(culture, () =>
        {
            var inCulture = Texts();
            Assert.Equal(expected.Display, inCulture.Display);
            Assert.Equal(expected.Spoken, inCulture.Spoken);
        });
    }
}
