using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Operating the MFD: typing through the KCCU's key L:vars (INI_MCDU1_* captain, INI_MCDU2_* first officer;
// live 2026-10-27: one calculator write of 1 pressed INIT), and clicking through the Coherent mouse route into
// the view whose wasm-instrument names the MFD gauge (the view number and guid change per session).
public class A350MfdInputTests
{
    // The point is DECIMAL: live 2026-10-28, in the MFD's ZFW field, INI_MCDU1_DECIMAL typed "167." and
    // INI_MCDU1_DOT (also a name in the module) typed nothing, so no weight or CG with a point could be entered.
    [Fact]
    public void Typed_text_maps_to_kccu_keys()
    {
        Assert.Equal(new[] { "V", "C", "B", "I", "SLASH", "1", "2", "DECIMAL", "5", "SPACE", "MINUS", "PLUS" },
            A350Kccu.KeysFor("vcbi/12.5 -+"));
        Assert.Null(A350Kccu.KeysFor("VC#BI"));   // no key for '#'
    }

    [Fact]
    public void A_key_is_its_sides_lvar()
    {
        Assert.Equal("INI_MCDU1_ENTER", A350Kccu.LVar(0, "ENTER"));
        Assert.Equal("INI_MCDU2_INIT", A350Kccu.LVar(1, "INIT"));
    }

    // Review 2026-10-29 (critical): the window pressed keys through SetLVar, whose calculator write is the same text for
    // a repeated key, and the MobiFlight channel drops a command identical to the one before it [VAR-4]: "330" typed
    // "30", "WMKK" typed "WMK". A press is one RPN string handed to the definition's Send, which makes each one unique.
    [Fact]
    public async Task A_repeated_key_is_pressed_again_through_the_definitions_sender()
    {
        var sent = new List<string>();
        var def = new MSFSBlindAssist.Aircraft.IniBuildsA350Definition { Send = (_, rpn) => { lock (sent) sent.Add(rpn); } };
        var input = def.NewMfdInput(null!);
        Assert.True(await input.TypeAsync(0, "330"));
        input.Press(1, "ENTER");
        Assert.Equal(new[] { "1 (>L:INI_MCDU1_3)", "1 (>L:INI_MCDU1_3)", "1 (>L:INI_MCDU1_0)", "1 (>L:INI_MCDU2_ENTER)" }, sent);
    }

    /// <summary>An input whose sends, waits and clicks are recorded in order; the click waits on <paramref name="click"/>
    /// when given.</summary>
    private static (A350MfdInput Input, List<string> Log) Recorded(TaskCompletionSource<bool>? click = null)
    {
        var log = new List<string>();
        var input = new A350MfdInput(rpn => { lock (log) log.Add("send " + rpn); },
            (ms, _) => { lock (log) log.Add("wait " + ms); return Task.CompletedTask; },
            (side, x, y, _) => { lock (log) log.Add($"click {side} {x} {y}"); return click?.Task ?? Task.FromResult(true); });
        return (input, log);
    }

    // Review 2026-10-29: [A350-3] the MFD key is one the aircraft does not reset (1, a hold of about 180 ms, then 0), and
    // it was only ever written 1. The click's release goes out 120 ms after ClickJs returns, and typing started at once,
    // so the first key could reach the field focused before. A line's action moves the cursor, lets the key go, clicks,
    // waits for the click to land, then types and presses ENTER.
    [Fact]
    public async Task An_action_lets_the_mfd_key_go_and_types_after_the_click_lands()
    {
        var (input, log) = Recorded();
        Assert.Equal(MfdAction.Done, await input.ActAsync(1, 583.5f, 442f, "FL"));
        Assert.Equal(new[]
        {
            "send 1 (>L:INI_MCDU2_MFD)", "wait 180", "send 0 (>L:INI_MCDU2_MFD)", "wait 50", "click 1 583.5 442", "wait 250",
            "send 1 (>L:INI_MCDU2_F)", "wait 150", "send 1 (>L:INI_MCDU2_L)", "wait 150", "send 1 (>L:INI_MCDU2_ENTER)",
        }, log);
    }

    // Tools review 2026-10-30: the probe pressed the ND key with Press, a single 1 the aircraft never resets, so the next
    // press had no rising edge and the cursor stayed where it was. The ND and MFD keys move the cursor the same way: held
    // about 180 ms, then let go [A350-3].
    [Theory]
    [InlineData("ND", 0, "INI_MCDU1_ND")]
    [InlineData("MFD", 1, "INI_MCDU2_MFD")]
    public async Task The_cursor_keys_are_held_and_let_go(string screen, int side, string lvar)
    {
        var (input, log) = Recorded();
        await input.MoveCursorAsync(side, screen);
        Assert.Equal(new[] { $"send 1 (>L:{lvar})", "wait 180", $"send 0 (>L:{lvar})", "wait 50" }, log);
        await Assert.ThrowsAsync<ArgumentException>(() => input.MoveCursorAsync(side, "INIT"));
    }

