using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

public sealed record PartialExitBreakevenRequest(
    Position Position,
    MarginLot MarginLot,
    TradeExecution CloseExecution,
    TradeExecutionLotAllocation Allocation,
    decimal QuantityBeforeAllocation,
    IReadOnlyCollection<RiskPlan> RiskPlans,
    DateTime RecordedAtUtc);

/// <summary>Creates an append-only breakeven stop revision after an explicitly allocated partial close.</summary>
public sealed class PartialExitBreakevenPlanFactory
{
    public RiskPlan? TryCreate(PartialExitBreakevenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var position = request.Position;
        var lot = request.MarginLot;
        var close = request.CloseExecution;
        var allocation = request.Allocation;
        if (position.Status != RiskVocabulary.Open || (position.Side != RiskVocabulary.Long && position.Side != RiskVocabulary.Short) ||
            lot.Status != RiskVocabulary.Open || lot.PositionId != position.PositionId || lot.CurrentQuantity <= 0 ||
            close.PositionId != position.PositionId || close.ExecutionRole != "Close" || close.Status != RiskVocabulary.Effective ||
            allocation.MarginLotId != lot.MarginLotId || allocation.TradeExecutionId != close.TradeExecutionId || allocation.Status != RiskVocabulary.Effective ||
            allocation.Quantity <= 0 || request.QuantityBeforeAllocation <= 0 || allocation.Quantity >= request.QuantityBeforeAllocation ||
            request.RecordedAtUtc < close.EnteredAtUtc || request.RecordedAtUtc < allocation.RecordedAtUtc)
            return null;
        var selection = new LotRiskEvaluator().SelectActivePlanLeaf(lot.MarginLotId, request.RiskPlans, close.ExecutedAt.UtcDateTime);
        if (!selection.IsSuccess) return null;
        var oldPlan = selection.Plan!;
        if (oldPlan.RiskBasis is null || oldPlan.RiskBasis.EntryBasisPrice <= 0 || oldPlan.RiskBasis.MarginLotId != lot.MarginLotId || oldPlan.RiskBasisId != oldPlan.RiskBasis.RiskBasisId)
            return null;
        var stop = position.Side == RiskVocabulary.Long
            ? decimal.Max(oldPlan.StopPrice, oldPlan.RiskBasis.EntryBasisPrice)
            : decimal.Min(oldPlan.StopPrice, oldPlan.RiskBasis.EntryBasisPrice);
        if (stop <= 0) return null;
        return new RiskPlan
        {
            MarginLotId = lot.MarginLotId,
            Revision = oldPlan.Revision + 1,
            PlanKind = RiskVocabulary.PartialExitBreakeven,
            RiskBasisId = oldPlan.RiskBasisId,
            RiskBasis = oldPlan.RiskBasis,
            StopPrice = stop,
            TakeProfitPrice = oldPlan.TakeProfitPrice,
            PartialTakeProfitFraction = oldPlan.PartialTakeProfitFraction,
            TriggerTradeExecutionId = close.TradeExecutionId,
            TriggerTradeExecution = close,
            TriggerAllocationId = allocation.AllocationId,
            TriggerAllocation = allocation,
            EffectiveAtUtc = close.ExecutedAt.UtcDateTime,
            RecordedAtUtc = request.RecordedAtUtc,
            SupersedesRevisionId = oldPlan.RiskPlanId,
            SupersedesRevision = oldPlan,
            Status = RiskVocabulary.Effective,
        };
    }
}
