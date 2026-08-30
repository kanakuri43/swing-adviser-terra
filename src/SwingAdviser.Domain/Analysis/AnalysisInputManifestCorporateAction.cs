using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>Specific corporate-action revision applied in an analysis input manifest.</summary>
public sealed class AnalysisInputManifestCorporateAction
{
    public int ManifestId { get; set; }
    public int CorporateActionId { get; set; }

    public AnalysisInputManifest Manifest { get; set; } = null!;
    public CorporateAction CorporateAction { get; set; } = null!;
}
