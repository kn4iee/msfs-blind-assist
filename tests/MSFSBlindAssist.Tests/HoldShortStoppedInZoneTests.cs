// The hold-short countdown's stopped-in-zone "Stop." is for a pilot who came to a stop after "Slow down", never for one
// who has stood still since before it was said. VirtualPilot legends, 2026-11-27 (KHOU "via H, hold short of C", KPHL
// "via T, hold short of J", and the eight older LEG_NOT_ENDED legs): a leg loaded at a standstill 35 to 45 m before a
// runway hold, inside the slow-down tier (150 ft at the least), heard "Hold short runway 13L at H in 40 metres. Slow
// down." and, 0.1 s later with the aircraft still standing, "Hold short runway 13L at H. Stop.": told to stop before
// moving, 38 m short of the line, outside its 25 m capture, with nothing after it. Main's taxi flights show the same at a
// standstill start that close to a hold (KPHL from T's start to runway 09L, origin/main 2a2d7d648); since main's #370
// the countdown reaches such a hold along the route, not only on the hold's own segment.
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the test to match the output.

using System.IO;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HoldShortStoppedInZoneTests
{
    // Frames after "Slow down" was said (the manager starts the flag false there), each a ground speed; how many said "Stop.".
    private static int StopsSaid(params double[] groundSpeedsKts)
    {
        bool rolled = false, stopSaid = false;
        int said = 0;
        foreach (double gs in groundSpeedsKts)
            if (HoldShortStoppedInZone.SaysStop(slowDownSaid: true, stopSaid, ref rolled, gs))
            {
                said++;
                stopSaid = true;
            }
        return said;
    }

    [Fact]
    public void A_route_loaded_at_a_standstill_inside_the_slow_down_tier_is_never_told_to_stop_before_it_moves()
        => Assert.Equal(0, StopsSaid(Enumerable.Repeat(0.0, 150).ToArray()));

    [Fact]
    public void A_pilot_who_slows_to_a_stop_after_slow_down_hears_stop_once()
        => Assert.Equal(1, StopsSaid(12.0, 10.0, 7.0, 4.0, 1.5, 0.6, 0.0, 0.0, 0.0));

    [Fact]
    public void Standing_then_rolling_then_stopping_again_hears_stop()
    {
        Assert.Equal(1, StopsSaid(0.0, 0.0, 0.0, 2.0, 5.0, 3.0, 0.4, 0.0));
        Assert.Equal(0, StopsSaid(0.0, 0.0, 0.0, 2.0, 5.0, 6.0));
    }

    [Fact]
    public void Nothing_before_slow_down_and_nothing_after_stop()
    {
        bool rolled = true;
        Assert.False(HoldShortStoppedInZone.SaysStop(slowDownSaid: false, stopSaid: false, ref rolled, 0.0));
        Assert.False(HoldShortStoppedInZone.SaysStop(slowDownSaid: true, stopSaid: true, ref rolled, 0.0));
    }

    // The manager starts the flag afresh where its "Slow down" is said, and asks the one rule for its fallback, a hold
    // short's only (never a leg end's nor a cleared crossing's).
    [Fact]
    public void The_countdown_asks_HoldShortStoppedInZone()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Matches(@"_holdShortSlowDownAnnounced = true;\s+_holdShortRolledSinceSlowDown = false;\s+return;", text);
        Assert.Matches(@"if \(legEnd == null && !clearedCrossing\s+&& HoldShortStoppedInZone\.SaysStop\(_holdShortSlowDownAnnounced, _holdShortStopAnnounced,\s+ref _holdShortRolledSinceSlowDown, _lastGroundSpeedKts\)\)", text);
        Assert.DoesNotContain("_holdShortSlowDownAnnounced && !_holdShortStopAnnounced && _lastGroundSpeedKts < 1.0", text);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
