namespace SwingAdviser.Domain.Analysis;

/// <summary>One all-instrument technical-analysis scan.</summary>
public sealed class ScanRun
{
    public int ScanRunId { get; set; }
    public int? DailyUpdateRunId { get; set; }
    public string RunType { get; set; } = string.Empty;
    public string UniverseDefinitionHash { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalInstruments { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }

    public DailyUpdateRun? DailyUpdateRun { get; set; }
    public ICollection<IndicatorResult> IndicatorResults { get; } = new List<IndicatorResult>();
    public ICollection<ScanExclusion> ScanExclusions { get; } = new List<ScanExclusion>();
}
