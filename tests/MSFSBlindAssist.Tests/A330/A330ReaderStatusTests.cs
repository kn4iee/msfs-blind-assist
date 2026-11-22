using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Tests;

public class A330ReaderStatusTests
{
    private static readonly A330ReaderState[] All = Enum.GetValues<A330ReaderState>();

    [Fact]
    public void Every_state_has_words_and_Running_has_none()
    {
        TestCulture.Run("de-DE", () =>
        {
            foreach (var s in All)
            {
                if (s == A330ReaderState.Running) Assert.Equal("", A330ReaderStatus.Text(s));
                else Assert.False(string.IsNullOrWhiteSpace(A330ReaderStatus.Text(s)), s.ToString());
            }
            Assert.Equal("A330 displays unavailable: the display reader is not installed", A330ReaderStatus.Text(A330ReaderState.NotInstalled));
            Assert.Equal("A330 displays starting", A330ReaderStatus.Text(A330ReaderState.Starting));
            Assert.Equal("A330 displays waiting for the aircraft", A330ReaderStatus.Text(A330ReaderState.SimulatorNotFound));
            Assert.Equal("A330 displays waiting for the aircraft", A330ReaderStatus.Text(A330ReaderState.AircraftNotFound));
            Assert.Equal("A330 displays unavailable: no access to the simulator", A330ReaderStatus.Text(A330ReaderState.NoAccess));
            Assert.Equal("A330 displays unavailable: this simulator version is not recognised yet", A330ReaderStatus.Text(A330ReaderState.NotRecognised));
            Assert.Equal("A330 displays unavailable: the display reader is out of date", A330ReaderStatus.Text(A330ReaderState.OutOfDate));
            Assert.Equal("A330 displays unavailable: the display reader stopped", A330ReaderStatus.Text(A330ReaderState.Failed));
            Assert.Equal("A330 displays stopped", A330ReaderStatus.Text(A330ReaderState.Stopped));
            return 0;
        });
    }

    [Fact]
    public void The_lasting_states_are_the_five_named()
    {
        var expected = new[] { A330ReaderState.NotInstalled, A330ReaderState.NoAccess, A330ReaderState.NotRecognised,
            A330ReaderState.OutOfDate, A330ReaderState.Failed };
        Assert.Equal(expected.OrderBy(x => x), All.Where(A330ReaderStatus.IsLasting).OrderBy(x => x));
    }
}