    [Fact]
    public async Task An_action_with_nothing_typed_only_clicks()
    {
        var (input, log) = Recorded();
        Assert.Equal(MfdAction.Done, await input.ActAsync(0, 10, 20, null));
        Assert.Equal("click 0 10 20", log[^2]);
        Assert.DoesNotContain(log, l => l.Contains("ENTER"));
    }

    // Review 2026-10-29: a second Enter during the first action's 1-2 s ran a second click and typing among the first
    // one's keys ("VVCCBBII").
    [Fact]
    public async Task A_second_action_while_one_runs_is_busy_and_sends_nothing()
    {
        var click = new TaskCompletionSource<bool>();
        var (input, log) = Recorded(click);
        var first = input.ActAsync(0, 10, 20, "VCBI");
        Assert.True(input.Busy);
        int before = log.Count;
        Assert.Equal(MfdAction.Busy, await input.ActAsync(0, 30, 40, "WMKK"));
        Assert.Equal(before, log.Count);
        click.SetResult(true);
        Assert.Equal(MfdAction.Done, await first);
        Assert.False(input.Busy);
    }

    [Fact]
    public async Task A_text_with_no_key_is_refused_before_anything_is_clicked()
    {
        var (input, log) = Recorded();
        Assert.Equal(MfdAction.NoKey, await input.ActAsync(0, 10, 20, "VC#BI"));
        Assert.Empty(log);
    }

    [Fact]
    public async Task An_unreachable_mfd_types_nothing()
    {
        var log = new List<string>();
        var input = new A350MfdInput(rpn => log.Add("send " + rpn), (_, _) => Task.CompletedTask, (_, _, _, _) => Task.FromResult(false));
        Assert.Equal(MfdAction.Unreachable, await input.ActAsync(0, 10, 20, "VCBI"));
        Assert.DoesNotContain(log, l => l.Contains("INI_MCDU1_V") || l.Contains("ENTER"));
    }

    [Fact]
    public void The_mfd_view_is_found_by_its_gauge()
    {
        Assert.Equal(30, A350MfdInput.GuidIf("30|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=inibuilds-A350.wasm&wasm_gauge=MFD"));
        Assert.Null(A350MfdInput.GuidIf("30|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=inibuilds-A350.wasm&wasm_gauge=ISIS"));
        Assert.Null(A350MfdInput.GuidIf("31|coui://x?wasm_module=other.wasm&wasm_gauge=MFD"));
        Assert.Null(A350MfdInput.GuidIf(""));
    }

    [Fact]
    public void A_click_moves_presses_and_releases_at_the_sides_point()
    {
        var js = A350MfdInput.ClickJs(30, 1, 583.5f, 442f);
        Assert.Contains("Coherent.call('WASM_MOUSE_MOVE',30,1419.5,442,0)", js);
        Assert.Contains("'WASM_MOUSE_DOWN',30,1419.5,442,0", js);
        Assert.Contains("'WASM_MOUSE_UP',30,1419.5,442,0", js);
        Assert.Contains("'WASM_CLICK',30,1419.5,442,0", js);
    }

    // Review 2026-10-29 (readers minor 17): the view was found once per window and the click answered 'ok' whatever happened,
    // so after a reload (a new guid in the same view) a click landed nowhere while typing went on. The click checks the page
    // still holds the guid it was found with before it clicks, and answers 'moved' when not, so the view is found again.
    [Fact]
    public void A_click_checks_its_mfd_is_still_there_before_it_clicks()
    {
        var js = A350MfdInput.ClickJs(30, 1, 583.5f, 442f);
        int check = js.IndexOf("getAttribute('guid')!=='30'", StringComparison.Ordinal);
        Assert.True(check >= 0, js);
        Assert.Contains("return 'moved'", js);
        Assert.True(check < js.IndexOf("WASM_MOUSE_MOVE", StringComparison.Ordinal), "the check comes before any mouse call");
    }

    [Fact]
    public void A_click_is_culture_safe()
    {
        var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains("30,216.5,247.25,0", A350MfdInput.ClickJs(30, 0, 216.5f, 247.25f));
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }
    }

    [Fact]
    public void Every_alt_letter_page_key_is_a_kccu_key()
    {
        Assert.All(MSFSBlindAssist.Forms.A350.A350MfdWindow.PageKeys.Values, k => Assert.Contains(k, A350Kccu.FunctionKeys));
        Assert.Equal("INIT", MSFSBlindAssist.Forms.A350.A350MfdWindow.PageKeys[System.Windows.Forms.Keys.I]);   // A380 letters
        Assert.Equal("FPLAN", MSFSBlindAssist.Forms.A350.A350MfdWindow.PageKeys[System.Windows.Forms.Keys.F]);
    }
}
