# Database Schema

AGENT.md の要約から分割した、SQLite 業務スキーマの詳細。
本ファイルは [`AGENTS.md`](../AGENTS.md) が言う「具体的なテーブル定義・列構成の正」であり、他 docs（[`product-spec.md`](./product-spec.md)、[`data-sources.md`](./data-sources.md)、[`technical-analysis.md`](./technical-analysis.md)、[`risk-management.md`](./risk-management.md)、[`ai-analysis.md`](./ai-analysis.md)）で言及される業務要件を、実際のテーブル・列へ落とし込む。
Non-negotiable rules・優先順位は AGENT.md 側が正であり、本ファイルの内容と矛盾する場合は AGENT.md を優先する。

この設計は初回マイグレーション時点の仮決定であり、バックテスト・実運用を通じて改訂されうる。表定義を変更する場合は既存マイグレーションを書き換えず、追加マイグレーションで反映する。

## Runtime database location

実運用 SQLite DB は設定値にせず、実行中 EXE と同じディレクトリの `swing-adviser.db` に固定する。

- パス解決の基準はカレントディレクトリではなく `AppContext.BaseDirectory` とする。
- ディレクトリが書き込み不可の場合、別ディレクトリへ暗黙フォールバックせず、起動時に明示的なエラーとして扱う（アプリを起動させない、またはエラー状態で起動しユーザーに提示する）。
- 開発・テスト環境では設定またはコマンドライン引数で別パスを明示指定できてよいが、既定値は上記固定パスとする。
- 接続オープン時に `PRAGMA foreign_keys = ON` を必ず有効化する。
- `PRAGMA journal_mode = WAL` を既定とし、長時間処理中も UI thread からの読み取りをブロックしにくくする（Configuration で上書き可能にしてよい）。

## Conventions

すべてのテーブルに共通する規約。個別テーブル節では規約からの逸脱だけを明記する。

### Naming
- テーブル名・列名は snake_case（EF Core Fluent API でマッピング設定する）。
- テーブル名は複数形（例: `positions`）、主キー列は `<単数形テーブル名>_id`（例: `position_id`）。
- 外部キー列は参照先の主キー列名をそのまま使う（例: `instrument_id`）。

### Primary keys
- 特に断りがない限り、各テーブルは EF Core が採番する surrogate integer 主キー（SQLite `INTEGER PRIMARY KEY`）を持つ。
- 改訂（revision）を持つ系列は、論理エンティティ（例: `margin_lots`）と改訂履歴テーブル（例: `margin_lot_contract_term_revisions`）を分離する。改訂履歴テーブルの主キーは行ごとに新規採番し、論理エンティティへの外部キーで束ねる。

### Revision / supersede pattern
状態が時間とともに改訂されるテーブル（価格、企業アクション、契約条件、信用コスト、risk plan、約定訂正等）は共通してこの形を取る。

- 既存行を UPDATE/DELETE で書き換えない。訂正・改定は新しい行として追加する。
- `revision`（同一論理キー内で 1 始まり連番の整数）、または旧行への自己参照 `supersedes_id` / `supersedes_revision_id`（nullable）のいずれか、テーブルの性質に応じて両方を持つ。
- `status` 列でその行が現在有効か（`Active` / `Effective` 等）、超過されたか（`Superseded`）、取り消されたか（`Voided` / `Corrected`）を区別する。具体的な語彙はテーブルごとに定義する。
- 「どの改訂を分析・判定に使用したか」は使用側（`analysis_input_manifests` 等）が改訂の主キーを直接参照することで追跡可能にする。「最新版」を暗黙に選ぶクエリに依存しない。

### Data types
- 金額・数量・比率・価格等の decimal 値は SQLite `REAL`/`FLOAT` を使わず、EF Core の decimal→文字列 value converter を介して `TEXT`（10 進表記の文字列）として保存する。中間丸めを避けるためであり、技術指標・損益計算の丸めルール（[`technical-analysis.md`](./technical-analysis.md)、[`risk-management.md`](./risk-management.md)）と整合させる。
- 真偽値は `INTEGER`（0/1）。
- 列挙的な値は数値コードではなく、各 docs で定義された語彙をそのまま `TEXT` として保存する（例: `Long` / `Short`、`Bullish` / `Neutral` / `Bearish`）。DB 側の `CHECK` 制約付与は任意（EF Core Migrations で追加可能な場合のみ）とし、正はアプリ（Domain）層の検証とする。
- 瞬間（日時+時刻）を表す列は `_at_utc` サフィックスを持ち、UTC の ISO-8601 文字列（例: `2026-08-29T01:05:00.0000000Z`）として保存する。
- 取引日・効力発生日等、時刻を持たない JST 暦日を表す列は `_date` サフィックスを持ち、`YYYY-MM-DD` の `TEXT` として保存する。
- ハッシュ値を表す列は `_sha256` サフィックスを持ち、64 文字小文字 16 進数の `TEXT` として保存する。
- 金額・価格には可能な限り `currency` 列を併記し、通貨を暗黙前提にしない。

### Deletion
削除より履歴・監査性を優先する（[`AGENTS.md`](../AGENTS.md)）。業務データの物理削除は行わず、`status` による無効化（`Voided` 等）で表現する。

---

## 1. Instrument master and margin regulation

対象市場範囲・取得元は [`data-sources.md`](./data-sources.md) の Market scope を参照。

### `instruments`
銘柄の不変な内部識別子。証券コードそのものは再割当てされうるため、内部 ID と分離する。

| 列 | 型 | 説明 |
|---|---|---|
| instrument_id | INTEGER PK | 内部識別子 |
| first_observed_at_utc | TEXT | 初めて銘柄マスタへ取り込んだ日時 |

### `instrument_master_revisions`
JPX 上場銘柄一覧の取り込みごとの改訂履歴。

