using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Infrastructure.Positions;

namespace SwingAdviser.Infrastructure.Tests;

public class ManualPositionWorkflowTests
{
    [Fact]
    public async Task ManualOpenAndExplicitClose_CreateAuditRecordsWithoutInferringLots()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var instrument = await AddInstrumentAsync(context);
        var service = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
        var executedAt = new DateTimeOffset(2026, 8, 31, 10, 15, 0, TimeSpan.FromHours(9));

        var open = await service.RegisterOpenAsync(new ManualOpenTradeRequest(instrument.InstrumentId, "Long", executedAt, 100m, 200, "JPY", "manual-v1", "v1", UserConfirmed: true));
        var lot = await context.MarginLots.SingleAsync();

        await service.RegisterCloseAsync(new ManualCloseTradeRequest(open.PositionId, executedAt.AddDays(1), 110m, 80, "JPY", [new ManualLotAllocationInput(lot.MarginLotId, 80m)], UserConfirmed: true));

        Assert.Equal(120m, lot.CurrentQuantity);
        Assert.Equal("Open", lot.Status);
        var close = await context.TradeExecutions.SingleAsync(execution => execution.ExecutionRole == "Close");
        Assert.Equal(80, close.Quantity);
        Assert.Single(await context.TradeExecutionLotAllocations.ToListAsync());
        Assert.Equal(200, (await context.TradeExecutions.SingleAsync(execution => execution.ExecutionRole == "Open")).Quantity);
    }

    [Fact]
    public async Task CloseWithMissingOrMismatchedAllocations_IsRejectedBeforeAnyExecutionIsSaved()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var instrument = await AddInstrumentAsync(context);
        var service = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
        var time = new DateTimeOffset(2026, 8, 31, 10, 15, 0, TimeSpan.FromHours(9));
        var open = await service.RegisterOpenAsync(new ManualOpenTradeRequest(instrument.InstrumentId, "Short", time, 100m, 100, "JPY", "manual-v1", "v1", UserConfirmed: true));
        var lot = await context.MarginLots.SingleAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterCloseAsync(new ManualCloseTradeRequest(open.PositionId, time.AddDays(1), 90m, 10, "JPY", [], UserConfirmed: true)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterCloseAsync(new ManualCloseTradeRequest(open.PositionId, time.AddDays(1), 90m, 10, "JPY", [new ManualLotAllocationInput(lot.MarginLotId, 9m)], UserConfirmed: true)));

        Assert.Single(await context.TradeExecutions.ToListAsync());
        Assert.Equal(100m, lot.CurrentQuantity);
    }

    [Fact]
    public async Task OpenWithoutUserConfirmation_IsRejectedBeforeAnyPositionOrExecutionIsSaved()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var instrument = await AddInstrumentAsync(context);
        var service = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
        var executedAt = new DateTimeOffset(2026, 8, 31, 10, 15, 0, TimeSpan.FromHours(9));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterOpenAsync(
            new ManualOpenTradeRequest(instrument.InstrumentId, "Long", executedAt, 100m, 100, "JPY", "manual-v1", "v1", UserConfirmed: false)));

        Assert.Empty(await context.Positions.ToListAsync());
        Assert.Empty(await context.TradeExecutions.ToListAsync());
        Assert.Empty(await context.MarginLots.ToListAsync());
    }

    [Fact]
    public async Task SplitCreatesSeparateAdjustmentAndPreservesOriginalExecution()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var instrument = await AddInstrumentAsync(context);
        var tradeService = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
        var time = new DateTimeOffset(2026, 8, 20, 10, 15, 0, TimeSpan.FromHours(9));
        await tradeService.RegisterOpenAsync(new ManualOpenTradeRequest(instrument.InstrumentId, "Long", time, 1_000m, 100, "JPY", "manual-v1", "v1", UserConfirmed: true));
        var lot = await context.MarginLots.SingleAsync();
        var snapshot = new StrategyParameterSnapshot { StrategyKey = "manual-v1", StrategyVersion = "v1", IndicatorEngineVersion = "test", CandidateScoringEngineVersion = "test", NormalizedParametersJson = "{}", ContentSha256 = new string('a', 64), CreatedAtUtc = DateTime.UtcNow };
        context.Add(snapshot);
        await context.SaveChangesAsync();
        context.Add(new RiskBasisSnapshot { MarginLotId = lot.MarginLotId, EntryBasisPrice = 1_000m, Currency = "JPY", AtrBasis = 100m, AtrReferenceBarDate = new DateOnly(2026, 8, 20), AtrPeriod = 14, AtrAlgorithmVersion = "test", PriceUnitBasisSha256 = new string('b', 64), StrategyParameterSnapshotId = snapshot.StrategyParameterSnapshotId, CorporateActionSetHash = new string('c', 64), ContentSha256 = new string('d', 64), CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
        var basis = await context.RiskBasisSnapshots.SingleAsync();
        context.Add(new RiskPlan { MarginLotId = lot.MarginLotId, Revision = 1, PlanKind = "Initial", RiskBasisId = basis.RiskBasisId, StopPrice = 800m, TakeProfitPrice = 1_300m, PartialTakeProfitFraction = .5m, EffectiveAtUtc = time.UtcDateTime, RecordedAtUtc = time.UtcDateTime, Status = "Effective" });
        context.Add(new CorporateAction { InstrumentId = instrument.InstrumentId, ActionType = "Split", EffectiveDate = new DateOnly(2026, 8, 25), AnnouncedAtUtc = time.UtcDateTime, AvailableAtUtc = time.UtcDateTime, FirstObservedAtUtc = time.UtcDateTime, SplitRatioNumerator = 2, SplitRatioDenominator = 1, SourceEventId = "split-1", Source = "test", RecordedAtUtc = time.UtcDateTime, Revision = 1, Status = "Active" });
        await context.SaveChangesAsync();

        var result = await new CorporateActionPositionAdjustmentService(new EfCorporateActionPositionAdjustmentStore(context)).ApplyAsync(new DateOnly(2026, 8, 31), DateTime.UtcNow);

        var adjustment = await context.PositionCorporateActionAdjustments.SingleAsync();
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal("Applied", adjustment.ReconciliationStatus);
        Assert.Equal(2m, adjustment.Ratio);
        Assert.Equal(200m, lot.CurrentQuantity);
        Assert.Equal(500m, adjustment.CostBasisAfter);
        Assert.Equal(400m, adjustment.StopPriceAfter);
        Assert.Equal(1_000m, (await context.TradeExecutions.SingleAsync()).Price);
        Assert.Empty(await context.TradeExecutions.Where(execution => execution.ExecutionRole == "Close").ToListAsync());
    }

    [Fact]
    public async Task UnsupportedActionRequiresReconciliationAndIsIdempotent()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var instrument = await AddInstrumentAsync(context);
        var service = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
        var time = new DateTimeOffset(2026, 8, 20, 10, 15, 0, TimeSpan.FromHours(9));
        await service.RegisterOpenAsync(new ManualOpenTradeRequest(instrument.InstrumentId, "Long", time, 100m, 100, "JPY", "manual-v1", "v1", UserConfirmed: true));
        context.Add(new CorporateAction { InstrumentId = instrument.InstrumentId, ActionType = "Unsupported", EffectiveDate = new DateOnly(2026, 8, 25), AnnouncedAtUtc = time.UtcDateTime, AvailableAtUtc = time.UtcDateTime, FirstObservedAtUtc = time.UtcDateTime, SourceEventId = "unsupported-1", Source = "test", RecordedAtUtc = time.UtcDateTime, Revision = 1, Status = "ReconciliationRequired" });
        await context.SaveChangesAsync();
        var adjustmentService = new CorporateActionPositionAdjustmentService(new EfCorporateActionPositionAdjustmentStore(context));

        Assert.Equal(1, (await adjustmentService.ApplyAsync(new DateOnly(2026, 8, 31), DateTime.UtcNow)).ReconciliationRequiredCount);
        Assert.Equal(0, (await adjustmentService.ApplyAsync(new DateOnly(2026, 8, 31), DateTime.UtcNow)).ReconciliationRequiredCount);
        Assert.Equal("ReconciliationRequired", (await context.PositionCorporateActionAdjustments.SingleAsync()).ReconciliationStatus);
    }

    private static async Task<Instrument> AddInstrumentAsync(SwingAdviserDbContext context)
    {
        var instrument = new Instrument { FirstObservedAtUtc = DateTime.UtcNow };
        context.Instruments.Add(instrument);
        await context.SaveChangesAsync();
        return instrument;
    }

    private static SqliteConnection OpenMigratedConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        using var context = CreateContext(connection);
        context.Database.Migrate();
        return connection;
    }

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
}
