using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Infrastructure.Tests;

public class RiskManagementEngineTests
{
    [Fact]
    public void InitialPlan_UsesDirectionSpecificStopAndOnePointFiveRTarget()
    {
        var (position, lot, opening, basis) = Opening("Short", 100m, 4m);
        var result = new InitialRiskPlanFactory().Create(new InitialRiskPlanRequest(position, lot, opening, basis, Utc(10)), new RiskManagementParameters());

        Assert.Equal(110m, result.RiskPlan.StopPrice);
        Assert.Equal(85m, result.RiskPlan.TakeProfitPrice);
        Assert.Equal(RiskVocabulary.Initial, result.RiskPlan.PlanKind);
        Assert.Equal(1, result.RiskPlan.Revision);
    }

    [Fact]
    public void PriceEvaluation_IncludesBothReachEvidenceButStopWinsOnSameBar()
    {
        var plan = Plan(20, 1, 90m, 115m);
        var result = new LotRiskEvaluator().Evaluate("Long", Bar(low: 89m, high: 116m), new[] { plan }, 10, Utc(9));

        Assert.Equal(RiskVocabulary.StopLoss, result.Decision);
        Assert.True(result.StopReachedToday);
        Assert.True(result.TargetReachedToday);
        Assert.Equal("Low", result.StopEvidence!.ComparedField);
        Assert.Equal("High", result.TargetEvidence!.ComparedField);
    }

    [Fact]
    public void PriorTargetReach_IsRebuiltFromEligibleHistoricalBars()
    {
        var plan = Plan(20, 1, 90m, 115m);
        var evaluator = new LotRiskEvaluator();
        var result = evaluator.ReconstructPriorTargetReach("Long", 10, new[]
        {
            (new HoldingBar(1, new DateOnly(2026, 8, 30), 100m, 114m, 98m, 110m), Utc(9)),
            (new HoldingBar(2, new DateOnly(2026, 8, 31), 110m, 116m, 105m, 112m), Utc(9)),
        }, new[] { plan });

        Assert.Equal(RiskVocabulary.Reached, result.State);
        Assert.Equal(new DateOnly(2026, 8, 31), result.FirstReachBarDate);
        Assert.Equal(2, result.DailyBarId);
        Assert.Equal(20, result.RiskPlanRevisionId);
    }

    [Fact]
    public void HoldingRisk_TargetWithMissingOneUnmatchedTechnicalSignal_FailsClosed()
    {
        var price = new LotPriceEvaluation(null, RiskVocabulary.Evaluated, Plan(20, 1, 90m, 115m), new PriceLineEvidence("Stop", "Low", "<=", 101m, 90m, false), new PriceLineEvidence("TakeProfit", "High", ">=", 116m, 115m, true), "ok");
        var input = new LotHoldingRiskInput(10, "Long", 100m, price, TargetReachState.NotReached, new TechnicalReversalInput(2m, 1m, 2m, 1m, null, 110m), false, 100);

        var result = new LotHoldingRiskEvaluator().Evaluate(input, .5m);

        Assert.Null(result.Decision);
        Assert.Equal("IncompletePositionData", result.EvaluationOutcome);
        Assert.Equal(RiskVocabulary.Indeterminate, result.TechnicalReversal.OverallState);
    }

    [Fact]
    public void HoldingRisk_TargetAndEmaReversalProducesExitInsteadOfPartialTakeProfit()
    {
        var price = new LotPriceEvaluation(null, RiskVocabulary.Evaluated, Plan(20, 1, 90m, 115m), new PriceLineEvidence("Stop", "Low", "<=", 101m, 90m, false), new PriceLineEvidence("TakeProfit", "High", ">=", 116m, 115m, true), "ok");
        var input = new LotHoldingRiskInput(10, "Long", 100m, price, TargetReachState.NotReached, new TechnicalReversalInput(2m, 1m, 2m, 1m, 111m, 110m), false, 100);

        var result = new LotHoldingRiskEvaluator().Evaluate(input, .5m);

        Assert.Equal(RiskVocabulary.Exit, result.Decision);
        Assert.Equal(RiskVocabulary.NotApplicable, result.PartialExitProposal.Status);
    }

