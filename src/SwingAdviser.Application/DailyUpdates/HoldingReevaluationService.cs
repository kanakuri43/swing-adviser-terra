using System.Text.Json;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Application.DailyUpdates;

public sealed record HoldingLotReevaluationInput(
    Position Position,
    MarginLot Lot,
    DailyBar? CurrentBar,
    IReadOnlyCollection<RiskPlan> RiskPlans,
    IReadOnlyList<(HoldingBar Bar, DateTime SessionStartUtc)> PriorBars,
    TechnicalReversalInput? TechnicalInput,
    bool PartialExitConfirmed,
    bool ReconciliationRequired);

public sealed record HoldingReevaluationLotOutcome(HoldingLotReevaluationInput Input, LotHoldingRiskResult Outcome, RiskPlan? AppliedRiskPlan);
public sealed record HoldingReevaluationPositionOutcome(Position Position, PositionHoldingRiskResult Outcome, IReadOnlyList<HoldingReevaluationLotOutcome> Lots);
public sealed record HoldingReevaluationResult(int EvaluatedPositionCount, int IndeterminatePositionCount, IReadOnlyList<HoldingReevaluationPositionOutcome> Positions);

public interface IHoldingReevaluationStore
{
    Task<IReadOnlyList<HoldingLotReevaluationInput>> LoadOpenLotsAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, CancellationToken cancellationToken);
    Task SaveAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, IReadOnlyList<HoldingReevaluationPositionOutcome> outcomes, CancellationToken cancellationToken);
}

/// <summary>Applies the fail-closed holding-risk rules and persists only analysis records; trades, lots and plans are never altered.</summary>
public sealed class HoldingReevaluationService(IHoldingReevaluationStore store, int tradingUnit = 100, decimal partialTakeProfitFraction = .5m)
{
    public async Task<HoldingReevaluationResult> ReevaluateAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, CancellationToken cancellationToken = default)
    {
        if (evaluationBarDate == default || evaluatedAtUtc.Kind != DateTimeKind.Utc || tradingUnit <= 0 || partialTakeProfitFraction is <= 0 or >= 1)
            throw new ArgumentException("Evaluation date/time and trading parameters are invalid.");
        var inputs = await store.LoadOpenLotsAsync(evaluationBarDate, evaluatedAtUtc, cancellationToken);
        var outcomes = new List<HoldingReevaluationPositionOutcome>();
        foreach (var group in inputs.GroupBy(input => input.Position.PositionId).OrderBy(group => group.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lots = group.OrderBy(input => input.Lot.MarginLotId).Select(input => EvaluateLot(input, evaluatedAtUtc)).ToArray();
            var aggregate = new PositionHoldingRiskAggregator().Aggregate(lots.Select(lot => lot.Outcome));
            outcomes.Add(new HoldingReevaluationPositionOutcome(group.First().Position, aggregate, lots));
        }
        await store.SaveAsync(evaluationBarDate, evaluatedAtUtc, outcomes, cancellationToken);
        return new HoldingReevaluationResult(outcomes.Count, outcomes.Count(outcome => outcome.Outcome.EvaluationOutcome != RiskVocabulary.Evaluated), outcomes);
    }

    private HoldingReevaluationLotOutcome EvaluateLot(HoldingLotReevaluationInput input, DateTime evaluatedAtUtc)
    {
        var fallback = new LotHoldingRiskResult(input.Lot.MarginLotId, "IncompletePositionData", null, false, false, TargetReachState.Indeterminate,
            new TechnicalReversalEvaluation(RiskVocabulary.Indeterminate, RiskVocabulary.Missing, RiskVocabulary.Missing), new PartialExitQuantityProposal(input.Lot.MarginLotId, RiskVocabulary.NotApplicable, null, null, null), "Required holding inputs are missing.");
        if (input.ReconciliationRequired)
            return new HoldingReevaluationLotOutcome(input, fallback with { EvaluationOutcome = "ReconciliationRequired", Reason = "Corporate-action reconciliation is required before holding evaluation." }, null);
        if (input.CurrentBar is null || input.RiskPlans.Count == 0)
            return new HoldingReevaluationLotOutcome(input, fallback, null);

        var bar = input.CurrentBar;
        var evaluationSession = evaluatedAtUtc;
        var price = new LotRiskEvaluator().Evaluate(input.Position.Side,
            new HoldingBar(bar.DailyBarId, bar.TradingDate, bar.Open, bar.High, bar.Low, bar.Close, true, bar.Status is "Final" or "Corrected"), input.RiskPlans, input.Lot.MarginLotId, evaluationSession);
        var selected = price.RiskPlan;
        var prior = new LotRiskEvaluator().ReconstructPriorTargetReach(input.Position.Side, input.Lot.MarginLotId, input.PriorBars, input.RiskPlans);
        var outcome = new LotHoldingRiskEvaluator().Evaluate(new LotHoldingRiskInput(input.Lot.MarginLotId, input.Position.Side, input.Lot.CurrentQuantity, price, prior, input.TechnicalInput, input.PartialExitConfirmed, tradingUnit), partialTakeProfitFraction);
        return new HoldingReevaluationLotOutcome(input, outcome, selected);
    }
}
