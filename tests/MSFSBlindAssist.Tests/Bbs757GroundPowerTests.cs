using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 EFB window's ground power buttons, as far as they need no simulator. BlueBird's Ground Services
/// GPU tile TOGGLES <c>L:BBS_757_GPU_AC_PWR_CONNECTED</c> (measured 2026-10-25, session 8, and again
/// 2026-10-26), so the tile is tapped only when that variable reads the other way from the wish and, for a
/// disconnect, external power is not on line. This pins what is said instead of a tap and after one, what
/// the Ground Power row says when it changes, and the tile's click sequence.
/// </summary>
public class Bbs757GroundPowerTests
{
    private const string Unread = "The ground power cannot be read, so nothing was tapped.";
    private const string AlreadyConnected = "Ground power is already connected.";
    private const string AlreadyDisconnected = "Ground power is already disconnected.";
    private const string OnLine =
        "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or the engines " +
        "powering the aircraft, then try again.";
    private const string ExtPwrUnread = "External power cannot be read, so nothing was tapped.";

    private const string Connected = "Ground power connected. Press EXT PWR on the Electrical panel to put it on line.";
    private const string Disconnected = "Ground power disconnected.";
    private const string NotConnected = "BlueBird did not connect the ground power.";
    private const string NotDisconnected = "BlueBird did not disconnect the ground power.";
    private const string ResultUnread = "The ground power cannot be read now.";

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- whether the tile is tapped -------------------------------------------------------------

    [Theory]
    // The connection unread: nothing says which way a tap would go, whatever else reads.
    [InlineData(true, null, null, Unread)]
    [InlineData(true, null, 0.0, Unread)]
    [InlineData(true, double.NaN, 0.0, Unread)]
    [InlineData(false, null, 0.0, Unread)]
    [InlineData(false, null, 1.0, Unread)]                       // before the EXT PWR refusal
    [InlineData(false, double.PositiveInfinity, null, Unread)]
    [InlineData(false, double.NegativeInfinity, 0.0, Unread)]
    // A connect while connected, whatever EXT PWR reads: a tap would disconnect it.
    [InlineData(true, 1.0, 0.0, AlreadyConnected)]
    [InlineData(true, 1.0, null, AlreadyConnected)]
    [InlineData(true, 1.0, 1.0, AlreadyConnected)]
    [InlineData(true, 0.51, null, AlreadyConnected)]             // above 0.5 reads 1
    // A disconnect while disconnected, ahead of the EXT PWR refusals: a tap would connect it.
    [InlineData(false, 0.0, 0.0, AlreadyDisconnected)]
    [InlineData(false, 0.0, 1.0, AlreadyDisconnected)]
    [InlineData(false, 0.0, null, AlreadyDisconnected)]
    [InlineData(false, 0.5, 0.0, AlreadyDisconnected)]           // 0.5 and below reads 0
    // A disconnect while connected: external power on line, or its state unread.
    [InlineData(false, 1.0, 1.0, OnLine)]
    [InlineData(false, 1.0, 0.51, OnLine)]
    [InlineData(false, 1.0, null, ExtPwrUnread)]
    [InlineData(false, 1.0, double.NaN, ExtPwrUnread)]
    [InlineData(false, 1.0, double.PositiveInfinity, ExtPwrUnread)]
    public void Refusals(bool connect, double? connected, double? extPwrOn, string expected)
    {
        Assert.Equal(expected, Bbs757GroundPower.Refusal(connect, connected, extPwrOn));
    }

    [Theory]
    [InlineData(true, 0.0, null, true)]            // disconnected, connecting: tap (a connect never looks at EXT PWR)
    [InlineData(true, 0.0, 0.0, true)]
    [InlineData(true, 0.0, 1.0, true)]
    [InlineData(true, 0.0, double.NaN, true)]
    [InlineData(true, 0.5, null, true)]            // 0.5 still reads 0 ...
    [InlineData(true, 0.51, null, false)]          // ... and above it reads 1
    [InlineData(false, 1.0, 0.0, true)]            // connected, EXT PWR off: tap
    [InlineData(false, 0.7, 0.5, true)]
    [InlineData(true, 1.0, 0.0, false)]            // already connected: a tap would undo it
    [InlineData(false, 0.0, 0.0, false)]           // already disconnected: a tap would undo it
    [InlineData(false, 1.0, 1.0, false)]           // EXT PWR on line: refused before any tap
    [InlineData(false, 1.0, null, false)]          // EXT PWR unread
    [InlineData(false, 1.0, double.NaN, false)]
    [InlineData(true, null, 0.0, false)]           // unread: never guess on a toggle
    [InlineData(false, null, 0.0, false)]
    [InlineData(true, double.NaN, null, false)]
    [InlineData(false, double.PositiveInfinity, 0.0, false)]
    public void Taps_only_when_different(bool connect, double? connected, double? extPwrOn, bool tap)
    {
        Assert.Equal(tap, Bbs757GroundPower.Refusal(connect, connected, extPwrOn) is null);
    }

    // ---- what is said after the taps ------------------------------------------------------------

