namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The Ctrl+M rows the A350's display call-outs add after the rows that existed, one list per area (E/WD, PFD, ND,
/// datalink), each in a file of its own (A350CalloutRows.Ecam.cs, .Pfd.cs, .Nd.cs, .Datalink.cs) so that two call-outs in
/// different areas never edit one line. A row is the key the pilot's unticking is stored under, and the label the dialog
/// shows. The rows that existed stay in <see cref="Forms.A350.A350MonitorManagerForm.BuildRows"/>, which lists
/// <see cref="All"/> after its last.
/// </summary>
internal static partial class A350CalloutRows
{
    /// <summary>Every row the areas add, in area order.</summary>
    public static IEnumerable<(string Key, string Label)> All => Ecam.Concat(Pfd).Concat(Nd).Concat(Datalink);
}
