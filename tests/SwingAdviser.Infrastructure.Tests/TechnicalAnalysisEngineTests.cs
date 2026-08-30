using SwingAdviser.Domain.Analysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Application.Analysis;

namespace SwingAdviser.Infrastructure.Tests;

public class TechnicalAnalysisEngineTests
{
    [Fact]
    public void Calculate_Requires201BarsAndUsesPriorTwentyBarsForVolumeRatio()
    {
        var insufficient = new PointInTimeAnalysisSeries(1, 1, new DateOnly(2026, 8, 28), DateTime.UtcNow, "m", "p", "c", CreateBars(200, 100), "Ok", 201);
        var engine = new TechnicalIndicatorEngine();
        Assert.Equal("InsufficientHistory", engine.Calculate(insufficient, new TechnicalStrategyParameters()).DataStatus);

        var completeBars = CreateBars(201, 150);
        var series = new PointInTimeAnalysisSeries(1, 1, completeBars[^1].TradingDate, DateTime.UtcNow, "m", "p", "c", completeBars, "Ok", 201);
        var result = engine.Calculate(series, new TechnicalStrategyParameters());
        Assert.Equal("Ok", result.DataStatus);
        Assert.Equal(100m, result.VolumeReferenceAverage);
        Assert.Equal(1.5m, result.VolumeRatio);
    }

    [Fact]
    public void Calculate_RejectsZeroVolumeReferenceInsteadOfGuessingARatio()
    {
        var bars = CreateBars(201, 100).Select(bar => bar with { Volume = 0 }).ToArray();
        bars[^1] = bars[^1] with { Volume = 100 };
        var series = new PointInTimeAnalysisSeries(1, 1, bars[^1].TradingDate, DateTime.UtcNow, "m", "p", "c", bars, "Ok", 201);
        var result = new TechnicalIndicatorEngine().Calculate(series, new TechnicalStrategyParameters());
        Assert.Equal("InvalidData", result.DataStatus);
        Assert.Equal("ReferenceAverageZero", result.VolumeRatioStatus);
        Assert.Null(result.VolumeRatio);
    }

    [Fact]
    public void Scoring_LongUsesNoVolumePoints_AndShortUsesFullVolumeAtTwo()
    {
        var longIndicators = Indicators(macdLine: 2m, signal: 1m, ema20: 12m, ema50: 11m, ema200: 10m, atr: 1m, volume: 1.5m);
        var scoring = new CandidateScoringEngine();
        var longResult = scoring.Evaluate(longIndicators, "Long", new TechnicalStrategyParameters());
        Assert.True(longResult.Matched);
        Assert.Equal(75, longResult.Score);
        Assert.Contains("\"volumeWeight\":0", longResult.ComponentsJson, StringComparison.Ordinal);

        var shortIndicators = Indicators(macdLine: 1m, signal: 2m, ema20: 10m, ema50: 11m, ema200: 12m, atr: 0m, volume: 2m);
        var shortResult = scoring.Evaluate(shortIndicators, "Short", new TechnicalStrategyParameters());
        Assert.True(shortResult.Matched);
        Assert.Equal(100, shortResult.Score);
        Assert.Equal("High", shortResult.ConfidenceLabel);
    }

    [Fact]
    public void Scoring_RejectsEqualMacdOrEmaAndIncludesExactVolumeBoundary()
    {
        var scoring = new CandidateScoringEngine();
        Assert.False(scoring.Evaluate(Indicators(1m, 1m, 12m, 11m, 10m, 1m, 1.5m), "Long", new TechnicalStrategyParameters()).Matched);
        Assert.False(scoring.Evaluate(Indicators(2m, 1m, 11m, 11m, 10m, 1m, 1.5m), "Long", new TechnicalStrategyParameters()).Matched);
        Assert.True(scoring.Evaluate(Indicators(2m, 1m, 12m, 11m, 10m, 1m, 1.5m), "Long", new TechnicalStrategyParameters()).Matched);
    }

