using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>A key's press is confirmed once: a key with a state reads it back after the settle time and says the
/// state's description; a key with none says "&lt;name&gt; pressed"; never both. In-memory rows, a fake sim access.</summary>
public class A330PanelButtonTests
{
    private const string PageRow = "TEST_PAGE";
    private const string KeyA = "TEST_KEY_A";
    private const string KeyB = "TEST_KEY_B";
    private const string Plain = "TEST_PLAIN";
    private const string RpnKey = "TEST_RPN_KEY";
    private const string NumberRow = "TEST_NUMBER";
    private const string NumberKey = "TEST_NUMBER_KEY";

    private static A330ControlMap Map() => new()
    {
        Controls =
        {
            new A330ControlMap.Control
            {
                Key = PageRow, Section = "S", Panel = "P", Label = "Test page", Kind = "status", Read = "L:INI_TEST_PAGE",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Label = "SYS A" },
                    new A330ControlMap.Position { Value = 1, Label = "SYS B" },
                },
            },
            new A330ControlMap.Control { Key = KeyA, Section = "S", Panel = "P", Label = "Test key A", Kind = "button", Event = "AIRLINER_TEST_A", Mode = "push", State = "INI_TEST_PAGE" },
            new A330ControlMap.Control { Key = KeyB, Section = "S", Panel = "P", Label = "Test key B", Kind = "button", Event = "AIRLINER_TEST_B", Mode = "push", State = "L:INI_TEST_PAGE", HoldMs = 300 },
            new A330ControlMap.Control { Key = Plain, Section = "S", Panel = "P", Label = "Test plain key", Kind = "button", Event = "AIRLINER_TEST_PLAIN", Mode = "push" },
            new A330ControlMap.Control { Key = RpnKey, Section = "S", Panel = "P", Label = "Test rpn key", Kind = "button", Mode = "rpn", Rpn = "{value} (>L:INI_TEST_CMD)", State = "INI_TEST_PAGE" },
            new A330ControlMap.Control { Key = NumberRow, Section = "S", Panel = "P", Label = "Test number", Kind = "status", Read = "L:INI_TEST_NUMBER" },
            new A330ControlMap.Control { Key = NumberKey, Section = "S", Panel = "P", Label = "Test number key", Kind = "button", Event = "AIRLINER_TEST_NUM", Mode = "push", State = "INI_TEST_NUMBER" },
        },
    };

    private static A330PanelSetTests.Def NewDef(A330PanelSetTests.FakeIo io, A330ControlMap? map = null) => new(io, map ?? Map());

    private static bool Press(IniA330Definition def, string key, A330PanelSetTests.Spoken said) =>
        def.HandleUIVariableSet(key, 1, def.GetVariables()[key], new SimConnectManager(IntPtr.Zero), said);

    private static async Task Release(A330PanelSetTests.FakeIo io)
    {
        io.Gate!.SetResult();
        await Task.Delay(100);
    }

    [Fact]
    public async Task An_ECAM_page_key_reads_back_its_page()
    {
        // the embedded map by key only: the key's state is the page row's variable, and the words are that row's
        var map = A330ControlMap.Embedded;
        var key = map.Controls.Single(c => c.Key == "A330_ECAM_ENG");
        Assert.NotNull(key.State);
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        var def = NewDef(io, map);
        var said = new A330PanelSetTests.Spoken();
        Assert.True(Press(def, "A330_ECAM_ENG", said));
        Assert.Equal(new[] { $"press {key.Event} {IniA330Definition.DefaultPushMs}" }, io.Sent);
        Assert.Empty(said.Lines);                                   // nothing yet: the aircraft is still settling
        Assert.Equal(new[] { IniA330Definition.ReadBackDelayMs }, io.Delays);
        await Release(io);
        var row = def.GetVariables()["A330_ECAM_PAGE"];
        Assert.Equal(new[] { "A330_ECAM_PAGE" }, io.FreshKeys);      // it reads the row the key names, fresh
        Assert.Equal(new[] { A330Write.StateSentence(row, 0) }, said.Lines);
        Assert.DoesNotContain(said.Lines, l => l.EndsWith("pressed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stateless_key_says_pressed()
    {
        var io = new A330PanelSetTests.FakeIo();
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Assert.True(Press(def, Plain, said));
        Assert.Equal(new[] { "press AIRLINER_TEST_PLAIN 150" }, io.Sent);
        Assert.Equal(new[] { "Test plain key pressed" }, said.Lines);   // the form of the base class's and the Fenix's "<name> pressed"
        Assert.Empty(io.Delays);                                          // no state to wait for
        Assert.Empty(io.FreshKeys);
    }

    [Fact]
    public async Task A_key_with_a_state_does_not_also_say_pressed()
    {
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Assert.True(Press(def, KeyB, said));
        Assert.Equal(new[] { "press AIRLINER_TEST_B 300" }, io.Sent);   // its own hold; "L:" in the state is read too
        Assert.Empty(said.Lines);                                        // not "pressed" now, and not a second sentence later
        await Release(io);
        Assert.Equal(new[] { "SYS B" }, said.Lines);

        // an rpn key is the same
        io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        def = NewDef(io);
        said = new A330PanelSetTests.Spoken();
        Assert.True(Press(def, RpnKey, said));
        Assert.Equal(new[] { "calc 1 (>L:INI_TEST_CMD)" }, io.Sent);
        await Release(io);
        Assert.Equal(new[] { "SYS A" }, said.Lines);
    }

    [Fact]
    public async Task A_newer_press_wins_the_read_back_of_a_row_two_keys_share()
    {
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Press(def, KeyA, said);
        Press(def, KeyB, said);
        Assert.Equal(2, io.Sent.Count);
        await Release(io);
        Assert.Equal(new[] { "SYS B" }, said.Lines);   // once, from the newest press
        Assert.Single(io.FreshKeys);

        // a press of an unrelated key does not cancel it
        io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        def = NewDef(io);
        said = new A330PanelSetTests.Spoken();
        Press(def, KeyA, said);
        Press(def, Plain, said);
        await Release(io);
        Assert.Equal(new[] { "Test plain key pressed", "SYS A" }, said.Lines);
    }

    [Fact]
    public async Task A_suppressed_press_reads_nothing_back()
    {
        // the First Officer's presses run with the announcer suppressed: no sentence now, none 1.5 s later
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken { Suppressed = true };
        Assert.True(Press(def, KeyB, said));
        said.Suppressed = false;
        Assert.Equal(new[] { "press AIRLINER_TEST_B 300" }, io.Sent);   // the press itself still goes out
        Assert.Empty(io.Delays);
        await Task.Delay(100);
        Assert.Empty(said.Lines);
        Assert.Empty(io.FreshKeys);
        // nothing is armed: the aircraft's change of the row is spoken as ever
        Deliver(def, PageRow, 0, said);
        Assert.Empty(said.Lines);
        Deliver(def, PageRow, 1, said);
        Assert.Equal(new[] { "Test page: SYS B" }, said.Lines);
    }

    [Fact]
    public async Task A_press_that_changes_an_announced_state_row_is_heard_once()
    {
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Deliver(def, PageRow, 0, said);   // the first reading seeds silently
        Press(def, KeyB, said);
        // the row's own change, delivered as MainForm would: consumed, so the row's announce is not a second voice
        Deliver(def, PageRow, 1, said);
        Assert.Empty(said.Lines);
        await Release(io);
        Assert.Equal(new[] { "SYS B" }, said.Lines);                    // the read-back is the only line
        // the echo ended with the read-back: a later change is spoken again
        Deliver(def, PageRow, 0, said);
        Assert.Equal(new[] { "SYS B", "Test page: SYS A" }, said.Lines);

        // a newer press of a key reading the row supersedes the first's echo and read-back, and ends it once
        io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        def = NewDef(io);
        said = new A330PanelSetTests.Spoken();
        Deliver(def, PageRow, 1, said);
        Press(def, KeyA, said);
        Press(def, KeyB, said);
        Deliver(def, PageRow, 0, said);
        await Release(io);
        Assert.Equal(new[] { "SYS A" }, said.Lines);
        Deliver(def, PageRow, 1, said);
        Assert.Equal(new[] { "SYS A", "Test page: SYS B" }, said.Lines);

        // a key with no state arms nothing
        io = new A330PanelSetTests.FakeIo();
        def = NewDef(io);
        said = new A330PanelSetTests.Spoken();
        Deliver(def, PageRow, 0, said);
        Press(def, Plain, said);
        Deliver(def, PageRow, 1, said);
        Assert.Equal(new[] { "Test plain key pressed", "Test page: SYS B" }, said.Lines);
    }

    /// <summary>The row's delivery as MainForm hands it to the definition (the mute wrap sets Suppressed around it).</summary>
    private static bool Deliver(IniA330Definition def, string key, double value, A330PanelSetTests.Spoken said) =>
        def.ProcessSimVarUpdate(key, value, said);

    [Fact]
    public async Task A_change_back_after_a_press_is_spoken()
    {
        // the master warning case: the lamp reads 1, the pilot cancels it (1 -> 0), the next warning sets it to 1 again
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 0 };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Deliver(def, PageRow, 1, said);
        Press(def, KeyA, said);
        Deliver(def, PageRow, 0, said);   // consumed: the read-back says it
        await Release(io);
        Assert.Equal(new[] { "SYS A" }, said.Lines);
        Deliver(def, PageRow, 1, said);   // the baseline followed the press, so this is a change and is spoken once
        Deliver(def, PageRow, 1, said);
        Assert.Equal(new[] { "SYS A", "Test page: SYS B" }, said.Lines);
    }

    [Fact]
    public void The_first_reading_of_a_state_row_is_silent()
    {
        var def = NewDef(new A330PanelSetTests.FakeIo());
        var said = new A330PanelSetTests.Spoken();
        Assert.True(Deliver(def, PageRow, 1, said));   // handled by the definition: the generic gate is never reached
        Deliver(def, PageRow, 1, said);
        Assert.Empty(said.Lines);
        Deliver(def, PageRow, 0, said);
        Assert.Equal(new[] { "Test page: SYS A" }, said.Lines);
    }

    [Fact]
    public async Task A_muted_state_row_says_nothing_for_a_background_change()
    {
        // MainForm's mute wrap (DefAnnounceMuteSets covers INI_A330) sets Suppressed around a muted row's delivery
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Deliver(def, PageRow, 0, said);
        said.Suppressed = true;
        Deliver(def, PageRow, 1, said);
        said.Suppressed = false;
        Assert.Empty(said.Lines);
        Deliver(def, PageRow, 1, said);                  // the baseline still followed: no change
        Assert.Empty(said.Lines);

        // the read-back of the pilot's own press is a confirmation, not a call-out: it still speaks
        Press(def, KeyB, said);
        said.Suppressed = true;
        Deliver(def, PageRow, 0, said);
        said.Suppressed = false;
        await Release(io);
        Assert.Equal(new[] { "SYS B" }, said.Lines);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_background_state_sentence_does_not_depend_on_the_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            var def = NewDef(new A330PanelSetTests.FakeIo());
            var said = new A330PanelSetTests.Spoken();
            Deliver(def, PageRow, 0, said);
            Deliver(def, PageRow, 2.5, said);   // a value the row names no position for
            Deliver(def, PageRow, 1, said);
            Assert.Equal(new[] { "Test page: 2.5", "Test page: SYS B" }, said.Lines);
            return 0;
        });
    }

    [Fact]
    public async Task A_state_change_after_the_echo_window_is_spoken_again()
    {
        long now = 1_000;
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 1 };
        var def = new A330PanelSetTests.Def(io, Map(), () => now);
        var said = new A330PanelSetTests.Spoken();
        Deliver(def, PageRow, 0, said);
        Press(def, KeyB, said);
        now += 2 * IniA330Definition.ReadBackDelayMs - 1;
        Deliver(def, PageRow, 1, said);                                   // still the press's echo
        Assert.Empty(said.Lines);
        now += 2;                                                         // the window is over
        Deliver(def, PageRow, 0, said);                                   // the aircraft's own change: spoken
        Assert.Equal(new[] { "Test page: SYS A" }, said.Lines);
        await Release(io);                                                // the read-back is still said
        Assert.Equal(new[] { "Test page: SYS A", "SYS B" }, said.Lines);
    }

    [Fact]
    public async Task A_state_that_cannot_be_read_says_nothing_at_all()
    {
        var io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = null };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Press(def, KeyA, said);
        await Release(io);
        Assert.Empty(said.Lines);
        io = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = double.NaN };
        def = NewDef(io);
        Press(def, KeyA, said);
        await Release(io);
        Assert.Empty(said.Lines);
    }

    [Fact]
    public void A_refused_key_says_so_and_never_pressed()
    {
        var io = new A330PanelSetTests.FakeIo { CanCalc = false };
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Assert.True(Press(def, Plain, said));   // handled: MainForm never falls back to its own write
        Assert.Equal(new[] { "Test plain key unavailable: the MobiFlight module is not responding." }, said.Lines);
        Assert.Empty(io.Sent);
        Assert.Empty(io.Delays);

        io = new A330PanelSetTests.FakeIo { CanCalc = false };
        def = NewDef(io);
        Assert.False(def.PressFromWindow(KeyA, 1, new SimConnectManager(IntPtr.Zero), new A330PanelSetTests.Spoken()));
        Assert.Empty(io.Delays);
    }

    [Fact]
    public void A_press_from_a_window_is_confirmed_like_the_panels()
    {
        var io = new A330PanelSetTests.FakeIo();
        var def = NewDef(io);
        var said = new A330PanelSetTests.Spoken();
        Assert.True(def.PressFromWindow(Plain, 1, new SimConnectManager(IntPtr.Zero), said));
        Assert.Equal(new[] { "Test plain key pressed" }, said.Lines);
    }

    [Fact]
    public void The_sentences_are_the_A32NX_button_read_backs()
    {
        // MainForm.AnnounceVariableState (the A32NX and A380 button read-back): the bare description, else
        // "<display name>: <value>"; the Fenix's and the base class's press: "<name> pressed"
        var def = NewDef(new A330PanelSetTests.FakeIo());
        var page = def.GetVariables()[PageRow];
        Assert.Equal("SYS B", A330Write.StateSentence(page, 1));
        Assert.Equal("Test number: 2.5", A330Write.StateSentence(def.GetVariables()[NumberRow], 2.5));
        Assert.Equal("Test number: 3", A330Write.StateSentence(def.GetVariables()[NumberRow], 3));
        Assert.Equal("Master warning pressed", A330Write.PressedSentence("Master warning"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task Every_sentence_is_the_same_under_a_comma_decimal_culture(string culture)
    {
        // no await inside TestCulture.Run's delegate: the sentences are built synchronously, the gated one is checked
        // after the culture is restored by the sentence builder's own invariance
        TestCulture.Run(culture, () =>
        {
            var def = NewDef(new A330PanelSetTests.FakeIo());
            Assert.Equal("Test number: 2.5", A330Write.StateSentence(def.GetVariables()[NumberRow], 2.5));
            Assert.Equal("Test number: 0.82", A330Write.StateSentence(def.GetVariables()[NumberRow], 0.82));
            Assert.Equal("Test plain key pressed", A330Write.PressedSentence("Test plain key"));
            var io = new A330PanelSetTests.FakeIo();
            var said = new A330PanelSetTests.Spoken();
            Press(NewDef(io), Plain, said);
            Assert.Equal(new[] { "Test plain key pressed" }, said.Lines);
            return 0;
        });

        var gated = new A330PanelSetTests.FakeIo { Gate = new TaskCompletionSource(), Fresh = 2.5 };
        var spoken = new A330PanelSetTests.Spoken();
        TestCulture.Run(culture, () => { Press(NewDef(gated), NumberKey, spoken); return 0; });
        await Release(gated);
        Assert.Equal(new[] { "Test number: 2.5" }, spoken.Lines);
    }
}
