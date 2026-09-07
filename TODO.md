# TODO — 実運用までのロードマップ

各機能の実際のコーディングは Codex CLI で行う。本ファイルは実装の順序・依存関係・確認ポイントを管理するための工程表であり、詳細なドメイン仕様は [`AGENTS.md`](./AGENTS.md) と `docs/` 配下、DBスキーマの列・キー定義は [`docs/database-schema.md`](./docs/database-schema.md) を参照する。矛盾がある場合は `AGENTS.md` を優先する。

各フェーズの実装セッションでは、Codex に該当フェーズの docs（該当箇所）と本ファイルの該当項目を渡し、完了後に `dotnet build`/`dotnet test` が green であることと Non-negotiable rules（`AGENTS.md`）との矛盾がないことを確認してからチェックを付ける。

「(UIモック確認)」と記載した項目は、実データ結線前に静的なダミーデータで画面を組み、レイアウト・情報量・誤操作防止の観点でユーザーと確認してから次へ進む。UIモック確認は2段階に分けて実施する。

- 第1段階（UIモック確認①）: 候補一覧・保有ポジション・約定履歴はいずれも `product-spec.md` の表示要件がほぼ確定しているため、Phase 3 完了時点でメイン画面としてまとめて確認する（Phase 4）。
- 第2段階（UIモック確認②③）: 更新進捗・AIチェック状態はそれぞれ Phase 7・Phase 9 のドメイン実装が固まってから、独立フェーズ（Phase 8・Phase 10）として個別に確認する。

Phase 4 のモックでは、参考デザインとして別リポジトリ `C:\Users\su\source\repos\swing-adviser-codex\src\SwingAdviser.Presentation`（`MainWindow.xaml` / `MainWindowViewModel.cs` 等）を使用する。参考にするのはウインドウサイズ・コントロール配置・タブキャプションのみであり、表示フィールドは本プロジェクト（`SwingAdviser.Domain`/`docs/database-schema.md`）のモデルに合わせて設計し直す。タブ名は次のとおり統一する。

- エントリー候補一覧 → 「候補」
- 保有建玉一覧 → 「保有」
- 過去の売買履歴 → 「履歴」

## Phase 0 — 基盤 (完了)
- [x] ソリューション構成作成（Domain/Application/Infrastructure/Presentation + Infrastructure.Tests）
- [x] EF Core Sqlite + EFCore.NamingConventions（snake_case）配線
- [x] `RuntimeDatabasePathResolver`（`docs/database-schema.md` Runtime database location）
- [x] 設計時 DbContext ファクトリ（`dotnet ef` 用）
- [x] 空の `InitialCreate` マイグレーション
- [x] 素のWPF（Prism/DIフレームワーク不使用）+ MahApps.Metro の最小 WPF シェル
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
- [x] JPX銘柄マスタ（上場銘柄一覧）取得・`instrument_master_revisions` 更新
- [x] JPX信用取引銘柄・貸株銘柄一覧取得・`margin_regulation_revisions` 更新
- [x] Yahoo Finance chart API による日足取得・`daily_bars` 更新（未調整値を正本として保存）
- [x] 企業アクション取得・`corporate_actions` の版管理・`effective_date`/`available_at` 判定
- [x] ファンダメンタルデータ（PER/PBR等）取得・`fundamental_data_snapshots` 更新
- [x] 外部取得失敗の `external_fetch_results` 記録（1銘柄失敗で全体停止しないこと）
- [x] 上記 Repository/取得処理のユニット・統合テスト（欠損・訂正・rate limit・timeout・network error）

## Phase 3 — テクニカル分析エンジン（Domain/Application）
参照: [`technical-analysis.md`](./docs/technical-analysis.md)
- [x] point-in-time 調整済み OHLCV 生成（分割・配当調整、`analysis_input_manifests` 連携）
- [x] EMA(20/50/200)・MACD(12/26/9)・出来高倍率・ATR14 の指標計算実装（各アルゴリズム識別子どおり）
- [x] `InsufficientHistory`/`HistoryIncomplete`/`InvalidData`/`PointInTimeUnverified` の fail-closed 処理
- [x] `candidate-scoring-engine-v1`（Long/Short非対称の必須条件・スコア計算）実装
- [x] 全銘柄スキャン Application サービス（`scan_runs`/`indicator_results`/`scan_exclusions`/`candidate_results` 生成、進捗・失敗件数の可視化）
- [x] 指標・シグナル境界値、Long/Short非対称ロジックのテスト（期待値を本体と同アルゴリズムで再計算するテストは避ける）

