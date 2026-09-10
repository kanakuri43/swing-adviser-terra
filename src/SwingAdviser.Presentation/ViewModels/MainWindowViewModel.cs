using System.Collections.ObjectModel;
using System.Text.Json;

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
    private DailyUpdateProgressRow _dailyUpdateProgress = DailyUpdateProgressRow.NotStarted;
    private UpdateProgressRow _aiQueueProgress = new("未実行", 0, false, "AIチェックはまだありません。", "AIチェックは日次分析の完了を待たず、設定で有効な場合だけ自動投入します。");
    private bool _isUpdateRunning;
    private bool _isDisplayLoading;
    private string _openPositionProfitAndLoss = "保有建玉の現在損益: なし";
    private CandidateRow? _selectedCandidate;
    private string _dailyUpdateDetailBeforeHeartbeat = "日次分析更新はまだ実行されていません。";

    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public DailyUpdateProgressRow DailyUpdateProgress { get => _dailyUpdateProgress; private set => Set(ref _dailyUpdateProgress, value); }
    public UpdateProgressRow AiQueueProgress { get => _aiQueueProgress; private set => Set(ref _aiQueueProgress, value); }
    public bool IsUpdateRunning { get => _isUpdateRunning; private set => Set(ref _isUpdateRunning, value); }
    public bool IsDisplayLoading { get => _isDisplayLoading; private set => Set(ref _isDisplayLoading, value); }
    public string OpenPositionProfitAndLoss { get => _openPositionProfitAndLoss; private set => Set(ref _openPositionProfitAndLoss, value); }
    public CandidateRow? SelectedCandidate { get => _selectedCandidate; set => Set(ref _selectedCandidate, value); }

    public ObservableCollection<CandidateRow> Candidates { get; } = [];
    public ObservableCollection<PositionRow> Positions { get; } = [];
    public ObservableCollection<ExecutionRow> Executions { get; } = [];

    public async Task ReloadManualRecordsAsync(SwingAdviser.Application.Positions.IManualPositionOverviewReader reader)
    {
        var positions = await reader.GetOpenPositionsAsync();
        var executions = await reader.GetExecutionsAsync();
        OpenPositionProfitAndLoss = FormatOpenPositionProfitAndLoss(positions);
        Positions.Clear();
        foreach (var position in positions)
        {
            var evaluated = position.EvaluationDecision is not null;
            var evaluationDetail = evaluated
                ? $"{position.EvaluationOutcome ?? "評価済み"}。保存済みの保有再評価結果です。"
                : EvaluationUnavailableReason(position.EvaluationOutcome);
            if (position.LatestClose is not null) evaluationDetail += $" 最新確定終値: {position.LatestClose:n4}円（{position.LatestBarDate:yyyy-MM-dd}）。";
            Positions.Add(new PositionRow(position.Code, position.Name, position.Side, $"{position.CurrentQuantity:n0}株", position.Strategy,
                position.EvaluationDecision ?? "未評価", position.EvaluationBarDate?.ToString("yyyy-MM-dd") ?? "未評価", evaluationDetail,
                Amount(position.StopCandidate), Amount(position.TakeProfitCandidate), position.EvaluationDecision == "Hold" ? evaluationDetail : "HOLD対象外/未評価",
                position.EarliestRepaymentDate?.ToString("yyyy-MM-dd") ?? "期限未確認", Amount(position.ConfirmedCost), "未算定", Amount(position.PriceProfitAndLoss), Amount(position.ReferenceNetProfitAndLoss), position.ReconciliationStatus, position.PositionId, Amount(position.LatestClose)));
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
        // The five-second AI-status poll rebuilds these immutable rows. Preserve the logical
        // selection before clearing the collection so the details pane does not disappear while
        // a selected candidate moves from queued to running/completed.
        var selectedCandidateResultId = SelectedCandidate?.CandidateResultId;
        Candidates.Clear();
        foreach (var candidate in overview.Candidates)
        {
            Candidates.Add(new CandidateRow(candidate.Code, candidate.Name, candidate.Direction, "Entry", candidate.EvaluationBarDate.ToString("yyyy-MM-dd"), "保存済み戦略", candidate.Score?.ToString() ?? "未算定", candidate.Confidence ?? "未算定", BuildPrimaryReason(candidate), candidate.AiStatus, candidate.CandidateResultId, candidate.LatestAttemptId, candidate.Verdict, candidate.VerdictAlignment, candidate.Summary, candidate.LatestClose is null ? "未取得" : $"{Math.Round(candidate.LatestClose.Value, 0, MidpointRounding.AwayFromZero):n0}円"));
        }
        if (selectedCandidateResultId.HasValue)
            SelectedCandidate = Candidates.FirstOrDefault(candidate => candidate.CandidateResultId == selectedCandidateResultId.Value);
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
        DailyUpdateProgress = new DailyUpdateProgressRow(
            overview.Status,
            percent,
            $"完了 {overview.CompletedSteps:n0} / {overview.TotalSteps:n0} 工程  ・  成功 {overview.SucceededCount:n0}件  ・  失敗 {overview.FailedCount:n0}件",
            overview.TotalSteps,
            null,
            null,
            0,
            false,
            overview.Status == "実行中" ? "現在の工程は保存済みの情報から特定できません。" : "実行中の工程はありません。",
            overview.Detail);
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
            Detail = $"前回のアプリ終了により更新は中断されました。{DailyUpdateProgress.Detail} 新しい日次分析更新は安全に開始できます。",
        };
    }

    public void BeginDailyUpdate()
    {
        IsUpdateRunning = true;
        StatusMessage = "日次分析更新を開始しました。完了後、候補・保有・AI状態を実データから再読込します。";
        _dailyUpdateDetailBeforeHeartbeat = "外部データを更新する準備をしています。中止できます。";
        DailyUpdateProgress = new DailyUpdateProgressRow(
            "実行中",
            0,
            "完了 0 / 11 工程  ・  成功 0件  ・  失敗 0件",
            11,
            1,
            DisplayStepName(SwingAdviser.Application.DailyUpdates.DailyUpdateStep.RefreshMarketData),
            0,
            true,
            "開始準備中",
            _dailyUpdateDetailBeforeHeartbeat);
    }

    public void ReportDailyUpdate(SwingAdviser.Application.DailyUpdates.DailyUpdateStepProgress progress)
    {
        _dailyUpdateDetailBeforeHeartbeat = progress.Detail ?? "日次分析更新を実行しています。";
        var workFraction = progress.TotalWorkItems is > 0
            ? Math.Clamp((double)(progress.CompletedWorkItems ?? 0) / progress.TotalWorkItems.Value, 0, 1)
            : 0;
        var isStageComplete = progress.Status is "Succeeded" or "Failed";
        var currentWork = progress.TotalWorkItems is > 0
            ? $"{(progress.CompletedWorkItems ?? 0):n0} / {progress.TotalWorkItems.Value:n0} 件"
            : isStageComplete ? "完了" : "処理の準備中";
        var overallPercent = progress.TotalSteps == 0
            ? 0
            : 100d * (progress.CompletedSteps + (isStageComplete ? 0 : workFraction)) / progress.TotalSteps;
        DailyUpdateProgress = new DailyUpdateProgressRow(
            "実行中",
            overallPercent,
            $"完了 {progress.CompletedSteps:n0} / {progress.TotalSteps:n0} 工程  ・  成功 {progress.SucceededCount:n0}件  ・  失敗 {progress.FailedCount:n0}件",
            progress.TotalSteps,
            (int)progress.Step,
            DisplayStepName(progress.Step),
            isStageComplete ? 100 : 100 * workFraction,
            !isStageComplete && progress.TotalWorkItems is not > 0,
            currentWork,
            _dailyUpdateDetailBeforeHeartbeat);
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

    private static string FormatOpenPositionProfitAndLoss(IReadOnlyList<SwingAdviser.Application.Positions.ManualPositionOverview> positions)
    {
        if (positions.Count == 0) return "保有建玉の現在損益: なし";
        if (positions.Any(position => position.PriceProfitAndLoss is null)) return "保有建玉の価格損益: 一部未算定 / ネット参考損益: 未算定";
        var priceTotal = positions.Sum(position => position.PriceProfitAndLoss!.Value);
        if (positions.Any(position => position.ReferenceNetProfitAndLoss is null))
            return $"保有建玉の価格損益: {priceTotal:+#,0;-#,0;0}円 / ネット参考損益: 未算定";
        var netTotal = positions.Sum(position => position.ReferenceNetProfitAndLoss!.Value);
        return $"保有建玉の価格損益: {priceTotal:+#,0;-#,0;0}円 / ネット参考損益: {netTotal:+#,0;-#,0;0}円";
    }

    private static string EvaluationUnavailableReason(string? outcome) => outcome switch
    {
        "IncompletePositionData" => "未評価: 建玉時ATR（リスク基準）または損切・利確プランが未登録です。",
        "ReconciliationRequired" => "未評価: 企業アクションの照合が必要です。",
        "InsufficientHistory" => "未評価: 保有再評価に必要な日足履歴が不足しています。",
        "HistoryIncomplete" => "未評価: 建玉後の日足履歴に欠損があります。",
        "PointInTimeUnverified" => "未評価: 時点整合性を確認できないデータが含まれます。",
        "IntradaySequenceUnknown" => "未評価: 建玉・プランの有効時刻と当日足の前後関係を判定できません。",
        "InvalidData" => "未評価: 保有再評価に必要なデータが不正です。",
        "Failed" => "未評価: 保有再評価でエラーが発生しました。",
        _ => "未評価: 日次更新後の保有再評価結果がありません。",
    };

    private static string BuildPrimaryReason(SwingAdviser.Application.Analysis.AiCandidateOverview candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(candidate.ScoreComponentsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "保存済みの候補スコアはありますが、判定詳細を読み取れません。";
            var state = ReadString(root, "state") == "Fresh" ? "新規に条件成立" : "条件を継続";
            var macdGap = ReadDecimal(root, "macdGap");
            var emaGap = ReadDecimal(root, "emaGap");
            var volumeRatio = ReadDecimal(root, "volumeRatio");
            var atr = ReadDecimal(root, "atr");
            if (macdGap is null || emaGap is null || volumeRatio is null || atr is null)
                return "保存済みの候補スコアはありますが、指標別の判定詳細は未保存です。";
            var macdOrder = candidate.Direction == "Long" ? "MACDがシグナルより上" : "MACDがシグナルより下";
            var emaOrder = candidate.Direction == "Long" ? "EMA20 ＞ EMA50 ＞ EMA200" : "EMA20 ＜ EMA50 ＜ EMA200";
            return $"{state}。{macdOrder}（優位幅 {Number(macdGap)}）、{emaOrder}（最小差 {Number(emaGap)}）、出来高 {Number(volumeRatio)}倍、ATR {Number(atr)}。";
        }
        catch (JsonException)
        {
            return "保存済みの候補スコアはありますが、判定詳細を読み取れません。候補詳細とAIチェックは参考情報です。";
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? ReadDecimal(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var result) ? result : null;

    private static string Number(decimal? value) => value?.ToString("n2") ?? "未取得";

    private static string DisplayStepName(SwingAdviser.Application.DailyUpdates.DailyUpdateStep step) => step switch
    {
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.RefreshMarketData => "外部データを更新",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.VerifyDataAvailability => "データ利用可否を確認",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.ApplyCorporateActionAdjustments => "企業アクションを反映",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.BuildPointInTimeSeries => "時点整合データを準備",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.RunTechnicalAnalysis => "テクニカル分析を実行",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.ExtractLongCandidates => "Long候補を抽出",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.ExtractShortCandidates => "Short候補を抽出",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.ReevaluateHoldings => "保有を再評価",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.PersistAnalysisResults => "分析結果を保存",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.PublishResults => "結果を公開",
        SwingAdviser.Application.DailyUpdates.DailyUpdateStep.EnqueueAiChecks => "AIチェックをキューへ投入",
        _ => step.ToString(),
    };
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

/// <summary>Separates completed workflow progress from work inside the current workflow step.</summary>
public sealed record DailyUpdateProgressRow(
    string Status,
    double OverallProgressPercent,
    string OverallSummary,
    int TotalSteps,
    int? CurrentStepNumber,
    string? CurrentStepName,
    double CurrentStepProgressPercent,
    bool IsCurrentStepIndeterminate,
    string CurrentWorkSummary,
    string Detail)
{
    public static DailyUpdateProgressRow NotStarted { get; } = new(
        "未実行",
        0,
        "完了 0 / 11 工程  ・  成功 0件  ・  失敗 0件",
        11,
        null,
        null,
        0,
        false,
        "実行中の工程はありません。",
        "更新すると、外部データ取得・分析・候補生成・保有再評価を実行します。");

    public string CurrentStepSummary => CurrentStepNumber is null || string.IsNullOrWhiteSpace(CurrentStepName)
        ? "現在の工程: なし"
        : $"現在の工程: {CurrentStepNumber:n0} / {TotalSteps:n0} — {CurrentStepName}";
}

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
