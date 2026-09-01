using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

public sealed class EfDailyUpdateOverviewReader(SwingAdviserDbContext context) : IDailyUpdateOverviewReader
{
    public async Task<DailyUpdateOverview> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var run = await context.DailyUpdateRuns.AsNoTracking()
            .OrderByDescending(item => item.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return new DailyUpdateOverview(null, "未実行", 0, Enum.GetValues<DailyUpdateStep>().Length, 0, 0, "日次分析更新はまだ実行されていません。", null, null);

        var summary = ReadSummary(run.StepSummaryJson);
        var detail = summary.Detail ?? (run.Status == "Running" ? "日次分析更新を実行しています。" : "保存済みの日次更新結果です。");
        return new DailyUpdateOverview(run.DailyUpdateRunId, DisplayStatus(run.Status), summary.CompletedSteps, Enum.GetValues<DailyUpdateStep>().Length,
            summary.SucceededCount, summary.FailedCount, detail, run.StartedAtUtc, run.CompletedAtUtc);
    }

    private static (int CompletedSteps, int SucceededCount, int FailedCount, string? Detail) ReadSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (0, 0, 0, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array) return (0, 0, 0, null);
            var values = steps.EnumerateArray().ToArray();
            var succeeded = values.Sum(item => item.TryGetProperty("succeeded", out var value) ? value.GetInt32() : 0);
            var failed = values.Sum(item => item.TryGetProperty("failed", out var value) ? value.GetInt32() : 0);
            var detail = values.Length == 0 ? null : values[^1].TryGetProperty("detail", out var last) ? last.GetString() : null;
            return (values.Length, succeeded, failed, detail);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { return (0, 0, 0, "更新進捗の保存内容を読み取れませんでした。履歴を確認してください。"); }
    }

    private static string DisplayStatus(string status) => status switch
    {
        "Running" => "実行中",
        "Succeeded" => "完了",
        "PartiallySucceeded" => "一部完了",
        "Failed" => "失敗",
        _ => status,
    };
}
