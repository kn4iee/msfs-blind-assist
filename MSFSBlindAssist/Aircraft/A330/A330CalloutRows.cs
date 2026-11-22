using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>
/// The Ctrl+M rows a display call-out adds beyond the variables' own: each area's plan fills its own list
/// (<c>A330CalloutRows.Mcdu.cs</c>, <c>.Ecam.cs</c>, <c>.Pfd.cs</c>, <c>.Nd.cs</c>, <c>.Datalink.cs</c>), and
/// the monitor manager shows them after the variable rows, in the order below. A variable's own call-out gets
/// its row through <c>ExcludeFromMonitorManager</c> false, never from here.
/// </summary>
internal static partial class A330CalloutRows
{
    /// <summary>Every row the plans add, in area order; the rows that existed before stay where they are.</summary>
    public static IEnumerable<(string Key, string Label)> All => Combine(Mcdu, Ecam, Pfd, Nd, Datalink);

    /// <summary>The areas' lists joined in the order the monitor manager shows them.</summary>
    internal static IEnumerable<(string Key, string Label)> Combine(
        IEnumerable<(string Key, string Label)> mcdu, IEnumerable<(string Key, string Label)> ecam,
        IEnumerable<(string Key, string Label)> pfd, IEnumerable<(string Key, string Label)> nd,
        IEnumerable<(string Key, string Label)> datalink)
        => mcdu.Concat(ecam).Concat(pfd).Concat(nd).Concat(datalink);

    /// <summary>The variable rows first, then the added rows in their given order. An added key that is
    /// already a row, a variable's key, or added twice is refused: one checkbox would silence two call-outs.</summary>
    internal static List<MonitorRow> Build(IReadOnlyList<MonitorRow> variableRows,
        IEnumerable<(string Key, string Label)> added, IEnumerable<string> variableKeys)
    {
        var taken = new HashSet<string>(variableKeys, StringComparer.Ordinal);
        foreach (var row in variableRows) taken.Add(row.Key);
        var rows = new List<MonitorRow>(variableRows);
        foreach (var (key, label) in added)
        {
            if (!taken.Add(key))
                throw new ArgumentException($"The call-out row \"{key}\" is already a Ctrl+M row or a variable's key.", nameof(added));
            rows.Add(new MonitorRow(key, label));
        }
        return rows;
    }
}
