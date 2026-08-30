using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class AiCheckSchemaTests
{
    [Fact]
    public void Step7Schema_CreatesAuditTablesAndUniqueResultOrderingKeys()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = new SwingAdviserDbContext(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        context.Database.Migrate();

        Assert.Equal("TEXT", GetColumnType(connection, "ai_check_attempts", "requested_at_utc"));
        Assert.Equal("INTEGER", GetColumnType(connection, "ai_check_attempts", "is_stale"));
        Assert.Equal("TEXT", GetColumnType(connection, "ai_check_sources", "retrieved_at_utc"));
        Assert.Contains("ix_ai_check_results_attempt_id", GetIndexNames(connection, "ai_check_results"));
        Assert.Contains("ix_ai_check_evidence_items_result_id_evidence_kind_ordinal", GetIndexNames(connection, "ai_check_evidence_items"));
        Assert.Contains("ix_ai_check_sources_result_id_ordinal", GetIndexNames(connection, "ai_check_sources"));
    }

    private static string GetColumnType(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA table_info({table});"; using var reader = command.ExecuteReader();
        while (reader.Read()) if (reader.GetString(1) == column) return reader.GetString(2);
        throw new InvalidOperationException($"Column '{column}' was not found.");
    }

    private static IReadOnlyList<string> GetIndexNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA index_list({table});"; using var reader = command.ExecuteReader(); var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1)); return names;
    }
}
