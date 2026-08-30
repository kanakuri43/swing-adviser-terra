# TODO — 実運用までのロードマップ

各機能の実際のコーディングは Codex CLI で行う。本ファイルは実装の順序・依存関係・確認ポイントを管理するための工程表であり、詳細なドメイン仕様は [`AGENTS.md`](./AGENTS.md) と `docs/` 配下、DBスキーマの列・キー定義は [`docs/database-schema.md`](./docs/database-schema.md) を参照する。矛盾がある場合は `AGENTS.md` を優先する。

各フェーズの実装セッションでは、Codex に該当フェーズの docs（該当箇所）と本ファイルの該当項目を渡し、完了後に `dotnet build`/`dotnet test` が green であることと Non-negotiable rules（`AGENTS.md`）との矛盾がないことを確認してからチェックを付ける。

「(UIモック確認)」と記載した項目は、実データ結線前に静的なダミーデータで画面を組み、レイアウト・情報量・誤操作防止の観点でユーザーと確認してから次へ進む。

## Phase 0 — 基盤 (完了)
- [x] ソリューション構成作成（Domain/Application/Infrastructure/Presentation + Infrastructure.Tests）
- [x] EF Core Sqlite + EFCore.NamingConventions（snake_case）配線
- [x] `RuntimeDatabasePathResolver`（`docs/database-schema.md` Runtime database location）
- [x] 設計時 DbContext ファクトリ（`dotnet ef` 用）
- [x] 空の `InitialCreate` マイグレーション
- [x] Prism + MahApps.Metro の最小 WPF シェル
- [x] `dotnet build` / `dotnet test` 成功（3テスト green）

## Phase 1 — DBスキーマ実装
各Stepとも Domain エンティティ → EF Core `IEntityTypeConfiguration` → 追加マイグレーション → Repository/migration テストの順で実装し、既存マイグレーションは書き換えない（節番号は [`docs/database-schema.md`](./docs/database-schema.md) の章立てに対応）。

- [x] Step 1（§1–2）: 銘柄マスタ・信用規制・価格データ・企業アクション・ファンダメンタル（`instruments`, `instrument_master_revisions`, `margin_regulation_revisions`, `daily_bars`, `corporate_actions`, `fundamental_data_snapshots`）
- [x] Step 2（§3–4）: 分析入力manifest・戦略パラメータ・日次更新run/取得ログ・スキャン/指標/候補結果（`analysis_input_manifests`系, `strategy_parameter_snapshots`, `daily_update_runs`, `external_fetch_results`, `scan_runs`, `indicator_results`, `scan_exclusions`, `candidate_results`）
- [x] Step 3（§5）: ポジション・約定・MarginLot（`positions`, `trade_executions`, `margin_lots`, `margin_lot_contract_term_revisions`, `trade_execution_lot_allocations`, `position_corporate_action_adjustments`）
- [x] Step 4（§6）: risk basis・risk plan（`risk_basis_snapshots`, `risk_plans`）
- [x] Step 5（§7）: 保有再評価（`lot_holding_evaluations`, `position_holding_evaluations`）
- [x] Step 6（§8）: 信用コスト台帳（`margin_cost_ledger_entries`）
- [x] Step 7（§9）: AIチェック（`ai_check_attempts`, `ai_check_results`, `ai_check_evidence_items`, `ai_check_sources`, `ai_check_evidence_citations`）
- [x] Step 8（§10）: 実データアクセスパターンに合わせたインデックス追加
- [x] 各Step完了ごとに追加マイグレーションが `dotnet ef migrations add` で意図通り生成されることを確認する

## Phase 2 — データ取得基盤（Infrastructure）
参照: [`data-sources.md`](./docs/data-sources.md)
- [ ] JPX銘柄マスタ（上場銘柄一覧）取得・`instrument_master_revisions` 更新
- [ ] JPX信用取引銘柄・貸株銘柄一覧取得・`margin_regulation_revisions` 更新
- [ ] Yahoo Finance chart API による日足取得・`daily_bars` 更新（未調整値を正本として保存）
- [ ] 企業アクション取得・`corporate_actions` の版管理・`effective_date`/`available_at` 判定
- [ ] ファンダメンタルデータ（PER/PBR等）取得・`fundamental_data_snapshots` 更新
- [ ] 外部取得失敗の `external_fetch_results` 記録（1銘柄失敗で全体停止しないこと）
- [ ] 上記 Repository/取得処理のユニット・統合テスト（欠損・訂正・rate limit・timeout・network error）

