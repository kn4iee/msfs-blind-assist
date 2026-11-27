using Position = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Position;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>What one write of the sweep showed (plan 04, Task 4 Step 2).</summary>
internal enum SweepVerdict
{
    /// <summary>The proof (an indication the control does not write itself) followed the position's name.</summary>
    Proven,
    /// <summary>The proof followed the other name: the pairing of names and values is swapped.</summary>
    Inverted,
    /// <summary>The read moved to the position written, and there is no proof the runner can read.</summary>
    ReadOnly,
    /// <summary>Nothing moved: not the read, not the proof, no other variable, in <see cref="SweepTiming.LimitMs"/>.</summary>
    Dead,
    /// <summary>A pass (proven or read-only) that settled only after <see cref="SweepTiming.LaggedMs"/>: its time goes to plan 06.</summary>
    Lagged,
    /// <summary>The same position written a second time moved the read again: the input event toggles.</summary>
    Toggle,
    /// <summary>The write was refused before it left (an input event the aircraft does not list, the calculator not
    /// answering, the simulator refusing the packet): never called dead.</summary>
    NotSent,
    /// <summary>The read went to another position than the one written (or to none of them), or did not move while
    /// other variables did: a position's input or value, or the control's read, is wrong (Step 5's "mislabeled").</summary>
    Mislabeled,
}

/// <summary>How the runner reaches a control's proof: not at all, as a variable it reads itself (a lamp or a
/// simvar), or only through the display reader (an E/WD memo, a lower ECAM page), which this tool does not reach.</summary>
internal enum ProofReach { None, Variable, DisplayReader }

/// <summary>A proof around one write. <see cref="ExpectedShown"/>: whether the indication should show at the position
/// written (from the proof's <c>when</c>); null when there is nothing to compare with.</summary>
internal sealed record ProofReading(ProofReach Reach, double? Before = null, SettleResult? After = null, bool? ExpectedShown = null, string? Text = null);

/// <summary>Everything one write of a position showed, for <see cref="SweepVerdicts.Classify"/>.</summary>
internal sealed record WriteObservation
{
    /// <summary>False: refused before it left (<see cref="NotSentReason"/> says why).</summary>
    public bool Sent { get; init; } = true;
    public string? NotSentReason { get; init; }
    public double? ReadBefore { get; init; }
    /// <summary>The control's read after the first write.</summary>
    public SettleResult? Read { get; init; }
    /// <summary>What the map says the read shows at the position written; null for a press or a continuous point.</summary>
    public double? ExpectedRead { get; init; }
    /// <summary>The control's positions, to name where a read went.</summary>
    public IReadOnlyList<Position> Positions { get; init; } = Array.Empty<Position>();
    public ProofReading Proof { get; init; } = new(ProofReach.None);
    /// <summary>The read after the same position was written again; null when it was not.</summary>
    public SettleResult? Repeat { get; init; }
    /// <summary>How many other variables of the snapshot moved with the write.</summary>
    public int SideEffects { get; init; }
}

internal sealed record Judgement(SweepVerdict Verdict, string Note)
{
    public string Word => SweepVerdicts.Word(Verdict);
}

/// <summary>Turns what a write showed into its verdict. Pure. A read of the control's own variable never proves a
/// name (it can read back exactly what was written while the aircraft ignored it); only a proof can.</summary>
internal static class SweepVerdicts
{
    public static string Word(SweepVerdict v) => v switch
    {
        SweepVerdict.Proven => "proven",
        SweepVerdict.Inverted => "inverted",
        SweepVerdict.ReadOnly => "read-only",
        SweepVerdict.Dead => "dead",
        SweepVerdict.Lagged => "lagged",
        SweepVerdict.Toggle => "toggle",
        SweepVerdict.NotSent => "not sent",
        _ => "mislabeled",
    };

    /// <summary>A verdict that counts as a pass: the control went where it was sent (lagged is still one).</summary>
    public static bool IsPass(SweepVerdict v) => v is SweepVerdict.Proven or SweepVerdict.ReadOnly or SweepVerdict.Lagged;

