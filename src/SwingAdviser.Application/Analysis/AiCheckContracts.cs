namespace SwingAdviser.Application.Analysis;

/// <summary>Configuration supplied by the host; no executable path, model, or timeout is embedded in queue logic.</summary>
public sealed record AiCheckOptions(
    string ExecutablePath,
    string? WorkingDirectory,
    string? Model,
    TimeSpan Timeout,
    IReadOnlyList<string> AdditionalArguments,
    int MaximumConcurrency = 2,
    bool EnableAutomaticChecks = false,
    int AutomaticCandidatesPerDirection = 3)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath)) throw new ArgumentException("An AI CLI executable path is required.", nameof(ExecutablePath));
        if (Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Timeout));
        if (MaximumConcurrency is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(MaximumConcurrency), "The initial release allows at most two concurrent checks.");
        if (AutomaticCandidatesPerDirection < 1) throw new ArgumentOutOfRangeException(nameof(AutomaticCandidatesPerDirection));
        if (AdditionalArguments.Any(argument => string.IsNullOrWhiteSpace(argument))) throw new ArgumentException("Additional arguments must be individual non-empty values.", nameof(AdditionalArguments));
    }
}

public sealed record AiCliRequest(string ExecutablePath, string? WorkingDirectory, string? Model, IReadOnlyList<string> AdditionalArguments, string Prompt, TimeSpan Timeout);
public sealed record AiCliResponse(string Stdout, string Stderr, int? ExitCode, AiCliCompletion Completion);
public enum AiCliCompletion { Completed, TimedOut, Cancelled, FailedToStart }
public interface IAiCliExecutor { Task<AiCliResponse> ExecuteAsync(AiCliRequest request, CancellationToken cancellationToken); }

public sealed record AiCandidateOverview(int CandidateResultId, string Code, string Name, string Direction, DateOnly EvaluationBarDate, int? Score, string? Confidence, string AiStatus, int? LatestAttemptId, bool IsStale, string? Verdict, string? VerdictAlignment, string? Summary);
public sealed record AiQueueOverview(int QueuedCount, int RunningCount, int SucceededCount, int FailedCount, int TimedOutCount, int InsufficientInformationCount, int CancelledCount, IReadOnlyList<AiCandidateOverview> Candidates);
public interface IAiCheckOverviewReader { Task<AiQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default); }
public sealed record AiQueueActionResult(int QueuedCount, IReadOnlyList<int> RejectedCandidateResultIds);
public interface IAiCheckQueueController
{
    Task<AiQueueActionResult> EnqueueUserAsync(IEnumerable<int> candidateResultIds, CancellationToken cancellationToken = default);
    Task<bool> CancelQueuedAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<int?> RetryAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<int> ProcessAvailableAsync(CancellationToken cancellationToken = default);
}
