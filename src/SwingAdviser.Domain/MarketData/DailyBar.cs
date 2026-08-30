namespace SwingAdviser.Domain.MarketData;

/// <summary>One provider-supplied, unadjusted daily OHLCV revision.</summary>
public sealed class DailyBar
{
    public int DailyBarId { get; set; }
    public int InstrumentId { get; set; }
    public DateOnly TradingDate { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public decimal? AdjClose { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime FetchedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Instrument Instrument { get; set; } = null!;
    public DailyBar? Supersedes { get; set; }
}
