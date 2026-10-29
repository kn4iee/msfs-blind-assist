using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The A350 display captures the tests read (A350/Fixtures), each loaded fresh, so a test that changes one never
/// changes another's.</summary>
internal static class A350Fixtures
{
    public static string Dir => Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures");

    /// <summary>The capture saved as <paramref name="file"/> (its name with ".json").</summary>
    public static A350DrawCapture Capture(string file) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(Dir, file)))!;
}
