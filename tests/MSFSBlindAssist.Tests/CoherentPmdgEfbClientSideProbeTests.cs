using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Review 2026-10-29 (A350 OIS I5): both A350 OIS views (and both PMDG tablets) share one title, so a client finds its own by
/// opening a one-shot socket on each candidate and asking its side. With both sides open, the second client probed the page
/// the first one held, and Coherent allows one inspector socket per page ([DBG-9]): the holder lost its agent or an answer.
/// A client now claims a page before probing it and keeps the claim while it is connected; a claimed page is never probed.
/// The page ids here are far from any other test's, as the claims are process-wide.
/// </summary>
public class CoherentPmdgEfbClientSideProbeTests
{
    private static Func<int, Task<string>> Sides(Dictionary<int, string> sides, List<int> probed) => id =>
    {
        probed.Add(id);
        return Task.FromResult(sides.TryGetValue(id, out var s) ? s : "");
    };

    [Fact]
    public async Task A_page_another_client_holds_is_never_probed()
    {
        using var other = CoherentPmdgEfbClient.PageClaims.TryClaim(910_001);
        Assert.NotNull(other);
        var probed = new List<int>();
        var picked = await CoherentPmdgEfbClient.ClaimSidePageAsync(new[] { 910_001, 910_002 }, "FO",
            Sides(new() { [910_001] = "CPT", [910_002] = "FO" }, probed));
        using var claim = picked?.Claim;
        Assert.Equal(910_002, picked?.Page);
        Assert.Equal(new[] { 910_002 }, probed);
    }

    [Fact]
    public async Task The_page_of_the_side_stays_claimed_and_the_others_are_released()
    {
        var probed = new List<int>();
        var picked = await CoherentPmdgEfbClient.ClaimSidePageAsync(new[] { 910_011, 910_012 }, "FO",
            Sides(new() { [910_011] = "CPT", [910_012] = "FO" }, probed));
        Assert.Equal(910_012, picked?.Page);
        Assert.Null(CoherentPmdgEfbClient.PageClaims.TryClaim(910_012));
        using (var cpt = CoherentPmdgEfbClient.PageClaims.TryClaim(910_011)) Assert.NotNull(cpt);
        picked!.Value.Claim.Dispose();
        using var free = CoherentPmdgEfbClient.PageClaims.TryClaim(910_012);
        Assert.NotNull(free);
    }

    [Fact]
    public async Task With_no_page_of_its_side_nothing_stays_claimed()
    {
        var picked = await CoherentPmdgEfbClient.ClaimSidePageAsync(new[] { 910_021, 910_022 }, "FO",
            Sides(new() { [910_021] = "CPT" }, new List<int>()));
        Assert.Null(picked);
        foreach (int id in new[] { 910_021, 910_022 })
            using (var c = CoherentPmdgEfbClient.PageClaims.TryClaim(id)) Assert.NotNull(c);
    }

    // The client releases its claim wherever its socket goes (a reconnect, a loop error, Stop): a second release must never
    // free a claim another client has taken since.
    [Fact]
    public void A_claim_is_released_once_however_often_it_is_disposed()
    {
        var mine = CoherentPmdgEfbClient.PageClaims.TryClaim(910_031)!;
        mine.Dispose();
        using var theirs = CoherentPmdgEfbClient.PageClaims.TryClaim(910_031);
        Assert.NotNull(theirs);
        mine.Dispose();
        Assert.Null(CoherentPmdgEfbClient.PageClaims.TryClaim(910_031));
    }
}
