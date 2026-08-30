namespace SwingAdviser.Domain.MarketData;

/// <summary>Point-in-time cache of structured fundamental metrics.</summary>
public sealed class FundamentalDataSnapshot
{
    public int FundamentalSnapshotId { get; set; }
    public int InstrumentId { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public decimal? Per { get; set; }
    public decimal? Pbr { get; set; }
    public decimal? MarketCap { get; set; }
    public decimal? DividendYield { get; set; }
    public string? AdditionalMetricsJson { get; set; }

    public Instrument Instrument { get; set; } = null!;
}
