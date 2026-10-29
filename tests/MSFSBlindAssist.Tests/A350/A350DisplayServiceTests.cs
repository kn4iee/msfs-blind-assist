using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// A copy can span two A350 frames and draw a half-updated screen [A350-2]: a value read from a draw is
// taken only once two reads in a row agree, so a torn frame is never announced.
public class A350DisplayServiceTests
{
    [Fact]
    public void A_value_is_taken_after_two_agreeing_reads()
    {
        var s = new A350StableValue<string>();
        Assert.False(s.Offer("PARK BRK", out _));          // first sight
        Assert.True(s.Offer("PARK BRK", out var v));       // confirmed
        Assert.Equal("PARK BRK", v);
        Assert.False(s.Offer("PARK BRK", out _));          // unchanged: nothing new
        Assert.Equal("PARK BRK", s.Current);
    }

    [Fact]
    public void A_one_read_flicker_is_never_taken()
    {
        var s = new A350StableValue<string>();
        s.Offer("A", out _); s.Offer("A", out _);
        Assert.False(s.Offer("garbage", out _));           // a torn frame
        Assert.False(s.Offer("A", out _));                 // back to what was taken: nothing new
        Assert.Equal("A", s.Current);
        Assert.False(s.Offer("B", out _));
        Assert.True(s.Offer("B", out var v));
        Assert.Equal("B", v);
    }

    [Fact]
    public void Reset_forgets_what_was_taken()
    {
        var s = new A350StableValue<int>();
        s.Offer(1, out _); s.Offer(1, out _);
        s.Reset();
        Assert.False(s.HasValue);
        Assert.False(s.Offer(1, out _));
        Assert.True(s.Offer(1, out _));
    }

    [Fact]
    public void The_status_is_spoken_in_plain_words()
    {
        Assert.Equal("A350 displays getting ready", A350DisplayStatus.GettingReady);
        Assert.Equal("A350 displays ready", A350DisplayStatus.Ready);
        Assert.Equal("A350 displays not available: the A350 module has no function CTR::Draw()",
            A350DisplayStatus.NotAvailable("the A350 module has no function CTR::Draw()"));
    }
}

/// <summary>The display service's preparation, with the installed module replaced (review 2026-10-29, engine M1 and M2).</summary>
public class A350DisplayServicePrepareTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a350service-" + Guid.NewGuid().ToString("N"));

    public A350DisplayServicePrepareTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Module(byte[] bytes)
    {
        var path = Path.Combine(_root, "inibuilds-A350.wasm");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void WaitPrepared(A350DisplayService service)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (service.State == A350DisplayState.Preparing && DateTime.UtcNow < until)
            Thread.Sleep(20);
    }

    // M1: Prepare caught four exception types; a module that could not be parsed (a truncated file mid-update) threw another,
    // and the service stayed "Preparing" for good, saying nothing, while the monitors polled null. Any failure leaves the
    // displays unavailable and says so.
    [Fact]
    public void A_module_that_cannot_be_read_leaves_the_displays_unavailable_and_says_so()
    {
        var truncated = new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF, 0x0F, 0x60 };
        var spoken = new List<string>();
        using var service = new A350DisplayService(_root) { FindModule = () => Module(truncated) };
        service.Spoken += line => { lock (spoken) spoken.Add(line); };
        service.Start();
        WaitPrepared(service);
        Assert.Equal(A350DisplayState.Unavailable, service.State);
        // The state is set before the sentence is said (on the worker): wait for the sentence itself.
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            lock (spoken)
                if (spoken.Count > 0) break;
            Thread.Sleep(20);
        }
        lock (spoken)
            Assert.Contains(spoken, l => l.StartsWith("A350 displays not available: ", StringComparison.Ordinal));
    }

    // M2: a first compile that finished after the pilot switched aircraft said "A350 displays ready" over the next one.
    // Nothing is said once the service is disposed.
    [Fact]
    public void Nothing_is_said_after_the_service_is_disposed()
    {
        var spokenAfter = new List<string>();
        bool disposed = false;
        var service = new A350DisplayService(_root) { FindModule = () => Module(A350ModulePatcherTests.Sample()) };
        service.Spoken += line => { if (Volatile.Read(ref disposed)) lock (spokenAfter) spokenAfter.Add(line); };
        service.Start();
        service.Dispose();
        Volatile.Write(ref disposed, true);
        WaitPrepared(service);
        Thread.Sleep(200);
        lock (spokenAfter)
            Assert.Empty(spokenAfter);
    }
}
