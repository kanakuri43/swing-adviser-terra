namespace SwingAdviser.Domain.Risk;

/// <summary>Versioned, validated inputs for the price-risk rules. They are frozen into each plan.</summary>
public sealed record RiskManagementParameters(
    decimal LongInitialStopAtrMultiple = 3.0m,
    decimal ShortInitialStopAtrMultiple = 2.5m,
    decimal PartialTakeProfitRiskMultiple = 1.5m,
    decimal PartialTakeProfitFraction = .50m,
    IReadOnlyList<int>? MaturityWarningBusinessDays = null)
{
    public const string InitialRiskPlanFactoryVersion = "initial-risk-plan-factory-v1";
    public const string HoldingRiskEvaluationVersion = "holding-risk-evaluation-v1";
    public const string PartialExitBreakevenPlanFactoryVersion = "partial-exit-breakeven-plan-factory-v1";
    public const string LotProfitAndLossVersion = "lot-profit-and-loss-v1";

    public IReadOnlyList<int> EffectiveMaturityWarningBusinessDays => MaturityWarningBusinessDays ?? new[] { 30, 10, 5, 1 };

    public void Validate()
    {
        if (LongInitialStopAtrMultiple <= 0 || ShortInitialStopAtrMultiple <= 0 || PartialTakeProfitRiskMultiple <= 0 || PartialTakeProfitFraction is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(RiskManagementParameters), "Risk-management parameters must be positive and the partial-exit fraction must be between zero and one.");
        if (EffectiveMaturityWarningBusinessDays.Count == 0 || EffectiveMaturityWarningBusinessDays.Any(days => days <= 0) || EffectiveMaturityWarningBusinessDays.Distinct().Count() != EffectiveMaturityWarningBusinessDays.Count)
            throw new ArgumentOutOfRangeException(nameof(MaturityWarningBusinessDays), "Maturity-warning thresholds must be distinct positive business-day counts.");
    }
}

public static class RiskVocabulary
{
    public const string Long = "Long";
    public const string Short = "Short";
    public const string Open = "Open";
    public const string Closed = "Closed";
    public const string Effective = "Effective";
    public const string Superseded = "Superseded";
    public const string Voided = "Voided";
    public const string Initial = "Initial";
    public const string PartialExitBreakeven = "PartialExitBreakeven";
    public const string Evaluated = "Evaluated";
    public const string StopLoss = "StopLoss";
    public const string Exit = "Exit";
    public const string TakeProfit = "TakeProfit";
    public const string Hold = "Hold";
    public const string Matched = "Matched";
    public const string NotMatched = "NotMatched";
    public const string Missing = "Missing";
    public const string Indeterminate = "Indeterminate";
    public const string Reached = "Reached";
    public const string NotReached = "NotReached";
    public const string Candidate = "Candidate";
    public const string NotFeasible = "NotFeasible";
    public const string NotApplicable = "NotApplicable";
}

public sealed record HoldingBar(int DailyBarId, DateOnly TradingDate, decimal Open, decimal High, decimal Low, decimal Close, bool IsPointInTimeVerified = true, bool IsFinalized = true);

public sealed record PriceLineEvidence(string LineKind, string ComparedField, string Operator, decimal ObservedPrice, decimal LinePrice, bool Reached);

public sealed record RiskPlanLeafSelection(RiskPlan? Plan, string? FailureOutcome, string Reason)
{
    public bool IsSuccess => Plan is not null;
}

public sealed record LotPriceEvaluation(
    string? Decision,
    string EvaluationOutcome,
    RiskPlan? RiskPlan,
    PriceLineEvidence? StopEvidence,
    PriceLineEvidence? TargetEvidence,
    string Reason)
{
    public bool StopReachedToday => StopEvidence?.Reached == true;
    public bool TargetReachedToday => TargetEvidence?.Reached == true;
}

public sealed record TargetReachState(string State, DateOnly? FirstReachBarDate = null, int? DailyBarId = null, int? RiskPlanRevisionId = null, string? Evidence = null)
{
    public static TargetReachState NotReached { get; } = new(RiskVocabulary.NotReached);
    public static TargetReachState Indeterminate { get; } = new(RiskVocabulary.Indeterminate);
}

public sealed record TechnicalReversalInput(decimal? PreviousMacdLine, decimal? PreviousMacdSignal, decimal? CurrentMacdLine, decimal? CurrentMacdSignal, decimal? CurrentEma20, decimal Close);

public sealed record TechnicalReversalEvaluation(string OverallState, string MacdState, string Ema20State);

public sealed record PartialExitQuantityProposal(int MarginLotId, string Status, decimal? CandidateQuantity, decimal? RemainingQuantity, decimal? EffectiveFraction);

public sealed record LotHoldingRiskInput(
    int MarginLotId,
    string Side,
    decimal CurrentQuantity,
    LotPriceEvaluation PriceEvaluation,
    TargetReachState PriorTargetReach,
    TechnicalReversalInput? TechnicalInput,
    bool PartialExitConfirmed,
    int TradingUnit);

public sealed record LotHoldingRiskResult(
    int MarginLotId,
    string EvaluationOutcome,
    string? Decision,
    bool StopReachedToday,
    bool TargetReachedToday,
    TargetReachState PriorTargetReach,
    TechnicalReversalEvaluation TechnicalReversal,
    PartialExitQuantityProposal PartialExitProposal,
    string Reason);

public sealed record PositionHoldingRiskResult(string EvaluationOutcome, string? Decision, string PartialExitStatus, decimal? PartialExitTotalCandidateQuantity, IReadOnlyList<LotHoldingRiskResult> Lots);
