using System.Collections.ObjectModel;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// Phase 4, 8, and 10 layout-only data source. Production data access is deliberately deferred.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    public string Title => "Swing Adviser — 日本株スイング判断支援";

    public string SafetyNotice =>
        "分析結果は参考情報です。注文・自動売買は行いません。約定は証券会社の通知を確認し、利用者が入力・確認した内容だけを保存します。";

    private string _statusMessage = "実データを読み込んでいます。日次分析更新は利用者が明示的に開始します。";
    private UpdateProgressRow _dailyUpdateProgress = new("未実行", 0, false, "日次分析更新はまだ実行されていません。", "更新すると、外部データ取得・分析・候補生成・保有再評価を実行します。");
    private UpdateProgressRow _aiQueueProgress = new("未実行", 0, false, "AIチェックはまだありません。", "AIチェックは日次分析の完了を待たず、設定で有効な場合だけ自動投入します。");
    private bool _isUpdateRunning;
    private bool _isDisplayLoading;
    private CandidateRow? _selectedCandidate;
    private string _dailyUpdateDetailBeforeHeartbeat = "日次分析更新はまだ実行されていません。";

    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public UpdateProgressRow DailyUpdateProgress { get => _dailyUpdateProgress; private set => Set(ref _dailyUpdateProgress, value); }
    public UpdateProgressRow AiQueueProgress { get => _aiQueueProgress; private set => Set(ref _aiQueueProgress, value); }
    public bool IsUpdateRunning { get => _isUpdateRunning; private set => Set(ref _isUpdateRunning, value); }
    public bool IsDisplayLoading { get => _isDisplayLoading; private set => Set(ref _isDisplayLoading, value); }
    public CandidateRow? SelectedCandidate { get => _selectedCandidate; set => Set(ref _selectedCandidate, value); }

    public ObservableCollection<CandidateRow> Candidates { get; } = [];
    public ObservableCollection<PositionRow> Positions { get; } = [];
    public ObservableCollection<ExecutionRow> Executions { get; } = [];

    public async Task ReloadManualRecordsAsync(SwingAdviser.Application.Positions.IManualPositionOverviewReader reader)
    {
        var positions = await reader.GetOpenPositionsAsync();
        var executions = await reader.GetExecutionsAsync();
        Positions.Clear();
        foreach (var position in positions)
        {
            var evaluated = position.EvaluationDecision is not null;
            var evaluationDetail = evaluated ? $"{position.EvaluationOutcome ?? "評価済み"}。保存済みの保有再評価結果です。" : "日次更新後に保有再評価を表示します。";
            if (position.LatestClose is not null) evaluationDetail += $" 最新確定終値: {position.LatestClose:n4}円（{position.LatestBarDate:yyyy-MM-dd}）。";
            Positions.Add(new PositionRow(position.Code, position.Name, position.Side, $"{position.CurrentQuantity:n0}株", position.Strategy,
                position.EvaluationDecision ?? "未評価", position.EvaluationBarDate?.ToString("yyyy-MM-dd") ?? "未評価", evaluationDetail,
                Amount(position.StopCandidate), Amount(position.TakeProfitCandidate), position.EvaluationDecision == "Hold" ? evaluationDetail : "HOLD理由なし/未評価",
                position.EarliestRepaymentDate?.ToString("yyyy-MM-dd") ?? "期限未確認", Amount(position.ConfirmedCost), "未算定", "未算定", Amount(position.ReferenceNetProfitAndLoss), position.ReconciliationStatus, position.PositionId, Amount(position.LatestClose)));
        }
        Executions.Clear();
        foreach (var execution in executions)
        {
            Executions.Add(new ExecutionRow(execution.Code, execution.Name, execution.Side, execution.ExecutionRole == "Open" ? "新規" : "決済", execution.RegistrationSource, execution.ExecutedAt.ToString("yyyy-MM-dd HH:mm zzz"), $"{execution.Price:n4}円", $"{execution.Quantity:n0}株", $"Rev {execution.Revision}（{execution.Status}）", execution.Notes ?? "利用者確認済みの約定監査原票です。", execution.TradeExecutionId));
        }
    }

    public async Task ReloadAiChecksAsync(SwingAdviser.Application.Analysis.IAiCheckOverviewReader reader)
    {
        var overview = await reader.GetOverviewAsync();
        Candidates.Clear();
        foreach (var candidate in overview.Candidates)
        {
            var priceDetail = candidate.LatestClose is null ? "最新確定終値は未取得です。" : $"最新確定終値 {candidate.LatestClose:n4}円（{candidate.LatestBarDate:yyyy-MM-dd}）。";
            Candidates.Add(new CandidateRow(candidate.Code, candidate.Name, candidate.Direction, "Entry", candidate.EvaluationBarDate.ToString("yyyy-MM-dd"), "保存済み戦略", candidate.Score?.ToString() ?? "未算定", candidate.Confidence ?? "未算定", "保存済みのテクニカル候補。" + priceDetail + " AI結果は参考情報です。", candidate.AiStatus, candidate.CandidateResultId, candidate.LatestAttemptId, candidate.Verdict, candidate.VerdictAlignment, candidate.Summary, candidate.LatestClose is null ? "未取得" : $"{Math.Round(candidate.LatestClose.Value, 0, MidpointRounding.AwayFromZero):n0}円"));
        }
        var total = overview.QueuedCount + overview.RunningCount + overview.SucceededCount + overview.FailedCount + overview.TimedOutCount + overview.InsufficientInformationCount + overview.CancelledCount;
        var active = overview.QueuedCount + overview.RunningCount;
        AiQueueProgress = new UpdateProgressRow(active > 0 ? "継続中" : total > 0 ? "完了" : "未実行", total == 0 ? 0 : 100d * (total - active) / total, active > 0,
            $"今回の対象 {total}件  ・  成功 {overview.SucceededCount}件  ・  実行中 {overview.RunningCount}件  ・  待機中 {overview.QueuedCount}件  ・  失敗 {overview.FailedCount + overview.TimedOutCount}件",
            active > 0
                ? "AIチェックはバックグラウンドで実行中です。状態と候補一覧は5秒ごとに自動更新します。失敗・timeout・情報不足でも日次分析結果を無効にしません。"
                : "AIチェックは完了または未実行です。失敗・timeout・情報不足でも日次分析結果を無効にしません。");
    }

    public async Task ReloadDailyUpdateAsync(SwingAdviser.Application.DailyUpdates.IDailyUpdateOverviewReader reader)
    {
        var overview = await reader.GetLatestAsync();
        var percent = overview.TotalSteps == 0 ? 0 : 100d * overview.CompletedSteps / overview.TotalSteps;
        DailyUpdateProgress = new UpdateProgressRow(overview.Status, percent, overview.Status == "実行中", $"{overview.CompletedSteps} / {overview.TotalSteps} ステップ  ・  成功 {overview.SucceededCount}件  ・  失敗 {overview.FailedCount}件", overview.Detail);
    }

    public void BeginDisplayReload(string operation)
    {
        IsDisplayLoading = true;
        if (!IsUpdateRunning) StatusMessage = $"{operation}を読み込んでいます。";
    }

    public void EndDisplayReload(bool succeeded)
    {
        IsDisplayLoading = false;
        if (succeeded && !IsUpdateRunning) StatusMessage = "保存済みの分析結果を表示しています。日次分析更新は利用者が明示的に開始します。";
    }

    /// <summary>A run marked Running in SQLite after application shutdown cannot still be executing in this window.</summary>
    public void MarkPreviousDailyUpdateAsInterrupted()
    {
        if (DailyUpdateProgress.Status != "実行中" || IsUpdateRunning) return;
        DailyUpdateProgress = DailyUpdateProgress with
        {
            Status = "前回中断",
            IsIndeterminate = false,
            Detail = $"前回のアプリ終了により更新は中断されました。{DailyUpdateProgress.Detail} 新しい日次分析更新は安全に開始できます。",
        };
    }

    public void BeginDailyUpdate()
    {
        IsUpdateRunning = true;
        StatusMessage = "日次分析更新を開始しました。完了後、候補・保有・AI状態を実データから再読込します。";
        _dailyUpdateDetailBeforeHeartbeat = "ステップ 1/11: 外部データを更新する準備をしています。中止できます。";
        DailyUpdateProgress = new UpdateProgressRow("実行中", 0, true, "0 / 11 ステップ  ・  成功 0件  ・  失敗 0件", _dailyUpdateDetailBeforeHeartbeat);
    }

    public void ReportDailyUpdate(SwingAdviser.Application.DailyUpdates.DailyUpdateStepProgress progress)
    {
        _dailyUpdateDetailBeforeHeartbeat = progress.Detail ?? "日次分析更新を実行しています。";
        var workFraction = progress.TotalWorkItems is > 0
            ? Math.Clamp((double)(progress.CompletedWorkItems ?? 0) / progress.TotalWorkItems.Value, 0, 1)
            : 0;
        var currentWork = progress.TotalWorkItems is > 0
            ? $"  ・  現在の処理 {(progress.CompletedWorkItems ?? 0):n0} / {progress.TotalWorkItems.Value:n0}"
            : string.Empty;
        DailyUpdateProgress = new UpdateProgressRow("実行中", 100d * (progress.CompletedSteps + workFraction) / progress.TotalSteps, progress.TotalWorkItems is not > 0,
            $"{progress.CompletedSteps} / {progress.TotalSteps} ステップ{currentWork}  ・  成功 {progress.SucceededCount}件  ・  失敗 {progress.FailedCount}件", _dailyUpdateDetailBeforeHeartbeat);
    }

    public void ReportDailyUpdateHeartbeat(TimeSpan elapsed)
    {
        if (!IsUpdateRunning) return;
        DailyUpdateProgress = DailyUpdateProgress with
        {
            Detail = $"{_dailyUpdateDetailBeforeHeartbeat} 経過 {Math.Floor(elapsed.TotalMinutes):n0}分{elapsed.Seconds:00}秒。中止できます。",
        };
    }

    public void EndDailyUpdate(string message)
    {
        IsUpdateRunning = false;
        StatusMessage = message;
    }

    public void ReportDisplayReloadFailure()
    {
        IsUpdateRunning = false;
        StatusMessage = "保存済みの分析表示を更新できませんでした。更新履歴を確認し、必要ならアプリを再起動してください。";
    }

    private static string Amount(decimal? value) => value is null ? "未算定" : $"{value.Value:n4}円";
}

