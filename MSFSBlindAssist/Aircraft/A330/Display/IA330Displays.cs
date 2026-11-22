namespace MSFSBlindAssist.Aircraft.A330.Display;

public interface IA330Displays
{
    A330ReaderState State { get; }
    string StatusText { get; }                                 // A330ReaderStatus.Text(State)
    void Want(A330Gauge gauge, object owner, int refreshMs);   // read this display about every refreshMs until Release
    void Release(A330Gauge gauge, object owner);
    A330Frame? Latest(A330Gauge gauge);                        // the newest COMPLETE frame no older than three refreshes; else null
    A330Frame? Newest(A330Gauge gauge);                        // the newest frame, complete or not
    string? McduScratchpad(bool firstOfficer);                 // the MCDU's scratchpad text as the reader gives it; null when it gives none
    // The two events are raised on any thread (the reader's output thread, a pool thread, or the caller's own for Start
    // and Stop), and two of them can arrive out of order: a handler marshals to the UI thread and reads State or
    // Latest there, rather than trusting the argument alone.
    event Action<A330Gauge>? FrameChanged;                     // a complete frame differs from the last complete one
    event Action<A330ReaderState>? StateChanged;               // the state changed
}
