namespace SwingAdviser.Domain.Positions;

/// <summary>User-confirmed allocation of a close execution to a specific lot; no FIFO inference is made.</summary>
public sealed class TradeExecutionLotAllocation
{
    public int AllocationId { get; set; }
    public int TradeExecutionId { get; set; }
    public int MarginLotId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesId { get; set; }
    public string Status { get; set; } = string.Empty;

    public TradeExecution TradeExecution { get; set; } = null!;
    public MarginLot MarginLot { get; set; } = null!;
    public TradeExecutionLotAllocation? Supersedes { get; set; }
}
