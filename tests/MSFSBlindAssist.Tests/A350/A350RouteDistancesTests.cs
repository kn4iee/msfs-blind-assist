using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// D and Shift+D from the A350's own VNAV variables, FG_VNAV_DIST_DEST and INI_TD_DIST_TOTAL (both in its var table): the
/// iniBuilds A300, the same vendor's FMS with the same names, measured their meanings in flight (the route distance to the
/// destination; the distance to the top of descent, negative once past it). Said in the PMDGs' and FBW Airbuses' words.
/// </summary>
public class A350RouteDistancesTests
{
    private static string NoEta(double nm, double knots) => "";
    private static string Eta(double nm, double knots) => $": eta {nm.ToString(CultureInfo.InvariantCulture)} at {knots.ToString(CultureInfo.InvariantCulture)}";

    [Fact]
    public void The_destination_reads_its_miles_and_the_time_at_the_ground_speed()
    {
        Assert.Equal("541 miles to destination: eta 541.4 at 423", A350RouteDistances.Destination(541.4, 423, Eta));
        Assert.Equal("25 miles to destination", A350RouteDistances.Destination(24.6, 0, NoEta));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    [InlineData(double.NaN)]
    public void No_route_is_not_available_never_zero_miles(double destinationNm)
    {
        Assert.Equal("Distance to destination not available", A350RouteDistances.Destination(destinationNm, 450, Eta));
        Assert.Equal("Top of descent not available", A350RouteDistances.TopOfDescent(destinationNm, 120, 450, Eta));
    }

    [Fact]
    public void The_top_of_descent_reads_ahead_not_yet_computed_or_past()
    {
        Assert.Equal("443 miles to top of descent: eta 443 at 470", A350RouteDistances.TopOfDescent(541, 443, 470, Eta));
        Assert.Equal("Top of descent not yet computed", A350RouteDistances.TopOfDescent(541, 0, 470, Eta));
        Assert.Equal("Past top of descent", A350RouteDistances.TopOfDescent(24.6, -81.2, 300, Eta));
        Assert.Equal("Past top of descent", A350RouteDistances.TopOfDescent(60, 0.5, 300, Eta));
        Assert.Equal("Top of descent not available", A350RouteDistances.TopOfDescent(541, double.NaN, 470, Eta));
    }

    [Fact]
    public void A_half_mile_rounds_up_whatever_the_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1 mile to destination", A350RouteDistances.Destination(0.5, 0, NoEta));
            Assert.Equal("1235 miles to top of descent", A350RouteDistances.TopOfDescent(2000, 1234.5, 0, NoEta));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>D and Shift+D read the two variables as streamed (each rides the batch, consumed silently) and the ground
    /// speed for the time; unread, they are not available.</summary>
    [Fact]
    public void D_and_shift_D_read_the_aircrafts_own_distances()
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var def = new MSFSBlindAssist.Aircraft.IniBuildsA350Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        var sim = new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        string[] Say(params MSFSBlindAssist.Hotkeys.HotkeyAction[] actions)
        {
            var speech = new SpeechCapture();
            foreach (var action in actions)
                Assert.True(def.HandleHotkeyAction(action, sim, speech, null!, null!));
            return speech.All.ToArray();
        }

        var both = new[] { MSFSBlindAssist.Hotkeys.HotkeyAction.ReadDistanceToDest, MSFSBlindAssist.Hotkeys.HotkeyAction.ReadDistanceToTOD };
        Assert.Equal(new[] { "Distance to destination not available", "Top of descent not available" }, Say(both));

        cache[def.KeyForInput(A350RouteDistances.DestinationVar)] = 541.4;
        cache[def.KeyForInput(A350RouteDistances.TopOfDescentVar)] = 443;
        cache["GROUND_VELOCITY"] = 423;
        Assert.Equal(new[] { "541 miles to destination: 01:16:48", "443 miles to top of descent: 01:02:50" }, Say(both));
        Assert.True(def.GetVariables()[def.KeyForInput(A350RouteDistances.DestinationVar)].ExcludeFromMonitorManager);
    }
}
