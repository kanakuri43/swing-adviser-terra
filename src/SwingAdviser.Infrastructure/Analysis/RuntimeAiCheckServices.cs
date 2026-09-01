using SwingAdviser.Application.Analysis;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Analysis;

/// <summary>Desktop composition root for the persistent queue. Environment values are a deployment-time configuration boundary.</summary>
public sealed class RuntimeAiCheckServices
{
    private RuntimeAiCheckServices(AiCheckQueueService queue) => Queue = queue;
    public AiCheckQueueService Queue { get; }
    public static RuntimeAiCheckServices Create()
    {
        // A research check can require several source lookups. It runs off the UI thread and does
        // not block the core daily analysis, so prefer a realistic default over a false timeout.
        var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_TIMEOUT_SECONDS"), out var configuredTimeout) ? configuredTimeout : 300;
        var parallelism = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_MAX_CONCURRENCY"), out var configuredParallelism) ? configuredParallelism : 2;
        var topCount = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_AI_AUTO_TOP_COUNT"), out var configuredTopCount) ? configuredTopCount : 3;
        var arguments = (Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_ARGUMENTS") ?? string.Empty).Split('\u001f', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var automatic = !bool.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_AI_AUTO_ENABLED"), out var configuredAutomatic) || configuredAutomatic;
        var options = new AiCheckOptions(CodexCliPathResolver.Resolve(), Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_WORKING_DIRECTORY"), Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_MODEL"), TimeSpan.FromSeconds(timeoutSeconds), arguments, parallelism, automatic, topCount);
        return new RuntimeAiCheckServices(new AiCheckQueueService(RuntimeSwingAdviserDbContextFactory.CreateContext, new CodexCliExecutor(), options));
    }
}
