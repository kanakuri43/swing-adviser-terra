using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class QueryIndexSchemaTests
{
    [Fact]
    public void Step8Schema_CreatesQueryIndexesIncludingTheActiveTermPartialIndex()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = new SwingAdviserDbContext(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        context.Database.Migrate();

        Assert.Contains("ix_positions_status", GetIndexNames(connection, "positions"));
        Assert.Contains("ix_ai_check_attempts_status", GetIndexNames(connection, "ai_check_attempts"));
        Assert.Contains("ix_candidate_results_direction_score_instrument_id", GetIndexNames(connection, "candidate_results"));
        var termIndexSql = GetIndexSql(connection, "ix_margin_lot_contract_term_revisions_final_repayment_date");
        Assert.Contains("WHERE status = 'Active'", termIndexSql, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetIndexNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA index_list({table});"; using var reader = command.ExecuteReader(); var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1)); return names;
    }

    private static string GetIndexSql(SqliteConnection connection, string indexName)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name"; command.Parameters.AddWithValue("$name", indexName);
        return (string?)command.ExecuteScalar() ?? throw new InvalidOperationException($"Index '{indexName}' was not found.");
    }
}
