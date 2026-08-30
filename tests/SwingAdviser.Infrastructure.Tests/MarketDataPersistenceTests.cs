using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class MarketDataPersistenceTests
{
    [Fact]
    public void Step1Schema_RoundTripsAppendOnlyMarketData_UsingTextForDecimalValues()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 9, 15, 0, DateTimeKind.Utc);

        var instrument = new Instrument { FirstObservedAtUtc = observedAt };
        context.Instruments.Add(instrument);
        context.SaveChanges();

        var masterRevision = new InstrumentMasterRevision
        {
            InstrumentId = instrument.InstrumentId,
            Code = "7203",
            Name = "Test Instrument",
            MarketSegment = "Prime",
            InstrumentType = "DomesticCommonStock",
            ListedStatus = "Listed",
            ScanEligibility = "Eligible",
            EffectiveAtDate = new DateOnly(2026, 8, 30),
            AvailableAtUtc = observedAt,
            Source = "JPX-ListedIssues",
            SourceFileHash = "source-hash",
            RecordedAtUtc = observedAt,
            Revision = 1,
            Status = "Active",
        };
        var marginRevision = new MarginRegulationRevision
        {
            InstrumentId = instrument.InstrumentId,
            SystemMarginEligible = "Eligible",
            GeneralMarginEligible = "Unknown",
            ShortSellEligible = "NotEligible",
            EffectiveAtDate = new DateOnly(2026, 8, 30),
            AvailableAtUtc = observedAt,
            Source = "JPX-MarginIssues",
            RecordedAtUtc = observedAt,
            Revision = 1,
            Status = "Active",
        };
        var dailyBar = new DailyBar
        {
            InstrumentId = instrument.InstrumentId,
            TradingDate = new DateOnly(2026, 8, 28),
            Open = 1234.5678m,
            High = 1250m,
            Low = 1200m,
            Close = 1240.125m,
            Volume = 123456789,
            AdjClose = null,
            Source = "YahooFinanceChartApiV8",
            FetchedAtUtc = observedAt,
            Revision = 1,
            Status = "Final",
        };
        var corporateAction = new CorporateAction
        {
            InstrumentId = instrument.InstrumentId,
            ActionType = "CashDividend",
            EffectiveDate = new DateOnly(2026, 9, 30),
            AnnouncedAtUtc = observedAt,
            AvailableAtUtc = observedAt,
            FirstObservedAtUtc = observedAt,
            DividendAmountPerShare = 42.5m,
            Currency = "JPY",
            SourceEventId = "dividend-2026-09",
            Source = "Provider",
            RecordedAtUtc = observedAt,
            Revision = 1,
            Status = "Active",
        };
        var fundamentalSnapshot = new FundamentalDataSnapshot
        {
            InstrumentId = instrument.InstrumentId,
            FetchedAtUtc = observedAt,
            Source = "Provider",
            Per = null,
            Pbr = 1.25m,
            MarketCap = 123456789012.345m,
            DividendYield = null,
        };

        context.AddRange(masterRevision, marginRevision, dailyBar, corporateAction, fundamentalSnapshot);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var restored = context.DailyBars.Single();
        Assert.Equal(1234.5678m, restored.Open);
        Assert.Equal(new DateOnly(2026, 8, 28), restored.TradingDate);
        Assert.Null(restored.AdjClose);
        Assert.Null(context.FundamentalDataSnapshots.Single().Per);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(open), open, trading_date, fetched_at_utc FROM daily_bars";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal("1234.5678", reader.GetString(1));
        Assert.Equal("2026-08-28", reader.GetString(2));
        Assert.Equal("2026-08-30T09:15:00.0000000Z", reader.GetString(3));
    }

    [Fact]
    public void Step1Schema_RejectsDuplicateDailyBarRevisionAndOrphanInstrumentReference()
    {
        using var connection = OpenMigratedConnection();
        using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = observedAt };
        context.Instruments.Add(instrument);
        context.SaveChanges();

        context.DailyBars.Add(CreateDailyBar(instrument.InstrumentId, observedAt));
        context.SaveChanges();

        context.DailyBars.Add(CreateDailyBar(instrument.InstrumentId, observedAt));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        var orphanBar = CreateDailyBar(instrument.InstrumentId + 999, observedAt);
        orphanBar.Revision = 2;
        context.DailyBars.Add(orphanBar);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    private static DailyBar CreateDailyBar(int instrumentId, DateTime fetchedAtUtc) => new()
    {
        InstrumentId = instrumentId,
        TradingDate = new DateOnly(2026, 8, 28),
        Open = 100m,
        High = 101m,
        Low = 99m,
        Close = 100m,
        Volume = 1000,
        Source = "Test",
        FetchedAtUtc = fetchedAtUtc,
        Revision = 1,
        Status = "Final",
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
            .EnableSensitiveDataLogging()
            .Options;
        return new SwingAdviserDbContext(options);
    }
}
