using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>Specific daily-bar revision included in an analysis input manifest.</summary>
public sealed class AnalysisInputManifestBar
{
    public int ManifestId { get; set; }
    public DateOnly TradingDate { get; set; }
    public int DailyBarId { get; set; }

    public AnalysisInputManifest Manifest { get; set; } = null!;
    public DailyBar DailyBar { get; set; } = null!;
}
