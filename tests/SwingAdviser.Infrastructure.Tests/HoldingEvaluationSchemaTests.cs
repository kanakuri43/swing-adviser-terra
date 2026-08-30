using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class HoldingEvaluationSchemaTests
{
    [Fact]
    public void Step5Schema_CreatesEvaluationTablesWithTextDecimalAndUniqueEvaluationKeys()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = CreateContext(connection);
        context.Database.Migrate();

        Assert.Equal("TEXT", GetColumnType(connection, "lot_holding_evaluations", "partial_exit_candidate_quantity"));
        Assert.Equal("TEXT", GetColumnType(connection, "lot_holding_evaluations", "evaluated_at_utc"));
        Assert.Equal("TEXT", GetColumnType(connection, "position_holding_evaluations", "partial_exit_total_candidate_quantity"));
        Assert.Contains("ix_lot_holding_evaluations_margin_lot_id_evaluation_bar_date_evaluated_at_utc", GetIndexNames(connection, "lot_holding_evaluations"));
        Assert.Contains("ix_position_holding_evaluations_position_id_evaluation_bar_date_evaluated_at_utc", GetIndexNames(connection, "position_holding_evaluations"));
    }

    private static string GetColumnType(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == columnName)
            {
                return reader.GetString(2);
            }
        }

        throw new InvalidOperationException($"Column '{columnName}' was not found in '{tableName}'.");
    }

    private static IReadOnlyList<string> GetIndexNames(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list({tableName});";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
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
