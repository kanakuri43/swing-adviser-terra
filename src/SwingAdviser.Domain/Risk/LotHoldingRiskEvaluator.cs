namespace SwingAdviser.Domain.Risk;

/// <summary>Applies the holding decision priority after price-line evaluation. It never changes executions or lots.</summary>
public sealed class LotHoldingRiskEvaluator
{
    public LotHoldingRiskResult Evaluate(LotHoldingRiskInput input, decimal partialTakeProfitFraction)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PriceEvaluation.EvaluationOutcome != RiskVocabulary.Evaluated)
            return Indeterminate(input, input.PriceEvaluation.EvaluationOutcome, input.PriceEvaluation.Reason);
        if (input.CurrentQuantity <= 0 || input.TradingUnit <= 0 || (input.Side != RiskVocabulary.Long && input.Side != RiskVocabulary.Short) || partialTakeProfitFraction is <= 0 or >= 1)
            return Indeterminate(input, "InvalidData", "Lot quantity, side, unit, or partial-exit fraction is invalid.");

        var targetState = input.PriceEvaluation.TargetReachedToday ? new TargetReachState(RiskVocabulary.Reached) : input.PriorTargetReach;
        if (input.PriceEvaluation.StopReachedToday)
            return Result(input, RiskVocabulary.Evaluated, RiskVocabulary.StopLoss, targetState, new TechnicalReversalEvaluation(RiskVocabulary.NotMatched, RiskVocabulary.NotMatched, RiskVocabulary.NotMatched), NotApplicable(input), "The stop was reached; it takes priority over every other condition.");
        if (targetState.State == RiskVocabulary.Indeterminate)
            return Indeterminate(input, "IncompletePositionData", "Prior target reach cannot be reconstructed from eligible history.");
        if (targetState.State != RiskVocabulary.NotReached && targetState.State != RiskVocabulary.Reached)
            return Indeterminate(input, "InvalidData", "Prior target-reach state is invalid.");
        if (targetState.State == RiskVocabulary.NotReached)
            return Result(input, RiskVocabulary.Evaluated, RiskVocabulary.Hold, targetState, new TechnicalReversalEvaluation(RiskVocabulary.NotMatched, RiskVocabulary.NotMatched, RiskVocabulary.NotMatched), NotApplicable(input), "No stop or target was reached.");

        var reversal = EvaluateReversal(input.Side, input.TechnicalInput);
        if (reversal.OverallState == RiskVocabulary.Indeterminate)
            return Result(input, "IncompletePositionData", null, targetState, reversal, NotApplicable(input), "Target was reached but technical reversal inputs are incomplete.");
        if (reversal.OverallState == RiskVocabulary.Matched)
            return Result(input, RiskVocabulary.Evaluated, RiskVocabulary.Exit, targetState, reversal, NotApplicable(input), "Target was reached and a technical reversal matched.");
        if (input.PartialExitConfirmed)
            return Result(input, RiskVocabulary.Evaluated, RiskVocabulary.Hold, targetState, reversal, NotApplicable(input), "Partial exit is confirmed and no technical reversal matched.");
        var proposal = new LotPartialExitQuantityCalculator().Calculate(input.MarginLotId, input.CurrentQuantity, partialTakeProfitFraction, input.TradingUnit);
        return Result(input, RiskVocabulary.Evaluated, RiskVocabulary.TakeProfit, targetState, reversal, proposal, "Target was reached and partial profit-taking remains a user decision.");
    }

    public TechnicalReversalEvaluation EvaluateReversal(string side, TechnicalReversalInput? input)
    {
        if (input is null || (side != RiskVocabulary.Long && side != RiskVocabulary.Short)) return new TechnicalReversalEvaluation(RiskVocabulary.Indeterminate, RiskVocabulary.Missing, RiskVocabulary.Missing);
        var macdMissing = input.PreviousMacdLine is null || input.PreviousMacdSignal is null || input.CurrentMacdLine is null || input.CurrentMacdSignal is null;
        var macd = macdMissing ? RiskVocabulary.Missing : side == RiskVocabulary.Long
            ? input.PreviousMacdLine >= input.PreviousMacdSignal && input.CurrentMacdLine < input.CurrentMacdSignal ? RiskVocabulary.Matched : RiskVocabulary.NotMatched
            : input.PreviousMacdLine <= input.PreviousMacdSignal && input.CurrentMacdLine > input.CurrentMacdSignal ? RiskVocabulary.Matched : RiskVocabulary.NotMatched;
        var ema = input.CurrentEma20 is null ? RiskVocabulary.Missing : side == RiskVocabulary.Long
            ? input.Close < input.CurrentEma20 ? RiskVocabulary.Matched : RiskVocabulary.NotMatched
            : input.Close > input.CurrentEma20 ? RiskVocabulary.Matched : RiskVocabulary.NotMatched;
        var overall = macd == RiskVocabulary.Matched || ema == RiskVocabulary.Matched ? RiskVocabulary.Matched
            : macd == RiskVocabulary.NotMatched && ema == RiskVocabulary.NotMatched ? RiskVocabulary.NotMatched : RiskVocabulary.Indeterminate;
        return new TechnicalReversalEvaluation(overall, macd, ema);
    }

    private static LotHoldingRiskResult Indeterminate(LotHoldingRiskInput input, string outcome, string reason) => Result(input, outcome, null, input.PriorTargetReach, new TechnicalReversalEvaluation(RiskVocabulary.Indeterminate, RiskVocabulary.Missing, RiskVocabulary.Missing), NotApplicable(input), reason);
    private static PartialExitQuantityProposal NotApplicable(LotHoldingRiskInput input) => new(input.MarginLotId, RiskVocabulary.NotApplicable, null, null, null);
    private static LotHoldingRiskResult Result(LotHoldingRiskInput input, string outcome, string? decision, TargetReachState targetReach, TechnicalReversalEvaluation reversal, PartialExitQuantityProposal proposal, string reason) => new(input.MarginLotId, outcome, decision, input.PriceEvaluation.StopReachedToday, input.PriceEvaluation.TargetReachedToday, targetReach, reversal, proposal, reason);
}

public sealed class LotPartialExitQuantityCalculator
{
    public PartialExitQuantityProposal Calculate(int marginLotId, decimal currentQuantity, decimal fraction, int tradingUnit)
    {
        if (marginLotId <= 0 || currentQuantity <= 0 || fraction is <= 0 or >= 1 || tradingUnit <= 0) return new PartialExitQuantityProposal(marginLotId, RiskVocabulary.NotFeasible, null, null, null);
        var candidate = decimal.Floor(currentQuantity * fraction / tradingUnit) * tradingUnit;
        var remaining = currentQuantity - candidate;
        if (candidate <= 0 || remaining < tradingUnit) return new PartialExitQuantityProposal(marginLotId, RiskVocabulary.NotFeasible, null, null, null);
        return new PartialExitQuantityProposal(marginLotId, RiskVocabulary.Candidate, candidate, remaining, candidate / currentQuantity);
    }
}
