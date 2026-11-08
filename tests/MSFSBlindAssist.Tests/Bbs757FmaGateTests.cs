// The 757 FMA's gate (S3): memory is trusted only while the two F/D switches and the MCP's selected altitude and
// heading agree with the simulator's own variables. The simulator may lag memory, so each field must match at least one
// reading of the last 3 s; different fields may match different readings (the 737's cross check). Pure logic.

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757FmaGateTests
{
    private static readonly Bbs757FmaRaw Cruise = new(At: 1, Pitch: 6, PitchArmed: 0, Roll: 5, RollArmed: 0, Status: 1,
        FdCaptain: true, FdFirstOfficer: true, McpAltitude: 35000, McpHeading: 65);

    private static Bbs757FmaReading R(Bbs757FmaRaw raw) => Bbs757FmaReader.Decode(Bbs757FmaReaderTests.Layout, raw);

    private static Bbs757SimValues Sim(double fd1 = 1, double fd2 = 1, double altitude = 35000, double heading = 65) =>
        new(new Dictionary<string, double>
        {
            ["BBS_FD1_SWITCH"] = fd1,
            ["BBS_FD2_SWITCH"] = fd2,
            ["BBS_AP_TARGET_ALTITUDE"] = altitude,
            ["BBS_AP_TARGET_HEADING"] = heading,
        });

    private static bool Passes(Bbs757SimValues sim, params (long AtMs, Bbs757FmaRaw Raw)[] history) =>
        Bbs757FmaGate.Passes(history.Select(h => (h.AtMs, R(h.Raw))).ToArray(), sim);

    [Fact]
    public void The_gate_keys_are_the_fd_switches_and_the_mcp_targets()
    {
        Assert.Equal("BBS_FD1_SWITCH", Bbs757FmaGate.FdCaptainKey);
        Assert.Equal("BBS_FD2_SWITCH", Bbs757FmaGate.FdFirstOfficerKey);
        Assert.Equal("BBS_AP_TARGET_ALTITUDE", Bbs757FmaGate.AltitudeKey);
        Assert.Equal("BBS_AP_TARGET_HEADING", Bbs757FmaGate.HeadingKey);
        Assert.Equal(3000, Bbs757FmaGate.WindowMs);
    }

    [Fact]
    public void Passes_when_fd_altitude_and_heading_match_within_3_s()
    {
        Assert.True(Passes(Sim(), (0, Cruise)));
        Assert.True(Passes(Sim(), (0, Cruise), (1000, Cruise), (3000, Cruise)));
        // The F/D bytes compare with their own switches: the Captain's with FD1, the First Officer's with FD2.
        var foOff = Cruise with { FdFirstOfficer = false };
        Assert.True(Passes(Sim(fd1: 1, fd2: 0), (0, foOff)));
        Assert.False(Passes(Sim(fd1: 0, fd2: 1), (0, foOff)));
        // A switch reads on above one half.
        Assert.True(Passes(Sim(fd1: 0.9, fd2: 0.1), (0, foOff)));
        // The simulator's whole-number targets round as memory's do.
        Assert.True(Passes(Sim(altitude: 35000.2, heading: 64.8), (0, Cruise)));
        // Each field alone fails it.
        Assert.False(Passes(Sim(altitude: 34000), (0, Cruise)));
        Assert.False(Passes(Sim(heading: 66), (0, Cruise)));
        Assert.False(Passes(Sim(fd2: 0), (0, Cruise)));
    }

    // Review Focus: the F/D switched while an MCP knob turns. The simulator still says F/D on (it lags) but already has
    // the new altitude; memory has the F/D off and the new altitude now, the F/D on and the old altitude 2 s ago. No one
    // reading matches every field, yet each field matches one reading of the window, so the gate passes.
    [Fact]
    public void Each_gate_field_may_match_a_different_sample_within_3_s()
    {
        var before = Cruise with { McpAltitude = 33000 };
        var after = Cruise with { FdCaptain = false };
        var sim = Sim(fd1: 1, altitude: 35000);

        Assert.True(Passes(sim, (1000, before), (3000, after)));
        // Neither reading alone matches.
        Assert.False(Passes(sim, (1000, before)));
        Assert.False(Passes(sim, (3000, after)));
        // Once the reading that matched the F/D is more than 3 s older than the newest, it no longer counts.
        Assert.False(Passes(sim, (1000, before), (4001, after)));
        Assert.True(Passes(sim, (1000, before), (4000, after)));
    }

    [Fact]
    public void Heading_0_equals_360()
    {
        Assert.True(Passes(Sim(heading: 360), (0, Cruise with { McpHeading = 0 })));
        Assert.True(Passes(Sim(heading: 0), (0, Cruise with { McpHeading = 360 })));
        Assert.True(Passes(Sim(heading: 359.6), (0, Cruise with { McpHeading = 0 })));
        Assert.False(Passes(Sim(heading: 359), (0, Cruise with { McpHeading = 0 })));
        Assert.True(Bbs757FmaGate.HeadingEquals(0, 360));
        Assert.False(Bbs757FmaGate.HeadingEquals(1, 360));
    }

    [Fact]
    public void An_fd_byte_that_disagrees_for_3_s_fails()
    {
        // The pilot switched the Captain's F/D off; memory still reads it on for every reading of the window.
        var on = Cruise;
        var sim = Sim(fd1: 0);
        Assert.False(Passes(sim, (0, on), (1000, on), (2000, on), (3000, on)));
        // Once memory follows, within the window, it passes.
        Assert.True(Passes(sim, (0, on), (1000, on), (2000, on), (3000, on with { FdCaptain = false })));
        // The First Officer's byte likewise.
        Assert.False(Passes(Sim(fd2: 0), (0, on), (1500, on), (3000, on)));
    }

    [Fact]
    public void Nothing_to_compare_fails()
    {
        // No reading.
        Assert.False(Bbs757FmaGate.Passes(Array.Empty<(long, Bbs757FmaReading)>(), Sim()));
        // A gate variable the simulator did not give, or one that is not a number.
        foreach (string key in new[] { "BBS_FD1_SWITCH", "BBS_FD2_SWITCH", "BBS_AP_TARGET_ALTITUDE", "BBS_AP_TARGET_HEADING" })
        {
            var values = new Dictionary<string, double>(Sim().Values);
            values.Remove(key);
            Assert.False(Bbs757FmaGate.Passes(new[] { (0L, R(Cruise)) }, new Bbs757SimValues(values)));
            values[key] = double.NaN;
            Assert.False(Bbs757FmaGate.Passes(new[] { (0L, R(Cruise)) }, new Bbs757SimValues(values)));
        }
        // A corrupt reading never matches (the monitor never keeps one; the gate does not trust one either).
        Assert.False(Passes(Sim(), (0, Cruise with { Roll = -5 })));
    }
}
