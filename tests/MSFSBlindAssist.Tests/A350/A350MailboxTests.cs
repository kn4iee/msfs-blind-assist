using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

// The ATC mailbox, live 2026-10-30: a departure clearance request transferred from the MFD's ATC COM, not sent.
public class A350MailboxTests
{
    private static A350DrawCapture Draft() => A350Fixtures.Capture("ctr_mailbox_draft.json");

    [Fact]
    public void A_request_reads_its_header_and_body_then_its_buttons()
    {
        var spoken = A350Mailbox.Read(Draft()).Select(l => l.Spoken).ToList();
        Assert.Equal(new[]
        {
            "1112Z TO VCBI, green",
            "REQUEST PREDEP, on cyan",
            "CLEARANCE UAE111 A359, on cyan",
            "TO OMDB AT VCBI STAND, on cyan",
            "A6 ATIS A, on cyan",
        }, spoken.Where(s => !s.EndsWith(" button", StringComparison.Ordinal)));
        Assert.Contains("PRINT button", spoken);
        Assert.Contains("SEND button", spoken);
        Assert.Contains("CANCEL button", spoken);
    }

    // Review 3 (M8): a key the mailbox greys read as a working one.
    [Fact]
    public void A_button_says_its_colour_unless_it_is_white()
    {
        var capture = Draft();
        var send = capture.Texts.First(t => t.Text.Trim() == "SEND");
        var grey = capture with { Texts = capture.Texts.Select(t => t.Text.Trim() == "SEND" ? t with { Colour = new(0.5f, 0.5f, 0.5f, 1) } : t).ToList() };
        var spoken = A350Mailbox.Read(grey).Select(l => l.Spoken).ToList();
        Assert.Contains("SEND button, grey", spoken);
        Assert.Contains("PRINT button", spoken);
    }

    [Fact]
    public void A_button_drawn_over_another_hides_it()
    {
        var spoken = A350Mailbox.Read(Draft()).Select(l => l.Spoken).ToList();
        Assert.DoesNotContain("CLOSE button", spoken);
        Assert.Equal(3, spoken.Count(s => s.EndsWith(" button", StringComparison.Ordinal)) - spoken.Count(s => s.Contains("page button", StringComparison.Ordinal)));
    }

    [Fact]
    public void Its_arrows_are_named_where_they_are_drawn()
    {
        var spoken = A350Mailbox.Read(Draft()).Select(l => l.Spoken).ToList();
        Assert.Contains("Previous page button", spoken);
        Assert.Contains("Next page button", spoken);
        Assert.DoesNotContain("Previous message button", spoken);   // one message: the arrows are not drawn
    }

    [Fact]
    public void With_two_messages_it_says_the_count_and_draws_the_message_arrows()
    {
        var spoken = A350Mailbox.Read(A350Fixtures.Capture("ctr_mailbox_two_drafts.json")).Select(l => l.Spoken).ToList();
        Assert.Contains("MSG 1/2, white", spoken);
        Assert.Contains("REQUEST PREDEP, on cyan", spoken);   // the count's rows never join the message's
        Assert.Contains("Previous message button", spoken);
        Assert.Contains("Next message button", spoken);
    }

    [Fact]
    public void A_button_is_clicked_at_its_middle_and_a_text_line_not_at_all()
    {
        var lines = A350Mailbox.Read(Draft());
        var cancel = lines.Single(l => l.Spoken == "CANCEL button");
        Assert.Equal(1474 + 71, cancel.ClickX);
        Assert.Equal(61 + 23.5f, cancel.ClickY);
        Assert.Equal(-1, lines.First().ClickX);
    }

    [Fact]
    public void With_no_power_for_it_the_mailbox_says_so_and_has_no_buttons()
    {
        var spoken = A350Mailbox.Read(A350Fixtures.Capture("ctr_parked_apu.json")).Select(l => l.Spoken).ToList();
        Assert.Contains("MAILBOX NOT AVAIL, amber", spoken);
    }

    [Fact]
    public void Its_lines_are_never_the_warning_displays()
    {
        var warning = A350Ewd.Parse(Draft()).Warning.Select(l => l.Spoken).ToList();
        Assert.DoesNotContain(warning, s => s.Contains("REQUEST PREDEP", StringComparison.Ordinal) || s.Contains("PRINT", StringComparison.Ordinal) || s.Contains("VCBI", StringComparison.Ordinal));
        Assert.DoesNotContain(A350Ewd.Parse(A350Fixtures.Capture("ctr_parked_apu.json")).Warning, l => l.Spoken.Contains("MAILBOX", StringComparison.Ordinal));
    }

    [Fact]
    public void The_alt_e_window_reads_it_above_the_warning_display()
    {
        string text = A350EwdText.Build(Draft());
        int mailbox = text.IndexOf("Mailbox:", StringComparison.Ordinal), warning = text.IndexOf("Warning display:", StringComparison.Ordinal);
        Assert.True(mailbox >= 0 && mailbox < warning, text);
        Assert.Contains("REQUEST PREDEP, on cyan", text);
    }

    [Fact]
    public void The_window_shows_a_dark_screen_and_nothing_to_press()
    {
        var (lines, shown) = A350MailboxWindow.PageLines(Draft(), dark: true);
        Assert.Empty(lines);
        Assert.Equal(new[] { MSFSBlindAssist.Aircraft.IniBuildsA350Definition.DarkScreen }, shown);
        Assert.Empty(A350MailboxWindow.PageLines(null, dark: false).Lines);
    }

    [Fact]
    public async Task A_press_brings_the_cursor_to_the_mailbox_then_clicks_there()
    {
        var sent = new List<string>();
        var clicks = new List<(float, float)>();
        var input = new A350MailboxInput(sent.Add, (_, _) => Task.CompletedTask, (x, y, _) => { clicks.Add((x, y)); return Task.FromResult(true); });
        Assert.Equal(MfdAction.Done, await input.ActAsync(1545, 84.5f));
        Assert.Equal(new[] { "1 (>L:INI_MCDU1_MAILBOX)", "0 (>L:INI_MCDU1_MAILBOX)" }, sent);
        Assert.Equal((1545f, 84.5f), clicks.Single());
    }

    [Fact]
    public async Task A_click_that_cannot_land_says_so()
    {
        var input = new A350MailboxInput(_ => { }, (_, _) => Task.CompletedTask, (_, _, _) => Task.FromResult(false));
        Assert.Equal(MfdAction.Unreachable, await input.ActAsync(1, 1));
        Assert.False(input.Busy);
    }

    [Theory]
    [InlineData("12|coui://x?wasm_module=inibuilds-A350&wasm_gauge=CTR", 12)]
    [InlineData("12|coui://x?wasm_module=inibuilds-A350&wasm_gauge=MFD", null)]
    [InlineData("12|coui://x?wasm_module=other&wasm_gauge=CTR", null)]
    [InlineData("", null)]
    public void The_view_is_known_by_its_gauge(string guidAndUrl, int? guid) => Assert.Equal(guid, A350MailboxInput.CtrGuidIf(guidAndUrl));
}
