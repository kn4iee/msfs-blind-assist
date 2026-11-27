namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>What one step of a control's sweep does. The runner turns each into a write through the
/// definition's own write path and a read of the control's read-back and proof.</summary>
public enum SweepStepKind
{
    /// <summary>Press the ECAM page key whose page shows this control's proof.</summary>
    PressEcamPage,
    /// <summary>Open the guard that covers the switch.</summary>
    OpenGuard,
    /// <summary>Write one of the control's other positions (or one point of a continuous control).</summary>
    SetPosition,
    /// <summary>The one press of a button that reports a state.</summary>
    Press,
    /// <summary>Extra presses that bring a multi-press key back to where it began.</summary>
    RestorePress,
    /// <summary>Write the position the control started in.</summary>
    ReturnToStart,
    /// <summary>Close the guard again.</summary>
    CloseGuard,
}

/// <summary>One step. <see cref="ControlKey"/> is the control it acts on (the guard's or the ECAM key's
/// for those steps); <see cref="Input"/> the value to write (null for a press); <see cref="ExpectedRead"/>
/// the read-back the map gives that position, null where there is none (a press, a continuous point).</summary>
public sealed record SweepStep(SweepStepKind Kind, string ControlKey, double? Input, double? ExpectedRead, string Text)
{
    public override string ToString() => Text;
}

/// <summary>The plan for one control: its steps in order, or no steps and the reason it is skipped.</summary>
public sealed record ControlPlan(string Key, string Label, IReadOnlyList<SweepStep> Steps, string? SkipReason)
{
    public bool IsPlanned => SkipReason == null;
}

/// <summary>The whole plan: every control of the map once, in the map's order, planned or skipped.</summary>
public sealed record SweepPlan(IReadOnlyList<ControlPlan> Controls)
{
    public int PlannedCount => Controls.Count(c => c.IsPlanned);
    public int SkippedCount => Controls.Count - PlannedCount;
}
