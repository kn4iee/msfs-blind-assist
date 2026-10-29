using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the A350 says on its own. The switches are spoken by MainForm's generic path (the A380's convention, one
/// Ctrl+M row each); the lights here, as the cockpit lights them (<see cref="A350LampBoard"/>), at a batch end, netted
/// (<see cref="A350LampSpeech"/>), each light with its own Ctrl+M row. The master caution and warning speak once, as
/// they come on: the press that puts them out is the pilot's own, and the first officer's and the lower legends are
/// the same light.
/// </summary>
public partial class IniBuildsA350Definition
{
    /// <summary>How long a pressed or picked control's own light is left to its label (the screen reader reads the
    /// button's new state, [CORE-7]): the 1 Hz light delivery and the gather window (the iniBuilds A300's value).</summary>
    public const long PressedLampMs = 3000;

    /// <summary>How many batch ends after a reset (a connect, a flight load) the lights still unread are seeded from the
    /// cache: a load re-delivers only the values it changed, so without a seed a light's first real change would be
    /// taken for its baseline. Three batch ends (about 3 s) let the load's own deliveries come first.</summary>
    public const int SeedAfterBatches = 3;

    private int _seedCountdown = SeedAfterBatches;

    /// <summary>The ANN LT selector's row: its Ctrl+M row mutes the annunciator test's sentence.</summary>
    private const string AnnLtRow = "A350_LIGHTS_INT_ANN_LT";

    /// <summary>The two master lights that speak, and what they say.</summary>
    private static readonly Dictionary<string, string> MasterSpeakers = new(StringComparer.Ordinal)
    {
        ["A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT"] = "Master caution",
        ["A350_LT_FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT"] = "Master warning",
    };

    /// <summary>The other master legends: the same two lights, shown, never spoken (a side's two warning legends are one
    /// light, A350PanelLamps.SteadyRules).</summary>
    private static readonly HashSet<string> MasterEchoes = new(StringComparer.Ordinal)
    {
        "A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ2_LIGHT", "A350_LT_FCU_MASTER_CAUTION_RIGHT_SEQ1_LIGHT",
        "A350_LT_FCU_MASTER_CAUTION_RIGHT_SEQ2_LIGHT", "A350_LT_FCU_MASTER_WARNING_RIGHT_SEQ1_LIGHT",
    };

    /// <summary>A light that shows what another row speaks: the master lights' other legends, and the ECP page keys'
    /// lights, which follow the SD page the "System display page" row speaks (the A380's), each change at once.</summary>
    private static bool IsEcho(A350Lamp lamp) =>
        MasterEchoes.Contains(lamp.Key) || lamp.Rule.Inputs.Contains(SdPageVar, StringComparer.Ordinal);

    private const string SdPageVar = "INI_ECAM_ACTIVE_PAGE";

    /// <summary>Whether a light's Ctrl+M row is unticked; tests replace it.</summary>
    internal Func<string, bool> IsMuted { get; set; } =
        key => SettingsManager.Current.A350DisabledMonitorVariablesSet.Contains(key);

    private A350LampBoard _lampBoard = null!;
    private readonly A350LampSpeech _lampSpeech = new();
    private ScreenReaderAnnouncer? _lampAnnouncer;

    /// <summary>Until when each pressed or picked control's own lights are its label's (by control id).</summary>
    private readonly Dictionary<string, long> _ownLampsUntil = new(StringComparer.Ordinal);

    /// <summary>A delivery that changes several lights (a light power flag, a shared rule input) checks each light's
    /// own row here, so MainForm must not wrap it in one row's mute ([VAR-8]).</summary>
    public override bool IsMuteWrapExempt(string varName) =>
        _lampBoard.SharedInputKeys.Contains(varName) || varName is A350LeverSpeech.Lever1 or A350LeverSpeech.Lever2;

    /// <summary>The thrust levers and the speed brake speak as the A380's do (<see cref="A350LeverSpeech"/>). A thrust
    /// lever's delivery may speak the "All thrust levers" row's sentence, so its rows are exempt from MainForm's mute
    /// wrap and each sentence checks its own row; the All row's own delivery says nothing (the levers spoke). A lone
    /// lever's sentence waits for the other lever and is said at a batch end.</summary>
    private bool HandleLeverDelivery(string key, double value, ScreenReaderAnnouncer announcer)
    {
        switch (key)
        {
            case A350LeverSpeech.Lever1 or A350LeverSpeech.Lever2:
                NotePickReached(key, value);
                _leverAnnouncer = announcer;
                if (_leverSpeech.Thrust(key == A350LeverSpeech.Lever1 ? 1 : 2, value, Clock(), announcer.Suppressed) is { } thrust
                    && !announcer.Suppressed && !IsMuted(thrust.Row) && !_disposed)
                    announcer.Announce(thrust.Text);
                return true;
            case A350LeverSpeech.AllLevers:
                NotePickReached(key, value);
                return true;
            case SpeedBrakeKey:
                NotePickReached(key, value);
                if (_leverSpeech.SpeedBrake(value) is { } spoilers && !announcer.Suppressed && !IsMuted(key) && !_disposed)
                    announcer.Announce(spoilers);
                return true;
            default:
                return false;
        }
    }

