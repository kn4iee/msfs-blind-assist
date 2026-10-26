using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using A = MSFSBlindAssist.Aircraft.BBS757.Bbs757AutoflightAnnouncements;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's autoflight call-outs (gap plan 05, Task 4): MCP values spoken once they rest, A/T arm and the
/// disconnect lights. Deliveries go through the definition's ProcessSimVarUpdate as the 1 Hz batch makes
/// them; the release is asked directly with the clock (<c>ReleaseRestedAutoflight</c>), so no timer runs.
/// </summary>
public class Bbs757AutoflightAnnouncementsTests
{
    private sealed class Rig
    {
        public long Now;
        public readonly HashSet<string> Muted = new(StringComparer.Ordinal);
        public readonly SpeechCapture Speech = new();
        public readonly BBS757Definition Def;

        public Rig()
        {
            Def = new BBS757Definition
            {
                NestRunning = () => true,
                NowMs = () => Now,
                IsMonitorMuted = key => Muted.Contains(key),
                UseRestTimer = false,
            };
        }

        public void At(long ms, string key, double value)
        {
            Now = ms;
            Def.ProcessSimVarUpdate(key, value, Speech);
        }

        public void Release(long ms)
        {
            Now = ms;
            Def.ReleaseRestedAutoflight(ms, Speech);
        }
    }