    [Fact]
    public void PartialExitQuantity_DoesNotTurnAnOddLotIntoFullClose()
    {
        var proposal = new LotPartialExitQuantityCalculator().Calculate(10, 150m, .5m, 100);

        Assert.Equal(RiskVocabulary.NotFeasible, proposal.Status);
        Assert.Null(proposal.CandidateQuantity);
    }

    [Fact]
    public void PositionAggregation_OneIndeterminateLotNeverBecomesHold()
    {
        var evaluated = LotResult(10, RiskVocabulary.Evaluated, RiskVocabulary.TakeProfit, RiskVocabulary.Candidate, 100m);
        var incomplete = LotResult(11, "HistoryIncomplete", null, RiskVocabulary.NotApplicable, null);

        var result = new PositionHoldingRiskAggregator().Aggregate(new[] { evaluated, incomplete });

        Assert.Equal("HistoryIncomplete", result.EvaluationOutcome);
        Assert.Null(result.Decision);
        Assert.Equal(new[] { 10, 11 }, result.Lots.Select(lot => lot.MarginLotId));
    }

    [Fact]
    public void BreakevenPlan_ForConfirmedPartialCloseTightensLongStopWithoutChangingTarget()
    {
        var (position, lot, _, basis) = Opening("Long", 100m, 4m);
        var old = Plan(20, 1, 90m, 115m, basis);
        var close = new TradeExecution { TradeExecutionId = 30, PositionId = 1, ExecutionRole = "Close", Status = RiskVocabulary.Effective, ExecutedAt = new DateTimeOffset(Utc(11)), EnteredAtUtc = Utc(11) };
        var allocation = new TradeExecutionLotAllocation { AllocationId = 40, TradeExecutionId = 30, MarginLotId = 10, Quantity = 100m, Status = RiskVocabulary.Effective, RecordedAtUtc = Utc(11) };

        var result = new PartialExitBreakevenPlanFactory().TryCreate(new PartialExitBreakevenRequest(position, lot, close, allocation, 200m, new[] { old }, Utc(12)));

        Assert.NotNull(result);
        Assert.Equal(100m, result!.StopPrice);
        Assert.Equal(115m, result.TakeProfitPrice);
        Assert.Equal(20, result.SupersedesRevisionId);
        Assert.Equal(RiskVocabulary.PartialExitBreakeven, result.PlanKind);
    }

    [Fact]
    public void MarginCostLedger_UnknownCostIsNotTreatedAsZero()
    {
        var resolution = new MarginCostLedger().Resolve(new[]
        {
            Ledger(1, "Interest", "Estimate", "Charge", 100m),
            Ledger(2, "Backwardation", "Unpublished", "Charge", null),
        });

        Assert.False(resolution.IsResolved);
        Assert.Null(resolution.NetCost);
    }