## Phase 4 — UIモック確認①（メイン画面: 候補・保有・履歴）
参照: [`product-spec.md`](./docs/product-spec.md) Candidate list / Positions / Trade records、参考デザイン `C:\Users\su\source\repos\swing-adviser-codex\src\SwingAdviser.Presentation`（ウインドウサイズ・コントロール配置・キャプションのみ参照。表示フィールドは本プロジェクトの `SwingAdviser.Domain` モデルに合わせる）

- [x] MahApps.Metro `MetroWindow` ベースのメイン画面骨格（`TabControl` に「候補」「保有」「履歴」の3タブ、ウインドウサイズ・最小サイズは参考デザインに合わせる）をダミーデータで実装
- [x] 「候補」タブ: `CandidateResult`/`IndicatorResult` 系フィールドで列構成（銘柄コード/銘柄名、Long/Short、Entry種別、判定基準バー日、適用戦略、スコア/信頼度、主な判定理由、AI状態、除外理由表示を含む）
- [x] 「保有」タブ: `Position`/`MarginLot`/`RiskPlan`/`PositionHoldingEvaluation`/`MarginCostLedgerEntry` 系フィールドで列構成（適用戦略、決済判定、判定日/判定理由、損切候補、利確候補、HOLD理由、返済期限・残営業日、確定/見積コスト、価格損益・ネット参考損益、要照合状態を含む）
- [x] 「履歴」タブ: `TradeExecution`（訂正revision含む）フィールドで列構成（登録元、約定日時/価格/株数、訂正操作の表現を含む）
- [x] 上記3タブをまとめてユーザーとレイアウト・情報量・誤操作防止の観点で確認する

## Phase 5 — リスク管理エンジン
参照: [`risk-management.md`](./docs/risk-management.md)
- [x] `initial-risk-plan-factory-v1`（risk basis・初期stop/target算出）実装
- [x] `holding-risk-evaluation-v1`（lot単位判定→position集約、`Hold`のfail-closed運用）実装
- [x] `PartialExitBreakevenPlanFactory`（部分利確後の建値ストップ遷移）実装
- [x] `MarginCostLedger`（買方金利・貸株料・逆日歩・配当金相当額、Estimate/Confirmed区別）実装
- [x] 返済期限集約・警告閾値（30/10/5/1営業日、設定化）実装
- [x] 損切/利確/HOLD境界値、複数lot集約、期限・コスト欠損状態のテスト

## Phase 6 — ポジション・約定管理（手動登録フロー）
参照: [`product-spec.md`](./docs/product-spec.md) Trade records、`AGENTS.md` Non-negotiable rules
- [x] 約定登録画面（候補一覧からの銘柄/方向入力補助→価格/日時/株数は利用者入力→保存前確認）実装
- [x] 部分決済のlot allocation明示登録（FIFO等の自動推測をしない）実装
- [x] 企業アクション換算・要照合状態の反映実装

## Phase 7 — 日次更新オーケストレーション
参照: [`product-spec.md`](./docs/product-spec.md) Daily update workflow
- [x] 11ステップの日次更新フロー（更新→point-in-time生成→テクニカル分析→Long/Short候補→保有再評価→保存→AIキュー投入）実装
- [x] `daily_update_runs`/進捗・成功/失敗件数の可視化実装
- [x] 1銘柄失敗時の継続動作、冪等性の確認

## Phase 8 — UIモック確認②（更新進捗表示）
参照: [`product-spec.md`](./docs/product-spec.md) Daily update workflow / UI/UX
- [x] 更新進捗表示（進捗バー・成功/失敗件数）をダミーデータで実装
- [x] 「分析更新完了・AIチェック継続中」の分離表現を実装
- [x] 上記をユーザーとレイアウト・情報量の観点で確認する

## Phase 9 — AIチェック統合
参照: [`ai-analysis.md`](./docs/ai-analysis.md)
- [x] Codex CLI 実行基盤（timeout・shell injection対策・秘密情報非露出）実装
- [x] 永続キュー（`Queued→Running→終端状態`、最大2並列、キャンセル・再試行）実装
- [x] semantic schema v1（`ai-result-v1`）パーサー・バリデーション実装
- [x] AIチェック状態・結果のUI実装（Verdictと候補方向の整合/逆表現、`InsufficientInformation`と`Neutral`の非混同）
- [x] AI失敗時フォールバック（テクニカル結果を無効化しない）のテスト

## Phase 10 — UIモック確認③（AIチェック状態・結果表示）
参照: [`ai-analysis.md`](./docs/ai-analysis.md)、[`product-spec.md`](./docs/product-spec.md) UI/UX
- [x] AIチェック状態・結果表示画面（未実行/待機中/実行中/成功/失敗/timeout/情報不足/キャンセル/旧結果の区別、単件・複数選択操作を含む）をダミーデータで実装
- [x] AI Verdictと候補方向の整合/逆表現、`InsufficientInformation`と`Neutral`の非混同表現を実装
- [x] 上記をユーザーとレイアウト・誤操作防止の観点で確認する

