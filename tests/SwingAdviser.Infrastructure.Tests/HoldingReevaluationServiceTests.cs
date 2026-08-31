using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Infrastructure.Tests;

public class HoldingReevaluationServiceTests
{
    [Fact]
    public async Task CorporateActionReconciliationFailsClosedAndPersistsNoTradingAction()
    {
        var position = new Position { PositionId = 1, Side = "Long", Status = "Open" };
        var lot = new MarginLot { MarginLotId = 2, PositionId = 1, Position = position, CurrentQuantity = 100m, Status = "Open" };
        var store = new FakeStore([new HoldingLotReevaluationInput(position, lot, null, [], [], null, false, true)]);

        var result = await new HoldingReevaluationService(store).ReevaluateAsync(new DateOnly(2026, 8, 31), new DateTime(2026, 8, 31, 7, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, result.IndeterminatePositionCount);
        Assert.Null(result.Positions.Single().Outcome.Decision);
        Assert.Equal("ReconciliationRequired", result.Positions.Single().Outcome.EvaluationOutcome);
        Assert.Single(store.Saved!);
        Assert.Equal(100m, lot.CurrentQuantity);
    }

    private sealed class FakeStore(IReadOnlyList<HoldingLotReevaluationInput> inputs) : IHoldingReevaluationStore
    {
        public IReadOnlyList<HoldingReevaluationPositionOutcome>? Saved { get; private set; }
        public Task<IReadOnlyList<HoldingLotReevaluationInput>> LoadOpenLotsAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, CancellationToken cancellationToken) => Task.FromResult(inputs);
        public Task SaveAsync(DateOnly evaluationBarDate, DateTime evaluatedAtUtc, IReadOnlyList<HoldingReevaluationPositionOutcome> outcomes, CancellationToken cancellationToken) { Saved = outcomes; return Task.CompletedTask; }
    }
}
