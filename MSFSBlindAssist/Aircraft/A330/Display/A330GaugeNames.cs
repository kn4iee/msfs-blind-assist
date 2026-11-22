namespace MSFSBlindAssist.Aircraft.A330.Display;

/// The pilot's name for each display, as it reads inside a sentence ("Reading the captain's MCDU"). A row that starts
/// with the name capitalises its first letter ("Captain's MCDU is off").
internal static class A330GaugeNames
{
    public static string Name(A330Gauge gauge) => gauge switch
    {
        A330Gauge.McduCaptain => "captain's MCDU",
        A330Gauge.McduFirstOfficer => "first officer's MCDU",
        A330Gauge.Ewd => "E/WD",
        A330Gauge.Sd => "system display",
        A330Gauge.PfdCaptain => "captain's PFD",
        A330Gauge.PfdFirstOfficer => "first officer's PFD",
        A330Gauge.NdCaptain => "captain's ND",
        A330Gauge.NdFirstOfficer => "first officer's ND",
        A330Gauge.DcduCaptain => "captain's DCDU",
        A330Gauge.DcduFirstOfficer => "first officer's DCDU",
        A330Gauge.Isis => "ISIS",
        _ => throw new ArgumentOutOfRangeException(nameof(gauge), gauge, "a display with no name"),
    };
}
