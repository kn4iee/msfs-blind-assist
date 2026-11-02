using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Plan 22: an ATC uplink as MSFSBA says it with the mailbox window closed. Live 2026-11-02 at VCBI, SayIntentions answering
/// departure clearance requests: an uplink on the mailbox flashes INI_ATC_MSG_LIGHT, about 0.6 s lit and 0.6 s dark, until the
/// pilot closes or answers it, and the "atc_message" aural (INI_ATC_MSG_SOUND) rings 10 s after the first flash and every 11 s
/// after. The glareshield's two ATC MSG lights read that one variable, so they are one light that speaks
/// (A350AnnouncementTests.Lights_both_sides_show_from_one_rule_are_one_light).
/// </summary>
public class A350UplinkCalloutTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly IniBuildsA350Definition _def;
    private static readonly Lazy<IReadOnlyList<A350Lamp>> Everywhere = new(() =>
    {
        var map = A350ControlMap.Load();
        var placement = A350PanelLayout.Place(map);
        return A350PanelLamps.Resolve(map, placement, placement.Structure.Keys);
    });

    private static readonly A350Lamp Captain = Everywhere.Value.Single(l => l.Key == "A350_LT_FCU_ATC_MSG_LEFT_SEQ1_LIGHT");
    private static readonly A350Lamp FirstOfficer = Everywhere.Value.Single(l => l.Key == "A350_LT_FCU_ATC_MSG_RIGHT_SEQ1_LIGHT");

    public A350UplinkCalloutTests()
    {
        _def = new IniBuildsA350Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
            Cached = (_, _) => null,
            IsMuted = _ => false,
        };
        _def.Attach(new SimConnectManager(IntPtr.Zero));
        foreach (var lamp in new[] { Captain, FirstOfficer })
            foreach (var key in lamp.InputKeys)
                _def.ProcessSimVarUpdate(key, key == lamp.Key ? 0 : 1, _speech);
        _now += 5000;
        _def.OnContinuousBatchDelivered(0);
    }

    /// <summary>The light as sampled: lit at each start, dark at each end, in ms from the first flash.</summary>
    private void Fly(IEnumerable<(long On, long Off)> flashes, long untilMs)
    {
        var timeline = flashes.SelectMany(f => new[] { (At: f.On, Lit: 1.0), (At: f.Off, Lit: 0.0) }).OrderBy(c => c.At).ToList();
        long start = _now;
        double shown = 0;
        int next = 0;
        for (long t = 0; t <= untilMs; t += 200)   // a batch end every 200 ms, the light delivered when it changed
        {
            _now = start + t;
            double lit = shown;
            while (next < timeline.Count && timeline[next].At <= t)
                lit = timeline[next++].Lit;
            if (lit != shown)
            {
                _def.ProcessSimVarUpdate(Captain.Key, lit, _speech);
                _def.ProcessSimVarUpdate(FirstOfficer.Key, lit, _speech);
                shown = lit;
            }
            _def.OnContinuousBatchDelivered(0);
        }
    }

    /// <summary>A flashing run as the sampler took it: lit 560 ms, dark 600 ms.</summary>
    private static IEnumerable<(long On, long Off)> Flashing(long fromMs, long toMs)
    {
        for (long t = fromMs; t < toMs; t += 1160)
            yield return (t, t + 560);
    }

    private static string Spoken => "ATC message light";

    // Live 2026-11-02, the fourth request (sim 2103Z to 2110Z): the light flashed from the first uplink on (the clearance that
    // followed the status message did not light it again) until the status message was closed, 14.96 s into the second sample.
    [Fact]
    public void The_live_uplink_is_said_once_as_it_flashes_and_once_when_it_is_closed()
    {
        Assert.Equal("Captain ATC message light", Captain.Name);
        Fly(Flashing(0, 110_000), 110_000 + A350LampSpeech.OffHoldMs + 1000);
        Assert.Equal(new[] { Spoken + " on", Spoken + " off" }, _speech.All);
    }

    // The first exchange (2026-11-02 20:38Z and 20:39Z): the status and then the clearance, each answered before the next came, so
    // the light went dark between them: each is said.
    [Fact]
    public void Two_uplinks_answered_in_turn_are_each_said_in_order()
    {
        Fly(Flashing(0, 20_000).Concat(Flashing(60_000, 80_000)), 80_000 + A350LampSpeech.OffHoldMs + 1000);
        Assert.Equal(new[] { Spoken + " on", Spoken + " off", Spoken + " on", Spoken + " off" }, _speech.All);
    }
}
