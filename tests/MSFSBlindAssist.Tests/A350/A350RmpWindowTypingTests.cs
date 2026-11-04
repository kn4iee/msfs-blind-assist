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
}
