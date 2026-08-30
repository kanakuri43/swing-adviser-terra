using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>One Long or Short entry-candidate evaluation; it never represents an order or trade.</summary>
public sealed class CandidateResult
{
    public int CandidateResultId { get; set; }
    public int IndicatorResultId { get; set; }
    public int InstrumentId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string SignalPurpose { get; set; } = string.Empty;
    public bool Matched { get; set; }
    public int? Score { get; set; }
    public string? ConfidenceLabel { get; set; }
    public string CandidateScoringEngineVersion { get; set; } = string.Empty;
    public string ScoreComponentsJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public IndicatorResult IndicatorResult { get; set; } = null!;
    public Instrument Instrument { get; set; } = null!;
}
