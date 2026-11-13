using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26 Task 7: the A350's windows list (input Ctrl+Shift+F), as the Fenix's, which lists its display windows with its
/// other windows: every A350 window, the displays first, each naming the key that opens it too.</summary>
public class A350WindowsListTests
{
    private static IReadOnlyList<Forms.A350.A350WindowEntry> Entries()
    {
        var def = new IniBuildsA350Definition();
        var sim = new SimConnect.SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        // The entries are only built here: none is opened, so no form or hotkey manager is needed.
        return def.WindowEntries(sim, new SpeechCapture(), null!, null!);
    }

    [Fact]
    public void It_lists_every_window_the_displays_first()
    {
        Assert.Equal(new[] { "PFD", "ND", "E/WD", "SD", "ISIS", "SND", "MFD", "RMP", "Mailbox", "FAP", "Printer" },
            Entries().Select(e => e.Name));
    }

    [Theory]
    [InlineData("PFD", "Alt+P")]
    [InlineData("ND", "Alt+N")]
    [InlineData("E/WD", "Alt+E")]
    [InlineData("SD", "Alt+S")]
    [InlineData("ISIS", "Alt+I")]
    [InlineData("SND", "Alt+Shift+N")]
    [InlineData("MFD", "Shift+M")]
    [InlineData("RMP", "Ctrl+Shift+R")]
    [InlineData("Mailbox", "Ctrl+Shift+D")]
    public void Each_names_the_key_that_opens_it_too(string name, string key) =>
        Assert.Contains(key, Entries().Single(e => e.Name == name).Description);
}
