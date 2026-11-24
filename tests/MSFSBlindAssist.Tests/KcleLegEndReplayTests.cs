// The KCLE 2026-11-14 "P, hold short of taxiway L" leg replayed through ProgressiveLegEnd: the logged leg never ended.

using System.Text.Json;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class KcleLegEndReplayTests
{
    private sealed record Frame(double t, double lat, double lon, double hdg, double gs, int seg);
    private sealed record Node(int id, double lat, double lon);
    private sealed record Track(Node junctionNode, List<Frame> frames);

    private static double GreatCircleM(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
        double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    private static Frame FirstArrival(double setbackM)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kcle-2026-11-14-p-hold-short-l.json");
        var track = JsonSerializer.Deserialize<Track>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        bool slowDownSpoken = false;
        foreach (var f in track.frames)
        {
            double distJ = GreatCircleM(f.lat, f.lon, track.junctionNode.lat, track.junctionNode.lon);
            double dist = Math.Max(0, distJ - setbackM);
            if (dist * 3.28084 <= Math.Clamp(f.gs * 1.68781 * 8.0, 150.0, 400.0)) slowDownSpoken = true;
            if (ProgressiveLegEnd.Arrived(ProgressiveTerminatorType.HoldShortTaxiway, dist, f.gs, slowDownSpoken, false, false))
                return f;
        }
        throw new InvalidOperationException("The replayed leg never ended.");
    }

    [Fact] public void The_logged_kcle_leg_ends_before_the_pilot_came_to_a_stop()
    {
        var fired = FirstArrival(setbackM: 35.0);            // measured 2026-11-15: t = 247.5, 59.3 m from the junction
        Assert.InRange(fired.t, 246.5, 248.5);
        Assert.True(fired.t < 255.2);                        // the pilot was stopped from t = 255.2, 21.0 m from the junction
    }
    [Fact] public void It_ends_even_with_no_setback_because_a_stop_21_metres_short_is_inside_the_radius()
        => Assert.InRange(FirstArrival(setbackM: 0.0).t, 252.0, 255.2);
    [Fact] public void The_standstill_at_the_24L_hold_line_does_not_end_the_leg()
        => Assert.True(FirstArrival(setbackM: 35.0).t > 240.0);   // the aircraft stood still at t = 193.4 to 198.0, 329 m out
}
