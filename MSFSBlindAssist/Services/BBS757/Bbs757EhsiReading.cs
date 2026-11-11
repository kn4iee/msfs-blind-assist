namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI's map frame as BlueBird holds it: the point the map is centered on, the rotation (the true heading, the true
/// track with track up, 0 in PLAN), where that point sits on the screen, and the screen's size in pixels. The geometry
/// projects every item through these seven values, so they are read, never derived.
/// </summary>
public readonly record struct Bbs757EhsiMap(
    double CenterLatitude,
    double CenterLongitude,
    double Rotation,
    double ScreenX,
    double ScreenY,
    double Width,
    double Height);

/// <summary>The draw's own constants, the layout's values, carried in every reading so the rows need no layout: the map's
/// radius factor, the aircraft's Y factor and the clip inset (<see cref="Bbs757EhsiGeometry.Project"/>,
/// <see cref="Bbs757EhsiGeometry.OnScreen"/>), the margin a point may lie beyond the range, the ground speed under which no
/// ETA is drawn, and the background item count at which EXCESS DATA shows.</summary>
public readonly record struct Bbs757EhsiConstants(
    double RadiusFactor,
    double AircraftYFactor,
    double ClipInset,
    double MarkerMargin,
    double MinEtaGroundSpeed,
    int ExcessLimit);

/// <summary>One leg of a route as BlueBird draws it: its label (empty for a discontinuity), its position in degrees (0, 0
/// for a discontinuity, which is its own leg: nothing is drawn or invented across it), its path terminator,
/// <see cref="Manual"/> (a manual-termination leg, whose top-line distance is dashes), <see cref="Symbol"/> (whether the
/// map draws a symbol and a label for it) and the constraint and ETA the DATA switch adds (ETA -1: none).</summary>
public readonly record struct Bbs757EhsiLeg(
    string Label,
    double Latitude,
    double Longitude,
    bool Discontinuity,
    int Terminator,
    bool Manual,
    bool Symbol,
    int ConstraintKind,
    int ConstraintFirst,
    int ConstraintSecond,
    double Eta);

/// <summary>A point the map draws that is not a leg: T/C, T/D, E/D or an airport, with the screen's own word for it.</summary>
public readonly record struct Bbs757EhsiPoint(string Word, bool Airport, double Latitude, double Longitude);

/// <summary>
/// The Captain's EHSI as read from memory. A light reading (every tick) leaves <see cref="Route"/>,
/// <see cref="Modification"/> and <see cref="Points"/> null and still carries <see cref="Active"/>: the leg the top line
/// uses (leg 0 of the modification while one is pending). <see cref="Powered"/> false: every other field is its default and
/// the reading is not corrupt (a dark screen holds anything). <see cref="Corrupt"/>: a torn or out-of-range read, with
/// no route, modification or points at all (never partial). <see cref="Constants"/> is the layout's in every reading,
/// powered or not.
/// </summary>
public sealed record Bbs757EhsiReading(
    bool Powered,
    string? Mode,
    bool Centered,
    double Range,
    bool RangeChanging,
    bool Valid,
    bool Aligning,
    double Number,
    bool TrackUp,
    bool TrueReference,
    double HeadingBug,
    double Latitude,
    double Longitude,
    double GroundSpeed,
    double Clock,
    Bbs757EhsiMap Map,
    Bbs757EhsiConstants Constants,
    Bbs757EhsiLeg? Active,
    bool RouteActive,
    bool DataSwitch,
    int ExcessCount,
    IReadOnlyList<Bbs757EhsiLeg>? Route,
    IReadOnlyList<Bbs757EhsiLeg>? Modification,
    IReadOnlyList<Bbs757EhsiPoint>? Points,
    bool Corrupt) : IBbs757Reading;
