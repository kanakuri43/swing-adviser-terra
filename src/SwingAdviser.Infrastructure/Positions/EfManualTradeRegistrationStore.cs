using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
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

    public async Task<CandidateRiskPlanSource?> GetCandidateRiskPlanSourceAsync(int candidateResultId, CancellationToken cancellationToken)
    {
        var candidate = await context.CandidateResults
            .Include(item => item.IndicatorResult).ThenInclude(item => item.Manifest)
            .SingleOrDefaultAsync(item => item.CandidateResultId == candidateResultId, cancellationToken);
        if (candidate?.IndicatorResult is null || candidate.IndicatorResult.Manifest is null) return null;
        var indicator = candidate.IndicatorResult;
        return new CandidateRiskPlanSource(candidate.CandidateResultId, candidate.InstrumentId, candidate.Direction, candidate.SignalPurpose,
            candidate.Matched, indicator.IndicatorResultId, indicator.InstrumentId, indicator.DataStatus, indicator.Atr14, indicator.EvaluationBarDate, indicator.AnalyzedAtUtc,
            indicator.StrategyParameterSnapshotId, indicator.Manifest.CorporateActionSetHash);
    }

    public void AddOpening(Position position, TradeExecution execution, MarginLot lot, RiskBasisSnapshot? riskBasis = null, RiskPlan? riskPlan = null)
    {
        if (position.PositionId == 0) context.Positions.Add(position);
        context.TradeExecutions.Add(execution);
        context.MarginLots.Add(lot);
        if (riskBasis is not null) context.RiskBasisSnapshots.Add(riskBasis);
        if (riskPlan is not null) context.RiskPlans.Add(riskPlan);
    }

    public void AddClose(TradeExecution execution, IReadOnlyCollection<TradeExecutionLotAllocation> allocations)
    {
        context.TradeExecutions.Add(execution);
        context.TradeExecutionLotAllocations.AddRange(allocations);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
