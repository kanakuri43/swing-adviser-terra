using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Positions;

public sealed class EfManualPositionOverviewReader(SwingAdviserDbContext context) : IManualPositionOverviewReader
{
    public async Task<IReadOnlyList<ManualPositionOverview>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await context.Positions.Where(position => position.Status == "Open").Include(position => position.MarginLots).ToListAsync(cancellationToken);
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
        return positions.OrderBy(position => position.PositionId).Select(position =>
        {
            var identity = revisions.TryGetValue(position.InstrumentId, out var revision)
                ? revision
                : ("（銘柄コード未確認）", "（銘柄名未確認）");
            var lotIds = position.MarginLots.Select(lot => lot.MarginLotId).ToArray();
            var reconciliation = lotIds.Any(required.Contains) ? "企業アクション要照合" : lotIds.Any(applied.Contains) ? "企業アクション換算済み" : "企業アクション未発生/未確認";
            return new ManualPositionOverview(position.PositionId, identity.Item1, identity.Item2, position.Side, position.MarginLots.Where(lot => lot.Status == "Open").Sum(lot => lot.CurrentQuantity), $"{position.AppliedStrategyKey} / {position.AppliedStrategyVersion}", reconciliation);
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
