using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Positions;

/// <summary>A user-managed position; it is distinct from a candidate and never places an order.</summary>
public sealed class Position
{
    public int PositionId { get; set; }
    public int InstrumentId { get; set; }
    public string Side { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AppliedStrategyKey { get; set; } = string.Empty;
    public string AppliedStrategyVersion { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public int? SourceCandidateResultId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public Instrument Instrument { get; set; } = null!;
    public CandidateResult? SourceCandidateResult { get; set; }
    public ICollection<TradeExecution> TradeExecutions { get; } = new List<TradeExecution>();
    public ICollection<MarginLot> MarginLots { get; } = new List<MarginLot>();
}
