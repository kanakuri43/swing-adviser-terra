using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Domain.Positions;

/// <summary>Immutable, user-confirmed trade execution record. Corrections append another revision.</summary>
public sealed class TradeExecution
{
    public int TradeExecutionId { get; set; }
    public int PositionId { get; set; }
    public string ExecutionRole { get; set; } = string.Empty;
    public DateTimeOffset ExecutedAt { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime EnteredAtUtc { get; set; }
    public int? PrefilledFromCandidateResultId { get; set; }
    public string? Notes { get; set; }
    public int Revision { get; set; }
    public int? SupersedesId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Position Position { get; set; } = null!;
    public CandidateResult? PrefilledFromCandidateResult { get; set; }
    public TradeExecution? Supersedes { get; set; }
    public ICollection<TradeExecutionLotAllocation> LotAllocations { get; } = new List<TradeExecutionLotAllocation>();
}
