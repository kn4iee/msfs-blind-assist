namespace MSFSBlindAssist.Aircraft.A330;

internal static partial class A330CalloutRows
{
    /// <summary>The FMS messages call-out: a message on the captain's MCDU scratchpad line, spoken with the MCDU
    /// window closed, and the FMS message lamps while the frames cannot say the message.</summary>
    public const string FmsMessageKey = "A330_CALLOUT_FMS_MESSAGE";

    /// <summary>Plan 03's rows.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Mcdu = new[] { (FmsMessageKey, "FMS messages") };
}
