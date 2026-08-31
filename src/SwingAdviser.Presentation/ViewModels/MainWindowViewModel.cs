using System.Collections.ObjectModel;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// Phase 4 layout-only data source. Production data access is deliberately deferred.
/// </summary>
public sealed class MainWindowViewModel
{
    public string Title => "Swing Adviser — 日本株スイング判断支援";

    public string SafetyNotice =>
        "分析結果は参考情報です。注文・自動売買は行いません。約定は証券会社の通知を確認し、利用者が入力・確認した内容だけを保存します。";

    public string MockStatus => "Phase 4 UIモック確認①: レイアウト・情報量・誤操作防止を確認するための静的なダミーデータです。";

    public string CandidateCaption => "スコアは候補条件への一致度を表す参考情報であり、勝率や利益を保証するものではありません。";

    public ObservableCollection<CandidateRow> Candidates { get; } =
    [
        new("7203", "トヨタ自動車", "Long", "Entry", "2026-08-28", "candidate-scoring-engine-v1", "82", "High", "EMA20 > EMA50、MACD上向き、出来高倍率 1.7", "未実行"),
        new("6758", "ソニーグループ", "Long", "Entry", "2026-08-28", "candidate-scoring-engine-v1", "74", "Medium", "高値更新、MACDヒストグラム改善", "待機中"),
        new("9101", "日本郵船", "Short", "Entry", "2026-08-28", "candidate-scoring-engine-v1", "79", "High", "EMA20 < EMA50、出来高増加、下落トレンド", "情報不足"),
    ];

    public ObservableCollection<ExcludedCandidateRow> ExcludedCandidates { get; } =
    [
        new("0000", "（履歴不足の例）", "InsufficientHistory", "145本 / 200本", "必須指標を算出できないため、条件不一致として扱いません。"),
        new("0001", "（PIT未保証の例）", "PointInTimeUnverified", "200本 / 200本", "企業アクションの利用可能時点を検証できないため、候補から除外しています。"),
    ];

    public ObservableCollection<PositionRow> Positions { get; } =
    [
        new("9432", "NTT", "Long", "1,000株", "swing-long-v1", "HOLD", "2026-08-28", "トレンド継続。損切・利確ライン未到達", "150.0円", "165.0円", "期限・コスト情報を確認中", "2026-11-20 / 残 57営業日", "-1,240円", "未確定", "+8,500円", "未算定（見積コスト欠損）", "期限確認済み / コスト要確認"),
        new("8306", "三菱UFJフィナンシャル・グループ", "Short", "500株", "swing-short-v1", "利確候補", "2026-08-28", "利確目標に接近。逆行時は損切候補を確認", "2,080.0円", "1,910.0円", "HOLD理由なし", "期限未確認 / 残営業日 未算定", "未確定", "-860円", "+31,000円", "未算定（確定コスト欠損）", "期限未確認・企業アクション要照合"),
    ];

    public ObservableCollection<ExecutionRow> Executions { get; } =
    [
        new("9432", "NTT", "Long", "新規", "利用者手入力", "2026-08-05 10:15", "154.2円", "1,000株", "Rev 1（現行）", "Rev 1: 利用者確認済み。訂正は元の約定を残した新しい revision として登録します。"),
        new("8306", "三菱UFJフィナンシャル・グループ", "Short", "新規", "候補から入力補助", "2026-08-12 13:40", "1,980.0円", "500株", "Rev 2（訂正済み）", "Rev 1: 2026-08-12 13:38 / 1,978.0円 / 500株。Rev 2: 利用者が証券会社の約定通知を確認して価格を訂正。"),
    ];
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
    string AiStatus);

public sealed record ExcludedCandidateRow(
    string Code,
    string Name,
    string DataStatus,
    string History,
    string ExclusionReason);

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
    string ReconciliationStatus);

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
    string RevisionDetail);