    [Fact]
    public async Task PointInTimeStore_FailsClosedForUnverifiedCorporateAction()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var analyzed = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = analyzed };
        context.Instruments.Add(instrument); await context.SaveChangesAsync();
        var bars = CreateBars(201, 100).Select(bar => new DailyBar { InstrumentId = instrument.InstrumentId, TradingDate = bar.TradingDate, Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close, Volume = bar.Volume, Source = "YahooFinanceChartApiV8", FetchedAtUtc = analyzed, Revision = 1, Status = "Final" });
        context.DailyBars.AddRange(bars);
        context.DailyBarHistoryCoverages.Add(new DailyBarHistoryCoverage { InstrumentId = instrument.InstrumentId, Source = "YahooFinanceChartApiV8", EarliestReturnedDate = new DateOnly(2025, 1, 1), LatestReturnedDate = new DateOnly(2025, 7, 20), FullHistoryConfirmed = true, ObservedAtUtc = analyzed, Revision = 1, Status = "Complete" });
        context.CorporateActions.Add(new CorporateAction { InstrumentId = instrument.InstrumentId, ActionType = "CashDividend", EffectiveDate = new DateOnly(2025, 6, 1), AvailableAtUtc = analyzed, FirstObservedAtUtc = analyzed, SourceEventId = "unknown", Source = "Yahoo", RecordedAtUtc = analyzed, Revision = 1, Status = "PointInTimeUnverified" });
        await context.SaveChangesAsync();

        var series = await new EfTechnicalScanStore(context).BuildSeriesAsync(instrument.InstrumentId, new DateOnly(2025, 7, 20), analyzed, 201, CancellationToken.None);
        Assert.Equal("PointInTimeUnverified", series.DataStatus);
        Assert.Empty(series.Bars);
        Assert.Single(context.AnalysisInputManifests);
    }

    [Fact]
    public async Task ScanService_OrdersCodesAndContinuesAfterOneInstrumentFailure()
    {
        var store = new RecordingStore();
        var parameters = new TechnicalStrategyParameters();
        var request = new TechnicalScanRequest(store.Series.Bars[^1].TradingDate, DateTime.UtcNow, null, "universe", parameters);
        var result = await new AllInstrumentScanService(store).RunAsync(request, null, CancellationToken.None);
        Assert.Equal("PartiallySucceeded", result.Status);
        Assert.Equal(new[] { "1000", "2000" }, store.ProcessedCodes);
        Assert.Equal(1, result.FailedCount);
    }

    private static IReadOnlyList<AdjustedDailyBar> CreateBars(int count, long lastVolume)
    {
        var start = new DateOnly(2025, 1, 1);
        return Enumerable.Range(0, count).Select(index =>
        {
            var close = 100m + index;
            return new AdjustedDailyBar(index + 1, start.AddDays(index), close - 1m, close + 1m, close - 2m, close, index == count - 1 ? lastVolume : 100);
        }).ToArray();
    }

    private static IndicatorComputation Indicators(decimal macdLine, decimal signal, decimal ema20, decimal ema50, decimal ema200, decimal atr, decimal volume)
        => new("Ok", 201, 201, macdLine, signal, macdLine - signal, ema20, ema50, ema200, atr, volume, 100m, "Ok", "{}", 0m, 0m, 0m, 0m, 0m);

    private static SqliteConnection OpenMigratedConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys = ON;"; command.ExecuteNonQuery();
        using var context = CreateContext(connection); context.Database.Migrate(); return connection;
    }
    private static SwingAdviserDbContext CreateContext(SqliteConnection connection) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options);

    private sealed class RecordingStore : ITechnicalScanStore
    {
        public PointInTimeAnalysisSeries Series { get; } = new(1, 1, CreateBars(201, 150)[^1].TradingDate, DateTime.UtcNow, "m", "p", "c", CreateBars(201, 150), "Ok", 201);
        public List<string> ProcessedCodes { get; } = [];
        public Task<IReadOnlyList<TechnicalScanInstrument>> GetEligibleUniverseAsync(DateOnly date, DateTime analyzedAtUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TechnicalScanInstrument>>([new(2, "2000"), new(1, "1000")]);
        public Task<int> GetOrCreateStrategySnapshotAsync(TechnicalStrategyParameters parameters, DateTime createdAtUtc, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<int> CreateScanRunAsync(TechnicalScanRequest request, int totalInstruments, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<PointInTimeAnalysisSeries> BuildSeriesAsync(int instrumentId, DateOnly evaluationBarDate, DateTime analyzedAtUtc, int requiredHistoryCount, CancellationToken cancellationToken)
        {
            ProcessedCodes.Add(instrumentId == 1 ? "1000" : "2000");
            if (instrumentId == 2) throw new InvalidOperationException();
            return Task.FromResult(Series);
        }
        public Task<int> SaveIndicatorAsync(int scanRunId, int strategySnapshotId, PointInTimeAnalysisSeries series, IndicatorComputation indicator, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task SaveCandidatesAsync(int indicatorResultId, int instrumentId, IReadOnlyList<CandidateEvaluation> candidates, DateTime createdAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveExclusionAsync(int scanRunId, int instrumentId, string reason, int available, int required, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteScanRunAsync(int scanRunId, string status, int succeededCount, int failedCount, DateTime completedAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
