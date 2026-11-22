namespace MSFSBlindAssist.Aircraft.A330.Display;

public enum A330ReaderState { NotInstalled, Starting, Running, SimulatorNotFound, NoAccess, AircraftNotFound, NotRecognised, OutOfDate, Failed, Stopped }

public static class A330ReaderStatus
{
    /// The pilot's words for a state; empty while Running.
    public static string Text(A330ReaderState state) => state switch
    {
        A330ReaderState.NotInstalled => "A330 displays unavailable: the display reader is not installed",
        A330ReaderState.Starting => "A330 displays starting",
        A330ReaderState.Running => "",
        A330ReaderState.SimulatorNotFound => "A330 displays waiting for the aircraft",
        A330ReaderState.AircraftNotFound => "A330 displays waiting for the aircraft",
        A330ReaderState.NoAccess => "A330 displays unavailable: no access to the simulator",
        A330ReaderState.NotRecognised => "A330 displays unavailable: this simulator version is not recognised yet",
        A330ReaderState.OutOfDate => "A330 displays unavailable: the display reader is out of date",
        A330ReaderState.Failed => "A330 displays unavailable: the display reader stopped",
        A330ReaderState.Stopped => "A330 displays stopped",
        _ => "",
    };

    /// True for a state that lasts until the pilot or an update changes something.
    public static bool IsLasting(A330ReaderState state) => state is
        A330ReaderState.NotInstalled or A330ReaderState.NoAccess or A330ReaderState.NotRecognised
        or A330ReaderState.OutOfDate or A330ReaderState.Failed;
}
