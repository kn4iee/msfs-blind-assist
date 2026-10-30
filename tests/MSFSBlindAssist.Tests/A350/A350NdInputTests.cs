using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

public class A350NdInputTests
{
    private sealed class Rig
    {
        public readonly List<string> Sent = new();
        public readonly List<string> Mouse = new();
        public bool Reachable = true;
        public TaskCompletionSource? Hold;
        public A350NdInput Input => _input ??= new A350NdInput(Sent.Add, (_, _) => Task.CompletedTask,
            async (side, x, y, _) =>
            {
                Mouse.Add($"click {side} {x:0} {y:0}");
                if (Hold != null) await Hold.Task;
                return Reachable;
            },
            (side, x0, y0, x1, y1, _) =>
            {
                Mouse.Add($"drag {side} {x0:0} {y0:0} {x1:0} {y1:0}");
                return Task.FromResult(Reachable);
            });
        private A350NdInput? _input;
    }

    [Theory]
    [InlineData("27|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=inibuilds-A350.wasm&wasm_gauge=EFIS", 27)]
    [InlineData("30|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=inibuilds-A350.wasm&wasm_gauge=MFD", null)]
    [InlineData("27|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=other.wasm&wasm_gauge=EFIS", null)]
    [InlineData("", null)]
    public void Only_an_a350_efis_view_is_taken(string view, int? guid)
    {
        Assert.Equal(guid, A350NdInput.EfisGuidIf(view));
    }

    [Fact]
    public async Task A_click_brings_the_sides_cursor_to_the_nd_first()
    {
        var rig = new Rig();
        Assert.Equal(MfdAction.Done, await rig.Input.ActAsync(1, 100, 200, null));
        Assert.Equal(new[] { "1 (>L:INI_MCDU2_ND)", "0 (>L:INI_MCDU2_ND)" }, rig.Sent);   // [A350-4]; the cursor keys are let go
        Assert.Equal(new[] { "click 1 100 200" }, rig.Mouse);
    }

    [Fact]
    public async Task A_field_takes_its_text_on_the_kccu_and_enter()
    {
        var rig = new Rig();
        Assert.Equal(MfdAction.Done, await rig.Input.ActAsync(0, 100, 200, "2.5"));
        Assert.Equal(new[] { "1 (>L:INI_MCDU1_2)", "1 (>L:INI_MCDU1_DECIMAL)", "1 (>L:INI_MCDU1_5)", "1 (>L:INI_MCDU1_ENTER)" }, rig.Sent.Skip(2));
    }

    [Fact]
    public async Task A_text_no_key_types_is_refused_before_anything_is_clicked()
    {
        var rig = new Rig();
        Assert.Equal(MfdAction.NoKey, await rig.Input.ActAsync(0, 100, 200, "2,5"));
        Assert.Empty(rig.Mouse);
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task An_unreachable_nd_types_nothing()
    {
        var rig = new Rig { Reachable = false };
        Assert.Equal(MfdAction.Unreachable, await rig.Input.ActAsync(0, 100, 200, "25"));
        Assert.DoesNotContain(rig.Sent, s => s.Contains("INI_MCDU1_2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_action_while_one_runs_is_busy()
    {
        var rig = new Rig { Hold = new TaskCompletionSource() };
        var first = rig.Input.ActAsync(0, 100, 200, null);
        Assert.True(rig.Input.Busy);
        Assert.Equal(MfdAction.Busy, await rig.Input.ActAsync(0, 300, 400, null));
        Assert.Equal(MfdAction.Busy, await rig.Input.DragAsync(0, 1, 2, 3, 4));
        rig.Hold.SetResult();
        Assert.Equal(MfdAction.Done, await first);
        Assert.False(rig.Input.Busy);
        Assert.Single(rig.Mouse);
    }

    [Fact]
    public async Task A_drag_brings_the_cursor_too()
    {
        var rig = new Rig();
        Assert.Equal(MfdAction.Done, await rig.Input.DragAsync(0, 404, 450, 404, 630));
        Assert.StartsWith("1 (>L:INI_MCDU1_ND)", rig.Sent[0]);
        Assert.Equal(new[] { "drag 0 404 450 404 630" }, rig.Mouse);
    }

    [Fact]
    public void A_drag_presses_at_the_start_moves_and_lets_go_at_the_end()
    {
        var js = A350NdInput.DragJs(27, 1240.5f, 450, 1240.5f, 630);
        Assert.Contains("getAttribute('guid')!=='27'", js);
        Assert.Contains("x0=1240.5", js);
        Assert.Contains("y1=630", js);
        Assert.Contains("WASM_MOUSE_DOWN", js);
        Assert.Contains("WASM_MOUSE_MOVE", js);
        Assert.Contains("WASM_MOUSE_UP", js);
    }

    [Fact]
    public void A_drag_never_takes_the_pcs_decimal_comma()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains("x0=1240.5", A350NdInput.DragJs(27, 1240.5f, 450, 1240.5f, 630));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void No_capture_and_a_dark_screen_have_nothing_to_click()
    {
        var none = A350NdWindow.PageLines(null, 0, false);
        Assert.Empty(none.Lines);
        Assert.Equal(new[] { MSFSBlindAssist.Aircraft.IniBuildsA350Definition.NotAvailableYet }, none.Shown);
        var dark = A350NdWindow.PageLines(A350Fixtures.Capture("efis0_anf_landing.json"), 0, true);
        Assert.Empty(dark.Lines);
        Assert.Equal(new[] { MSFSBlindAssist.Aircraft.IniBuildsA350Definition.DarkScreen }, dark.Shown);
    }

    [Fact]
    public void The_key_of_the_a380s_airport_map_opens_it_on_the_a350()
    {
        // The window key's handling is the definition's, before MainForm's "only on the A380".
        var source = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "IniBuildsA350Definition.Hotkeys.cs"));
        Assert.Contains("case HotkeyAction.ShowOANS:", source);
    }
}
