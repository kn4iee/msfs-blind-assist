using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 09 Task 1: every page the A350's MFD menus reach (walked on 1.2.8 at VCBI, 2026-11-03), with its KCCU key where one
/// exists and its menu clicks; the window's page buttons and its "Go to MFD page" list.</summary>
public class A350MfdPagesTests
{
    [Fact]
    public void Every_page_has_a_path()
    {
        Assert.Equal(33, A350MfdPages.All.Count);   // 32 in the menus, and DIRECT TO, which only its key and a waypoint reach
        Assert.All(A350MfdPages.All, p => Assert.True(p.KccuKey != null || p.MenuClicks.Count >= 2, p.Label));
        Assert.Equal(A350MfdPages.All.Count, A350MfdPages.All.Select(p => p.Label).Distinct().Count());
        // The walk's own clicks: the system selector, FMS 1, ACTIVE, then its list's fourth item, WIND.
        var wind = A350MfdPages.All.Single(p => p.Label == "ACTIVE: WIND");
        Assert.Null(wind.KccuKey);
        Assert.Equal(new[] { (116, 26), (105, 66), (115, 77), (115, 251) }, wind.MenuClicks);
        Assert.Equal("RADNAV", A350MfdPages.All.Single(p => p.Label == "POSITION: NAVAIDS").KccuKey);
        Assert.Equal(new[] { (116, 26), (105, 130), (536, 77) }, A350MfdPages.All.Single(p => p.Label == "SURV: TRAFFIC").MenuClicks);
    }

    [Fact]
    public void The_buttons_press_the_same_keys_as_the_alt_letters()
    {
        Assert.Equal(15, A350MfdWindow.PageButtons.Count);
        foreach (var (text, key) in A350MfdWindow.PageButtons)
        {
            int amp = text.IndexOf('&');
            Assert.True(amp >= 0 && amp < text.Length - 1, text);
            var letter = (Keys)char.ToUpperInvariant(text[amp + 1]);
            Assert.Equal(A350MfdWindow.PageKeys[letter], key);
        }
    }

    // A closed drop-down list fires a change on every arrow: arrowing over pages navigates once, to where the arrowing stopped.
    [Fact]
    public void A_settled_selection_navigates_once()
    {
        var choice = new A350SettledChoice(TimeSpan.FromMilliseconds(350));
        var t0 = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        choice.Changed(1, t0);
        choice.Changed(2, t0.AddMilliseconds(100));
        choice.Changed(3, t0.AddMilliseconds(200));
        Assert.Null(choice.Due(t0.AddMilliseconds(400)));
        Assert.Equal(3, choice.Due(t0.AddMilliseconds(600)));
        Assert.Null(choice.Due(t0.AddMilliseconds(900)));
    }

    // In the air ATC COM's VERTICAL list offers ALT/FL grey: a page item drawn grey is not offered, so nothing is pressed.
    [Fact]
    public void A_greyed_page_presses_nothing()
    {
        var lines = A350MfdPage.Read(A350Fixtures.Capture("p09_mfd_atc_vertical_menu.json"), 0);
        Assert.False(A350MfdPages.Offered(lines, 485, 251));   // ALT/FL button, grey
        Assert.True(A350MfdPages.Offered(lines, 485, 171));    // CLIMB TO button
        Assert.False(A350MfdPages.Offered(lines, 300, 300));   // nothing there
    }
}
