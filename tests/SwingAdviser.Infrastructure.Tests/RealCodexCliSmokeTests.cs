using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

/// <summary>
/// Opt-in verification of the installed, authenticated Codex CLI. It is deliberately excluded
/// from ordinary test runs because it consumes an external AI request.
/// </summary>
public class RealCodexCliSmokeTests
{
    [Fact]
    [Trait("Category", "External")]
    public async Task RealCli_QueuePersistsAUsableAiResult()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SWING_ADVISER_RUN_REAL_CLI_SMOKE"), "true", StringComparison.OrdinalIgnoreCase)) return;

        var databasePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-real-cli-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var migration = CreateContext(connectionString)) await migration.Database.MigrateAsync();
            var ids = await RepresentativeWorkflowEndToEndTests.SeedCandidateAsync(connectionString);
            var queue = new AiCheckQueueService(
                () => CreateContext(connectionString),
                new CodexCliExecutor(),
                new AiCheckOptions(CodexCliPathResolver.Resolve(), null, null, TimeSpan.FromMinutes(5), [], MaximumConcurrency: 1));

            Assert.Equal(1, (await queue.EnqueueUserAsync([ids.CandidateResultId], CancellationToken.None)).QueuedCount);

            for (var retry = 0; retry < 1_240; retry++)
            {
                await using var check = CreateContext(connectionString);
                if (await check.AiCheckAttempts.AnyAsync(item => item.Status != "Queued" && item.Status != "Running")) break;
                await Task.Delay(250);
            }

            await using var assertion = CreateContext(connectionString);
            var attempt = await assertion.AiCheckAttempts.SingleAsync();
            Assert.Contains(attempt.Status, ["Succeeded", "InsufficientInformation"]);
            Assert.Single(await assertion.AiCheckResults.ToListAsync());
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
        }
    }

    private static SwingAdviserDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connectionString).UseSnakeCaseNamingConvention().Options);
}
