namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// Ctrl+M rows of the A350's display call-outs, one list per area (E/WD, PFD, ND, datalink), each in a file of its own
/// (A350CalloutRows.Ecam.cs, .Pfd.cs, .Nd.cs, .Datalink.cs) so that two call-outs in different areas never edit one line.
/// A row is the key the pilot's unticking is stored under, and the label the dialog shows.
/// <see cref="Forms.A350.A350MonitorManagerForm.BuildRows"/> lists the variables' rows and the display call-outs' rows it
/// names itself, then <see cref="All"/>.
/// </summary>
internal static partial class A350CalloutRows
{
    /// <summary>Every row the areas add, in area order.</summary>
    public static IEnumerable<(string Key, string Label)> All => Ecam.Concat(Pfd).Concat(Nd).Concat(Datalink);
}
