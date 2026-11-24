using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

// What BA says about a cleared crossing. Distance text arrives ready-made from DistanceFormatter; the class never
// writes a unit. UnitProvider is process-global, hence the shared collection.
[Collection("DistanceUnitGlobalState")]
public class ClearedCrossingCalloutsTests
{
    private static void AllSentences()
    {
        Assert.Equal("Runway 04L at C in 120 metres, cleared to cross.", ClearedCrossingCallouts.Approach("runway 04L at C", "120 metres", first: true));
        Assert.Equal("Runway 04L at C in 60 metres.", ClearedCrossingCallouts.Approach("runway 04L at C", "60 metres", first: false));
        Assert.Equal("Crossing runway 04L at C.", ClearedCrossingCallouts.AtTheLine("runway 04L at C"));
        // Continue released a stop the pilot was cleared across for one of its runways: the crossing of every runway at
        // it, straight after "Continuing.", in the order things happen, then the rest of the sentence as it was.
        Assert.Equal("Continuing. Crossing runway 09 and runway 01. ",
            ClearedCrossingCallouts.Continuing(ClearedCrossingCallouts.AtTheLine("runway 09 and runway 01")));
        Assert.Equal("Continuing. Crossing runway 09 and runway 01. Taxiway K. ",
            ClearedCrossingCallouts.Continuing(ClearedCrossingCallouts.AtTheLine("runway 09 and runway 01")) + "Taxiway K. ");
        // Nothing cleared there: the sentence's own opening, exactly as it was.
        Assert.Equal("Continuing. ", ClearedCrossingCallouts.Continuing(null));
        Assert.Equal("Cleared across runway 22R. No stop.", ClearedCrossingCallouts.EarlyContinue("22R", null));
        Assert.Equal("Cleared across runway 09. Still holding short of runway 01.", ClearedCrossingCallouts.EarlyContinue("09", "runway 01"));
        Assert.Equal("Holding short of runway 04L at C. 80 metres.", ClearedCrossingCallouts.HoldRestored("runway 04L at C", "80 metres"));
        Assert.Equal("Past the hold line of runway 04L at C.", ClearedCrossingCallouts.PastTheLine("runway 04L at C"));
        Assert.Equal("Next stop runway 27 in 300 metres.", ClearedCrossingCallouts.NextStop("runway 27", "300 metres"));
        Assert.Equal(", runway 04L at C in 150 metres, cleared to cross", ClearedCrossingCallouts.StatusClause("runway 04L at C", "150 metres", cleared: true));
        Assert.Equal(", hold short of runway 27 in 300 metres", ClearedCrossingCallouts.StatusClause("runway 27", "300 metres", cleared: false));
        // Every kind of stop, not only runways: the label is the stop's own.
        Assert.Equal("Next stop taxiway L in 120 metres.", ClearedCrossingCallouts.NextStop("taxiway L", "120 metres"));
        Assert.Equal(", hold short of holding point L2 in 90 metres", ClearedCrossingCallouts.StatusClause("holding point L2", "90 metres", cleared: false));
        Assert.Equal("No runway ahead on this route.", ClearedCrossingCallouts.NoRunwayAhead);
        Assert.Equal("No taxi route.", ClearedCrossingCallouts.NoRoute);
    }

    [Fact] public void Every_sentence_is_worded_exactly() => AllSentences();

    [Fact] public void Every_sentence_is_worded_exactly_under_a_comma_decimal_culture()
        => TestCulture.Run("de-DE", () => { AllSentences(); return 0; });

    [Fact] public void The_pilots_feet_option_reaches_the_sentences()
    {
        var saved = DistanceFormatter.UnitProvider;
        try
        {
            DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
            Assert.Equal("Runway 04L at C in 400 feet, cleared to cross.", ClearedCrossingCallouts.Approach("runway 04L at C", DistanceFormatter.FromFeet(400), first: true));
            Assert.Equal("Next stop taxiway L in 400 feet.", ClearedCrossingCallouts.NextStop("taxiway L", DistanceFormatter.FromFeet(400)));
            TestCulture.Run("de-DE", () =>
            {
                Assert.Equal("Next stop taxiway L in 400 feet.", ClearedCrossingCallouts.NextStop("taxiway L", DistanceFormatter.FromFeet(400)));
                return 0;
            });
        }
        finally { DistanceFormatter.UnitProvider = saved; }
    }

    [Fact] public void ClearedCrossingCallouts_never_writes_a_unit_of_its_own()
    {
        string path = Path.Combine(Bbs757SeamsTests.RepoRoot(), "MSFSBlindAssist", "Navigation", "ClearedCrossingHolds.cs");
        string source = File.ReadAllText(path);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\""))
        {
            string literal = m.Value.ToLowerInvariant();
            Assert.DoesNotContain("metre", literal);
            Assert.DoesNotContain("feet", literal);
            Assert.DoesNotContain("foot", literal);
        }
    }
}
