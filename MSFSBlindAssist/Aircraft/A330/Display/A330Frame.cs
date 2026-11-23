namespace MSFSBlindAssist.Aircraft.A330.Display;

public enum A330Gauge { McduCaptain, McduFirstOfficer, Ewd, Sd, PfdCaptain, PfdFirstOfficer, NdCaptain, NdFirstOfficer, DcduCaptain, DcduFirstOfficer, Isis }

public readonly record struct A330Rgb(float R, float G, float B);

/// One text as the display drew it. X is where its first character starts, Baseline its text line, in the display's own pixels;
/// CharX is each character's start; Order is its place in the display's drawing order.
/// Colour is null while the reader gives none (spec M14: not found yet with its current approach); a consumer then says no colour word.
public sealed record A330Text(int Order, float X, float Baseline, string Text, string Font, float Size, A330Rgb? Colour, IReadOnlyList<float> CharX);

/// One frame of one display. Complete: the reader vouches that no text the display drew is missing.
/// Missing: how many characters the frame lacks (0 when complete).
/// Drawn is false when the display shows nothing (the cockpit unpowered). Such a frame has no texts and is not complete;
/// it is a state, not a failure.
/// HiddenRowsLeftOut is true only when the reader vouches that rows the display does not show (the rest of a list) are left
/// out; false until spec M15 is solved. On the wire "hiddenRowsLeftOut":true, absent meaning false.
/// List is true when the frame has a list the display clips (shows only some of its rows); ListAbove when rows of that list
/// are hidden above the ones shown, ListBelow when rows of it are hidden below them. All three are false for a frame
/// without such a list, and while the reader does not say. On the wire "list":true, "listAbove":true and "listBelow":true,
/// each absent meaning false.
public sealed record A330Frame(A330Gauge Gauge, IReadOnlyList<A330Text> Texts, bool Complete, int Missing, long ReadTicks, bool Drawn = true, bool HiddenRowsLeftOut = false,
    bool List = false, bool ListAbove = false, bool ListBelow = false);
