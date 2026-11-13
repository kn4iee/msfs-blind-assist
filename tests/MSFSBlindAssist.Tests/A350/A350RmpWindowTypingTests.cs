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
        // A second page key, on the RMP now picked. The window sends its keys and read-backs in order, so this key going out
        // says the first key's read-back is over, however slow the machine: a fixed wait could end before it ran and pass
        // on nothing.
        processKey.Invoke(window, new object[] { new Message(), Keys.Alt | Keys.V });
        await Until(() => { lock (sent) return sent.Contains(A350Rmp.Press(2, "VHF")); });
        lock (sent) Assert.Equal(new[] { A350Rmp.Press(1, "NAV"), A350Rmp.Press(2, "VHF") }, sent);
        lock (drawn) Assert.Empty(drawn);   // the first key's read-back drew no RMP: not 2's, and 1 is no longer shown
        await Until(() => { lock (drawn) return drawn.Count > 0; });
        lock (drawn) Assert.Equal(new[] { 2 }, drawn);   // the second key's read-back is of its own RMP, so read-backs do draw
    }

    private static async Task Until(Func<bool> met)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!met())
        {
            Assert.True(DateTime.UtcNow < limit, "the window's keys never got that far");
            await Task.Delay(20);
        }
    }
}
