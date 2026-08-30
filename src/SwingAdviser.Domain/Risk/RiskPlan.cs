using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

/// <summary>Append-only stop-loss and take-profit plan revision for a margin lot.</summary>
public sealed class RiskPlan
{
    public int RiskPlanId { get; set; }
    public int MarginLotId { get; set; }
    public int Revision { get; set; }
    public string PlanKind { get; set; } = string.Empty;
    public int RiskBasisId { get; set; }
    public decimal StopPrice { get; set; }
    public decimal TakeProfitPrice { get; set; }
    public decimal PartialTakeProfitFraction { get; set; }
    public int? TriggerTradeExecutionId { get; set; }
    public int? TriggerAllocationId { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public int? SupersedesRevisionId { get; set; }
    public string Status { get; set; } = string.Empty;

    public MarginLot MarginLot { get; set; } = null!;
    public RiskBasisSnapshot RiskBasis { get; set; } = null!;
    public TradeExecution? TriggerTradeExecution { get; set; }
    public TradeExecutionLotAllocation? TriggerAllocation { get; set; }
    public RiskPlan? SupersedesRevision { get; set; }
}