| 列 | 型 | 説明 |
|---|---|---|
| instrument_master_revision_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| code | TEXT | 証券コード |
| name | TEXT | 銘柄名 |
| market_segment | TEXT | 市場区分 |
| instrument_type | TEXT | 例: `DomesticCommonStock`（将来 ETF 等拡張時に追加） |
| listed_status | TEXT | `Listed` / `Delisted` / `UnderReview` 等 |
| scan_eligibility | TEXT | `Eligible` / `NotEligible` / `Unknown`。対象市場フィルタ（設定値）との積集合判定に使用 |
| effective_at_date | TEXT | 一覧上の基準日 |
| available_at_utc | TEXT | このデータが分析時点で利用可能になった日時（取り込み完了日時） |
| source | TEXT | 例: `JPX-ListedIssues` |
| source_file_hash | TEXT | 取得元ファイルの内容ハッシュ（重複取り込み検知） |
| recorded_at_utc | TEXT | |
| revision | INTEGER | 銘柄ごとに 1 始まり連番 |
| supersedes_revision_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Active` / `Superseded` |

一意制約: `(instrument_id, revision)`。
全銘柄スキャンは、評価日に `effective_at_date` が有効かつ `available_at_utc <= AnalyzedAt` の最新改訂だけを使う（[`technical-analysis.md`](./technical-analysis.md) All-instrument scan contract）。`Unknown` を適格と推測しない。

### `margin_regulation_revisions`
信用取引対象区分・売建可否・規制情報（JPX 信用取引銘柄一覧・貸株銘柄一覧）。銘柄マスタとは別ソースのため分離する。

| 列 | 型 | 説明 |
|---|---|---|
| margin_regulation_revision_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| system_margin_eligible | TEXT | `Eligible` / `NotEligible` / `Unknown` |
| general_margin_eligible | TEXT | 同上 |
| short_sell_eligible | TEXT | 貸株銘柄一覧に基づく `Eligible` / `NotEligible` / `Unknown` |
| regulation_flags_json | TEXT NULL | 増担保規制等、構造化された追加フラグ |
| effective_at_date | TEXT | |
| available_at_utc | TEXT | |
| source | TEXT | 例: `JPX-MarginIssues`, `JPX-LendableStock` |
| recorded_at_utc | TEXT | |
| revision | INTEGER | |
| supersedes_revision_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Active` / `Superseded` |

一意制約: `(instrument_id, revision)`。
テクニカルな `Short Entry` 候補生成（`candidate_results`）はこのテーブルを参照しない。売建可否は候補一覧・保有画面で別情報として提示する（[`data-sources.md`](./data-sources.md)）。

---

## 2. Price data and corporate actions

### `daily_bars`
取得元の未調整 OHLCV。正本であり、訂正は新 revision を追加する。

| 列 | 型 | 説明 |
|---|---|---|
| daily_bar_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| trading_date | TEXT | JST 取引日 |
| open, high, low, close | TEXT(decimal) | 未調整値 |
| volume | INTEGER | |
| adj_close | TEXT(decimal) NULL | provider 提供の参考値。検算・障害調査専用。指標計算・シグナル判定へは使用しない |
| source | TEXT | 例: `YahooFinanceChartApiV8` |
| fetched_at_utc | TEXT | |
| revision | INTEGER | `(instrument_id, trading_date)` 内で 1 始まり連番 |
| supersedes_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Provisional` / `Final` / `Corrected` / `Voided` |

一意制約: `(instrument_id, trading_date, revision)`。
どの改訂を分析へ使用したかは `analysis_input_manifest_bars` が個別に参照する。EMA は保存済みの有限分析窓を起点とし、その開始日もmanifest hashへ含める。窓内に必要本数がない場合は `InsufficientHistory` として `indicator_results` 側で扱う。

### `daily_bar_history_coverages`
取得元応答の観測範囲を、銘柄・取得元ごとに append-only で記録する。`full_history_confirmed` は上場来履歴を要求した場合の監査情報であり、日次スキャンの実行可否には使わない。

主な列: `instrument_id`, `source`, `earliest_returned_date`, `latest_returned_date`, `full_history_confirmed`, `observed_at_utc`, `revision`, `supersedes_id`, `status` (`Complete` / `Incomplete`)。

一意制約: `(instrument_id, source, revision)`。`supersedes_id` は非NULL時に一意とし、revision chain の分岐を防ぐ。

### `corporate_actions`
分割・併合・現金配当。日足とは別に版管理する。

| 列 | 型 | 説明 |
|---|---|---|
| corporate_action_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| action_type | TEXT | `Split` / `Consolidation` / `CashDividend` / `Unsupported`（端株・現金交付・合併・スピンオフ等） |
| effective_date | TEXT | 効力発生日 |
| announced_at_utc | TEXT | |
| available_at_utc | TEXT | 分析時点で利用可能になった日時 |
| first_observed_at_utc | TEXT | |
| split_ratio_numerator | INTEGER NULL | Split/Consolidation の新株数 |
| split_ratio_denominator | INTEGER NULL | Split/Consolidation の旧株数 |
| dividend_amount_per_share | TEXT(decimal) NULL | CashDividend の 1 株配当額 |
| currency | TEXT NULL | |
| source_event_id | TEXT | 取得元イベント ID |
| source | TEXT | |
| recorded_at_utc | TEXT | |
| revision | INTEGER | `(instrument_id, source_event_id)` 内で 1 始まり連番 |
| supersedes_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Active` / `Superseded` / `Voided` / `ReconciliationRequired` |

一意制約: `(instrument_id, source_event_id, revision)`。
分析系列には `effective_date <= EvaluationBarDate` かつ `available_at_utc <= AnalyzedAt` を満たす改訂だけを適用する（[`data-sources.md`](./data-sources.md)、[`technical-analysis.md`](./technical-analysis.md) Look-ahead bias）。`available_at_utc` を復元できない履歴データは `PointInTimeUnverified` として `indicator_results`／`analysis_input_manifests` 側で区別する。`action_type = Unsupported` は常に `ReconciliationRequired` とし、推測調整しない。

