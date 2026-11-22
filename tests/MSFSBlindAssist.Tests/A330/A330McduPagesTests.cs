using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using MSFSBlindAssist.Forms.IniA330;
using Xunit;

namespace MSFSBlindAssist.Tests;

// The window's whole listing for pages captured from the cockpit, against listings
// written out by a separate implementation of the same rules.
public class A330McduPagesTests
{
    private static string FixturesDir() => Path.Combine(
        ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A330", "Fixtures");

    private static A330Frame Fixture(string name) =>
        A330ReaderProtocol.ParseFrame(File.ReadAllText(Path.Combine(FixturesDir(), name)), 0)!;

    public static IEnumerable<object[]> Pages()
    {
        yield return new object[] { "p03_initb.json", new[]
        {
            "Title: INIT FUEL PREDICTION \u2190\u2192",
            "   TAXI           ZFW/ZFWCG",
            "1: 0.6          125.0/27.0",
            "   TRIP /TIME         BLOCK",
            "2:   2.8/0015          54.6",
            "   RTE RSV/%",
            "3:   0.1/5.0",
            "   ALTN /TIME     TOW/   LW",
            "4:   3.7/0017   179.0/176.2",
            "   FINAL/TIME     TRIP WIND",
            "5:   2.2/0030         HD000",
            "   MIN DEST FOB  EXTRA/TIME",
            "6:   5.9          45.1/1015",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_data1.json", new[]
        {
            "Title: DATA INDEX   1/2 \u2190\u2192",
            "    POSITION",
            "1: <MONITOR",
            "    IRS",
            "2: <MONITOR",
            "     GPS",
            "3: <MONITOR",
            "4: <A/C STATUS",
            "    CLOSEST          PRINT",
            "5: <AIRPORTS      FUNCTION>",
            "    EQUITIME (inop)    AOC",
            "6: <POINT (inop)  FUNCTION>",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_data2.json", new[]
        {
            "Title: DATA INDEX   2/2 \u2190\u2192",
            "                    STORED",
            "1: <WAYPOINTS    WAYPOINTS>",
            "                    STORED",
            "2: <NAVAIDS        NAVAIDS>",
            "                    STORED",
            "3: <RUNWAYS        RUNWAYS>",
            "                    STORED (unavailable)",
            "4: <ROUTES          ROUTES>(unavailable)",
            "5:",
            "6:",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_status.json", new[]
        {
            "Title: A330-200",
            "    ENG",
            "1: CF6-80AE3",
            "    ACTIVE NAV DATA BASE",
            "2: 29OCT-26NOV   AB49012001",
            "    SECOND NAV DATA BASE",
            "3: \u219001OCT-29OCT",
            "4:",
            "   CHG CODE",
            "5: [ ]",
            "   IDLE/PERF     SOFTWARE",
            "6: +0.0/+0.0  STATUS/XLOAD>",
            "Scratchpad: NAV ACCUR DOWNGRADED",
        } };
        yield return new object[] { "p03_fpln1.json", new[]
        {
            "Title: FROM          MSF773 \u2190\u2192",
            "                 SPD/ALT",
            "1: KATL26L ----  ---/ -----",
            "    VRSTY3 BRG292    1NM",
            "2: INTCPT  ----  ---/ -----",
            "    VRSTY3 TRK288    6NM",
            "3: SNUFY   ----  ---/ -----",
            "    VRSTY3           4NM",
            "4: SLAWW   ----  ---/ -----",
            "    VRSTY3           4NM",
            "5: RIGGZ   ----  ---/ -----",
            "    DEST   TIME  DIST  EFOB",
            "6: KMCO17L ----  394   -.--",
            "Scratchpad: NAV ACCURACY UPGRAD   \u2191\u2193",
        } };
        yield return new object[] { "p03_fpln2.json", new[]
        {
            "Title: FROM          MSF773 \u2190\u2192",
            "                 SPD/ALT",
            "1: RIVTT   ----  250/ -----",
            "    VRSTY3          40NM",
            "2: VRSTY   ----  ---/ -----",
            "    VRSTY3 TRK151   28NM",
            "3: NOKIE   ----  ---/ -----",
            "4: ---F-PLN DISCONTINUITY--",
            "    ALYNA4         270NM",
            "5: ALYNA   ----  ---/ -----",
            "    DEST   TIME  DIST  EFOB",
            "6: KMCO17L ----  394   -.--",
            "Scratchpad: NAV ACCURACY UPGRAD   \u2191\u2193",
        } };
        yield return new object[] { "p03_fpln3.json", new[]
        {
            "Title: FROM          MSF773 \u2190\u2192",
            "                 SPD/ALT",
            "1: BANYA   ----  210/  7000",
            "    ALYNA4           0NM",
            "2: MANUAL  ----  ---/ -----",
            "3: ---F-PLN DISCONTINUITY--",
            "                    14NM",
            "4: SEEDO   ----  ---/  2500",
            "                     3-3.0\u00B0",
            "5: COCAS   ----  ---/  1500",
            "    DEST   TIME  DIST  EFOB",
            "6: KMCO17L ----  394   -.--",
            "Scratchpad: NAV ACCURACY UPGRAD   \u2191\u2193",
        } };
        yield return new object[] { "p03_latrev.json", new[]
        {
            "Title: LAT REV FROM RIGGZ",
            "      33\u00B034.6N/084\u00B041.8W",
            "1:                FIX INFO>",
            "2: <OFFSET",
            "                  NEXT WPT",
            "3: <HOLD            [     ]",
            "    ENABLE        NEW DEST",
            "4: \u2190ALTN             [    ]",
            "5:                 AIRWAYS>",
            "6: <RETURN",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_sids.json", new[]
        {
            "Title: DEPARTURES FROM KATL \u2190\u2192",
            "    RWY      SID     TRANS",
            "1: 26L      -----     -----",
            "   SIDS   AVAILABLE   TRANS",
            "2: \u2190NONE",
            "3: \u2190ATL2",
            "4: \u2190BANNG3",
            "5: \u2190CUTTN2",
            "6: *ERASE           INSERT*",
            "Scratchpad: NAV ACCURACY UPGRAD",
        } };
        yield return new object[] { "p03_perfto.json", new[]
        {
            "Title: TAKE OFF RWY 26L",
            "    V1  FLP RETR        RWY",
            "1: 141    F=147         26L",
            "    VR  SLT RETR T.O SHIFT",
            "2: 146    S=173    [M][  ]*",
            "    V2  CLEAN     FLAPS/THS",
            "3: 151    O=209     2/UP0.5",
            "   TRANS ALT   FLEX TO TEMP",
            "4: 18000                50\u00B0",
            "   THR RED/ACC  ENG OUT ACC",
            "5:  2500/2500          2500",
            "    UPLINK (inop)     NEXT",
            "6: <TO DATA (inop)   PHASE>",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_prog.json", new[]
        {
            "Title: TO MSF773",
            "    CRZ      OPT    REC MAX",
            "1:  FL330   FL402    FL415",
            "2: <REPORT",
            "3:",
            "     BRG /DIST",
            "4:  ---\u00B0 /---    TO [     ]",
            "    PREDICTIVE",
            "5: <GPS         GPS PRIMARY",
            "   REQUIRED ACCUR ESTIMATED",
            "6:  1.00NM   HIGH    0.03NM",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_radnav.json", new[]
        {
            "Title: RADIO NAV",
            "   VOR1/FREQ      VOR2/FREQ",
            "1:  GSU/116.90  GSU/116.90",
            "   CRS                  CRS",
            "2: [ ]                  [ ]",
            "   ILS /FREQ",
            "3: [  ]/[    ]",
            "   CRS",
            "4: [ ]",
            "   ADF1/FREQ      ADF2/FREQ",
            "5: [  ]/[   . ] [  ]/[   .]",
            "6:",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_fuelpred.json", new[]
        {
            "Title: FUEL PRED",
            "    AT        UTC      EFOB",
            "1: KMCO      1716      51.2",
            "2: KTPA      1902      47.5",
            "   RTE RSV/%      ZFW/ZFWCG",
            "3:   0.1/5.0    125.0/27.0",
            "   ALTN /TIME    FOB",
            "4:   3.7/0017    54.6/FF+FQ",
            "   FINAL/TIME      GW/   CG",
            "5:   2.2/0030   170.6/ 27.0",
            "   MIN DEST FOB  EXTRA/TIME",
            "6:   5.9          45.1/1015",
            "Scratchpad:",
        } };
        yield return new object[] { "p03_init.json", new[]
        {
            "Title: INIT        \u2190\u2192",
            "    CO RTE          FROM/TO",
            "1: ----------     ----/----",
            "   ALTN/CO RTE",
            "2: ----/----------",
            "   FLT NBR",
            "3: --------       IRS INIT>",
            "4:",
            "   COST INDEX",
            "5: 20                 WIND>",
            "   CRZ FL/TEMP        TROPO",
            "6: ---- /---\u00B0         36090",
            "Scratchpad: NAV ACCUR DOWNGRADED",
        } };
        yield return new object[] { "p03_menu.json", new[]
        {
            "Title: MCDU MENU",
            "                    SELECT (unavailable)",
            "1: <FM1           NAV B/UP>(unavailable)",
            "2: <ATSU",
            "3: <AIDS (unavailable)",
            "4: <CFDS (unavailable)",
            "5:",
            "6:",
            "Scratchpad: SELECT DESIRED SYSTEM",
        } };
        yield return new object[] { "p03_fpln.json", new[]
        {
            "Title: FROM          MSF773 \u2190\u2192",
            "                 SPD/ALT",
            "1: (LIM)   0003  250/ 10000",
            "    VRSTY3           4NM",
            "2: RIGGZ   0004   \" / 11160",
            "    VRSTY3 TRK177    9NM",
            "3: RIVTT   0005 * \" / 16080",
            "    VRSTY3          40NM",
            "4: VRSTY   0011  291/ FL281",
            "                     3NM",
            "5: (T/C)   0015  .74/ FL330",
            "    DEST   TIME  DIST  EFOB",
            "6: KMCO17L 0103  394   47.3",
            "Scratchpad:                       \u2191\u2193",
        } };
        yield return new object[] { "p03_departures.json", new[]
        {
            "Title: DEPARTURES FROM KATL \u2190\u2192",
            "    RWY      SID     TRANS",
            "1: ---      -----     -----",
            "      AVAILABLE RUNWAYS",
            "2: \u219008L  2700M",
            "         084",
            "3: \u219026R  2700M",
            "         264",
            "4: \u219008R  3000M",
            "         084",
            "5: \u219026L  3000M",
            "         264",
            "6: <RETURN",
            "Scratchpad: NAV ACCURACY UPGRAD",
        } };
        yield return new object[] { "p03_xload.json", new[]
        {
            "Title: P/N XLOAD     1/7",   // the page counter half a row under the title, on the title line
            "1: FMS1 UPDATE",
            "                P/N STATUS",
            "2:                   PRINT*",
            "3:",
            "4:",
            "5: <A/C STATUS",
            "6:               NEXT PAGE>",
            "Scratchpad:",
        } };
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void The_window_lists_the_page_as_the_cockpit_shows_it(string fixture, string[] expected)
    {
        var listing = CduLineText.Listing(A330McduLines.From(A330McduScreen.From(Fixture(fixture))!));
        Assert.Equal(expected, listing);
    }
}
