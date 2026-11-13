using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26 Task 3: the PFD window (Alt+P), as the PMDG 737's: the side's PFD live, then the controls that change it, each
/// the EFIS or FCU panel's own row: FD, LS, VV, the baro knob's STD push and QNH pull, its unit, and the typed QNH.</summary>
public class A350PfdWindowTests
{
    [Fact]
    public void Its_keys_are_the_sides_efis_rows_and_the_fcus_fd()
    {
        // The knob's push and pull as the panel names them (re-review M14: "Baro STD: QNH" read as a contradiction).
        Assert.Equal(new[] { "FD", "LS", "VV", "Baro STD (push)", "Baro QNH (pull)", "Baro unit" }, A350PfdWindow.Keys.Select(k => k.Label));
        Assert.Equal(new[] { "A350_FCU_FD", "A350_EFIS_LS_LEFT", "A350_EFIS_VV_LEFT", "A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH",
            "A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", "A350_EFIS_QNH_UNIT_SELECTOR_LEFT" }, A350PfdWindow.Keys.Select(k => k.Row(0)));
        Assert.Equal(new[] { "A350_FCU_FD", "A350_EFIS_LS_RIGHT", "A350_EFIS_VV_RIGHT", "A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH",
            "A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL", "A350_EFIS_QNH_UNIT_SELECTOR_RIGHT" }, A350PfdWindow.Keys.Select(k => k.Row(1)));
        Assert.Equal(new[] { true, true, true, false, false, false }, A350PfdWindow.Keys.Select(k => k.Toggle));
        Assert.Equal(("A350_CAPT_QNH_SET", "A350_FO_QNH_SET"), (A350PfdWindow.QnhRow(0), A350PfdWindow.QnhRow(1)));
    }

    // A typo in a row key would be a button that presses nothing: every row the window presses is one of the aircraft's.
    [Fact]
    public void Every_row_it_presses_is_a_row_of_the_aircraft()
    {
        var rows = new IniBuildsA350Definition().GetVariables();
        foreach (int side in new[] { 0, 1 })
        {
            Assert.All(A350PfdWindow.Keys, k => Assert.True(rows.ContainsKey(k.Row(side)), k.Row(side)));
            Assert.True(rows.ContainsKey(A350PfdWindow.QnhRow(side)), A350PfdWindow.QnhRow(side));
        }
    }

    [Fact]
    public void Alt_p_opens_the_pfd_window() => Assert.Equal("PFD", A350Hotkeys.DisplayWindows[HotkeyAction.ReadDisplayPFD]);

    // The feed reads each side's PFD as its status display reads it, and each side's screen dark by its own power.
    [Fact]
    public void Its_feed_reads_each_sides_pfd_and_its_own_power()
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var def = new IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnect.SimConnectManager(IntPtr.Zero));
        var feed = def.DisplayFeed("Captain PFD", "First Officer PFD");
        var captain = A350Fixtures.Capture("efis0_climb.json");
        var firstOfficer = A350Fixtures.Capture("efis1_fd_stand.json");
        Assert.Equal(A350Pfd.Read(captain, 0), feed.Read(captain, 0));
        Assert.Equal(A350Pfd.Read(firstOfficer, 1), feed.Read(firstOfficer, 1));
        cache[def.KeyForInput("INI_LEFT_EFIS_POWERED")] = 0;
        cache[def.KeyForInput("INI_RIGHT_EFIS_POWERED")] = 1;
        Assert.True(feed.Dark(0));
        Assert.False(feed.Dark(1));
    }

    private sealed class Rows : IA350WindowRows
    {
        public readonly List<string> Log = new();
        public string? State(string row) => null;
        public void Press(string row, double value = 1) => Log.Add($"press {row}");
        public void Toggle(string row) => Log.Add($"toggle {row}");
        public void Type(string row, string text) => Log.Add($"type {row} {text}");
    }

    // The QNH box applies on Enter through the side's typed row, as the panel's field does.
    [Fact]
    public void The_qnh_box_types_the_sides_row_on_enter()
    {
        var rows = new Rows();
        var feed = new A350DisplayFeed(_ => null, _ => Task.FromResult<A350DrawCapture?>(null), _ => false, (_, _) => Array.Empty<string>());
        using var form = new A350PfdWindow(feed, rows);
        var box = form.Controls.OfType<Control>().SelectMany(c => c.Controls.Cast<Control>().Prepend(c)).OfType<A350TypedBox>().Single();
        Assert.Equal("QNH", box.AccessibleName);
        box.Text = "1013";
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, new object[] { new KeyEventArgs(Keys.Enter) });
        Assert.Equal(new[] { "type A350_CAPT_QNH_SET 1013" }, rows.Log);
    }

    // Re-review M15: each window's row of keys is named.
    [Fact]
    public void Every_display_windows_key_row_is_named()
    {
        var rows = new Rows();
        var feed = new A350DisplayFeed(_ => null, _ => Task.FromResult<A350DrawCapture?>(null), _ => false, (_, _) => Array.Empty<string>());
        string Name(Form form) => form.Controls.OfType<A350KeyRow>().Single().AccessibleName ?? "";
        using var pfd = new A350PfdWindow(feed, rows);
        using var ewd = new A350EwdWindow(feed, rows);
        using var sd = new A350SdWindow(feed, rows);
        using var isis = new A350IsisWindow(feed, rows);
        using var snd = new A350SndWindow(feed, rows);
        Assert.Equal(new[] { "EFIS", "ECAM keys", "SD page keys", "ISIS keys", "SND keys" }, new Form[] { pfd, ewd, sd, isis, snd }.Select(Name));
    }
}
