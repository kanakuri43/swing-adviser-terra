namespace SwingAdviser.Domain.MarketData;

/// <summary>Append-only revision of margin-trading eligibility and regulation data.</summary>
public sealed class MarginRegulationRevision
{
    public int MarginRegulationRevisionId { get; set; }
    public int InstrumentId { get; set; }
    public string SystemMarginEligible { get; set; } = string.Empty;
    public string GeneralMarginEligible { get; set; } = string.Empty;
    public string ShortSellEligible { get; set; } = string.Empty;
    public string? RegulationFlagsJson { get; set; }
    public DateOnly EffectiveAtDate { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesRevisionId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Instrument Instrument { get; set; } = null!;
    public MarginRegulationRevision? SupersedesRevision { get; set; }
}
