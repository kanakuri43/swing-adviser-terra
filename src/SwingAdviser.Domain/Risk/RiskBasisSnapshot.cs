using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

/// <summary>Immutable entry-price and ATR basis for a single margin lot.</summary>
public sealed class RiskBasisSnapshot
{
    public int RiskBasisId { get; set; }
    public int MarginLotId { get; set; }
    public decimal EntryBasisPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AtrBasis { get; set; }
    public DateOnly AtrReferenceBarDate { get; set; }
    public int AtrPeriod { get; set; }
    public string AtrAlgorithmVersion { get; set; } = string.Empty;
    public string PriceUnitBasisSha256 { get; set; } = string.Empty;
    public int? SourceCandidateResultId { get; set; }
    public int? SourceIndicatorResultId { get; set; }
    public int? ManualOpenAnalysisInputManifestId { get; set; }
    public int StrategyParameterSnapshotId { get; set; }
    public string CorporateActionSetHash { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public MarginLot MarginLot { get; set; } = null!;
    public CandidateResult? SourceCandidateResult { get; set; }
    public IndicatorResult? SourceIndicatorResult { get; set; }
    public AnalysisInputManifest? ManualOpenAnalysisInputManifest { get; set; }
    public StrategyParameterSnapshot StrategyParameterSnapshot { get; set; } = null!;
}
