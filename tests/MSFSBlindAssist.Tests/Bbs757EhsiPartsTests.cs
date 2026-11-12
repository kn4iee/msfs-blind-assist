// What every one of plan 28's five EHSI parts shares: a part the layout lacks (or this build does not read yet) costs
// only its own rows, one "not read" row where they would start, in the modes where it would show something; and every
// row the parts add formats the same on a comma-decimal PC.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiPartsTests
{
    private static readonly Bbs757EhsiLayout WithParts = Bbs757EhsiLayoutTests.SampleWithParts();

    private static string[] Rows(Bbs757EhsiLayout layout, int mode, bool full = true, bool valid = true)
    {
        var reading = Bbs757EhsiReaderTests.Read(new Scene { Mode = mode, Valid = valid }, full, layout);
        Assert.False(reading.Corrupt);
        return Bbs757EhsiText.Rows(reading).ToArray();
    }

    private static string[] NotRead(string[] rows) => rows.Where(r => r.Contains(": not read: ", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void A_missing_part_lists_not_read_with_its_reason()
    {
        // A layout file from before plan 28: every part missing with the file's reason, in MAP each where its rows start
        // (TCAS's left block, the map's own parts before the map's items, the bearing pointers before the heading bug).
        var old = Bbs757EhsiLayoutTests.Sample();
        var map = Rows(old, mode: 2);
        Assert.Equal(new[]
        {
            "TCAS: not read: not in this layout file",
            "Map options: not read: not in this layout file",
            "TERR, wind and runways: not read: not in this layout file",
            "FIX, descent and altitude guides: not read: not in this layout file",
            "VOR and ILS: not read: not in this layout file",
        }, NotRead(map));
        Assert.StartsWith("Top right: ", map[Array.IndexOf(map, "TCAS: not read: not in this layout file") - 1], StringComparison.Ordinal);
        Assert.StartsWith("Heading bug: ", map[Array.IndexOf(map, "VOR and ILS: not read: not in this layout file") + 1], StringComparison.Ordinal);

        // In VOR and ILS the approach's row replaces its whole picture, from the top left; no map options or guides. The
        // top right ETA and the heading bug are plan 26's and stay.
        foreach (int mode in new[] { 0, 1 })
        {
            var rows = Rows(old, mode);
            Assert.Equal(new[]
            {
                mode == 0 ? "EHSI: VOR, range 160" : "EHSI: ILS, range 160",
                "Top: HDG 119 M",
                "VOR and ILS: not read: not in this layout file",
                rows[3],
                "TCAS: not read: not in this layout file",
                "TERR, wind and runways: not read: not in this layout file",
                "Heading bug: 147, 28° right",
            }, rows);
            Assert.StartsWith("Top right: ", rows[3], StringComparison.Ordinal);
        }

        // PLAN draws no bearing pointers; MAP with the IRS not valid draws no map and no pointers.
        Assert.Equal(new[]
        {
            "TCAS: not read: not in this layout file",
            "Map options: not read: not in this layout file",
            "TERR, wind and runways: not read: not in this layout file",
            "FIX, descent and altitude guides: not read: not in this layout file",
        }, NotRead(Rows(old, mode: 3)));
        Assert.Equal(new[]
        {
            "TCAS: not read: not in this layout file",
            "TERR, wind and runways: not read: not in this layout file",
        }, NotRead(Rows(old, mode: 2, valid: false)));

        // Each part with its own reason: the recipe's failing step, or (while its layout part is present) the parts this
        // build does not read yet. The approach and TCAS read from a layout that has them list no "not read" row.
        var missing = WithParts with
        {
            Approach = null, ApproachMissing = "course field not found",
            Tcas = null, TcasMissing = "traffic vector not found",
            MapOptions = null, MapOptionsMissing = "navaid list not found",
            MapExtras = null, MapExtrasMissing = "terrain static not found",
        };
        Assert.Equal(new[]
        {
            "TCAS: not read: traffic vector not found",
            "Map options: not read: navaid list not found",
            "TERR, wind and runways: not read: terrain static not found",
            "FIX, descent and altitude guides: not read: " + Bbs757EhsiReader.NotReadYetInThisBuild,
            "VOR and ILS: not read: course field not found",
        }, NotRead(Rows(missing, mode: 2)));
        // The map extras missing cost only their own rows: no TERR, wind, runway or deceleration row, in any mode.
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var rows = Rows(missing, mode);
            Assert.Contains("TERR, wind and runways: not read: terrain static not found", rows);
            Assert.DoesNotContain(rows, r => new[] { "Bottom left: wind", "Top right: TERR", "Top right: terrain", "Map: runway", "Map: deceleration" }
                .Any(p => r.StartsWith(p, StringComparison.Ordinal)));
        }
        Assert.Equal(new[]
        {
            "FIX, descent and altitude guides: not read: " + Bbs757EhsiReader.NotReadYetInThisBuild,
        }, NotRead(Rows(WithParts, mode: 2)));
        Assert.Equal("not read yet in this build", Bbs757EhsiReader.NotReadYetInThisBuild);

        // The light reading carries the reasons too.
        Assert.Equal(NotRead(Rows(missing, mode: 2)), NotRead(Rows(missing, mode: 2, full: false)));
        // A reading with neither a part nor a reason (dark, corrupt, or built by hand) lists no "not read" row.
        Assert.Empty(NotRead(Bbs757EhsiText.Rows(Bbs757EhsiTextTests.Ehsi()).ToArray()));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_format_under_de_DE_and_sv_SE(string culture)
    {
        var vor = Bbs757EhsiApproachTests.Read(Bbs757EhsiApproachTests.VorSea(), mode: 0);
        var ils = Bbs757EhsiApproachTests.Read(Bbs757EhsiApproachTests.IlsIsnq(), mode: 1);
        var unpinned = Bbs757EhsiApproachTests.IlsIsnq();
        unpinned.LocalizerDeviation = -0.6;
        unpinned.Glideslope = -1.24;
        var ilsOff = Bbs757EhsiApproachTests.Read(unpinned, mode: 1);
        var traffic = Bbs757EhsiReaderTests.Read(new Scene
        {
            Range = 20,
            Tweak = b =>
            {
                Bbs757EhsiApproachTests.VorSea().Write(b);
                new Bbs757EhsiTcasTests.Tcas
                {
                    Tfc = true, Ta = true,
                    Traffic = new[] { new Bbs757EhsiTcasTests.Entry(1, 1, 1065, Bbs757EhsiTcasTests.At(3.76, -10.9).Lat, Bbs757EhsiTcasTests.At(3.76, -10.9).Lon) },
                }.Write(b);
            },
        }, true, Bbs757EhsiApproachTests.P);

        var options = Bbs757EhsiMapOptionsTests.Read(new Bbs757EhsiMapOptionsTests.Options
        {
            NavAid = true, Airport = true, Waypoint = true, TunedLeft = 0,
            NavaidList = new Bbs757EhsiMapOptionsTests.Nav?[] { Bbs757EhsiMapOptionsTests.Sea, Bbs757EhsiMapOptionsTests.Pae with { Nm = 12.34 } },
            AirportList = new[] { new Bbs757EhsiMapOptionsTests.Place("KBFI", 5.26, -10.4) },
            WaypointList = new Bbs757EhsiMapOptionsTests.Place?[] { new Bbs757EhsiMapOptionsTests.Place("HAVHO", 3.06, 30) },
            Left = new Bbs757EhsiMapOptionsTests.Panel(false, Bbs757EhsiMapOptionsTests.Sea, 186.6),
        });
        var plan = Bbs757EhsiMapOptionsTests.Read(new Bbs757EhsiMapOptionsTests.Options
        {
            NavAid = true, NavaidList = new Bbs757EhsiMapOptionsTests.Nav?[] { Bbs757EhsiMapOptionsTests.Pae with { Nm = 12.34 } },
        }, mode: 3);

        // The map extras: TERR, the wind (70.9 kt truncated, from 287.4° true), RW16L and the two green rings at range 20.
        var extras = Bbs757EhsiMapExtrasTests.Read(new Bbs757EhsiMapExtrasTests.Extras
        {
            Terr = true, Picture = true, WindShown = true,
            Departure = new Bbs757EhsiMapExtrasTests.Runway("16L", Bbs757EhsiTcasTests.At(2.04, -15), Bbs757EhsiTcasTests.At(3.9, -14)),
            Starts = new[] { Bbs757EhsiTcasTests.At(6.06, 10) },
            Ends = new[] { Bbs757EhsiTcasTests.At(9.04, 20) },
        }, range: 20, sim: new Bbs757SimValues(new Dictionary<string, double>
        {
            [Bbs757EhsiReader.WindSpeedKey] = 70.9, [Bbs757EhsiReader.WindDirectionKey] = 287.4, [Bbs757EhsiReader.GearKey] = 0,
        }));

        foreach (var reading in new[] { vor, ils, ilsOff, traffic, options, plan, extras })
            Assert.Equal(TestCulture.Run("en-US", () => Bbs757EhsiText.Rows(reading)), TestCulture.Run(culture, () => Bbs757EhsiText.Rows(reading)));

        var rows = TestCulture.Run(culture, () => Bbs757EhsiText.Rows(vor).Concat(Bbs757EhsiText.Rows(ils)).Concat(Bbs757EhsiText.Rows(ilsOff))
            .Concat(Bbs757EhsiText.Rows(traffic)).Concat(Bbs757EhsiText.Rows(options)).Concat(Bbs757EhsiText.Rows(plan))
            .Concat(Bbs757EhsiText.Rows(extras)).ToArray());
        Assert.Contains("Top right: TERR", rows);
        Assert.Contains("Top right: terrain 031 red, 004 amber", rows);
        Assert.Contains("Bottom left: wind 70 kt from 153° right", rows);
        Assert.Contains("Map: runway 16L, 2.0 NM, 15° left", rows);
        Assert.Contains("Map: deceleration starts, 6.1 NM, 10° right", rows);
        Assert.Contains("Map: deceleration ends, 9.0 NM, 20° right", rows);
        Assert.Contains("Map: VORTAC SEA, tuned, 0.5 NM, 173° left", rows);
        Assert.Contains("Map: VOR/DME PAE, 12.3 NM, 40° right", rows);
        Assert.Contains("Map: airport KBFI, 5.3 NM, 10° left", rows);
        Assert.Contains("Map: waypoint HAVHO, 3.1 NM, 30° right", rows);
        Assert.Contains("Map: VOR L course 187 through SEA", rows);
        Assert.Contains("Map: VOR/DME PAE, 12.3 NM south of the center", rows);
        Assert.Contains("Top left: DME 0.5", rows);
        Assert.Contains("Center: deviation 0.5 dots right", rows);
        Assert.Contains("Bearing pointer 1: VOR L, SEA, 74° right", rows);
        // VOR's top right ETA, as MAP's prints it.
        string mapEta = Bbs757EhsiText.Rows(Bbs757EhsiApproachTests.Read(Bbs757EhsiApproachTests.VorSea(), mode: 2))
            .Single(r => r.StartsWith("Top right: ", StringComparison.Ordinal));
        Assert.Contains(mapEta, rows);
        Assert.Contains("Top left: DME 0.6", rows);
        Assert.Contains("Bottom right: 110.30", rows);
        Assert.Contains("Center: deviation 1.2 dots left", rows);
        Assert.Contains("Right side: glideslope 1.2 dots below", rows);
        Assert.Contains("Map: proximate traffic, 3.8 NM, 11° left, +10 climbing", rows);
        Assert.Contains("Right: TRAFFIC, amber", rows);
    }
}
