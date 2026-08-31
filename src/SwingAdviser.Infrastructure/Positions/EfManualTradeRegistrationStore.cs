using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Positions;

/// <summary>EF Core repository for the manual-only position workflow.</summary>
public sealed class EfManualTradeRegistrationStore(SwingAdviserDbContext context) : IManualTradeRegistrationStore
{
    public Task<Position?> FindPositionAsync(int positionId, CancellationToken cancellationToken) =>
        context.Positions.SingleOrDefaultAsync(position => position.PositionId == positionId, cancellationToken);

    public async Task<IReadOnlyList<MarginLot>> GetOpenLotsAsync(int positionId, CancellationToken cancellationToken) =>
        await context.MarginLots
            .Where(lot => lot.PositionId == positionId && lot.Status == "Open")
            .OrderBy(lot => lot.MarginLotId)
            .ToListAsync(cancellationToken);

    public void AddOpening(Position position, TradeExecution execution, MarginLot lot)
    {
        if (position.PositionId == 0) context.Positions.Add(position);
        context.TradeExecutions.Add(execution);
        context.MarginLots.Add(lot);
    }

    public void AddClose(TradeExecution execution, IReadOnlyCollection<TradeExecutionLotAllocation> allocations)
    {
        context.TradeExecutions.Add(execution);
        context.TradeExecutionLotAllocations.AddRange(allocations);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