public sealed record CandidateRow(
    string Code,
    string Name,
    string Direction,
    string EntryType,
    string EvaluationBarDate,
    string Strategy,
    string Score,
    string Confidence,
    string PrimaryReason,
    string AiStatus,
    int? CandidateResultId = null,
    int? LatestAiAttemptId = null,
    string? AiVerdict = null,
    string? AiVerdictAlignment = null,
    string? AiSummary = null,
    string LatestClose = "未取得")
{
    public bool IsMock => !CandidateResultId.HasValue;
    public bool CanQueueAiCheck => CandidateResultId.HasValue && AiStatus == "未実行";
    public bool CanRegisterManualExecution => CandidateResultId.HasValue;
    public bool CanCancelAiCheck => LatestAiAttemptId.HasValue && AiStatus == "待機中";
    public bool CanRetryAiCheck => LatestAiAttemptId.HasValue && AiStatus is "失敗" or "timeout" or "情報不足" or "キャンセル";
    public bool CanChangeAiQueue => CanCancelAiCheck || CanRetryAiCheck;
    public bool CanShowAiDetail => AiStatus is "成功" or "失敗" or "timeout" or "情報不足" or "キャンセル" or "旧結果";
    public string AiQueueActionLabel => CanCancelAiCheck ? "取消" : CanRetryAiCheck ? "再試行" : "操作不可";
    public string AiStatusDescription => AiStatus switch
    {
        "未実行" => "まだAIチェックを要求していません。",
        "待機中" => "永続キューで実行待ちです。待機中に限り取消できます。",
        "実行中" => "AIチェックを実行しています。重複投入や取消はできません。",
        "成功" => "構造化されたAI結果を保存しました。Verdict は売買推奨ではありません。",
        "失敗" => "AIチェックは失敗しました。テクニカル候補は無効になりません。",
        "timeout" => "制限時間内に完了しませんでした。必要なら利用者が再試行します。",
        "情報不足" => "Neutral（中立）ではなく、判断に必要な情報が不足した状態です。",
        "キャンセル" => "待機中のAIチェックを取り消しました。",
        "旧結果" => "以前の分析時点の結果です。現在の候補判断には用いません。",
        _ => "AIチェックの状態を確認してください。",
    };

    public string AiOperationHint => IsMock
        ? "UIモックでは操作しません。実データの候補が表示されると、状態に応じて操作できます。"
        : CanQueueAiCheck ? "AIチェックをキューへ追加します。"
        : CanCancelAiCheck ? "待機中のAIチェックを取り消します。"
        : CanRetryAiCheck ? "過去の試行を上書きせず、新しい試行として再実行します。"
        : "この状態ではキュー操作はできません。";

    public string ManualRegistrationHint => CanRegisterManualExecution
        ? "候補は銘柄・方向の入力補助だけに使います。"
        : "UIモックのダミー候補から約定を登録することはできません。";
}

public sealed record UpdateProgressRow(
    string Status,
    double ProgressPercent,
    bool IsIndeterminate,
    string Summary,
    string Detail);

public sealed record PositionRow(
    string Code,
    string Name,
    string Direction,
    string Quantity,
    string Strategy,
    string ExitDecision,
    string EvaluationDate,
    string DecisionReason,
    string StopCandidate,
    string TakeProfitCandidate,
    string HoldReason,
    string RepaymentTerm,
    string ConfirmedCost,
    string EstimatedCost,
    string PriceProfitAndLoss,
    string NetReferenceProfitAndLoss,
    string ReconciliationStatus,
    int? PositionId = null,
    string LatestClose = "未取得");

public sealed record ExecutionRow(
    string Code,
    string Name,
    string Direction,
    string ExecutionRole,
    string RegistrationSource,
    string ExecutedAt,
    string Price,
    string Quantity,
    string RevisionStatus,
    string RevisionDetail,
    int? TradeExecutionId = null);
