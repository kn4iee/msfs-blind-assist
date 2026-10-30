using MSFSBlindAssist.Aircraft.A350.Display;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

public class A350MemoryWarningTests
{
    private const long GB = 1L << 30;

    [Fact]
    public void Plenty_says_nothing()
    {
        Assert.Null(new A350MemoryWarning().Observe(50 * GB));
    }

    [Fact]
    public void Below_ten_gigabytes_it_is_said_once_with_what_is_left()
    {
        var warning = new A350MemoryWarning();
        Assert.Equal(A350MemoryWarning.Sentence(9), warning.Observe(9 * GB + GB / 2));
        Assert.Null(warning.Observe(9 * GB));
        Assert.Null(warning.Observe(6 * GB));
    }

    [Fact]
    public void Below_five_gigabytes_it_is_said_again()
    {
        var warning = new A350MemoryWarning();
        warning.Observe(9 * GB);
        Assert.Equal(A350MemoryWarning.Sentence(4), warning.Observe(4 * GB));
        Assert.Null(warning.Observe(1 * GB));
    }

    [Fact]
    public void Straight_to_critical_says_one_sentence()
    {
        var warning = new A350MemoryWarning();
        Assert.Equal(A350MemoryWarning.Sentence(3), warning.Observe(3 * GB));
        Assert.Null(warning.Observe(9 * GB));
    }

    [Fact]
    public void Plenty_again_arms_it_again_but_a_small_recovery_does_not()
    {
        var warning = new A350MemoryWarning();
        warning.Observe(9 * GB);
        Assert.Null(warning.Observe(11 * GB));
        Assert.Null(warning.Observe(9 * GB));
        Assert.Null(warning.Observe(13 * GB));
        Assert.Equal(A350MemoryWarning.Sentence(9), warning.Observe(9 * GB));
    }

    [Fact]
    public void No_reading_says_nothing()
    {
        Assert.Null(new A350MemoryWarning().Observe(0));
    }

    [Fact]
    public void The_sentence_says_what_to_do_about_it()
    {
        Assert.Contains("9 gigabytes", A350MemoryWarning.Sentence(9));
        Assert.Contains("save", A350MemoryWarning.Sentence(9), StringComparison.OrdinalIgnoreCase);
    }
}
