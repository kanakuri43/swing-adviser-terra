namespace SwingAdviser.Domain.Risk;

/// <summary>Fail-closed aggregation of independent lot evaluations for one position.</summary>
public sealed class PositionHoldingRiskAggregator
{
    public PositionHoldingRiskResult Aggregate(IEnumerable<LotHoldingRiskResult> evaluations)
    {
        var lots = evaluations?.OrderBy(item => item.MarginLotId).ToArray() ?? Array.Empty<LotHoldingRiskResult>();
        if (lots.Length == 0) return new PositionHoldingRiskResult("IncompletePositionData", null, RiskVocabulary.NotApplicable, null, lots);
        var incomplete = lots.FirstOrDefault(item => item.EvaluationOutcome != RiskVocabulary.Evaluated);
        if (incomplete is not null) return new PositionHoldingRiskResult(incomplete.EvaluationOutcome, null, RiskVocabulary.NotApplicable, null, lots);
        var decision = lots.Select(item => item.Decision).OrderByDescending(Rank).FirstOrDefault();
        if (decision is null) return new PositionHoldingRiskResult("InvalidData", null, RiskVocabulary.NotApplicable, null, lots);
        if (decision != RiskVocabulary.TakeProfit) return new PositionHoldingRiskResult(RiskVocabulary.Evaluated, decision, RiskVocabulary.NotApplicable, null, lots);
        var candidates = lots.Where(item => item.Decision == RiskVocabulary.TakeProfit && item.PartialExitProposal.Status == RiskVocabulary.Candidate).ToArray();
        if (candidates.Length > 0) return new PositionHoldingRiskResult(RiskVocabulary.Evaluated, decision, RiskVocabulary.Candidate, candidates.Sum(item => item.PartialExitProposal.CandidateQuantity!.Value), lots);
        var hasNotFeasible = lots.Any(item => item.Decision == RiskVocabulary.TakeProfit && item.PartialExitProposal.Status == RiskVocabulary.NotFeasible);
        return new PositionHoldingRiskResult(RiskVocabulary.Evaluated, decision, hasNotFeasible ? RiskVocabulary.NotFeasible : RiskVocabulary.NotApplicable, null, lots);
    }

    private static int Rank(string? decision) => decision switch
    {
        RiskVocabulary.StopLoss => 4,
        RiskVocabulary.Exit => 3,
        RiskVocabulary.TakeProfit => 2,
        RiskVocabulary.Hold => 1,
        _ => 0,
    };
}
