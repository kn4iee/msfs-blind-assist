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

    /// <summary>When, after a reset (a connect, a flight load), the lights and levers still unread are seeded from the cache
    /// (a load re-delivers only the values it changed, so without a seed a light's first real change would be taken for its
    /// baseline): on evidence from the batch deliveries, the MD-11's gate: every batch delivered, a value of the A350's own
    /// changed, then every batch delivered QuietCycles times with nothing seedable moving, or 30 s after the first full
    /// cycle. Review 2026-10-29: three batch ends (about a second) seeded while the stream still repeated the last flight's
    /// values, which became the baselines, and each of the new flight's differences was spoken.</summary>
    private readonly MD11.Md11SeedGate _seedGate = new();

    /// <summary>The keys the seed fills: every light's inputs and the levers' rows.</summary>
    private HashSet<string> _seedKeys = new(StringComparer.Ordinal);

    /// <summary>The continuous batches registered; tests replace it.</summary>
    internal Func<SimConnect.SimConnectManager, IReadOnlyCollection<int>> ActiveBatches { get; set; } = sim => sim.ActiveContinuousBatches;

    /// <summary>What the cache holds for the seed's keys as the gate is armed: a redelivery of those is no change.</summary>
    private IEnumerable<KeyValuePair<string, double>> KnownSeedValues()
    {
        if (_sim is not { } sim) yield break;
        foreach (var key in _seedKeys)
            if (Cached(sim, key) is double value)
                yield return new KeyValuePair<string, double>(key, value);
    }

    /// <summary>Arms the seed; the definition arms it as it is built (a first connection) and on every reset.</summary>
    private void ArmSeed()
    {
        _seedKeys = _lampBoard.InputKeys.Concat(new[] { A350LeverSpeech.Lever1, A350LeverSpeech.Lever2, SpeedBrakeKey }.Where(_rows.ContainsKey))
            .ToHashSet(StringComparer.Ordinal);
        _seedGate.Arm(KnownSeedValues().ToList());
    }

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

    /// <summary>A light that shows what another row or light speaks: the master lights' other legends; the ECP page keys'
    /// lights, which follow the SD page the "System display page" row speaks (the A380's), each change at once; the second
    /// of lamps lit by one rule (<see cref="_twinEchoes"/>); a light mirroring a switch row (<see cref="_mirrorEchoes"/>).</summary>
    private bool IsEcho(A350Lamp lamp) =>
        MasterEchoes.Contains(lamp.Key) || lamp.Rule.Inputs.Contains(SdPageVar, StringComparer.Ordinal)
        || _twinEchoes.Contains(lamp.Key) || _mirrorEchoes.Contains(lamp.Key);

    /// <summary>The lamps after the first of those lit by one rule (ECAM CLR, ATC MSG, AUTOLAND on both sides: review
    /// 2026-10-29, "Captain ECAM clear light on. First officer ECAM clear light on."): the first is the light.</summary>
    private readonly HashSet<string> _twinEchoes = new(StringComparer.Ordinal);

    /// <summary>Lamps whose rule reads only a placed switch row's variable (an RMP's reception lights, the cockpit door's
    /// OPEN light, each display-cycle light): the row speaks the change, the light repeated it (review 2026-10-29).</summary>
    private readonly HashSet<string> _mirrorEchoes = new(StringComparer.Ordinal);

    /// <summary>The first of lamps lit by one rule: what it is called, without its side, and every button whose press
    /// makes it the pilot's own.</summary>
    private readonly Dictionary<string, (string Name, IReadOnlyList<string> Controls)> _twinLights = new(StringComparer.Ordinal);

    /// <summary>What a light is called when it speaks (and on its Ctrl+M row).</summary>
    private string SpokenName(A350Lamp lamp) =>
        MasterSpeakers.TryGetValue(lamp.Key, out var master) ? master
        : _twinLights.TryGetValue(lamp.Key, out var twin) ? twin.Name
        : lamp.Name;

    /// <summary>Works out the twins and the mirrors among the lamps (once, as the definition is built).</summary>
    private void FindEchoLamps()
    {
        foreach (var group in _lamps.Values.GroupBy(l => l.Rule.Source, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var lamps = group.ToList();
            var first = lamps[0];
            bool bothSides = lamps.Any(l => l.Name.StartsWith("Captain ", StringComparison.Ordinal))
                && lamps.Any(l => l.Name.StartsWith("First officer ", StringComparison.Ordinal));
            string name = bothSides && first.Name.StartsWith("Captain ", StringComparison.Ordinal)
                ? char.ToUpperInvariant(first.Name["Captain ".Length]) + first.Name[("Captain ".Length + 1)..]
                : first.Name;
            _twinLights[first.Key] = (name, lamps.Select(l => l.ControlId).OfType<string>().Distinct(StringComparer.Ordinal).ToList());
            foreach (var echo in lamps.Skip(1))
                _twinEchoes.Add(echo.Key);
        }
        var switchVars = _rows.Values
            .Where(r => r.Action == A350RowAction.Set && r.Control?.StateVar is { Length: > 2 } sv && sv[1] == ':')
            .Select(r => r.Control!.StateVar![2..]).ToHashSet(StringComparer.Ordinal);
        foreach (var lamp in _lamps.Values)
            if (lamp.Rule.Inputs.Count == 1 && switchVars.Contains(lamp.Rule.Inputs[0]))
                _mirrorEchoes.Add(lamp.Key);
    }

    /// <summary>Switch rows another row's variable drives, to the row that speaks for both, the first on the variable (both
    /// tiller disconnects flip one INI_TILLER_DISC: review 2026-10-29, a change elsewhere was said twice and a pick on one
    /// was said back by the other). A twin follows silently with no Ctrl+M row; the speaking row keeps quiet on a delivery
    /// its twin's pick made. The All thrust levers row is not one: its levers speak as one (HandleLeverDelivery).</summary>
    internal IReadOnlyDictionary<string, string> SetTwins => _setTwins;
    private readonly Dictionary<string, string> _setTwins = new(StringComparer.Ordinal);

    private void FindSetTwins()
    {
        var speakers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _rows.Values)
        {
            if (row.Action != A350RowAction.Set || row.Control is not { StateVar: { } stateVar } control
                || control.Kind == A350Kinds.Knob || row.Key == A350LeverSpeech.AllLevers)
                continue;
            if (speakers.TryGetValue(stateVar, out var speaker))
                _setTwins[row.Key] = speaker;
            else
                speakers[stateVar] = row.Key;
        }
    }

    /// <summary>A delivery to a speaking row that its twin's pending pick made: the pilot's own, said by the screen reader.</summary>
    private bool IsTwinPick(string key, double value) =>
        _setTwins.Any(t => t.Value == key && _pendingPicks.TryGetValue(t.Key, out var p)
            && Math.Abs(value - p.Target) < A350WritePlan.SameValueTolerance);

    /// <summary>The FCU windows that speak on their own (<see cref="A350FcuCallouts.Speaking"/>), by each cache key one of
    /// their inputs streams under; and per typed row, the speaking windows its value moves, whose change it confirms itself
    /// (parity task 9, 2026-10-29).</summary>
    private readonly Dictionary<string, List<A350FcuWindow>> _speakingByInputKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _typedEchoKeys = new(StringComparer.Ordinal);

    /// <summary>Each speaking window's last phrase, for the preset going out of view (<see cref="A350FcuCallouts.IsQuiet"/>).</summary>
    private readonly Dictionary<string, string> _windowPhrase = new(StringComparer.Ordinal);

    /// <summary>The cache key a window input streams under: its first under the window's own key.</summary>
    private static string WindowInputKey(A350FcuWindow window, string input) =>
        input == window.Inputs[0] ? window.Key : A350PanelLamps.InputKey(input);

    private void FindSpeakingWindows()
    {
        var speaking = _fcuWindows.Values.Where(w => A350FcuCallouts.Speaking.Contains(w.Name)).ToList();
        foreach (var window in speaking)
            foreach (var input in window.Inputs)
            {
                string key = WindowInputKey(window, input);
                if (!_speakingByInputKey.TryGetValue(key, out var windows))
                    _speakingByInputKey[key] = windows = new List<A350FcuWindow>();
                windows.Add(window);
            }
        foreach (var (rowKey, field) in _typed)
        {
            var inputs = A350TypedEntry.Inputs(field);
            var keys = speaking.Where(w => w.Inputs.Intersect(inputs, StringComparer.Ordinal).Any()).Select(w => w.Key).ToArray();
            if (keys.Length > 0)
                _typedEchoKeys[rowKey] = keys;
        }
    }

    /// <summary>A speaking window's input arrived: its phrase goes to the shared FCU call-out, which stages it to the batch
    /// end, absorbs a load's values and MSFSBA's own typed value, and is muted by the window's Ctrl+M row ([A320-37]). An
    /// input still unread is no phrase, never a baseline; the preset going out of view is recorded, never said.</summary>
    private void ObserveWindow(A350FcuWindow window, string deliveredKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (_sim is not { } sim)
            return;
        string? phrase = A350FcuCallouts.Phrase(window.Name, input =>
        {
            string key = WindowInputKey(window, input);
            return key == deliveredKey ? value : Cached(sim, key);
        });
        if (phrase == null)
            return;
        // A window reads several inputs and the call-out keeps one phrase per key: another input arriving with the same
        // words is no new sample, and passed on it would unstage the change the first one staged.
        if (_windowPhrase.TryGetValue(window.Key, out var last) && last == phrase)
            return;
        if (A350FcuCallouts.IsQuiet(last, phrase))
            RebaselineFcuValue(window.Key, phrase);
        else
            AnnounceFcuValue(window.Key, phrase, announcer, muted: IsMuted(window.Key));
        _windowPhrase[window.Key] = phrase;
    }

    /// <summary>MSFSBA's own typed value is said by its own confirmation: the windows it moves stay quiet for the echo's
    /// window, armed before each write of the run and again as it is confirmed (the shared FCU call-out's 2.5 s, as the iniBuilds A300's).</summary>
    private void ArmTypedEcho(string rowKey)
    {
        if (_typedEchoKeys.TryGetValue(rowKey, out var keys))
            SuppressFcuValueChangeEcho(keys);
    }

    /// <summary>MSFSBA's own press of a baro unit button: its label says the new unit, so the baro does not say it again.</summary>
    private void ArmUnitButtonEcho(A350Control control)
    {
        if (A350FcuCallouts.UnitButtonWindow(control.Id) is { } name && _fcuWindows.Values.FirstOrDefault(w => w.Name == name) is { } window)
            SuppressFcuValueChangeEcho(window.Key);
    }

    /// <summary>Encoders stepped with a value line, by value key, with their last step's time: the value they left is said
    /// once at the first batch end <see cref="A350FcuCallouts.StepSettleMs"/> after the last step (panels minor 3).</summary>
    private readonly Dictionary<string, long> _stepReadbacks = new(StringComparer.Ordinal);
    private ScreenReaderAnnouncer? _stepAnnouncer;

    private void ReleaseStepReadbacks()
    {
        long now = Clock();
        foreach (var (key, at) in _stepReadbacks.Where(s => now - s.Value >= A350FcuCallouts.StepSettleMs).ToList())
        {
            _stepReadbacks.Remove(key);
            if (_sim is { } sim && Cached(sim, key) is double value && !_disposed && _values.TryGetValue(key, out var line))
                _stepAnnouncer?.Announce($"{line.Name}: {A350Readouts.Format(line.Control, value)}");
        }
    }

    /// <summary>The IRS alignment call-out's Ctrl+M row (<see cref="A350IrsAlignment"/>), carried by the A350's own overall
    /// flag, consumed silently.</summary>
    public const string IrsAlignmentKey = "A350_IRS_ALIGNMENT";

    private readonly A350IrsAlignment _irs = new();
    private ScreenReaderAnnouncer? _irsAnnouncer;

    /// <summary>Whether the IRS panel is offered (its call-out with it).</summary>
    private bool HasIrsPanel => _rows.ContainsKey("A350_IRS_IR_1");

    /// <summary>Each IR on and aligned, as the batch just delivered left them; while the seed settles after a load, nothing
    /// is judged (the cache can hold the last flight's values), and the settled sample is the new baseline.</summary>
    private void ObserveIrs(bool seeded)
    {
        if (!HasIrsPanel || _sim is not { } sim)
            return;
        if (seeded)
            _irs.Reset();
        if (_seedGate.Armed)
            return;
        var irs = Enumerable.Range(1, 3).Select(n =>
            Cached(sim, $"A350_IRS_IR_{n}") is double on && Cached(sim, $"A350_STAT_IR{n}_ALIGNED") is double aligned
                ? ((bool On, bool Aligned)?)(on >= 0.5, aligned >= 0.5) : null).ToList();
        if (_irs.Observe(irs) is { } said && !IsMuted(IrsAlignmentKey) && !_disposed)
            _irsAnnouncer?.Announce(said);
    }

    /// <summary>Whether a light changed with the pilot's own press or pick of its button, or a twin's button.</summary>
    private bool IsOwnLamp(A350Lamp lamp)
    {
        IEnumerable<string> controls = _twinLights.TryGetValue(lamp.Key, out var twin) ? twin.Controls
            : lamp.ControlId is { } id ? new[] { id } : Array.Empty<string>();
        long now = Clock();
        return controls.Any(c => _ownLampsUntil.TryGetValue(c, out var until) && now <= until);
    }

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
    /// own row here, so MainForm must not wrap it in one row's mute ([VAR-8]); so does the take-off airspeed feed, whose
    /// calls are the V-speed rows'.</summary>
    public override bool IsMuteWrapExempt(string varName) =>
        _lampBoard.SharedInputKeys.Contains(varName) || varName is A350LeverSpeech.Lever1 or A350LeverSpeech.Lever2
        || varName == A350TakeoffCallouts.IasKey;

    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();
    private bool _calloutOnGround = true;

    public override string? TakeoffCalloutFeedKey => A350TakeoffCallouts.IasKey;

    /// <summary>The per-frame airspeed is needed on the ground and while armed only.</summary>
    public override bool TakeoffCalloutFeedNeeded => _takeoffCallouts.NeedsSamples(_calloutOnGround);

    /// <summary>The take-off roll call-outs (<see cref="A350TakeoffCallouts"/>), as the A380's: the airspeed feed is
    /// consumed and the calls one sample crosses are one interrupting sentence, each muted by its V-speed's row
    /// ([TKO-5]); SIM_ON_GROUND and the V-speeds are peeked, never consumed (the base says "On ground", the generic
    /// monitor "V1: 142 knots").</summary>
    private bool HandleTakeoffCallouts(string key, double value, ScreenReaderAnnouncer announcer)
    {
        if (key == A350TakeoffCallouts.IasKey)
        {
            var callouts = _takeoffCallouts.ProcessSample(value, _calloutOnGround);
            if (callouts.Count > 0 && !announcer.Suppressed && !_disposed
                && TakeoffVSpeedCallouts.Compose(callouts,
                    callout => A350TakeoffCallouts.Keys.MuteKeyFor(callout) is { Length: > 0 } row && IsMuted(row)) is { } sentence)
                announcer.AnnounceImmediate(sentence);
            return true;
        }
        if (key == "SIM_ON_GROUND")
            _calloutOnGround = value >= 0.5;
        else if (A350TakeoffCallouts.Keys.IsVSpeedKey(key))
            A350TakeoffCallouts.Keys.Feed(_takeoffCallouts, key, value);
        return false;
    }

    /// <summary>A reconnect: the roll call-outs' arm goes, never the speeds, which a reconnect need not deliver again.</summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _takeoffCallouts.Reset();
    }

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
                _lampSpeech.Forget(SpokenName(turn.Lamp));
            if (testing && !announcer.Suppressed && !IsMuted(AnnLtRow))
                announcer.Announce(A350LampCallouts.AnnunciatorTest);
            return;
        }
        if (_lampBoard.PowerFlips(key, value))
            _lampSpeech.NotePowerChange(Clock());
        foreach (var turn in _lampBoard.Update(key, value))
        {
            var lamp = turn.Lamp;
            string name = SpokenName(lamp);
            bool master = MasterSpeakers.ContainsKey(lamp.Key);
            // A master light's press only ever puts it out (its off is silent anyway): lit again inside the press's own
            // window, it is the next caution or warning, never the pilot's own (review 2026-10-29).
            bool own = IsOwnLamp(lamp) && !(master && turn.On);
            bool quiet = announcer.Suppressed || IsMuted(lamp.Key) || IsEcho(lamp) || own;
            if (quiet)
            {
                _lampSpeech.Forget(name);   // the light moved on from any earlier change still waiting
                continue;
            }
            // Both master lamps are steady (MASTER WARN's rule holds while its warning is active, its legends flashing): lit
            // again after an off, each is a new "on".
            _lampSpeech.Add(new A350LampChange(name, turn.On), Clock(), silentOff: master, steady: master);
            _lampAnnouncer = announcer;
        }
    }

    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        bool seeded = false;
        if (_seedGate.Armed && _sim is { } sim && _seedGate.OnBatchDelivered(batchNum, ActiveBatches(sim), Clock()) != MD11.Md11SeedTrigger.None)
        {
            seeded = true;
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
        ReleaseStepReadbacks();
        foreach (var (row, text) in _leverSpeech.Flush(Clock()))
            if (!IsMuted(row) && !_disposed)
                _leverAnnouncer?.Announce(text);
        ObserveIrs(seeded);
    }

    /// <summary>A disconnect or a flight load: every light's baseline goes, so the next value of each is silent; what
    /// MSFSBA last commanded no longer stands for the cockpit (a held button's next press reads the cockpit); and the roll
    /// call-outs' arm goes (a load delivers the per-frame airspeed before SIM_ON_GROUND: an arm kept from the stand would
    /// call all three at a loaded cruise).</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _takeoffCallouts.Reset();
        _commanded.Clear();
        _lampBoard.Reset();
        _lampSpeech.Clear();
        _leverSpeech.Reset();
        _ownLampsUntil.Clear();
        _stepReadbacks.Clear();   // a step made before the load is not the new flight's value
        _irs.Reset();
        ArmSeed();
        // The display call-outs too: the new flight's E/WD and FMA are its baseline (review 2026-10-29).
        Ecam?.Rebaseline();
        Pfd?.Rebaseline();
    }
}
