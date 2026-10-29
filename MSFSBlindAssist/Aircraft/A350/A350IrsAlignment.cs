namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// "IRS aligned", said once as the last IR that is on (its IR pushbutton, <c>INI_IRn_STATE</c>) finishes aligning
/// (<c>INI_IRSn_ALIGNED</c>): what a sighted pilot sees as the alignment memo goes, which the E/WD call-outs never say (a
/// line going is silent). The HS787's words. Judged on whole samples (the definition asks at a batch end, so one IR's
/// delivery is never a half-made change); the first whole sample, and the first after <see cref="Reset"/>, is a silent
/// baseline (parity task 8). Pure.
/// </summary>
public sealed class A350IrsAlignment
{
    public const string Aligned = "IRS aligned";

    private bool? _wasAligned;

    /// <param name="irs">Each IR: on and aligned; null while either is unread.</param>
    public string? Observe(IReadOnlyList<(bool On, bool Aligned)?> irs)
    {
        if (irs.Any(i => i == null))
            return null;
        var on = irs.Select(i => i!.Value).Where(i => i.On).ToList();
        bool aligned = on.Count > 0 && on.All(i => i.Aligned);
        bool? was = _wasAligned;
        _wasAligned = aligned;
        return was == false && aligned ? Aligned : null;
    }

    public void Reset() => _wasAligned = null;
}
