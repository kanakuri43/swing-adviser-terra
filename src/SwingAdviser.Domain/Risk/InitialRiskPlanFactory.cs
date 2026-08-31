using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

public sealed record InitialRiskPlanRequest(Position Position, MarginLot MarginLot, TradeExecution OpeningExecution, RiskBasisSnapshot RiskBasis, DateTime RecordedAtUtc);
public sealed record InitialRiskPlanResult(RiskBasisSnapshot RiskBasis, RiskPlan RiskPlan);

/// <summary>Creates the immutable initial basis and plan for a user-confirmed opening lot.</summary>
public sealed class InitialRiskPlanFactory
{
    public InitialRiskPlanResult Create(InitialRiskPlanRequest request, RiskManagementParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        var position = request.Position;
        var lot = request.MarginLot;
        var execution = request.OpeningExecution;
        var basis = request.RiskBasis;
        if (position.Status != RiskVocabulary.Open || lot.Status != RiskVocabulary.Open || lot.CurrentQuantity <= 0 ||
            (position.Side != RiskVocabulary.Long && position.Side != RiskVocabulary.Short) ||
            execution.ExecutionRole != RiskVocabulary.Open || execution.Status != RiskVocabulary.Effective ||
            lot.PositionId != position.PositionId || execution.PositionId != position.PositionId || lot.OpeningTradeExecutionId != execution.TradeExecutionId ||
            basis.MarginLotId != lot.MarginLotId || basis.EntryBasisPrice != execution.Price || !string.Equals(basis.Currency, execution.Currency, StringComparison.Ordinal) ||
            basis.EntryBasisPrice <= 0 || basis.AtrBasis <= 0 || basis.AtrPeriod <= 0 || basis.AtrReferenceBarDate == default ||
            string.IsNullOrWhiteSpace(basis.Currency) || string.IsNullOrWhiteSpace(basis.AtrAlgorithmVersion) || string.IsNullOrWhiteSpace(basis.PriceUnitBasisSha256) ||
            string.IsNullOrWhiteSpace(basis.CorporateActionSetHash) || string.IsNullOrWhiteSpace(basis.ContentSha256) || basis.StrategyParameterSnapshotId <= 0)
            throw new ArgumentException("Opening execution, lot, position, and frozen risk basis must be complete and mutually consistent.", nameof(request));

        try
        {
            var multiplier = position.Side == RiskVocabulary.Long ? parameters.LongInitialStopAtrMultiple : parameters.ShortInitialStopAtrMultiple;
            var risk = checked(multiplier * basis.AtrBasis);
            var targetOffset = checked(risk * parameters.PartialTakeProfitRiskMultiple);
            var stop = position.Side == RiskVocabulary.Long ? checked(basis.EntryBasisPrice - risk) : checked(basis.EntryBasisPrice + risk);
            var target = position.Side == RiskVocabulary.Long ? checked(basis.EntryBasisPrice + targetOffset) : checked(basis.EntryBasisPrice - targetOffset);
            if (stop <= 0 || target <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Calculated risk lines must be positive.");
            var plan = new RiskPlan
            {
                MarginLotId = lot.MarginLotId,
                Revision = 1,
                PlanKind = RiskVocabulary.Initial,
                RiskBasisId = basis.RiskBasisId,
                RiskBasis = basis,
                StopPrice = stop,
                TakeProfitPrice = target,
                PartialTakeProfitFraction = parameters.PartialTakeProfitFraction,
                EffectiveAtUtc = execution.ExecutedAt.UtcDateTime,
                RecordedAtUtc = request.RecordedAtUtc,
                Status = RiskVocabulary.Effective,
            };
            return new InitialRiskPlanResult(basis, plan);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Risk-line calculation overflowed decimal range.", exception.Message);
        }
    }
}
