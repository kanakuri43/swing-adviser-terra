using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// Coordinates independent external fetches. A failure for one instrument is audited and does not
/// prevent later instruments from being refreshed; user-requested cancellation is propagated.
/// </summary>
public sealed class MarketDataIngestionService
{
    private readonly MarketDataRepository _repository;
    private readonly IJpxListedIssuesSource _listedIssuesSource;
    private readonly IJpxMarginIssuesSource _marginIssuesSource;
    private readonly IYahooFinanceSource _yahooFinanceSource;
    private readonly TimeProvider _clock;

    public MarketDataIngestionService(
        MarketDataRepository repository,
        IJpxListedIssuesSource listedIssuesSource,
        IJpxMarginIssuesSource marginIssuesSource,
        IYahooFinanceSource yahooFinanceSource,
        TimeProvider? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _listedIssuesSource = listedIssuesSource ?? throw new ArgumentNullException(nameof(listedIssuesSource));
        _marginIssuesSource = marginIssuesSource ?? throw new ArgumentNullException(nameof(marginIssuesSource));
        _yahooFinanceSource = yahooFinanceSource ?? throw new ArgumentNullException(nameof(yahooFinanceSource));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<int> RefreshInstrumentMasterAsync(int? dailyUpdateRunId, CancellationToken cancellationToken)
    {
        var attemptedAtUtc = UtcNow;
        try
        {
            var snapshot = await _listedIssuesSource.FetchAsync(cancellationToken);
            var imported = await _repository.ImportInstrumentMasterAsync(snapshot.Records, JstDate(attemptedAtUtc), attemptedAtUtc, snapshot.SourceFileHash, cancellationToken);
            await SucceedAsync(dailyUpdateRunId, "JPX-ListedIssues", null, snapshot.Records.Count, attemptedAtUtc, cancellationToken);
            return imported;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(dailyUpdateRunId, "JPX-ListedIssues", null, "Cancelled", "The listed-issues refresh was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(dailyUpdateRunId, "JPX-ListedIssues", null, Classify(exception), SafeMessage(exception));
            return 0;
        }
    }

    public async Task<int> RefreshMarginEligibilityAsync(int? dailyUpdateRunId, CancellationToken cancellationToken)
    {
        var attemptedAtUtc = UtcNow;
        try
        {
            var records = await _marginIssuesSource.FetchAsync(cancellationToken);
            var imported = await _repository.ImportMarginEligibilityAsync(records, JstDate(attemptedAtUtc), attemptedAtUtc, cancellationToken);
            await SucceedAsync(dailyUpdateRunId, "JPX-MarginIssues", null, records.Count, attemptedAtUtc, cancellationToken);
            return imported;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(dailyUpdateRunId, "JPX-MarginIssues", null, "Cancelled", "The margin-eligibility refresh was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(dailyUpdateRunId, "JPX-MarginIssues", null, Classify(exception), SafeMessage(exception));
            return 0;
        }
    }

    public async Task<InstrumentRefreshResult> RefreshInstrumentAsync(int? dailyUpdateRunId, int instrumentId, string code, CancellationToken cancellationToken)
    {
        var chartRecords = 0;
        var fundamentalRecords = 0;
        try
        {
            var attemptedAtUtc = UtcNow;
            var chart = await _yahooFinanceSource.FetchChartAsync(code, cancellationToken);
            chartRecords = await _repository.ImportYahooChartAsync(instrumentId, chart, attemptedAtUtc, cancellationToken);
            await SucceedAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", instrumentId, chart.DailyBars.Count + chart.CorporateActions.Count, attemptedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", instrumentId, "Cancelled", "The Yahoo chart refresh was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", instrumentId, Classify(exception), SafeMessage(exception));
        }

        try
        {
            var attemptedAtUtc = UtcNow;
            var fundamental = await _yahooFinanceSource.FetchFundamentalsAsync(code, cancellationToken);
            await _repository.AddFundamentalSnapshotAsync(instrumentId, fundamental, attemptedAtUtc, cancellationToken);
            fundamentalRecords = 1;
            await SucceedAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", instrumentId, fundamentalRecords, attemptedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", instrumentId, "Cancelled", "The Yahoo fundamentals refresh was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", instrumentId, Classify(exception), SafeMessage(exception));
        }

        return new InstrumentRefreshResult(instrumentId, chartRecords, fundamentalRecords);
    }

    public async Task<IReadOnlyList<InstrumentRefreshResult>> RefreshInstrumentsAsync(int? dailyUpdateRunId, IEnumerable<(int InstrumentId, string Code)> instruments, CancellationToken cancellationToken)
    {
        var results = new List<InstrumentRefreshResult>();
        foreach (var instrument in instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RefreshInstrumentAsync(dailyUpdateRunId, instrument.InstrumentId, instrument.Code, cancellationToken));
        }
        return results;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    private Task SucceedAsync(int? runId, string sourceKind, int? instrumentId, int recordCount, DateTime attemptedAtUtc, CancellationToken cancellationToken)
        => _repository.RecordFetchResultAsync(runId, sourceKind, instrumentId, "Succeeded", null, null, recordCount, attemptedAtUtc, cancellationToken);

    private Task FailAsync(int? runId, string sourceKind, int? instrumentId, string errorKind, string message)
        => _repository.RecordFetchResultAsync(runId, sourceKind, instrumentId, "Failed", errorKind, message, null, UtcNow, CancellationToken.None);

    private static string Classify(Exception exception)
    {
        if (exception is ExternalDataFetchException fetchException) return fetchException.ErrorKind;
        if (exception is HttpRequestException) return "NetworkError";
        if (exception is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 } }) return "DatabaseLocked";
        return "Unknown";
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message.Replace("\r", " ").Replace("\n", " ");
        return message.Length <= 1000 ? message : message[..1000];
    }

    private static DateOnly JstDate(DateTime utc)
    {
        TimeZoneInfo jst;
        try { jst = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"); }
        catch (TimeZoneNotFoundException) { jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"); }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, jst));
    }
}

public sealed record InstrumentRefreshResult(int InstrumentId, int ChartRecordsImported, int FundamentalSnapshotsAdded);
