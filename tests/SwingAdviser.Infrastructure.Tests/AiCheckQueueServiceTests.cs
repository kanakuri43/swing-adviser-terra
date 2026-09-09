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
    public void CodexCliStartInfo_UsesUserProfileAsHomeOnlyWhenHomeIsMissing()
    {
        var request = new AiCliRequest("codex.exe", null, null, [], "test prompt", TimeSpan.FromSeconds(5));

        var fallback = CodexCliExecutor.CreateStartInfo(request, null, @"C:\\Users\\test-user", @"C:\\Temp\\ai-result.json");
        var configured = CodexCliExecutor.CreateStartInfo(request, @"D:\\custom-home", @"C:\\Users\\test-user", @"C:\\Temp\\ai-result.json");

        Assert.Equal(@"C:\\Users\\test-user", fallback.Environment["HOME"]);
        Assert.Equal(@"D:\\custom-home", configured.Environment["HOME"]);
        Assert.Equal(["exec", "--ignore-user-config", "--output-last-message", @"C:\\Temp\\ai-result.json", "test prompt"], fallback.ArgumentList);
    }

    [Fact]
    public void CodexCliStartInfo_UsesExistingCodexHome()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), $"swing-adviser-codex-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(codexHome);
        try
        {
            var request = new AiCliRequest("codex.exe", null, null, [], "test prompt", TimeSpan.FromSeconds(5));

            var startInfo = CodexCliExecutor.CreateStartInfo(request, null, @"C:\\Users\\test-user", codexHomeDirectory: codexHome);

            Assert.Equal(codexHome, startInfo.Environment["CODEX_HOME"]);
        }
        finally
        {
            Directory.Delete(codexHome);
        }
    }

    [Fact]
    public void CodexCliPathResolver_FindsVersionedDesktopExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swing-adviser-codex-bin-{Guid.NewGuid():N}");
        var versionDirectory = Path.Combine(root, "version-1");
        Directory.CreateDirectory(versionDirectory);
        var executable = Path.Combine(versionDirectory, "codex.exe");
        File.WriteAllText(executable, string.Empty);
        try
        {
            Assert.Equal(executable, CodexCliPathResolver.FindExecutableInDirectory(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    [Fact]
    public async Task CliExecutorException_IsRecordedAsTerminalFailureInsteadOfLeavingAttemptRunning()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-ai-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migrationContext = CreateContext(connectionString)) await migrationContext.Database.MigrateAsync();
            int candidateId;
            await using (var seed = CreateContext(connectionString)) candidateId = await SeedCandidateAsync(seed);
            var options = new AiCheckOptions("codex", null, null, TimeSpan.FromSeconds(5), [], MaximumConcurrency: 2);
            var service = new AiCheckQueueService(() => CreateContext(connectionString), new ThrowingExecutor(), options);

            await service.EnqueueUserAsync([candidateId], CancellationToken.None);
            await WaitUntilAsync(async () =>
            {
                await using var check = CreateContext(connectionString);
                return await check.AiCheckAttempts.AnyAsync(item => item.Status == "Failed");
            });

            await using var assertion = CreateContext(connectionString);
            var attempt = await assertion.AiCheckAttempts.SingleAsync();
            Assert.Equal("Failed", attempt.Status);
            Assert.Equal("CliExecutionFailure", attempt.ErrorKind);
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal("The AI executor raised an exception; inspect the application diagnostics for details.", attempt.SanitizedStderr);
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CancelledCliResponse_IsRecordedAsCancelledTerminalAttempt()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-ai-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migrationContext = CreateContext(connectionString)) await migrationContext.Database.MigrateAsync();
            int candidateId;
            await using (var seed = CreateContext(connectionString)) candidateId = await SeedCandidateAsync(seed);
            var options = new AiCheckOptions("codex", null, null, TimeSpan.FromSeconds(5), [], MaximumConcurrency: 2);
            var service = new AiCheckQueueService(() => CreateContext(connectionString), new CancelledExecutor(), options);

            await service.EnqueueUserAsync([candidateId], CancellationToken.None);
            await WaitUntilAsync(async () =>
            {
                await using var check = CreateContext(connectionString);
                return await check.AiCheckAttempts.AnyAsync(item => item.Status == "Cancelled");
            });

            await using var assertion = CreateContext(connectionString);
            var attempt = await assertion.AiCheckAttempts.SingleAsync();
            Assert.Equal("Cancelled", attempt.Status);
            Assert.Equal("Cancelled", attempt.ErrorKind);
            Assert.NotNull(attempt.CompletedAtUtc);
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Overview_OnlyShowsCandidatesFromTheLatestCompletedScan()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-ai-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migrationContext = CreateContext(connectionString)) await migrationContext.Database.MigrateAsync();
            await using (var seed = CreateContext(connectionString))
            {
                await SeedCandidateAsync(seed, new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc), "Running", "1111");
                await SeedCandidateAsync(seed, new DateTime(2026, 8, 31, 2, 0, 0, DateTimeKind.Utc), "Succeeded", "2222");
            }

            var service = new AiCheckQueueService(() => CreateContext(connectionString), new FailedExecutor(), new AiCheckOptions("codex", null, null, TimeSpan.FromSeconds(5), [], MaximumConcurrency: 2));

            var overview = await service.GetOverviewAsync();

            var candidate = Assert.Single(overview.Candidates);
            Assert.Equal("2222", candidate.Code);
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Overview_CountsOnlyAttemptsFromTheLatestDailyUpdate()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-ai-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migrationContext = CreateContext(connectionString)) await migrationContext.Database.MigrateAsync();
            await using (var seed = CreateContext(connectionString))
            {
                var timestamp = new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc);
                var firstCandidateId = await SeedCandidateAsync(seed, timestamp, code: "1111");
                var secondCandidateId = await SeedCandidateAsync(seed, timestamp.AddHours(1), code: "2222");
                var previousRunId = await SeedDailyUpdateRunAsync(seed, timestamp);
                var latestRunId = await SeedDailyUpdateRunAsync(seed, timestamp.AddHours(2));
                await SeedAttemptAsync(seed, firstCandidateId, previousRunId, "TimedOut");
                await SeedAttemptAsync(seed, secondCandidateId, latestRunId, "Succeeded");
                await SeedAttemptAsync(seed, secondCandidateId, latestRunId, "Failed");
                await SeedAttemptAsync(seed, secondCandidateId, null, "TimedOut");
            }

            var service = new AiCheckQueueService(() => CreateContext(connectionString), new FailedExecutor(), new AiCheckOptions("codex", null, null, TimeSpan.FromSeconds(5), [], MaximumConcurrency: 2));

            var overview = await service.GetOverviewAsync();

            Assert.Equal(1, overview.SucceededCount);
            Assert.Equal(1, overview.FailedCount);
            Assert.Equal(0, overview.TimedOutCount);
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    private static async Task<int> SeedCandidateAsync(SwingAdviserDbContext context, DateTime? timestamp = null, string scanStatus = "Succeeded", string code = "7203")
    {
        var observedAt = timestamp ?? new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = observedAt };
        context.Instruments.Add(instrument); await context.SaveChangesAsync();
        context.InstrumentMasterRevisions.Add(new InstrumentMasterRevision { InstrumentId = instrument.InstrumentId, Code = code, Name = "テスト銘柄", MarketSegment = "Prime", InstrumentType = "Equity", ListedStatus = "Listed", ScanEligibility = "Eligible", EffectiveAtDate = new DateOnly(2026, 8, 31), AvailableAtUtc = observedAt, Source = "Test", SourceFileHash = "master", RecordedAtUtc = observedAt, Revision = 1, Status = "Active" });
        var manifest = new AnalysisInputManifest { InstrumentId = instrument.InstrumentId, EvaluationBarDate = new DateOnly(2026, 8, 31), AnalyzedAtUtc = observedAt, FirstBarDate = new DateOnly(2025, 1, 1), LastBarDate = new DateOnly(2026, 8, 31), BarCount = 250, PriceRevisionSetHash = "price", CorporateActionSetHash = "actions", ManifestHash = "manifest", CreatedAtUtc = observedAt };
        var strategy = new StrategyParameterSnapshot { StrategyKey = "candidate", StrategyVersion = "v1", IndicatorEngineVersion = "engine", CandidateScoringEngineVersion = "candidate-scoring-engine-v1", NormalizedParametersJson = "{}", ContentSha256 = $"strategy-{code}", CreatedAtUtc = observedAt };
        var scan = new ScanRun { RunType = "Manual", UniverseDefinitionHash = "universe", StartedAtUtc = observedAt, CompletedAtUtc = scanStatus == "Running" ? null : observedAt, Status = scanStatus, TotalInstruments = 1, SucceededCount = 1, FailedCount = 0 };
        context.AddRange(manifest, strategy, scan); await context.SaveChangesAsync();
        var indicator = new IndicatorResult { ScanRunId = scan.ScanRunId, InstrumentId = instrument.InstrumentId, EvaluationBarDate = new DateOnly(2026, 8, 31), AnalyzedAtUtc = observedAt, ManifestId = manifest.ManifestId, StrategyParameterSnapshotId = strategy.StrategyParameterSnapshotId, DataStatus = "Ok", HistoryAvailableCount = 250, HistoryRequiredCount = 201, VolumeRatioStatus = "Ok", RawValuesJson = "{}", CreatedAtUtc = observedAt };
        context.IndicatorResults.Add(indicator); await context.SaveChangesAsync();
        context.ScanRunResultUses.Add(new ScanRunResultUse { ScanRunId = scan.ScanRunId, IndicatorResultId = indicator.IndicatorResultId, UseKind = "Computed", UsedAtUtc = observedAt });
        await context.SaveChangesAsync();
        var candidate = new CandidateResult { IndicatorResultId = indicator.IndicatorResultId, InstrumentId = instrument.InstrumentId, Direction = "Long", SignalPurpose = "Entry", Matched = true, Score = 80, ConfidenceLabel = "High", CandidateScoringEngineVersion = "candidate-scoring-engine-v1", ScoreComponentsJson = "{}", CreatedAtUtc = observedAt };
        context.CandidateResults.Add(candidate); await context.SaveChangesAsync();
        return candidate.CandidateResultId;
    }

    private static async Task<int> SeedDailyUpdateRunAsync(SwingAdviserDbContext context, DateTime startedAtUtc)
    {
        var run = new DailyUpdateRun { StartedAtUtc = startedAtUtc, CompletedAtUtc = startedAtUtc.AddMinutes(1), Status = "Succeeded" };
        context.DailyUpdateRuns.Add(run); await context.SaveChangesAsync();
        return run.DailyUpdateRunId;
    }

    private static async Task SeedAttemptAsync(SwingAdviserDbContext context, int candidateResultId, int? dailyUpdateRunId, string status)
    {
        var candidate = await context.CandidateResults.Include(item => item.IndicatorResult).SingleAsync(item => item.CandidateResultId == candidateResultId);
        context.AiCheckAttempts.Add(new AiCheckAttempt
        {
            CandidateResultId = candidateResultId,
            TriggeringDailyUpdateRunId = dailyUpdateRunId,
            RequestedBy = dailyUpdateRunId is null ? "User" : "Auto",
            RequestedAtUtc = candidate.CreatedAtUtc,
            CompletedAtUtc = candidate.CreatedAtUtc,
            Status = status,
            EvaluationBarDate = candidate.IndicatorResult.EvaluationBarDate,
            NormalizedInputSnapshotJson = "{}",
            NormalizedInputSnapshotHash = $"input-{candidateResultId}-{status}-{dailyUpdateRunId}",
            TechnicalInputManifestId = candidate.IndicatorResult.ManifestId,
            StrategySnapshotHash = "strategy",
            PromptTemplateVersion = "test",
            PromptTemplateHash = "prompt",
            CliExecutablePath = "test-codex",
            TimeoutSeconds = 5,
        });
        await context.SaveChangesAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++) { if (await condition()) return; await Task.Delay(25); }
        throw new TimeoutException("The persistent queue did not complete in time.");
    }

    private static SwingAdviserDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connectionString).UseSnakeCaseNamingConvention().Options);
    private sealed class FailedExecutor : IAiCliExecutor { public Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiCliResponse("not-json", "simulated failure", 9, AiCliCompletion.Completed)); }
    private sealed class ThrowingExecutor : IAiCliExecutor { public Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("simulated executor exception"); }
    private sealed class CancelledExecutor : IAiCliExecutor { public Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiCliResponse(string.Empty, string.Empty, null, AiCliCompletion.Cancelled)); }
}
