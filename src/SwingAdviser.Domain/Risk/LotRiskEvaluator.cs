namespace SwingAdviser.Domain.Risk;

/// <summary>Selects a single auditable risk-plan leaf and evaluates daily stop/target reach without inferring intraday ordering.</summary>
public sealed class LotRiskEvaluator
{
    /// <summary>Rebuilds target-reach state from eligible daily bars; it never trusts a persisted boolean or current price.</summary>
    public TargetReachState ReconstructPriorTargetReach(string side, int marginLotId, IEnumerable<(HoldingBar Bar, DateTime SessionStartUtc)> historicalBars, IReadOnlyCollection<RiskPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(historicalBars);
        var bars = historicalBars.OrderBy(item => item.Bar.TradingDate).ToArray();
        if (bars.Select(item => item.Bar.TradingDate).Distinct().Count() != bars.Length)
            return TargetReachState.Indeterminate;
        foreach (var item in bars)
        {
            var evaluation = Evaluate(side, item.Bar, plans, marginLotId, item.SessionStartUtc);
            if (evaluation.EvaluationOutcome != RiskVocabulary.Evaluated) return TargetReachState.Indeterminate;
            if (evaluation.TargetReachedToday)
                return new TargetReachState(RiskVocabulary.Reached, item.Bar.TradingDate, item.Bar.DailyBarId, evaluation.RiskPlan!.RiskPlanId, evaluation.TargetEvidence!.ToString());
        }
        return TargetReachState.NotReached;
    }

    public RiskPlanLeafSelection SelectActivePlanLeaf(int marginLotId, IReadOnlyCollection<RiskPlan> plans, DateTime evaluationSessionStartUtc)
    {
        if (marginLotId <= 0 || evaluationSessionStartUtc.Kind != DateTimeKind.Utc) return new RiskPlanLeafSelection(null, "IncompletePositionData", "The lot ID or session-start cutoff is invalid.");
        var relevant = plans.Where(plan => plan.MarginLotId == marginLotId && plan.Status != RiskVocabulary.Voided).ToArray();
        if (relevant.Length == 0) return new RiskPlanLeafSelection(null, "IncompletePositionData", "No risk plan exists for the lot.");
        if (relevant.Any(plan => plan.EffectiveAtUtc > evaluationSessionStartUtc || plan.RecordedAtUtc > evaluationSessionStartUtc))
            return new RiskPlanLeafSelection(null, "IntradaySequenceUnknown", "A plan revision was not effective and recorded before the evaluation session.");
        if (relevant.Any(plan => plan.RiskPlanId <= 0 || plan.RiskBasisId <= 0 || plan.Revision <= 0 || plan.StopPrice <= 0 || plan.TakeProfitPrice <= 0 || plan.PartialTakeProfitFraction is <= 0 or >= 1))
            return new RiskPlanLeafSelection(null, "InvalidData", "The risk-plan chain contains invalid values.");
        var ids = relevant.Select(plan => plan.RiskPlanId).ToHashSet();
        if (relevant.Any(plan => plan.SupersedesRevisionId.HasValue && !ids.Contains(plan.SupersedesRevisionId.Value)))
            return new RiskPlanLeafSelection(null, "IncompletePositionData", "The risk-plan chain has a missing predecessor.");
        var superseded = relevant.Where(plan => plan.SupersedesRevisionId.HasValue).Select(plan => plan.SupersedesRevisionId!.Value).ToHashSet();
        var leaves = relevant.Where(plan => !superseded.Contains(plan.RiskPlanId) && plan.Status == RiskVocabulary.Effective).ToArray();
        if (leaves.Length != 1) return new RiskPlanLeafSelection(null, "IncompletePositionData", "The risk-plan chain has no unique effective leaf.");
        var leaf = leaves[0];
        var visited = new HashSet<int>();
        for (var cursor = leaf; ;)
        {
            if (!visited.Add(cursor.RiskPlanId)) return new RiskPlanLeafSelection(null, "InvalidData", "The risk-plan chain contains a cycle.");
            if (!cursor.SupersedesRevisionId.HasValue) break;
            cursor = relevant.SingleOrDefault(plan => plan.RiskPlanId == cursor.SupersedesRevisionId.Value)!;
            if (cursor is null) return new RiskPlanLeafSelection(null, "IncompletePositionData", "The risk-plan chain contains a missing predecessor.");
        }
        if (visited.Count != relevant.Length || relevant.Select(plan => plan.Revision).Distinct().Count() != relevant.Length)
            return new RiskPlanLeafSelection(null, "IncompletePositionData", "The risk-plan chain is branched or has duplicate revisions.");
        return new RiskPlanLeafSelection(leaf, null, "A unique effective plan leaf was selected.");
    }

    public LotPriceEvaluation Evaluate(string side, HoldingBar bar, IReadOnlyCollection<RiskPlan> plans, int marginLotId, DateTime evaluationSessionStartUtc)
    {
        if (bar is null || !bar.IsPointInTimeVerified) return Failure("PointInTimeUnverified", "The evaluation bar is not point-in-time verified.");
        if (!bar.IsFinalized) return Failure("InvalidData", "The evaluation bar is not finalized.");
        if (bar.DailyBarId <= 0 || bar.TradingDate == default || bar.Open <= 0 || bar.Low <= 0 || bar.High < bar.Low || bar.Close < bar.Low || bar.Close > bar.High)
            return Failure("InvalidData", "The evaluation bar contains invalid OHLC values.");
        if (side != RiskVocabulary.Long && side != RiskVocabulary.Short) return Failure("InvalidData", "Position side is invalid.");
        var selection = SelectActivePlanLeaf(marginLotId, plans, evaluationSessionStartUtc);
        if (!selection.IsSuccess) return Failure(selection.FailureOutcome!, selection.Reason);
        var plan = selection.Plan!;
        var stopObserved = side == RiskVocabulary.Long ? bar.Low : bar.High;
        var targetObserved = side == RiskVocabulary.Long ? bar.High : bar.Low;
        var stopReached = side == RiskVocabulary.Long ? stopObserved <= plan.StopPrice : stopObserved >= plan.StopPrice;
        var targetReached = side == RiskVocabulary.Long ? targetObserved >= plan.TakeProfitPrice : targetObserved <= plan.TakeProfitPrice;
        var stopOperator = side == RiskVocabulary.Long ? "<=" : ">=";
        var targetOperator = side == RiskVocabulary.Long ? ">=" : "<=";
        var stop = new PriceLineEvidence("Stop", side == RiskVocabulary.Long ? "Low" : "High", stopOperator, stopObserved, plan.StopPrice, stopReached);
        var target = new PriceLineEvidence("TakeProfit", side == RiskVocabulary.Long ? "High" : "Low", targetOperator, targetObserved, plan.TakeProfitPrice, targetReached);
        return new LotPriceEvaluation(stopReached ? RiskVocabulary.StopLoss : null, RiskVocabulary.Evaluated, plan, stop, target, "Daily high/low price lines were evaluated.");
    }

    private static LotPriceEvaluation Failure(string outcome, string reason) => new(null, outcome, null, null, null, reason);
}