### `fundamental_data_snapshots`
PER/PBR/時価総額等、構造化ファンダメンタルデータのキャッシュ。決算詳細・ニュース等の非構造化情報は保存せず AI チェックへ委ねる。

| 列 | 型 | 説明 |
|---|---|---|
| fundamental_snapshot_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| fetched_at_utc | TEXT | |
| source | TEXT | |
| per, pbr | TEXT(decimal) NULL | 欠損許容 |
| market_cap | TEXT(decimal) NULL | |
| dividend_yield | TEXT(decimal) NULL | |
| additional_metrics_json | TEXT NULL | 将来項目追加用 |

取得不能項目は NULL とし、0 やダミー値へ推測変換しない。取得不能は AI チェック結果自体を無効にしない。

---

## 3. Analysis input manifest and strategy parameters

分析の再現性を担保する土台。指標計算・候補抽出・risk basis のすべてがこの層を参照する。

### `analysis_input_manifests`
特定銘柄・特定評価日・特定分析実行時点で、実際に使用した日足改訂集合と企業アクション改訂集合を凍結する。

| 列 | 型 | 説明 |
|---|---|---|
| manifest_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| evaluation_bar_date | TEXT | `D` |
| analyzed_at_utc | TEXT | `A` |
| first_bar_date | TEXT | |
| last_bar_date | TEXT | |
| bar_count | INTEGER | |
| price_revision_set_hash | TEXT(sha256) | 使用した `daily_bars` 改訂集合のハッシュ |
| corporate_action_set_hash | TEXT(sha256) | 使用した `corporate_actions` 改訂集合のハッシュ |
| manifest_hash | TEXT(sha256) | 上記を含む manifest 全体のハッシュ |
| created_at_utc | TEXT | |

一意制約: `(instrument_id, evaluation_bar_date, analyzed_at_utc, manifest_hash)`。

### `analysis_input_manifest_bars`
manifest が実際に使用した日足の改訂を、取引日ごとに 1 件ずつ列挙する。

| 列 | 型 | 説明 |
|---|---|---|
| manifest_id | INTEGER FK → analysis_input_manifests | |
| trading_date | TEXT | |
| daily_bar_id | INTEGER FK → daily_bars | 使用した特定改訂 |

主キー: `(manifest_id, trading_date)`。
指標エンジンは、この一覧と件数・先頭日・末尾日・最低必要本数が一致しない入力を `InvalidData` として拒否する（[`technical-analysis.md`](./technical-analysis.md)）。

### `analysis_input_manifest_corporate_actions`
manifest が適用した企業アクションの改訂を列挙する。

| 列 | 型 | 説明 |
|---|---|---|
| manifest_id | INTEGER FK → analysis_input_manifests | |
| corporate_action_id | INTEGER FK → corporate_actions | 使用した特定改訂 |

主キー: `(manifest_id, corporate_action_id)`。

### `strategy_parameter_snapshots`
戦略パラメータの正は設定（Configuration）だが、判定実行のたびに解決済みの完全な正規化 JSON を不変スナップショットとして凍結する。

| 列 | 型 | 説明 |
|---|---|---|
| strategy_parameter_snapshot_id | INTEGER PK | |
| strategy_key | TEXT | |
| strategy_version | TEXT | |
| indicator_engine_version | TEXT | 例: `ema-sma-seed-v1` 等の集合を表す識別子群を含む |
| candidate_scoring_engine_version | TEXT | 例: `candidate-scoring-engine-v1` |
| normalized_parameters_json | TEXT | デフォルトと上書きを解決済みの完全な正規化 JSON |
| content_sha256 | TEXT(sha256) | |
| created_at_utc | TEXT | |

一意制約: `content_sha256`（同一パラメータ集合は同一 snapshot を再利用する）。
過去判定は現在の設定を参照し直さず、常にこのスナップショットを介する。

---

## 4. Daily update, scan and technical analysis results

日次更新フロー全体は [`product-spec.md`](./product-spec.md) を参照。

### `daily_update_runs`
日次更新（更新〜候補抽出〜保有再評価〜AIキュー投入まで）1 回分の実行記録。

| 列 | 型 | 説明 |
|---|---|---|
| daily_update_run_id | INTEGER PK | |
| started_at_utc | TEXT | |
| completed_at_utc | TEXT NULL | |
| status | TEXT | `Succeeded` / `PartiallySucceeded` / `Failed` |
| step_summary_json | TEXT NULL | 各ステップの成功/失敗件数等 |

### `external_fetch_results`
株価・企業アクション・銘柄マスタ・信用規制・逆日歩等、外部データ取得 1 件ごとの成否記録。個別テーブルへ分割せず種別列で表す。

| 列 | 型 | 説明 |
|---|---|---|
| fetch_result_id | INTEGER PK | |
| daily_update_run_id | INTEGER NULL FK → daily_update_runs | 手動個別取得の場合は NULL |
| daily_update_fetch_checkpoint_id | INTEGER NULL FK → daily_update_fetch_checkpoints | 再開判定に使った取得チェックポイント。既存の監査行は NULL を許容 |
| source_kind | TEXT | `PriceData` / `CorporateAction` / `InstrumentMaster` / `MarginRegulation` / `Backwardation` / `FundamentalData` |
| instrument_id | INTEGER NULL FK → instruments | 一覧系取得（銘柄マスタ等）は NULL |
| status | TEXT | `Succeeded` / `Failed` |
| error_kind | TEXT NULL | `HttpError` / `RateLimit` / `Timeout` / `InvalidData` / `Unknown` |
| error_message | TEXT NULL | |
| record_count | INTEGER NULL | |
| attempted_at_utc | TEXT | |

1 銘柄・1 情報源の失敗で全体を停止しない設計に対応する（[`product-spec.md`](./product-spec.md)）。

