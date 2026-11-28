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
    /// <summary>Write the same position again, right after its <see cref="SetPosition"/>: a write that flips the
    /// read back finds an input event that toggles instead of setting by value.</summary>
    RepeatPosition,
    /// <summary>The one press of a button that reports a state.</summary>
    Press,
    /// <summary>Extra presses that bring a multi-press key back to where it began.</summary>
    RestorePress,
    /// <summary>Put the control back where it started. Its <see cref="SweepStep.Input"/> is the map's pairing for
    /// the start, shown in the dry run; the runner never writes it: it restores from the journal, by the input
    /// event's own starting value (an inverted pairing would "restore" to the wrong side).</summary>
    ReturnToStart,
    /// <summary>Close the guard again; restored from the journal like <see cref="ReturnToStart"/>.</summary>
    CloseGuard,
}

/// <summary>One step. <see cref="ControlKey"/> is the control it acts on (the guard's or the ECAM key's
/// for those steps); <see cref="Input"/> the value to write (null for a press); <see cref="ExpectedRead"/>
/// the read-back the map gives that position, null where there is none (a press, a continuous point). A key of a
/// <see cref="KeyFamily"/>: the value the map says its press selects, and for the unit's <see cref="SweepStepKind.ReturnToStart"/>
/// the start, with the key that selects it.</summary>
public sealed record SweepStep(SweepStepKind Kind, string ControlKey, double? Input, double? ExpectedRead, string Text)
{
    public override string ToString() => Text;
}

/// <summary>The plan for one control: its steps in order, or no steps and the reason it is skipped. A
/// <see cref="Family"/> unit is keyed by its shared state variable, and its keys' own plans say they are pressed with it.</summary>
public sealed record ControlPlan(string Key, string Label, IReadOnlyList<SweepStep> Steps, string? SkipReason, KeyFamily? Family = null)
{
    public bool IsPlanned => SkipReason == null;
}

/// <summary>The whole plan: every control of the map once, in the map's order, planned or skipped, and each family of
/// keys that share one state as one unit, just before its first key.</summary>
public sealed record SweepPlan(IReadOnlyList<ControlPlan> Controls)
{
    public int PlannedCount => Controls.Count(c => c.IsPlanned);
    public int SkippedCount => Controls.Count - PlannedCount;
}
