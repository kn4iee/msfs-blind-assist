using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The load watch (task 11e). The 757 EFB window's "Load fuel and payload from SimBrief" taps
/// BlueBird's own IMPORT FROM SIMBRIEF and START BOARDING, and BlueBird then fuels and boards at its
/// own pace with nothing a screen reader can see. The definition watches the tanks and stations, so
/// the watch outlives the window and ends with the flight it belongs to: it reads them every
/// <see cref="Bbs757LoadWatch.IntervalMs"/>, and <see cref="Bbs757LoadWatch"/> judges each read and
/// says once each "Fueling complete." and "Boarding complete.", or after an hour what has not
/// finished. Those are background changes the pilot did not trigger, so they always speak (CORE-7),
/// queued behind whatever is being said. One watch at a time; a flight load or a lost connection
/// (<see cref="OnSimContextReset"/>) and a profile switch (<see cref="OnSwitchedAway"/>) end it
/// silently. Like every other wait here it runs on the UI thread: the window's action starts it, and
/// its awaits keep that context.
/// </summary>
public partial class BBS757Definition
{
    private sealed record LoadWatchRun(Bbs757LoadWatch Watch, CancellationTokenSource Cts);

    // The watch running, or null. Set and cleared on the UI thread.
    private LoadWatchRun? _loadWatch;

    /// <summary>Whether a load watch runs.</summary>
    internal bool LoadWatchRunning => _loadWatch != null;

    /// <summary>
    /// Starts watching a load BlueBird's EFB has started, toward <paramref name="loads"/>, speaking in
    /// the aircraft's <paramref name="units"/>. False, and nothing started, when a watch already runs
    /// or the profile was switched away. Never throws: the loop catches and logs.
    /// </summary>
    internal bool StartLoadWatch(Bbs757Loads loads, Bbs757WeightUnits? units, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        if (_lifetime.IsCancellationRequested || _loadWatch != null) return false;
        var run = new LoadWatchRun(new Bbs757LoadWatch(loads, units), CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
        _loadWatch = run;
        _ = WatchLoadAsync(run, WriteIoFor(sim), announcer);
        return true;
    }

    private async Task WatchLoadAsync(LoadWatchRun run, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        var ct = run.Cts.Token;
        long started = NowMs();
        try
        {
            while (true)
            {
                var read = await ReadLoadAsync(io, ct);
                ct.ThrowIfCancellationRequested();
                if (run.Watch.Observe(read, NowMs() - started) is { } said)
                {
                    Log.Info("BBS757", $"{Bbs757Loadsheet.LogLine("watch", run.Watch.Loads, read)} ({StageOf(run.Watch)})");
                    announcer.Announce(said);
                }
                if (run.Watch.Finished) return;
                await io.Delay(Bbs757LoadWatch.IntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* a flight load, a lost connection or a profile switch */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Load watch failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_loadWatch, run)) _loadWatch = null;
            run.Cts.Dispose();
        }
    }

    /// <summary>"fuel done, boarding loading" and so on, for the log.</summary>
    private static string StageOf(Bbs757LoadWatch watch) =>
        $"fuel {(watch.FuelDone ? "done" : "loading")}, boarding {(watch.BoardingDone ? "done" : "loading")}" +
        (watch.TimedOut ? ", gave up after an hour" : "");

    /// <summary>
    /// The eight load variables read fresh, together (each on its own fresh-read request), and
    /// gathered by key; a value that could not be read is null.
    /// </summary>
    private static async Task<Bbs757LoadReadBack> ReadLoadAsync(Bbs757SimIo io, CancellationToken ct)
    {
        var vars = Bbs757Loadsheet.ReadBackVariables;
        double?[] read = await Task.WhenAll(vars.Select(v => io.ReadFresh(v.Key, ct)));
        var values = new Dictionary<string, double?>(StringComparer.Ordinal);
        for (int i = 0; i < vars.Count; i++) values[vars[i].Key] = read[i];
        return Bbs757Loadsheet.ReadBack(values);
    }

    /// <summary>Ends the load watch, silently: a flight load, a lost connection or a profile switch.</summary>
    private void CancelLoadWatch()
    {
        var run = _loadWatch;
        _loadWatch = null;
        if (run == null) return;
        try { run.Cts.Cancel(); } catch (ObjectDisposedException) { /* it ended meanwhile */ }
    }
}
