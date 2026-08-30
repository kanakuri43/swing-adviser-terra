namespace SwingAdviser.Domain.Positions;

/// <summary>A position lot with independently tracked contract terms and current adjusted quantity.</summary>
public sealed class MarginLot
{
    public int MarginLotId { get; set; }
    public int PositionId { get; set; }
    public int OpeningTradeExecutionId { get; set; }
    public int OpenedQuantity { get; set; }
    public decimal CurrentQuantity { get; set; }
    public string Status { get; set; } = string.Empty;

    public Position Position { get; set; } = null!;
    public TradeExecution OpeningTradeExecution { get; set; } = null!;
    public ICollection<MarginLotContractTermRevision> ContractTermRevisions { get; } = new List<MarginLotContractTermRevision>();
    public ICollection<TradeExecutionLotAllocation> TradeExecutionLotAllocations { get; } = new List<TradeExecutionLotAllocation>();
    public ICollection<PositionCorporateActionAdjustment> CorporateActionAdjustments { get; } = new List<PositionCorporateActionAdjustment>();
}
