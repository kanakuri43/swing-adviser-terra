using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>Immutable technical-indicator result for one instrument and evaluation date.</summary>
public sealed class IndicatorResult
{
    public int IndicatorResultId { get; set; }
    public int ScanRunId { get; set; }
    public int InstrumentId { get; set; }
    public DateOnly EvaluationBarDate { get; set; }
    public DateTime AnalyzedAtUtc { get; set; }
    public int ManifestId { get; set; }
    public int StrategyParameterSnapshotId { get; set; }
    public string DataStatus { get; set; } = string.Empty;
    public int HistoryAvailableCount { get; set; }
    public int HistoryRequiredCount { get; set; }
    public decimal? MacdLine { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? MacdHistogram { get; set; }
    public decimal? Ema20 { get; set; }
    public decimal? Ema50 { get; set; }
    public decimal? Ema200 { get; set; }
    public decimal? Atr14 { get; set; }
    public decimal? VolumeRatio { get; set; }
    public decimal? VolumeReferenceAverage { get; set; }
    public string VolumeRatioStatus { get; set; } = string.Empty;
    public string RawValuesJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public ScanRun ScanRun { get; set; } = null!;
    public Instrument Instrument { get; set; } = null!;
    public AnalysisInputManifest Manifest { get; set; } = null!;
    public StrategyParameterSnapshot StrategyParameterSnapshot { get; set; } = null!;
    public ICollection<CandidateResult> CandidateResults { get; } = new List<CandidateResult>();
}