### `daily_update_fetch_checkpoints`
同じ評価日の更新を中断後に再開するための、取得種別・銘柄ごとの耐久チェックポイント。`external_fetch_results` は各通信試行の監査原票、こちらは再利用可否を示す状態であり、役割を混同しない。

主な列: `daily_update_run_id`, `evaluation_bar_date`, `source_kind`, `target_key`（一覧系は `global`、銘柄系は内部ID文字列）、`instrument_id` NULL可、`requested_range_start_date`, `covered_through_date`, `data_revision_fingerprint`, `status`（`Running` / `Succeeded` / `Failed` / `Interrupted`）、`invalidation_reason`, `started_at_utc`, `completed_at_utc`, `valid_until_utc`, `reused_from_checkpoint_id`。

再利用は、同一 `evaluation_bar_date`・種別・対象で、`Succeeded`、期限内、かつ現在の日足・企業アクション・履歴カバレッジ（またはJPX revision集合）のfingerprintが一致する場合だけ許可する。中断、失敗、期限切れ、fingerprint不一致は再取得する。Yahoo chartは日足と企業アクションを同一取得単位として扱い、成功チェックポイントは両者と履歴カバレッジの保存後にだけ確定する。検索インデックスは `(evaluation_bar_date, source_kind, target_key, completed_at_utc)`。

### `scan_runs`
全銘柄スキャン（指標計算・候補評価）1 回分の実行記録。

| 列 | 型 | 説明 |
|---|---|---|
| scan_run_id | INTEGER PK | |
| daily_update_run_id | INTEGER NULL FK → daily_update_runs | |
| run_type | TEXT | `DailyUpdate` / `Manual` |
| universe_definition_hash | TEXT | 対象市場フィルタ設定のハッシュ |
| started_at_utc | TEXT | |
| completed_at_utc | TEXT NULL | |
| status | TEXT | `Succeeded` / `PartiallySucceeded` / `Failed` |
| total_instruments | INTEGER | |
| succeeded_count | INTEGER | |
| failed_count | INTEGER | |

### `indicator_results`
銘柄・評価日単位の指標計算結果。Long/Short 双方の候補評価が同じ結果を共有する（[`technical-analysis.md`](./technical-analysis.md) All-instrument scan contract）。

| 列 | 型 | 説明 |
|---|---|---|
| indicator_result_id | INTEGER PK | |
| scan_run_id | INTEGER FK → scan_runs | |
| instrument_id | INTEGER FK → instruments | |
| evaluation_bar_date | TEXT | |
| analyzed_at_utc | TEXT | |
| manifest_id | INTEGER FK → analysis_input_manifests | |
| strategy_parameter_snapshot_id | INTEGER FK → strategy_parameter_snapshots | |
| data_status | TEXT | `Ok` / `InsufficientHistory` / `HistoryIncomplete` / `InvalidData` / `PointInTimeUnverified` / `ReconciliationRequired` |
| history_available_count | INTEGER | |
| history_required_count | INTEGER | 初期値 201 |
| macd_line, macd_signal, macd_histogram | TEXT(decimal) NULL | `data_status <> Ok` の場合 NULL |
| ema20, ema50, ema200 | TEXT(decimal) NULL | |
| atr14 | TEXT(decimal) NULL | |
| volume_ratio | TEXT(decimal) NULL | |
| volume_reference_average | TEXT(decimal) NULL | |
| volume_ratio_status | TEXT | `Ok` / `ReferenceAverageZero` |
| raw_values_json | TEXT | 前日値・中間値等、監査用の詳細（decimal は文字列のまま格納） |
| created_at_utc | TEXT | |

一意制約: `(instrument_id, evaluation_bar_date, analyzed_at_utc, strategy_parameter_snapshot_id)`。
必須指標を計算できない銘柄は `candidate_results` を生成しない。後日の価格・企業アクション訂正でも既存行は上書きせず、新しい入力manifestに対する結果を追加する。

### `scan_run_result_uses`
各スキャンが候補一覧へ採用した不変の `indicator_results` を記録する。入力manifestと戦略スナップショットが同一なら、再計算・候補再評価をせず既存の結果を `Reused` としてこの表へ紐付ける。これにより、結果の計算元と今回の候補一覧の所属を混同せず、監査性を保つ。

| 列 | 型 | 説明 |
|---|---|---|
| scan_run_result_use_id | INTEGER PK | |
| scan_run_id | INTEGER FK → scan_runs | 今回のスキャン |
| indicator_result_id | INTEGER FK → indicator_results | 計算済みまたは再利用した結果 |
| use_kind | TEXT | `Computed` / `Reused` |
| used_at_utc | TEXT | 今回のスキャンで採用した時刻 |

一意制約: `(scan_run_id, indicator_result_id)`。既存の `indicator_results` は移行時にすべて `Computed` としてバックフィルする。

### `scan_exclusions`
スキャン対象外となった銘柄の理由。

| 列 | 型 | 説明 |
|---|---|---|
| scan_exclusion_id | INTEGER PK | |
| scan_run_id | INTEGER FK → scan_runs | |
| instrument_id | INTEGER FK → instruments | |
| reason | TEXT | `InsufficientHistory` / `HistoryIncomplete` / `InvalidData` / `NotEligible` / `PointInTimeUnverified` / `ReconciliationRequired` |
| history_available_count | INTEGER NULL | |
| history_required_count | INTEGER NULL | |

一意制約: `(scan_run_id, instrument_id)`。

### `candidate_results`
Long/Short の Entry 候補。

