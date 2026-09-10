using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Application.Positions;

/// <summary>
/// Registers only user-confirmed executions. It has no market-price, signal-date, or order-placement path.
/// </summary>
public sealed class ManualTradeRegistrationService(IManualTradeRegistrationStore store)
{
    private static readonly TimeSpan JapanStandardTimeOffset = TimeSpan.FromHours(9);

    public async Task<ManualTradeRegistrationResult> RegisterOpenAsync(ManualOpenTradeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateManualFields(request.UserConfirmed, request.ExecutedAt, request.Price, request.Quantity, request.Currency);
        if (request.InstrumentId <= 0 || !IsSide(request.Side))
            throw new ArgumentException("A valid instrument and Long/Short side are required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppliedStrategyKey) || string.IsNullOrWhiteSpace(request.AppliedStrategyVersion))
            throw new ArgumentException("The applied strategy key and version must be explicitly recorded.", nameof(request));

        var candidateRiskSource = request.CandidateResultId is { } candidateResultId
            ? await store.GetCandidateRiskPlanSourceAsync(candidateResultId, cancellationToken)
                ?? throw new InvalidOperationException("The candidate's ATR basis is unavailable. Refresh analysis and select the candidate again.")
            : null;
        if (candidateRiskSource is not null) ValidateCandidateRiskSource(candidateRiskSource, request);

        Position position;
        if (request.ExistingPositionId is { } existingPositionId)
        {
            position = await store.FindPositionAsync(existingPositionId, cancellationToken)
                ?? throw new InvalidOperationException("The selected position no longer exists.");
            if (position.Status != RiskVocabulary.Open || position.InstrumentId != request.InstrumentId || position.Side != request.Side)
                throw new InvalidOperationException("A new lot can only be added to an open position with the same instrument and side.");
        }
        else
        {
            position = new Position
            {
                InstrumentId = request.InstrumentId,
                Side = request.Side,
                Status = RiskVocabulary.Open,
                AppliedStrategyKey = request.AppliedStrategyKey.Trim(),
                AppliedStrategyVersion = request.AppliedStrategyVersion.Trim(),
                Memo = NullIfWhiteSpace(request.Memo),
                SourceCandidateResultId = request.CandidateResultId,
                OpenedAtUtc = DateTime.UtcNow,
            };
        }

        var enteredAtUtc = DateTime.UtcNow;
        var execution = new TradeExecution
        {
            Position = position,
            ExecutionRole = RiskVocabulary.Open,
            ExecutedAt = request.ExecutedAt,
            Price = request.Price,
            Quantity = request.Quantity,
            Currency = request.Currency.Trim(),
            EnteredAtUtc = enteredAtUtc,
            PrefilledFromCandidateResultId = request.CandidateResultId,
            Notes = NullIfWhiteSpace(request.Notes),
            Revision = 1,
            Status = RiskVocabulary.Effective,
        };
        var lot = new MarginLot
        {
            Position = position,
            OpeningTradeExecution = execution,
            OpenedQuantity = request.Quantity,
            CurrentQuantity = request.Quantity,
            Status = RiskVocabulary.Open,
        };

        RiskBasisSnapshot? riskBasis = null;
        RiskPlan? riskPlan = null;
        if (candidateRiskSource is not null)
        {
            var recordedAtUtc = enteredAtUtc;
            riskBasis = CreateCandidateRiskBasis(candidateRiskSource, lot, execution, recordedAtUtc);
            riskPlan = new InitialRiskPlanFactory().Create(
                new InitialRiskPlanRequest(position, lot, execution, riskBasis, recordedAtUtc),
                new RiskManagementParameters()).RiskPlan;
            riskBasis.MarginLot = lot;
            riskPlan.MarginLot = lot;
        }

        store.AddOpening(position, execution, lot, riskBasis, riskPlan);
        await store.SaveChangesAsync(cancellationToken);
        return new ManualTradeRegistrationResult(position.PositionId, execution.TradeExecutionId, new[] { lot.MarginLotId });
    }

    public async Task<ManualTradeRegistrationResult> RegisterCloseAsync(ManualCloseTradeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateManualFields(request.UserConfirmed, request.ExecutedAt, request.Price, request.Quantity, request.Currency);
        if (request.PositionId <= 0) throw new ArgumentOutOfRangeException(nameof(request), "A position must be selected.");
        if (request.Allocations is null || request.Allocations.Count == 0)
            throw new ArgumentException("Close registrations require an explicit lot allocation.", nameof(request));
        if (request.Allocations.Any(allocation => allocation.MarginLotId <= 0 || allocation.Quantity <= 0) ||
            request.Allocations.Select(allocation => allocation.MarginLotId).Distinct().Count() != request.Allocations.Count)
            throw new ArgumentException("Each allocated lot must appear once with a positive quantity.", nameof(request));
        if (request.Allocations.Sum(allocation => allocation.Quantity) != request.Quantity)
            throw new ArgumentException("The lot allocation total must exactly equal the manually entered close quantity.", nameof(request));

        var position = await store.FindPositionAsync(request.PositionId, cancellationToken)
            ?? throw new InvalidOperationException("The selected position no longer exists.");
        if (position.Status != RiskVocabulary.Open || !IsSide(position.Side))
            throw new InvalidOperationException("Only an open Long or Short position can receive a close execution.");

        var lots = await store.GetOpenLotsAsync(position.PositionId, cancellationToken);
        var lotsById = lots.ToDictionary(lot => lot.MarginLotId);
        foreach (var allocation in request.Allocations)
        {
            if (!lotsById.TryGetValue(allocation.MarginLotId, out var lot) || lot.Status != RiskVocabulary.Open || allocation.Quantity > lot.CurrentQuantity)
                throw new InvalidOperationException("Every allocation must reference an open lot of this position and cannot exceed its remaining quantity.");
        }

        var enteredAtUtc = DateTime.UtcNow;
        var execution = new TradeExecution
        {
            Position = position,
            ExecutionRole = "Close",
            ExecutedAt = request.ExecutedAt,
            Price = request.Price,
            Quantity = request.Quantity,
            Currency = request.Currency.Trim(),
            EnteredAtUtc = enteredAtUtc,
            Notes = NullIfWhiteSpace(request.Notes),
            Revision = 1,
            Status = RiskVocabulary.Effective,
        };
        var allocations = request.Allocations.Select(input => new TradeExecutionLotAllocation
        {
            TradeExecution = execution,
            MarginLot = lotsById[input.MarginLotId],
            Quantity = input.Quantity,
            EffectiveAtUtc = request.ExecutedAt.UtcDateTime,
            RecordedAtUtc = enteredAtUtc,
            Revision = 1,
            Status = RiskVocabulary.Effective,
        }).ToArray();

        foreach (var allocation in allocations)
        {
            allocation.MarginLot.CurrentQuantity -= allocation.Quantity;
            if (allocation.MarginLot.CurrentQuantity == 0m) allocation.MarginLot.Status = RiskVocabulary.Closed;
        }
        if (lots.All(lot => lot.CurrentQuantity == 0m))
        {
            position.Status = RiskVocabulary.Closed;
            position.ClosedAtUtc = request.ExecutedAt.UtcDateTime;
        }

        store.AddClose(execution, allocations);
        await store.SaveChangesAsync(cancellationToken);
        return new ManualTradeRegistrationResult(position.PositionId, execution.TradeExecutionId, allocations.Select(allocation => allocation.MarginLotId).ToArray());
    }

    private static void ValidateManualFields(bool userConfirmed, DateTimeOffset executedAt, decimal price, int quantity, string currency)
    {
        if (!userConfirmed) throw new InvalidOperationException("The user must confirm the entered execution details before saving.");
        if (executedAt.Offset != JapanStandardTimeOffset) throw new ArgumentException("Execution time must be explicitly entered as JST.", nameof(executedAt));
        if (price <= 0 || quantity <= 0 || string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Execution price, quantity, and currency must be explicitly entered and positive.");
    }

    private static bool IsSide(string side) => side is RiskVocabulary.Long or RiskVocabulary.Short;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateCandidateRiskSource(CandidateRiskPlanSource source, ManualOpenTradeRequest request)
    {
        if (source.InstrumentId != request.InstrumentId || source.IndicatorInstrumentId != source.InstrumentId || source.Direction != request.Side || source.SignalPurpose != "Entry" || !source.Matched || source.IndicatorDataStatus != "Ok" ||
            source.IndicatorResultId <= 0 || source.Atr14 is null or <= 0 || source.EvaluationBarDate == default || source.AnalyzedAtUtc.Kind != DateTimeKind.Utc ||
            source.StrategyParameterSnapshotId <= 0 || string.IsNullOrWhiteSpace(source.CorporateActionSetHash))
            throw new InvalidOperationException("The selected candidate does not contain a complete, matching ATR risk basis.");
        if (source.AnalyzedAtUtc > request.ExecutedAt.UtcDateTime)
            throw new InvalidOperationException("A candidate analyzed after the entered execution time cannot be used as its ATR basis.");
    }

    private static RiskBasisSnapshot CreateCandidateRiskBasis(CandidateRiskPlanSource source, MarginLot lot, TradeExecution execution, DateTime recordedAtUtc)
    {
        var priceUnitBasis = Hash(new { schemaVersion = "price-unit-basis-v1", source.InstrumentId, execution.Currency, source.CorporateActionSetHash });
        var content = Hash(new
        {
            schemaVersion = "risk-basis-v1",
            source.CandidateResultId,
            source.IndicatorResultId,
            source.InstrumentId,
            execution.Price,
            execution.Currency,
            atrBasis = source.Atr14,
            source.EvaluationBarDate,
            atrPeriod = new TechnicalStrategyParameters().AtrPeriod,
            atrAlgorithm = "atr-wilder-v1",
            priceUnitBasis,
            source.StrategyParameterSnapshotId,
            source.CorporateActionSetHash,
        });
        return new RiskBasisSnapshot
        {
            MarginLotId = lot.MarginLotId,
            EntryBasisPrice = execution.Price,
            Currency = execution.Currency,
            AtrBasis = source.Atr14!.Value,
            AtrReferenceBarDate = source.EvaluationBarDate,
            AtrPeriod = new TechnicalStrategyParameters().AtrPeriod,
            AtrAlgorithmVersion = "atr-wilder-v1",
            PriceUnitBasisSha256 = priceUnitBasis,
            SourceCandidateResultId = source.CandidateResultId,
            SourceIndicatorResultId = source.IndicatorResultId,
            StrategyParameterSnapshotId = source.StrategyParameterSnapshotId,
            CorporateActionSetHash = source.CorporateActionSetHash,
            ContentSha256 = content,
            CreatedAtUtc = recordedAtUtc,
        };
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
