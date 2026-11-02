namespace MSFSBlindAssist.Aircraft.A350;

internal static partial class A350CalloutRows
{
    /// <summary>The ND call-outs' rows.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Nd = new[]
    {
        (A350WaypointCallout.Key, A350WaypointCallout.Label),
    };
}
