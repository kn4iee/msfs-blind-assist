using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class ProgressiveLegEndTests
{
    private const ProgressiveTerminatorType Twy = ProgressiveTerminatorType.HoldShortTaxiway;
    private const ProgressiveTerminatorType Across = ProgressiveTerminatorType.AfterCrossingRunway;

    [Fact] public void A_hold_leg_ends_inside_the_capture_radius()
    {
        Assert.True(ProgressiveLegEnd.Arrived(Twy, 24.9, 12.0, false, false));
        Assert.False(ProgressiveLegEnd.Arrived(Twy, 25.1, 12.0, false, false));
    }
    [Fact] public void A_hold_leg_ends_when_the_pilot_stops_after_the_slow_down_call()
        => Assert.True(ProgressiveLegEnd.Arrived(Twy, 60.0, 0.5, slowDownSpoken: true, nearARunway: false));
    [Fact] public void A_stop_for_traffic_before_the_slow_down_call_does_not_end_the_leg()
        => Assert.False(ProgressiveLegEnd.Arrived(Twy, 150.0, 0.0, slowDownSpoken: false, nearARunway: false));
    [Fact] public void A_stop_within_thirty_metres_ends_the_leg_whatever_was_spoken()
    {
        Assert.True(ProgressiveLegEnd.Arrived(Twy, 30.0, 2.0, false, false));
        Assert.False(ProgressiveLegEnd.Arrived(Twy, 30.1, 2.0, false, false));
        Assert.False(ProgressiveLegEnd.Arrived(Twy, 30.0, 2.1, false, false));
    }
    [Fact] public void A_leg_across_a_runway_never_ends_on_or_beside_a_runway()
    {
        Assert.False(ProgressiveLegEnd.Arrived(Across, 10.0, 0.0, true, nearARunway: true));
        Assert.False(ProgressiveLegEnd.Arrived(ProgressiveTerminatorType.EndOfTaxiway, 10.0, 0.0, true, nearARunway: true));
        Assert.True(ProgressiveLegEnd.Arrived(Across, 10.0, 0.0, false, nearARunway: false));
        Assert.False(ProgressiveLegEnd.Arrived(Across, 60.0, 0.0, slowDownSpoken: true, nearARunway: false));
    }
    [Fact] public void A_hold_leg_still_ends_beside_a_runway()
        => Assert.True(ProgressiveLegEnd.Arrived(ProgressiveTerminatorType.HoldShortRunway, 10.0, 0.0, false, nearARunway: true));
    // BA never asks for a stop on a runway: a leg that does not end at a hold (across a runway, end of
    // the taxiway) holds its "Slow down" and "Stop" calls until the aircraft is clear of the runway's margin.
    [Theory]
    [InlineData(ProgressiveTerminatorType.AfterCrossingRunway)]
    [InlineData(ProgressiveTerminatorType.EndOfTaxiway)]
    public void A_leg_not_ending_at_a_hold_asks_for_no_slow_down_or_stop_on_or_beside_a_runway(ProgressiveTerminatorType type)
    {
        Assert.False(ProgressiveLegEnd.MaySpeak(type, CountdownTier.SlowDown, nearARunway: true));
        Assert.False(ProgressiveLegEnd.MaySpeak(type, CountdownTier.Stop, nearARunway: true));
        Assert.True(ProgressiveLegEnd.MaySpeak(type, CountdownTier.Outer, nearARunway: true));
        Assert.True(ProgressiveLegEnd.MaySpeak(type, CountdownTier.SlowDown, nearARunway: false));
        Assert.True(ProgressiveLegEnd.MaySpeak(type, CountdownTier.Stop, nearARunway: false));
    }
    [Theory]
    [InlineData(ProgressiveTerminatorType.HoldShortRunway)]
    [InlineData(ProgressiveTerminatorType.HoldShortTaxiway)]
    [InlineData(ProgressiveTerminatorType.HoldAtNamedPoint)]
    public void A_hold_ending_speaks_every_tier_beside_a_runway(ProgressiveTerminatorType type)
    {
        foreach (var tier in new[] { CountdownTier.Outer, CountdownTier.SlowDown, CountdownTier.Stop })
            Assert.True(ProgressiveLegEnd.MaySpeak(type, tier, nearARunway: true));
    }
    [Theory]
    [InlineData(ProgressiveTerminatorType.HoldShortTaxiway, "L", null, "Hold short taxiway L")]
    [InlineData(ProgressiveTerminatorType.HoldShortRunway, "04R", null, "Hold short runway 04R")]
    [InlineData(ProgressiveTerminatorType.HoldAtNamedPoint, "L2", null, "Hold at L2")]
    [InlineData(ProgressiveTerminatorType.AfterCrossingRunway, "24L", null, "Route ends")]
    [InlineData(ProgressiveTerminatorType.EndOfTaxiway, "", null, "Route ends")]
    [InlineData(ProgressiveTerminatorType.EndOfTaxiway, "", "end of taxiway A", "Hold short end of taxiway A")]
    public void The_countdown_lead_follows_the_ending(ProgressiveTerminatorType type, string target, string? label, string expected)
        => Assert.Equal(expected, ProgressiveLegEnd.CountdownLead(new ProgressiveTerminator(type, target), label));
    // A leg end that exists only inside a runway's clear margin: the pilot's entry is right, so the
    // refusal says why instead of "Check your entry".
    [Fact] public void A_leg_end_found_only_beside_a_runway_is_refused_with_the_reason()
    {
        Assert.Equal("There is no place to hold short of taxiway L on A clear of a runway.",
            ProgressiveLegEnd.NoHoldShortClearOfARunway("L", "A"));
        Assert.Equal("Taxiway A has no end clear of a runway.",
            ProgressiveLegEnd.NoEndClearOfARunway("A"));
    }
    [Fact] public void The_countdown_reads_the_same_under_a_comma_decimal_culture()
    {
        TestCulture.Run("de-DE", () =>
        {
            Assert.Equal("Hold short taxiway L in 120 metres.", ProgressiveLegEnd.Countdown("Hold short taxiway L", "120 metres", CountdownTier.Outer));
            Assert.Equal("Hold short taxiway L in 60 metres. Slow down.", ProgressiveLegEnd.Countdown("Hold short taxiway L", "60 metres", CountdownTier.SlowDown));
            Assert.Equal("Route ends in 30 metres. Stop.", ProgressiveLegEnd.Countdown("Route ends", "30 metres", CountdownTier.Stop));
            return 0;
        });
    }
    [Fact] public void The_countdown_speaks_feet_when_the_pilot_chose_feet()
    {
        var saved = DistanceFormatter.UnitProvider;
        try
        {
            DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
            Assert.Equal("Hold short taxiway L in 400 feet.", ProgressiveLegEnd.Countdown("Hold short taxiway L", DistanceFormatter.FromFeet(400), CountdownTier.Outer));
        }
        finally { DistanceFormatter.UnitProvider = saved; }
    }
    [Fact] public void ProgressiveLegEnd_never_writes_a_unit_of_its_own()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "MSFSBlindAssist", "Navigation", "ProgressiveLegEnd.cs"));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "\"(?:[^\"\\\\]|\\\\.)*\""))
        {
            string lit = m.Value.ToLowerInvariant();
            Assert.DoesNotContain("metre", lit);
            Assert.DoesNotContain("feet", lit);
            Assert.DoesNotContain("foot", lit);
        }
    }
}
