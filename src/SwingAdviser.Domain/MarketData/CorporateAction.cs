namespace SwingAdviser.Domain.MarketData;

/// <summary>Append-only corporate-action revision. Unsupported actions are retained for reconciliation.</summary>
public sealed class CorporateAction
{
    public int CorporateActionId { get; set; }
    public int InstrumentId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    /// <summary>
    /// Provider-announced time when known. Yahoo chart events do not provide it, so unknown must remain null.
    /// </summary>
    public DateTime? AnnouncedAtUtc { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public DateTime FirstObservedAtUtc { get; set; }
    public int? SplitRatioNumerator { get; set; }
    public int? SplitRatioDenominator { get; set; }
    public decimal? DividendAmountPerShare { get; set; }
    public string? Currency { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Instrument Instrument { get; set; } = null!;
    public CorporateAction? Supersedes { get; set; }
}
