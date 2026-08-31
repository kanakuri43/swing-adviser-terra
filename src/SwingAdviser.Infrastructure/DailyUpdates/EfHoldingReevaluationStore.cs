using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

/// <summary>Loads immutable position inputs and appends holding evaluations. Corporate-action adjusted lots stay fail-closed until adjusted risk-plan inputs are available.</summary>
public sealed class EfHoldingReevaluationStore(SwingAdviserDbContext context) : IHoldingReevaluationStore
{
    public async Task<IReadOnlyList<HoldingLotReevaluationInput>> LoadOpenLotsAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, CancellationToken cancellationToken)
    {
        var positions = await context.Positions.Where(position => position.Status == "Open")
            .Include(position => position.MarginLots).ThenInclude(lot => lot.OpeningTradeExecution).ToListAsync(cancellationToken);
        var lots = positions.SelectMany(position => position.MarginLots.Where(lot => lot.Status == "Open")).ToArray();
        if (lots.Length == 0) return Array.Empty<HoldingLotReevaluationInput>();
        var lotIds = lots.Select(lot => lot.MarginLotId).ToArray();
        var instrumentIds = positions.Select(position => position.InstrumentId).Distinct().ToArray();
        var plans = await context.RiskPlans.Where(plan => lotIds.Contains(plan.MarginLotId) && plan.Status != "Voided").Include(plan => plan.RiskBasis).ToListAsync(cancellationToken);
        var allBars = await context.DailyBars.Where(bar => instrumentIds.Contains(bar.InstrumentId) && bar.TradingDate <= evaluationBarDate && bar.FetchedAtUtc <= evaluatedAtUtc).ToListAsync(cancellationToken);
        var barsByInstrument = allBars.GroupBy(bar => new { bar.InstrumentId, bar.TradingDate }).Select(group => group.OrderByDescending(bar => bar.Revision).First())
            .GroupBy(bar => bar.InstrumentId).ToDictionary(group => group.Key, group => group.OrderBy(bar => bar.TradingDate).ToArray());
        var indicators = await context.IndicatorResults.Where(indicator => instrumentIds.Contains(indicator.InstrumentId) && indicator.EvaluationBarDate <= evaluationBarDate && indicator.AnalyzedAtUtc <= evaluatedAtUtc).ToListAsync(cancellationToken);
        var indicatorsByInstrument = indicators.GroupBy(indicator => new { indicator.InstrumentId, indicator.EvaluationBarDate })
            .ToDictionary(group => group.Key, group => group.OrderByDescending(indicator => indicator.AnalyzedAtUtc).First());
        var adjustments = await context.PositionCorporateActionAdjustments.Where(adjustment => lotIds.Contains(adjustment.MarginLotId)).ToListAsync(cancellationToken);
        var partialLots = await context.TradeExecutionLotAllocations.Where(allocation => lotIds.Contains(allocation.MarginLotId) && allocation.Status == "Effective")
            .Include(allocation => allocation.TradeExecution).Where(allocation => allocation.TradeExecution.ExecutionRole == "Close" && allocation.TradeExecution.Status == "Effective")
            .Select(allocation => allocation.MarginLotId).Distinct().ToListAsync(cancellationToken);
        var partialSet = partialLots.ToHashSet();
        var adjustedSet = adjustments.Select(adjustment => adjustment.MarginLotId).ToHashSet();

        return lots.Select(lot =>
        {
            var position = positions.Single(item => item.PositionId == lot.PositionId);
            barsByInstrument.TryGetValue(position.InstrumentId, out var bars);
            var current = bars?.SingleOrDefault(bar => bar.TradingDate == evaluationBarDate);
            var prior = (bars ?? Array.Empty<DailyBar>()).Where(bar => bar.TradingDate < evaluationBarDate && bar.TradingDate >= DateOnly.FromDateTime(lot.OpeningTradeExecution.ExecutedAt.Date))
                .Select(bar => (new HoldingBar(bar.DailyBarId, bar.TradingDate, bar.Open, bar.High, bar.Low, bar.Close, true, bar.Status is "Final" or "Corrected"), DateTime.SpecifyKind(bar.TradingDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc))).ToArray();
            var currentIndicator = indicatorsByInstrument.GetValueOrDefault(new { InstrumentId = position.InstrumentId, EvaluationBarDate = evaluationBarDate });
            var previousIndicator = indicatorsByInstrument.Where(item => item.Key.InstrumentId == position.InstrumentId && item.Key.EvaluationBarDate < evaluationBarDate).OrderByDescending(item => item.Key.EvaluationBarDate).Select(item => item.Value).FirstOrDefault();
            var technical = current is null ? null : new TechnicalReversalInput(previousIndicator?.MacdLine, previousIndicator?.MacdSignal, currentIndicator?.MacdLine, currentIndicator?.MacdSignal, currentIndicator?.Ema20, current.Close);
            return new HoldingLotReevaluationInput(position, lot, current, plans.Where(plan => plan.MarginLotId == lot.MarginLotId).ToArray(), prior, technical, partialSet.Contains(lot.MarginLotId), adjustedSet.Contains(lot.MarginLotId));
        }).ToArray();
    }

    public async Task SaveAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, IReadOnlyList<HoldingReevaluationPositionOutcome> outcomes, CancellationToken cancellationToken)
    {
        var positionIds = outcomes.Select(outcome => outcome.Position.PositionId).ToArray();
        var existingPositionIds = (await context.PositionHoldingEvaluations
            .Where(evaluation => positionIds.Contains(evaluation.PositionId) && evaluation.EvaluationBarDate == evaluationBarDate && evaluation.EvaluatedAtUtc == evaluatedAtUtc)
            .Select(evaluation => evaluation.PositionId).ToListAsync(cancellationToken)).ToHashSet();
        var lotIds = outcomes.SelectMany(outcome => outcome.Lots).Select(lot => lot.Input.Lot.MarginLotId).ToArray();
        var existingLotIds = (await context.LotHoldingEvaluations
            .Where(evaluation => lotIds.Contains(evaluation.MarginLotId) && evaluation.EvaluationBarDate == evaluationBarDate && evaluation.EvaluatedAtUtc == evaluatedAtUtc)
            .Select(evaluation => evaluation.MarginLotId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var outcome in outcomes)
        {
            if (!existingPositionIds.Contains(outcome.Position.PositionId)) context.PositionHoldingEvaluations.Add(new PositionHoldingEvaluation
            {
                PositionId = outcome.Position.PositionId,
                EvaluationBarDate = evaluationBarDate,
                EvaluatedAtUtc = evaluatedAtUtc,
                AggregatedDecision = outcome.Outcome.Decision,
                PartialExitStatus = outcome.Outcome.PartialExitStatus,
                PartialExitTotalCandidateQuantity = outcome.Outcome.PartialExitTotalCandidateQuantity,
                EvaluationOutcome = outcome.Outcome.EvaluationOutcome,
                LotEvaluationsJson = JsonSerializer.Serialize(outcome.Lots.Select(lot => new { lotId = lot.Input.Lot.MarginLotId, outcome = lot.Outcome.EvaluationOutcome, decision = lot.Outcome.Decision, reason = lot.Outcome.Reason })),
                CreatedAtUtc = evaluatedAtUtc,
            });
            foreach (var lot in outcome.Lots.Where(lot => lot.Input.CurrentBar is not null && lot.AppliedRiskPlan is not null && !existingLotIds.Contains(lot.Input.Lot.MarginLotId)))
            {
                context.LotHoldingEvaluations.Add(new LotHoldingEvaluation
                {
                    MarginLotId = lot.Input.Lot.MarginLotId,
                    PositionId = outcome.Position.PositionId,
                    EvaluationBarDate = evaluationBarDate,
                    EvaluatedAtUtc = evaluatedAtUtc,
                    DailyBarId = lot.Input.CurrentBar!.DailyBarId,
                    RiskPlanRevisionId = lot.AppliedRiskPlan!.RiskPlanId,
                    Decision = lot.Outcome.Decision,
                    StopReachedToday = lot.Outcome.StopReachedToday,
                    TargetReachedToday = lot.Outcome.TargetReachedToday,
                    PriorTargetReachState = lot.Outcome.PriorTargetReach.State,
                    PriorTargetFirstReachBarDate = lot.Outcome.PriorTargetReach.FirstReachBarDate,
                    TechnicalReversalState = lot.Outcome.TechnicalReversal.OverallState,
                    MacdReversalState = lot.Outcome.TechnicalReversal.MacdState,
                    Ema20ReversalState = lot.Outcome.TechnicalReversal.Ema20State,
                    PartialExitStatus = lot.Outcome.PartialExitProposal.Status,
                    PartialExitCandidateQuantity = lot.Outcome.PartialExitProposal.CandidateQuantity,
                    EvaluationOutcome = lot.Outcome.EvaluationOutcome,
                    EvaluationEvidenceJson = JsonSerializer.Serialize(new { schemaVersion = "holding-evaluation-evidence-v1", lot.Outcome.Reason }),
                    CreatedAtUtc = evaluatedAtUtc,
                });
            }
        }
        await context.SaveChangesAsync(cancellationToken);
    }
}
