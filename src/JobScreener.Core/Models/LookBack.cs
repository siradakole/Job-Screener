namespace JobScreener.Core.Models;

public enum LookBackUnit { Hours, Days, Weeks }

public sealed record LookBack
{
    public LookBack(int amount, LookBackUnit unit)
    {
        if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount), "Must be at least 1.");
        Amount = amount;
        Unit = unit;
    }

    public int Amount { get; }
    public LookBackUnit Unit { get; }

    public TimeSpan ToTimeSpan() => Unit switch
    {
        LookBackUnit.Hours => TimeSpan.FromHours(Amount),
        LookBackUnit.Days => TimeSpan.FromDays(Amount),
        LookBackUnit.Weeks => TimeSpan.FromDays(7 * Amount),
        _ => throw new ArgumentOutOfRangeException(nameof(Unit))
    };

    public DateTimeOffset CutoffFrom(DateTimeOffset now) => now - ToTimeSpan();
}
