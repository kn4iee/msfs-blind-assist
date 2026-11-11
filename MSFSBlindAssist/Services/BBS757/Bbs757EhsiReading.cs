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
