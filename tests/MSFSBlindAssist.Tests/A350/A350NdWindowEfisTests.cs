using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26 Task 4: the ND and every function of it in one window, on Alt+N (the owner, 2026-11-13: "the whole point is to
/// have the nd plus all its functions in one place"): the ND and its airport map, its mode and range, every EFIS button that changes
/// it, BTV; the list first, as the Fenix's ND window. Ctrl+Shift+B no longer opens it.</summary>
public class A350NdWindowEfisTests
{
    [Fact]
    public void Its_efis_keys_are_the_sides_nd_buttons()
    {
        Assert.Equal(new[] { "CSTR", "WPT", "VORD", "NDB", "ARPT", "VOR 1", "WX", "TERR", "TRAF", "VOR 2", "TAXI" },
            A350NdWindow.EfisKeys.Select(k => k.Label));
        Assert.Equal(new[]
        {
            "A350_EFIS_TSK_1_LEFT", "A350_EFIS_TSK_2_LEFT", "A350_EFIS_TSK_3_LEFT", "A350_EFIS_TSK_4_LEFT", "A350_EFIS_TSK_5_LEFT",
            "A350_EFIS_BSK_1_LEFT", "A350_EFIS_BSK_2_LEFT", "A350_EFIS_BSK_3_LEFT", "A350_EFIS_BSK_4_LEFT", "A350_EFIS_BSK_5_LEFT",
            "A350_EFIS_TAXI_LEFT",
        }, A350NdWindow.EfisKeys.Select(k => k.Row(0)));
        Assert.All(A350NdWindow.EfisKeys, k => Assert.EndsWith("_RIGHT", k.Row(1)));
        Assert.Equal(new[] { "TAXI" }, A350NdWindow.EfisKeys.Where(k => k.Toggle).Select(k => k.Label));
    }

    // Every row of each EFIS panel is on the PFD window or the ND window: nothing the panel does is left out of the two.
    [Theory]
    [InlineData("EFIS Captain", 0)]
    [InlineData("EFIS First Officer", 1)]
    public void Every_efis_row_is_on_the_pfd_or_the_nd_window(string panel, int side)
    {
        var onWindows = A350PfdWindow.Keys.Select(k => k.Row(side)).Append(A350PfdWindow.QnhRow(side))
            .Concat(A350NdWindow.EfisKeys.Select(k => k.Row(side)))
            .Append(A350NdWindow.KnobRow(side, mode: true)).Append(A350NdWindow.KnobRow(side, mode: false)).ToHashSet();
        var rows = A350PanelLayout.Place(A350ControlMap.Load()).RowsByPanel[panel].Select(r => r.Key).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Contains(row, onWindows));
    }

    [Fact]
    public void Every_row_it_presses_is_a_row_of_the_aircraft()
    {
        var rows = new IniBuildsA350Definition().GetVariables();
        foreach (int side in new[] { 0, 1 })
            Assert.All(A350NdWindow.EfisKeys, k => Assert.True(rows.ContainsKey(k.Row(side)), k.Row(side)));
    }

    [Fact]
    public void Alt_n_opens_it_and_ctrl_shift_b_no_longer_does()
    {
        Assert.Equal("ND", A350Hotkeys.DisplayWindows[HotkeyAction.ReadDisplayND]);
        Assert.DoesNotContain(HotkeyAction.ShowOANS, A350Hotkeys.DisplayWindows.Keys);
    }

    private sealed class Rows : IA350WindowRows
    {
        public readonly List<string> Log = new();
        public string? State(string row) => row == "A350_EFIS_TSK_1_LEFT" ? "On" : null;
        public void Press(string row, double value = 1) => Log.Add($"press {row}");
        public void Toggle(string row) => Log.Add($"toggle {row}");
        public void Type(string row, string text) => Log.Add($"type {row} {text}");
    }

    private static A350NdWindow Window(IA350WindowRows rows) => new(_ => null, _ => Task.FromResult<A350DrawCapture?>(null), _ => false,
        new A350NdInput(_ => { }), new SpeechCapture(), (_, _, _) => { }, (_, _) => null, null, rows);

    // The Fenix's order: the list first, then the controls, the side box last.
    [Fact]
    public void The_list_comes_first_then_the_controls()
    {
        using var form = Window(new Rows());
        var order = form.Controls.Cast<Control>().OrderBy(c => c.TabIndex).Select(c => c.AccessibleName).ToList();
        Assert.Equal(new[] { "ND display", "Text: type, then Enter on a field", "ND mode", "ND range", "EFIS", "BTV controls", "Side" }, order);
    }

    [Fact]
    public void An_efis_button_reads_its_state_and_presses_its_row()
    {
        var rows = new Rows();
        using var form = Window(rows);
        form.RefreshEfisStates();
        var cstr = form.EfisButtons[0];
        Assert.Equal("CSTR: On", cstr.Text);
        Assert.Equal("WPT", form.EfisButtons[1].Text);
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cstr, new object[] { EventArgs.Empty });
        Assert.Equal(new[] { "press A350_EFIS_TSK_1_LEFT" }, rows.Log);
    }

    private sealed class StateRows : IA350WindowRows
    {
        public string? Cstr;
        public string? State(string row) => row == "A350_EFIS_TSK_1_LEFT" ? Cstr : null;
        public void Press(string row, double value = 1) { }
        public void Toggle(string row) { }
        public void Type(string row, string text) { }
    }

    // Final review 2a, the ND window too: a tick on the draw from before a press returned before the EFIS buttons' labels.
    [Fact]
    public async Task A_tick_on_the_older_draw_still_refreshes_the_efis_buttons()
    {
        var older = A350Fixtures.Capture("efis0_anf_rose_01.json");
        var fresh = A350Fixtures.Capture("efis0_anf_rose_01.json");
        var rows = new StateRows();
        using var form = new A350NdWindow(_ => older, _ => Task.FromResult<A350DrawCapture?>(fresh), _ => false,
            new A350NdInput(_ => { }), new SpeechCapture(), (_, _, _) => { }, (_, _) => null, null, rows);
        await form.RefreshAsync(true);
        rows.Cstr = "On";
        await form.RefreshAsync(false);   // the monitor's latest is still the draw from before
        Assert.Equal("CSTR: On", form.EfisButtons[0].Text);
    }

    // Plan 26's review minor: the ND window differed from the other five (its list named "ND", its title, no taskbar entry).
    [Fact]
    public void It_is_named_and_shown_as_the_other_display_windows()
    {
        using var form = Window(new Rows());
        Assert.Equal("A350 ND", form.Text);
        Assert.True(form.ShowInTaskbar);
        Assert.Equal("ND display", form.Controls.OfType<MSFSBlindAssist.Forms.DisplayListBox>().Single().AccessibleName);
    }

    private static string[] Items(Form form) => form.Controls.OfType<MSFSBlindAssist.Forms.DisplayListBox>().Single().Items.Cast<string>().ToArray();

    // Re-review I1: a side switched while the fresh draw was on its way read the captain's draw as the first officer's ND, and Enter
    // then clicked the first officer's ND at the captain's places. A draw is its own side's.
    [Fact]
    public async Task A_draw_for_one_side_is_never_shown_for_the_other()
    {
        var captainsDraw = A350Fixtures.Capture("efis0_anf_rose_01.json");
        var firstOfficersLatest = A350Fixtures.Capture("efis1_aligned_stand.json");
        var draw = new TaskCompletionSource<A350DrawCapture?>();
        using var form = new A350NdWindow(side => side == 0 ? captainsDraw : firstOfficersLatest, _ => draw.Task, _ => false,
            new A350NdInput(_ => { }), new SpeechCapture(), (_, _, _) => { }, (_, _) => null, null, new Rows());
        var refresh = form.RefreshAsync(true);
        form.Controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Side").SelectedIndex = 1;
        draw.SetResult(captainsDraw);
        await refresh;
        Assert.Equal(A350NdWindow.PageLines(firstOfficersLatest, 1, false).Shown, Items(form));
    }

    // Re-review M10: presses close together read one fresh draw, after the last, as the display windows do.
    [Fact]
    public async Task Presses_close_together_read_one_fresh_draw()
    {
        var latest = A350Fixtures.Capture("efis0_anf_rose_01.json");
        int drawn = 0;
        var wait = new TaskCompletionSource();
        using var form = new A350NdWindow(_ => latest, _ => { drawn++; return Task.FromResult<A350DrawCapture?>(A350Fixtures.Capture("efis0_anf_rose_01.json")); },
            _ => false, new A350NdInput(_ => { }), new SpeechCapture(), (_, _, _) => { }, (_, _) => null, null, new Rows())
        { Wait = _ => wait.Task };
        var presses = Enumerable.Range(0, 5).Select(_ => form.AfterActionAsync()).ToList();
        wait.SetResult();
        await Task.WhenAll(presses);
        Assert.Equal(1, drawn);
    }

    // Re-review M10: a fresh read asked for while one runs is a fresh read after it, never a plain one the older draw then blocks.
    [Fact]
    public async Task A_fresh_read_asked_while_one_runs_follows_it_fresh()
    {
        var latest = A350Fixtures.Capture("efis0_anf_rose_01.json");
        int drawn = 0;
        var first = new TaskCompletionSource<A350DrawCapture?>();
        using var form = new A350NdWindow(_ => latest, _ => ++drawn == 1 ? first.Task : Task.FromResult<A350DrawCapture?>(A350Fixtures.Capture("efis0_anf_rose_01.json")),
            _ => false, new A350NdInput(_ => { }), new SpeechCapture(), (_, _, _) => { }, (_, _) => null, null, new Rows());
        var running = form.RefreshAsync(true);
        _ = form.RefreshAsync(true);   // asked while the first runs
        first.SetResult(A350Fixtures.Capture("efis0_anf_rose_01.json"));
        await running;
        for (int i = 0; i < 20 && drawn < 2; i++) await Task.Yield();
        Assert.Equal(2, drawn);
    }

    private static Task Run(A350NdWindow form, string method) =>
        (Task)typeof(A350NdWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;

    // Re-review M2: a BTV sequence works on the side it started on. A side switched while it runs (Clear BTV takes seconds) never has
    // its knobs turned: before, the steps after the switch took the first officer's ND and turned its knobs to the zoom.
    [Fact]
    public async Task A_btv_sequence_keeps_the_side_it_started_on()
    {
        var stand = A350Fixtures.Capture("efis0_fd_stand.json");   // no airport map: the LANDING tab never opens, the ND is put back
        var knobs = new List<int>();
        A350NdWindow? form = null;
        form = new A350NdWindow(_ => stand,
            _ =>
            {
                form!.Controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Side").SelectedIndex = 1;
                return Task.FromResult<A350DrawCapture?>(stand);
            },
            _ => false, new A350NdInput(_ => { }, (_, _) => Task.CompletedTask, (_, _, _, _) => Task.FromResult(true), null), new SpeechCapture(),
            (side, _, _) => knobs.Add(side), (_, _) => 3, null, new Rows()) { Wait = _ => Task.CompletedTask };
        try
        {
            await Run(form, "ClearBtvAsync");
            Assert.NotEmpty(knobs);
            Assert.All(knobs, side => Assert.Equal(0, side));
        }
        finally { form.Dispose(); }
    }

    // Review m2: a draw that fails says nothing of what the ND shows. A step looking without waiting tries again rather than take a line
    // for gone: with the functions panel open, one failed draw made Clear BTV click the Functions button (closing the panel) first.
    [Fact]
    public async Task A_failed_draw_is_never_taken_for_a_line_gone()
    {
        var landing = A350Fixtures.Capture("efis0_anf_landing.json");
        var torn = landing with { Error = "torn" };
        int draws = 0;
        var clicks = new List<(float X, float Y)>();
        using var form = new A350NdWindow(_ => landing, _ => Task.FromResult<A350DrawCapture?>(++draws == 2 ? torn : landing), _ => false,
            new A350NdInput(_ => { }, (_, _) => Task.CompletedTask, (_, x, y, _) => { clicks.Add((x, y)); return Task.FromResult(true); }, null),
            new SpeechCapture(), (_, _, _) => { }, (_, mode) => mode ? A350BtvArming.PlanMode : A350BtvArming.ZoomRange, null, new Rows())
        {
            Wait = _ => Task.CompletedTask,
        };
        await Run(form, "ClearBtvAsync");
        var field = A350NdPage.Read(landing, 0, A350BtvArming.PlanMode, A350BtvArming.ZoomRange)
            .First(l => l.Spoken.StartsWith("BTV EXIT AT:", StringComparison.Ordinal));
        Assert.Equal((field.ClickX, field.ClickY), clicks[0]);
    }

    // Re-review M2: a step finds what it clicks in a fresh draw only. With the fresh draw failing, the monitor's draw from before the step
    // (its LANDING tab, its BTV EXIT AT field) is never clicked: the step fails as the map's tab not opening.
    [Fact]
    public async Task A_btv_step_never_clicks_a_draw_from_before_it()
    {
        var landing = A350Fixtures.Capture("efis0_anf_landing.json");
        var torn = landing with { Error = "torn" };
        var clicks = new List<int>();
        var speech = new SpeechCapture();
        using var form = new A350NdWindow(_ => landing, _ => Task.FromResult<A350DrawCapture?>(torn), _ => false,
            new A350NdInput(_ => { }, (_, _) => Task.CompletedTask, (side, _, _, _) => { clicks.Add(side); return Task.FromResult(true); }, null),
            speech, (_, _, _) => { }, (_, mode) => mode ? A350BtvArming.PlanMode : A350BtvArming.ZoomRange, null, new Rows())
        {
            Wait = _ => Task.CompletedTask,
        };
        await Run(form, "ClearBtvAsync");
        Assert.Empty(clicks);
        Assert.Contains(speech.Interrupts, s => s.StartsWith("The map's LANDING tab did not open.", StringComparison.Ordinal));
    }
}
