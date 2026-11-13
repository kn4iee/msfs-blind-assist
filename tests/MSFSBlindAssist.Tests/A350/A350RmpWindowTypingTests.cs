using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>What the RMP window says as a frequency is typed (review 2026-11-04, forms W2).</summary>
public class A350RmpWindowTypingTests
{
    // The window takes each digit in ProcessCmdKey, so no character reaches the screen reader to echo: "1 2 1 . 5" typed into
    // VHF said nothing until Enter. The window says each one, as its squawk entry does.
    [Fact]
    public void Each_character_typed_into_a_frequency_is_said()
    {
        var speech = new SpeechCapture();
        using var window = new A350RmpWindow(_ => null, _ => Task.FromResult<A350DrawCapture?>(null), _ => false, _ => { }, speech);
        foreach (char c in "121.5")
            window.TypeCharacter(c);
        Assert.Equal(new[] { "1", "2", "1", "point", "5" }, speech.Interrupts);
    }

    // Whole-branch review (2026-11-13): a page key's read-back took the RMP from the side box when it ran, after the keys were
    // sent. With another RMP picked meanwhile, the key went to RMP 1 and RMP 2's screen was read as what it changed. The
    // read-back is of the RMP the key went to, and with another picked it reads nothing.
    [Fact]
    public async Task A_page_keys_read_back_is_of_the_rmp_the_key_went_to()
    {
        var drawn = new List<int>();
        var sent = new List<string>();
        using var window = new A350RmpWindow(_ => null, rmp => { lock (drawn) drawn.Add(rmp); return Task.FromResult<A350DrawCapture?>(null); },
            _ => false, rpn => { lock (sent) sent.Add(rpn); }, new SpeechCapture());
        var processKey = typeof(A350RmpWindow).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        processKey.Invoke(window, new object[] { new Message(), Keys.Alt | Keys.N });
        window.SideBox.SelectedIndex = 1;   // another RMP, before the read-back runs
        await Task.Delay(1200);             // the key gap, then the read-back's wait
        lock (sent) Assert.Contains(A350Rmp.Press(1, "NAV"), sent);
        lock (drawn) Assert.DoesNotContain(2, drawn);
    }
}
