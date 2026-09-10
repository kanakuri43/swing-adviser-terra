using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.DailyUpdates;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Infrastructure.Positions;

namespace SwingAdviser.Infrastructure.Tests;

/// <summary>
/// A durable, representative path across Phases 2-9. The AI executor is a
/// deterministic test double; this test never starts a real CLI or places an order.
/// </summary>
public class RepresentativeWorkflowEndToEndTests
{
    [Fact]
    public async Task CandidateAnalyzedAfterEnteredExecution_RejectsTheOpeningWithoutPersistingPartialRiskData()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-e2e-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migration = CreateContext(connectionString)) await migration.Database.MigrateAsync();
            var ids = await SeedCandidateAsync(connectionString);
            await using var context = CreateContext(connectionString);
            var registration = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));

            await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RegisterOpenAsync(new ManualOpenTradeRequest(
                ids.InstrumentId, "Long", new DateTimeOffset(2026, 8, 31, 9, 30, 0, TimeSpan.FromHours(9)), 100m, 100, "JPY", "candidate", "v1",
                CandidateResultId: ids.CandidateResultId, UserConfirmed: true)));

            Assert.Empty(await context.Positions.ToListAsync());
            Assert.Empty(await context.TradeExecutions.ToListAsync());
            Assert.Empty(await context.MarginLots.ToListAsync());
            Assert.Empty(await context.RiskBasisSnapshots.ToListAsync());
            Assert.Empty(await context.RiskPlans.ToListAsync());
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CandidateAiManualOpenHoldingEvaluationAndManualClose_AreAuditedEndToEnd()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-e2e-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migration = CreateContext(connectionString)) await migration.Database.MigrateAsync();
            var ids = await SeedCandidateAsync(connectionString);

            var aiQueue = new AiCheckQueueService(
                () => CreateContext(connectionString),
                new SucceededExecutor(),
                new AiCheckOptions("test-codex", null, null, TimeSpan.FromSeconds(5), [], MaximumConcurrency: 2));
            var queued = await aiQueue.EnqueueUserAsync([ids.CandidateResultId], CancellationToken.None);
            Assert.Equal(1, queued.QueuedCount);
            await WaitUntilAsync(async () =>
            {
                await using var assertion = CreateContext(connectionString);
                return await assertion.AiCheckAttempts.AnyAsync(item => item.CandidateResultId == ids.CandidateResultId && item.Status == "Succeeded");
            });

            await using (var context = CreateContext(connectionString))
            {
                var registration = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
                var openedAt = new DateTimeOffset(2026, 8, 31, 10, 15, 0, TimeSpan.FromHours(9));
                var open = await registration.RegisterOpenAsync(new ManualOpenTradeRequest(
                    ids.InstrumentId, "Long", openedAt, 100m, 200, "JPY", "candidate", "v1",
                    CandidateResultId: ids.CandidateResultId, UserConfirmed: true));
                var lot = await context.MarginLots.SingleAsync(item => item.MarginLotId == open.MarginLotIds.Single());
                var basis = await context.RiskBasisSnapshots.SingleAsync(item => item.MarginLotId == lot.MarginLotId);
                var initialPlan = await context.RiskPlans.SingleAsync(item => item.MarginLotId == lot.MarginLotId);
                Assert.Equal(10m, basis.AtrBasis);
                Assert.Equal(ids.CandidateResultId, basis.SourceCandidateResultId);
                Assert.Equal(70m, initialPlan.StopPrice);
                Assert.Equal(145m, initialPlan.TakeProfitPrice);
                context.DailyBars.Add(new DailyBar
                {
                    InstrumentId = ids.InstrumentId,
                    TradingDate = new DateOnly(2026, 9, 1),
                    Open = 118m,
                    High = 119m,
                    Low = 116m,
                    Close = 118m,
                    Volume = 1_000,
                    Source = "Test",
                    FetchedAtUtc = new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc),
                    Revision = 1,
                    Status = "Final",
                });
                await context.SaveChangesAsync();

                var evaluationTime = new DateTime(2026, 9, 1, 7, 30, 0, DateTimeKind.Utc);
                var reevaluation = await new HoldingReevaluationService(new EfHoldingReevaluationStore(context))
                    .ReevaluateAsync(new DateOnly(2026, 9, 1), evaluationTime);
                // The historical opening was entered after this evaluation session in the test, so
                // the plan is correctly excluded rather than backdated into a past decision.
                Assert.Null(Assert.Single(reevaluation.Positions).Outcome.Decision);
                Assert.Equal("IntradaySequenceUnknown", reevaluation.Positions.Single().Outcome.EvaluationOutcome);
                Assert.Equal("NotApplicable", reevaluation.Positions.Single().Outcome.PartialExitStatus);
                Assert.Single(await context.PositionHoldingEvaluations.ToListAsync());
                Assert.Empty(await context.LotHoldingEvaluations.ToListAsync());

                // The user, not the evaluation, explicitly enters and confirms the close and its lot allocation.
                await registration.RegisterCloseAsync(new ManualCloseTradeRequest(
                    open.PositionId, new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.FromHours(9)),
                    118m, 200, "JPY", [new ManualLotAllocationInput(lot.MarginLotId, 200m)], UserConfirmed: true));
            }

            await using (var assertion = CreateContext(connectionString))
            {
                var attempt = await assertion.AiCheckAttempts.SingleAsync();
                Assert.Equal("Succeeded", attempt.Status);
                Assert.Equal("Bullish", (await assertion.AiCheckResults.SingleAsync()).Verdict);
                var executions = await assertion.TradeExecutions.OrderBy(item => item.TradeExecutionId).ToListAsync();
                Assert.Equal(["Open", "Close"], executions.Select(item => item.ExecutionRole));
                Assert.Equal(100m, executions.Single(item => item.ExecutionRole == "Open").Price);
                Assert.Equal(118m, executions.Single(item => item.ExecutionRole == "Close").Price);
                Assert.Equal("Closed", (await assertion.Positions.SingleAsync()).Status);
                Assert.Equal("Closed", (await assertion.MarginLots.SingleAsync()).Status);
            }
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    internal static async Task<SeedIds> SeedCandidateAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        var timestamp = new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = timestamp };
        context.Instruments.Add(instrument);
        await context.SaveChangesAsync();
        context.InstrumentMasterRevisions.Add(new InstrumentMasterRevision { InstrumentId = instrument.InstrumentId, Code = "7203", Name = "テスト銘柄", MarketSegment = "Prime", InstrumentType = "DomesticCommonStock", ListedStatus = "Listed", ScanEligibility = "Eligible", EffectiveAtDate = new DateOnly(2026, 8, 31), AvailableAtUtc = timestamp, Source = "Test", SourceFileHash = "master", RecordedAtUtc = timestamp, Revision = 1, Status = "Active" });
        var manifest = new AnalysisInputManifest { InstrumentId = instrument.InstrumentId, EvaluationBarDate = new DateOnly(2026, 8, 31), AnalyzedAtUtc = timestamp, FirstBarDate = new DateOnly(2025, 1, 1), LastBarDate = new DateOnly(2026, 8, 31), BarCount = 250, PriceRevisionSetHash = "price", CorporateActionSetHash = "actions", ManifestHash = "manifest", CreatedAtUtc = timestamp };
        var strategy = new StrategyParameterSnapshot { StrategyKey = "candidate", StrategyVersion = "v1", IndicatorEngineVersion = "indicator-engine-v1", CandidateScoringEngineVersion = "candidate-scoring-engine-v1", NormalizedParametersJson = "{}", ContentSha256 = new string('d', 64), CreatedAtUtc = timestamp };
        var scan = new ScanRun { RunType = "Manual", UniverseDefinitionHash = "universe", StartedAtUtc = timestamp, Status = "Succeeded", TotalInstruments = 1, SucceededCount = 1, FailedCount = 0 };
        context.AddRange(manifest, strategy, scan);
        await context.SaveChangesAsync();
        var indicator = new IndicatorResult { ScanRunId = scan.ScanRunId, InstrumentId = instrument.InstrumentId, EvaluationBarDate = new DateOnly(2026, 8, 31), AnalyzedAtUtc = timestamp, ManifestId = manifest.ManifestId, StrategyParameterSnapshotId = strategy.StrategyParameterSnapshotId, DataStatus = "Ok", HistoryAvailableCount = 250, HistoryRequiredCount = 201, Atr14 = 10m, VolumeRatioStatus = "Ok", RawValuesJson = "{}", CreatedAtUtc = timestamp };
        context.IndicatorResults.Add(indicator);
        await context.SaveChangesAsync();
        context.ScanRunResultUses.Add(new ScanRunResultUse { ScanRunId = scan.ScanRunId, IndicatorResultId = indicator.IndicatorResultId, UseKind = "Computed", UsedAtUtc = timestamp });
        await context.SaveChangesAsync();
        var candidate = new CandidateResult { IndicatorResultId = indicator.IndicatorResultId, InstrumentId = instrument.InstrumentId, Direction = "Long", SignalPurpose = "Entry", Matched = true, Score = 80, ConfidenceLabel = "High", CandidateScoringEngineVersion = "candidate-scoring-engine-v1", ScoreComponentsJson = "{}", CreatedAtUtc = timestamp };
        context.CandidateResults.Add(candidate);
        await context.SaveChangesAsync();
        return new SeedIds(instrument.InstrumentId, candidate.CandidateResultId, indicator.IndicatorResultId, strategy.StrategyParameterSnapshotId);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("The persistent AI queue did not reach a terminal success state.");
    }

    private static SwingAdviserDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connectionString).UseSnakeCaseNamingConvention().Options);

    internal sealed record SeedIds(int InstrumentId, int CandidateResultId, int IndicatorResultId, int StrategyParameterSnapshotId);

    private sealed class SucceededExecutor : IAiCliExecutor
    {
        public Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiCliResponse(
            """{"schemaVersion":"ai-result-v1","outcome":"Succeeded","verdict":"Bullish","confidence":"High","summary":"Test-only AI check result.","technicalView":"Positive","fundamentalView":null,"positiveFactors":[],"riskFactors":[],"invalidationConditions":[],"checkedAtUtc":"2026-08-31T02:00:00.0000000Z","sources":[{"url":"https://example.test/source","title":"Test source","publishedAtUtc":null,"retrievedAtUtc":"2026-08-31T01:30:00.0000000Z"}]}""",
            string.Empty, 0, AiCliCompletion.Completed));
    }
}