## Phase 11 — 統合・品質保証
- [x] Phase2〜9の結線後、代表的なE2Eシナリオ（候補抽出→AIチェック→建玉登録→保有再評価→決済登録）を手動で通す
- [x] `dotnet build`/`dotnet test` が全体で green であることを確認
- [x] ログ・エラー分類（HTTP error/rate limit/timeout/invalid data/CLI failure/SQLite lock/cancellation）の網羅性レビュー
- [x] Non-negotiable rules（自動売買化していないか等）の最終レビュー

## Phase 12 — 実データ画面結線（Phase 11未完了分の補完）
現状、起動時に実行しているのはDB接続・AIキュー復旧のみで、外部データ取得→日次更新→スキャンは起動していない。候補の実データ表示も `candidate_results` に既存データがある場合のみモックを置換する実装にとどまり、日次更新を画面から実行して結果を各タブへ反映する結線が未実装。これはPhase 11の完了範囲から漏れていたため、Phase 12として実装し、実運用移行準備は後続フェーズへ繰り下げる。

- [x] 日次更新（Phase 7のオーケストレーション）を画面から起動できるトリガーの実装（外部データ取得→point-in-time生成→テクニカル分析→スキャン→候補生成→保有再評価→保存→AIキュー投入）
- [x] 更新進捗表示（Phase 8のUIモック）への実データ結線（`daily_update_runs`の進捗・成功/失敗件数）
- [x] 「候補」タブへのスキャン/候補結果の実データ結線（更新実行後に`candidate_results`が反映されること）
- [x] 「保有」タブへの保有再評価結果の実データ結線（`position_holding_evaluations`等）
- [x] 株価取得結果（`daily_bars`等）の画面反映確認
- [x] AIキューへの実投入・実行確認（日次更新から`AiCheckQueueService.EnqueueAsync`経由でCodex CLIが実際に実行され、結果がAIチェック画面に反映されること。`AiCheckOptions.EnableAutomaticChecks`とCLI実行パス設定を含む）— 実CLIの隔離キューで`Succeeded`/`Neutral`結果の永続化まで確認済み
- [x] E2E確認: アプリ起動→日次更新実行→候補（AI状態/AI Verdict列を含む）/保有/更新進捗の各タブに実データが表示されること — 2026-09-02 JSTに実機確認。保有建玉0件のため保有一覧は空表示

## Phase 13 — 実運用移行準備
- [x] 実運用DB配置（EXEと同ディレクトリの`swing-adviser.db`固定、書き込み不可時の明示エラー）の実機確認 — 2026-09-04にRelease実行ファイルのスクラッチコピーで確認。書込み可能フォルダでは`swing-adviser.db`がexe隣に新規作成され通常起動、書込み不可フォルダ（ACLで拒否）では暗黙フォールバックせず「起動エラー」ダイアログを表示して停止することを実機確認。合わせて`docs/database-schema.md`が要求していた`PRAGMA foreign_keys=ON`/`PRAGMA journal_mode=WAL`が実運用コードに未実装だったギャップを`RuntimeSwingAdviserDbContextFactory`で修正し、回帰テストを追加（`dotnet test`73件green）
- [x] 設定ファイル（APIキー参照・Codex CLIパス等）の秘密情報非コミット確認 — 全設定値は環境変数（`SWING_ADVISER_*`）経由でハードコード/appsettings等の設定ファイルは存在せず、git履歴にも実秘密情報のコミットなしを確認。将来の混入予防として`.gitignore`に`.env`/`appsettings.*.json`/`*secrets*.json`を追加
- [ ] ~~配布物（自己完結exe等）の確定・インストール手順整備~~（このPC専用運用のため対象外。ビルド済みexeをこのPC上でそのまま実行する運用とする）
- [x] 日次更新の中断再開・差分取得: 銘柄・取得種別ごとの完了チェックポイントを永続化し、同一評価日で有効な成功分は再利用する。中断/失敗分、鮮度切れ分、訂正検知分だけを再取得し、日足・企業アクション・履歴カバレッジ・取得監査の整合性を維持する。
- [x] バックアップ/リストア手順（SQLiteファイルコピー等）の確立 — `docs/operations-tutorial.md`の手順を2026-09-04にスクラッチ環境で実機確認（マーカー行付きDBでバックアップ→退避→復元→再起動→データ保持を確認）。WAL化後もアプリ完全終了後は単一ファイルコピーで問題ないことも確認済み
- [ ] 小規模・期間限定の試験運用

## Phase 14 — 実運用開始後
- [ ] JPXファイル形式変更・Yahoo非公式API変更等への追従方針の運用ドキュメント化
- [ ] 障害対応・問い合わせ対応手順の整備
