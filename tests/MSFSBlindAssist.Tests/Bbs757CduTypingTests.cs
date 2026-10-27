using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 08 Task 2: typed entries go out faster and a missed key is said.</summary>
public class Bbs757CduTypingTests
{
    private static Bbs757CduScreen WithScratchpad(string text)
    {
        var lines = Bbs757CduFrame.Blank.Lines.ToArray();
        lines[Bbs757CduFrame.ScratchpadRow] = text.PadRight(Bbs757CduFrame.Columns)[..Bbs757CduFrame.Columns];
        return new Bbs757CduScreen(lines);
    }

    [Fact]
    public void Typing_keys_are_the_cdu_keys()
    {
        Assert.Equal(new[] { "KEYK", "KEYA", "KEYT", "KEYL", "NUM1", "NUM1", "NUM0", "NUM0", Bbs757CduKeys.Period, "NUM5" },
            Bbs757CduTyping.Keys("katl1100.5"));
        // As the window always typed: Bbs757CduKeys.ForChar of each character, one with no key skipped.
        Assert.Equal("katl/1".SelectMany(Bbs757CduKeys.ForChar).ToList(), Bbs757CduTyping.Keys("katl/1"));
        Assert.Equal(new[] { "KEYA" }, Bbs757CduTyping.Keys("a#"));
        Assert.Empty(Bbs757CduTyping.Keys(""));
    }

    [Fact]
    public void The_typing_timing_is_the_measured_one()
    {
        // flight-08.md: powered, 100/100 lost repeated digits (6/10); 100/200 15/15; 100/300 keeps a step of margin.
        Assert.Equal(100, Bbs757CduTyping.HoldMs);
        Assert.Equal(300, Bbs757CduTyping.GapMs);
        Assert.True(CoherentBbs757TabletClient.CycleMs(Bbs757CduTyping.HoldMs, Bbs757CduTyping.GapMs) < CoherentBbs757TabletClient.KeyCycleMs);
    }

    [Fact]
    public void Mismatch_cases()
    {
        // A match is silent, whatever the case typed.
        Assert.Null(Bbs757CduTyping.Mismatch("katl", WithScratchpad("KATL")));
        Assert.Null(Bbs757CduTyping.Mismatch("1100.5", WithScratchpad("1100.5")));
        // A missed key says what the scratchpad shows.
        Assert.Equal("Scratchpad shows KAT", Bbs757CduTyping.Mismatch("KATL", WithScratchpad("KAT")));
        Assert.Equal("Scratchpad shows 10.5", Bbs757CduTyping.Mismatch("100.5", WithScratchpad("10.5")));
        // An empty scratchpad.
        Assert.Equal("Scratchpad is empty", Bbs757CduTyping.Mismatch("KATL", WithScratchpad("")));
        // A message over the entry is said as the scratchpad shows it.
        Assert.Equal("Scratchpad shows INVALID ENTRY", Bbs757CduTyping.Mismatch("KATL", WithScratchpad("INVALID ENTRY")));
        // Typed onto what the scratchpad already held: the CDU adds to an entry.
        Assert.Null(Bbs757CduTyping.Mismatch("TL", WithScratchpad("KATL"), scratchpadBefore: "KA"));
        Assert.Equal("Scratchpad shows KAT", Bbs757CduTyping.Mismatch("TL", WithScratchpad("KAT"), scratchpadBefore: "KA"));
        // A character with no key is not expected in the scratchpad.
        Assert.Null(Bbs757CduTyping.Mismatch("KA#TL", WithScratchpad("KATL")));
        // Nothing typable typed: nothing to check.
        Assert.Null(Bbs757CduTyping.Mismatch("#", WithScratchpad("")));
    }

    [Fact]
    public void A_landed_entry_is_read_back_as_the_scratchpad_shows_it()
    {
        // The guide's promise, as every other CDU window does: after typing, the pilot hears what the CDU took.
        Assert.Equal("KATL", Bbs757CduTyping.ReadBack("katl", WithScratchpad("KATL")));
        Assert.Equal("1100.5", Bbs757CduTyping.ReadBack("1100.5", WithScratchpad("1100.5")));
        // Typed onto an entry already there: the whole scratchpad, as the CDU shows it.
        Assert.Equal("KATL", Bbs757CduTyping.ReadBack("TL", WithScratchpad("KATL"), scratchpadBefore: "KA"));
    }

    [Fact]
    public void A_missed_key_is_read_back_as_the_mismatch()
    {
        Assert.Equal("Scratchpad shows KAT", Bbs757CduTyping.ReadBack("KATL", WithScratchpad("KAT")));
        Assert.Equal("Scratchpad is empty", Bbs757CduTyping.ReadBack("KATL", WithScratchpad("")));
        Assert.Equal("Scratchpad shows INVALID ENTRY", Bbs757CduTyping.ReadBack("KATL", WithScratchpad("INVALID ENTRY")));
    }

    [Fact]
    public void Nothing_to_read_back_when_nothing_typable_landed_on_an_empty_scratchpad()
    {
        Assert.Null(Bbs757CduTyping.ReadBack("#", WithScratchpad("")));
    }
}
