// Tests for MSFSBlindAssist.Navigation.ClearanceStep: one step of an ATC clearance and the
// sentence that reads it back. Every sentence is also checked under a comma-decimal culture.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceStepTests
{
    private static ClearanceStep Step(ClearanceStepKind kind, ClearanceTargetKind target, string name,
        string? at = null, bool fromSign = false)
        => new(kind, target, name, NodeId: 7, FromSign: fromSign, At: at);

    private static void Check(string expected, ClearanceStep step)
    {
        Assert.Equal(expected, step.Sentence());
        Assert.Equal(expected, TestCulture.Run("de-DE", step.Sentence));
    }

    [Fact]
    public void Via_ReadsViaName()
        => Check("Via A", Step(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "A"));

    [Fact]
    public void LeftOnto_ReadsLeftOntoName()
        => Check("Left onto P", Step(ClearanceStepKind.LeftOnto, ClearanceTargetKind.Taxiway, "P"));

    [Fact]
    public void RightOnto_ReadsRightOntoName()
        => Check("Right onto C", Step(ClearanceStepKind.RightOnto, ClearanceTargetKind.Taxiway, "C"));

    [Fact]
    public void CrossRunway_ReadsCrossRunway()
        => Check("Cross runway 24L", Step(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L"));

    [Fact]
    public void CrossRunway_WithAt_NamesTheTaxiway()
        => Check("Cross runway 24L at P", Step(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", at: "P"));

    [Fact]
    public void CrossTaxiway_ReadsCrossTaxiway()
        => Check("Cross taxiway B", Step(ClearanceStepKind.Cross, ClearanceTargetKind.Taxiway, "B"));

    [Fact]
    public void HoldShortRunway_ReadsHoldShortOfRunway()
        => Check("Hold short of runway 04R", Step(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "04R"));

    [Fact]
    public void HoldShortTaxiway_ReadsHoldShortOfTaxiway()
        => Check("Hold short of taxiway L", Step(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "L"));

    [Fact]
    public void HoldShortHoldingPoint_IsMarked()
        => Check("Hold short of A1 (holding point)", Step(ClearanceStepKind.HoldShort, ClearanceTargetKind.HoldingPoint, "A1"));

    [Fact]
    public void HoldShortSignHoldingPoint_UsesTheSignSuffix()
        => Check("Hold short of A1 (holding point, from sign, approximate)",
            Step(ClearanceStepKind.HoldShort, ClearanceTargetKind.HoldingPoint, "A1", fromSign: true));

    [Fact]
    public void Spoken_NumbersTheStepAndLowerCasesTheFirstLetter()
    {
        var cross = Step(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", at: "P");
        var left = Step(ClearanceStepKind.LeftOnto, ClearanceTargetKind.Taxiway, "P");
        Assert.Equal("Step 2: cross runway 24L at P.", cross.Spoken(2));
        Assert.Equal("Step 1: left onto P.", left.Spoken(1));
        Assert.Equal("Step 2: cross runway 24L at P.", TestCulture.Run("de-DE", () => cross.Spoken(2)));
        Assert.Equal("Step 1: left onto P.", TestCulture.Run("de-DE", () => left.Spoken(1)));
    }

    [Theory]
    [InlineData(ClearanceStepKind.Via, true)]
    [InlineData(ClearanceStepKind.LeftOnto, true)]
    [InlineData(ClearanceStepKind.RightOnto, true)]
    [InlineData(ClearanceStepKind.Cross, false)]
    [InlineData(ClearanceStepKind.HoldShort, false)]
    public void IsVia_IsTrueForTheTaxiwayKinds(ClearanceStepKind kind, bool expected)
        => Assert.Equal(expected, Step(kind, ClearanceTargetKind.Taxiway, "A").IsVia);
}
