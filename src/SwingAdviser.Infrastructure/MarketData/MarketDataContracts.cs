namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>Normalized record supplied by the JPX listed-issues source.</summary>
public sealed record ListedInstrumentSourceRecord(
    string Code,
    string Name,
    string MarketSegment,
    string InstrumentType,
    string ListedStatus,
    string ScanEligibility);

public sealed record ListedInstrumentSourceSnapshot(
    IReadOnlyList<ListedInstrumentSourceRecord> Records,
    string SourceFileHash);

/// <summary>Eligibility information explicitly published by JPX. Missing values stay Unknown.</summary>
public sealed record MarginEligibilitySourceRecord(
    string Code,
    string SystemMarginEligible,
    string GeneralMarginEligible,
    string ShortSellEligible,
    string? RegulationFlagsJson);

/// <summary>One unadjusted OHLCV value returned by the Yahoo chart endpoint.</summary>
public sealed record YahooDailyBarSourceRecord(
    DateOnly TradingDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? AdjClose);

/// <summary>Corporate action observed in a Yahoo chart response.</summary>
public sealed record CorporateActionSourceRecord(
    string ActionType,
    DateOnly EffectiveDate,
    DateTime EventAtUtc,
    int? SplitRatioNumerator,
    int? SplitRatioDenominator,
    decimal? DividendAmountPerShare,
    string? Currency,
    string SourceEventId);

public sealed record YahooChartSourceSnapshot(
    IReadOnlyList<YahooDailyBarSourceRecord> DailyBars,
    IReadOnlyList<CorporateActionSourceRecord> CorporateActions,
    bool FullHistoryConfirmed = false);

public sealed record FundamentalDataSourceRecord(
    decimal? Per,
    decimal? Pbr,
    decimal? MarketCap,
    decimal? DividendYield,
    string? AdditionalMetricsJson);

public interface IJpxListedIssuesSource
{
    Task<ListedInstrumentSourceSnapshot> FetchAsync(CancellationToken cancellationToken);
}

public interface IJpxMarginIssuesSource
{
    Task<IReadOnlyList<MarginEligibilitySourceRecord>> FetchAsync(CancellationToken cancellationToken);
}

public interface IYahooFinanceSource
{
    Task<YahooChartSourceSnapshot> FetchChartAsync(string code, CancellationToken cancellationToken);

    Task<FundamentalDataSourceRecord> FetchFundamentalsAsync(string code, CancellationToken cancellationToken);
}

/// <summary>Typed failure that can be persisted without exposing transport implementation details.</summary>
public sealed class ExternalDataFetchException : Exception
{
    public ExternalDataFetchException(string errorKind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = errorKind;
    }

    public string ErrorKind { get; }
}
