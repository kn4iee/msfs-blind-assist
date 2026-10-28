using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

// The keypad entry the RMP window types (live 2026-10-28, each line's entry read from the A350's memory). The RMP has a
// selected line: digits and CLR edit that line's entry (six characters at most; one CLR deletes one), its line select
// key loads it as standby, and another line's key only selects that line, emptying its entry, and loads nothing (the
// manual: select the radio, type, press to load). Its screen shows neither the selection nor the entry. So a new entry
// first empties the selected line's entry (CLR on an empty one does nothing) and presses its own line's key: on the
// selected line that loads the now empty entry, which does nothing; on another it selects it.
public class A350RmpEntryTests
{
    private static string[] Start(int line) =>
        Enumerable.Repeat("CLR", A350RmpEntry.ClearPresses).Append(A350Rmp.LineSelectKey(line)).ToArray();

    [Fact]
    public void A_new_entry_selects_its_line_before_its_first_character()
    {
        var entry = new A350RmpEntry();
        Assert.Equal(Start(2).Append("2"), entry.Type('2', 2));
        Assert.Equal(new[] { "7" }, entry.Type('7', 2));
        Assert.Equal(new[] { "PERIOD" }, entry.Type('.', 2));
        Assert.Equal("27.", entry.Typed);
        Assert.Equal(2, entry.Line);
    }

    [Fact]
    public void The_clears_empty_the_longest_entry_the_rmp_keeps()
    {
        Assert.True(A350RmpEntry.ClearPresses >= 6);   // 12 digits typed kept 123456
    }

    [Fact]
    public void A_character_with_no_key_types_nothing()
    {
        var entry = new A350RmpEntry();
        entry.Type('1', 1);
        Assert.Empty(entry.Type('A', 1));
        Assert.Equal("1", entry.Typed);
    }

    [Fact]
    public void Moving_an_entry_deletes_it_and_types_it_again_on_the_new_line()
    {
        var entry = new A350RmpEntry();
        entry.Type('2', 1);
        entry.Type('7', 1);
        Assert.Equal(new[] { "CLR", "CLR", "LSK3", "2", "7" }, entry.MoveTo(3));
        Assert.Equal(3, entry.Line);
        Assert.Equal("27", entry.Typed);
    }

    [Fact]
    public void Moving_to_its_own_line_or_with_nothing_typed_presses_nothing()
    {
        var entry = new A350RmpEntry();
        Assert.Empty(entry.MoveTo(2));
        entry.Type('2', 2);
        Assert.Empty(entry.MoveTo(2));
    }

    [Fact]
    public void Loading_presses_the_line_select_key_and_ends_the_entry()
    {
        var entry = new A350RmpEntry();
        entry.Type('2', 2);
        Assert.Equal(new[] { "LSK2" }, entry.Load(2));
        Assert.Equal("", entry.Typed);
        Assert.Equal(Start(2).Append("3"), entry.Type('3', 2));   // the next entry selects again: a refusal may have kept it
    }

    [Fact]
    public void Deleting_presses_clr_and_forgets_the_last_character()
    {
        var entry = new A350RmpEntry();
        entry.Type('2', 1);
        entry.Type('7', 1);
        Assert.Equal("CLR", entry.DeleteLast());
        Assert.Equal("2", entry.Typed);
        Assert.Equal("Entry 2", entry.Spoken);
        entry.DeleteLast();
        Assert.Equal("", entry.Typed);
        Assert.Equal("Entry empty", entry.Spoken);
    }

    [Fact]
    public void Deleting_with_nothing_typed_presses_nothing()
    {
        Assert.Null(new A350RmpEntry().DeleteLast());
    }

    [Fact]
    public void Abandoning_deletes_every_character_typed()
    {
        var entry = new A350RmpEntry();
        entry.Type('2', 1);
        entry.Type('7', 1);
        entry.Type('2', 1);
        Assert.Equal(new[] { "CLR", "CLR", "CLR" }, entry.Abandon());
        Assert.Equal("", entry.Typed);
        Assert.Empty(entry.Abandon());
    }

    // After the line select key the line is read back. A frequency the RMP refused leaves the line as it was, as does one
    // already in standby: "unchanged" is true of both, "not taken" of only the first.
    [Fact]
    public void A_line_the_load_left_as_it_was_says_the_standby_is_unchanged()
    {
        Assert.Equal("272: standby unchanged. VHF2: active 124.850, white; standby 122.000, white",
            A350RmpEntry.LoadReadBack("272", "VHF2: active 124.850, white; standby 122.000, white",
                "VHF2: active 124.850, white; standby 122.000, white"));
    }

    [Fact]
    public void A_line_the_load_changed_is_read_as_it_is()
    {
        Assert.Equal("VHF2: active 124.850, white; standby 127.200, white",
            A350RmpEntry.LoadReadBack("272", "VHF2: active 124.850, white; standby 122.000, white",
                "VHF2: active 124.850, white; standby 127.200, white"));
    }

    [Fact]
    public void A_load_with_nothing_typed_reads_the_line_as_it_is()
    {
        const string line = "VHF2: active 124.850, white; standby 122.000, white";
        Assert.Equal(line, A350RmpEntry.LoadReadBack("", line, line));
        Assert.Equal(line, A350RmpEntry.LoadReadBack("272", null, line));
    }
}
