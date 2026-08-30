namespace SwingAdviser.Domain.MarketData;

/// <summary>Append-only evidence that a provider returned a complete, validated history range.</summary>
public sealed class DailyBarHistoryCoverage
{
    public int DailyBarHistoryCoverageId { get; set; }
    public int InstrumentId { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateOnly EarliestReturnedDate { get; set; }
    public DateOnly LatestReturnedDate { get; set; }
    public bool FullHistoryConfirmed { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Instrument Instrument { get; set; } = null!;
    public DailyBarHistoryCoverage? Supersedes { get; set; }
}
