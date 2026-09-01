namespace SwingAdviser.Application.DailyUpdates;

/// <summary>Host-facing entry point for a complete daily update. The host supplies operational settings; the UI supplies only progress and cancellation.</summary>
public interface IDailyUpdateExecutionService
{
    Task<DailyUpdateRunResult> RunAsync(IProgress<DailyUpdateStepProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Read model for the latest durable daily-update run. It is intentionally separate from the in-memory progress callback.</summary>
public sealed record DailyUpdateOverview(
    int? DailyUpdateRunId,
    string Status,
    int CompletedSteps,
    int TotalSteps,
    int SucceededCount,
    int FailedCount,
    string Detail,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);

public interface IDailyUpdateOverviewReader
{
    Task<DailyUpdateOverview> GetLatestAsync(CancellationToken cancellationToken = default);
}
