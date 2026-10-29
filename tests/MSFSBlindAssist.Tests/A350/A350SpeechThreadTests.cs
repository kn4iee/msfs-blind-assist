using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests.A350;

// Review 2026-10-29 (OIS I3): the A350 reader speaks from its worker thread and the two monitors from pool threads, while
// the announcer speaks only from the UI thread (off it a line is lost unheard, as [A380C-22] measured on the A380). Every
// line MainForm takes from them is posted to the UI thread before it is spoken.
public class A350SpeechThreadTests
{
    [Fact]
    public void Every_A350_display_line_is_spoken_on_the_UI_thread()
    {
        string body = StartA350DisplayServiceBody();
        int spoken = Regex.Matches(body, @"announcer\.Announce\w*\(").Count;
        int posted = Regex.Matches(body, @"SafeBeginInvoke\(\(\) => announcer\.Announce\(").Count;
        Assert.True(spoken >= 3, "the reader, the E/WD monitor and the PFD monitor each speak");
        Assert.Equal(spoken, posted);
    }

    private static string StartA350DisplayServiceBody()
    {
        string text = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "MainForm.AircraftSwitch.cs"));
        int start = text.IndexOf("private void StartA350DisplayService()", StringComparison.Ordinal);
        Assert.True(start >= 0, "StartA350DisplayService not found in MainForm.AircraftSwitch.cs");
        int end = text.IndexOf("private void StopA350DisplayService(", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of StartA350DisplayService not found");
        return text.Substring(start, end - start);
    }
}
