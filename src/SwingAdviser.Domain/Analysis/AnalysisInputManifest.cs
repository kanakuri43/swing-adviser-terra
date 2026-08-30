using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>Frozen, point-in-time set of market-data revisions used for one analysis.</summary>
public sealed class AnalysisInputManifest
{
    public int ManifestId { get; set; }
    public int InstrumentId { get; set; }
    public DateOnly EvaluationBarDate { get; set; }
    public DateTime AnalyzedAtUtc { get; set; }
    public DateOnly FirstBarDate { get; set; }
    public DateOnly LastBarDate { get; set; }
    public int BarCount { get; set; }
    public string PriceRevisionSetHash { get; set; } = string.Empty;
    public string CorporateActionSetHash { get; set; } = string.Empty;
    public string ManifestHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public Instrument Instrument { get; set; } = null!;
    public ICollection<AnalysisInputManifestBar> Bars { get; } = new List<AnalysisInputManifestBar>();
    public ICollection<AnalysisInputManifestCorporateAction> CorporateActions { get; } = new List<AnalysisInputManifestCorporateAction>();
    public ICollection<IndicatorResult> IndicatorResults { get; } = new List<IndicatorResult>();
}
