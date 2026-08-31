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
        var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_TIMEOUT_SECONDS"), out var configuredTimeout) ? configuredTimeout : 120;
        var parallelism = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_MAX_CONCURRENCY"), out var configuredParallelism) ? configuredParallelism : 2;
        var topCount = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_AI_AUTO_TOP_COUNT"), out var configuredTopCount) ? configuredTopCount : 3;
        var arguments = (Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_ARGUMENTS") ?? string.Empty).Split('\u001f', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = new AiCheckOptions(Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_PATH") ?? "codex", Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_WORKING_DIRECTORY"), Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_MODEL"), TimeSpan.FromSeconds(timeoutSeconds), arguments, parallelism, bool.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_AI_AUTO_ENABLED"), out var automatic) && automatic, topCount);
        return new RuntimeAiCheckServices(new AiCheckQueueService(RuntimeSwingAdviserDbContextFactory.CreateContext, new CodexCliExecutor(), options));
    }
}
