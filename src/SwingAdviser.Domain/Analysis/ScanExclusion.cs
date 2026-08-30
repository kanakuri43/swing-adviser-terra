using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>Reason an instrument was excluded from a scan.</summary>
public sealed class ScanExclusion
{
    public int ScanExclusionId { get; set; }
    public int ScanRunId { get; set; }
    public int InstrumentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? HistoryAvailableCount { get; set; }
    public int? HistoryRequiredCount { get; set; }

    public ScanRun ScanRun { get; set; } = null!;
    public Instrument Instrument { get; set; } = null!;
}
