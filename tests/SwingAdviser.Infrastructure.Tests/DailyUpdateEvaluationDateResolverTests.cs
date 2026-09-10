using SwingAdviser.Infrastructure.DailyUpdates;

namespace SwingAdviser.Infrastructure.Tests;

public class DailyUpdateEvaluationDateResolverTests
{
    [Theory]
    [InlineData(2026, 9, 10, 6, 29, 2026, 9, 9)] // Thu 15:29 JST: previous weekday
    [InlineData(2026, 9, 10, 6, 30, 2026, 9, 10)] // Thu 15:30 JST: same-day bar is eligible
    [InlineData(2026, 9, 13, 6, 30, 2026, 9, 11)] // Sun: previous Friday
    [InlineData(2026, 9, 14, 6, 29, 2026, 9, 11)] // Mon 15:29 JST: previous Friday
    public void Resolve_UsesSameWeekdayAfterJstCutoffOtherwisePreviousWeekday(int year, int month, int day, int hourUtc, int minuteUtc, int expectedYear, int expectedMonth, int expectedDay)
    {
        var result = DailyUpdateEvaluationDateResolver.Resolve(new DateTime(year, month, day, hourUtc, minuteUtc, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }

    [Fact]
    public void Resolve_RejectsNonUtcRequestedTime()
    {
        var requestedAt = new DateTime(2026, 9, 10, 6, 30, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => DailyUpdateEvaluationDateResolver.Resolve(requestedAt));
    }
}
