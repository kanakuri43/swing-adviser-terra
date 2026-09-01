using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Positions;

public sealed class EfManualPositionOverviewReader(SwingAdviserDbContext context) : IManualPositionOverviewReader
{
    public async Task<IReadOnlyList<ManualPositionOverview>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await context.Positions.Where(position => position.Status == "Open")
            .Include(position => position.MarginLots).ThenInclude(lot => lot.OpeningTradeExecution).ToListAsync(cancellationToken);
        var revisions = await CurrentRevisionsAsync(positions.Select(position => position.InstrumentId), cancellationToken);
        var requiredLotIds = await context.PositionCorporateActionAdjustments
            .Where(adjustment => adjustment.ReconciliationStatus == "ReconciliationRequired")
            .Select(adjustment => adjustment.MarginLotId)
            .ToListAsync(cancellationToken);
        var appliedLotIds = await context.PositionCorporateActionAdjustments
            .Where(adjustment => adjustment.ReconciliationStatus == "Applied")
            .Select(adjustment => adjustment.MarginLotId)
            .ToListAsync(cancellationToken);
        var required = requiredLotIds.ToHashSet();
        var applied = appliedLotIds.ToHashSet();
        var positionIds = positions.Select(position => position.PositionId).ToArray();
        var lotIds = positions.SelectMany(position => position.MarginLots).Select(lot => lot.MarginLotId).ToArray();
        var evaluations = await context.PositionHoldingEvaluations.Where(item => positionIds.Contains(item.PositionId)).ToListAsync(cancellationToken);
        var plans = await context.RiskPlans.Where(item => lotIds.Contains(item.MarginLotId) && item.Status == "Effective").ToListAsync(cancellationToken);
        var terms = await context.MarginLotContractTermRevisions.Where(item => lotIds.Contains(item.MarginLotId) && item.Status != "Superseded" && item.Status != "Voided").ToListAsync(cancellationToken);
        var costs = await context.MarginCostLedgerEntries.Where(item => lotIds.Contains(item.MarginLotId)).ToListAsync(cancellationToken);
        var instrumentIds = positions.Select(position => position.InstrumentId).Distinct().ToArray();
        var bars = await context.DailyBars.Where(bar => instrumentIds.Contains(bar.InstrumentId) && (bar.Status == "Final" || bar.Status == "Corrected")).ToListAsync(cancellationToken);
        return positions.OrderBy(position => position.PositionId).Select(position =>
        {
            var identity = revisions.TryGetValue(position.InstrumentId, out var revision)
                ? revision
                : ("（銘柄コード未確認）", "（銘柄名未確認）");
            var lotIds = position.MarginLots.Select(lot => lot.MarginLotId).ToArray();
            var reconciliation = lotIds.Any(required.Contains) ? "企業アクション要照合" : lotIds.Any(applied.Contains) ? "企業アクション換算済み" : "企業アクション未発生/未確認";
            var evaluation = evaluations.Where(item => item.PositionId == position.PositionId)
                .OrderByDescending(item => item.EvaluationBarDate).ThenByDescending(item => item.EvaluatedAtUtc).FirstOrDefault();
            var activePlans = plans.Where(plan => lotIds.Contains(plan.MarginLotId)).GroupBy(plan => plan.MarginLotId)
                .Select(group => group.OrderByDescending(plan => plan.Revision).First()).ToArray();
            var activeTerms = terms.Where(term => lotIds.Contains(term.MarginLotId)).GroupBy(term => term.MarginLotId)
                .Select(group => group.OrderByDescending(term => term.Revision).First()).ToArray();
            var resolution = new MarginCostLedger().Resolve(costs.Where(cost => lotIds.Contains(cost.MarginLotId)));
            var bar = bars.Where(item => item.InstrumentId == position.InstrumentId)
                .GroupBy(item => item.TradingDate).Select(group => group.OrderByDescending(item => item.Revision).First())
                .OrderByDescending(item => item.TradingDate).FirstOrDefault();
            var referencePnl = bar is null || lotIds.Any(applied.Contains)
                ? null
                : position.MarginLots.Where(lot => lot.Status == "Open").Sum(lot => (position.Side == "Long" ? bar.Close - lot.OpeningTradeExecution.Price : lot.OpeningTradeExecution.Price - bar.Close) * lot.CurrentQuantity) - resolution.NetCost;
            return new ManualPositionOverview(position.PositionId, identity.Item1, identity.Item2, position.Side, position.MarginLots.Where(lot => lot.Status == "Open").Sum(lot => lot.CurrentQuantity), $"{position.AppliedStrategyKey} / {position.AppliedStrategyVersion}", reconciliation,
                evaluation?.AggregatedDecision, evaluation?.EvaluationBarDate, evaluation?.EvaluationOutcome,
                activePlans.Length == 1 ? activePlans[0].StopPrice : null, activePlans.Length == 1 ? activePlans[0].TakeProfitPrice : null,
                activeTerms.Where(term => term.FinalRepaymentDate.HasValue).Select(term => term.FinalRepaymentDate).Min(),
                resolution.HasCompleteConfirmedCost ? resolution.ConfirmedNetCost : null, referencePnl,
                bar?.TradingDate, bar?.Close);
        }).ToArray();
    }

    public async Task<IReadOnlyList<ManualExecutionOverview>> GetExecutionsAsync(CancellationToken cancellationToken = default)
    {
        var executions = await context.TradeExecutions.Include(execution => execution.Position).OrderByDescending(execution => execution.ExecutedAt).ToListAsync(cancellationToken);
        var revisions = await CurrentRevisionsAsync(executions.Select(execution => execution.Position.InstrumentId), cancellationToken);
        return executions.Select(execution =>
        {
            var identity = revisions.TryGetValue(execution.Position.InstrumentId, out var revision)
                ? revision
                : ("（銘柄コード未確認）", "（銘柄名未確認）");
            var source = execution.PrefilledFromCandidateResultId.HasValue ? "候補から入力補助（利用者手入力）" : "利用者手入力";
            return new ManualExecutionOverview(execution.TradeExecutionId, identity.Item1, identity.Item2, execution.Position.Side, execution.ExecutionRole, source, execution.ExecutedAt, execution.Price, execution.Quantity, execution.Revision, execution.Status, execution.Notes);
        }).ToArray();
    }

    private async Task<Dictionary<int, (string Code, string Name)>> CurrentRevisionsAsync(IEnumerable<int> instrumentIds, CancellationToken cancellationToken)
    {
        var ids = instrumentIds.Distinct().ToArray();
        var revisions = await context.InstrumentMasterRevisions.Where(revision => ids.Contains(revision.InstrumentId) && revision.Status == "Active").ToListAsync(cancellationToken);
        return revisions.GroupBy(revision => revision.InstrumentId).ToDictionary(group => group.Key, group =>
        {
            var revision = group.OrderByDescending(item => item.AvailableAtUtc).ThenByDescending(item => item.Revision).First();
            return (revision.Code, revision.Name);
        });
    }
}
