using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class RiskPersistenceTests
{
    [Fact]
    public void Step4Schema_PersistsImmutableBasisAndAppendOnlyPlanRevisions()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var recordedAt = new DateTime(2026, 8, 30, 1, 0, 0, DateTimeKind.Utc);
        var (marginLot, strategySnapshot) = CreateMarginLotAndStrategySnapshot(context, recordedAt);
        var riskBasis = new RiskBasisSnapshot
        {
            MarginLotId = marginLot.MarginLotId,
            EntryBasisPrice = 1234.5678m,
            Currency = "JPY",
            AtrBasis = 25.125m,
            AtrReferenceBarDate = new DateOnly(2026, 8, 28),
            AtrPeriod = 14,
            AtrAlgorithmVersion = "atr-wilder-v1",
            PriceUnitBasisSha256 = "price-unit-hash",
            StrategyParameterSnapshotId = strategySnapshot.StrategyParameterSnapshotId,
            CorporateActionSetHash = "corporate-action-set-hash",
            ContentSha256 = "risk-basis-hash",
            CreatedAtUtc = recordedAt,
        };
        context.RiskBasisSnapshots.Add(riskBasis);
        context.SaveChanges();
        var initialPlan = new RiskPlan
        {
            MarginLotId = marginLot.MarginLotId,
            Revision = 1,
            PlanKind = "Initial",
            RiskBasisId = riskBasis.RiskBasisId,
            StopPrice = 1184.3178m,
            TakeProfitPrice = 1334.9678m,
            PartialTakeProfitFraction = 0.5m,
            EffectiveAtUtc = recordedAt,
            RecordedAtUtc = recordedAt,
            Status = "Effective",
        };
        context.RiskPlans.Add(initialPlan);
        context.SaveChanges();
        context.RiskPlans.Add(new RiskPlan
        {
            MarginLotId = marginLot.MarginLotId,
            Revision = 2,
            PlanKind = "PartialExitBreakeven",
            RiskBasisId = riskBasis.RiskBasisId,
            StopPrice = 1234.5678m,
            TakeProfitPrice = 1334.9678m,
            PartialTakeProfitFraction = 0.5m,
            EffectiveAtUtc = recordedAt.AddDays(1),
            RecordedAtUtc = recordedAt.AddDays(1),
            SupersedesRevisionId = initialPlan.RiskPlanId,
            Status = "Effective",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        Assert.Equal(1234.5678m, context.RiskBasisSnapshots.Single().EntryBasisPrice);
        Assert.Equal(25.125m, context.RiskBasisSnapshots.Single().AtrBasis);
        Assert.Equal(2, context.RiskPlans.Count());
        Assert.Equal(1, context.RiskPlans.Single(plan => plan.Revision == 2).SupersedesRevisionId);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(entry_basis_price), entry_basis_price, atr_reference_bar_date FROM risk_basis_snapshots";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal("1234.5678", reader.GetString(1));
        Assert.Equal("2026-08-28", reader.GetString(2));
    }

    [Fact]
    public void Step4Schema_RejectsDuplicateBasisAndPlanRevisionForOneLot()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var recordedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var (marginLot, strategySnapshot) = CreateMarginLotAndStrategySnapshot(context, recordedAt);
        var riskBasis = CreateRiskBasis(marginLot.MarginLotId, strategySnapshot.StrategyParameterSnapshotId, recordedAt);
        context.RiskBasisSnapshots.Add(riskBasis);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.RiskBasisSnapshots.Add(CreateRiskBasis(marginLot.MarginLotId, strategySnapshot.StrategyParameterSnapshotId, recordedAt));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        context.RiskPlans.Add(CreateRiskPlan(marginLot.MarginLotId, riskBasis.RiskBasisId, recordedAt));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        context.RiskPlans.Add(CreateRiskPlan(marginLot.MarginLotId, riskBasis.RiskBasisId, recordedAt));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    private static RiskBasisSnapshot CreateRiskBasis(int marginLotId, int strategyParameterSnapshotId, DateTime createdAtUtc) => new()
    {
        MarginLotId = marginLotId,
        EntryBasisPrice = 100m,
        Currency = "JPY",
        AtrBasis = 2m,
        AtrReferenceBarDate = new DateOnly(2026, 8, 28),
        AtrPeriod = 14,
        AtrAlgorithmVersion = "atr-wilder-v1",
        PriceUnitBasisSha256 = "price-unit",
        StrategyParameterSnapshotId = strategyParameterSnapshotId,
        CorporateActionSetHash = "corporate-action-set",
        ContentSha256 = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = createdAtUtc,
    };

    private static RiskPlan CreateRiskPlan(int marginLotId, int riskBasisId, DateTime recordedAtUtc) => new()
    {
        MarginLotId = marginLotId,
        Revision = 1,
        PlanKind = "Initial",
        RiskBasisId = riskBasisId,
        StopPrice = 96m,
        TakeProfitPrice = 108m,
        PartialTakeProfitFraction = 0.5m,
        EffectiveAtUtc = recordedAtUtc,
        RecordedAtUtc = recordedAtUtc,
        Status = "Effective",
    };

    private static (MarginLot MarginLot, StrategyParameterSnapshot StrategySnapshot) CreateMarginLotAndStrategySnapshot(
        SwingAdviserDbContext context,
        DateTime recordedAtUtc)
    {
        var instrument = new Instrument { FirstObservedAtUtc = recordedAtUtc };
        context.Instruments.Add(instrument);
        context.SaveChanges();
        var position = new Position
        {
            InstrumentId = instrument.InstrumentId,
            Side = "Long",
            Status = "Open",
            AppliedStrategyKey = "test",
            AppliedStrategyVersion = "v1",
            OpenedAtUtc = recordedAtUtc,
        };
        context.Positions.Add(position);
        context.SaveChanges();
        var execution = new TradeExecution
        {
            PositionId = position.PositionId,
            ExecutionRole = "Open",
            ExecutedAt = new DateTimeOffset(recordedAtUtc, TimeSpan.Zero),
            Price = 100m,
            Quantity = 100,
            Currency = "JPY",
            EnteredAtUtc = recordedAtUtc,
            Revision = 1,
            Status = "Effective",
        };
        context.TradeExecutions.Add(execution);
        context.SaveChanges();
        var marginLot = new MarginLot
        {
            PositionId = position.PositionId,
            OpeningTradeExecutionId = execution.TradeExecutionId,
            OpenedQuantity = 100,
            CurrentQuantity = 100m,
            Status = "Open",
        };
        var strategySnapshot = new StrategyParameterSnapshot
        {
            StrategyKey = "test",
            StrategyVersion = "v1",
            IndicatorEngineVersion = "test",
            CandidateScoringEngineVersion = "test",
            NormalizedParametersJson = "{}",
            ContentSha256 = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = recordedAtUtc,
        };
        context.AddRange(marginLot, strategySnapshot);
        context.SaveChanges();
        return (marginLot, strategySnapshot);
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

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SwingAdviserDbContext(options);
    }
}
