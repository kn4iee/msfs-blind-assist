using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// A trap costs the pilot nothing: the draw is tried once more on a new copy ([A350-2]), the retry's capture is what a
// monitor gets, and what a monitor has already said is not said again. The flight of 2026-10-31 had two traps in 3.6 hours.
public class A350TrapRetryTests
{
    /// <summary>A memory source handing out the copies a test names, in turn (the last one stays).</summary>
    private sealed class Copies : IA350MemorySource
    {
        private readonly Queue<A350MemoryCopy> _copies;
        private A350MemoryCopy _last;
        public int Taken { get; private set; }

        public Copies(params A350MemoryCopy[] copies)
        {
            _copies = new Queue<A350MemoryCopy>(copies);
            _last = copies[0];
        }

        public ulong Base => 1;
        public int LastReread => 0;
        public long LastBytes => 65536;
        public int LastLeftOut => 0;
        public A350ReaderFailure? LastFailure => null;
        public bool TryLocate() => true;
        public bool IsStillValid() => true;

        public A350MemoryCopy Snapshot()
        {
            Taken++;
            if (_copies.Count > 0) _last = _copies.Dequeue();
            return _last;
        }

        public void Dispose() { }
    }

    private static A350DisplayService Service(Copies copies, A350DrawEngine engine) => new(copies, engine, engine.Facts);

    [Fact]
    public async Task A_trapped_draw_is_retried_once_on_a_new_copy()
    {
        var copies = new Copies(A350DrawEngineTests.Copy(trap: true), A350DrawEngineTests.Copy(trap: false));
        using var service = Service(copies, A350DrawEngineTests.Engine());
        var caps = await service.DrawAsync(("CTR", 0));
        Assert.Equal(2, copies.Taken);
        Assert.True(Assert.Single(caps!).Ok);
    }

    [Fact]
    public async Task Only_the_retry_reaches_the_monitor()
    {
        var copies = new Copies(A350DrawEngineTests.Copy(trap: true), A350DrawEngineTests.Copy(trap: false));
        using var service = Service(copies, A350DrawEngineTests.Engine());
        var caps = await service.DrawAsync(("CTR", 0));
        var cap = Assert.Single(caps!);
        Assert.Null(cap.Error);
        Assert.Equal("HELLO", Assert.Single(cap.Texts).Text);
    }

    [Fact]
    public async Task A_second_trap_in_one_request_is_not_retried()
    {
        var copies = new Copies(A350DrawEngineTests.Copy(trap: true), A350DrawEngineTests.Copy(trap: true));
        using var service = Service(copies, A350DrawEngineTests.Engine());
        var caps = await service.DrawAsync(("CTR", 0), ("CTR", 0));
        Assert.Equal(2, copies.Taken);
        Assert.All(caps!, c => Assert.StartsWith("trap", c.Error));
    }

    [Fact]
    public async Task A_copy_that_draws_takes_no_second_copy()
    {
        var copies = new Copies(A350DrawEngineTests.Copy(trap: false));
        using var service = Service(copies, A350DrawEngineTests.Engine());
        Assert.True(Assert.Single((await service.DrawAsync(("CTR", 0)))!).Ok);
        Assert.Equal(1, copies.Taken);
    }

    [Fact]
    public void A_trap_between_two_agreeing_ecam_reads_says_nothing_new()
    {
        var said = new List<string>();
        var monitor = new A350EcamMonitor(_ => false, said.Add);
        var dark = new A350DrawCapture("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);
        var fault = A350Fixtures.Capture("ctr_powerup_fws_fault.json");
        var trapped = new A350DrawCapture("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(),
            "trap: error while executing at wasm backtrace:", 1);
        foreach (var c in new[] { dark, dark, fault, fault }) monitor.Observe(c);
        var before = said.ToList();
        Assert.NotEmpty(before);
        foreach (var c in new[] { trapped, fault }) monitor.Observe(c);
        Assert.Equal(before, said);
    }
}
