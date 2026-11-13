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
        Assert.Equal(new[] { "ND", "Text: type, then Enter on a field", "ND mode", "ND range", "EFIS", "BTV controls", "Side" }, order);
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
}
