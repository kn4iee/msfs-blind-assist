using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A330FmaTests
{
    private static A330FmaText C(int at = 0, int pitch = 0, int pArm = 0, int roll = 0, int rArm = 0, int appr = 0,
        bool mach = false, bool trk = false, bool lockThr = false, int cap = 0, bool climbing = true)
        => A330Fma.Compose(new A330FmaInputs(at, pitch, pArm, 0, roll, rArm, appr, mach, trk, lockThr, cap, climbing));

    [Fact]
    public void Heading_pull_reads_HDG_and_TRACK_in_TRK_FPA()
    {
        Assert.Equal("HDG", C(roll: 2).Lateral);
        Assert.Equal("TRACK", C(roll: 2, trk: true).Lateral);
        Assert.Equal("TRACK", C(roll: 11).Lateral);
    }

    [Fact]
    public void Open_mode_says_climb_or_descent_from_the_FCU_altitude()
    {
        Assert.Equal("OP CLB", C(pitch: 2, climbing: true).Vertical);
        Assert.Equal("OP DES", C(pitch: 2, climbing: false).Vertical);
    }

    [Fact]
    public void VS_reads_FPA_in_TRK_FPA()
    {
        Assert.Equal("V S", C(pitch: 8).Vertical);
        Assert.Equal("FPA", C(pitch: 8, trk: true).Vertical);
    }

    [Fact]
    public void ALT_armed_shows_beside_an_open_climb()
        => Assert.Equal("ALT", C(pitch: 2, pArm: 6).Armed);

    [Fact]
    public void An_unknown_mode_is_spoken_as_its_number_never_guessed()
    {
        Assert.Equal("vertical mode 17", C(pitch: 17).Vertical);
        Assert.Equal("lateral mode 9", C(roll: 9).Lateral);
    }

    [Fact]
    public void Land_replaces_both_columns_and_shows_the_capability()
    {
        var f = C(appr: 1, roll: 5, pitch: 7, cap: 3);
        Assert.Equal("LAND", f.Vertical);
        Assert.Equal("", f.Lateral);
        Assert.Equal("CAT 3", f.Capability);
    }

    [Fact]
    public void Nothing_engaged_says_so() => Assert.Equal("No flight modes", C().Summary());
}
