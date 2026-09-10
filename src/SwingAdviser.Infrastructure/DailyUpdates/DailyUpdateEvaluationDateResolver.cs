namespace SwingAdviser.Infrastructure.DailyUpdates;

/// <summary>Chooses the intended daily bar date without treating an unavailable bar as final.</summary>
internal static class DailyUpdateEvaluationDateResolver
{
    private static readonly TimeOnly SameDayCutoffJst = new(15, 30);

    internal static DateOnly Resolve(DateTime requestedAtUtc)
    {
        if (requestedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The requested time must be UTC.", nameof(requestedAtUtc));

        var jst = TimeZoneInfo.ConvertTimeFromUtc(requestedAtUtc, JapanTimeZone());
        var date = DateOnly.FromDateTime(jst.Date);
        if (IsWeekday(date) && TimeOnly.FromDateTime(jst) >= SameDayCutoffJst)
            return date;

        return PreviousWeekday(date);
    }

    private static DateOnly PreviousWeekday(DateOnly date)
    {
        var candidate = date.AddDays(-1);
        while (!IsWeekday(candidate)) candidate = candidate.AddDays(-1);
        return candidate;
    }

    private static bool IsWeekday(DateOnly date) => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static TimeZoneInfo JapanTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"); }
    }
}
