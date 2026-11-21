using MSFSBlindAssist.SimConnect;
using static MSFSBlindAssist.SimConnect.SimConnectManager;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The decision behind SimConnectManager.RefreshInputEventReads: when to enumerate the aircraft's
/// input events again and when to subscribe from the catalogue already held.
/// </summary>
public class A330InputEventCatalogueTests
{
    [Fact]
    public void A_menu_switch_to_reads_after_a_load_under_a_profile_without_reads_enumerates()
    {
        // 1. A load under the PMDG 737 profile (no reads): a catalogue is held, nothing enumerates.
        Assert.Equal(InputEventRefreshAction.Nothing,
            DecideInputEventRefresh(catalogueEmpty: false, catalogueStale: false, wantsReads: false, reenumerate: true));
        // 2. The catalogue is now older than the load. The menu switch to the A330 must enumerate.
        Assert.Equal(InputEventRefreshAction.Enumerate,
            DecideInputEventRefresh(catalogueEmpty: false, catalogueStale: true, wantsReads: true, reenumerate: false));
    }

    [Fact]
    public void A_menu_switch_with_a_fresh_catalogue_subscribes_from_the_cache()
        => Assert.Equal(InputEventRefreshAction.ResubscribeFromCache,
            DecideInputEventRefresh(catalogueEmpty: false, catalogueStale: false, wantsReads: true, reenumerate: false));

    [Fact]
    public void A_menu_switch_to_an_aircraft_without_reads_only_drops_the_subscriptions()
    {
        Assert.Equal(InputEventRefreshAction.DropSubscriptions,
            DecideInputEventRefresh(false, false, wantsReads: false, reenumerate: false));
        Assert.Equal(InputEventRefreshAction.DropSubscriptions,
            DecideInputEventRefresh(false, true, wantsReads: false, reenumerate: false));
    }

    [Theory]
    [InlineData(false, true, InputEventRefreshAction.Enumerate)]
    [InlineData(false, false, InputEventRefreshAction.Nothing)]
    [InlineData(true, true, InputEventRefreshAction.Enumerate)]
    [InlineData(true, false, InputEventRefreshAction.Enumerate)]
    public void A_load_enumerates_for_reads_or_an_empty_catalogue_only(bool empty, bool wants, InputEventRefreshAction expected)
        => Assert.Equal(expected, DecideInputEventRefresh(empty, catalogueStale: false, wants, reenumerate: true));

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void An_empty_catalogue_always_enumerates(bool stale, bool wants)
        => Assert.Equal(InputEventRefreshAction.Enumerate, DecideInputEventRefresh(true, stale, wants, reenumerate: false));
}
