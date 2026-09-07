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
    private readonly int _maxConcurrentInstrumentFetches;

    public MarketDataIngestionService(
        MarketDataRepository repository,
        IJpxListedIssuesSource listedIssuesSource,
        IJpxMarginIssuesSource marginIssuesSource,
        IYahooFinanceSource yahooFinanceSource,
        TimeProvider? clock = null,
        int maxConcurrentInstrumentFetches = 1)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _listedIssuesSource = listedIssuesSource ?? throw new ArgumentNullException(nameof(listedIssuesSource));
        _marginIssuesSource = marginIssuesSource ?? throw new ArgumentNullException(nameof(marginIssuesSource));
        _yahooFinanceSource = yahooFinanceSource ?? throw new ArgumentNullException(nameof(yahooFinanceSource));
        _clock = clock ?? TimeProvider.System;
        _maxConcurrentInstrumentFetches = Math.Clamp(maxConcurrentInstrumentFetches, 1, 8);
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
        var fetched = await FetchInstrumentAsync(new InstrumentRefreshTarget(instrumentId, code, true, null, true), 0, cancellationToken);
        return await PersistInstrumentAsync(dailyUpdateRunId, fetched, cancellationToken);
    }

    public Task<IReadOnlyList<InstrumentRefreshTarget>> CreateRefreshTargetsAsync(
        IEnumerable<(int InstrumentId, string Code)> instruments,
        DateOnly evaluationBarDate,
        DateTime analyzedAtUtc,
        DateTime chartPeriodStartUtc,
        CancellationToken cancellationToken)
        => _repository.CreateRefreshTargetsAsync(instruments, evaluationBarDate, analyzedAtUtc, chartPeriodStartUtc, cancellationToken);

    public async Task<IReadOnlyList<InstrumentRefreshResult>> RefreshInstrumentsAsync(int? dailyUpdateRunId, IEnumerable<(int InstrumentId, string Code)> instruments,
        CancellationToken cancellationToken, IProgress<InstrumentRefreshProgress>? progress = null)
    {
        return await RefreshInstrumentsAsync(dailyUpdateRunId, instruments.Select(item => new InstrumentRefreshTarget(item.InstrumentId, item.Code, true, null, true)), cancellationToken, progress);
    }

    /// <summary>Overlaps bounded network fetches with serial SQLite persistence without sharing the DbContext across threads.</summary>
    public async Task<IReadOnlyList<InstrumentRefreshResult>> RefreshInstrumentsAsync(int? dailyUpdateRunId, IEnumerable<InstrumentRefreshTarget> instruments,
        CancellationToken cancellationToken, IProgress<InstrumentRefreshProgress>? progress = null)
    {
        var targets = instruments.ToArray();
        var results = new InstrumentRefreshResult[targets.Length];
        var pending = new List<Task<FetchedInstrument>>(_maxConcurrentInstrumentFetches);
        var nextTargetIndex = 0;
        var completedCount = 0;
        var successfulCount = 0;
        var failedCount = 0;
        var reusedCount = 0;

        while (nextTargetIndex < targets.Length || pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (nextTargetIndex < targets.Length && pending.Count < _maxConcurrentInstrumentFetches)
            {
                var target = targets[nextTargetIndex];
                if (!target.RefreshChart && !target.RefreshFundamentals)
                {
                    results[nextTargetIndex] = new InstrumentRefreshResult(target.InstrumentId, 0, 0);
                    completedCount++;
                    reusedCount++;
                    progress?.Report(new InstrumentRefreshProgress(completedCount, targets.Length, target.Code, UsedCachedChart: true, successfulCount, failedCount, reusedCount));
                    nextTargetIndex++;
                    continue;
                }

                pending.Add(FetchInstrumentAsync(target, nextTargetIndex, cancellationToken));
                nextTargetIndex++;
            }

            if (pending.Count == 0) continue;

            var completedFetch = await Task.WhenAny(pending);
            pending.Remove(completedFetch);
            var fetched = await completedFetch;
            var result = await PersistInstrumentAsync(dailyUpdateRunId, fetched, cancellationToken);
            results[fetched.TargetIndex] = result;
            completedCount++;
            if (result.ChartSucceeded) successfulCount++;
            if (result.ChartFailed) failedCount++;
            progress?.Report(new InstrumentRefreshProgress(completedCount, targets.Length, fetched.Target.Code, false, successfulCount, failedCount, reusedCount));
        }

        return results;
    }

    private async Task<FetchedInstrument> FetchInstrumentAsync(InstrumentRefreshTarget target, int targetIndex, CancellationToken cancellationToken)
    {
        Task<FetchAttempt<YahooChartSourceSnapshot>>? chartTask = target.RefreshChart
            ? FetchAsync(() => FetchChartAsync(target, cancellationToken), cancellationToken)
            : null;
        Task<FetchAttempt<FundamentalDataSourceRecord>>? fundamentalTask = target.RefreshFundamentals
            ? FetchAsync(() => _yahooFinanceSource.FetchFundamentalsAsync(target.Code, cancellationToken), cancellationToken)
            : null;
        if (chartTask is not null && fundamentalTask is not null) await Task.WhenAll(chartTask, fundamentalTask);
        else if (chartTask is not null) await chartTask;
        else if (fundamentalTask is not null) await fundamentalTask;
        return new FetchedInstrument(targetIndex, target, chartTask is null ? null : await chartTask, fundamentalTask is null ? null : await fundamentalTask);
    }

    private Task<YahooChartSourceSnapshot> FetchChartAsync(InstrumentRefreshTarget target, CancellationToken cancellationToken)
        => _yahooFinanceSource is IIncrementalYahooFinanceSource incrementalSource
            ? incrementalSource.FetchChartAsync(target.Code, target.ChartPeriodStartUtc, cancellationToken)
            : _yahooFinanceSource.FetchChartAsync(target.Code, cancellationToken);

    private async Task<InstrumentRefreshResult> PersistInstrumentAsync(int? dailyUpdateRunId, FetchedInstrument fetched, CancellationToken cancellationToken)
    {
        var chartRecords = 0;
        var chartSucceeded = false;
        var chartFailed = false;
        if (fetched.Chart?.Value is not null)
        {
            try
            {
                chartRecords = await _repository.ImportYahooChartAsync(fetched.Target.InstrumentId, fetched.Chart.Value, fetched.Chart.AttemptedAtUtc, cancellationToken);
                await SucceedAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", fetched.Target.InstrumentId, fetched.Chart.Value.DailyBars.Count + fetched.Chart.Value.CorporateActions.Count, fetched.Chart.AttemptedAtUtc, cancellationToken);
                chartSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await FailAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", fetched.Target.InstrumentId, "Cancelled", "The Yahoo chart refresh was cancelled.");
                throw;
            }
            catch (Exception exception)
            {
                await FailAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", fetched.Target.InstrumentId, Classify(exception), SafeMessage(exception));
                chartFailed = true;
            }
        }
        else if (fetched.Chart is not null)
        {
            await FailAsync(dailyUpdateRunId, "YahooFinanceChartApiV8", fetched.Target.InstrumentId, Classify(fetched.Chart.Error!), SafeMessage(fetched.Chart.Error!));
            chartFailed = true;
        }

        var fundamentalRecords = 0;
        if (fetched.Fundamental?.Value is not null)
        {
            try
            {
                await _repository.AddFundamentalSnapshotAsync(fetched.Target.InstrumentId, fetched.Fundamental.Value, fetched.Fundamental.AttemptedAtUtc, cancellationToken);
                fundamentalRecords = 1;
                await SucceedAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", fetched.Target.InstrumentId, fundamentalRecords, fetched.Fundamental.AttemptedAtUtc, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await FailAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", fetched.Target.InstrumentId, "Cancelled", "The Yahoo fundamentals refresh was cancelled.");
                throw;
            }
            catch (Exception exception)
            {
                await FailAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", fetched.Target.InstrumentId, Classify(exception), SafeMessage(exception));
            }
        }
        else if (fetched.Fundamental is not null)
        {
            await FailAsync(dailyUpdateRunId, "YahooFinanceQuoteApiV7", fetched.Target.InstrumentId, Classify(fetched.Fundamental.Error!), SafeMessage(fetched.Fundamental.Error!));
        }

        return new InstrumentRefreshResult(fetched.Target.InstrumentId, chartRecords, fundamentalRecords, chartSucceeded, chartFailed);
    }

    private async Task<FetchAttempt<T>> FetchAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) where T : class
    {
        var attemptedAtUtc = UtcNow;
        try
        {
            return new FetchAttempt<T>(await operation(), null, attemptedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FetchAttempt<T>(null, exception, attemptedAtUtc);
        }
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

internal sealed record FetchAttempt<T>(T? Value, Exception? Error, DateTime AttemptedAtUtc) where T : class;
internal sealed record FetchedInstrument(int TargetIndex, InstrumentRefreshTarget Target, FetchAttempt<YahooChartSourceSnapshot>? Chart, FetchAttempt<FundamentalDataSourceRecord>? Fundamental);

/// <summary>Visible count for a long-running universe refresh. It is informational and never changes analysis outcomes.</summary>
public sealed record InstrumentRefreshProgress(
    int CompletedCount,
    int TotalCount,
    string LastCompletedCode,
    bool UsedCachedChart = false,
    int SuccessfulCount = 0,
    int FailedCount = 0,
    int ReusedCount = 0);

public sealed record InstrumentRefreshResult(int InstrumentId, int ChartRecordsImported, int FundamentalSnapshotsAdded, bool ChartSucceeded = false, bool ChartFailed = false);
