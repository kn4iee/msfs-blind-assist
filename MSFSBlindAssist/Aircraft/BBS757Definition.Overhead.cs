using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The overhead panels' writes: plan 03's guarded controls (the emergency lights, the stab trim
/// cutouts), the fire handle unlocks' relock and the Pressurization panel's typed landing altitude,
/// and later plan 12's overhead panels, which join the seams at the end of this file. A guarded
/// control is any map control whose <see cref="Bbs757Control.Guard"/> names another map control
/// (BlueBird's guard cover): it moves only with the cover open, so a pick is planned from fresh
/// reads of both by <see cref="Bbs757Guarded.Plan"/> and sent here.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>
    /// A pick on a guarded control: settle, refuse aloud if nothing can land, read the control and its
    /// guard fresh, send what <see cref="Bbs757Guarded.Plan"/> plans (the guard's press and the state
    /// write, <see cref="Bbs757Guarded.StepGapMs"/> apart), then read the control back. It follows
    /// <see cref="SetPositionAsync"/>: a newer pick of the same control supersedes this one at any
    /// await before its first call, and both rows are re-read at the end so their combos show where
    /// the aircraft really is.
    ///
    /// Once the first call has gone, a NEWER PICK of the same control does not stop the sequence: a
    /// half-run one would leave the guard open with no state write behind it (the gaps are awaited on
    /// the profile's lifetime, not on this pick's own token). The newer pick still cancels this one's
    /// read-back, so only the newer pick speaks. A flight load or a dropped connection
    /// (<see cref="CancelPendingPicks"/>, seen through <see cref="_pickEpoch"/>) and a profile switch
    /// do stop it: after each gap it sends nothing more, says nothing and re-reads nothing, so no
    /// write reaches the flight that follows.
    ///
    /// Speech follows CORE-7: the screen reader read the combo, so a pick that lands says nothing and
    /// one that does not says "{label} did not move" once (an unreadable read-back counts as not
    /// landed); a pick that cannot be planned or written says "{label} unavailable" and sends nothing.
    ///
    /// True when it wrote and waited out the read-back delay; false when it refused, found the control
    /// already there, or was superseded, dropped or switched away. Never throws.
    /// </summary>
    internal async Task<bool> SetGuardedAsync(Bbs757Control control, Bbs757Control guard, double target,
        Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        int epoch = Volatile.Read(ref _pickEpoch);   // before the pick is entered: a drop from here on is this pick's
        var cts = BeginPick(control.Node);
        if (cts == null) return false;   // switched away: nothing is written
        var ct = cts.Token;
        bool resync = false, wrote = false;

        try
        {
            if (!await SettleAndCheckWriteAsync(control, io, announcer, ct))
            {
                resync = true;
                return false;
            }

            // Both at once: an unread variable then costs one read timeout, not two.
            var reads = await Task.WhenAll(io.ReadFresh(control.Node, ct), io.ReadFresh(guard.Node, ct));
            ct.ThrowIfCancellationRequested();
            double? current = reads[0], guardState = reads[1];

            var plan = Bbs757Guarded.Plan(control, guard, current, guardState, target);
            if (plan.Refused)
            {
                Log.Debug("BBS757", $"{control.Node}: read {GuardedLogValue(current)}, guard {guard.Node} read {GuardedLogValue(guardState)}, " +
                    $"target {GuardedLogValue(target)}: no safe write.");
                SayUnavailable(control, announcer);
                resync = true;
                return false;
            }
            if (plan.Calls.Count == 0) return false;   // already there, guard where the pick needs it

            OnOwnWrite(control.Node);   // plan 13
            OnOwnWrite(guard.Node);
            for (int i = 0; i < plan.Calls.Count; i++)
            {
                if (i > 0)
                {
                    // The first call has gone: finish even if a newer pick has superseded this one
                    // (its token is not awaited here), but not across a flight load or a profile
                    // switch, whichever lands before or during the gap.
                    if (SequenceDropped(epoch)) return false;
                    await io.Delay(Bbs757Guarded.StepGapMs, _lifetime.Token);
                    if (SequenceDropped(epoch)) return false;
                }
                io.Write(plan.Calls[i]);
            }

            await io.Delay(ReadBackDelayMs, ct);
            resync = true;
            wrote = true;

            double? after = await io.ReadFresh(control.Node, ct);
            ct.ThrowIfCancellationRequested();
            if (!LandedOnPick(control, after, target)) announcer.Announce($"{control.Label} did not move");
        }
        catch (OperationCanceledException) { /* superseded by a newer pick, a reload, or switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: guarded write failed: {ex.Message}");
            resync = true;   // whatever was or was not sent, the combos should show the aircraft's position
        }
        finally
        {
            if (EndPick(control.Node, cts) && resync)
            {
                io.RequestRead(control.Node);
                io.RequestRead(guard.Node);
            }
        }
        return wrote;
    }

    /// <summary>
    /// True when a guarded sequence that has sent its first call must send nothing more: the pilot
    /// switched away from the profile, or a flight load or dropped connection dropped the pick
    /// (<see cref="_pickEpoch"/> moved since <paramref name="epoch"/> was read). A newer pick of the
    /// same control is neither, so it never ends a sequence.
    /// </summary>
    private bool SequenceDropped(int epoch)
        => _lifetime.IsCancellationRequested || epoch != Volatile.Read(ref _pickEpoch);

    /// <summary>True when <paramref name="reading"/> is a number holding the position <paramref name="target"/> (within the read-back slack).</summary>
    private static bool LandedOnPick(Bbs757Control control, double? reading, double target)
    {
        if (reading is not double value || !double.IsFinite(value)) return false;
        int to = Bbs757Rpn.PositionIndex(control, target);
        return to >= 0 && Bbs757Rpn.PositionIndex(control, value) == to;
    }

    private static string GuardedLogValue(double? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing";

    // =================================================================================
    // Fire handle unlocks
    // =================================================================================

    /// <summary>
    /// Called by <see cref="PressAsync"/> right after a button's press has been sent: when the button is
    /// a fire handle unlock (<see cref="Bbs757Write.RelockAfterUnlock"/>), starts the relock that
    /// BlueBird's own <c>O:</c> timer would have made in the cockpit and the calculator press cannot
    /// start. Any other control starts nothing. Never throws.
    /// </summary>
    private void RelockIfUnlock(Bbs757Control control, Bbs757SimIo io)
    {
        string? relock = Bbs757Write.RelockAfterUnlock(control, out int delayMs);
        if (relock == null) return;
        _ = RelockAfterDelayAsync(control, relock, delayMs, io);   // never throws: it catches and logs
    }

    /// <summary>
    /// Waits <paramref name="delayMs"/> on the profile's lifetime, then writes <paramref name="relock"/>.
    /// Switching to another profile ends the wait and sends nothing: the handle then stays as the
    /// cockpit would leave it. Nothing is spoken: the press already said "{label} pressed", and the
    /// relock is the cockpit's own behaviour. The awaits keep the UI context (no ConfigureAwait(false)),
    /// like every write here.
    ///
    /// It does not consult <see cref="_pickEpoch"/>, as a guarded sequence does: the write is the
    /// cockpit's resting value (0, locked), so it is safe across a flight load or a reconnect, and
    /// skipping it there would leave the handle unlocked, the defect this relock exists to prevent.
    /// </summary>
    private async Task RelockAfterDelayAsync(Bbs757Control control, string relock, int delayMs, Bbs757SimIo io)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;   // the source is disposed once switched away: its token must not be touched
            await io.Delay(delayMs, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;   // switched away as the wait ended
            OnOwnWrite(control.Node);   // plan 13
            io.Write(relock);
        }
        catch (OperationCanceledException) { /* switched away: the handle stays as the cockpit would leave it */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: relock failed: {ex.Message}");
        }
    }

    // =================================================================================
    // The Pressurization panel's landing altitude
    // =================================================================================

    /// <summary>The map's panel with the pressurization controls (Mode Select, Equip), which gains the landing altitude field.</summary>
    internal const string PressurizationPanel = "Pressurization";

    /// <summary>The section a Pressurization panel the map lacks is created under.</summary>
    private const string OverheadSection = "Overhead";

    // The seams of BBS757Definition.Hooks.cs, filled in by the overhead plans one feature at a time:
    // each feature has its own helper below and the seam calls it.

    static partial void AddOverheadVariables(Dictionary<string, SimVarDefinition> vars)
    {
        AddLandingAltitudeVariables(vars);
        AddSpringControlVariables(vars);
    }

    partial void AddOverheadPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        AddLandingAltitudePanels(structure, controls, displays);
        AddSpringControlPanels(structure, controls);   // after the field, which stays first on Pressurization
    }

    partial void HandleOverheadTypedText(string varKey, string text, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (varKey != Bbs757LandingAltitude.SetKey) return;
        handled = true;
        _ = SetLandingAltitudeAsync(text, simConnect, announcer);   // never throws: it catches and logs
    }

    partial void HandleOverheadSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, ref bool handled)
    {
        HandleLandingAltitudeSet(varKey, ref handled);
        if (!handled) HandleSpringSet(varKey, simConnect, announcer, ref handled);
    }

    private static void HandleLandingAltitudeSet(string varKey, ref bool handled)
    {
        if (varKey != Bbs757LandingAltitude.SetKey) return;
        // The registration's event is never sent (ruling R13): claiming the key here keeps it from the
        // generic path, which would transmit it. The field's text goes through HandleOverheadTypedText.
        handled = true;
        Log.Debug("BBS757", $"{varKey}: a panel set carries no landing altitude; nothing sent.");
    }

    partial void FormatOverheadDisplay(string varKey, double value, ref string? text)
    {
        if (varKey == Bbs757LandingAltitude.DisplayKey) text = Bbs757LandingAltitude.Display(value);
    }

    /// <summary>
    /// The landing altitude field and its read-back row, added after the map's controls (never
    /// shadowing a key already present).
    /// </summary>
    private static void AddLandingAltitudeVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        // The field. A key containing "_SET" is what MainForm renders as a text box Enter applies. The
        // definition claims it (HandleOverheadTypedText, HandleOverheadSet), so its event is never sent:
        // the registration only keeps it an event, which takes no data definition. SimConnectManager
        // maps every Event registration with MapClientEventToSimEvent at connect, and a dotted private
        // name changes nothing in the sim even if it were ever sent (ruling R16).
        Add(Bbs757LandingAltitude.SetKey, new SimVarDefinition
        {
            Name = "BBS757.LDG_ALT_SET", DisplayName = "Landing Altitude",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
            UnparseableTextAsNaN = true,
        });

        // The read-back row: BlueBird's landing altitude in feet, read when the panel is shown and after
        // a set, shown as "1030 feet" (FormatOverheadDisplay), never announced, no Ctrl+M row.
        Add(Bbs757LandingAltitude.DisplayKey, new SimVarDefinition
        {
            Name = Bbs757LandingAltitude.ValueVar, DisplayName = "Landing Altitude",
            Type = SimVarType.LVar, Units = "feet", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true, ExcludeFromMonitorManager = true,
        });
    }

    /// <summary>
    /// The field goes first in the Pressurization panel (the pilot types it before any switch there)
    /// and its row joins the panel's status display after the map's rows. A map without a Pressurization
    /// panel gets one under the Overhead section, so the field is never lost.
    /// </summary>
    private static void AddLandingAltitudePanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        if (!controls.TryGetValue(PressurizationPanel, out var panel))
        {
            controls[PressurizationPanel] = panel = new List<string>();
            if (!structure.TryGetValue(OverheadSection, out var panels)) structure[OverheadSection] = panels = new List<string>();
            panels.Add(PressurizationPanel);
        }
        panel.Insert(0, Bbs757LandingAltitude.SetKey);

        if (!displays.TryGetValue(PressurizationPanel, out var rows)) displays[PressurizationPanel] = rows = new List<string>();
        rows.Add(Bbs757LandingAltitude.DisplayKey);
    }

    /// <summary>
    /// The landing altitude field's text: refused aloud unless a landing altitude
    /// (<see cref="Bbs757LandingAltitude.TryParseText"/>), refused aloud when a calculator write cannot
    /// land; otherwise written (<see cref="Bbs757LandingAltitude.Write"/>), read back fresh after
    /// <see cref="ReadBackDelayMs"/> and the value READ spoken, whatever it is: a write that did not
    /// take (BBS Nest stopped) says the old altitude, never the one typed. Then the status row is
    /// re-read so it shows the same value. A typed number's confirmation, a validation error and a
    /// refusal always speak (CORE-7). Never throws: fire and forget from the panel. The awaits keep the
    /// UI context (no ConfigureAwait(false)), like every write here.
    /// </summary>
    internal async Task SetLandingAltitudeAsync(string typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is written or said
            var ct = _lifetime.Token;                        // taken once, after the check: safe on a disposed source
            var io = WriteIoFor(sim);

            if (!Bbs757LandingAltitude.TryParseText(typed, out int feet, out string error))
            {
                announcer.AnnounceImmediate(error);
                return;
            }
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", "Landing altitude: write refused, nothing sent.");
                announcer.AnnounceImmediate(Bbs757LandingAltitude.Phrase(null));   // "Landing altitude unavailable"
                return;
            }

            OnOwnWrite(Bbs757LandingAltitude.DisplayKey);   // plan 13
            io.Write(Bbs757LandingAltitude.Write(feet));

            await io.Delay(ReadBackDelayMs, ct);
            double? back = await io.ReadFresh(Bbs757LandingAltitude.DisplayKey, ct);
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(Bbs757LandingAltitude.Phrase(back));
            io.RequestRead(Bbs757LandingAltitude.DisplayKey);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Landing altitude set failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate(Bbs757LandingAltitude.Phrase(null));
        }
    }

    // =================================================================================
    // Plan 12: the spring-loaded switches (the trims, the manual cabin altitude)
    // =================================================================================

    /// <summary>
    /// The eight buttons and the seven reads their presses make (<see cref="Bbs757SpringControls"/>), after the
    /// map's controls, never shadowing a key already present. A button is an Event registration, as the landing
    /// altitude field is: it takes no data definition, the definition claims its key
    /// (<see cref="HandleOverheadSet"/>) so the generic path never sends it, and its dotted private name changes
    /// nothing in the sim even if it were ever sent (ruling R16). A read is an OnRequest L:var on no panel, with
    /// no Ctrl+M row ([VAR-9]): a fresh read delivers only a registered key. The stab trim's readout (the trim
    /// call-out's <c>MON_ElevatorTrim</c>) and Mode Select (the map's control) are registered already.
    /// </summary>
    private static void AddSpringControlVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        foreach (var button in Bbs757SpringControls.Buttons)
        {
            Add(button.Key, new SimVarDefinition
            {
                Name = button.EventName, DisplayName = button.Label,
                Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
                RenderAsButton = true, IsMomentary = true,
                SuppressRestingButtonState = true,   // a momentary push has no resting value to show (CORE-10's opt-in)
            });
        }

        foreach (var read in Bbs757SpringControls.Reads)
        {
            Add(read.Key, new SimVarDefinition
            {
                Name = read.Key, DisplayName = read.Label,
                Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            });
        }
    }

    /// <summary>
    /// The six trim buttons go first on the map's Stabilizer trim panel (ahead of the cutouts and their guards),
    /// the two cabin buttons last on Pressurization (the landing altitude field stays first). A panel the map
    /// lacks is created under its section, so the buttons are never lost.
    /// </summary>
    private static void AddSpringControlPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls)
    {
        List<string> PanelOf(Bbs757SpringControl control)
        {
            if (controls.TryGetValue(control.Panel, out var panel)) return panel;
            controls[control.Panel] = panel = new List<string>();
            if (!structure.TryGetValue(control.Section, out var panels)) structure[control.Section] = panels = new List<string>();
            panels.Add(control.Panel);
            return panel;
        }

        var trims = Bbs757SpringControls.Buttons.Where(b => !b.Control.NeedsManualMode).ToList();
        if (trims.Count > 0) PanelOf(trims[0].Control).InsertRange(0, trims.Select(b => b.Key));

        var cabin = Bbs757SpringControls.Buttons.Where(b => b.Control.NeedsManualMode).ToList();
        if (cabin.Count > 0) PanelOf(cabin[0].Control).AddRange(cabin.Select(b => b.Key));
    }

    /// <summary>A press of a spring button: claimed at once so the generic path never sends it, then held on its own task (never throws).</summary>
    private void HandleSpringSet(string varKey, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (Bbs757SpringControls.ForButtonKey(varKey) == null) return;
        handled = true;
        _ = PressSpringAsync(varKey, sim, announcer);   // never throws: it catches and logs
    }

    /// <summary>The spring controls holding now, by <see cref="Bbs757SpringControl.Key"/>: one hold per switch at a time.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _springHolds = new(StringComparer.Ordinal);

    /// <summary>
    /// A spring button: hold its switch to one side for <see cref="Bbs757SpringControls.HoldMs"/>, release it, make
    /// sure it rests, then say the readout once.
    /// <list type="number">
    /// <item>A press while the same switch is holding is ignored (its two buttons share the switch).</item>
    /// <item>Nothing can land (<c>CanWrite</c>): "{label} unavailable", nothing sent. A cabin button then reads Mode
    /// Select fresh: not MAN says "Set the pressurization mode to MAN first", unread "{label} unavailable", and
    /// nothing is sent.</item>
    /// <item>The hold is written, written again at <see cref="Bbs757SpringControls.HoldRewriteMs"/> (the cabin
    /// switch's XML puts it back to 4 within about a second; skipped once the pilot has switched profiles) and
    /// released at <see cref="Bbs757SpringControls.HoldMs"/>. These waits are on <see cref="CancellationToken.None"/>,
    /// as <see cref="PressAsync"/>'s release is: nothing in the aircraft returns a trim switch to rest, so a lost
    /// release runs the trim to its stop (flight-12.md, measured twice).</item>
    /// <item>The release is verified (<see cref="ReleaseSpringAsync"/>), even after a profile switch: it is the safety.
    /// A switch still off rest says "{name} switch did not return, check it", interrupting.</item>
    /// <item>Unless switched away, <see cref="Bbs757SpringControls.ReadoutDelayMs"/> later the readout is read
    /// fresh and said once (CORE-7: the screen reader said the button; the app says only the read-back). The stab
    /// trim's is the exception: the trim call-out already speaks while the stabilizer moves, so the trim is also read
    /// fresh before the hold and <see cref="Bbs757SpringControls.StabTrimSpeech"/> decides: "Stab trim did not move"
    /// when it did not, nothing when it moved and the call-out speaks it, "Trim 5.3" when it moved and the call-out
    /// is silent (<see cref="StabTrimCalloutSilent"/>).</item>
    /// </list>
    /// Nothing is said once the pilot has switched to another profile. The awaits keep the UI context (no
    /// ConfigureAwait(false)), like every write here. Never throws: fire and forget from the panel.
    /// </summary>
    internal async Task PressSpringAsync(string buttonKey, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var button = Bbs757SpringControls.ForButtonKey(buttonKey);
        if (button == null) return;
        var control = button.Control;
        if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is pressed or said
        var ct = _lifetime.Token;                        // taken once, after the check: safe on a disposed source
        if (!_springHolds.TryAdd(control.Key, 0))
        {
            Log.Debug("BBS757", $"{button.Label}: {control.LVar} is already holding; press ignored.");
            return;
        }

        bool holding = true, wrote = false;
        try
        {
            var io = WriteIoFor(sim);
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", $"{button.Label}: write refused, nothing sent.");
                announcer.Announce(Bbs757SpringControls.UnavailablePhrase(button.Label));
                return;
            }
            if (control.NeedsManualMode)
            {
                double? mode = await io.ReadFresh(Bbs757SpringControls.ModeSelectNode, ct);
                ct.ThrowIfCancellationRequested();
                if (Bbs757SpringControls.ManualModeRefusal(mode, button.Label) is { } refusal)
                {
                    Log.Debug("BBS757", $"{button.Label}: Mode Select read {GuardedLogValue(mode)}; nothing sent.");
                    announcer.Announce(refusal);
                    return;
                }
            }
            bool stabTrim = ReferenceEquals(control, Bbs757SpringControls.AltnStabTrim);
            double? trimBefore = null;
            if (stabTrim)
            {
                trimBefore = await io.ReadFresh(control.ReadoutKey, ct);   // to tell whether the press moved it
                ct.ThrowIfCancellationRequested();
            }

            var (hold, release) = Bbs757SpringControls.Codes(control, button.Positive);
            OnOwnWrite(control.LVar);   // plan 13
            wrote = true;
            bool rested = await HoldSpringAsync(control, hold, release, io);
            _springHolds.TryRemove(control.Key, out _);   // released: the switch may be held again
            holding = false;

            if (_lifetime.IsCancellationRequested) return;   // switched away: released, nothing is said
            if (!rested)
            {
                Log.Warn("BBS757", $"{control.LVar} did not read its rest value after {Bbs757SpringControls.MaxReleaseWrites} releases.");
                announcer.AnnounceImmediate(Bbs757SpringControls.DidNotReturnPhrase(control));
            }

            await io.Delay(Bbs757SpringControls.ReadoutDelayMs, ct);
            double? value = await io.ReadFresh(control.ReadoutKey, ct);
            ct.ThrowIfCancellationRequested();
            string? said = stabTrim
                ? Bbs757SpringControls.StabTrimSpeech(trimBefore, value, StabTrimCalloutSilent())
                : Bbs757SpringControls.ReadoutPhrase(control, value);
            if (said != null) announcer.Announce(said);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{button.Label}: press failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested)
                announcer.Announce(wrote ? Bbs757SpringControls.ReadoutPhrase(control, null)
                    : Bbs757SpringControls.UnavailablePhrase(button.Label));
        }
        finally
        {
            if (holding) _springHolds.TryRemove(control.Key, out _);
        }
    }

    /// <summary>
    /// True when the trim call-out (the base class's <c>MON_ElevatorTrim</c> row) will not speak a move: the pilot
    /// muted its Ctrl+M row, or turned trim announcements off (Shift+T). Speech outside ProcessSimVarUpdate checks
    /// the mute itself ([VAR-8]), as the gear and altimeter call-outs do.
    /// </summary>
    private bool StabTrimCalloutSilent()
        => !_trimAnnouncementsEnabled || IsMonitorMuted(Bbs757SpringControls.StabTrimReadoutKey);

    /// <summary>
    /// Holds the switch: <paramref name="hold"/> now and again at <see cref="Bbs757SpringControls.HoldRewriteMs"/>
    /// (not once switched away), then the verified release at <see cref="Bbs757SpringControls.HoldMs"/>. The waits
    /// are never canceled, and a hold that fails part-way is released at once. True when the switch reads its rest
    /// value afterwards. Never throws.
    /// </summary>
    private async Task<bool> HoldSpringAsync(Bbs757SpringControl control, string hold, string release, Bbs757SimIo io)
    {
        try
        {
            io.Write(hold);
            await io.Delay(Bbs757SpringControls.HoldRewriteMs, CancellationToken.None);
            if (!_lifetime.IsCancellationRequested) io.Write(hold);
            await io.Delay(Bbs757SpringControls.HoldMs - Bbs757SpringControls.HoldRewriteMs, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.LVar}: hold failed: {ex.Message}; releasing it now.");
        }
        return await ReleaseSpringAsync(control, release, io);
    }

    /// <summary>
    /// Writes <paramref name="release"/>, waits <see cref="Bbs757SpringControls.ReleaseReadBackMs"/> (so a read that
    /// has not yet seen the write cannot count as a lost release), reads the switch fresh and, while it does not
    /// read its rest value (<see cref="Bbs757SpringControls.AtRest"/>; unread counts as off rest), writes it again:
    /// at most <see cref="Bbs757SpringControls.MaxReleaseWrites"/> writes in all. Runs after a profile switch too
    /// (the waits and reads are on <see cref="CancellationToken.None"/>; a real read gives up after its own
    /// timeout). True when the switch rests. Never throws: a failed write, wait or read counts as a release that
    /// did not land.
    /// </summary>
    private static async Task<bool> ReleaseSpringAsync(Bbs757SpringControl control, string release, Bbs757SimIo io)
    {
        for (int written = 0; written < Bbs757SpringControls.MaxReleaseWrites; written++)
        {
            try { io.Write(release); }
            catch (Exception ex) { Log.Error("BBS757", $"{control.LVar}: release write failed: {ex.Message}"); }

            double? reading = null;
            try
            {
                await io.Delay(Bbs757SpringControls.ReleaseReadBackMs, CancellationToken.None);
                reading = await io.ReadFresh(control.LVar, CancellationToken.None);
            }
            catch (Exception ex) { Log.Error("BBS757", $"{control.LVar}: release read-back failed: {ex.Message}"); }
            if (Bbs757SpringControls.AtRest(control, reading)) return true;
        }
        return false;
    }
}
