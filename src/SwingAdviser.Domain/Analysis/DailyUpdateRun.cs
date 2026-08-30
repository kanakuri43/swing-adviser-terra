namespace SwingAdviser.Domain.Analysis;

/// <summary>One end-to-end daily-update execution record.</summary>
public sealed class DailyUpdateRun
{
    public int DailyUpdateRunId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StepSummaryJson { get; set; }

    public ICollection<ExternalFetchResult> ExternalFetchResults { get; } = new List<ExternalFetchResult>();
    public ICollection<ScanRun> ScanRuns { get; } = new List<ScanRun>();
}
