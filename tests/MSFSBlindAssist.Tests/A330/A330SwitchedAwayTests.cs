using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Switching away from the A330 closes what it opened.</summary>
public class A330SwitchedAwayTests
{
    [Fact]
    public void Switching_away_closes_the_efb_connection()
    {
        var def = new IniA330Definition();
        // the client a shown EFB window would hold; ShowEfb needs a window, so the field is set by reflection
        var client = CoherentPmdgEfbClient.ForIniA330();
        FieldInfo held = typeof(IniA330Definition).GetField("_efbClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo disposed = typeof(CoherentPmdgEfbClient).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        held.SetValue(def, client);
        Assert.False((bool)disposed.GetValue(client)!);

        def.OnSwitchedAway();

        Assert.True((bool)disposed.GetValue(client)!);
        Assert.Null(held.GetValue(def));
    }
}
