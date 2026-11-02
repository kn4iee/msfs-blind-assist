using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The flight of 2026-10-31: the landing memo (LDG, SIGNS: ON, CABIN: READY, …) was said in full between 18:06:29 and
// 18:08:47, went when the AP OFF warning took the main column (18:10:17), and was said in full again at 18:10:22 as the
// warning cleared. The owner's answer (question 3): a memo line back unchanged within 10 s is not said again; a warning or
// a caution (red or amber in the main column) is always said.
public class A350MemoHoldTests
{
    private DateTime _now = new(2026, 10, 31, 18, 6, 29, DateTimeKind.Utc);

    private const float Main = 905, MemoColumn = 1253;

    private static EcamLine Line(string text, string colour, float x, int row) =>
        new(new[] { new DrawnCell(text, colour, x, 313 + row * 36) }, 313 + row * 36, false, EcamBox.None);

    private static A350EwdView View(params EcamLine[] lines) => new(
        Array.Empty<IReadOnlyList<DrawnCell>>(), lines, Array.Empty<IReadOnlyList<DrawnCell>>(), Array.Empty<IReadOnlyList<DrawnCell>>(),
        lines.Length > 0);

    private static readonly EcamLine[] LandingMemo =
    {
        Line("LDG", "white", Main, 0), Line("SIGNS: ON", "green", Main, 1), Line("CABIN: READY", "green", Main, 2),
        Line("LDG GEAR DOWN", "green", Main, 3), Line("FLAPS: LDG", "green", Main, 4), Line("GND SPLR: ARM", "green", Main, 5),
        Line("LDG INHIBIT", "magenta", MemoColumn, 0), Line("WXR/PWS OFF", "amber", MemoColumn, 1),
    };

    /// <summary>Two reads of one view, the time moving on between them; what the second read says.</summary>
    private IReadOnlyList<string> Twice(A350EcamAnnouncer a, A350EwdView view, int afterSeconds = 1)
    {
        _now = _now.AddSeconds(afterSeconds);
        Assert.Empty(a.Observe(view));
        _now = _now.AddSeconds(0.5);
        return a.Observe(view);
    }

    private A350EcamAnnouncer Started(params EcamLine[] lines)
    {
        var a = new A350EcamAnnouncer(() => _now);
        Twice(a, View(lines));   // the baseline
        return a;
    }

    [Fact]
    public void The_landing_memo_back_after_an_ap_off_warning_is_not_said_again()
    {
        var a = Started();
        Assert.Equal(LandingMemo.Length, Twice(a, View(LandingMemo)).Count);   // said once, as the memo came
        Assert.Equal(new[] { "AUTO FLT AP OFF, red" }, Twice(a, View(Line("AUTO FLT AP OFF", "red", Main, 0)), afterSeconds: 60));
        Assert.Empty(Twice(a, View(LandingMemo), afterSeconds: 4));   // back 5 s later: not said again
    }

    [Fact]
    public void A_memo_back_after_10_s_is_said()
    {
        var a = Started(LandingMemo);
        Twice(a, View(Line("AUTO FLT AP OFF", "red", Main, 0)));
        Assert.Equal(LandingMemo.Length, Twice(a, View(LandingMemo), afterSeconds: 11).Count);
    }

    [Fact]
    public void An_amber_caution_back_within_10_s_is_said_again()
    {
        var caution = Line("HYD G SYS LO PR", "amber", Main, 0);
        var a = Started(caution);
        Twice(a, View());
        Assert.Equal(new[] { "HYD G SYS LO PR, amber" }, Twice(a, View(caution), afterSeconds: 2));
    }

    [Fact]
    public void A_red_warning_back_within_10_s_is_said_again()
    {
        var warning = Line("AUTO FLT AP OFF", "red", Main, 0);
        var a = Started(warning);
        Twice(a, View());
        Assert.Equal(new[] { "AUTO FLT AP OFF, red" }, Twice(a, View(warning), afterSeconds: 2));
    }

    [Fact]
    public void An_amber_line_in_the_memo_column_is_a_memo()
    {
        var wxr = Line("WXR/PWS OFF", "amber", MemoColumn, 0);
        var a = Started(wxr);
        Twice(a, View());
        Assert.Empty(Twice(a, View(wxr), afterSeconds: 2));
    }

    [Fact]
    public void A_memo_back_changed_is_said()
    {
        var a = Started(Line("SIGNS: ON", "green", Main, 0));
        Twice(a, View());
        Assert.Equal(new[] { "SIGNS: OFF, green" }, Twice(a, View(Line("SIGNS: OFF", "green", Main, 0)), afterSeconds: 2));
    }

    [Fact]
    public void A_flight_load_forgets_the_lines_that_went()
    {
        var memo = Line("PARK BRK", "green", Main, 0);
        var a = Started(memo);
        Twice(a, View());   // went
        a.Reset();          // a flight load
        Twice(a, View());   // the new flight's baseline
        Assert.Equal(new[] { "PARK BRK, green" }, Twice(a, View(memo), afterSeconds: 2));
    }
}