    [Fact]
    public void First_reading_is_silent()
    {
        var rig = new Rig();

        rig.At(0, A.HeadingWatchKey, 90);
        rig.Release(10_000);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Heading_spoken_once_it_rests_without_another_delivery()
    {
        var rig = new Rig();
        rig.At(0, A.HeadingWatchKey, 80);   // baseline
        rig.At(1000, A.HeadingWatchKey, 90);

        rig.Release(2499);
        Assert.Empty(rig.Speech.All);
        rig.Release(2500);
        Assert.Equal(new[] { "MCP heading 090" }, rig.Speech.All);
        rig.Release(9000);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public void Spin_speaks_only_the_last_value()
    {
        // 090, 095, 100 delivered 1 s apart, then nothing: one "MCP heading 100" 1500 ms after the last.
        var rig = new Rig();
        rig.At(0, A.HeadingWatchKey, 85);
        rig.At(1000, A.HeadingWatchKey, 90);
        rig.Release(1250);
        rig.At(2000, A.HeadingWatchKey, 95);
        rig.Release(2250);
        rig.Release(2750);
        rig.At(3000, A.HeadingWatchKey, 100);
        rig.Release(3250);
        rig.Release(4499);
        Assert.Empty(rig.Speech.All);

        rig.Release(4500);

        Assert.Equal(new[] { "MCP heading 100" }, rig.Speech.All);
    }

    [Fact]
    public void A_value_spun_away_and_back_says_nothing()
    {
        var rig = new Rig();
        rig.At(0, A.AltitudeWatchKey, 10000);
        rig.At(1000, A.AltitudeWatchKey, 11000);
        rig.At(2000, A.AltitudeWatchKey, 10000);

        rig.Release(5000);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Muted_row_is_dropped_when_it_rests()
    {
        var rig = new Rig();
        rig.Muted.Add(A.HeadingWatchKey);
        rig.At(0, A.HeadingWatchKey, 80);
        rig.At(1000, A.HeadingWatchKey, 90);

        rig.Release(5000);

        Assert.Empty(rig.Speech.All);
        rig.Muted.Clear();
        rig.Release(9000);
        Assert.Empty(rig.Speech.All);   // dropped, not held for later
    }

    [Fact]
    public async Task Own_set_is_not_repeated()
    {
        // The app's own Ctrl+H set says "Heading 090" once; the row's deliveries during and after it add nothing.
        var rig = new Rig();
        double heading = 0;
        var io = new McpIo
        {
            Read = (_, _) => Task.FromResult<double?>(heading),
            Write = _ => { heading = 90; rig.At(rig.Now, A.HeadingWatchKey, 90); },
            Delay = (ms, _) => { rig.Now += ms; return Task.CompletedTask; },
        };
        var def = new BBS757Definition
        {
            NestRunning = () => true, NowMs = () => rig.Now, UseRestTimer = false,
            CanWrite = _ => true, McpSetIo = _ => io,
        };
        def.ProcessSimVarUpdate(A.HeadingWatchKey, 0, rig.Speech);   // baseline

        await def.SetMcpHeadingAsync(90, new SimConnectManager(IntPtr.Zero), rig.Speech);
        def.ProcessSimVarUpdate(A.HeadingWatchKey, 90, rig.Speech);  // the batch delivers it once more, late
        def.ReleaseRestedAutoflight(rig.Now + 10_000, rig.Speech);

        Assert.Equal(new[] { "Heading 090" }, rig.Speech.All);
    }

    [Fact]
    public void Speed_silent_while_blank()
    {
        var rig = new Rig();
        rig.At(0, Bbs757Mcp.IasBlankedVar, 0);
        rig.At(0, A.ModeWatchKey, 0);
        rig.At(0, A.SpeedWatchKey, 250);
        rig.At(1000, Bbs757Mcp.IasBlankedVar, 1);
        rig.At(1000, A.SpeedWatchKey, 286.69);   // VNAV's own target behind the blank window
        rig.At(2000, A.SpeedWatchKey, 287.2);

        rig.Release(10_000);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Speed_spoken_when_intervene_opens_the_window_on_a_new_speed()
    {
        var rig = new Rig();
        rig.At(0, Bbs757Mcp.IasBlankedVar, 1);
        rig.At(0, A.ModeWatchKey, 0);
        rig.At(0, A.SpeedWatchKey, 283);
        rig.At(1000, Bbs757Mcp.IasBlankedVar, 0);
        rig.At(1000, A.SpeedWatchKey, 287);

        rig.Release(3000);

        Assert.Equal(new[] { "MCP speed 287" }, rig.Speech.All);
    }

    [Fact]
    public void Mach_only_in_mach_mode()
    {
        var rig = new Rig();
        rig.At(0, Bbs757Mcp.IasBlankedVar, 0);
        rig.At(0, A.ModeWatchKey, 0);
        rig.At(0, A.MachTenthsWatchKey, 20);   // in IAS the wheels show knots: 283
        rig.At(0, A.MachWatchKey, 90);
        rig.At(1000, A.MachWatchKey, 100);
        rig.Release(4000);
        Assert.Empty(rig.Speech.All);

        rig.At(5000, A.ModeWatchKey, 1);
        rig.At(5000, A.MachTenthsWatchKey, 70);
        rig.At(5000, A.MachWatchKey, 40);      // .73: the first Mach is a baseline
        rig.At(6000, A.MachWatchKey, 50);      // .74
        rig.Release(8000);

        Assert.Equal(new[] { "MCP Mach 0.74" }, rig.Speech.All);
    }

    [Fact]
    public void Speed_in_mach_mode_is_the_machs_equivalent_and_silent()
    {
        var rig = new Rig();
        rig.At(0, Bbs757Mcp.IasBlankedVar, 0);
        rig.At(0, A.ModeWatchKey, 1);
        rig.At(0, A.SpeedWatchKey, 281.1);
        rig.At(1000, A.SpeedWatchKey, 285.3);

        rig.Release(5000);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Disconnect_lights()
    {
        var rig = new Rig();
        rig.At(0, A.ApDiscLightKey, 0);
        rig.At(0, A.AtDiscLightKey, 0);
        rig.At(1000, A.ApDiscLightKey, 1);
        rig.At(2000, A.ApDiscLightKey, 0);
        rig.At(3000, A.AtDiscLightKey, 1);
        rig.At(4000, A.ApDiscLightKey, 1);

        Assert.Equal(new[] { "Autopilot disconnect", "Autothrottle disconnect", "Autopilot disconnect" }, rig.Speech.All);
    }

    [Fact]
    public void A_T_armed_phrases()
    {
        var rig = new Rig();
        rig.At(0, A.AtArmedKey, 1);
        rig.At(1000, A.AtArmedKey, 0);
        rig.At(2000, A.AtArmedKey, 1);

        Assert.Equal(new[] { "A/T disarmed", "A/T armed" }, rig.Speech.All);
        Assert.Null(A.AtArmedPhrase(double.NaN));
    }

    [Fact]
    public void A_reset_makes_the_next_values_baselines()
    {
        var rig = new Rig();
        rig.At(0, A.HeadingWatchKey, 80);
        rig.At(1000, A.HeadingWatchKey, 90);
        rig.Def.OnSimContextReset();   // a flight load
        rig.At(1500, A.HeadingWatchKey, 270);

        rig.Release(10_000);

        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Phrases_invariant_under_de_DE(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("MCP heading 090", A.HeadingPhrase(90.4));
            Assert.Equal("MCP heading 000", A.HeadingPhrase(360));
            Assert.Equal("MCP altitude 12000", A.AltitudePhrase(12000));
            Assert.Equal("MCP vertical speed minus 1500", A.VsPhrase(-1500));
            Assert.Equal("MCP vertical speed 1500", A.VsPhrase(1500));
            Assert.Equal("MCP vertical speed 0", A.VsPhrase(-0.4));
            Assert.Equal("MCP speed 250", A.SpeedPhrase(250.4));
            Assert.Equal("MCP Mach 0.78", A.MachPhrase(0.78));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Rows_are_announced_batched_and_muteable_except_the_two_silent_ones()
    {
        var vars = new BBS757Definition().GetVariables();
        var expected = new Dictionary<string, (string Name, string Label)>
        {
            [A.HeadingWatchKey] = ("BBS_AP_TARGET_HEADING", "MCP Heading"),
            [A.AltitudeWatchKey] = ("BBS_AP_TARGET_ALTITUDE", "MCP Altitude"),
            [A.VsWatchKey] = ("BBS_AP_TARGET_VS", "MCP Vertical Speed"),
            [A.SpeedWatchKey] = ("BBS757_ORANGE_AIRSPEED_BUGS", "MCP Speed"),
            [A.MachWatchKey] = ("B757_MCP_Gau_SPD_AUTO_10", "MCP Mach"),
            [A.AtArmedKey] = ("BBS_AUTO_THROTTLE", "A/T Armed"),
            [A.ApDiscLightKey] = ("B757_MAIN_APDISC_1_DECAL", "Autopilot Disconnect"),
            [A.AtDiscLightKey] = ("B757_MAIN_ATDISC_1_DECAL", "Autothrottle Disconnect"),
        };
        foreach (var (key, (name, label)) in expected)
        {
            var def = vars[key];
            Assert.Equal(name, def.Name);
            Assert.Equal(label, def.DisplayName);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced, key);
            Assert.False(def.ExcludeFromBatch, key);
            Assert.False(def.ExcludeFromMonitorManager, key);
        }
        foreach (string silent in new[] { A.ModeWatchKey, A.MachTenthsWatchKey })
        {
            Assert.Equal(UpdateFrequency.Continuous, vars[silent].UpdateFrequency);
            Assert.True(vars[silent].IsAnnounced, silent);          // only an announced row joins the batch
            Assert.True(vars[silent].ExcludeFromMonitorManager, silent);
        }
        Assert.Equal("BBS_AIRSPEED_IN_MACH", vars[A.ModeWatchKey].Name);
        Assert.Equal("B757_MCP_Gau_SPD_AUTO_100", vars[A.MachTenthsWatchKey].Name);

        var rows = new BBS757Definition().MonitorRows().Select(r => r.Key).ToHashSet();
        Assert.Superset(expected.Keys.ToHashSet(), rows);
    }

    [Fact]
    public void The_on_request_keys_the_sets_read_stay()
    {
        // The sets' fresh reads use the OnRequest keys; the watch rows share their Name ([VAR-7] allows one Continuous).
        var vars = new BBS757Definition().GetVariables();
        foreach (string key in new[] { Bbs757Mcp.HeadingVar, Bbs757Mcp.SpeedVar, Bbs757Mcp.MachModeVar, Bbs757Mcp.MachTenthsDrumVar })
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
    }
}
