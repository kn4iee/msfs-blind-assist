namespace MSFSBlindAssist.Aircraft.A350;

internal static partial class A350CalloutRows
{
    /// <summary>The PFD call-outs' rows: one per message the PFD flags (plan 07, question 6); the rest stay on the PFD flags row.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Pfd =
        Display.A350PfdFlagAnnouncer.Messages.Select(m => (Display.A350PfdFlagAnnouncer.KeyFor(m), m + " call-out")).ToList();
}
