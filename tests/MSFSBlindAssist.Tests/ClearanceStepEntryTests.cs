// Tests for MSFSBlindAssist.Navigation.ClearanceStepEntry: the Taxi Guidance window's step block as data (the step
// kinds, the name box's label, the Alt+letter kinds, the refusal with nothing chosen, and the step a pick makes), and
// the window's access keys, read from its source.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceStepEntryTests
{
    private static ClearanceNameLists.Item Taxiway(string name) => new(name, ClearanceTargetKind.Taxiway, name);

    private static ClearanceNameLists.Item Runway(string name) => new(name, ClearanceTargetKind.Runway, name);

    private static NamedHoldingPoint Point(string name, int node, string kind = "runway")
        => new() { Name = name, NodeId = node, Kind = kind };

    [Fact]
    public void The_kinds_are_listed_in_order_each_with_its_own_first_letter()
    {
        Assert.Equal(new[] { "Via taxiway", "Left onto taxiway", "Right onto taxiway", "Cross", "Hold short of" },
            ClearanceStepEntry.KindItems);
        Assert.Equal(ClearanceStepEntry.KindItems.Count,
            ClearanceStepEntry.KindItems.Select(k => char.ToUpperInvariant(k[0])).Distinct().Count());
        // Index for index with the kinds the planner reads.
        Assert.Equal(ClearanceStepKind.Via, ClearanceStepEntry.KindAt(0));
        Assert.Equal(ClearanceStepKind.LeftOnto, ClearanceStepEntry.KindAt(1));
        Assert.Equal(ClearanceStepKind.RightOnto, ClearanceStepEntry.KindAt(2));
        Assert.Equal(ClearanceStepKind.Cross, ClearanceStepEntry.KindAt(3));
        Assert.Equal(ClearanceStepKind.HoldShort, ClearanceStepEntry.KindAt(4));
    }

    [Fact]
    public void Nothing_selected_in_the_kind_list_reads_as_via()
    {
        Assert.Equal(ClearanceStepKind.Via, ClearanceStepEntry.KindAt(-1));
        Assert.Equal(ClearanceStepKind.Via, ClearanceStepEntry.KindAt(5));
    }

    [Theory]
    [InlineData('V', ClearanceStepKind.Via)]
    [InlineData('L', ClearanceStepKind.LeftOnto)]
    [InlineData('R', ClearanceStepKind.RightOnto)]
    [InlineData('X', ClearanceStepKind.Cross)]
    [InlineData('H', ClearanceStepKind.HoldShort)]
    [InlineData('x', ClearanceStepKind.Cross)]
    public void Each_kind_has_its_own_access_key(char letter, ClearanceStepKind kind)
        => Assert.Equal(kind, ClearanceStepEntry.KindForAccessKey(letter));

    [Theory]
    [InlineData('K')]
    [InlineData('W')]
    [InlineData('D')]
    [InlineData('C')]
    [InlineData('3')]
    public void Other_letters_pick_no_kind(char letter)
        => Assert.Null(ClearanceStepEntry.KindForAccessKey(letter));

    [Theory]
    [InlineData(ClearanceStepKind.Via, "Taxi&way:", "Taxiway")]
    [InlineData(ClearanceStepKind.LeftOnto, "Taxi&way:", "Taxiway")]
    [InlineData(ClearanceStepKind.RightOnto, "Taxi&way:", "Taxiway")]
    [InlineData(ClearanceStepKind.Cross, "Runway or taxi&way to cross:", "Runway or taxiway to cross")]
    [InlineData(ClearanceStepKind.HoldShort, "Hold short of runway or taxi&way:", "Hold short of runway, taxiway or holding point")]
    public void The_name_box_follows_the_kind_and_keeps_Alt_W(ClearanceStepKind kind, string label, string accessibleName)
    {
        Assert.Equal(label, ClearanceStepEntry.NameBoxLabel(kind));
        Assert.Equal(accessibleName, ClearanceStepEntry.NameBoxAccessibleName(kind));
        Assert.Equal(1, label.Count(c => c == '&'));
        Assert.False(string.IsNullOrWhiteSpace(ClearanceStepEntry.NameBoxDescription(kind)));
    }

    // Review focus 1: Enter in the name box with nothing chosen adds no step, and says why.
    [Fact]
    public void Enter_with_nothing_chosen_is_refused_with_the_reason()
    {
        Assert.Equal("Choose a name first.", ClearanceStepEntry.Refusal(null));
        Assert.Null(ClearanceStepEntry.Refusal(Taxiway("B")));
    }

    // Remove Step on an empty list is the window's only other refusal; the screen reader reads the list otherwise.
    [Fact]
    public void Remove_step_on_an_empty_list_says_so()
        => Assert.Equal("No step to remove.", ClearanceStepEntry.NoStepToRemove);

    [Theory]
    [InlineData(ClearanceStepKind.Via, "Via B")]
    [InlineData(ClearanceStepKind.LeftOnto, "Left onto B")]
    [InlineData(ClearanceStepKind.RightOnto, "Right onto B")]
    public void A_via_kind_names_a_taxiway(ClearanceStepKind kind, string sentence)
    {
        var step = ClearanceStepEntry.Build(kind, Taxiway("B"));
        Assert.Equal(kind, step.Kind);
        Assert.Equal(ClearanceTargetKind.Taxiway, step.TargetKind);
        Assert.Equal("B", step.Name);
        Assert.Equal(sentence, step.Sentence());
    }

    [Fact]
    public void A_crossing_at_a_taxiway_is_said_with_its_place()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), atLabel: "P");

        Assert.Equal(ClearanceTargetKind.Runway, step.TargetKind);
        Assert.Equal("P", step.At);
        Assert.Equal(0, step.AtNodeId);
        Assert.Equal("Step 2: cross runway 24L at P.", TestCulture.Run("de-DE", () => step.Spoken(2)));
        Assert.Equal("Step 2: cross runway 24L at P.", TestCulture.Run("sv-SE", () => step.Spoken(2)));
    }

    [Fact]
    public void A_crossing_with_no_place_named_has_no_at()
    {
        var none = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("04L"), atLabel: ClearanceStepEntry.NoAtPlace);
        var hidden = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("04L"));

        Assert.Null(none.At);
        Assert.Null(hidden.At);
        Assert.Equal("Cross runway 04L", none.Sentence());
    }

    [Fact]
    public void A_crossing_at_a_holding_point_takes_its_name_and_node()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("23R"),
            atLabel: "P1" + HoldShortTargetList.HoldingPointSuffix, atPoint: Point("P1", 4711));

        Assert.Equal("P1", step.At);
        Assert.Equal(4711, step.AtNodeId);
        Assert.False(step.AtNodeIsPassedStop);
        Assert.Equal("Step 3: cross runway 23R at P1.", TestCulture.Run("de-DE", () => step.Spoken(3)));
    }

    [Fact]
    public void Crossing_a_taxiway_takes_no_place()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Taxiway("B"), atLabel: "P", atPoint: Point("P1", 9));

        Assert.Equal(ClearanceTargetKind.Taxiway, step.TargetKind);
        Assert.Null(step.At);
        Assert.Equal(0, step.AtNodeId);
        Assert.Equal("Cross taxiway B", step.Sentence());
    }

    [Fact]
    public void Hold_short_names_a_runway_or_a_taxiway()
    {
        var runway = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("04R"));
        var taxiway = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Taxiway("L"));

        Assert.Equal("Step 4: hold short of runway 04R.", TestCulture.Run("sv-SE", () => runway.Spoken(4)));
        Assert.Equal("Step 1: hold short of taxiway L.", TestCulture.Run("de-DE", () => taxiway.Spoken(1)));
    }

    [Fact]
    public void Hold_short_of_a_holding_point_carries_its_node_kind_and_source()
    {
        var painted = Point("L2", 77, "intermediate");
        var fromSign = Point("KK", 88, NamedHoldingPointResolver.SignKind);

        var a = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item(HoldShortTargetList.HoldingPointLabel(painted), ClearanceTargetKind.HoldingPoint, "L2", painted));
        var b = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item(HoldShortTargetList.HoldingPointLabel(fromSign), ClearanceTargetKind.HoldingPoint, "KK", fromSign));

        Assert.Equal((ClearanceTargetKind.HoldingPoint, "L2", 77, "intermediate", false),
            (a.TargetKind, a.Name, a.NodeId, a.HoldingPointKind, a.FromSign));
        Assert.True(b.FromSign);
        Assert.Equal(88, b.NodeId);
        Assert.Equal("Step 2: hold short of L2 (holding point).", TestCulture.Run("de-DE", () => a.Spoken(2)));
    }

    // ---- The window's access keys (read from its source) -------------------------------------

    private static string FormSource()
        => File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

    // Every access key a control's Text gives in the window ("Show &fitting only" -> 'F'), with the name box's own.
    private static List<char> AccessKeys()
    {
        var keys = new List<char>();
        foreach (Match m in Regex.Matches(FormSource(), "Text = \"([^\"\\n]*)\""))
        {
            string text = m.Groups[1].Value;
            int amp = text.IndexOf('&');
            if (amp >= 0 && amp + 1 < text.Length) keys.Add(char.ToUpperInvariant(text[amp + 1]));
        }
        keys.Add('W');   // the name box's label, ClearanceStepEntry.NameBoxLabel
        return keys;
    }

    // Alt+V, Alt+L, Alt+R, Alt+X and Alt+H pick a step kind from anywhere in the window (ProcessCmdKey): no
    // control may take one of those letters.
    [Fact]
    public void No_control_takes_a_step_kind_letter()
    {
        var keys = AccessKeys();
        foreach (char letter in new[] { 'V', 'L', 'R', 'X', 'H' })
            Assert.DoesNotContain(letter, keys);
    }

    [Fact]
    public void The_step_block_keys_and_the_CAT_III_box_each_have_one_control()
    {
        var keys = AccessKeys();
        foreach (char letter in new[] { 'K', 'W', 'D', 'U', 'M', 'Z', '3' })
            Assert.Equal(1, keys.Count(k => k == letter));

        string form = FormSource();
        Assert.Contains("Text = \"CAT &3 / low-visibility hold (LVP)\"", form);
        Assert.Contains("AccessibleName = \"CAT three, low visibility hold\"", form);
    }
}
