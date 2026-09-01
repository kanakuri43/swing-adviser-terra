using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Application.DailyUpdates;

/// <summary>Small explicit adapter for workflow collaborators which belong to later phases or environment-specific configuration.</summary>
public sealed class DelegateDailyUpdateStage(DailyUpdateStep step, Func<DailyUpdateContext, CancellationToken, Task<DailyUpdateStepResult>> execute) : IDailyUpdateStage
{
    public DailyUpdateStep Step => step;
    public Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken) => execute(context, cancellationToken);
}

public sealed class CorporateActionAdjustmentDailyUpdateStage(CorporateActionPositionAdjustmentService service) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.ApplyCorporateActionAdjustments;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken)
    {
        var result = await service.ApplyAsync(context.Request.EvaluationBarDate, context.AnalyzedAtUtc, cancellationToken);
        return new DailyUpdateStepResult(result.AppliedCount, result.ReconciliationRequiredCount, result.ReconciliationRequiredCount == 0 ? "Corporate-action adjustments were applied." : "Some holdings require corporate-action reconciliation.");
    }
}

public sealed class HoldingReevaluationDailyUpdateStage(HoldingReevaluationService service) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.ReevaluateHoldings;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken)
    {
        var result = await service.ReevaluateAsync(context.Request.EvaluationBarDate, context.AnalyzedAtUtc, cancellationToken);
        return new DailyUpdateStepResult(result.EvaluatedPositionCount - result.IndeterminatePositionCount, result.IndeterminatePositionCount, result.IndeterminatePositionCount == 0 ? "Open positions were re-evaluated." : "Some positions were left without a decision (fail-closed).");
    }
}

public sealed class DailyUpdateTechnicalState
{
    public TechnicalScanResult? ScanResult { get; internal set; }
}

/// <summary>Explicitly documents the scan boundary: manifests are persisted per instrument immediately before indicator calculation.</summary>
public sealed class PointInTimePreparationDailyUpdateStage : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.BuildPointInTimeSeries;
    public Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new DailyUpdateStepResult(0, 0, "Point-in-time series are generated and frozen per instrument by the following technical-analysis stage."));
}

/// <summary>All source services append their own manifests, snapshots, evaluations, and fetch audit rows; this is a durable checkpoint, not a duplicate write.</summary>
public sealed class PersistResultsDailyUpdateStage : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.PersistAnalysisResults;
    public Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new DailyUpdateStepResult(0, 0, "Analysis and holding outputs were persisted append-only by their producing stages."));
}

public interface IDailyUpdateResultsPublisher
{
    Task PublishAsync(DailyUpdateRunResultPlaceholder update, CancellationToken cancellationToken);
}

/// <summary>Presentation callback boundary. Phase 8 supplies the visual implementation without allowing UI code to perform analysis itself.</summary>
public sealed class PublishResultsDailyUpdateStage(IDailyUpdateResultsPublisher publisher) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.PublishResults;
    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken)
    {
        await publisher.PublishAsync(new DailyUpdateRunResultPlaceholder(context.DailyUpdateRunId, context.Request.EvaluationBarDate), cancellationToken);
        return new DailyUpdateStepResult(1, 0, "The completed core-analysis state was published to presentation.");
    }
}

public sealed record DailyUpdateRunResultPlaceholder(int DailyUpdateRunId, DateOnly EvaluationBarDate);

public interface IAiCandidateQueueEnqueuer
{
    /// <summary>Enqueues eligible candidates only; it must not execute or await AI analysis.</summary>
    Task<int> EnqueueAsync(int dailyUpdateRunId, DateOnly evaluationBarDate, CancellationToken cancellationToken);
}

/// <summary>Queue insertion is decoupled from core analysis. The concrete durable queue is introduced in Phase 9.</summary>
public sealed class AiQueueDailyUpdateStage(IAiCandidateQueueEnqueuer enqueuer) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.EnqueueAiChecks;
    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken)
    {
        var queued = await enqueuer.EnqueueAsync(context.DailyUpdateRunId, context.Request.EvaluationBarDate, cancellationToken);
        return new DailyUpdateStepResult(queued, 0, queued == 0 ? "No AI check was automatically queued." : "AI checks were queued; their completion is independent of this daily update.");
    }
}

public interface ICandidateResultCounter
{
    Task<int> CountMatchedAsync(int scanRunId, string direction, CancellationToken cancellationToken);
}

/// <summary>The scan atomically freezes PIT inputs, calculates indicators, and persists both Long and Short candidate records.</summary>
public sealed class TechnicalAnalysisDailyUpdateStage(AllInstrumentScanService scanService, DailyUpdateTechnicalState state, TechnicalStrategyParameters parameters) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.RunTechnicalAnalysis;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken)
    {
        var progress = new Progress<TechnicalScanProgress>(item => context.ReportStageProgress(
            $"ステップ 5/11: テクニカル分析中（{item.Completed:n0}/{item.Total:n0}銘柄、候補 {item.CandidateCount:n0}件、失敗 {item.FailedCount:n0}件）。中止できます。",
            item.Completed,
            item.Total));
        var result = await scanService.RunAsync(
            new TechnicalScanRequest(context.Request.EvaluationBarDate, context.AnalyzedAtUtc, context.DailyUpdateRunId, context.Request.UniverseDefinitionHash, parameters),
            progress,
            cancellationToken);
        state.ScanResult = result;
        return new DailyUpdateStepResult(result.SucceededCount, result.FailedCount, $"PIT inputs, indicators, and candidate records were persisted in scan run {result.ScanRunId}.");
    }
}

public sealed class CandidateExtractionDailyUpdateStage(string direction, DailyUpdateTechnicalState state, ICandidateResultCounter counter) : IDailyUpdateStage
{
    public DailyUpdateStep Step => direction == "Long" ? DailyUpdateStep.ExtractLongCandidates : direction == "Short" ? DailyUpdateStep.ExtractShortCandidates : throw new ArgumentOutOfRangeException(nameof(direction));

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken)
    {
        if (state.ScanResult is null) return new DailyUpdateStepResult(0, 1, "Technical analysis did not produce a scan run.");
        var count = await counter.CountMatchedAsync(state.ScanResult.ScanRunId, direction, cancellationToken);
        return new DailyUpdateStepResult(count, 0, $"{direction} entry candidates were extracted from the persisted scan run.");
    }
}
