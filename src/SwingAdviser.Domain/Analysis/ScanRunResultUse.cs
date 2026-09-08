namespace SwingAdviser.Domain.Analysis;

/// <summary>Records the immutable indicator result used by one scan, whether computed then or safely reused.</summary>
public sealed class ScanRunResultUse
{
    public int ScanRunResultUseId { get; set; }
    public int ScanRunId { get; set; }
    public int IndicatorResultId { get; set; }
    public string UseKind { get; set; } = string.Empty;
    public DateTime UsedAtUtc { get; set; }

    public ScanRun ScanRun { get; set; } = null!;
    public IndicatorResult IndicatorResult { get; set; } = null!;
}