| 列 | 型 | 説明 |
|---|---|---|
| candidate_result_id | INTEGER PK | |
| indicator_result_id | INTEGER FK → indicator_results | |
| instrument_id | INTEGER FK → instruments | 非正規化（一覧クエリ簡略化） |
| direction | TEXT | `Long` / `Short` |
| signal_purpose | TEXT | 現行は常に `Entry`（`SignalPurpose` は将来 `Exit` 拡張を見込み保持） |
| matched | INTEGER(bool) | 必須条件をすべて満たしたか |
| score | INTEGER NULL | 0–100。`matched = 0` の場合 NULL |
| confidence_label | TEXT NULL | `High` / `Medium` / `Low` |
| candidate_scoring_engine_version | TEXT | 例: `candidate-scoring-engine-v1` |
| score_components_json | TEXT | MACD/EMA/出来高の重み・生値・方向 gap・正規化強度・閾値・入力ハッシュを決定的な順序で保持 |
| created_at_utc | TEXT | |

一意制約: `(indicator_result_id, direction)`。
スコアは勝率・利益保証として表現しない（表示側の責務だが、`score_components_json` にも保証を示す文言を含めない）。

---

## 5. Positions, trade executions and margin lots

候補（`candidate_results`）と保有ポジションは別概念。候補一覧から渡してよいのは銘柄・方向等の入力補助までであり、価格・日時・株数は利用者が明示登録する（[`AGENTS.md`](../AGENTS.md) Non-negotiable rules）。

### `positions`

| 列 | 型 | 説明 |
|---|---|---|
| position_id | INTEGER PK | |
| instrument_id | INTEGER FK → instruments | |
| side | TEXT | `Long` / `Short` |
| status | TEXT | `Open` / `Closed` / `Archived` |
| applied_strategy_key | TEXT | |
| applied_strategy_version | TEXT | |
| memo | TEXT NULL | |
| source_candidate_result_id | INTEGER NULL FK → candidate_results | 候補一覧からの入力補助の由来（監査用。価格・日時を含まない） |
| opened_at_utc | TEXT | |
| closed_at_utc | TEXT NULL | |

### `trade_executions`
利用者の明示操作による約定履歴。監査原票として内容を変更しない。訂正は新しい行を追加する。

| 列 | 型 | 説明 |
|---|---|---|
| trade_execution_id | INTEGER PK | |
| position_id | INTEGER FK → positions | |
| execution_role | TEXT | `Open` / `Close` |
| executed_at | TEXT | 利用者が確認・入力した約定日時（JST） |
| price | TEXT(decimal) | |
| quantity | INTEGER | |
| currency | TEXT | |
| entered_at_utc | TEXT | 登録操作日時 |
| prefilled_from_candidate_result_id | INTEGER NULL FK → candidate_results | 候補一覧からの銘柄/方向入力補助の由来。価格・日時・株数の自動採用ではない |
| notes | TEXT NULL | |
| revision | INTEGER | 同一論理約定の訂正系列内で 1 始まり連番 |
| supersedes_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Effective` / `Corrected` / `Voided` |

現在値・終値を約定価格へ、サイン日時を約定日時へ自動採用しない。ボタン一回で約定登録まで完了する UI 経路を作らない（[`AGENTS.md`](../AGENTS.md)）。

### `margin_lots`
返済期限・契約条件は建玉（新規建約定）単位で異なるため、ポジションと分離して管理する。

| 列 | 型 | 説明 |
|---|---|---|
| margin_lot_id | INTEGER PK | |
| position_id | INTEGER FK → positions | |
| opening_trade_execution_id | INTEGER FK → trade_executions | `execution_role = Open` の特定改訂（unsuperseded） |
| opened_quantity | INTEGER | 元約定株数。不変 |
| current_quantity | TEXT(decimal) | 企業アクション換算・部分決済後の現在基準株数（端株許容） |
| status | TEXT | `Open` / `Closed` |

一意制約: `opening_trade_execution_id`。

### `margin_lot_contract_term_revisions`
制度/一般の区分、証券会社・商品、返済期限。証券会社で確認した都度、新しい revision を追加する。

| 列 | 型 | 説明 |
|---|---|---|
| contract_term_revision_id | INTEGER PK | |
| margin_lot_id | INTEGER FK → margin_lots | |
| margin_category | TEXT | `System` / `General`。同一 lot 内で保有途中に書き換えない（アプリ層で検証） |
| broker | TEXT | |
| product | TEXT | |
| term_type | TEXT | `FixedDate` / `NoFixedTerm` / `Unknown` |
| final_repayment_date | TEXT NULL | `term_type = FixedDate` の場合の証券会社確認済み最終返済可能日 |
| confirmed_at_utc | TEXT | 利用者が確認した日時 |
| evidence | TEXT NULL | 確認根拠のメモ |
| recorded_at_utc | TEXT | |
| revision | INTEGER | `margin_lot_id` 内で 1 始まり連番 |
| supersedes_revision_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Active` / `Superseded` |

一意制約: `(margin_lot_id, revision)`。
制度信用でも「約定日+6か月」を自動確定期限とせず、期限不明は `term_type = Unknown` のまま `ReconciliationRequired` 扱いの一因とする（[`product-spec.md`](./product-spec.md)、[`risk-management.md`](./risk-management.md)）。

### `trade_execution_lot_allocations`
部分決済がどの lot へ充当されたかの、利用者確認済み明示登録。FIFO 等での自動推測を行わない。

