using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class PositionPersistenceTests
{
    [Fact]
    public void Step3Schema_PreservesManualExecutionInputsAndLotAuditHistory()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var recordedAt = new DateTime(2026, 8, 30, 2, 0, 0, DateTimeKind.Utc);
        var executedAt = new DateTimeOffset(2026, 8, 30, 10, 30, 0, TimeSpan.FromHours(9));
        var instrument = new Instrument { FirstObservedAtUtc = recordedAt };
        context.Instruments.Add(instrument);
        context.SaveChanges();

        var position = new Position
        {
            InstrumentId = instrument.InstrumentId,
            Side = "Long",
            Status = "Open",
            AppliedStrategyKey = "candidate-scoring",
            AppliedStrategyVersion = "v1",
            SourceCandidateResultId = null,
            OpenedAtUtc = recordedAt,
        };
        context.Positions.Add(position);
        context.SaveChanges();

        var openingExecution = new TradeExecution
        {
            PositionId = position.PositionId,
            ExecutionRole = "Open",
            ExecutedAt = executedAt,
            Price = 1234.5678m,
            Quantity = 100,
            Currency = "JPY",
            EnteredAtUtc = recordedAt,
            PrefilledFromCandidateResultId = null,
            Revision = 1,
            Status = "Effective",
        };
        context.TradeExecutions.Add(openingExecution);
        context.SaveChanges();

        var marginLot = new MarginLot
        {
            PositionId = position.PositionId,
            OpeningTradeExecutionId = openingExecution.TradeExecutionId,
            OpenedQuantity = 100,
            CurrentQuantity = 100m,
            Status = "Open",
        };
        context.MarginLots.Add(marginLot);
        context.SaveChanges();

        var closeExecution = new TradeExecution
        {
            PositionId = position.PositionId,
            ExecutionRole = "Close",
            ExecutedAt = executedAt.AddDays(1),
            Price = 1300.25m,
            Quantity = 40,
            Currency = "JPY",
            EnteredAtUtc = recordedAt.AddDays(1),
            Revision = 1,
            Status = "Effective",
        };
        var corporateAction = new CorporateAction
        {
            InstrumentId = instrument.InstrumentId,
            ActionType = "CashDividend",
            EffectiveDate = new DateOnly(2026, 9, 30),
            AnnouncedAtUtc = recordedAt,
            AvailableAtUtc = recordedAt,
            FirstObservedAtUtc = recordedAt,
            DividendAmountPerShare = 25m,
            Currency = "JPY",
            SourceEventId = "cash-dividend-1",
            Source = "Test",
            RecordedAtUtc = recordedAt,
            Revision = 1,
            Status = "Active",
        };
        context.AddRange(
            closeExecution,
            corporateAction,
            new MarginLotContractTermRevision
            {
                MarginLotId = marginLot.MarginLotId,
                MarginCategory = "System",
                Broker = "Example Broker",
                Product = "Margin",
                TermType = "Unknown",
                FinalRepaymentDate = null,
                ConfirmedAtUtc = recordedAt,
                RecordedAtUtc = recordedAt,
                Revision = 1,
                Status = "Active",
            });
        context.SaveChanges();

        context.AddRange(
            new TradeExecutionLotAllocation
            {
                TradeExecutionId = closeExecution.TradeExecutionId,
                MarginLotId = marginLot.MarginLotId,
                Quantity = 40m,
                EffectiveAtUtc = recordedAt.AddDays(1),
                RecordedAtUtc = recordedAt.AddDays(1),
                Revision = 1,
                Status = "Effective",
            },
            new PositionCorporateActionAdjustment
            {
                MarginLotId = marginLot.MarginLotId,
                CorporateActionId = corporateAction.CorporateActionId,
                QuantityBefore = 100m,
                QuantityAfter = 100m,
                CostBasisBefore = 1234.5678m,
                CostBasisAfter = 1234.5678m,
                ReconciliationStatus = "Applied",
                AppliedAtUtc = recordedAt,
            });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var restoredOpening = context.TradeExecutions.Single(execution => execution.ExecutionRole == "Open");
        Assert.Equal(1234.5678m, restoredOpening.Price);
        Assert.Equal(executedAt, restoredOpening.ExecutedAt);
        Assert.Null(restoredOpening.PrefilledFromCandidateResultId);
        Assert.Equal(100m, context.MarginLots.Single().CurrentQuantity);
        Assert.Single(context.MarginLotContractTermRevisions);
        Assert.Single(context.TradeExecutionLotAllocations);
        Assert.Single(context.PositionCorporateActionAdjustments);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(price), price, executed_at FROM trade_executions WHERE execution_role = 'Open'";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal("1234.5678", reader.GetString(1));
        Assert.Equal("2026-08-30T10:30:00.0000000+09:00", reader.GetString(2));
    }

    [Fact]
    public void Step3Schema_RejectsMultipleMarginLotsForOneOpeningExecution()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var recordedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = recordedAt };
        context.Instruments.Add(instrument);
        context.SaveChanges();
        var position = new Position
        {
            InstrumentId = instrument.InstrumentId,
            Side = "Short",
            Status = "Open",
            AppliedStrategyKey = "test",
            AppliedStrategyVersion = "v1",
            OpenedAtUtc = recordedAt,
        };
        context.Positions.Add(position);
        context.SaveChanges();
        var execution = new TradeExecution
        {
            PositionId = position.PositionId,
            ExecutionRole = "Open",
            ExecutedAt = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(9)),
            Price = 100m,
            Quantity = 100,
            Currency = "JPY",
            EnteredAtUtc = recordedAt,
            Revision = 1,
            Status = "Effective",
        };
        context.TradeExecutions.Add(execution);
        context.SaveChanges();
        context.MarginLots.Add(CreateMarginLot(position.PositionId, execution.TradeExecutionId));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.MarginLots.Add(CreateMarginLot(position.PositionId, execution.TradeExecutionId));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    private static MarginLot CreateMarginLot(int positionId, int openingTradeExecutionId) => new()
    {
        PositionId = positionId,
        OpeningTradeExecutionId = openingTradeExecutionId,
        OpenedQuantity = 100,
        CurrentQuantity = 100m,
        Status = "Open",
    };

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

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SwingAdviserDbContext(options);
    }
}
