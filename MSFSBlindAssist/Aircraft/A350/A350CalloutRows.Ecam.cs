namespace MSFSBlindAssist.Aircraft.A350;

internal static partial class A350CalloutRows
{
    /// <summary>The E/WD call-outs' rows.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Ecam = new[]
    {
        (Display.A350EcamMonitor.ReverserKey, Display.A350EcamMonitor.ReverserLabel),
    };
}