| 列 | 型 | 説明 |
|---|---|---|
| allocation_id | INTEGER PK | |
| trade_execution_id | INTEGER FK → trade_executions | `execution_role = Close` |
| margin_lot_id | INTEGER FK → margin_lots | |
| quantity | TEXT(decimal) | |
| effective_at_utc | TEXT | |
| recorded_at_utc | TEXT | |
| revision | INTEGER | |
| supersedes_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Effective` / `Superseded` / `Voided` |

一意制約: `(trade_execution_id, margin_lot_id, revision)`。

### `position_corporate_action_adjustments`
保有中に効力発生した企業アクションによる、現在基準への換算履歴。元約定（`trade_executions`）は変更しない。

| 列 | 型 | 説明 |
|---|---|---|
| adjustment_id | INTEGER PK | |
| margin_lot_id | INTEGER FK → margin_lots | |
| corporate_action_id | INTEGER FK → corporate_actions | |
| ratio | TEXT(decimal) NULL | 分割比率 `r`（Split/Consolidation のみ） |
| quantity_before, quantity_after | TEXT(decimal) NULL | |
| cost_basis_before, cost_basis_after | TEXT(decimal) NULL | 取得単価 |
| atr_basis_before, atr_basis_after | TEXT(decimal) NULL | 固定 ATR |
| stop_price_before, stop_price_after | TEXT(decimal) NULL | |
| take_profit_price_before, take_profit_price_after | TEXT(decimal) NULL | |
| reconciliation_status | TEXT | `Applied` / `ReconciliationRequired` |
| applied_at_utc | TEXT | |

一意制約: `(margin_lot_id, corporate_action_id)`。
現金配当（`action_type = CashDividend`）は株数・単価・ATR・ラインを変更しないため、`before = after` のまま `reconciliation_status = Applied` の行として権利落ち警告目的にのみ記録する。`action_type = Unsupported`（端株・現金交付・合併・スピンオフ等）は常に `reconciliation_status = ReconciliationRequired` とし、照合完了まで当該ポジションの自動再評価を停止する。

---

## 6. Risk basis and risk plans

損切/利確ルールの算出契約は [`risk-management.md`](./risk-management.md) を参照。

### `risk_basis_snapshots`
建玉時に凍結する基準価格・固定 ATR・単位ハッシュ。

| 列 | 型 | 説明 |
|---|---|---|
| risk_basis_id | INTEGER PK | |
| margin_lot_id | INTEGER FK → margin_lots | |
| entry_basis_price | TEXT(decimal) | |
| currency | TEXT | |
| atr_basis | TEXT(decimal) | |
| atr_reference_bar_date | TEXT | |
| atr_period | INTEGER | 初期値 14 |
| atr_algorithm_version | TEXT | Wilder 方式の識別子 |
| price_unit_basis_sha256 | TEXT(sha256) | schema version・instrument・currency・凍結済み corporate-action set hash から生成 |
| source_candidate_result_id | INTEGER NULL FK → candidate_results | 候補由来の新規建の場合 |
| source_indicator_result_id | INTEGER NULL FK → indicator_results | 候補由来の場合、評価日ATRの根拠 |
| manual_open_analysis_input_manifest_id | INTEGER NULL FK → analysis_input_manifests | 候補なし手動建玉の場合、直近確定ATRの根拠manifest |
| strategy_parameter_snapshot_id | INTEGER FK → strategy_parameter_snapshots | |
| corporate_action_set_hash | TEXT(sha256) | |
| content_sha256 | TEXT(sha256) | currency・単位ハッシュを含む |
| created_at_utc | TEXT | |

一意制約: `margin_lot_id`（1 lot につき初期 risk basis は 1 件。訂正が必要な場合もこの行は変更せず、新しい lot・新しい risk basis を作る運用とし、basis 自体の revision は持たない）。
entry price と ATR の currency または単位ハッシュが一致しない場合は生成しない（fail-closed）。

### `risk_plans`
損切/利確ラインの改訂履歴。

| 列 | 型 | 説明 |
|---|---|---|
| risk_plan_id | INTEGER PK | |
| margin_lot_id | INTEGER FK → margin_lots | |
| revision | INTEGER | `margin_lot_id` 内で 1 始まり連番 |
| plan_kind | TEXT | `Initial` / `PartialExitBreakeven` |
| risk_basis_id | INTEGER FK → risk_basis_snapshots | |
| stop_price | TEXT(decimal) | |
| take_profit_price | TEXT(decimal) | |
| partial_take_profit_fraction | TEXT(decimal) | 初期値 0.50 |
| trigger_trade_execution_id | INTEGER NULL FK → trade_executions | `PartialExitBreakeven` の発生根拠（Close 約定） |
| trigger_allocation_id | INTEGER NULL FK → trade_execution_lot_allocations | 同上、参照した allocation 改訂 |
| effective_at_utc | TEXT | |
| recorded_at_utc | TEXT | |
| supersedes_revision_id | INTEGER NULL, 自己FK | |
| status | TEXT | `Effective` / `Superseded` / `Voided` |

一意制約: `(margin_lot_id, revision)`。
`PartialExitBreakeven` は、Long なら `max(従来ライン, entry_basis_price)`、Short なら `min(従来ライン, entry_basis_price)` として、従来より不利な方向へ緩めない（[`risk-management.md`](./risk-management.md)）。旧 plan・約定・allocation・保有数量は上書きしない。

---

## 7. Holding risk evaluation

保有再評価の判定契約（`holding-risk-evaluation-v1`）は [`risk-management.md`](./risk-management.md) を参照。lot 単位で判定してから position 単位へ集約する。

### `lot_holding_evaluations`

| 列 | 型 | 説明 |
|---|---|---|
| lot_evaluation_id | INTEGER PK | |
| margin_lot_id | INTEGER FK → margin_lots | |
| position_id | INTEGER FK → positions | 非正規化 |
| evaluation_bar_date | TEXT | `D` |
| evaluated_at_utc | TEXT | |
| daily_bar_id | INTEGER FK → daily_bars | 評価に使用した `D` の確定/訂正済み日足の特定改訂 |
| risk_plan_revision_id | INTEGER FK → risk_plans | 評価に使用した単一 leaf |
| decision | TEXT NULL | `StopLoss` / `Exit` / `TakeProfit` / `Hold` |
| stop_reached_today | INTEGER(bool) | |
| target_reached_today | INTEGER(bool) | |
| prior_target_reach_state | TEXT | `NotReached` / `Reached` / `Indeterminate`（`D` より前の 1.5R 到達状態） |
| prior_target_first_reach_bar_date | TEXT NULL | `Reached` の場合の最初の到達日 |
| technical_reversal_state | TEXT | `Matched` / `NotMatched` / `Indeterminate` |
| macd_reversal_state | TEXT | `Matched` / `NotMatched` / `Missing` |
| ema20_reversal_state | TEXT | `Matched` / `NotMatched` / `Missing` |
| partial_exit_status | TEXT | `NotApplicable` / `Candidate` / `NotFeasible` |
| partial_exit_candidate_quantity | TEXT(decimal) NULL | |
| evaluation_outcome | TEXT | `Evaluated` / `InsufficientHistory` / `HistoryIncomplete` / `InvalidData` / `PointInTimeUnverified` / `ReconciliationRequired` / `IncompletePositionData` / `IntradaySequenceUnknown` / `Failed` |
| evaluation_evidence_json | TEXT | 比較した High/Low/Close、ライン価格、比較演算子、参照した価格・risk-plan revision ID 等の証跡 |
| diagnostics_json | TEXT NULL | `Failed` 時の sanitized 診断情報 |
| created_at_utc | TEXT | |

一意制約: `(margin_lot_id, evaluation_bar_date, evaluated_at_utc)`。
`decision` は優先順位表（`StopLoss > Exit > TakeProfit > Hold`）に基づき 1 lot につき 1 件確定する。`evaluation_outcome <> Evaluated` の場合 `decision` は NULL とし、既知 0 や直近成功結果へフォールバックしない。この評価は `trade_executions`、lot allocation、position 数量、risk-plan revision を生成・変更しない。

### `position_holding_evaluations`
Position 単位の集約結果。

| 列 | 型 | 説明 |
|---|---|---|
| position_evaluation_id | INTEGER PK | |
| position_id | INTEGER FK → positions | |
| evaluation_bar_date | TEXT | |
| evaluated_at_utc | TEXT | |
| aggregated_decision | TEXT NULL | `StopLoss` / `Exit` / `TakeProfit` / `Hold` |
| partial_exit_status | TEXT | `NotApplicable` / `Candidate` / `NotFeasible` |
| partial_exit_total_candidate_quantity | TEXT(decimal) NULL | `aggregated_decision = TakeProfit` の場合のみ、lot 別候補数量の合計 |
| evaluation_outcome | TEXT | `lot_holding_evaluations.evaluation_outcome` と同じ語彙 |
| lot_evaluations_json | TEXT | 対象 lot と各判定を lot ID 昇順で列挙した参照情報（`lot_evaluation_id` を含む） |
| created_at_utc | TEXT | |

一意制約: `(position_id, evaluation_bar_date, evaluated_at_utc)`。
1 lot でも `evaluation_outcome <> Evaluated` なら position 全体を判定不能とし、評価できた lot の暫定結果を `Hold` や売買候補へ昇格させない。

---

## 8. Margin carrying costs

対象期間・逆日歩等の取得元は [`data-sources.md`](./data-sources.md) を参照。

### `margin_cost_ledger_entries`
買方金利、貸株料、逆日歩、配当金相当額、証券会社固有コストの追記型台帳。

| 列 | 型 | 説明 |
|---|---|---|
| ledger_entry_id | INTEGER PK | |
| margin_lot_id | INTEGER FK → margin_lots | |
| cost_type | TEXT | `Interest` / `LoanFee` / `Backwardation` / `DividendEquivalent` / `BrokerSpecific` / `Other` |
| direction | TEXT | `Charge` / `Credit` |
| status | TEXT | `Estimate` / `Confirmed` / `Corrected` / `Unpublished` / `NotOccurred` / `KnownZero` / `Unknown` |
| period_start, period_end | TEXT NULL | |
| quantity | TEXT(decimal) NULL | |
| amount | TEXT(decimal) NULL | `status` が `Unpublished` / `Unknown` 等の場合は NULL のまま。0 円と推測しない |
| currency | TEXT | |
| rate | TEXT(decimal) NULL | |
| rate_unit | TEXT NULL | |
| day_count_convention | TEXT NULL | |
| source | TEXT | |
| available_at_utc | TEXT | |
| observed_at_utc | TEXT NULL | 証券会社明細での確定観測日時 |
| recorded_at_utc | TEXT | |
| revision | INTEGER | 同一 `(margin_lot_id, cost_type, period_start, period_end)` 内で 1 始まり連番 |
| supersedes_id | INTEGER NULL, 自己FK | |

一意制約: `(margin_lot_id, cost_type, period_start, period_end, revision)`。
証券会社明細の `Confirmed`/`Corrected` を正とし、アプリ計算の `Estimate` と明確に区別する。一般信用へ制度信用の逆日歩を自動適用しない。`KnownZero` / `NotOccurred` / `NotApplicable` は「欠損」とは別の解決済み 0 効果として区別する。

返済期限接近の警告閾値（初期値 30/10/5/1 営業日前）は Configuration 側の設定値であり、本テーブルには持たない。

---

## 9. AI check

Codex CLI による AI チェックの構造化結果 schema v1 は [`ai-analysis.md`](./ai-analysis.md) を正とする。

### `ai_check_attempts`

| 列 | 型 | 説明 |
|---|---|---|
| attempt_id | INTEGER PK | |
| candidate_result_id | INTEGER FK → candidate_results | 初期版の対象は候補（Entry）のみ |
| triggering_daily_update_run_id | INTEGER NULL FK → daily_update_runs | 自動投入の場合の起動元 |
| requested_by | TEXT | `User` / `Auto` |
| requested_at_utc | TEXT | |
| started_at_utc | TEXT NULL | |
| completed_at_utc | TEXT NULL | |
| status | TEXT | `Queued` / `Running` / `Succeeded` / `Failed` / `TimedOut` / `InsufficientInformation` / `Cancelled` |
| evaluation_bar_date | TEXT | |
| normalized_input_snapshot_hash | TEXT(sha256) | |
| technical_input_manifest_id | INTEGER FK → analysis_input_manifests | |
| strategy_snapshot_hash | TEXT(sha256) | `strategy_parameter_snapshots.content_sha256` の非正規化保持 |
| prompt_template_version | TEXT | |
| prompt_template_hash | TEXT(sha256) | |
| cli_executable_path | TEXT | |
| cli_version | TEXT NULL | |
| model | TEXT NULL | |
| timeout_seconds | INTEGER | |
| sanitized_arguments | TEXT NULL | 秘密情報を除いた実行引数 |
| exit_code | INTEGER NULL | |
| error_kind | TEXT NULL | |
| sanitized_stderr | TEXT NULL | |
| raw_response_hash | TEXT(sha256) NULL | CLI 生 stdout のハッシュ（診断用。構造化結果ハッシュとは別管理） |
| structured_result_sha256 | TEXT(sha256) NULL | 正規順で UTF-8 JSON 化した構造化結果のハッシュ |
| is_stale | INTEGER(bool) | 新しい analysis run 後の旧結果フラグ |

`status IN ('Queued','Running')` の間は同一 `(candidate_result_id, normalized_input_snapshot_hash, model)` の重複投入をアプリ層で防止する（終端状態後の再試行は新しい attempt として許可）。再試行・再チェックは過去 attempt を書き換えず、新しい行を追加する。アプリ終了時に `Running` だった attempt は `Failed`（`error_kind = Interrupted`）に更新し、`Queued` はそのまま次回再開可能な状態で残す。

### `ai_check_results`
`schemaVersion` は `ai-result-v1` の完全一致だけを受理する。

| 列 | 型 | 説明 |
|---|---|---|
| result_id | INTEGER PK | |
| attempt_id | INTEGER FK → ai_check_attempts | |
| schema_version | TEXT | 固定値 `ai-result-v1` |
| outcome | TEXT | `Succeeded` / `InsufficientInformation` |
| verdict | TEXT NULL | `Bullish` / `Neutral` / `Bearish`。`outcome = Succeeded` 時のみ必須 |
| confidence | TEXT NULL | `High` / `Medium` / `Low`。`outcome = Succeeded` 時のみ必須 |
| summary | TEXT | 非空 |
| technical_view | TEXT NULL | |
| fundamental_view | TEXT NULL | |
| checked_at_utc | TEXT | |

一意制約: `attempt_id`。
`outcome = InsufficientInformation` の場合 `verdict`/`confidence`/両 view は NULL とし、`Neutral` へ変換しない。

### `ai_check_evidence_items`
`positiveFactors` / `riskFactors` / `invalidationConditions` の各配列要素。AI が返した順序のまま保存する。

| 列 | 型 | 説明 |
|---|---|---|
| evidence_item_id | INTEGER PK | |
| result_id | INTEGER FK → ai_check_results | |
| evidence_kind | TEXT | `Positive` / `Risk` / `Invalidation` |
| ordinal | INTEGER | 配列内 0 始まり順序 |
| text | TEXT | |

一意制約: `(result_id, evidence_kind, ordinal)`。上限 10 件/kind はアプリ層で検証。

### `ai_check_sources`
`sources` 配列。配列順が citation 順。

| 列 | 型 | 説明 |
|---|---|---|
| source_id | INTEGER PK | |
| result_id | INTEGER FK → ai_check_results | |
| ordinal | INTEGER | 0 始まり、payload 配列順 |
| url | TEXT | HTTP(S) absolute URL（userinfo なし） |
| title | TEXT NULL | |
| published_at_utc | TEXT NULL | |
| retrieved_at_utc | TEXT | |

一意制約: `(result_id, ordinal)`。`published_at_utc <= retrieved_at_utc <= ai_check_results.checked_at_utc` を要求する。同一 URL は異なる公開時刻の版を除き重複させない。上限 20 件/result はアプリ層で検証。

### `ai_check_evidence_citations`
`EvidenceItem.sourceOrdinals` の展開。

| 列 | 型 | 説明 |
|---|---|---|
| evidence_item_id | INTEGER FK → ai_check_evidence_items | |
| source_ordinal | INTEGER | 同一 `result_id` 内の `ai_check_sources.ordinal` を指す |

主キー: `(evidence_item_id, source_ordinal)`。citation なしの場合は行を持たない（空配列）。

---

## 10. Indexing notes

必須ではないが、想定される主要クエリに対して以下のインデックスを検討する。

- `candidate_results`: `(evaluation_bar_date, direction, score DESC, instrument_id ASC)` — 候補一覧の既定ソート順（[`technical-analysis.md`](./technical-analysis.md) の同点時銘柄コード昇順に対応）。
- `positions`: `(status)` — 保有一覧表示。
- `margin_lot_contract_term_revisions`: `(final_repayment_date)` WHERE `status = 'Active'` — 期限接近警告の集計。
- `ai_check_attempts`: `(status)` — 永続キューの取り出し（`Queued` の draining）。
- `daily_bars`: `(instrument_id, trading_date)` — 既定の一意制約に含まれるため追加インデックス不要な場合が多い。

---

## Currently undecided

以下はスキーマとして仮決定であり、確定仕様として扱わない。

- JPX/Yahoo 等の銘柄コード正規化・コード再利用ルール（`instruments`/`instrument_master_revisions` の将来的な列追加要否を含む）。
- バックテスト専用の実行・結果テーブル（現状は `scan_runs`/`indicator_results`/`analysis_input_manifests` の再利用を想定するが、専用テーブルが必要になる可能性がある）。
- AI 結果 semantic schema の将来バージョン（v2 以降）に伴う `ai_check_results` 系テーブルの拡張方法。
- `margin_regulation_revisions.regulation_flags_json` の具体的な内部構造。

既存コード/docs/issue に根拠がなければ、この節に列挙した項目は変更容易な形で仮実装し、仮定を作業結果に明記する。
