using JobScreener.Core.Models;

namespace JobScreener.Tests;

public class LookBackTests
{
    [Theory]
    [InlineData(1, LookBackUnit.Hours, 1)]
    [InlineData(2, LookBackUnit.Days, 48)]
    [InlineData(1, LookBackUnit.Weeks, 168)]
    public void ToTimeSpan_converts_units_to_hours(int amount, LookBackUnit unit, double expectedHours)
    {
        var span = new LookBack(amount, unit).ToTimeSpan();
        Assert.Equal(expectedHours, span.TotalHours);
    }

    [Fact]
    public void CutoffFrom_subtracts_the_window()
    {
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var cutoff = new LookBack(3, LookBackUnit.Hours).CutoffFrom(now);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), cutoff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_rejects_amounts_below_one(int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LookBack(amount, LookBackUnit.Hours));
    }
}