## Phase 3 — テクニカル分析エンジン（Domain/Application）
参照: [`technical-analysis.md`](./docs/technical-analysis.md)
- [ ] point-in-time 調整済み OHLCV 生成（分割・配当調整、`analysis_input_manifests` 連携）
- [ ] EMA(20/50/200)・MACD(12/26/9)・出来高倍率・ATR14 の指標計算実装（各アルゴリズム識別子どおり）
- [ ] `InsufficientHistory`/`HistoryIncomplete`/`InvalidData`/`PointInTimeUnverified` の fail-closed 処理
- [ ] `candidate-scoring-engine-v1`（Long/Short非対称の必須条件・スコア計算）実装
- [ ] 全銘柄スキャン Application サービス（`scan_runs`/`indicator_results`/`scan_exclusions`/`candidate_results` 生成、進捗・失敗件数の可視化）
- [ ] 指標・シグナル境界値、Long/Short非対称ロジックのテスト（期待値を本体と同アルゴリズムで再計算するテストは避ける）

**(UIモック確認①)** 候補一覧画面（[`product-spec.md`](./docs/product-spec.md) Candidate list の表示要件）をダミーデータで組み、列構成・スコア表現・除外理由表示をユーザーと確認する。

## Phase 4 — リスク管理エンジン
参照: [`risk-management.md`](./docs/risk-management.md)
- [ ] `initial-risk-plan-factory-v1`（risk basis・初期stop/target算出）実装
- [ ] `holding-risk-evaluation-v1`（lot単位判定→position集約、`Hold`のfail-closed運用）実装
- [ ] `PartialExitBreakevenPlanFactory`（部分利確後の建値ストップ遷移）実装
- [ ] `MarginCostLedger`（買方金利・貸株料・逆日歩・配当金相当額、Estimate/Confirmed区別）実装
- [ ] 返済期限集約・警告閾値（30/10/5/1営業日、設定化）実装
- [ ] 損切/利確/HOLD境界値、複数lot集約、期限・コスト欠損状態のテスト

**(UIモック確認②)** 保有ポジション一覧・詳細画面（product-spec.md Positions の表示要件）をダミーデータで組み、損切/利確候補・HOLD理由・期限警告・コスト表示の見え方を確認する。

## Phase 5 — ポジション・約定管理（手動登録フロー）
参照: [`product-spec.md`](./docs/product-spec.md) Trade records、`AGENTS.md` Non-negotiable rules
- [ ] 約定登録画面（候補一覧からの銘柄/方向入力補助→価格/日時/株数は利用者入力→保存前確認）実装
- [ ] 部分決済のlot allocation明示登録（FIFO等の自動推測をしない）実装
- [ ] 企業アクション換算・要照合状態の反映実装

**(UIモック確認③)** 約定登録フローのモックを確認する。特に「現在値/終値の自動採用がないこと」「ボタン一回で売買成立まで進まないこと」を重点確認する。

## Phase 6 — 日次更新オーケストレーション
参照: [`product-spec.md`](./docs/product-spec.md) Daily update workflow
- [ ] 11ステップの日次更新フロー（更新→point-in-time生成→テクニカル分析→Long/Short候補→保有再評価→保存→AIキュー投入）実装
- [ ] `daily_update_runs`/進捗・成功/失敗件数の可視化実装
- [ ] 1銘柄失敗時の継続動作、冪等性の確認

**(UIモック確認④)** 更新進捗表示（「分析更新完了・AIチェック継続中」の分離表現を含む）をモックで確認する。

## Phase 7 — AIチェック統合
参照: [`ai-analysis.md`](./docs/ai-analysis.md)
- [ ] Codex CLI 実行基盤（timeout・shell injection対策・秘密情報非露出）実装
- [ ] 永続キュー（`Queued→Running→終端状態`、最大2並列、キャンセル・再試行）実装
- [ ] semantic schema v1（`ai-result-v1`）パーサー・バリデーション実装
- [ ] AIチェック状態・結果のUI実装（Verdictと候補方向の整合/逆表現、`InsufficientInformation`と`Neutral`の非混同）
- [ ] AI失敗時フォールバック（テクニカル結果を無効化しない）のテスト

**(UIモック確認⑤)** AIチェック状態・結果表示画面をモックで確認する。

## Phase 8 — 統合・品質保証
- [ ] Phase2〜7の結線後、代表的なE2Eシナリオ（候補抽出→AIチェック→建玉登録→保有再評価→決済登録）を手動で通す
- [ ] `dotnet build`/`dotnet test` が全体で green であることを確認
- [ ] ログ・エラー分類（HTTP error/rate limit/timeout/invalid data/CLI failure/SQLite lock/cancellation）の網羅性レビュー
- [ ] Non-negotiable rules（自動売買化していないか等）の最終レビュー

## Phase 9 — 実運用移行準備
- [ ] 実運用DB配置（EXEと同ディレクトリの`swing-adviser.db`固定、書き込み不可時の明示エラー）の実機確認
- [ ] 設定ファイル（APIキー参照・Codex CLIパス等）の秘密情報非コミット確認
- [ ] 配布物（自己完結exe等）の確定・インストール手順整備
- [ ] バックアップ/リストア手順（SQLiteファイルコピー等）の確立
- [ ] 小規模・期間限定の試験運用

## Phase 10 — 実運用開始後
- [ ] JPXファイル形式変更・Yahoo非公式API変更等への追従方針の運用ドキュメント化
- [ ] 障害対応・問い合わせ対応手順の整備
