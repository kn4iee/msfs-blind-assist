namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The two members of the app's <c>IniA330Definition</c> that the linked <c>A330Write.cs</c> names. The definition
/// itself cannot be linked (it needs the whole app), so this tool restates them. <see cref="DefaultPushMs"/> is the
/// press hold every planned press carries: <c>The_sweep_tools_press_hold_is_the_apps</c> (tests, A330) keeps it equal
/// to the app's.
/// </summary>
internal static class IniA330Definition
{
    /// <summary>The app's momentary-key hold (IniA330Definition.Handlers.cs).</summary>
    internal const int DefaultPushMs = 150;

    /// <summary>Only <c>A330Write.ParkingBrake</c> calls it, and the sweep never writes the parking brake (denied).</summary>
    internal static double ParkingBrakeInput(bool set) =>
        throw new InvalidOperationException("The panel sweep never writes the parking brake: it is on the deny list.");
}
