namespace SwingAdviser.Domain.Analysis;

/// <summary>Immutable, normalized strategy-parameter snapshot used by an analysis.</summary>
public sealed class StrategyParameterSnapshot
{
    public int StrategyParameterSnapshotId { get; set; }
    public string StrategyKey { get; set; } = string.Empty;
    public string StrategyVersion { get; set; } = string.Empty;
    public string IndicatorEngineVersion { get; set; } = string.Empty;
    public string CandidateScoringEngineVersion { get; set; } = string.Empty;
    public string NormalizedParametersJson { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<IndicatorResult> IndicatorResults { get; } = new List<IndicatorResult>();
}