    [Fact]
    public void MarginCostLedger_EstimateIsReferenceOnlyUntilEveryLogicalItemIsConfirmed()
    {
        var estimate = Ledger(1, "Interest", "Estimate", "Charge", 12m);
        var resolution = new MarginCostLedger().Resolve(new[] { estimate });
        var profit = new LotProfitAndLossCalculator().Calculate("Long", 100m, 100m, 102m, 6m, "JPY", "JPY", "JPY", resolution);

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasCompleteConfirmedCost);
        Assert.Equal(188m, profit.ReferenceNetProfitLoss);
        Assert.Null(profit.ConfirmedCostAdjustedProfitLoss);
    }

    [Fact]
    public void MarginCostLedger_KnownZeroIsAResolvedZeroRatherThanMissingData()
    {
        var zero = Ledger(1, "Interest", "KnownZero", "Charge", null);
        var resolution = new MarginCostLedger().Resolve(new[] { zero });

        Assert.True(resolution.IsResolved);
        Assert.True(resolution.HasCompleteConfirmedCost);
        Assert.Equal(0m, resolution.NetCost);
        Assert.Equal(0m, resolution.ConfirmedNetCost);
    }

    [Fact]
    public void MaturityAggregation_UsesEarliestDeadlineAndKeepsUnknownDistinct()
    {
        var one = new MarginLotMaturityInput(10, "Open", new[] { Term(1, 10, new DateOnly(2026, 9, 10)) });
        var two = new MarginLotMaturityInput(11, "Open", new[] { Term(2, 11, new DateOnly(2026, 9, 5)) });
        var result = new RepaymentMaturityAggregator().Evaluate(new[] { one, two }, new DateOnly(2026, 9, 1), new RiskManagementParameters(), new WeekdayBusinessDayCalendar());

        Assert.Equal(new DateOnly(2026, 9, 5), result.EarliestFinalRepaymentDate);
        Assert.Equal("Normal", result.Status);
        var unknown = new RepaymentMaturityAggregator().Evaluate(new[] { one, new MarginLotMaturityInput(12, "Open", Array.Empty<MarginLotContractTermRevision>()) }, new DateOnly(2026, 9, 1), new RiskManagementParameters());
        Assert.Equal("Unknown", unknown.Status);
        Assert.Null(unknown.EarliestFinalRepaymentDate);
    }

    private static (Position Position, MarginLot Lot, TradeExecution Opening, RiskBasisSnapshot Basis) Opening(string side, decimal price, decimal atr)
    {
        var position = new Position { PositionId = 1, Side = side, Status = "Open" };
        var execution = new TradeExecution { TradeExecutionId = 2, PositionId = 1, ExecutionRole = "Open", Status = "Effective", Price = price, Currency = "JPY", ExecutedAt = new DateTimeOffset(Utc(8)) };
        var lot = new MarginLot { MarginLotId = 10, PositionId = 1, OpeningTradeExecutionId = 2, CurrentQuantity = 200m, Status = "Open" };
        var basis = new RiskBasisSnapshot { RiskBasisId = 4, MarginLotId = 10, EntryBasisPrice = price, Currency = "JPY", AtrBasis = atr, AtrReferenceBarDate = new DateOnly(2026, 8, 28), AtrPeriod = 14, AtrAlgorithmVersion = "atr-wilder-v1", PriceUnitBasisSha256 = "unit", CorporateActionSetHash = "actions", ContentSha256 = "content", StrategyParameterSnapshotId = 1 };
        return (position, lot, execution, basis);
    }

    private static RiskPlan Plan(int id, int revision, decimal stop, decimal target, RiskBasisSnapshot? basis = null) => new() { RiskPlanId = id, MarginLotId = 10, Revision = revision, RiskBasisId = basis?.RiskBasisId ?? 4, RiskBasis = basis ?? new RiskBasisSnapshot { RiskBasisId = 4, MarginLotId = 10, EntryBasisPrice = 100m }, StopPrice = stop, TakeProfitPrice = target, PartialTakeProfitFraction = .5m, EffectiveAtUtc = Utc(8), RecordedAtUtc = Utc(8), Status = RiskVocabulary.Effective };
    private static HoldingBar Bar(decimal low, decimal high) => new(1, new DateOnly(2026, 9, 1), 100m, high, low, 105m);
    private static LotHoldingRiskResult LotResult(int id, string outcome, string? decision, string partialStatus, decimal? quantity) => new(id, outcome, decision, false, false, TargetReachState.NotReached, new TechnicalReversalEvaluation(RiskVocabulary.NotMatched, RiskVocabulary.NotMatched, RiskVocabulary.NotMatched), new PartialExitQuantityProposal(id, partialStatus, quantity, null, null), "test");
    private static MarginCostLedgerEntry Ledger(int id, string type, string status, string direction, decimal? amount) => new() { LedgerEntryId = id, MarginLotId = 10, CostType = type, Status = status, Direction = direction, Amount = amount, Currency = "JPY", Revision = 1 };
    private static MarginLotContractTermRevision Term(int id, int lotId, DateOnly date) => new() { ContractTermRevisionId = id, MarginLotId = lotId, TermType = "FixedDate", FinalRepaymentDate = date, Status = "Active" };
    private static DateTime Utc(int hour) => new(2026, 9, 1, hour, 0, 0, DateTimeKind.Utc);
}
