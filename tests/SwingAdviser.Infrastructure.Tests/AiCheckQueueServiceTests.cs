using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class AiCheckQueueServiceTests
{
    [Fact]
    public async Task CliFailure_CreatesTerminalAttemptWithoutInvalidatingTechnicalCandidate()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-ai-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migrationContext = CreateContext(connectionString)) await migrationContext.Database.MigrateAsync();
            int candidateId;
            await using (var seed = CreateContext(connectionString)) candidateId = await SeedCandidateAsync(seed);
            var options = new AiCheckOptions("codex", null, null, TimeSpan.FromSeconds(5), [], MaximumConcurrency: 2);
            var service = new AiCheckQueueService(() => CreateContext(connectionString), new FailedExecutor(), options);

            var queued = await service.EnqueueUserAsync([candidateId], CancellationToken.None);
            Assert.Equal(1, queued.QueuedCount);
            await WaitUntilAsync(async () =>
            {
                await using var check = CreateContext(connectionString);
                return await check.AiCheckAttempts.AnyAsync(item => item.Status == "Failed");
            });

            await using (var assertion = CreateContext(connectionString))
            {
                Assert.True((await assertion.CandidateResults.SingleAsync(item => item.CandidateResultId == candidateId)).Matched);
                var attempt = await assertion.AiCheckAttempts.SingleAsync();
                Assert.Equal("Failed", attempt.Status);
                Assert.Equal("CliNonZeroExit", attempt.ErrorKind);
                Assert.NotNull(attempt.RawResponseHash);
                Assert.Empty(assertion.AiCheckResults);
            }
        }
        finally
        {
            // The queue worker is intentionally fire-and-forget; Windows can retain the test file briefly after its terminal state is saved.
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    private static async Task<int> SeedCandidateAsync(SwingAdviserDbContext context)
    {
        var timestamp = new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = timestamp };
        context.Instruments.Add(instrument); await context.SaveChangesAsync();
        context.InstrumentMasterRevisions.Add(new InstrumentMasterRevision { InstrumentId = instrument.InstrumentId, Code = "7203", Name = "テスト銘柄", MarketSegment = "Prime", InstrumentType = "Equity", ListedStatus = "Listed", ScanEligibility = "Eligible", EffectiveAtDate = new DateOnly(2026, 8, 31), AvailableAtUtc = timestamp, Source = "Test", SourceFileHash = "master", RecordedAtUtc = timestamp, Revision = 1, Status = "Active" });
        var manifest = new AnalysisInputManifest { InstrumentId = instrument.InstrumentId, EvaluationBarDate = new DateOnly(2026, 8, 31), AnalyzedAtUtc = timestamp, FirstBarDate = new DateOnly(2025, 1, 1), LastBarDate = new DateOnly(2026, 8, 31), BarCount = 250, PriceRevisionSetHash = "price", CorporateActionSetHash = "actions", ManifestHash = "manifest", CreatedAtUtc = timestamp };
        var strategy = new StrategyParameterSnapshot { StrategyKey = "candidate", StrategyVersion = "v1", IndicatorEngineVersion = "engine", CandidateScoringEngineVersion = "candidate-scoring-engine-v1", NormalizedParametersJson = "{}", ContentSha256 = "strategy", CreatedAtUtc = timestamp };
        var scan = new ScanRun { RunType = "Manual", UniverseDefinitionHash = "universe", StartedAtUtc = timestamp, Status = "Succeeded", TotalInstruments = 1, SucceededCount = 1, FailedCount = 0 };
        context.AddRange(manifest, strategy, scan); await context.SaveChangesAsync();
        var indicator = new IndicatorResult { ScanRunId = scan.ScanRunId, InstrumentId = instrument.InstrumentId, EvaluationBarDate = new DateOnly(2026, 8, 31), AnalyzedAtUtc = timestamp, ManifestId = manifest.ManifestId, StrategyParameterSnapshotId = strategy.StrategyParameterSnapshotId, DataStatus = "Ok", HistoryAvailableCount = 250, HistoryRequiredCount = 201, VolumeRatioStatus = "Ok", RawValuesJson = "{}", CreatedAtUtc = timestamp };
        context.IndicatorResults.Add(indicator); await context.SaveChangesAsync();
        var candidate = new CandidateResult { IndicatorResultId = indicator.IndicatorResultId, InstrumentId = instrument.InstrumentId, Direction = "Long", SignalPurpose = "Entry", Matched = true, Score = 80, ConfidenceLabel = "High", CandidateScoringEngineVersion = "candidate-scoring-engine-v1", ScoreComponentsJson = "{}", CreatedAtUtc = timestamp };
        context.CandidateResults.Add(candidate); await context.SaveChangesAsync();
        return candidate.CandidateResultId;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++) { if (await condition()) return; await Task.Delay(25); }
        throw new TimeoutException("The persistent queue did not complete in time.");
    }

    private static SwingAdviserDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connectionString).UseSnakeCaseNamingConvention().Options);
    private sealed class FailedExecutor : IAiCliExecutor { public Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiCliResponse("not-json", "simulated failure", 9, AiCliCompletion.Completed)); }
}
