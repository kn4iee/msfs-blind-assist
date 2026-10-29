using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

// Review 2026-10-29 (panels I3): "All thrust levers" read, planned and checked lever 1 only (its state variable is lever
// 1's angle, and it shared lever 1's commanded hold). With lever 1 at Climb and lever 2 at Idle a pick of All Climb sent
// nothing; Thrust lever 1 to TOGA then All to TOGA wrote nothing either; and lever 2 failing to move was never said:
// asymmetric thrust the pilot could not hear about, on a take-off roll for one. The All row plans from both levers (one
// position only while they agree), holds the pick on both, and reads both back.
public class A350AllThrustLeversTests
{
    private sealed class Pedestal : IniBuildsA350Definition
    {
        public Pedestal() : base(new[] { "Pedestal" }) { }
    }

    private readonly SpeechCapture _speech = new();
    private readonly List<string> _sent = new();
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _fresh = new(StringComparer.Ordinal);
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Pedestal _def;
    private long _now = 100_000;

    public A350AllThrustLeversTests()
    {
        _def = new Pedestal
        {
            CanLand = _ => true,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
            Delay = _ => Task.CompletedTask,
            IsMuted = _ => false,
            Clock = () => _now,
        };
        _def.Attach(_sim);
    }

    private void Pick(string key, double value) =>
        _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    /// <summary>The levers as the cache holds them: the All row streams lever 1's angle under its own key.</summary>
    private void Levers(double one, double two)
    {
        _cache[A350LeverSpeech.Lever1] = one;
        _cache[A350LeverSpeech.Lever2] = two;
        _cache[A350LeverSpeech.AllLevers] = one;
    }

    [Fact]
    public void With_the_levers_split_a_pick_of_all_moves_both()
    {
        Levers(25, 0);
        _fresh[A350LeverSpeech.Lever1] = 25;
        _fresh[A350LeverSpeech.Lever2] = 25;
        Pick(A350LeverSpeech.AllLevers, 25);
        Assert.Contains(_sent, rpn => rpn.Contains("(>L:INI_TLA1_REQUEST)") && rpn.Contains("(>L:INI_TLA2_REQUEST)"));
    }

    [Fact]
    public void A_pick_of_all_after_one_levers_own_still_moves_the_other()
    {
        Levers(0, 0);
        _fresh[A350LeverSpeech.Lever1] = 45;
        _fresh[A350LeverSpeech.Lever2] = 45;
        Pick(A350LeverSpeech.Lever1, 45);   // TOGA: lever 1 is held there
        _sent.Clear();
        _now += 1000;                       // inside the hold
        Pick(A350LeverSpeech.AllLevers, 45);
        Assert.Contains(_sent, rpn => rpn.Contains("(>L:INI_TLA2_REQUEST)"));
    }

    [Fact]
    public void A_lever_that_stays_put_is_named()
    {
        Levers(0, 0);
        _fresh[A350LeverSpeech.Lever1] = 45;
        _fresh[A350LeverSpeech.Lever2] = 0;   // lever 2 did not follow
        Pick(A350LeverSpeech.AllLevers, 45);
        Assert.Equal(new[] { "Thrust lever 2 did not move" }, _speech.All);
    }

    [Fact]
    public void Both_staying_put_is_all_of_them()
    {
        Levers(0, 0);
        _fresh[A350LeverSpeech.Lever1] = 0;
        _fresh[A350LeverSpeech.Lever2] = 0;
        Pick(A350LeverSpeech.AllLevers, 45);
        Assert.Equal(new[] { "All thrust levers did not move" }, _speech.All);
    }

    [Fact]
    public void Both_reaching_the_pick_say_nothing()
    {
        Levers(0, 0);
        _fresh[A350LeverSpeech.Lever1] = 45;
        _fresh[A350LeverSpeech.Lever2] = 45;
        Pick(A350LeverSpeech.AllLevers, 45);
        Assert.Empty(_speech.All);
    }
}
