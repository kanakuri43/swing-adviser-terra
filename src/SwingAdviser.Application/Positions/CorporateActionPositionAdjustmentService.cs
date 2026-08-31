using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Application.Positions;

public sealed record CorporateActionLotState(
    MarginLot Lot,
    TradeExecution OpeningExecution,
    IReadOnlyCollection<PositionCorporateActionAdjustment> ExistingAdjustments,
    IReadOnlyCollection<CorporateAction> CorporateActions,
    decimal? EntryBasisPrice,
    decimal? AtrBasis,
    decimal? StopPrice,
    decimal? TakeProfitPrice);

public sealed record CorporateActionAdjustmentRunResult(int AppliedCount, int ReconciliationRequiredCount);

public interface ICorporateActionPositionAdjustmentStore
{
    Task<IReadOnlyList<CorporateActionLotState>> GetOpenLotStatesAsync(DateOnly throughDate, DateTime availableThroughUtc, CancellationToken cancellationToken);

    void Add(PositionCorporateActionAdjustment adjustment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Records the current-unit effect of corporate actions without ever changing original executions or creating trades.
/// Unsupported or incomplete actions fail closed into reconciliation-required history.
/// </summary>
public sealed class CorporateActionPositionAdjustmentService(ICorporateActionPositionAdjustmentStore store)
{
    private static readonly TimeSpan JapanStandardTimeOffset = TimeSpan.FromHours(9);

    public async Task<CorporateActionAdjustmentRunResult> ApplyAsync(DateOnly throughDate, DateTime appliedAtUtc, CancellationToken cancellationToken = default)
    {
        if (throughDate == default) throw new ArgumentOutOfRangeException(nameof(throughDate));
        if (appliedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("The application timestamp must be UTC.", nameof(appliedAtUtc));

        var applied = 0;
        var required = 0;
        var states = await store.GetOpenLotStatesAsync(throughDate, appliedAtUtc, cancellationToken);
        foreach (var state in states)
        {
            var openingDate = DateOnly.FromDateTime(state.OpeningExecution.ExecutedAt.ToOffset(JapanStandardTimeOffset).Date);
            var existingIds = state.ExistingAdjustments.Select(adjustment => adjustment.CorporateActionId).ToHashSet();
            var hasUnresolvedPriorAction = state.ExistingAdjustments.Any(adjustment => adjustment.ReconciliationStatus == "ReconciliationRequired");
            var basis = CurrentBasis(state);
            foreach (var action in state.CorporateActions.OrderBy(action => action.EffectiveDate).ThenBy(action => action.CorporateActionId))
            {
                if (action.EffectiveDate < openingDate || existingIds.Contains(action.CorporateActionId)) continue;
                var adjustment = CreateAdjustment(state.Lot, action, basis, hasUnresolvedPriorAction, appliedAtUtc);
                store.Add(adjustment);
                existingIds.Add(action.CorporateActionId);
                if (adjustment.ReconciliationStatus == "Applied")
                {
                    applied++;
                    // This is the derived current-unit value only; the original execution and opened quantity remain immutable.
                    state.Lot.CurrentQuantity = adjustment.QuantityAfter ?? state.Lot.CurrentQuantity;
                    basis = new CurrentUnitBasis(
                        adjustment.QuantityAfter ?? basis.Quantity,
                        adjustment.CostBasisAfter ?? basis.CostBasis,
                        adjustment.AtrBasisAfter ?? basis.AtrBasis,
                        adjustment.StopPriceAfter ?? basis.StopPrice,
                        adjustment.TakeProfitPriceAfter ?? basis.TakeProfitPrice);
                }
                else
                {
                    required++;
                    hasUnresolvedPriorAction = true;
                }
            }
        }
        if (applied > 0 || required > 0) await store.SaveChangesAsync(cancellationToken);
        return new CorporateActionAdjustmentRunResult(applied, required);
    }

    private static PositionCorporateActionAdjustment CreateAdjustment(MarginLot lot, CorporateAction action, CurrentUnitBasis basis, bool hasUnresolvedPriorAction, DateTime appliedAtUtc)
    {
        if (hasUnresolvedPriorAction || action.Status != "Active") return Required(lot, action, appliedAtUtc);
        if (action.ActionType == "CashDividend")
        {
            return new PositionCorporateActionAdjustment
            {
                MarginLot = lot,
                CorporateAction = action,
                QuantityBefore = basis.Quantity,
                QuantityAfter = basis.Quantity,
                CostBasisBefore = basis.CostBasis,
                CostBasisAfter = basis.CostBasis,
                AtrBasisBefore = basis.AtrBasis,
                AtrBasisAfter = basis.AtrBasis,
                StopPriceBefore = basis.StopPrice,
                StopPriceAfter = basis.StopPrice,
                TakeProfitPriceBefore = basis.TakeProfitPrice,
                TakeProfitPriceAfter = basis.TakeProfitPrice,
                ReconciliationStatus = "Applied",
                AppliedAtUtc = appliedAtUtc,
            };
        }
        if (action.ActionType is not ("Split" or "Consolidation") || action.SplitRatioNumerator is not > 0 || action.SplitRatioDenominator is not > 0 || !basis.IsComplete)
            return Required(lot, action, appliedAtUtc);

        var ratio = (decimal)action.SplitRatioNumerator.Value / action.SplitRatioDenominator.Value;
        return new PositionCorporateActionAdjustment
        {
            MarginLot = lot,
            CorporateAction = action,
            Ratio = ratio,
            QuantityBefore = basis.Quantity,
            QuantityAfter = checked(basis.Quantity!.Value * ratio),
            CostBasisBefore = basis.CostBasis,
            CostBasisAfter = checked(basis.CostBasis!.Value / ratio),
            AtrBasisBefore = basis.AtrBasis,
            AtrBasisAfter = checked(basis.AtrBasis!.Value / ratio),
            StopPriceBefore = basis.StopPrice,
            StopPriceAfter = checked(basis.StopPrice!.Value / ratio),
            TakeProfitPriceBefore = basis.TakeProfitPrice,
            TakeProfitPriceAfter = checked(basis.TakeProfitPrice!.Value / ratio),
            ReconciliationStatus = "Applied",
            AppliedAtUtc = appliedAtUtc,
        };
    }

    private static PositionCorporateActionAdjustment Required(MarginLot lot, CorporateAction action, DateTime appliedAtUtc) => new()
    {
        MarginLot = lot,
        CorporateAction = action,
        ReconciliationStatus = "ReconciliationRequired",
        AppliedAtUtc = appliedAtUtc,
    };

    private static CurrentUnitBasis CurrentBasis(CorporateActionLotState state)
    {
        var latest = state.ExistingAdjustments
            .Where(adjustment => adjustment.ReconciliationStatus == "Applied")
            .OrderBy(adjustment => adjustment.CorporateAction.EffectiveDate)
            .ThenBy(adjustment => adjustment.CorporateActionId)
            .LastOrDefault();
        return latest is null
            ? new CurrentUnitBasis(state.Lot.CurrentQuantity, state.EntryBasisPrice, state.AtrBasis, state.StopPrice, state.TakeProfitPrice)
            : new CurrentUnitBasis(latest.QuantityAfter, latest.CostBasisAfter, latest.AtrBasisAfter, latest.StopPriceAfter, latest.TakeProfitPriceAfter);
    }

    private sealed record CurrentUnitBasis(decimal? Quantity, decimal? CostBasis, decimal? AtrBasis, decimal? StopPrice, decimal? TakeProfitPrice)
    {
        public bool IsComplete => Quantity is > 0 && CostBasis is > 0 && AtrBasis is > 0 && StopPrice is > 0 && TakeProfitPrice is > 0;
    }
}
