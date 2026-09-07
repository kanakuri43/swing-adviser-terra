using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>
/// Durable state for one externally acquired input in a daily evaluation. It is separate from
/// the append-only attempt log so a resumed update can distinguish success from interruption.
/// </summary>
public sealed class DailyUpdateFetchCheckpoint
{
    public int DailyUpdateFetchCheckpointId { get; set; }
    public int DailyUpdateRunId { get; set; }
    public DateOnly EvaluationBarDate { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string TargetKey { get; set; } = string.Empty;
    public int? InstrumentId { get; set; }
    public DateOnly? RequestedRangeStartDate { get; set; }
    public DateOnly? CoveredThroughDate { get; set; }
    public string? DataRevisionFingerprint { get; set; }
    public string Status { get; set; } = "Running";
    public string? InvalidationReason { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }
    public int? ReusedFromCheckpointId { get; set; }

    public DailyUpdateRun DailyUpdateRun { get; set; } = null!;
    public Instrument? Instrument { get; set; }
    public DailyUpdateFetchCheckpoint? ReusedFromCheckpoint { get; set; }
    public ICollection<ExternalFetchResult> ExternalFetchResults { get; } = new List<ExternalFetchResult>();
}
