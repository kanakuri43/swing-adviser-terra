namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>Reference-project-compatible finite history window for daily technical analysis.</summary>
public static class TechnicalHistoryWindow
{
    public static DateOnly GetStart(DateOnly evaluationBarDate, int requiredHistoryCount, int historyLookbackYears)
    {
        var referenceStart = evaluationBarDate.AddYears(-Math.Clamp(historyLookbackYears, 1, 10));
        var indicatorStart = evaluationBarDate.AddDays(-(Math.Max(1, requiredHistoryCount) * 2 + 30));
        return indicatorStart < referenceStart ? indicatorStart : referenceStart;
    }
}
