// The 757's display registry (plan 23 Task 7): one list of the display readers' monitors, their Ctrl+M rows and their
// keys, filled on its first use, so Ctrl+M and the hotkeys see every reader before the readers start; a reader's key runs
// before anything else the definition does with it, and can hand the key to the AI read (S15).

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Bbs757DisplayRegistryTests
{
    private static readonly MonitorRow FmaRow = new("MSFSBA_BBS757_FMA", "Flight Mode Annunciator");

    [Fact]
    public void A_second_handler_for_one_key_throws()
    {
        var registry = new Bbs757DisplayRegistry();
        registry.OnKey(HotkeyAction.ReadDisplayUpperECAM, _ => true);

        Assert.Throws<InvalidOperationException>(() => registry.OnKey(HotkeyAction.ReadDisplayUpperECAM, _ => false));

        // The first handler stays; a key with none is not handled.
        Assert.True(registry.TryHandle(HotkeyAction.ReadDisplayUpperECAM, default));
        Assert.False(registry.TryHandle(HotkeyAction.ReadDisplayLowerECAM, default));
    }

    [Fact]
    public void Each_readers_gate_keys_are_recorded_once_in_registration_order()
    {
        var registry = new Bbs757DisplayRegistry();
        var fma = new BBS757Definition().Fma;

        registry.Add(fma, new[] { "L_A", "L_B" });
        registry.Add(fma);   // a reader with no gate variable
        registry.Add(fma, new[] { "L_B", "L_C" });

        Assert.Equal(new[] { "L_A", "L_B", "L_C" }, registry.GateKeys);
        Assert.Equal(3, registry.Monitors.Count);
    }

    [Fact]
    public void The_fma_registers_its_gate_keys()
    {
        var def = new BBS757Definition();

        Assert.Equal(Bbs757FmaDisplay.GateKeys, def.Displays.GateKeys.Take(Bbs757FmaDisplay.GateKeys.Count));
    }

    [Fact]
    public void Monitor_rows_list_every_reader_before_the_readers_start()
    {
        var def = new BBS757Definition();

        var rows = def.MonitorRows();

        Assert.Null(def.DisplaySession);   // nothing has started
        Assert.Contains(FmaRow, rows);
        Assert.Same(def.Fma, def.Displays.Monitors[0]);   // the FMA registers itself first, the seams after it
        Assert.All(def.Displays.Monitors, m => Assert.True(m.KeyFallsBackToAi()));   // not started: the AI read
        Assert.All(def.Displays.Rows, r => Assert.Contains(r, rows));
        Assert.Same(def.Displays, def.Displays);   // filled once
    }

    [Fact]
    public void Monitor_rows_end_with_the_flight_mode_annunciator()
    {
        var def = new BBS757Definition();

        var rows = def.MonitorRows();
        var readers = def.Displays.Rows;

        // The readers' rows come last, the FMA's first among them.
        Assert.Equal(FmaRow, readers[0]);
        Assert.Equal(readers, rows.Skip(rows.Count - readers.Count));
        Assert.Equal(FmaRow, rows[rows.Count - readers.Count]);
        // Every row once.
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_registered_key_runs_before_the_base_and_can_fall_back_to_ai()
    {
        var def = new BBS757Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var contexts = new List<Bbs757KeyContext>();
        bool handles = true;
        // The base answers this key itself ("Trim announcements off"), so a handler that runs first is heard by silence.
        def.Displays.OnKey(HotkeyAction.ToggleTrimAnnouncements, ctx => { contexts.Add(ctx); return handles; });
        def.Displays.OnKey(HotkeyAction.ReadDisplayUpperECAM, ctx => ctx.AiRead());

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ToggleTrimAnnouncements, sim, speech, null!, null!));

        Assert.Empty(speech.All);
        Assert.Empty(aiReads);
        var context = Assert.Single(contexts);
        Assert.Same(sim, context.Sim);
        Assert.Same(speech, context.Announcer);

        // A handler that does not take the key leaves it to the definition and the base.
        handles = false;
        Assert.True(def.HandleHotkeyAction(HotkeyAction.ToggleTrimAnnouncements, sim, speech, null!, null!));
        Assert.Single(speech.All);

        // The fallback is the AI read of the same key.
        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayUpperECAM, sim, speech, null!, null!));
        Assert.Equal(new[] { HotkeyAction.ReadDisplayUpperECAM }, aiReads);
        Assert.Single(speech.All);
    }
}
