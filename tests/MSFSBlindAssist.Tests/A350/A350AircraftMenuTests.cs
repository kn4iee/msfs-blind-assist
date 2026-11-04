using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The A350's Aircraft menu item (review 2026-11-04, forms).</summary>
public class A350AircraftMenuTests
{
    // "iniBuilds Airbus A&350" took the A380's access key: Alt+A then 3 no longer picked the A380 at once but moved between the two.
    [Fact]
    public void The_a350_takes_no_access_key_the_a380_has()
    {
        string designer = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "MainForm.Designer.cs"));
        char Key(string item)
        {
            var m = Regex.Match(designer, $@"this\.{item}\.Text = ""[^""]*&(.)");
            Assert.True(m.Success, item);
            return char.ToLowerInvariant(m.Groups[1].Value[0]);
        }
        Assert.NotEqual(Key("flyByWireA380MenuItem"), Key("iniBuildsA350MenuItem"));
    }
}
