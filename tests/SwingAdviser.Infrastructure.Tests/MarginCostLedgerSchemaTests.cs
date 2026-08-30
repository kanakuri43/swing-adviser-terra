using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class MarginCostLedgerSchemaTests
{
    [Fact]
    public void Step6Schema_StoresMoneyAndRatesAsNullableText()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = CreateContext(connection);
        context.Database.Migrate();

        Assert.Equal("TEXT", GetColumnType(connection, "margin_cost_ledger_entries", "amount"));
        Assert.Equal("TEXT", GetColumnType(connection, "margin_cost_ledger_entries", "rate"));
        Assert.Equal("TEXT", GetColumnType(connection, "margin_cost_ledger_entries", "period_start"));
        Assert.Contains("ix_margin_cost_ledger_entries_margin_lot_id_cost_type_period_start_period_end_revision", GetIndexNames(connection));
    }

    private static string GetColumnType(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == columnName) return reader.GetString(2);
        }

        throw new InvalidOperationException($"Column '{columnName}' was not found.");
    }

    private static IReadOnlyList<string> GetIndexNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA index_list(margin_cost_ledger_entries);";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));
        return names;
    }

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        return new SwingAdviserDbContext(options);
    }
}
