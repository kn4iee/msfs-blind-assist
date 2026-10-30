namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The A350's doors, as the A380's (its <c>_doorDefs</c>): each the stock <c>INTERACTIVE POINT OPEN:n</c>, 0 to 1, open
/// past 0.05, with the same points 0 to 12 in all six presets' flight_model.cfg (2026-10-30). Named as MSFSBA's OIS window
/// names them (its agent's <c>A.doorName</c>: "Door L1", "Forward cargo door"). The fuel connection (point 11,
/// FUEL_CONT_RIGHT_WING) reads connected or disconnected, as the A380's fuel truck does; the GPU connection (point 10) is
/// left out, as EXT 1 and 2 say "Available" when it connects. Pure.
/// </summary>
public static class A350Doors
{
    public sealed record Door(string Key, string Name, int Point, bool Connection = false)
    {
        public string Var => $"INTERACTIVE POINT OPEN:{Point}";
    }

    public static readonly IReadOnlyList<Door> All = new Door[]
    {
        new("A350_DOOR_L1", "Door L1", 0), new("A350_DOOR_R1", "Door R1", 1),
        new("A350_DOOR_L2", "Door L2", 2), new("A350_DOOR_R2", "Door R2", 3),
        new("A350_DOOR_L3", "Door L3", 4), new("A350_DOOR_R3", "Door R3", 5),
        new("A350_DOOR_L4", "Door L4", 9), new("A350_DOOR_R4", "Door R4", 8),
        new("A350_DOOR_CARGO_FWD", "Forward cargo door", 7), new("A350_DOOR_CARGO_AFT", "Aft cargo door", 6),
        new("A350_DOOR_CARGO_BULK", "Bulk cargo door", 12),
        new("A350_FUEL_TRUCK", "Fuel truck", 11, Connection: true),
    };

    /// <summary>The row the doors are shown beside: the Doors and Hatches panel's.</summary>
    public const string PanelRow = "A350_COCKPIT_DOOR_LOCK";

    public static bool IsOpen(double value) => value > 0.05;

    public static string State(Door door, bool open) =>
        door.Connection ? (open ? "Connected" : "Disconnected") : (open ? "Open" : "Closed");

    public static string Said(Door door, bool open) => $"{door.Name} {State(door, open).ToLowerInvariant()}";
}
