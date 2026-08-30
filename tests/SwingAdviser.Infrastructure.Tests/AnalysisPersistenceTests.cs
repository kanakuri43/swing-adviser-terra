using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class AnalysisPersistenceTests
{
    [Fact]
    public void Step2Schema_PersistsFrozenAnalysisInputAndCandidateResult()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var analyzedAt = new DateTime(2026, 8, 30, 4, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = analyzedAt };
        context.Instruments.Add(instrument);
        context.SaveChanges();

        var dailyBar = CreateDailyBar(instrument.InstrumentId, analyzedAt);
        var corporateAction = new CorporateAction
        {
            InstrumentId = instrument.InstrumentId,
            ActionType = "CashDividend",
            EffectiveDate = new DateOnly(2026, 8, 28),
            AnnouncedAtUtc = analyzedAt,
            AvailableAtUtc = analyzedAt,
            FirstObservedAtUtc = analyzedAt,
            DividendAmountPerShare = 10m,
            Currency = "JPY",
            SourceEventId = "event-1",
            Source = "Test",
            RecordedAtUtc = analyzedAt,
            Revision = 1,
            Status = "Active",
        };
        context.AddRange(dailyBar, corporateAction);
        context.SaveChanges();

        var manifest = new AnalysisInputManifest
        {
            InstrumentId = instrument.InstrumentId,
            EvaluationBarDate = dailyBar.TradingDate,
            AnalyzedAtUtc = analyzedAt,
            FirstBarDate = dailyBar.TradingDate,
            LastBarDate = dailyBar.TradingDate,
            BarCount = 1,
            PriceRevisionSetHash = "price-revision-set",
            CorporateActionSetHash = "corporate-action-set",
            ManifestHash = "manifest-hash",
            CreatedAtUtc = analyzedAt,
        };
        var strategySnapshot = new StrategyParameterSnapshot
        {
            StrategyKey = "candidate-scoring",
            StrategyVersion = "v1",
            IndicatorEngineVersion = "ema-sma-seed-v1",
            CandidateScoringEngineVersion = "candidate-scoring-engine-v1",
            NormalizedParametersJson = "{}",
            ContentSha256 = "parameter-hash",
            CreatedAtUtc = analyzedAt,
        };
        var dailyUpdate = new DailyUpdateRun
        {
            StartedAtUtc = analyzedAt,
            Status = "Succeeded",
        };
        context.AddRange(manifest, strategySnapshot, dailyUpdate);
        context.SaveChanges();

        context.AddRange(
            new AnalysisInputManifestBar
            {
                ManifestId = manifest.ManifestId,
                TradingDate = dailyBar.TradingDate,
                DailyBarId = dailyBar.DailyBarId,
            },
            new AnalysisInputManifestCorporateAction
            {
                ManifestId = manifest.ManifestId,
                CorporateActionId = corporateAction.CorporateActionId,
            },
            new ExternalFetchResult
            {
                DailyUpdateRunId = dailyUpdate.DailyUpdateRunId,
                InstrumentId = instrument.InstrumentId,
                SourceKind = "PriceData",
                Status = "Succeeded",
                RecordCount = 1,
                AttemptedAtUtc = analyzedAt,
            });
        var scanRun = new ScanRun
        {
            DailyUpdateRunId = dailyUpdate.DailyUpdateRunId,
            RunType = "DailyUpdate",
            UniverseDefinitionHash = "universe-hash",
            StartedAtUtc = analyzedAt,
            Status = "Succeeded",
            TotalInstruments = 1,
            SucceededCount = 1,
            FailedCount = 0,
        };
        context.ScanRuns.Add(scanRun);
        context.SaveChanges();

        var indicator = new IndicatorResult
        {
            ScanRunId = scanRun.ScanRunId,
            InstrumentId = instrument.InstrumentId,
            EvaluationBarDate = dailyBar.TradingDate,
            AnalyzedAtUtc = analyzedAt,
            ManifestId = manifest.ManifestId,
            StrategyParameterSnapshotId = strategySnapshot.StrategyParameterSnapshotId,
            DataStatus = "Ok",
            HistoryAvailableCount = 250,
            HistoryRequiredCount = 201,
            MacdLine = 1.2345m,
            MacdSignal = 1.2m,
            MacdHistogram = 0.0345m,
            Ema20 = 100m,
            Ema50 = 95m,
            Ema200 = 80m,
            Atr14 = 3.25m,
            VolumeRatio = 1.5m,
            VolumeReferenceAverage = 1200000.25m,
            VolumeRatioStatus = "Ok",
            RawValuesJson = "{}",
            CreatedAtUtc = analyzedAt,
        };
        context.AddRange(
            indicator,
            new ScanExclusion
            {
                ScanRunId = scanRun.ScanRunId,
                InstrumentId = instrument.InstrumentId,
                Reason = "NotEligible",
            });
        context.SaveChanges();

        context.CandidateResults.Add(new CandidateResult
        {
            IndicatorResultId = indicator.IndicatorResultId,
            InstrumentId = instrument.InstrumentId,
            Direction = "Long",
            SignalPurpose = "Entry",
            Matched = true,
            Score = 85,
            ConfidenceLabel = "High",
            CandidateScoringEngineVersion = "candidate-scoring-engine-v1",
            ScoreComponentsJson = "{}",
            CreatedAtUtc = analyzedAt,
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var restored = context.IndicatorResults.Single();
        Assert.Equal(1.2345m, restored.MacdLine);
        Assert.Equal(3.25m, restored.Atr14);
        Assert.Single(context.AnalysisInputManifestBars);
        Assert.Single(context.AnalysisInputManifestCorporateActions);
        Assert.Equal(85, context.CandidateResults.Single().Score);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(macd_line), macd_line, analyzed_at_utc FROM indicator_results";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal("1.2345", reader.GetString(1));
        Assert.Equal("2026-08-30T04:00:00.0000000Z", reader.GetString(2));
    }

    [Fact]
    public void Step2Schema_RejectsDuplicateManifestBar()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var analyzedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = analyzedAt };
        context.Instruments.Add(instrument);
        context.SaveChanges();
        var dailyBar = CreateDailyBar(instrument.InstrumentId, analyzedAt);
        context.DailyBars.Add(dailyBar);
        context.SaveChanges();
        var manifest = new AnalysisInputManifest
        {
            InstrumentId = instrument.InstrumentId,
            EvaluationBarDate = dailyBar.TradingDate,
            AnalyzedAtUtc = analyzedAt,
            FirstBarDate = dailyBar.TradingDate,
            LastBarDate = dailyBar.TradingDate,
            BarCount = 1,
            PriceRevisionSetHash = "p",
            CorporateActionSetHash = "c",
            ManifestHash = "m",
            CreatedAtUtc = analyzedAt,
        };
        context.AnalysisInputManifests.Add(manifest);
        context.SaveChanges();
        context.AnalysisInputManifestBars.Add(new AnalysisInputManifestBar
        {
            ManifestId = manifest.ManifestId,
            TradingDate = dailyBar.TradingDate,
            DailyBarId = dailyBar.DailyBarId,
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        context.AnalysisInputManifestBars.Add(new AnalysisInputManifestBar
        {
            ManifestId = manifest.ManifestId,
            TradingDate = dailyBar.TradingDate,
            DailyBarId = dailyBar.DailyBarId,
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    private static DailyBar CreateDailyBar(int instrumentId, DateTime fetchedAtUtc) => new()
    {
        InstrumentId = instrumentId,
        TradingDate = new DateOnly(2026, 8, 28),
        Open = 100m,
        High = 101m,
        Low = 99m,
        Close = 100m,
        Volume = 1000,
        Source = "Test",
        FetchedAtUtc = fetchedAtUtc,
        Revision = 1,
        Status = "Final",
    };

    private static SqliteConnection OpenMigratedConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        using var context = CreateContext(connection);
        context.Database.Migrate();
        return connection;
    }

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SwingAdviserDbContext(options);
    }
}
