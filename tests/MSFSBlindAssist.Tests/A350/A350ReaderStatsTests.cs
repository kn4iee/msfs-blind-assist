using System.Globalization;
using MSFSBlindAssist.Aircraft.A350.Display;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

public class A350ReaderStatsTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    private static A350ReaderStats Fed()
    {
        var stats = new A350ReaderStats();
        stats.Copied(T0, 40, 512L << 20, 1 << 20, 0);
        stats.Copied(T0.AddSeconds(1), 60, 520L << 20, 3 << 20, 2);
        stats.Drew("EFIS", 0, 10, ok: true, tornTexts: 0, headRoomBytes: 4L << 20);
        stats.Drew("MFD", 0, 30, ok: true, tornTexts: 2, headRoomBytes: 12L << 20);
        stats.Drew("EFIS", 0, 20, ok: false, tornTexts: 0, headRoomBytes: 1L << 20);
        stats.Retried();
        return stats;
    }

    [Fact]
    public void Nothing_is_due_before_a_minute_has_passed()
    {
        var stats = Fed();
        Assert.Null(stats.Due(T0.AddSeconds(59)));
    }

    [Fact]
    public void It_tells_when_a_line_is_due_without_taking_it()
    {
        var stats = Fed();
        Assert.False(stats.IsDue(T0.AddSeconds(59)));
        Assert.True(stats.IsDue(T0.AddSeconds(60)));
        Assert.NotNull(stats.Due(T0.AddSeconds(60)));
        Assert.False(stats.IsDue(T0.AddSeconds(60)));
    }

    [Fact]
    public void Nothing_is_due_with_nothing_to_say()
    {
        Assert.Null(new A350ReaderStats().Due(T0.AddHours(1)));
    }

    [Fact]
    public void The_minute_says_what_the_reader_did()
    {
        var line = Fed().Due(T0.AddSeconds(60));
        Assert.Equal("60 s: 2 copies, avg 50 ms, max 60 ms, 520 MB, 3.0 MB read again, 2 regions left out; "
            + "3 draws, avg 20 ms, max 30 ms (MFD#0), head room max 12 MB; "
            + "1 failed (EFIS#0 1), 1 retried, 2 torn texts", line);
    }

    [Fact]
    public void The_minute_says_what_the_machine_had_left()
    {
        var memory = new A350MachineMemory(SimCommit: 33L << 30, FreeCommit: 49L << 30, FreePhysical: 8L << 30, OwnCommit: 1L << 30);
        var line = Fed().Due(T0.AddSeconds(60), memory);
        Assert.EndsWith("; sim commit 33.0 GB, free commit 49.0 GB, free physical 8.0 GB, MSFSBA 1.0 GB", line);
    }

    [Fact]
    public void A_quiet_minute_says_no_failures()
    {
        var stats = new A350ReaderStats();
        stats.Copied(T0, 40, 1L << 20, 0, 0);
        stats.Drew("CTR", 0, 5, ok: true, tornTexts: 0, headRoomBytes: 0);
        Assert.Contains("; no failures", stats.Due(T0.AddMinutes(1)));
    }

    [Fact]
    public void After_a_line_the_counts_start_again()
    {
        var stats = Fed();
        stats.Due(T0.AddSeconds(60));
        Assert.Null(stats.Due(T0.AddSeconds(200)));
        stats.Copied(T0.AddSeconds(201), 70, 1L << 20, 0, 0);
        Assert.Null(stats.Due(T0.AddSeconds(260)));
        Assert.StartsWith("60 s: 1 copies, avg 70 ms", stats.Due(T0.AddSeconds(261)));
    }

    [Fact]
    public void The_line_never_takes_the_pcs_decimal_comma()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Contains("3.0 MB read again", Fed().Due(T0.AddMinutes(1)));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_failure_is_logged_once_a_minute_per_display_and_error()
    {
        var stats = new A350ReaderStats();
        Assert.True(stats.LogFailure("EFIS", 0, "trap: out of bounds", T0));
        Assert.False(stats.LogFailure("EFIS", 0, "trap: out of bounds", T0.AddSeconds(59)));
        Assert.True(stats.LogFailure("EFIS", 0, "trap: another", T0.AddSeconds(1)));
        Assert.True(stats.LogFailure("MFD", 0, "trap: out of bounds", T0.AddSeconds(2)));
        Assert.True(stats.LogFailure("EFIS", 0, "trap: out of bounds", T0.AddSeconds(60)));
    }

    [Fact]
    public void Distinct_failures_never_grow_without_end()
    {
        var stats = new A350ReaderStats();
        for (int n = 0; n < 1000; n++) stats.LogFailure("EFIS", 0, "trap at " + n, T0);
        Assert.True(stats.FailureKeys <= A350ReaderStats.MaxFailureKeys);
    }
}

public class A350SpeechTraceTests
{
    [Fact]
    public void The_announcer_tells_its_trace_what_it_drops()
    {
        using var announcer = new MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer(IntPtr.Zero) { Suppressed = true };
        var seen = new List<(string How, string Line)>();
        announcer.Trace = (how, line) => seen.Add((how, line));
        announcer.Announce("Flaps 1");
        announcer.AnnounceWithQueue("Gear down");
        announcer.Announce("");
        Assert.Equal(new[] { ("dropped", "Flaps 1"), ("dropped", "Gear down") }, seen);
    }
}
