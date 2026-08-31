using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Positions;

public sealed class EfCorporateActionPositionAdjustmentStore(SwingAdviserDbContext context) : ICorporateActionPositionAdjustmentStore
{
    public async Task<IReadOnlyList<CorporateActionLotState>> GetOpenLotStatesAsync(DateOnly throughDate, DateTime availableThroughUtc, CancellationToken cancellationToken)
    {
        var lots = await context.MarginLots
            .Where(lot => lot.Status == "Open")
            .Include(lot => lot.OpeningTradeExecution)
            .Include(lot => lot.CorporateActionAdjustments)
                .ThenInclude(adjustment => adjustment.CorporateAction)
            .ToListAsync(cancellationToken);
        var positionIds = lots.Select(lot => lot.PositionId).Distinct().ToArray();
        var positions = await context.Positions.Where(position => positionIds.Contains(position.PositionId)).ToDictionaryAsync(position => position.PositionId, cancellationToken);
        var actualInstrumentIds = positions.Values.Select(position => position.InstrumentId).Distinct().ToArray();
        var actions = await context.CorporateActions
            .Where(action => actualInstrumentIds.Contains(action.InstrumentId) && action.EffectiveDate <= throughDate && action.AvailableAtUtc <= availableThroughUtc && action.Status != "Superseded" && action.Status != "Voided")
            .ToListAsync(cancellationToken);
        var bases = await context.RiskBasisSnapshots.Where(basis => lots.Select(lot => lot.MarginLotId).Contains(basis.MarginLotId)).ToDictionaryAsync(basis => basis.MarginLotId, cancellationToken);
        var plans = await context.RiskPlans.Where(plan => lots.Select(lot => lot.MarginLotId).Contains(plan.MarginLotId) && plan.Status == "Effective")
            .ToListAsync(cancellationToken);

        return lots.Select(lot =>
        {
            var position = positions[lot.PositionId];
            var activePlan = plans.Where(plan => plan.MarginLotId == lot.MarginLotId).OrderByDescending(plan => plan.Revision).FirstOrDefault();
            bases.TryGetValue(lot.MarginLotId, out var basis);
            return new CorporateActionLotState(
                lot,
                lot.OpeningTradeExecution,
                lot.CorporateActionAdjustments.ToArray(),
                actions.Where(action => action.InstrumentId == position.InstrumentId).ToArray(),
                basis?.EntryBasisPrice,
                basis?.AtrBasis,
                activePlan?.StopPrice,
                activePlan?.TakeProfitPrice);
        }).ToArray();
    }

    public void Add(PositionCorporateActionAdjustment adjustment) => context.PositionCorporateActionAdjustments.Add(adjustment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
