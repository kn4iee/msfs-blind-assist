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

    /// <summary>The two master lights that speak, and what they say.</summary>
    private static readonly Dictionary<string, string> MasterSpeakers = new(StringComparer.Ordinal)
    {
        ["A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT"] = "Master caution",
        ["A350_LT_FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT"] = "Master warning",
    };

    /// <summary>The other six master legends: the same two lights, shown, never spoken.</summary>
    private static readonly HashSet<string> MasterEchoes = new(StringComparer.Ordinal)
    {
        "A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ2_LIGHT", "A350_LT_FCU_MASTER_CAUTION_RIGHT_SEQ1_LIGHT",
        "A350_LT_FCU_MASTER_CAUTION_RIGHT_SEQ2_LIGHT", "A350_LT_FCU_MASTER_WARNING_LEFT_SEQ2_LIGHT",
        "A350_LT_FCU_MASTER_WARNING_RIGHT_SEQ1_LIGHT", "A350_LT_FCU_MASTER_WARNING_RIGHT_SEQ2_LIGHT",
    };

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
    public override bool IsMuteWrapExempt(string varName) => _lampBoard.SharedInputKeys.Contains(varName);

    /// <summary>A pressed or picked control's lights are its label's for <see cref="PressedLampMs"/>.</summary>
    private void NoteOwnLamps(A350Control control) => _ownLampsUntil[control.Id] = Clock() + PressedLampMs;

    /// <summary>A light's input: feeds the board; each light it turns is kept for the batch end's sentence unless
    /// muted, the pilot's own, or a master light going out.</summary>
    private void HandleLampDelivery(string key, double value, ScreenReaderAnnouncer announcer)
    {
        if (_lampBoard.PowerFlips(key, value))
            _lampSpeech.NotePowerChange(Clock());
        foreach (var turn in _lampBoard.Update(key, value))
        {
            var lamp = turn.Lamp;
            string name = MasterSpeakers.TryGetValue(lamp.Key, out var master) ? master : lamp.Name;
            bool quiet = announcer.Suppressed || IsMuted(lamp.Key) || MasterEchoes.Contains(lamp.Key)
                || (master != null && !turn.On)
                || (lamp.ControlId != null && _ownLampsUntil.TryGetValue(lamp.ControlId, out var until) && Clock() <= until);
            if (quiet)
            {
                _lampSpeech.Forget(name);   // the light moved on from any earlier change still waiting
                continue;
            }
            _lampSpeech.Add(new A350LampChange(name, turn.On), Clock());
            _lampAnnouncer = announcer;
        }
    }

    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        if (_lampSpeech.Flush(Clock()) is string sentence && !_disposed)
            _lampAnnouncer?.Announce(sentence);
    }

    /// <summary>A disconnect or a flight load: every light's baseline goes, so the next value of each is silent.</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _lampBoard.Reset();
        _lampSpeech.Clear();
        _ownLampsUntil.Clear();
    }
}