    private const string SpeedBrakeKey = "A350_FCTL_SPD_BRK_LEVER";
    private readonly A350LeverSpeech _leverSpeech = new();
    private ScreenReaderAnnouncer? _leverAnnouncer;

    /// <summary>A pressed or picked control's lights are its label's for <see cref="PressedLampMs"/>.</summary>
    private void NoteOwnLamps(A350Control control) => _ownLampsUntil[control.Id] = Clock() + PressedLampMs;

    /// <summary>A light's input: feeds the board; each light it turns is kept for the batch end's sentence unless
    /// muted or the pilot's own. A master light going out is never said, but held as any light's off: MASTER WARN
    /// flashes, and a delivery caught dark must neither drop the pending "on" nor let the next lit one say it again.</summary>
    private void HandleLampDelivery(string key, double value, ScreenReaderAnnouncer announcer)
    {
        // ANN LT to TEST lights every light at once, and back to BRT or DIM returns them: the lights follow unspoken
        // (the switch's position is the screen reader's), and the test is said once, as the ANN LT row's. Every ANN LT
        // delivery goes this way: a first value at TEST (after a load, or arriving after the lights were decided) is a
        // baseline, never each light turning on.
        if (key == A350PanelLamps.AnnLtKey)
        {
            bool testing = _lampBoard.TestFlips(key, value) == true;
            foreach (var turn in _lampBoard.Update(key, value))
                _lampSpeech.Forget(MasterSpeakers.GetValueOrDefault(turn.Lamp.Key, turn.Lamp.Name));
            if (testing && !announcer.Suppressed && !IsMuted(AnnLtRow))
                announcer.Announce(A350LampCallouts.AnnunciatorTest);
            return;
        }
        if (_lampBoard.PowerFlips(key, value))
            _lampSpeech.NotePowerChange(Clock());
        foreach (var turn in _lampBoard.Update(key, value))
        {
            var lamp = turn.Lamp;
            string name = MasterSpeakers.TryGetValue(lamp.Key, out var master) ? master : lamp.Name;
            bool quiet = announcer.Suppressed || IsMuted(lamp.Key) || IsEcho(lamp)
                || (lamp.ControlId != null && _ownLampsUntil.TryGetValue(lamp.ControlId, out var until) && Clock() <= until);
            if (quiet)
            {
                _lampSpeech.Forget(name);   // the light moved on from any earlier change still waiting
                continue;
            }
            _lampSpeech.Add(new A350LampChange(name, turn.On), Clock(), silentOff: master != null);
            _lampAnnouncer = announcer;
        }
    }

    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        if (_seedCountdown > 0 && --_seedCountdown == 0 && _sim is { } sim)
        {
            foreach (var key in _lampBoard.InputKeys)
                if (Cached(sim, key) is double cached)
                    _lampBoard.Seed(key, cached);
            // The levers the same way, where their rows are offered: a load re-delivers only what it changed.
            if (_rows.ContainsKey(A350LeverSpeech.Lever1) && Cached(sim, A350LeverSpeech.Lever1) is double tla1)
                _leverSpeech.SeedThrust(1, tla1);
            if (_rows.ContainsKey(A350LeverSpeech.Lever2) && Cached(sim, A350LeverSpeech.Lever2) is double tla2)
                _leverSpeech.SeedThrust(2, tla2);
            if (_rows.ContainsKey(SpeedBrakeKey) && Cached(sim, SpeedBrakeKey) is double handle)
                _leverSpeech.SeedSpeedBrake(handle);
        }
        if (_lampSpeech.Flush(Clock()) is string sentence && !_disposed)
            _lampAnnouncer?.Announce(sentence);
        foreach (var (row, text) in _leverSpeech.Flush(Clock()))
            if (!IsMuted(row) && !_disposed)
                _leverAnnouncer?.Announce(text);
    }

    /// <summary>A disconnect or a flight load: every light's baseline goes, so the next value of each is silent; and
    /// what MSFSBA last commanded no longer stands for the cockpit (a held button's next press reads the cockpit).</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _commanded.Clear();
        _lampBoard.Reset();
        _lampSpeech.Clear();
        _leverSpeech.Reset();
        _ownLampsUntil.Clear();
        _seedCountdown = SeedAfterBatches;
    }
}
