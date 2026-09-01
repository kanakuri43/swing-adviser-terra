using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Application.DailyUpdates;

public enum DailyUpdateStep
{
    RefreshMarketData = 1,
    VerifyDataAvailability = 2,
    ApplyCorporateActionAdjustments = 3,
    BuildPointInTimeSeries = 4,
    RunTechnicalAnalysis = 5,
    ExtractLongCandidates = 6,
    ExtractShortCandidates = 7,
    ReevaluateHoldings = 8,
    PersistAnalysisResults = 9,
    PublishResults = 10,
    EnqueueAiChecks = 11,
}

public sealed record DailyUpdateRequest(DateOnly EvaluationBarDate, DateTime RequestedAtUtc, string UniverseDefinitionHash);

public sealed class DailyUpdateContext
{
    private Action<string, int?, int?>? _stageProgressReporter;

    public DailyUpdateContext(int dailyUpdateRunId, DailyUpdateRequest request, DateTime analyzedAtUtc)
    {
        DailyUpdateRunId = dailyUpdateRunId;
        Request = request;
        AnalyzedAtUtc = analyzedAtUtc;
    }

    public int DailyUpdateRunId { get; }
    public DailyUpdateRequest Request { get; }
    /// <summary>Frozen after the external-refresh step, so the following PIT reads can see only data available by that time.</summary>
    public DateTime AnalyzedAtUtc { get; internal set; }

    /// <summary>Reports work inside the currently executing stage without changing the durable step outcome.</summary>
    public void ReportStageProgress(string detail, int? completedWorkItems = null, int? totalWorkItems = null)
        => _stageProgressReporter?.Invoke(detail, completedWorkItems, totalWorkItems);

    internal void SetStageProgressReporter(Action<string, int?, int?>? reporter)
        => _stageProgressReporter = reporter;
}

/// <summary>Outcome for one deterministic orchestration step. Unit counts are surfaced without hiding partial errors.</summary>
public sealed record DailyUpdateStepResult(int SucceededCount = 0, int FailedCount = 0, string? Detail = null)
{
    public bool IsSuccess => FailedCount == 0;
}

public sealed record DailyUpdateStepProgress(
    int DailyUpdateRunId,
    DailyUpdateStep Step,
    int CompletedSteps,
    int TotalSteps,
    int SucceededCount,
    int FailedCount,
    string Status,
    string? Detail,
    int? CompletedWorkItems = null,
    int? TotalWorkItems = null);

public sealed record DailyUpdateRunResult(
    int DailyUpdateRunId,
    string Status,
    IReadOnlyDictionary<DailyUpdateStep, DailyUpdateStepResult> Steps)
{
    public int SucceededCount => Steps.Values.Sum(step => step.SucceededCount);
    public int FailedCount => Steps.Values.Sum(step => step.FailedCount);
}

public interface IDailyUpdateStage
{
    DailyUpdateStep Step { get; }
    Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken);
}

public interface IDailyUpdateRunStore
{
    Task<DailyUpdateRun> StartAsync(DateTime startedAtUtc, CancellationToken cancellationToken);
    Task UpdateSummaryAsync(int dailyUpdateRunId, string stepSummaryJson, CancellationToken cancellationToken);
    Task CompleteAsync(int dailyUpdateRunId, string status, DateTime completedAtUtc, string stepSummaryJson, CancellationToken cancellationToken);
}