    public static Judgement Classify(WriteObservation o)
    {
        if (!o.Sent) return new(SweepVerdict.NotSent, o.NotSentReason ?? "");

        var notes = new List<string>();
        bool readMoved = o.Read?.Moved == true;
        bool proofReadable = o.Proof.Reach == ProofReach.Variable;
        bool proofMoved = proofReadable && o.Proof.After?.Moved == true;

        if (!readMoved && !proofMoved && o.SideEffects == 0)
            return new(SweepVerdict.Dead, o.Read?.TimedOut == true ? "the read never settled" : "nothing moved");

        if (readMoved && o.Repeat is { Moved: true } again)
            return new(SweepVerdict.Toggle, SweepTiming.Same(again.Value, o.ReadBefore)
                ? "the second write flipped it back"
                : $"the second write moved it again, to {SweepTsv.Num(again.Value)}");

        // where the read went, against the position written
        string? wentElsewhere = null;
        if (readMoved && o.ExpectedRead is double expected && o.Read!.Value is double got && !SweepTiming.Same(got, expected))
        {
            var at = o.Positions.FirstOrDefault(p => SweepTiming.Same(p.Value, got));
            var meant = o.Positions.FirstOrDefault(p => SweepTiming.Same(p.Value, expected));
            wentElsewhere = $"the read went to {(at != null ? $"{at.Label} ({SweepTsv.Num(got)})" : $"{SweepTsv.Num(got)}, which is no position")}"
                + $", not {(meant != null ? $"{meant.Label} ({SweepTsv.Num(expected)})" : SweepTsv.Num(expected))}";
        }

        SweepVerdict verdict;
        if (proofReadable && o.Proof.ExpectedShown is bool shouldShow && o.Proof.After?.Value is double shown
            && (readMoved || proofMoved) && (shown > 0.5) == shouldShow)
        {
            verdict = SweepVerdict.Proven;
            if (wentElsewhere != null) notes.Add(wentElsewhere);
            if (!readMoved) notes.Add("the read did not move");
        }
        else if (proofReadable && o.Proof.ExpectedShown is bool && proofMoved)
        {
            verdict = SweepVerdict.Inverted;
            notes.Add($"the proof {(o.Proof.ExpectedShown == true ? "did not show" : "showed")} at this position");
            if (wentElsewhere != null) notes.Add(wentElsewhere);
        }
        else
        {
            if (proofReadable) notes.Add(o.Proof.ExpectedShown is bool ? "the proof did not follow" : "the proof has no position to compare with");
            if (o.Proof.Reach == ProofReach.DisplayReader) notes.Add($"proof: needs the display reader ({o.Proof.Text})");
            if (!readMoved)
            {
                verdict = SweepVerdict.Mislabeled;
                notes.Add(proofMoved ? "the read did not move, the proof did" : "the read did not move, other variables did");
            }
            else if (wentElsewhere != null)
            {
                verdict = SweepVerdict.Mislabeled;
                notes.Add(wentElsewhere);
            }
            else verdict = SweepVerdict.ReadOnly;
        }

        if (verdict is SweepVerdict.Proven or SweepVerdict.ReadOnly)
        {
            int? slowest = Max(readMoved ? o.Read?.SettledMs : null, proofMoved ? o.Proof.After?.SettledMs : null);
            bool stillMoving = (readMoved && o.Read!.TimedOut) || (proofMoved && o.Proof.After!.TimedOut);
            if (stillMoving || slowest > SweepTiming.LaggedMs)
            {
                notes.Insert(0, stillMoving
                    ? $"{Word(verdict)}, still moving after {SweepTiming.LimitMs} ms"
                    : $"{Word(verdict)} after {slowest} ms");
                verdict = SweepVerdict.Lagged;
            }
        }
        return new(verdict, string.Join("; ", notes));
    }

    private static int? Max(int? a, int? b) => a is int x ? (b is int y ? Math.Max(x, y) : x) : b;
}
