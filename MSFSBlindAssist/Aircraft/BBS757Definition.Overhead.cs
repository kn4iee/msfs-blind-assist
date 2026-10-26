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
    }

    partial void AddOverheadPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        AddLandingAltitudePanels(structure, controls, displays);
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
}