    [Theory]
    [InlineData(true, 0.0, 1.0, Connected)]
    [InlineData(false, 1.0, 0.0, Disconnected)]
    [InlineData(true, 0.0, 0.0, NotConnected)]
    [InlineData(false, 1.0, 1.0, NotDisconnected)]
    [InlineData(true, 0.0, null, ResultUnread)]
    [InlineData(false, 1.0, null, ResultUnread)]
    [InlineData(true, 0.0, double.NaN, ResultUnread)]
    [InlineData(false, 1.0, double.PositiveInfinity, ResultUnread)]
    [InlineData(true, 0.0, 0.51, Connected)]                     // above 0.5 reads 1
    [InlineData(true, 0.0, 0.5, NotConnected)]                   // 0.5 and below reads 0
    // Only the read after decides, whatever was read before.
    [InlineData(true, null, 1.0, Connected)]
    [InlineData(false, double.NaN, 0.0, Disconnected)]
    [InlineData(true, 1.0, 1.0, Connected)]
    [InlineData(true, 1.0, 0.0, NotConnected)]
    [InlineData(false, 0.0, 1.0, NotDisconnected)]
    [InlineData(true, null, null, ResultUnread)]
    public void Results(bool connect, double? before, double? after, string expected)
    {
        Assert.Equal(expected, Bbs757GroundPower.Result(connect, before, after));
    }

    [Theory]
    [InlineData(1.0, "Ground power connected")]
    [InlineData(0.0, "Ground power disconnected")]
    [InlineData(0.51, "Ground power connected")]
    [InlineData(0.5, "Ground power disconnected")]
    [InlineData(2.0, "Ground power connected")]
    [InlineData(-1.0, "Ground power disconnected")]
    [InlineData(double.NaN, null)]                               // not finite: nothing to say
    [InlineData(double.PositiveInfinity, null)]
    [InlineData(double.NegativeInfinity, null)]
    public void A_change_of_the_ground_power_row_says_connected_or_disconnected(double value, string? phrase)
    {
        Assert.Equal(phrase, Bbs757GroundPower.ChangePhrase(value));
    }

    [Fact]
    public void Every_phrase_is_the_same_under_a_comma_decimal_culture()
    {
        // Values with a fraction beside the ones that read as unread: no phrase may follow the PC's culture.
        double?[] values = { null, double.NaN, 0.0, 0.3, 0.5, 0.7, 1.0 };

        List<string?> Phrases()
        {
            var said = new List<string?>();
            foreach (bool connect in new[] { true, false })
            {
                foreach (double? a in values)
                {
                    said.Add(Bbs757GroundPower.Result(connect, a, a));
                    foreach (double? b in values) said.Add(Bbs757GroundPower.Refusal(connect, a, b));
                }
            }
            foreach (double v in values.OfType<double>()) said.Add(Bbs757GroundPower.ChangePhrase(v));
            return said;
        }

        List<string?> expected = new();
        InCulture("", () => expected = Phrases());

        Assert.Contains(Connected, expected);
        Assert.Contains(OnLine, expected);
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(expected, Phrases()));
    }

    // ---- the variables and the run windows ---------------------------------------------------------

    [Fact]
    public void The_variable_names_and_the_run_windows_are_pinned()
    {
        Assert.Equal("BBS_757_GPU_AC_PWR_CONNECTED", Bbs757GroundPower.ConnectedVar);
        Assert.Equal("B757_OVHD_EXT_PWR_BUTTON_ON", Bbs757GroundPower.ExtPwrOnVar);
        // The window's own OnRequest key for the L:var the announced EXT PWR row also reads: another key.
        Assert.Equal("BBS757_EFB_EXT_PWR_ON", Bbs757GroundPower.ExtPwrOnKey);
        Assert.NotEqual(Bbs757SharedKeys.ExtPwrOn, Bbs757GroundPower.ExtPwrOnKey);
        Assert.Equal(60_000, Bbs757GroundPower.WindowRunMaxMs);
        Assert.Equal(8000, Bbs757GroundPower.WindowRunEchoMs);
        // No run outlasts its window: the four taps take six seconds.
        Assert.True(Bbs757EfbLayout.TotalMs(Bbs757EfbLayout.ToggleGroundPower()) < Bbs757GroundPower.WindowRunMaxMs);
    }

    // ---- the click sequence -----------------------------------------------------------------------

    [Fact]
    public void Sequence_ends_on_the_gpu_tile()
    {
        // Measured 2026-10-25 (session 8) and again 2026-10-26: the same four taps, 1.5 s apart, toggled the
        // ground power each time they ran.
        var seq = Bbs757EfbLayout.ToggleGroundPower();

        Assert.Equal(new[]
        {
            Bbs757EfbLayout.Home, Bbs757EfbLayout.AircraftIcon, Bbs757EfbLayout.AircraftMenuGroundServices,
            Bbs757EfbLayout.GpuTile,
        }, seq.Select(s => s.Point));
        Assert.Equal(new EfbPoint(1005, 231), Bbs757EfbLayout.GpuTile);
        Assert.Single(seq, s => s.Point == Bbs757EfbLayout.GpuTile);   // one tap: it toggles
        Assert.All(seq, s => Assert.Equal(Bbs757EfbLayout.NavigateDelayMs, s.DelayAfterMs));
    }
}
