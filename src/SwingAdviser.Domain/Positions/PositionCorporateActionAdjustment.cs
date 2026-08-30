using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Positions;

/// <summary>Separate adjustment history that preserves the original execution record.</summary>
public sealed class PositionCorporateActionAdjustment
{
    public int AdjustmentId { get; set; }
    public int MarginLotId { get; set; }
    public int CorporateActionId { get; set; }
    public decimal? Ratio { get; set; }
    public decimal? QuantityBefore { get; set; }
    public decimal? QuantityAfter { get; set; }
    public decimal? CostBasisBefore { get; set; }
    public decimal? CostBasisAfter { get; set; }
    public decimal? AtrBasisBefore { get; set; }
    public decimal? AtrBasisAfter { get; set; }
    public decimal? StopPriceBefore { get; set; }
    public decimal? StopPriceAfter { get; set; }
    public decimal? TakeProfitPriceBefore { get; set; }
    public decimal? TakeProfitPriceAfter { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; }

    public MarginLot MarginLot { get; set; } = null!;
    public CorporateAction CorporateAction { get; set; } = null!;
}
